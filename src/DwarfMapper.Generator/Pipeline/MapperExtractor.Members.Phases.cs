// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The resolution passes lifted out of <c>ResolveMembers</c>.
    /// </summary>
    /// <remarks>
    ///     <c>ResolveMembers</c> carried no seam comments, so its phases were DERIVED rather than read: every
    ///     top-level construct in the body was listed with its size, and each candidate was then braced in place
    ///     and built. A clean build means the span declares nothing the rest of the method needs; CS0103 names
    ///     what it shares. Only spans the compiler certified that way appear here.
    ///     See <c>Issues/round27/SEAM-STAGE.md</c> for the derivation and its measurements.
    /// </remarks>
    internal static partial class MapperExtractor
    {

        // [DwarfMapper(SkipNullSourceMembers = true)]: a null source member must keep the destination's
        // default rather than overwrite it. Mark each simple, nullable-source, post-construction-settable
        // member so the emitter guards it with `if (src.X is not null) dst.X = …;`. Non-nullable value-type
        // sources (never null) and required/init-only/read-only targets (cannot be deferred) are left as-is.
        /// <remarks>
        ///     Certified separable before it moved: bracing the span in place and building reported no escaping
        ///     local, so the pass declares nothing the rest of <c>ResolveMembers</c> reads. It runs LAST among the
        ///     passes for a reason the code cannot state on its own — every earlier pass may still add a member,
        ///     and this one has to see the finished list.
        /// </remarks>
        private static void ApplySkipNullSourceMembers(
            MemberRequest req,
            MemberLookups lookups,
            MemberAccumulators acc){
            if (req.Options.SkipNullSourceMembers && acc.Result.Count > 0)
            {
                var srcTypeByName = new Dictionary<string, ITypeSymbol>(lookups.Comparer);
                foreach (var (sName, sType) in ReadableMembers(req.SourceType, req.Compilation, req.Options.AllowNonPublic))
                    srcTypeByName[sName] = sType;

                var deferrableTargets = new HashSet<string>(StringComparer.Ordinal);
                for (var t = req.TargetType; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                    foreach (var tm in t.GetMembers())
                        if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: false } && !p.IsRequired)
                        {
                            deferrableTargets.Add(p.Name);
                        }
                        else if (tm is IFieldSymbol f && !f.IsReadOnly && !f.IsConst && !f.IsRequired)
                        {
                            deferrableTargets.Add(f.Name);
                        }

                for (var i = 0; i < acc.Result.Count; i++)
                {
                    var m = acc.Result[i];
                    if (string.IsNullOrEmpty(m.SourceName) ||
                        m.SourceName.IndexOf('.') >= 0 ||
                        m.ValueExpression is not null ||
                        m.UnflattenIntermediateFqn is not null ||
                        m.WhenPredicate is not null ||
                        m.SkipIfSourceNull ||
                        !deferrableTargets.Contains(m.TargetName))
                    {
                        continue;
                    }

                    if (srcTypeByName.TryGetValue(m.SourceName, out var st) && (st.IsReferenceType || IsNullableValue(st, out _)))
                        // The emitter now guards this with `if (src.X is not null) dst.X = …;`, so inside that
                        // guard flow analysis already proves non-null: no CS8601, hence no '!' and no DWARF070.
                        // SkipNullSourceMembers IS the fix DWARF070 would have told them to apply.
                    {
                        acc.Result[i] = m with
                        {
                            SkipIfSourceNull = true,
                            NullRefIntoNonNullable = false
                        };
                    }
                }
            }
        }

        /// <summary>
        ///     The source member a <c>[MapValue]</c> for <paramref name="target" /> shadows: the first member of the
        ///     group auto-match would have used — keyed the way THIS pair matches, normalized under
        ///     <c>NameConvention.Flexible</c> and by the pair's comparer otherwise — that <c>[MapIgnoreSource]</c>
        ///     has not disowned. <see langword="null" /> when nothing would have matched or all of it is disowned.
        /// </summary>
        /// <remarks>
        ///     Disowning is tested against the member's REAL name, never the target's spelling: source coverage
        ///     reads <c>[MapIgnoreSource]</c> by real name, so a remedy that silenced DWARF064 under the target's
        ///     spelling would leave the same member unconsumed for DWARF039. Under exact matching the two
        ///     spellings coincide and the distinction costs nothing; under <c>CaseInsensitive</c> or Flexible it
        ///     is the difference between a remedy the reader can follow once and one they must write twice.
        /// </remarks>
        private static string? ShadowedSourceMember(MemberRequest req, MemberLookups lookups, string target)
        {
            if (!lookups.SourceGroups.TryGetValue(lookups.Flexible ? NormalizeName(target) : target, out var group))
            {
                return null;
            }

            foreach (var (name, _) in group)
                if (!(req.IgnoredSourceMembers?.Contains(name) ?? false))
                {
                    return name;
                }

            return null;
        }

        // MAPVALUE: constant / computed values assigned to a destination member (no source). Processed
        // after [MapProperty] (so conflicts are caught) and before AUTO matching. A [MapValue]'d target
        // counts as mapped, suppressing DWARF001. The projection resolver reads the directive in the SAME
        // position for the same reason, through the SAME validation below.
        /// <remarks>
        ///     Its POSITION is the contract, not just its content: after [MapProperty] so a conflict is caught,
        ///     before AUTO so a valued target suppresses DWARF001. Moving the call moves the behaviour.
        /// </remarks>
        private static void ResolveMapValues(
            MemberRequest req,
            MemberLookups lookups,
            MemberAccumulators acc){
            foreach (var mv in req.MapValues ??
                               Array.Empty<(string Target, bool IsConstant, TypedConstant Value,
                                   string? Use, string? ConstLiteral)>())
            {
                var mvTgt = mv.Target;
                if (!TryValidateMapValueTarget(mvTgt,
                        acc.HandledTargets,
                        req.Ignores,
                        name => req.ConsumedCtorParams is not null && req.ConsumedCtorParams.Contains(name),
                        lookups.WritableByName,
                        name => ShadowedSourceMember(req, lookups, name),
                        req.Location,
                        acc.Diagnostics,
                        out var mvTgtType))
                {
                    continue;
                }

                if (mv.IsConstant)
                {
                    string literal;
                    if (mv.ConstLiteral is not null)
                    {
                        literal = mv.ConstLiteral;
                    }
                    else if (!TryFormatConstant(mv.Value, mvTgtType, req.Compilation, out literal, out var why))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueTypeMismatch, req.Location, why));
                        continue;
                    }

                    acc.Result.Add(new MemberMap(mvTgt, "", ValueExpression: literal));
                }
                else if (mv.Use is not null)
                {
                    var provider = (req.ValueProviders ?? Array.Empty<(string Name, ITypeSymbol ReturnType)>())
                        .FirstOrDefault(p => StringComparer.Ordinal.Equals(p.Name, mv.Use));
                    if (provider.Name is null || !HasImplicitConversion(req.Compilation, provider.ReturnType, mvTgtType))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueUseInvalid,
                            req.Location,
                            $"[MapValue(Use = \"{mv.Use}\")] for '{mvTgt}' must name a parameterless method whose return type is assignable to '{mvTgtType.ToDisplayString()}'"));
                        continue;
                    }

                    // Escaped HERE rather than at the emitter, because what goes into the model is a finished C#
                    // EXPRESSION that MapEmitter appends verbatim — the model-transitive shape the b888cc3 defect
                    // had. `Use` names a method the consumer declared, so it may legally be `@class`, and the
                    // attribute carries the bare `class` that ISymbol.Name would also have given.
                    acc.Result.Add(new MemberMap(mvTgt, "", ValueExpression: Identifiers.Escape(mv.Use) + "()"));
                }
                else
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid,
                        req.Location,
                        $"[MapValue] for '{mvTgt}' provides neither a constant value nor Use="));
                }
            }
        }

        /// <summary>
        ///     [MapProperty]: every explicitly named source-to-destination pair, validated and resolved. Runs
        ///     FIRST, so an explicit choice wins over anything auto-matching would have inferred.
        /// </summary>
        /// <remarks>
        ///     The two sets declared at the top arrived here from the prologue, where they sat among the shared
        ///     lookups without being shared: each is read by this pass alone. A local declared just above a span
        ///     reads as shared state to any tool that reasons about spans, so every one was checked individually
        ///     rather than trusted -- these two moved, the rest are genuinely shared and became bundle fields.
        /// </remarks>
        private static void ResolveExplicitMaps(
            MemberRequest req,
            MemberLookups lookups,
            MemberAccumulators acc)
        {
            // Intermediate roots already opened by an unflatten leaf — additional leaves into the same root
            // are allowed (City + Street → Address); only a DIRECT mapping of the root conflicts (DWARF046).
            var unflattenRoots = new HashSet<string>(StringComparer.Ordinal);
            var explicitSeen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (srcName, tgtName, useMethod) in req.ExplicitMaps)
            {
                if (!explicitSeen.Add(tgtName))
                {
                    // More than one [MapProperty] for the same destination.
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, req.Location, tgtName));
                    continue;
                }

                // Unflatten: a dotted TARGET path (e.g. "Address.City") assigns the leaf through a acc.Synthesized
                // intermediate (single level). The intermediate must be a writable class with a public
                // parameterless constructor; it is instantiated post-construction by the emitter.
                if (tgtName.IndexOf('.') >= 0)
                {
                    // When / NullSubstitute are not supported on an unflatten (dotted) target — the unflatten
                    // path does not read these extras, so catch the unsupported combination loudly rather than
                    // silently dropping the annotation.
                    if (lookups.ExtrasByTarget.TryGetValue(tgtName, out var uex) && (uex.When is not null || uex.HasNullSub))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid,
                            req.Location,
                            $"[MapProperty(When/NullSubstitute)] is not supported on the unflatten target '{tgtName}'; apply it to a direct member"));
                        continue;
                    }

                    ResolveUnflattenTarget(
                        req.SourceType,
                        srcName,
                        tgtName,
                        useMethod,
                        req.Compilation,
                        req.Location,
                        acc.Diagnostics,
                        acc.HandledTargets,
                        unflattenRoots,
                        lookups.WritableByName,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        acc.Synthesized,
                        req.NullStrategy,
                        req.Options.AutoNest,
                        req.NestedRegistry,
                        req.Options.NullAsNull,
                        req.Options.IsPreserve,
                        req.Options.IsSetNull,
                        req.Options.ImplicitConversions,
                        req.Options.AllowNonPublic,
                        acc.Result);
                    continue;
                }

                acc.HandledTargets.Add(tgtName);

                // If this explicit mapping targets a constructor parameter (already consumed), skip it here
                // UNLESS the member is `required` and the ctor lacks [SetsRequiredMembers] — in that case
                // the member must also appear in the object initializer to satisfy CS9035.
                if (req.ConsumedCtorParams is not null &&
                    req.ConsumedCtorParams.Contains(tgtName) &&
                    (req.RequiredMustInitialize is null ||
                     !req.RequiredMustInitialize.Contains(tgtName)))
                {
                    continue;
                }

                if (req.Ignores.Contains(tgtName))
                {
                    // Contradictory: [MapIgnore] and [MapProperty] target the same member.
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, req.Location, tgtName));
                    continue;
                }

                if (!lookups.WritableByName.TryGetValue(tgtName, out var tgtType))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, req.Location, tgtName));
                    continue;
                }

                ITypeSymbol? srcMatch;
                if (srcName.IndexOf('.') >= 0)
                {
                    // Deep source path, e.g. "Customer.Name" → resolve hop-by-hop (member names never contain
                    // dots, so this is unambiguous). The leaf type drives the conversion; the dotted SourceName
                    // is emitted verbatim as `s.Customer.Name` (a null interior hop throws at runtime — DWARF044
                    // warns when that is possible).
                    if (!TryResolveSourcePath(req.SourceType,
                            srcName,
                            req.Compilation,
                            req.Options.AllowNonPublic,
                            out srcMatch,
                            out var nullableHop,
                            out var badSegment))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathSegmentNotFound,
                            req.Location,
                            $"[MapProperty] source path '{srcName}' has no member '{badSegment}'"));
                        continue;
                    }

                    if (nullableHop)
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathNullableHop,
                            req.Location,
                            $"[MapProperty] source path '{srcName}' traverses a nullable member; a null interior value throws at runtime"));
                    }
                }
                else
                {
                    srcMatch = ReadableMembers(req.SourceType, req.Compilation, req.Options.AllowNonPublic)
                        .Where(m => StringComparer.Ordinal.Equals(m.Name, srcName))
                        .Select(m => (ITypeSymbol?)m.Type)
                        .FirstOrDefault();
                }

                if (srcMatch is null)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, req.Location, srcName));
                    continue;
                }

                // B17: the acc.Synthesized-helper table AS IT STOOD before this member's conversion was resolved.
                // A valid StringFormat REPLACES whatever the resolution below produces, and the replaced helper
                // used to stay in the table and be emitted as a `private static` nothing calls. Snapshotted here
                // rather than inside the branch because TryResolveConversion is what adds it, and taken only when
                // this member actually carries a format — no allocation on the ordinary path.
                var synthBeforeConversion = req.StringFormats is not null && req.StringFormats.ContainsKey(tgtName)
                    ? new HashSet<string>(acc.Synthesized.Keys, StringComparer.Ordinal)
                    : null;

                // The share, on a [MapProperty] rename. Skipped when the caller named a converter or a format:
                // both TRANSFORM the value, and a share performs no conversion at all, so honouring the share
                // over them would drop the transform silently. See TryPlanShare.
                var transformsTheValue = useMethod is not null ||
                                         (req.StringFormats is not null && req.StringFormats.ContainsKey(tgtName));

                // ...and standing aside SILENTLY is the other half of the same mistake. A caller who wrote both
                // [MapShare] and Use= wrote one directive that does nothing, which is precisely the "accepted
                // it, changed nothing, said nothing" shape this round exists to remove. Report which one won.
                if (transformsTheValue && req.ShareMembers.Contains(tgtName))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ShareInvalid,
                        req.Location,
                        $"[MapShare] member '{tgtName}' also carries a [MapProperty] that TRANSFORMS its value " +
                        (useMethod is not null ? "(Use=)" : "(StringFormat=)") +
                        "; a share performs no conversion at all, so the two cannot both apply — remove one of them",
                        MemberName: tgtName));
                }

                if (!transformsTheValue &&
                    TryPlanShare(srcMatch,
                        tgtType,
                        req.Options.NullAsNull,
                        req.ShareMembers.Contains(tgtName),
                        req.Location,
                        tgtName,
                        acc.Diagnostics,
                        out var explicitSharePlan))
                {
                    acc.Result.Add(new MemberMap(tgtName,
                        srcName,
                        ShareEmptyFallback: explicitSharePlan.EmptyFallback,
                        ShareGuardsDefault: explicitSharePlan.GuardsDefault));
                    continue;
                }

                if (TryResolveConversion(req.Compilation,
                        srcMatch,
                        tgtType,
                        useMethod,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        acc.Synthesized,
                        req.NullStrategy,
                        req.Location,
                        tgtName,
                        acc.Diagnostics,
                        out var conv,
                        out var nullH,
                        out var convNeedsCtx,
                        req.Options.AutoNest,
                        req.NestedRegistry,
                        req.Options.NullAsNull,
                        req.Options.IsPreserve,
                        isSetNull: req.Options.IsSetNull,
                        implicitConversions: req.Options.ImplicitConversions,
                        reservedConverters: lookups.ReservedConverters))
                {
                    // [MapProperty(StringFormat="…")]: replace the resolved converter with a format-aware
                    // src.ToString(format, InvariantCulture). Only valid for an IFormattable source into a string
                    // target, and not alongside Use= (which already owns the transform). An invalid use reports
                    // DWARF073 (an Error — so no output is emitted — hence leaving the default converter in place
                    // rather than skipping the member avoids a spurious second diagnostic).
                    if (req.StringFormats is not null && req.StringFormats.TryGetValue(tgtName, out var fmt))
                    {
                        if (tgtType.SpecialType != SpecialType.System_String)
                        {
                            acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StringFormatInvalid,
                                req.Location,
                                $"[MapProperty(StringFormat=\"{fmt}\")] for '{tgtName}' needs a string destination, but it is '{tgtType.ToDisplayString()}'"));
                        }
                        else if (useMethod is not null)
                        {
                            acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StringFormatInvalid,
                                req.Location,
                                $"[MapProperty(StringFormat=…)] for '{tgtName}' cannot be combined with Use= — the converter already produces the value"));
                        }
                        else if (!ParsableConverter.SupportsStringFormat(srcMatch))
                        {
                            acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StringFormatInvalid,
                                req.Location,
                                $"[MapProperty(StringFormat=…)] for '{tgtName}' needs a source implementing IFormattable; '{srcMatch.ToDisplayString()}' does not"));
                        }
                        else
                        {
                            conv = ParsableConverter.AddFormattedToString(acc.Synthesized, srcMatch, fmt);
                            // B17: drop what the resolution above acc.Synthesized for THIS member — the format-aware
                            // converter has replaced it, so nothing references it and it was being emitted as an
                            // unused `private static` beside every formatted one. Only keys this call ADDED are
                            // removed: a helper an earlier member already needed is in the snapshot and survives,
                            // and a LATER member needing the same conversion re-adds it, because every
                            // synthesizer is add-if-absent and returns the name either way. The formatted helper
                            // itself is admitted to the snapshot first so a format whose name collides with a
                            // just-added key cannot delete itself.
                            if (synthBeforeConversion is not null)
                            {
                                synthBeforeConversion.Add(conv);
                                foreach (var orphan in acc.Synthesized.Keys
                                             .Where(k => !synthBeforeConversion.Contains(k)).ToList())
                                    acc.Synthesized.Remove(orphan);
                            }
                        }
                    }

                    // Phase 8: NullSubstitute (direct-assignable only) and When (guarded assignment).
                    string? nullSubLit = null;
                    string? whenPred = null;
                    if (lookups.ExtrasByTarget.TryGetValue(tgtName, out var ex))
                    {
                        if (ex.HasNullSub)
                        {
                            if (conv is not null)
                            {
                                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NullSubstituteInvalid,
                                    req.Location,
                                    $"[MapProperty(NullSubstitute=)] for '{tgtName}' is not supported together with a converter (Use=)"));
                            }
                            else if (ex.NullSubLiteral is not null)
                            {
                                nullSubLit = ex.NullSubLiteral;
                            }
                            else if (!TryFormatConstant(ex.NullSub, tgtType, req.Compilation, out var lit, out var why))
                            {
                                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NullSubstituteInvalid,
                                    req.Location,
                                    why));
                            }
                            else
                            {
                                nullSubLit = lit;
                            }
                        }

                        if (ex.When is not null)
                        {
                            var ok = false;
                            foreach (var m in req.AllMethods)
                                if (StringComparer.Ordinal.Equals(m.Name, ex.When) && m.ReturnType.SpecialType == SpecialType.System_Boolean && HasImplicitConversion(req.Compilation, req.SourceType, m.ParamType))
                                {
                                    ok = true;
                                    break;
                                }

                            if (!ok)
                            {
                                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.WhenPredicateInvalid,
                                    req.Location,
                                    $"[MapProperty(When = \"{ex.When}\")] for '{tgtName}' must name a bool-returning method that takes the source"));
                            }
                            else
                            {
                                whenPred = ex.When;
                                // Item 14 (DWARF066): a When guard on a non-nullable reference target leaves it at
                                // its default (null) when the predicate is false — a latent null in a non-null
                                // contract. Restricted to non-nullable reference targets; Info to limit false
                                // positives (a member with its own default initializer is fine).
                                if (tgtType.IsReferenceType && tgtType.NullableAnnotation != NullableAnnotation.Annotated)
                                {
                                    acc.Diagnostics.Add(new DiagnosticInfo(
                                        DiagnosticDescriptors.WhenLeavesNonNullableDefault,
                                        req.Location,
                                        tgtName));
                                }
                            }
                        }
                    }

                    acc.Result.Add(new MemberMap(tgtName,
                        srcName,
                        conv,
                        nullH,
                        convNeedsCtx,
                        SourceMayBeNullRef(srcMatch),
                        NullSubstituteLiteral: nullSubLit,
                        WhenPredicate: whenPred,
                        // NullSubstitute already coalesces the null away (`src.X ?? literal`), so the assignment
                        // is provably non-null and needs neither the '!' nor DWARF070.
                        NullRefIntoNonNullable: nullSubLit is null && IsDirectNullRefAssign(conv, nullH, srcMatch, tgtType),
                        // Same nested nullable→non-nullable forgiveness as the auto-match path; skipped when
                        // NullSubstitute already handled the null.
                        // Round 29 T2.9, on every converter-bearing member site: the converter's own RETURN
                        // annotation. Skipped when NullSubstitute already coalesced the null away, for the same
                        // reason the argument half is.
                        ConverterReturnIsNullableRef: nullSubLit is null &&
                                                      ForgiveConverterNullableReturn(conv,
                                                          tgtType,
                                                          req.AutoCandidates,
                                                          req.AllMethods,
                                                          tgtName,
                                                          req.Location,
                                                          acc.Diagnostics),
                        ConverterParamIsNonNullableRef: nullSubLit is null &&
                                                        ForgiveNestedNullableArg(conv,
                                                            srcMatch,
                                                            tgtType,
                                                            req.AutoCandidates,
                                                            req.AllMethods,
                                                            srcName,
                                                            req.Location,
                                                            acc.Diagnostics)));
                }
            }
        }

        /// <summary>
        ///     AUTO: every writable destination not already claimed, matched to a source member by name under the
        ///     configured comparer. Runs LAST of the three matching passes, so it only ever sees what [MapProperty]
        ///     and [MapValue] left behind -- which is what makes "already handled" a sufficient skip condition.
        /// </summary>
        /// <remarks>
        ///     The span deliberately starts four lines above the loop, at the `targets` list it consumes. That list
        ///     is built from the destination type alone, so pulling it in costs nothing and the pass owns its own
        ///     input instead of taking it as a parameter. Re-probed at the wider boundary to confirm the span is
        ///     still separable before it moved.
        /// </remarks>
        private static void ResolveAutoMatchedMembers(
            MemberRequest req,
            MemberLookups lookups,
            MemberAccumulators acc)
        {
            var targets = WritableMembers(req.TargetType, req.Compilation, req.Options.AllowNonPublic)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var target in targets)
            {
                // Skip members already consumed as constructor parameters (positional record members appear
                // as both ctor params AND init properties — must not double-assign).
                // EXCEPTION: `required` members whose ctor lacks [SetsRequiredMembers] must also be set in
                // the object initializer (CS9035), so do NOT skip them.
                if (req.ConsumedCtorParams is not null &&
                    req.ConsumedCtorParams.Contains(target.Name) &&
                    (req.RequiredMustInitialize is null ||
                     !req.RequiredMustInitialize.Contains(target.Name)))
                {
                    // Under a [MapConstructor] factory the skip above is not "the constructor assigns it" — it is
                    // "nobody assigns it". The factory owns construction, so an init-only/required member keeps
                    // whatever the factory chose, and a matching SOURCE value is silently discarded.
                    //
                    // Silent is the whole problem: the build is green and the member simply holds the wrong
                    // value. Round 18 hit this twice in one codebase — once losing an entity's Identifier through
                    // a `.Empty` factory that minted a fresh Guid, and once in a map that "compiled green but
                    // silently dropped Identifier, TotalArguments and IsCoreCommand", which was backed out on the
                    // principle that lossy-but-green is worse than undone.
                    //
                    // Only reported when a source member actually WOULD have supplied a value — a member nothing
                    // maps to loses nothing, and warning about it would be noise on every record type.
                    if (req.FactoryExcludedMembers is not null && req.FactoryExcludedMembers.Contains(target.Name, StringComparer.Ordinal) && !req.Ignores.Contains(target.Name) && lookups.SourceGroups.ContainsKey(lookups.Flexible ? NormalizeName(target.Name) : target.Name))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.FactoryDropsMember,
                            req.Location,
                            target.Name,
                            MemberName: target.Name));
                    }

                    continue;
                }

                if (acc.HandledTargets.Contains(target.Name))
                {
                    continue;
                }

                if (req.Ignores.Contains(target.Name))
                {
                    // Ignoring a `required` member does not produce a mapper that skips it — it produces an
                    // object initializer that omits it, which is CS9035 from GENERATED code. The consumer then
                    // reads a raw compiler error about a file they did not write, with nothing pointing back at
                    // the [MapIgnore] that caused it.
                    //
                    // This is the single most-repeated friction point of the Round-18 migration: three separate
                    // conversions hit it independently and each reinvented the same workaround, because
                    // AutoMapper's expression trees bypassed the compile-time rule entirely and simply left the
                    // member null. `.Ignore()` on a required member is therefore common in migrating code.
                    if (!req.RequiredMembersAlreadySatisfied && (req.ConsumedCtorParams is null || !req.ConsumedCtorParams.Contains(target.Name)) && IsRequiredMember(req.TargetType, target.Name))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.IgnoredRequiredMember,
                            req.Location,
                            target.Name,
                            MemberName: target.Name));
                    }

                    continue;
                }

                // Phase 5: an additional parameter matching this target by name wins over a by-name source
                // member. Emitted as the parameter name directly (or a scalar conversion of it). Converters
                // that need recursion context are not used here (extra params are not propagated to nesting).
                if (req.ExtraParams is not null)
                {
                    // Extra parameters match destinations case-insensitively (e.g. param `tenant` → `Tenant`),
                    // independent of the mapper's member-matching case sensitivity.
                    (string Name, ITypeSymbol Type) ep = default;
                    foreach (var cand in req.ExtraParams)
                        if (StringComparer.OrdinalIgnoreCase.Equals(cand.Name, target.Name))
                        {
                            ep = cand;
                            break;
                        }

                    if (ep.Name is not null &&
                        TryResolveConversion(req.Compilation,
                            ep.Type!,
                            target.Type,
                            null,
                            req.AllMethods,
                            req.AutoCandidates,
                            req.EnumPolicy,
                            acc.Synthesized,
                            req.NullStrategy,
                            req.Location,
                            target.Name,
                            acc.Diagnostics,
                            out var epConv,
                            out var epNull,
                            out var epNeedsCtx,
                            req.Options.AutoNest,
                            req.NestedRegistry,
                            req.Options.NullAsNull,
                            req.Options.IsPreserve,
                            isSetNull: req.Options.IsSetNull,
                            implicitConversions: req.Options.ImplicitConversions,
                            reservedConverters: lookups.ReservedConverters) &&
                        !epNeedsCtx)
                    {
                        // The extra parameter is carried as a source ACCESS, not as a finished ValueExpression:
                        // ValueExpression short-circuits the emitter before any null handling is read, which is
                        // how this site came to emit `Count = count` for `int? -> int` (CS0266, a compile ERROR
                        // in the consumer's .g.cs), `ToDto(inner)` for `Child? -> ChildDto` (CS8604) and
                        // `Inner = inner` for `Child? -> Child` (CS8601). Its nullability metadata is resolved
                        // by exactly the helpers every other member edge uses, so the four emitters keep
                        // answering the extra parameter the same way they answer a source member — the parameter
                        // is just where the value is read FROM.
                        acc.Result.Add(new MemberMap(target.Name,
                            "",
                            epConv,
                            epNull,
                            false, // !epNeedsCtx is in the guard above: an extra parameter never threads (ctx, depth).
                            SourceMayBeNullRef(ep.Type!),
                            NullRefIntoNonNullable: IsDirectNullRefAssign(epConv, epNull, ep.Type!, target.Type),
                            ConverterReturnIsNullableRef: ForgiveConverterNullableReturn(epConv,
                                target.Type,
                                req.AutoCandidates,
                                req.AllMethods,
                                target.Name,
                                req.Location,
                                acc.Diagnostics),
                            ConverterParamIsNonNullableRef: ForgiveNestedNullableArg(epConv,
                                ep.Type!,
                                target.Type,
                                req.AutoCandidates,
                                req.AllMethods,
                                ep.Name,
                                req.Location,
                                acc.Diagnostics,
                                NullSourceKind.MappingParameter),
                            // Escaped for the same reason the signature fragment is: this string is emitted as
                            // C# (`Class = @class`), so a parameter the user spelled `@class` must keep its `@`.
                            // It is also what DWARF070 and the ThrowIfNull message name the parameter by, which
                            // is why they read '@class' rather than 'class' for that (rare) spelling.
                            SourceAccessExpression: Identifiers.Escape(ep.Name)));
                        acc.HandledTargets.Add(target.Name);
                        acc.ConsumedExtraParams.Add(ep.Name);
                        continue;
                    }
                }

                if (!lookups.SourceGroups.TryGetValue(lookups.Flexible ? NormalizeName(target.Name) : target.Name, out var matches))
                {
                    var flatMatches = new List<(string Root, string Leaf, ITypeSymbol LeafType)>();
                    foreach (var fi in lookups.FlattenInfos)
                    foreach (var leaf in fi.Leaves)
                        if (lookups.Comparer.Equals(leaf.Name, target.Name))
                        {
                            flatMatches.Add((fi.Root, leaf.Name, leaf.Type));
                        }

                    if (flatMatches.Count > 1)
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten, req.Location, target.Name));
                        continue;
                    }

                    if (flatMatches.Count == 1)
                    {
                        var fm = flatMatches[0];
                        if (TryResolveConversion(req.Compilation,
                                fm.LeafType,
                                target.Type,
                                null,
                                req.AllMethods,
                                req.AutoCandidates,
                                req.EnumPolicy,
                                acc.Synthesized,
                                req.NullStrategy,
                                req.Location,
                                target.Name,
                                acc.Diagnostics,
                                out var fconv,
                                out var fnull,
                                out var fneedsCtx,
                                req.Options.AutoNest,
                                req.NestedRegistry,
                                req.Options.NullAsNull,
                                req.Options.IsPreserve,
                                isSetNull: req.Options.IsSetNull,
                                implicitConversions: req.Options.ImplicitConversions,
                                reservedConverters: lookups.ReservedConverters))
                        {
                            acc.Result.Add(new MemberMap(target.Name,
                                fm.Root + "." + fm.Leaf,
                                fconv,
                                fnull,
                                fneedsCtx,
                                SourceMayBeNullRef(fm.LeafType),
                                NullRefIntoNonNullable:
                                IsDirectNullRefAssign(fconv, fnull, fm.LeafType, target.Type),
                                ConverterReturnIsNullableRef: ForgiveConverterNullableReturn(fconv,
                                    target.Type,
                                    req.AutoCandidates,
                                    req.AllMethods,
                                    target.Name,
                                    req.Location,
                                    acc.Diagnostics),
                                ConverterParamIsNonNullableRef: ForgiveNestedNullableArg(fconv,
                                    fm.LeafType,
                                    target.Type,
                                    req.AutoCandidates,
                                    req.AllMethods,
                                    fm.Root + "." + fm.Leaf,
                                    req.Location,
                                    acc.Diagnostics)));
                            // B26: an unguarded `src.Root.Leaf` now exists — this is what DWARF044 warns about.
                            acc.ConsumedFlattenRoots.Add(fm.Root);
                        }

                        continue;
                    }

                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember,
                        req.Location,
                        target.Name,
                        MemberName: target.Name));
                    continue;
                }

                if (matches.Count > 1)
                {
                    acc.Diagnostics.Add(lookups.Flexible
                        ? new DiagnosticInfo(DiagnosticDescriptors.AmbiguousNormalizedMatch,
                            req.Location,
                            $"target '{target.Name}' matches multiple source members under NameConvention.Flexible (" + string.Join(", ", matches.Select(m => m.Name)) + "); disambiguate with [MapProperty]")
                        : new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, req.Location, target.Name));
                    continue;
                }

                var source = matches[0];
                if (req.ReinterpretMembers.Contains(target.Name))
                {
                    // [Reinterpret] lets the caller assert the field CORRESPONDENCE the by-name proof cannot
                    // verify. It does NOT let them assert that the bytes line up, because a mismatched pair does
                    // not fail loudly — MemoryMarshal.Cast<int, long> halves the span length, so the copy fills
                    // half the destination and zeroes the rest. Same-size was always the documented contract
                    // (DWARF022's help text); enforcing it here is where that belongs, rather than in a runtime
                    // guard inside every emitted copy.
                    if (source.Type is IArrayTypeSymbol sa &&
                        target.Type is IArrayTypeSymbol ta &&
                        sa.ElementType.IsUnmanagedType &&
                        ta.ElementType.IsUnmanagedType &&
                        BlittableProof.SameBytesIgnoringNames(sa.ElementType, ta.ElementType))
                    {
                        // DWARF106 (round 29, T0.2c review fix 3). Everywhere else the blit now yields to a
                        // conversion the user wrote; [Reinterpret] is the one place it does not, because it
                        // names THIS member explicitly while an auto-adopted converter is ambient. Honouring
                        // the explicit instruction is right — saying nothing about it is not. The two facts sit
                        // in different files and no other diagnostic relates them, so the bypass is reported,
                        // informationally, naming what is not being called and how to get it called.
                        ReportReinterpretBypass(req, lookups, acc, target.Name, sa.ElementType, ta.ElementType);
                        var blit = CollectionConverter.SynthesizeBlit(acc.Synthesized,
                            source.Type,
                            sa.ElementType,
                            ta.ElementType);
                        acc.Result.Add(new MemberMap(target.Name,
                            source.Name,
                            blit,
                            SourceIsNullableRef: SourceMayBeNullRef(source.Type)));
                    }
                    else
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid,
                            req.Location,
                            target.Name));
                    }

                    continue;
                }

                // Explicit-only (trust boundary): a by-name match must NOT silently auto-wire. This is exactly the
                // mass-assignment surface — the field lines up by name, so it WOULD be copied, and DWARF001 would
                // never notice because the member is "mapped". Refuse it and make the developer decide, so an
                // attacker-controlled same-named field (IsAdmin) cannot over-post onto a protected member. Explicit
                // [MapProperty]/[MapValue]/[MapIgnore] and [Reinterpret] have already been honoured above; only the
                // implicit by-name wire is blocked here.
                if (req.Options.ExplicitOnly)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AutoMatchDisabled,
                        req.Location,
                        target.Name,
                        MemberName: target.Name));
                    continue;
                }

                // [MapShare], and the automatic share the immutability proof authorises. Decided BEFORE the
                // resolver runs, not after: resolving first would synthesize a __DwarfMapColl_* helper that
                // nothing then calls, and the aggregate emitter writes every helper the table holds.
                if (TryPlanShare(source.Type,
                        target.Type,
                        req.Options.NullAsNull,
                        req.ShareMembers.Contains(target.Name),
                        req.Location,
                        target.Name,
                        acc.Diagnostics,
                        out var sharePlan))
                {
                    acc.Result.Add(new MemberMap(target.Name,
                        source.Name,
                        ShareEmptyFallback: sharePlan.EmptyFallback,
                        ShareGuardsDefault: sharePlan.GuardsDefault));
                    continue;
                }

                if (TryResolveConversion(req.Compilation,
                        source.Type,
                        target.Type,
                        null,
                        req.AllMethods,
                        req.AutoCandidates,
                        req.EnumPolicy,
                        acc.Synthesized,
                        req.NullStrategy,
                        req.Location,
                        target.Name,
                        acc.Diagnostics,
                        out var conv,
                        out var nullH,
                        out var needsCtx,
                        req.Options.AutoNest,
                        req.NestedRegistry,
                        req.Options.NullAsNull,
                        req.Options.IsPreserve,
                        isSetNull: req.Options.IsSetNull,
                        implicitConversions: req.Options.ImplicitConversions,
                        reservedConverters: lookups.ReservedConverters))
                {
                    // A nullable-reference source passed into a user-declared converter/map whose parameter is
                    // non-nullable would emit CS8604. This only matters when the null would actually reach a
                    // NON-nullable DESTINATION member: NullRefIntoNonNullableRef gates on exactly that. When the
                    // destination is nullable, a null legitimately propagates (e.g. a linked list's terminal
                    // Next) — no forgiving, no diagnostic, behaviour unchanged. When the destination IS
                    // non-nullable, null-forgive the argument (below, via the emitter) so it compiles, and report
                    // DWARF070 — the same actionable signal the scalar raw-assign path gives against the user's DTO.
                    // Synthesized nested mappers flow through IsSynthesized; a null-tolerant user converter (nullable
                    // param) is excluded by ConverterParamIsNonNullableRef and keeps its null.
                    acc.Result.Add(new MemberMap(target.Name,
                        source.Name,
                        conv,
                        nullH,
                        needsCtx,
                        SourceMayBeNullRef(source.Type),
                        NullRefIntoNonNullable: IsDirectNullRefAssign(conv, nullH, source.Type, target.Type),
                        ConverterReturnIsNullableRef: ForgiveConverterNullableReturn(
                            conv,
                            target.Type,
                            req.AutoCandidates,
                            req.AllMethods,
                            target.Name,
                            req.Location,
                            acc.Diagnostics),
                        ConverterParamIsNonNullableRef: ForgiveNestedNullableArg(
                            conv,
                            source.Type,
                            target.Type,
                            req.AutoCandidates,
                            req.AllMethods,
                            source.Name,
                            req.Location,
                            acc.Diagnostics)));
                }
            }
        }
    }
}
