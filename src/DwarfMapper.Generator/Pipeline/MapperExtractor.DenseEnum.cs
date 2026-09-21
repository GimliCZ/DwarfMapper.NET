// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     The prefix every helper <see cref="DenseEnumProof.Synthesize" /> writes carries. Read back by
        ///     <see cref="ReportUnappliedDenseEnumDirectives" /> to tell "the directive fired" from "the directive
        ///     named a member resolution never reached".
        /// </summary>
        private const string DenseHelperPrefix = "__DwarfDense_";

        /// <summary>
        ///     Everything true of a <c>[MapDenseEnumKeys]</c> APPLICATION regardless of the member it names —
        ///     the checks that can be made from the attribute list and the destination type alone — and the
        ///     member→offset table the two resolution sites then read.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Kept beside its <c>[Reinterpret]</c> and <c>[MapShare]</c> twins rather than inside
        ///         <see cref="TryPlanDense" />, and for the same reason: <see cref="TryPlanDense" /> is only ever
        ///         called for a member that WAS matched, so a name matching nothing never reaches it. A directive
        ///         naming a member that does not exist is a typo, and a typo that maps the dictionary the ordinary
        ///         way is the "accepted it, changed nothing, said nothing" shape this round exists to remove.
        ///     </para>
        ///     <para>
        ///         Duplicate applications are refused rather than resolved. <c>[MapDenseEnumKeys("C", Offset = 1)]</c>
        ///         beside <c>[MapDenseEnumKeys("C", Offset = 2)]</c> is a contradiction with two different
        ///         emitted index computations behind it; picking one by declaration order would emit arithmetic
        ///         the consumer never chose, and which one they got would depend on the order Roslyn happened to
        ///         return the attributes in.
        ///     </para>
        /// </remarks>
        /// <param name="directives">Every application, in source order, from <c>ReadDenseEnumKeys</c>.</param>
        /// <param name="targetType">The destination type whose writable members the names are checked against.</param>
        /// <param name="compilation">The compilation, for the writable-member walk.</param>
        /// <param name="allowNonPublic">Whether non-public destination members count as writable.</param>
        /// <param name="ignores">The effective <c>[MapIgnore]</c> set.</param>
        /// <param name="mapValues">The <c>[MapValue]</c> directives, which claim a member outright.</param>
        /// <param name="location">Where every refusal is reported.</param>
        /// <param name="diagnostics">Refusal sink.</param>
        /// <returns>The surviving member→offset table, keyed ordinally.</returns>
        private static Dictionary<string, int> ValidateDenseEnumDirectives(
            List<(string Member, int Offset)> directives,
            INamedTypeSymbol targetType,
            Compilation compilation,
            bool allowNonPublic,
            HashSet<string> ignores,
            IReadOnlyList<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>?
                mapValues,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var byMember = new Dictionary<string, int>(StringComparer.Ordinal);
            if (directives.Count == 0)
            {
                return byMember;
            }

            var writableNames = new HashSet<string>(
                WritableMembers(targetType, compilation, allowNonPublic).Select(m => m.Name),
                StringComparer.Ordinal);

            foreach (var (member, offset) in directives)
            {
                if (byMember.ContainsKey(member))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DenseEnumInvalid,
                        location,
                        $"[MapDenseEnumKeys] names member '{member}' more than once; the two applications would " +
                        "index the same array differently and only one of them can be emitted — keep the one " +
                        "whose Offset you meant",
                        MemberName: member));
                    continue;
                }

                if (ignores.Contains(member))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, member));
                    continue;
                }

                if (mapValues is not null && mapValues.Any(v => StringComparer.Ordinal.Equals(v.Target, member)))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DenseEnumInvalid,
                        location,
                        $"[MapDenseEnumKeys] member '{member}' is also assigned by [MapValue], which supplies the " +
                        "value outright; there is no source dictionary left to index — remove one of them",
                        MemberName: member));
                    continue;
                }

                if (!writableNames.Contains(member))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DenseEnumInvalid,
                        location,
                        $"[MapDenseEnumKeys] member '{member}' does not name a writable destination member of " +
                        $"'{targetType.ToDisplayString()}'",
                        MemberName: member));
                    continue;
                }

                byMember[member] = offset;
            }

            return byMember;
        }

        /// <summary>
        ///     Decides whether one member takes the dense index, and synthesizes the helper that fills it.
        ///     <para>
        ///         <b>There is no automatic path, deliberately.</b> Unlike the share — where the generator can
        ///         prove immutability and act on it unasked — nothing here is decidable without the consumer: an
        ///         <c>[InlineArray(n)]</c> destination member is a DECLARATION they write, and whether they want
        ///         one is not the generator's judgement to make. So the directive is the trigger, and this
        ///         method's whole job is to prove the mapping into it is total and to refuse loudly when it is
        ///         not.
        ///     </para>
        ///     <para>
        ///         <b>Refused, never downgraded.</b> A member this refuses is skipped by the caller and the
        ///         <c>DWARF105</c> error stops the output — it does NOT fall through to an ordinary dictionary
        ///         copy. Falling through would leave a consumer believing a directive is in force that is not,
        ///         and it is the "bounds-checked fallback hiding an unprovable shape" this feature must never
        ///         emit.
        ///     </para>
        /// </summary>
        /// <param name="srcType">The source member's type.</param>
        /// <param name="tgtType">The destination member's type.</param>
        /// <param name="offset">The enum value that maps to slot 0.</param>
        /// <param name="synth">The mapper's synthesized-helper table.</param>
        /// <param name="location">Where a refusal is reported.</param>
        /// <param name="targetName">The destination member's name, for the refusal message.</param>
        /// <param name="diagnostics">Refusal sink.</param>
        /// <param name="converter">The synthesized helper's name, when the proof holds.</param>
        /// <returns>True when the dense fill was planned; false when it was refused (and reported).</returns>
        private static bool TryPlanDense(
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            int offset,
            Dictionary<string, SynthesizedMethod> synth,
            LocationInfo? location,
            string targetName,
            List<DiagnosticInfo> diagnostics,
            out string converter)
        {
            converter = "";

            if (!DenseEnumProof.TryProve(srcType, tgtType, offset, out var plan, out var reason))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DenseEnumInvalid,
                    location,
                    $"[MapDenseEnumKeys] member '{targetName}' " + reason,
                    MemberName: targetName));
                return false;
            }

            converter = DenseEnumProof.Synthesize(synth, srcType, tgtType, plan, offset);
            return true;
        }

        /// <summary>
        ///     Reports every <c>[MapDenseEnumKeys]</c> that named a real, writable member which resolution then
        ///     never reached — a directive that changed nothing and said nothing.
        /// </summary>
        /// <remarks>
        ///     The prologue catches a name that matches no member; <see cref="TryPlanDense" /> catches a member
        ///     whose shape cannot be proven. Between them sits a member that exists and is writable but that
        ///     neither resolution site ever offered the directive: one bound to a constructor parameter, one
        ///     claimed by an earlier pass, one flattened. In every such case the consumer wrote a directive that
        ///     is not in force, and the dictionary they believe is being indexed densely is not.
        /// </remarks>
        /// <param name="denseMembers">The validated member→offset table.</param>
        /// <param name="result">The resolved member maps.</param>
        /// <param name="location">Where a refusal is reported.</param>
        /// <param name="diagnostics">
        ///     Refusal sink, also READ: a member already refused by <see cref="TryPlanDense" /> carries a
        ///     <c>DWARF105</c> naming it, and reporting a second, vaguer one about the same member would make the
        ///     specific message harder to find rather than easier.
        /// </param>
        /// <param name="start">
        ///     Where THIS resolution's entries begin in <paramref name="diagnostics" />. The list belongs to the
        ///     mapper CLASS and every method appends to it, so scanning the whole thing would let a refusal
        ///     raised for one method silence this report for another method that names the same member — two
        ///     independent questions with one answer between them.
        /// </param>
        private static void ReportUnappliedDenseEnumDirectives(
            Dictionary<string, int> denseMembers,
            List<MemberMap> result,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            int start)
        {
            if (denseMembers.Count == 0)
            {
                return;
            }

            foreach (var member in denseMembers.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (result.Any(m => StringComparer.Ordinal.Equals(m.TargetName, member) &&
                                    m.ConverterMethod is { } c &&
                                    c.StartsWith(DenseHelperPrefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                var alreadyRefused = false;
                for (var i = start; i < diagnostics.Count; i++)
                    if (ReferenceEquals(diagnostics[i].Descriptor, DiagnosticDescriptors.DenseEnumInvalid) &&
                        StringComparer.Ordinal.Equals(diagnostics[i].MemberName, member))
                    {
                        alreadyRefused = true;
                        break;
                    }

                if (alreadyRefused)
                {
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DenseEnumInvalid,
                    location,
                    $"[MapDenseEnumKeys] member '{member}' was never reached by member resolution, so the " +
                    "directive is not in force: the member is bound elsewhere (a constructor parameter, an " +
                    "earlier directive, a flattened path) and is not being filled from an enum-keyed dictionary",
                    MemberName: member));
            }
        }
    }
}
