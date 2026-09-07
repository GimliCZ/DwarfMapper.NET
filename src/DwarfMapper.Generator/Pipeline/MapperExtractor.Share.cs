// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     What <see cref="TryPlanShare" /> decided for one member: share it, or copy it as before.
        /// </summary>
        /// <param name="EmptyFallback">
        ///     The allocation-free empty value the share's guard falls back to; see
        ///     <see cref="Model.MemberMap.ShareEmptyFallback" />.
        /// </param>
        /// <param name="GuardsDefault">
        ///     Whether the guard is written as <c>.IsDefault ? … : …</c> rather than <c>?? …</c>.
        /// </param>
        private readonly record struct SharePlan(string EmptyFallback, bool GuardsDefault);

        /// <summary>
        ///     Decides whether a member may be assigned by REFERENCE instead of routed through the collection or
        ///     dictionary helper that copies it.
        ///     <para>
        ///         <b>The proof is the whole feature.</b> Sharing a mutable object couples two graphs the consumer
        ///         believes are independent, and nothing reports that afterwards — the failure class the DWARF103
        ///         code fix exists to refuse. So the automatic path shares only what
        ///         <see cref="ImmutabilityProof" /> answers <see cref="ImmutabilityVerdict.Proven" /> for, and an
        ///         explicit <c>[MapShare]</c> extends that to
        ///         <see cref="ImmutabilityVerdict.Unprovable" /> — the caller's assertion about a shape the proof
        ///         cannot see through, exactly as <c>[Reinterpret]</c> asserts a layout the blit proof declines —
        ///         while <see cref="ImmutabilityVerdict.Mutable" /> is refused with <c>DWARF104</c> in both modes,
        ///         because no assertion can make a settable member unsettable.
        ///     </para>
        ///     <para>
        ///         <b>Why the element pair needs no separate customization gate here, unlike the blit.</b> The blit
        ///         at chain position 2 had to ask (round 29 T0.2c) because it replaced an element LOOP that would
        ///         have called a user conversion or applied a pair-scoped directive. This cannot: it fires only
        ///         when the two member types are IDENTICAL, which makes the element pair identical too, and an
        ///         identical element pair resolves through <c>HasImplicitConversion</c> to a direct assignment
        ///         before <c>FindUserDeclaredConversion</c> is ever consulted. The helper being replaced is
        ///         therefore already a bulk copy with no per-element mapping in it —
        ///         <c>new List&lt;Badge&gt;(src)</c>, <c>ImmutableList.CreateRange(src)</c> — so there is nothing
        ///         for the share to bypass. Stated rather than assumed, because "a fast path that silently skipped
        ///         a declared conversion" is this round's most expensive finding.
        ///     </para>
        /// </summary>
        /// <param name="srcType">The source member's type.</param>
        /// <param name="tgtType">The destination member's type.</param>
        /// <param name="nullAsNull">The mapper's <c>NullCollectionStrategy.AsNull</c> setting.</param>
        /// <param name="forced">True when the caller wrote <c>[MapShare]</c> for this member.</param>
        /// <param name="location">Where to report a refusal.</param>
        /// <param name="targetName">The destination member's name, for the refusal message.</param>
        /// <param name="diagnostics">Refusal sink. Written only when <paramref name="forced" />.</param>
        /// <param name="plan">The share, when one was decided.</param>
        /// <returns>True when the member should be shared and the conversion resolver skipped entirely.</returns>
        private static bool TryPlanShare(
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            bool nullAsNull,
            bool forced,
            LocationInfo? location,
            string targetName,
            List<DiagnosticInfo> diagnostics,
            out SharePlan plan)
        {
            plan = default;

            // Same type on both sides is not a convenience — it is what makes "assign the reference" a MAPPING
            // rather than a conversion. SymbolEqualityComparer.Default ignores nullable ANNOTATIONS, which is
            // right: `List<Badge>?` and `List<Badge>` are the same storage, and the guard below answers the null.
            if (!SymbolEqualityComparer.Default.Equals(srcType, tgtType))
            {
                if (forced)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ShareInvalid,
                        location,
                        $"[MapShare] member '{targetName}' maps '{srcType.ToDisplayString()}' to " +
                        $"'{tgtType.ToDisplayString()}'; a reference can only be shared when the two sides are the " +
                        "same type, because sharing performs no conversion at all",
                        MemberName: targetName));
                }

                return false;
            }

            var verdict = ImmutabilityProof.Classify(tgtType, out var reason);
            if (verdict == ImmutabilityVerdict.Mutable)
            {
                // Refused in BOTH modes. The automatic path is silent about it (a refusal costs a copy, which is
                // what every member got yesterday); the explicit one is not, because the caller asked.
                if (forced)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ShareInvalid,
                        location,
                        $"[MapShare] member '{targetName}' is not immutable: sharing would alias mutable state — " +
                        reason,
                        MemberName: targetName));
                }

                return false;
            }

            if (!forced && verdict != ImmutabilityVerdict.Proven)
            {
                return false;
            }

            // AsNull hands a null source through to the destination; the share's guard turns it into an empty
            // collection. Those are different answers, so the share stands aside wherever the option is in
            // force — checked ONCE, ahead of the shape analysis, and not per shape. An earlier draft asked
            // `shape.NullAsNull` off the COLLECTION resolution only, which left a same-type
            // ImmutableDictionary sharing under AsNull while the dictionary helper it replaced returned null:
            // a divergence visible only to a caller who had opted into the option, which is the worst place
            // for one. Refusing here costs a copy on a path that asked for different null semantics anyway.
            if (nullAsNull)
            {
                if (forced)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ShareInvalid,
                        location,
                        $"[MapShare] member '{targetName}' is mapped under NullCollectionStrategy.AsNull, whose " +
                        "null-preserving contract the share does not implement — the share answers a null source " +
                        "with an empty collection; remove [MapShare] or map this member under the default AsEmpty",
                        MemberName: targetName));
                }

                return false;
            }

            // Not a collection or dictionary member: the resolver already assigns it directly (an identity
            // conversion is implicit), so there is no helper to remove and no guard to write. A [MapShare] here
            // is honoured by the code that was already going to be emitted, which is why it is not a diagnostic.
            if (!CollectionConverter.TryResolve(srcType, tgtType, out _, out _, out _, nullAsNull) &&
                !DictionaryConverter.TryResolve(srcType, tgtType, out _, out _, out _, out _, out _))
            {
                return false;
            }

            var empty = ImmutabilityProof.TryEmptyExpression(tgtType);
            if (empty is null)
            {
                if (forced)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ShareInvalid,
                        location,
                        $"[MapShare] member '{targetName}' has no allocation-free empty value ('{tgtType.ToDisplayString()}' " +
                        "exposes no static Empty and is not a read-only sequence interface), so the share could not " +
                        "reproduce what the copying helper returns for a null source without allocating",
                        MemberName: targetName));
                }

                return false;
            }

            plan = new SharePlan(empty, ImmutabilityProof.GuardsOnDefault(tgtType));
            return true;
        }
    }
}
