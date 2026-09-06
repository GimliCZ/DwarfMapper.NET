// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Runtime.CompilerServices;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The arms of <c>TryResolveConversion</c>'s dispatch chain.
    /// </summary>
    /// <remarks>
    ///     Each answers one question -- "is this pair mine?" -- and the first to claim it decides. The order of
    ///     the calls IS the resolution policy: several pairs match more than one arm, and the earlier arm is the
    ///     intended answer. Reordering these calls changes behaviour, which is why each summary says what it sits
    ///     before or after. See <c>Issues/round27/SEAM-STAGE.md</c> for how the spans were derived and certified.
    /// </remarks>
    internal static partial class MapperExtractor
    {

        /// <summary>
        ///     Auto-synthesized nested object mapper -- the last arm that can still resolve a pair, so everything
        ///     before it has already declined. Falling through from here reaches DWARF005.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when this arm CLAIMED the pair, in which case <paramref name="resolved" /> carries the
        ///     verdict it reached; <c>false</c> when the pair is not this arm's to answer and the chain continues.
        /// </returns>
        // ── Auto-synthesized nested object mapper ─────────────────────────────
        // Placed LAST before DWARF005: only fires when nothing else resolved the pair.
        // Gate: autoNest=true AND both types are mappable named object types.
        private static bool HandleAutoNestedObjectMap(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            ref string? converterMethod,
            out bool resolved)
        {
            resolved = false;

            if (req.AutoNest && req.NestedRegistry is not null && req.TgtType is INamedTypeSymbol namedTgt)
            {
                // AutoNestWouldClaim is this same condition, asked by the blit gate one arm earlier; the two
                // are one method so they cannot drift apart (round 29 T0.2c).
                if (AutoNestWouldClaim(req))
                {
                    // DWARF071: the source is a CONCRETE class that other types derive from. It maps fine, but only
                    // the declared members are mapped — a derived instance at run time loses everything declared
                    // below the base. DWARF033 catches the abstract/interface form of this; the concrete form is
                    // instantiable and slips past it. Reported (not refused) because base-only mapping is often
                    // exactly what was intended. Suppressed under req.AllowInterfaceSrc — a [MapDerivedType] arm has
                    // already told us how the runtime type is dispatched.
                    if (!req.AllowInterfaceSrc && HasDerivedTypesInCompilation(req.Compilation, req.SrcType))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.PolymorphicSourceMayDropMembers,
                            req.Location,
                            req.SrcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    }

                    // C1: pass the effective req.AutoNest value so the drain loop uses it for the pair's body.
                    var synthName = req.NestedRegistry.GetOrReserve(req.SrcType, namedTgt, req.Location, req.AutoNest);
                    if (synthName is not null)
                    {
                        converterMethod = synthName;
                        resolved = true;
                        return true;
                    }
                    // GetOrReserve returned null → cap exceeded; DWARF031 will be reported after drain.
                    // Fall through to DWARF005.
                }
                else if (!req.AllowInterfaceSrc && IsAbstractOrInterfaceAutoNestSource(req.Compilation, req.SrcType, namedTgt))
                {
                    // C2: abstract/interface source — emit DWARF033 (loud, never silent).
                    // Suppressed when req.AllowInterfaceSrc=true (e.g. [MapDerivedType] arms where the caller
                    // explicitly opted in to mapping an interface source to a concrete DTO).
                    var srcName = req.SrcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AbstractSourceAutoNest, req.Location, srcName));
                    resolved = false;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     A non-nullable source into a Nullable&lt;T&gt; target: resolve src to the underlying type and let C#'s
        ///     implicit T to T? lifting do the rest.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when this arm CLAIMED the pair, in which case <paramref name="resolved" /> carries the
        ///     verdict it reached; <c>false</c> when the pair is not this arm's to answer and the chain continues.
        /// </returns>
        // Target-nullable composition: non-Nullable<> src → T? (Nullable<> target).
        // The source is not a Nullable<T>, so resolve src→underlying and let the implicit T→T? lift do the
        // rest (valid C# assignment). A nullable-VALUE source is handled by the nullable-capable-target
        // branch above.
        //
        // The source may still be a possibly-null REFERENCE, and then the lift has to be explicit: the
        // converter is a synthesized nested mapper with a value-type return, which cannot answer null, so
        // it threw ("Cannot map a null 'S' to value-type 'D'.") on a null the Nullable<U> destination could
        // have held — I7's reverse genre, and the mirror of the kind-instead-of-capability confusion the
        // gate above had. NullableProjectRef makes the CALL SITE test for null first, which is the only
        // place that can: the helper's return type leaves it no way to express the answer.
        // TASKS.md I7 / round 23 N2.
        private static bool HandleTargetNullableComposition(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            Dictionary<string, SynthesizedMethod> synthesized,
            ref string? converterMethod,
            ref NullHandling nullHandling,
            out bool resolved)
        {
            resolved = false;

            if (!IsNullableValue(req.SrcType, out _) && IsNullableValue(req.TgtType, out var tgtUnderlying))
            {
                if (TryResolveConversion(req.Compilation,
                        req.SrcType,
                        tgtUnderlying,
                        req.UseMethod,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        synthesized,
                        req.NullStrategy,
                        req.Location,
                        req.TargetName,
                        diagnostics,
                        out var innerConvT,
                        out _,
                        out _,
                        req.AutoNest,
                        req.NestedRegistry,
                        req.NullAsNull,
                        implicitConversions: req.ImplicitConversions,
                        reservedConverters: req.ReservedConverters))
                {
                    converterMethod = innerConvT; // returns U; assigned to U? field via implicit U→U?
                    // A possibly-null reference source needs the explicit null test. A value-type source
                    // (non-Nullable<>) always yields a value, and a direct assignment (no converter) already
                    // lifts through the implicit U→U?, so both keep NullHandling.None.
                    if (innerConvT is not null && SourceMayBeNullRef(req.SrcType))
                    {
                        nullHandling = NullHandling.NullableProjectRef;
                    }

                    resolved = true;

                    return true;
                }

                // Did not resolve — fall through to DWARF005
                resolved = false;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     A Nullable&lt;T&gt; source into a target that cannot hold null -- the unwrapping arm, reached only after
        ///     the null-preserving arm above has declined.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when this arm CLAIMED the pair, in which case <paramref name="resolved" /> carries the
        ///     verdict it reached; <c>false</c> when the pair is not this arm's to answer and the chain continues.
        /// </returns>
        // Inner unresolved or has no converter (implicit, already caught above) — fall through.
        private static bool HandleNullableValueSource(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            Dictionary<string, SynthesizedMethod> synthesized,
            ref string? converterMethod,
            ref NullHandling nullHandling,
            out bool resolved)
        {
            resolved = false;

            if (IsNullableValue(req.SrcType, out var underlying))
            {
                // First check the simple implicit-conversion path (int? → int, int? → long, etc.)
                if (HasImplicitConversion(req.Compilation, underlying, req.TgtType))
                {
                    // Same question the direct-assign path at the top of this method asks, and this arm never
                    // asked it: `long? → double` unwraps to `long → double`, which IS implicit in C# and IS
                    // lossy, so it was assigned in silence at every severity. The DWARF038 names the UNWRAPPED
                    // pair, because that is the conversion being applied. (TASKS.md I20.)
                    if (NumericConverter.IsCrossCategoryLossy(underlying, req.TgtType))
                    {
                        EmitImplicitConversionDiag(diagnostics,
                            req.Location,
                            req.TargetName,
                            underlying,
                            req.TgtType,
                            "cross-category numeric",
                            req.ImplicitConversions,
                            true);
                    }

                    nullHandling = req.NullStrategy == NullStrategy.SetDefault
                        ? NullHandling.ValueOrDefault
                        : NullHandling.ThrowIfNull;
                    resolved = true;
                    return true;
                }

                // Recurse: try to resolve a conversion from the underlying (non-nullable) type to req.TgtType.
                // This handles cases like E1? → E2 where E1 → E2 requires a synthesized conversion.
                // Guard: 'underlying' is not itself nullable (Nullable<Nullable<T>> is illegal in C#).
                if (TryResolveConversion(req.Compilation,
                        underlying,
                        req.TgtType,
                        req.UseMethod,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        synthesized,
                        req.NullStrategy,
                        req.Location,
                        req.TargetName,
                        diagnostics,
                        out var innerConv,
                        out _,
                        out _,
                        req.AutoNest,
                        req.NestedRegistry,
                        req.NullAsNull,
                        implicitConversions: req.ImplicitConversions,
                        reservedConverters: req.ReservedConverters))
                {
                    nullHandling = req.NullStrategy == NullStrategy.SetDefault
                        ? NullHandling.ValueOrDefault
                        : NullHandling.ThrowIfNull;
                    converterMethod = innerConv; // may be null (direct assign after unwrap) or a synthesized method
                    resolved = true;
                    return true;
                }

                // Fall through — let the rest of TryResolveConversion attempt further resolutions.
            }

            return false;
        }

        /// <summary>
        ///     A nullable source into a target that CAN hold null, preserving null as null. Ordered before the
        ///     unwrapping arm on purpose: the same pair matches both, and this one is the correct answer.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when this arm CLAIMED the pair, in which case <paramref name="resolved" /> carries the
        ///     verdict it reached; <c>false</c> when the pair is not this arm's to answer and the chain continues.
        /// </returns>
        // Nullable-value source into a target that CAN HOLD NULL: T? → U? (both Nullable<>) or T? → U?
        // (a nullable-annotated reference target) with a non-implicit inner T→U. Null-preserving
        // (null → null). Must come before the source-nullable branch so that a nullable source with a
        // synthesized inner conversion resolves to NullableProject rather than ThrowIfNull/ValueOrDefault.
        //
        // The gate used to demand BOTH sides be Nullable<T>, which made the lift depend on the DESTINATION'S
        // KIND rather than on whether it can hold the null: `S1? → D1?` lifted when D1 was a struct and threw
        // ("Source member 'X' was null") when D1 was a class or a record — an inconsistency no user can predict
        // from the types, and undocumented (the NullStrategy contract governs nullable-value source →
        // NON-nullable target). TASKS.md I7 / round 23 N2. Nullable-capable is deliberately annotation-strict
        // for references: an unannotated or oblivious reference target keeps the documented throw.
        private static bool HandleNullableCapableTarget(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            Dictionary<string, SynthesizedMethod> synthesized,
            ref string? converterMethod,
            ref NullHandling nullHandling,
            out bool resolved)
        {
            resolved = false;

            if (IsNullableValue(req.SrcType, out var bothSrcU) && TryGetNullableCapableTarget(req.TgtType, out var bothTgtU))
                // req.ImplicitConversions is threaded, and it used to be dropped here. Every recursion that crosses
                // a Nullable<> wrapper defaulted the option back to `true` (permissive), so under
                // [DwarfMapper(ImplicitConversions = false)] a lossy conversion between two nullable members
                // reported DWARF038 as a WARNING and the mapper was still generated — the strict setting was
                // silently off for the whole nullable half of the type space. The collection-element and
                // dictionary key/value recursions above always passed it; these three did not. (TASKS.md I20.)
            {
                if (TryResolveConversion(req.Compilation,
                        bothSrcU,
                        bothTgtU,
                        req.UseMethod,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        synthesized,
                        req.NullStrategy,
                        req.Location,
                        req.TargetName,
                        diagnostics,
                        out var innerNonNull,
                        out _,
                        out _,
                        req.AutoNest,
                        req.NestedRegistry,
                        req.NullAsNull,
                        implicitConversions: req.ImplicitConversions,
                        reservedConverters: req.ReservedConverters) &&
                    innerNonNull is not null)
                {
                    converterMethod = innerNonNull;
                    nullHandling = NullHandling.NullableProject;
                    resolved = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     True when resolving the collection's ELEMENT pair would land on a conversion the USER wrote — a
        ///     declared method the auto-candidate arm adopts, or the user's own <c>implicit</c>/<c>explicit
        ///     operator</c>. Asked by <see cref="HandleCollectionConversion" /> before the blit proofs, which is
        ///     several arms before either of those arms actually runs.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Reads the ELEMENT pair through <c>req with</c>, so the two questions below are asked of exactly
        ///         the request the recursive <c>TryResolveConversion</c> further down will build: same methods,
        ///         same reservations, same auto-nest, same modes. <c>UseMethod</c> is cleared and
        ///         <c>AllowInterfaceSrc</c> reset to <see langword="false" /> because that recursion passes
        ///         neither — a <c>Use=</c> names the converter for the COLLECTION member, never for its elements.
        ///     </para>
        ///     <para>
        ///         The operator question is asked about PRECEDENCE, not existence: the user-operator arm is the
        ///         LAST in the chain, so an operator only becomes the resolver's answer when the auto-nest arm
        ///         declines the pair (e.g. under <c>[AutoNest(false)]</c>). With auto-nest on, the element pair
        ///         resolves to a synthesized <c>__DwarfMap_Obj_*</c> and the operator is not called on the scalar
        ///         path either — so the blit is not bypassing anything and stays. This mirrors the span gate,
        ///         which reaches the same conclusion from the other side by testing the verdict it already has
        ///         for <c>GeneratedNames.IsUserConv</c>.
        ///     </para>
        ///     <para>
        ///         A pair-scoped <c>[MapConstructor&lt;S,T&gt;]</c> is deliberately NOT this question's business,
        ///         and the first draft of this gate got that wrong. It looked covered here: the factory is
        ///         honoured only for a pair some <c>[GenerateMap&lt;S,T&gt;]</c> declares, and a declared pair
        ///         contributes a candidate method, so the search below finds it. But under Preserve and SetNull
        ///         <see cref="PrefersSynthesizedObjectMap" /> hands that candidate BACK — a public method cannot
        ///         accept the shared <c>DwarfRefContext</c> — so this method correctly answers "no user
        ///         conversion" while the synthesized helper the pair actually routes through is the very thing
        ///         carrying the factory. It is asked with the other pair-scoped directives instead, in
        ///         <c>ElementPairHasCustomization</c>, which is where "the helper for this pair is customized"
        ///         belongs. Pinned for all three reference modes. A pair-scoped
        ///         <c>[MapNullSkip&lt;S,T&gt;]</c> genuinely is byte-equivalent on a pair the blit proof accepted
        ///         — an unmanaged struct pair has no nullable member to skip — and is consulted by neither; also
        ///         pinned.
        ///     </para>
        /// </remarks>
        private static bool ElementPairResolvesToUserConversion(
            ConversionRequest req,
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem)
        {
            var elemReq = req with
            {
                SrcType = srcElem,
                TgtType = tgtElem,
                UseMethod = null,
                AllowInterfaceSrc = false
            };

            // Ambiguity counts as "the user wrote a conversion for this pair": the resolver refuses it with
            // DWARF013, and a block copy that quietly resolved the ambiguity by ignoring both candidates would
            // be the loudest possible bypass.
            FindUserDeclaredConversion(elemReq, out var found, out var ambiguous);
            if (ambiguous || (found is not null && !PrefersSynthesizedObjectMap(elemReq, found)))
            {
                return true;
            }

            return !AutoNestWouldClaim(elemReq) &&
                   UserConversionConverter.Exists(req.Compilation, srcElem, tgtElem);
        }

        /// <summary>
        ///     Reports <c>DWARF106</c> when <c>[Reinterpret]</c> on <paramref name="memberName" /> takes the block
        ///     copy in place of something the element pair would otherwise have been given: a conversion the user
        ///     wrote, or a pair-scoped directive / hook the element helper would have carried.
        /// </summary>
        /// <remarks>
        ///     Round 29 T0.2c review fix 3. Asks the SAME questions the array/list gate asks — through the same
        ///     <see cref="ElementPairResolvesToUserConversion" /> and the same
        ///     <c>NestedMappingRegistry.PairCustomization</c> the gate's <c>PairIsCustomized</c> is derived from
        ///     — so the diagnostic can never disagree with the gate about whether there was anything there to
        ///     bypass. It reports what the gate would have honoured and <c>[Reinterpret]</c> overrides; when the
        ///     gate finds nothing, there is no conflict and nothing is said. The element request is built with the
        ///     same values the member-level resolution below uses, for the same reason: the question must be the
        ///     one the resolver would have answered.
        ///     <para>
        ///         Round 29 T0.2d widened it. The original returned early unless a user CONVERSION resolved, so
        ///         <c>[Reinterpret]</c> overriding a pair-scoped <c>[MapIgnore&lt;T&gt;]</c> /
        ///         <c>[MapProperty&lt;S,T&gt;]</c> / <c>[MapValue&lt;T&gt;]</c> / <c>[MapConstructor&lt;S,T&gt;]</c>
        ///         or a <c>[BeforeMap]</c>/<c>[AfterMap]</c> hook was silent — the same intentional bypass, the
        ///         same invisible consequence. One id, two message shapes; the conversion is reported first when
        ///         both are present, because that is the arm the gate answers first.
        ///     </para>
        /// </remarks>
        private static void ReportReinterpretBypass(
            MemberRequest req,
            MemberLookups lookups,
            MemberAccumulators acc,
            string memberName,
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem)
        {
            var probe = new ConversionRequest(req.Compilation,
                srcElem,
                tgtElem,
                null,
                req.AllMethods,
                req.AutoCandidates,
                req.EnumPolicy,
                req.NullStrategy,
                req.Location,
                memberName,
                req.Options.AutoNest,
                req.NestedRegistry,
                req.Options.NullAsNull,
                req.Options.IsPreserve,
                false,
                req.Options.IsSetNull,
                req.Options.ImplicitConversions,
                lookups.ReservedConverters);

            string bypassed;
            string verb;
            if (ElementPairResolvesToUserConversion(probe, srcElem, tgtElem))
            {
                // Name the thing that is not being called. A declared method is named directly; an operator has
                // no name a user could grep for, so it is described by the pair it converts between.
                FindUserDeclaredConversion(probe, out var found, out _);
                bypassed = found is not null
                    ? $"the declared conversion method '{found}'"
                    : $"the user-defined conversion operator from '{srcElem.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' to '{tgtElem.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'";
                verb = "calling";
            }
            else
            {
                // The directive shape. A pair-scoped directive or hook has no single symbol to name, so the
                // phrase comes from the customization rule itself — the only place that knows which kind matched
                // and therefore whether it was DECLARED FOR this pair (an attribute) or MATCHED TO it by
                // implicit conversion (a hook), and which verb reads correctly for it.
                if (req.NestedRegistry?.PairCustomization(srcElem, tgtElem) is not { } customization)
                {
                    return;
                }

                bypassed = customization.What;
                verb = customization.Verb;
            }

            // "the block copy fills '<member>'" rather than "it is not called for its elements": every pronoun
            // in the old frame bound to the bypassed thing rather than to the member, which is the one noun a
            // reader needs to act on. The member is named three times on purpose — it is what they go and edit.
            acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretBypassesConversion,
                req.Location,
                $"[Reinterpret] on '{memberName}' takes precedence over {bypassed}, so the block copy fills " +
                $"'{memberName}' without {verb} it; remove [Reinterpret] from '{memberName}' to use it instead",
                MemberName: memberName));
        }

        /// <summary>
        ///     Reports <c>DWARF101</c> when <paramref name="element" /> — a struct on one side of a mapped
        ///     collection — wastes a quarter or more of its bytes on alignment padding, naming the field order
        ///     that packs it. Silent for anything <see cref="LayoutHygiene.Measure" /> refuses to measure, which
        ///     includes every struct the consumer does not declare.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Round 29, <c>T0.3</c>. Called for BOTH element types, and once per struct TYPE rather than once
        ///         per member. The two are connected: reporting only the destination would be the tidier rule and
        ///         the wrong one, because a pair that blits today is layout-identical, and a consumer who reorders
        ///         one side alone breaks that identity and loses the block copy in silence — <c>DWARF100</c> would
        ///         not say so either, since the two field lists no longer line up positionally at all. Both sides
        ///         are named so the remedy is applied to both. An identity pair (<c>V[] → V[]</c>) collapses to
        ///         one report through the dedupe below rather than through a special case.
        ///     </para>
        ///     <para>
        ///         The report is anchored on the TYPE's declaration, not on the member that reached it. The
        ///         message asks the consumer to reorder that type's fields, so that is where the squiggle has to
        ///         be for the mandate to hold — "the exact location" is the line they will edit. It also makes
        ///         once-per-type structural rather than merely tidy: two members of one padded type now produce
        ///         diagnostics identical in descriptor, location AND text, so dropping the second cannot be
        ///         dropping information, and the surviving one no longer depends on which member happened to be
        ///         declared first. The dedupe below is still what performs the drop — a scan of the sink for the
        ///         same text — and the message stays type-only for it: the struct, its numbers and its field
        ///         order, and nothing about the member. The sink is the mapper class's own diagnostic list, so
        ///         "once" means once per mapper class; a probe resolving into a throwaway list (the flatten and
        ///         hetero leaf probes) cannot see it, which is the one gap in the rule and is bounded by those
        ///         probes' own scope.
        ///     </para>
        /// </remarks>
        private static void ReportPaddedElementStruct(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            ITypeSymbol element)
        {
            if (LayoutHygiene.Measure(element) is not { } layout || !LayoutHygiene.WastesAQuarter(layout))
            {
                return;
            }

            // A struct ANOTHER generator emitted passes every measurement test and fails the only one that
            // matters at a report: its field order is not the consumer's to change, and a diagnostic raised
            // inside a .g.cs is one they cannot suppress either — the shape this project has already been bitten
            // by (GeneratedCodeIsWarningFreeTests exists for the emission side of it). The refusal lives HERE
            // rather than in Measure on purpose: a generated struct still HAS a size, and Task 2.1 reads that
            // size for a threshold which asks the consumer to edit nothing. Measurability and actionability are
            // different questions, so they are asked in different places.
            foreach (var declaration in element.DeclaringSyntaxReferences)
                if (GeneratedSourceExtensions.IsGeneratorAuthored(declaration.SyntaxTree))
                {
                    return;
                }

            // Measure has already required an in-source declaration (IsSourceSequential refuses a metadata
            // struct), so this location exists whenever a layout came back. LocationInfo.From can still decline
            // a span it cannot map onto a live IDE snapshot, and the member's own location is a better answer
            // there than none at all.
            var declared = element.Locations.FirstOrDefault(l => l.IsInSource);
            var location = (declared is null ? null : LocationInfo.From(declared)) ?? req.Location;

            var message =
                $"'{element.ToDisplayString()}' is {layout.Size.ToString(CultureInfo.InvariantCulture)} bytes with " +
                $"{layout.Padding.ToString(CultureInfo.InvariantCulture)} bytes of padding; declaring its fields as " +
                $"{layout.PackedOrderText} makes it {layout.PackedSize.ToString(CultureInfo.InvariantCulture)} bytes " +
                "— smaller arrays, and a layout-identical twin can take the blit";

            foreach (var reported in diagnostics)
                if (ReferenceEquals(reported.Descriptor, DiagnosticDescriptors.StructLayoutPadding) &&
                    string.Equals(reported.MessageArg, message, StringComparison.Ordinal))
                {
                    return;
                }

            // No MemberName, unlike its neighbours: that property bag entry is what a code fix reads to find
            // the member it must edit, and this diagnostic has no member to edit — the remedy is on the type,
            // and whichever member reached it first is an accident of declaration order. Handing a future fix
            // an arbitrary member would be worse than handing it nothing.
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StructLayoutPadding, location, message));
        }

        /// <summary>
        ///     The transfer-model facts for one compilation — which types are derived from, and which are EF
        ///     entities — gathered ONCE and handed to <see cref="TransferModelShape.Classify(INamedTypeSymbol, Compilation, TransferModelShape.CompilationFacts)" />
        ///     so the predicate stays pure.
        /// </summary>
        /// <remarks>
        ///     The brief for <c>T2.2</c> says "compute them once per compilation and pass them in", and this is
        ///     the shape of that in a chain whose entry point (<c>TryResolveConversion</c>) is called once per
        ///     MEMBER from a dozen sites: gathering at the entry point would sweep the assembly per member, and
        ///     threading the facts down from every caller would put a parameter nobody else reads through all of
        ///     them. Keyed by a <see cref="ConditionalWeakTable{TKey,TValue}" />, exactly as
        ///     <c>BaseTypesByCompilation</c> is a few files over and for the same reason: the entry (and the
        ///     symbols it holds) dies with the compilation, where a static dictionary in a long-lived generator
        ///     host would pin symbols from every compilation it ever saw.
        ///     <para>
        ///         Called LAST in the gate below, after every cheap question has already been answered, so a
        ///         compilation with no candidate pair in it never pays for the sweep at all.
        ///     </para>
        /// </remarks>
        private static readonly ConditionalWeakTable<Compilation, TransferModelShape.CompilationFacts>
            TransferModelFactsByCompilation = new();

        private static TransferModelShape.CompilationFacts TransferModelFacts(Compilation compilation)
        {
            return TransferModelFactsByCompilation.GetValue(compilation,
                static c => TransferModelShape.CompilationFacts.Gather(c));
        }

        /// <summary>
        ///     Reports <c>DWARF103</c> when a mapped collection builds one CLASS element per item and that
        ///     element type is transfer-model shaped — i.e. it could be a <c>readonly record struct</c>, which
        ///     would make the whole collection one allocation instead of one per element.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The site.</b> After the element pair has RESOLVED, and before the collection helper is
        ///         synthesized. Resolution is what tells this apart from every neighbouring shape without
        ///         re-deriving anything: <paramref name="elemConv" /> names what will actually build each
        ///         element. The blit branches above cannot swallow it — every one of them requires an unmanaged
        ///         struct pair, and this speaks only about classes — and the element recursion below has already
        ///         reported its own refusals, so a pair that got here is a pair that maps.
        ///     </para>
        ///     <para>
        ///         <b>"This generator builds the element" is the whole gate</b>, and the obvious rule — the one
        ///         the blit gate above uses, "no user-declared conversion for the element pair" — is wrong here.
        ///         The shape the research measured is <c>partial List&lt;OrderDto&gt; Map(List&lt;Order&gt;)</c>
        ///         beside <c>partial OrderDto Map(Order)</c>, whose element resolves to that SIBLING PARTIAL
        ///         METHOD; it counts as a user conversion to the blit gate (the blit would be bypassing a call)
        ///         and it is this generator's own emitted body here. So the question asked is which of the two
        ///         it is: a synthesized object map, or an auto-candidate whose signature IS this element pair —
        ///         and <c>AutoCandidates</c> holds partial DEFINITIONS only (<c>CollectMapperMethods</c>) plus
        ///         the <c>[GenerateMap&lt;S,T&gt;]</c> pairs, so everything in it is a body the generator
        ///         writes. A hand-written converter, a conversion operator's shim and an identity copy (nothing
        ///         is allocated per element at all) are none of those, and are silent.
        ///     </para>
        ///     <para>
        ///         <b>Three refusals the classifier cannot make</b>, because they are about the REPORT rather
        ///         than the shape. A pair-scoped directive or hook is honoured through the same
        ///         <c>NestedMappingRegistry.PairCustomization</c> the blit gate reads: an <c>[AfterMap]</c> that
        ///         mutates the target is a compile error or a lost write once the target is a value type, which
        ///         is the silent semantic change this project refuses. Preserve and SetNull are refused because
        ///         reference identity is the point of those modes and a value type has none. And a DTO another
        ///         generator emitted is refused for <see cref="ReportPaddedElementStruct" />'s reason: the
        ///         consumer cannot rewrite a declaration they did not write — asked of the SOURCE too, though
        ///         there it costs only the block-copy clause, since the target is still theirs to change and
        ///         that is where the allocation goes.
        ///     </para>
        ///     <para>
        ///         <b>Once per element PAIR</b>, through the same message-text dedupe as <c>DWARF101</c> — the
        ///         message names the pair, its size and its remedy and nothing about the member it was reached
        ///         through, so a second member mapping the same pair produces an identical string and drops. The
        ///         sink is the mapper class's own diagnostic list, so "once" is once per mapper class. The
        ///         location is the MAPPING site rather than the type: the cost is paid where the collection is,
        ///         a type-level rule would fire on every DTO in a solution, and that is the trade the research
        ///         made explicitly. <c>MemberName</c> is left off for <see cref="ReportPaddedElementStruct" />'s
        ///         reason too — the remedy edits the type, and whichever member reached it first is an accident
        ///         of declaration order.
        ///     </para>
        /// </remarks>
        private static void ReportTransferModelElement(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem,
            string? elemConv)
        {
            // Reference identity is the point of these two modes: Preserve rebuilds the source's topology and
            // SetNull writes null into a back-edge. A value type has no identity to preserve and no field that
            // can hold that null, so the suggestion would undo what the mapper was configured for.
            if (req.IsPreserve || req.IsSetNull)
            {
                return;
            }

            // Class → class. The classifier refuses a value-type target on its own, so only the SOURCE side is
            // asked here — "one allocation per element" is a claim about what the source loop produces.
            if (srcElem.TypeKind != TypeKind.Class || tgtElem is not INamedTypeSymbol { TypeKind: TypeKind.Class } target)
            {
                return;
            }

            if (!ElementIsBuiltByGeneratedCode(req, srcElem, tgtElem, elemConv))
            {
                return;
            }

            // A directive or hook declared for this pair survives the rewrite only by accident: a [BeforeMap]/
            // [AfterMap] taking the target by value writes into a copy once the target is a struct. Asked
            // through the registry's own rule, so this and the blit gate cannot disagree about what customizes
            // a pair.
            if (req.NestedRegistry?.PairIsCustomized(srcElem, tgtElem) == true)
            {
                return;
            }

            if (IsGeneratorAuthored(target))
            {
                return;
            }

            var facts = TransferModelFacts(req.Compilation);

            var verdict = TransferModelShape.Classify(target, req.Compilation, facts);
            if (!verdict.IsShaped)
            {
                return;
            }

            // The SOURCE is classified too, and only for the block-copy clause. It is asked last, after the
            // target's own verdict has already earned the report, so a pair that says nothing never pays for
            // it — and it is asked at all because the clause advises converting the source, which no other
            // rule in this method has checked. An unshaped source still reports; it just hears nothing about
            // its own type. See TransferModelElementMessage's remarks.
            //
            // A source in a .g.cs is refused here rather than by the classifier, and only for the CLAUSE: the
            // consumer cannot rewrite a declaration they did not write, which is the same question
            // ReportPaddedElementStruct asks and the same one asked of the target above. The diagnostic still
            // fires — the target is theirs to change, and that is where the allocation goes (fix round 2).
            // A source from a referenced assembly needs no check here: the classifier refuses a symbol with no
            // DeclaringSyntaxReferences outright, so it is never IsShaped.
            var source = (INamedTypeSymbol)srcElem;
            // NULL, not a fabricated refusal: "there is no verdict for the source" is what this means, and a
            // Verdict.No would have had to invent a reason string nothing on this path ever prints. The first
            // attempt at that used `default`, which is a trap worth recording — Outcome.Eligible WAS the enum's
            // zero, so `default(Verdict)` reported IsShaped TRUE and the clause came straight back. The
            // regression test caught it; the nullable makes the state unrepresentable instead. T2.3 ruling 3
            // then moved NotEligible into the zero slot, so the trap no longer exists — this stays a nullable
            // anyway, because "nobody asked" and "asked, and the answer was no" are different facts and only
            // one of them has a reason to print.
            TransferModelShape.Verdict? sourceVerdict = IsGeneratorAuthored(source)
                ? null
                : TransferModelShape.Classify(source, req.Compilation, facts);

            var message = TransferModelElementMessage(source, target, verdict, sourceVerdict);

            foreach (var reported in diagnostics)
                if (ReferenceEquals(reported.Descriptor, DiagnosticDescriptors.CollectionElementCouldBeAStruct) &&
                    string.Equals(reported.MessageArg, message, StringComparison.Ordinal))
                {
                    return;
                }

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.CollectionElementCouldBeAStruct,
                req.Location,
                message));
        }

        /// <summary>
        ///     True when <paramref name="type" /> is declared in a file another generator emitted. Asked of BOTH
        ///     element types, because both are named by a message that asks for a declaration to be rewritten —
        ///     and a <c>.g.cs</c> is neither the consumer's to edit nor theirs to suppress a diagnostic in.
        /// </summary>
        private static bool IsGeneratorAuthored(INamedTypeSymbol type)
        {
            foreach (var declaration in type.DeclaringSyntaxReferences)
                if (GeneratedSourceExtensions.IsGeneratorAuthored(declaration.SyntaxTree))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     True when the ELEMENTS of this collection are constructed by a body this generator emits — the
        ///     one fact that makes "one allocation per element" both true and ours to remove.
        /// </summary>
        /// <remarks>
        ///     <c>null</c> is an identity copy: the references are copied and no element is allocated, so the
        ///     message's claim would be false. A synthesized object map is ours by construction. Anything else
        ///     synthesized (a conversion operator's shim, an enum or numeric helper) is not an object map and
        ///     does not build a transfer model. What is left is a name from the mapper, and only an
        ///     <c>AutoCandidates</c> entry MATCHING THIS ELEMENT PAIR qualifies: that list is the partial
        ///     definitions plus the <c>[GenerateMap&lt;S,T&gt;]</c> pairs, every one of which this generator
        ///     writes the body of. A hand-written <c>OrderDto Convert(Order)</c> lives in <c>AllMethods</c>
        ///     instead and owns its own construction.
        /// </remarks>
        private static bool ElementIsBuiltByGeneratedCode(
            ConversionRequest req,
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem,
            string? elemConv)
        {
            if (elemConv is null)
            {
                return false;
            }

            if (GeneratedNames.IsAnySynthesized(elemConv))
            {
                return GeneratedNames.IsObjectMap(elemConv);
            }

            foreach (var candidate in req.AutoCandidates)
                if (string.Equals(candidate.Name, elemConv, StringComparison.Ordinal) &&
                    SymbolEqualityComparer.Default.Equals(candidate.ParamType, srcElem) &&
                    SymbolEqualityComparer.Default.Equals(candidate.ReturnType, tgtElem))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     Builds <c>DWARF103</c>'s message: the pair, what the rewrite buys, the would-be struct's size,
        ///     and — only where the verdict earns them — the <c>in</c> advice and the scope of the derived-type
        ///     check.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Every number here is worded to the evidence behind it.</b>
        ///         <see cref="TransferModelShape.Verdict.SizeIsUpperBound" /> means a member was costed as an
        ///         8-byte reference, which is its x64 width and twice its x86 one, so the size is an
        ///         OVER-estimate and is printed as "at most". The bound only ever over-counts, so the advice
        ///         cannot become wrong on a narrower platform — but printing an estimate as a measurement would
        ///         be a false claim about the consumer's machine, which is the defect that flag exists to
        ///         prevent.
        ///     </para>
        ///     <para>
        ///         The nested clause is not decoration either: the classifier counts a nested transfer model
        ///         INLINE, because the code fix rewrites it too. Convert the root alone and the member stays a
        ///         reference and the size is smaller. "With any transfer model it holds a struct too" is what
        ///         makes the number true of the rewrite it is advising, in the cases where the type has one and
        ///         (vacuously) in the cases where it does not.
        ///     </para>
        ///     <para>
        ///         <b>The <c>in</c> advice belongs to ONE band, and getting that wrong is easy in both
        ///         directions.</b> Reading <c>SuggestIn</c> alone would advise it at 40 bytes and NOT at 72,
        ///         which is backwards; reading <c>TooLarge || SuggestIn</c> — what this shipped with until fix
        ///         round 3 — advises it at 40 bytes as well, which the measurement does not support: a 64-byte
        ///         struct still beat its class by value (26.4 ns vs 29.9 ns). The spec tiers the bands ≤32 B
        ///         silent, 32–64 B Info, &gt;64 B suggest <c>in</c>, the project owner ruled that tiering
        ///         reasonable (<c>Issues/round29/RESEARCH-hardware-mode.md</c> §8, ruling (b)), and the answer
        ///         is <c>Kind == TooLarge</c> on its own. The middle band's whole report is the size, printed
        ///         in every band.
        ///     </para>
        ///     <para>
        ///         The clause reprints the size, so it reprints the HEDGE with it (T2.2 review, important 4):
        ///         the message used to say "at most 72 bytes" in one clause and "At 72 bytes" in the next, and
        ///         the threshold comparison is provisional in the same way the number is — 72 bytes of
        ///         references is 36 on a 32-bit runtime, which is under the limit. The advice survives that
        ///         either way (passing a smaller struct by <c>in</c> costs nothing), and the clause says so
        ///         rather than pretending the comparison was settled.
        ///     </para>
        ///     <para>
        ///         The derivation clause states the SCOPE of the sweep rather than its conclusion. That is
        ///         deliberate: the flag does not propagate out of a nested model, and it reads false for an
        ///         <c>internal</c> type that an <c>InternalsVisibleTo</c> grant lets a friend assembly derive
        ///         from. A message that claimed derivation had been ruled out would be wrong in both of those;
        ///         this one never claims it at all, and says what was actually walked where the gap is widest.
        ///     </para>
        ///     <para>
        ///         <b>The block-copy clause is EARNED twice over, and used to be earned not at all</b> (T2.2
        ///         review, criticals 1 and 2). It said "with '{source}' a struct as well, the pair takes the
        ///         block copy" unconditionally, which failed two different ways. It advised converting a SOURCE
        ///         type nothing had classified — and in the commonest real shape, entity to DTO, that is advice
        ///         to turn an ORM-tracked entity into a value type, exactly what the classifier exists to
        ///         refuse. And it stated a block copy the generator can itself disprove: <c>CanReinterpret</c>
        ///         needs BOTH element types unmanaged, so a target with a reference member — the same fact that
        ///         sets <see cref="TransferModelShape.Verdict.SizeIsUpperBound" /> — can never blit, and the
        ///         message hedged its byte count for that reason two clauses earlier while asserting the
        ///         consequence as fact. So the clause now requires the source to be transfer-model shaped in its
        ///         own right AND neither side to hold a reference, and even then says "could", naming the layout
        ///         and field-name identity the proof still requires and this site has not checked.
        ///     </para>
        ///     <para>
        ///         Note what is NOT gated: the diagnostic still FIRES for an unshaped source. Collapsing N
        ///         object headers into one array is the measured win and does not depend on the source type at
        ///         all; the block copy is a further tier. Narrowing the trigger would have silenced the
        ///         entity-to-DTO shape, where the target is the consumer's own type and the win is undiminished.
        ///     </para>
        /// </remarks>
        private static string TransferModelElementMessage(
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem,
            TransferModelShape.Verdict verdict,
            TransferModelShape.Verdict? sourceVerdict)
        {
            // WITHOUT the annotation. A `List<OrderDto?>` element arrives here as `OrderDto?`, and the message
            // names a type the consumer is being asked to REDECLARE — "declare 'OrderDto?' as a readonly record
            // struct" names no declaration that exists. It also matters to the dedupe: `List<OrderDto>` and
            // `List<OrderDto?>` on one mapper are one type and must be one report, not two.
            var source = srcElem.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            var target = tgtElem.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            var size = verdict.Size.ToString(CultureInfo.InvariantCulture);

            var sizeText = verdict.SizeIsUpperBound
                ? $"at most {size} bytes, a reference member counted at 8 bytes, its x64 width,"
                : $"{size} bytes,";

            var message =
                $"'{source}' → '{target}' allocates one '{target}' per element, and '{target}' is " +
                "transfer-model shaped: declared as a readonly record struct — with any transfer model it " +
                $"holds a struct too — it is {sizeText} and the collection becomes one allocation instead of " +
                "one per element.";

            // The block copy needs an UNMANAGED pair, and a verdict carrying SizeIsUpperBound is one whose
            // would-be struct holds a reference — so on either side that flag is proof the pair can never
            // blit, whatever else is true of it. The flag propagates out of a nested model (TryMeasureMember
            // hands nested.SizeIsUpperBound straight back), so it covers the whole inlined graph and not just
            // the top level.
            var namesTheSource = sourceVerdict is { IsShaped: true, SizeIsUpperBound: false } &&
                                 !verdict.SizeIsUpperBound;

            if (namesTheSource)
            {
                message += $" '{source}' is transfer-model shaped too, and neither type holds a reference: as " +
                           "structs with identical layout and matching field names the pair could take the " +
                           "block copy instead of the element loop.";
            }

            // TooLarge ALONE, not `TooLarge || SuggestIn` (fix round 3). The spec tiers the bands — ≤32 B
            // silent, 32–64 B Info, >64 B suggest `in` — and the project owner ruled that tiering reasonable
            // (Issues/round29/RESEARCH-hardware-mode.md §8, ruling (b); the measurement is §7). Under it is that a
            // 64-byte struct still beat its class BY VALUE (26.4 ns vs 29.9 ns, 240 B), so `in` in the middle
            // band would be advising an indirection the numbers do not ask for. The middle band's report is
            // the size, which is printed above in every band.
            if (verdict.Kind == TransferModelShape.Outcome.TooLarge)
            {
                var limit = TransferModelShape.SuggestInSizeLimit.ToString(CultureInfo.InvariantCulture);
                message += verdict.SizeIsUpperBound
                    ? $" At most {size} bytes — over the {limit}-byte limit for copying by value unless a " +
                      "32-bit runtime narrows it below, and worth passing by 'in' either way."
                    : $" At {size} bytes it is over the {limit}-byte limit for copying by value, so pass it " +
                      "by 'in'.";
            }

            // ONE caveat covering whichever of the two types earned it. The source earns it on exactly the
            // evidence the target does — the sweep walked this assembly, and a consuming project can subclass
            // either — and only when the message named it in the first place; adding a second sentence instead
            // would say the same thing twice about a pair that is usually public on both sides (fix round 2).
            var caveatTarget = verdict.DerivationCheckedWithinAssemblyOnly;
            var caveatSource = namesTheSource && sourceVerdict is { DerivationCheckedWithinAssemblyOnly: true };

            if (caveatTarget || caveatSource)
            {
                var both = caveatTarget && caveatSource;
                var caveated = both ? $"'{target}' or '{source}'" : caveatTarget ? $"'{target}'" : $"'{source}'";

                message += $" The check that nothing derives from {caveated} covered this assembly only, since " +
                           (both ? "both are" : $"{caveated} is") + " public and not sealed — a project " +
                           "referencing this one can still derive from " + (both ? "them." : "it.");
            }

            return message;
        }

        /// <summary>
        ///     Collections: element-wise conversion, including the in-place and context-threading shapes.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when this arm CLAIMED the pair, in which case <paramref name="resolved" /> carries the
        ///     verdict it reached; <c>false</c> when the pair is not this arm's to answer and the chain continues.
        /// </returns>

        private static bool HandleCollectionConversion(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            Dictionary<string, SynthesizedMethod> synthesized,
            ref string? converterMethod,
            ref bool converterNeedsCtx,
            out bool resolved)
        {
            resolved = false;

            if (CollectionConverter.TryResolve(req.SrcType,
                    req.TgtType,
                    out var srcElem,
                    out var tgtElem,
                    out var collShape,
                    req.NullAsNull))
            {
                // ── Layout hygiene on the element types (round 29, T0.3) ────────────────────────────────────
                // Reported HERE, above every branch below, because the padding is worth the same to the reader
                // whether the pair goes on to blit or to take the element loop: a block copy copies the wasted
                // bytes as faithfully as the loop writes them, and the array is the same size either way. The
                // near-miss below could not carry it — that one speaks only when the blit was REFUSED, so the
                // pairs with the most to gain (the ones already blitting) would never have heard it.
                ReportPaddedElementStruct(req, diagnostics, srcElem);
                ReportPaddedElementStruct(req, diagnostics, tgtElem);

                // ── The blit is a fast path, never a change of meaning (round 29, T0.2c) ────────────────────
                // This arm decides the block copy at chain position 2 — BEFORE the arms that adopt a
                // user-declared conversion for the ELEMENT pair, and before the element recursion below runs at
                // all. So the proof used to be the whole decision, and a `public static DstV Conv(SrcV s)`
                // declared beside a `SrcV[] → DstV[]` member was silently never called: the bytes were copied
                // and nothing in the build said so. Same for a pair-scoped [MapIgnore<T>]/[MapProperty<S,T>]/
                // [MapValue<T>] or a [BeforeMap]/[AfterMap] hook matching the element pair.
                //
                // The fix mirrors the span map's gate (MapperExtractor.Phases.cs, TryHandleSpanMap) from the
                // opposite side: the span endpoint resolves the element pair first and then asks whether the
                // verdict it got back is a DEFAULT converter; this arm cannot resolve first — doing so would
                // synthesize an unused __DwarfMap_Obj_* for every blitted pair, and under [AutoNest(false)]
                // would turn a working blit into DWARF005 — so it asks the resolver's own questions instead,
                // through the resolver's own predicates (FindUserDeclaredConversion / AutoNestWouldClaim /
                // UserConversionConverter.Exists), and never through a second copy of them.
                //
                // What still blits: a pair whose element resolution would land on a SYNTHESIZED object map
                // (or on nothing at all). CanReinterpret proves the by-name field correspondence that map
                // would apply, so the two agree byte for byte — which is exactly why the proof is allowed to
                // replace it, and only it.
                var elemHasUserConversion = ElementPairResolvesToUserConversion(req, srcElem, tgtElem);
                var elemPairIsCustomized = req.NestedRegistry?.PairIsCustomized(srcElem, tgtElem) == true;
                var elemPairKeepsTheLoop = elemHasUserConversion || elemPairIsCustomized;

                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    !elemPairKeepsTheLoop &&
                    BlittableProof.CanReinterpret(srcElem,
                        tgtElem))
                {
                    converterMethod = CollectionConverter.SynthesizeBlit(synthesized, req.SrcType, srcElem, tgtElem);
                    resolved = true;
                    return true;
                }

                // R25-03: enum arrays as underlying-primitive blit. Kept separate from the struct proof above
                // because the question is not layout — the layouts are trivially identical — but whether the
                // SCALAR path is a reinterpret. Under ByName it is not: its switch throws on a value matching
                // no member, and an enum may legally hold any value of its underlying type.
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    !elemPairKeepsTheLoop &&
                    BlittableProof.CanReinterpretEnums(srcElem, tgtElem, req.EnumPolicy.Strategy))
                {
                    converterMethod = CollectionConverter.SynthesizeBlit(synthesized, req.SrcType, srcElem, tgtElem);
                    resolved = true;
                    return true;
                }

                // R25-02 (T2): the LIST-involved shapes — array→List, List→array, List→List. The element proof
                // is the one above, reused verbatim and never relaxed; all that changes is which storage the
                // bytes are read from and written to. Interfaces are excluded on the source side because
                // CollectionsMarshal.AsSpan is declared on the concrete List<T>.
                //
                // NOT reached for array→array: that returns above. NOT gated on a minimum length either —
                // measured locally at 2026-08-23, the crossover is between n=4 and n=8 and the sub-crossover
                // penalty is tens of nanoseconds, so a runtime branch would cost more clarity than it buys
                // time. The RFC's `Count >= 32` guard came from a container that this hardware does not
                // reproduce. See benchmarks/results/2026-08-23-round25-kernels.md.
                if (!collShape.NullAsNull)
                {
                    var tgtIsArray = collShape.Target == CollectionConverter.TargetKind.Array;
                    var tgtIsListFamily = collShape.Target is CollectionConverter.TargetKind.List
                        or CollectionConverter.TargetKind.ICollection
                        or CollectionConverter.TargetKind.IList
                        or CollectionConverter.TargetKind.IReadOnlyList
                        or CollectionConverter.TargetKind.IReadOnlyCollection;
                    var tgtIsImmutableArray = collShape.Target == CollectionConverter.TargetKind.ImmutableArray;
                    var srcIsList = CollectionConverter.IsConcreteList(req.SrcType);
                    var srcIsImmutableArray = CollectionConverter.IsImmutableArray(req.SrcType);
                    var elementBlits = !elemPairKeepsTheLoop &&
                                       (BlittableProof.CanReinterpret(srcElem, tgtElem) ||
                                        BlittableProof.CanReinterpretEnums(srcElem, tgtElem, req.EnumPolicy.Strategy));

                    var srcStorage = collShape.SourceIsArray ? CollectionConverter.BlitStorage.Array
                        : srcIsList ? CollectionConverter.BlitStorage.List
                        : srcIsImmutableArray ? CollectionConverter.BlitStorage.ImmutableArray
                        : (CollectionConverter.BlitStorage?)null;

                    var tgtStorage = tgtIsArray ? CollectionConverter.BlitStorage.Array
                        : tgtIsListFamily ? CollectionConverter.BlitStorage.List
                        : tgtIsImmutableArray ? CollectionConverter.BlitStorage.ImmutableArray
                        : (CollectionConverter.BlitStorage?)null;

                    if (elementBlits &&
                        srcStorage is { } ss &&
                        tgtStorage is { } ts &&
                        !(ss == CollectionConverter.BlitStorage.Array && ts == CollectionConverter.BlitStorage.Array))
                    {
                        converterMethod = CollectionConverter.SynthesizeBlitListShape(synthesized,
                            req.SrcType,
                            srcElem,
                            tgtElem,
                            ss,
                            ts);
                        resolved = true;
                        return true;
                    }
                }

                // The blit was not provable. If the pair MISSED it narrowly, say so — the element loop is correct
                // but the caller is one rename away from a block copy, and nothing else in the build reports that.
                // Never reached for [Reinterpret] members: that branch forces the blit and returns before this
                // method is called, so DWARF022 stays the only voice on the explicit form.
                //
                // Gated on elemHasUserConversion — and deliberately NOT on elemPairIsCustomized (round 29 T0.2c).
                // The two halves of the blit refusal are not the same kind of fact here. A user conversion OWNS
                // the pair: no rename would hand that caller the block copy, so "you are one rename away" would
                // be false. A pair-scoped rename is the opposite — it is precisely the caller who reconciled a
                // name mismatch by hand and for whom renaming the field IS the fix, which
                // BlittableProofNearMissTests.A_name_mismatch_reconciled_by_MapProperty_still_reports_the_near_miss
                // has pinned since the diagnostic was introduced. A pair directive that is NOT a rename cannot
                // produce a spurious hint either: TryExplainNearMiss answers false for a pair that already lines
                // up by name, which is the only shape a [MapIgnore]/[MapValue]/hook refusal leaves behind.
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    !elemHasUserConversion &&
                    BlittableProof.TryExplainNearMiss(srcElem, tgtElem, out var nearMissReason))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.BlitNearMiss,
                        req.Location,
                        $"'{req.TargetName}' maps an array whose element types are nearly layout-identical, so it " +
                        $"takes the element-by-element copy: {nearMissReason}",
                        MemberName: req.TargetName));
                }

                // SIMD widening fast-path: array→array of a lossless primitive widen pair (e.g. int[]→long[],
                // float[]→double[]) → Vector.Widen. Identical result to the scalar implicit widen; reflection-free.
                // Comes AFTER blit (same-size pairs blit; widen pairs differ in size so CanReinterpret is false).
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    CollectionConverter.IsWidenPair(srcElem,
                        tgtElem))
                {
                    converterMethod = CollectionConverter.SynthesizeSimdWiden(synthesized, req.SrcType, srcElem, tgtElem);
                    resolved = true;
                    return true;
                }

                // A3: determine effective null-as-null for the OUTER collection helper based on target nullability.
                // Reference-type collections: fall back to AsEmpty when target is non-nullable to prevent CS8601.
                // ImmutableArray<T>?: CollectionConverter.TryResolve already handles Nullable<ImmutableArray<T>>
                // by unwrapping it and setting req.NullAsNull=true in the shape, so collShape.NullAsNull is already
                // correct and we just need to preserve it.
                var collEffectiveNullAsNull = collShape.Target == CollectionConverter.TargetKind.ImmutableArray
                    // ImmutableArray: shape.NullAsNull is authoritative (set by TryResolve for Nullable<> unwrapping).
                    ? collShape.NullAsNull
                    // Reference-type collections: only AsNull when target field is nullable ref type.
                    : req.NullAsNull && IsNullableReferenceType(req.TgtType);

                // A1: propagate req.NullAsNull to the element converter so nullable elements
                // (e.g. element type List<int>? inside List<List<int>?>) generate helpers
                // that preserve null instead of silently mapping to empty.
                if (!TryResolveConversion(req.Compilation,
                        srcElem,
                        tgtElem,
                        null,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        synthesized,
                        req.NullStrategy,
                        req.Location,
                        req.TargetName,
                        diagnostics,
                        out var elemConv,
                        out var elemNull,
                        out var elemNeedsCtx,
                        req.AutoNest,
                        req.NestedRegistry,
                        req.NullAsNull,
                        req.IsPreserve,
                        isSetNull: req.IsSetNull,
                        implicitConversions: req.ImplicitConversions,
                        reservedConverters: req.ReservedConverters))
                {
                    resolved = false; // element diagnostic already reported by the recursive call
                    return true;
                }

                // ── The element could be a struct (round 29, T2.2) ──────────────────────────────────────────
                // Reported HERE, after the element pair resolved and before the collection helper is built:
                // elemConv is what names the body that will construct each element, which is the one fact that
                // makes "one allocation per element" ours to remove rather than the caller's. Above this the
                // pair has not been resolved yet; below it the helper is already synthesized.
                ReportTransferModelElement(req, diagnostics, srcElem, tgtElem, elemConv);

                // #100: a nullable-annotated REFERENCE element whose target element is non-nullable needs the
                // same per-element null handling as a nullable VALUE element (int?→int). SymbolEqualityComparer
                // ignores nullable annotations, so without this the identity fast-path emits a direct collection
                // copy the compiler rejects (List<string?>→List<string> = CS8620) or an array clone that smuggles
                // nulls past the annotation. A non-null target element cannot hold null, so throw on a null
                // element — loud and never-silent (there is no valid non-null reference "default" to substitute).
                if (elemConv is null &&
                    elemNull == NullHandling.None &&
                    srcElem.IsReferenceType &&
                    srcElem.NullableAnnotation == NullableAnnotation.Annotated &&
                    tgtElem.NullableAnnotation != NullableAnnotation.Annotated &&
                    SymbolEqualityComparer.Default.Equals(
                        srcElem.WithNullableAnnotation(NullableAnnotation.None),
                        tgtElem.WithNullableAnnotation(NullableAnnotation.None)))
                {
                    elemNull = NullHandling.ThrowIfNull;
                }

                // Preserve OR SetNull: if the element converter is an auto-nested object mapper, force it
                // recursion-capable so it gets the (ctx, depth) signature — the collection helper will call it
                // with (elem, ctx, depth + 1), threading ONE shared context across the collection edge. This
                // is what lets a cycle routed through a collection break (SetNull → back-edge null) or
                // depth-cap, instead of the element re-entering the public entry (fresh context → StackOverflow).
                if ((req.IsPreserve || req.IsSetNull) && elemConv is not null && GeneratedNames.IsObjectMap(elemConv) && req.NestedRegistry is not null)
                {
                    req.NestedRegistry.ForceRecursionCapable(elemConv);
                    elemNeedsCtx = true;
                }

                // Apply effective req.NullAsNull (A3: may be false even when req.NullAsNull=true if target is non-nullable).
                if (collEffectiveNullAsNull != req.NullAsNull)
                {
                    collShape = new CollectionConverter.Shape(collShape.Target,
                        collShape.SourceIsArray,
                        collShape.Count,
                        collEffectiveNullAsNull);
                }

                converterMethod = CollectionConverter.Synthesize(synthesized,
                    req.SrcType,
                    srcElem,
                    tgtElem,
                    collShape,
                    elemConv,
                    elemNull,
                    req.IsPreserve,
                    elemNeedsCtx);
                // Thread (ctx, depth) when the collection register-before-fills (Preserve mutable) OR its
                // element is recursion-capable (Preserve, or None/SetNull self-referential element).
                converterNeedsCtx = (req.IsPreserve && CollectionConverter.IsMutableReferenceCollection(collShape.Target)) ||
                                    elemNeedsCtx;

                // None+Throw: the element resolved either to a PUBLIC declared method (e.g. a self-map `Map`) or
                // to a SYNTHESIZED object-map helper (`__DwarfMap_Obj_…`, which is what a [GenerateMap<S,T>] pair
                // produces — there is no declared method to resolve to). Record a re-synthesis closure for BOTH:
                // if the element turns out self-recursive, the post-pass re-emits this collection helper so it
                // threads (ctx, depth) into the element call.
                //
                // Only covering the public-method case was a real bug: a [GenerateMap] pair whose type recurses
                // THROUGH a collection edge (e.g. `class Node { List<Node> Kids; }`) had its object helper marked
                // recursion-capable by ComputeRecursionCapability() — gaining (ctx, depth) IN PLACE — while the
                // collection helper calling it was never re-synthesized, so it still called it with one argument.
                // That emitted code which did not compile (CS7036). The equivalent partial-method mapper worked,
                // because its element resolved to a declared method and so WAS recorded here.
                if (!req.IsPreserve && !req.IsSetNull && !elemNeedsCtx && req.NestedRegistry is not null && elemConv is not null && (!GeneratedNames.IsAnySynthesized(elemConv) || GeneratedNames.IsObjectMap(elemConv)) && tgtElem is INamedTypeSymbol tgtElemNamed && IsMappableObjectPair(req.Compilation, srcElem, tgtElemNamed))
                {
                    var hName = converterMethod;
                    var capSrc = req.SrcType;
                    var capElem = srcElem;
                    var capTgt = tgtElem;
                    var capShape = collShape;
                    var capNull = elemNull;
                    req.NestedRegistry.RecordCtxUpgradeCandidate(hName,
                        new[]
                        {
                            elemConv
                        },
                        resolve =>
                            CollectionConverter.SynthesizeInPlace(synthesized,
                                hName,
                                capSrc,
                                capElem,
                                capTgt,
                                capShape,
                                resolve(elemConv),
                                capNull));
                }

                resolved = true;

                return true;
            }

            return false;
        }

        /// <summary>
        ///     Dictionaries: key and value converted independently, then recombined.
        /// </summary>
        /// <returns>
        ///     <c>true</c> when this arm CLAIMED the pair, in which case <paramref name="resolved" /> carries the
        ///     verdict it reached; <c>false</c> when the pair is not this arm's to answer and the chain continues.
        /// </returns>

        private static bool HandleDictionaryConversion(
            ConversionRequest req,
            List<DiagnosticInfo> diagnostics,
            Dictionary<string, SynthesizedMethod> synthesized,
            ref string? converterMethod,
            ref bool converterNeedsCtx,
            out bool resolved)
        {
            resolved = false;

            if (DictionaryConverter.TryResolve(req.SrcType,
                    req.TgtType,
                    out var srcKey,
                    out var srcVal,
                    out var tgtKey,
                    out var tgtVal,
                    out var dictHasCount,
                    out var dictTargetKind))
            {
                // A3: determine effective null-as-null for the OUTER dict helper based on target nullability.
                // If req.NullAsNull=true but the target dict type is non-nullable, fall back to AsEmpty
                // to prevent CS8601 (nullable helper assigned to non-nullable field).
                var dictEffectiveNullAsNull = req.NullAsNull && IsNullableReferenceType(req.TgtType);

                // A1: propagate req.NullAsNull to nested key/value converters so nullable elements
                // (e.g. the value type List<int>? in Dictionary<string, List<int>?>) generate
                // helpers that preserve null instead of silently mapping to empty.
                if (!TryResolveConversion(req.Compilation,
                        srcKey,
                        tgtKey,
                        null,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        synthesized,
                        req.NullStrategy,
                        req.Location,
                        req.TargetName,
                        diagnostics,
                        out var keyConv,
                        out var keyNull,
                        out var keyNeedsCtx,
                        req.AutoNest,
                        req.NestedRegistry,
                        req.NullAsNull,
                        req.IsPreserve,
                        isSetNull: req.IsSetNull,
                        implicitConversions: req.ImplicitConversions,
                        reservedConverters: req.ReservedConverters))
                {
                    resolved = false;
                    return true;
                }

                if (!TryResolveConversion(req.Compilation,
                        srcVal,
                        tgtVal,
                        null,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        synthesized,
                        req.NullStrategy,
                        req.Location,
                        req.TargetName,
                        diagnostics,
                        out var valConv,
                        out var valNull,
                        out var valNeedsCtx,
                        req.AutoNest,
                        req.NestedRegistry,
                        req.NullAsNull,
                        req.IsPreserve,
                        isSetNull: req.IsSetNull,
                        implicitConversions: req.ImplicitConversions,
                        reservedConverters: req.ReservedConverters))
                {
                    resolved = false;
                    return true;
                }

                // Preserve OR SetNull: if the key/value converter is an auto-nested object mapper, force it RC
                // so it carries (ctx, depth) and the dict helper threads the shared context into it — this is
                // what lets a cycle routed through a dictionary value break (SetNull) or depth-cap.
                if ((req.IsPreserve || req.IsSetNull) && req.NestedRegistry is not null)
                {
                    if (keyConv is not null && GeneratedNames.IsObjectMap(keyConv))
                    {
                        req.NestedRegistry.ForceRecursionCapable(keyConv);
                        keyNeedsCtx = true;
                    }

                    if (valConv is not null && GeneratedNames.IsObjectMap(valConv))
                    {
                        req.NestedRegistry.ForceRecursionCapable(valConv);
                        valNeedsCtx = true;
                    }
                }

                converterMethod = DictionaryConverter.Synthesize(synthesized,
                    req.SrcType,
                    tgtKey,
                    tgtVal,
                    dictHasCount,
                    dictTargetKind,
                    keyConv,
                    keyNull,
                    valConv,
                    valNull,
                    dictEffectiveNullAsNull,
                    req.IsPreserve,
                    keyNeedsCtx,
                    valNeedsCtx);
                // The dict helper threads (ctx, depth) when it register-before-fills (Preserve mutable) OR a
                // key/value converter is recursion-capable (Preserve, or None/SetNull self-referential value).
                var isMutableDict = dictTargetKind != DictionaryConverter.DictTargetKind.ImmutableDictionary && dictTargetKind != DictionaryConverter.DictTargetKind.IImmutableDictionary;
                converterNeedsCtx = (req.IsPreserve && isMutableDict) || keyNeedsCtx || valNeedsCtx;

                // None+Throw: a key/value resolved to a PUBLIC declared method. Record a re-synthesis
                // closure so the post-pass can upgrade this dict helper if that method is self-recursive.
                if (!req.IsPreserve && !req.IsSetNull && req.NestedRegistry is not null)
                {
                    var keyIsPublicObj = keyConv is not null &&
                                         !keyNeedsCtx &&
                                         !GeneratedNames.IsAnySynthesized(keyConv) &&
                                         tgtKey is INamedTypeSymbol tk &&
                                         IsMappableObjectPair(req.Compilation, srcKey, tk);
                    var valIsPublicObj = valConv is not null &&
                                         !valNeedsCtx &&
                                         !GeneratedNames.IsAnySynthesized(valConv) &&
                                         tgtVal is INamedTypeSymbol tv &&
                                         IsMappableObjectPair(req.Compilation, srcVal, tv);
                    if (keyIsPublicObj || valIsPublicObj)
                    {
                        var hName = converterMethod;
                        var elems = new List<string>();
                        if (keyIsPublicObj)
                        {
                            elems.Add(keyConv!);
                        }

                        if (valIsPublicObj)
                        {
                            elems.Add(valConv!);
                        }

                        var cSrc = req.SrcType;
                        var cTk = tgtKey;
                        var cTv = tgtVal;
                        var cHas = dictHasCount;
                        var cKind = dictTargetKind;
                        var cKeyConv = keyConv;
                        var cKeyNull = keyNull;
                        var cValConv = valConv;
                        var cValNull = valNull;
                        var cNullAsNull = dictEffectiveNullAsNull;
                        req.NestedRegistry.RecordCtxUpgradeCandidate(hName,
                            elems.ToArray(),
                            resolve =>
                            {
                                var nk = cKeyConv;
                                var nkCtx = false;
                                if (keyIsPublicObj)
                                {
                                    var r = resolve(cKeyConv!);
                                    if (!string.Equals(r, cKeyConv, StringComparison.Ordinal))
                                    {
                                        nk = r;
                                        nkCtx = true;
                                    }
                                }

                                var nv = cValConv;
                                var nvCtx = false;
                                if (valIsPublicObj)
                                {
                                    var r = resolve(cValConv!);
                                    if (!string.Equals(r, cValConv, StringComparison.Ordinal))
                                    {
                                        nv = r;
                                        nvCtx = true;
                                    }
                                }

                                DictionaryConverter.SynthesizeInPlace(synthesized,
                                    hName,
                                    cSrc,
                                    cTk,
                                    cTv,
                                    cHas,
                                    cKind,
                                    nk,
                                    cKeyNull,
                                    nkCtx,
                                    nv,
                                    cValNull,
                                    nvCtx,
                                    cNullAsNull);
                            });
                    }
                }

                resolved = true;

                return true;
            }

            return false;
        }
    }
}
