// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    /// <summary>
    ///     The declaration headers of every type the mapper is nested inside, OUTERMOST FIRST — the chain the
    ///     emitter has to reproduce so the generated half lands in the same containing type as the user's half.
    ///     <para>
    ///     Each containing type must itself be <c>partial</c>: C# only lets a partial type be completed inside
    ///     a partial containing type. That is precisely what DWARF002 already says, so a non-partial outer type
    ///     reports DWARF002 against the outer type — an actionable error instead of the CS0759/CS8795 pair that
    ///     the compiler would otherwise raise from inside generated code.
    ///     </para>
    /// </summary>
    private static List<string> ContainingTypeDeclarations(
        INamedTypeSymbol classSymbol, TypeDeclarationSyntax classSyntax, List<DiagnosticInfo> diagnostics)
    {
        var chain = new List<string>();

        for (var outer = classSymbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            var isPartial = outer.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(t => t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

            if (!isPartial)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MapperNotPartial,
                    LocationInfo.From(classSyntax.Identifier.GetLocation()),
                    outer.Name));
                return new List<string>();
            }

            // The reopened partial declaration must use the SAME type keyword as the original, otherwise the
            // compiler rejects it with CS0261 ("partial declarations must be all classes, all record classes,
            // all structs, all record structs, or all interfaces"). This used to collapse everything that was
            // not a struct to "class", so a mapper nested in a record — or in an interface — emitted
            // `partial class Outer` over a `record Outer` and the generated file simply did not compile.
            var keyword = outer switch
            {
                { TypeKind: TypeKind.Interface } => "interface",
                { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
                { IsRecord: true } => "record",
                { TypeKind: TypeKind.Struct } => "struct",
                _ => "class",
            };
            var accessibility = SyntaxFacts.GetText(outer.DeclaredAccessibility);
            chain.Add($"{accessibility} partial {keyword} {outer.Name}");
        }

        chain.Reverse(); // ContainingType walks inner -> outer; the emitter opens outer -> inner.
        return chain;
    }

    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy, IReadOnlyList<string> flattenRoots, List<string> reinterpretMembers,
        HashSet<string>? consumedCtorParams = null,
        HashSet<string>? requiredMustInitialize = null,
        bool autoNest = false,
        NestedMappingRegistry? nestedRegistry = null,
        bool nullAsNull = false,
        bool isPreserve = false,
        bool isSetNull = false,
        bool implicitConversions = true,
        IReadOnlyList<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>? mapValues = null,
        IReadOnlyList<(string Name, ITypeSymbol ReturnType)>? valueProviders = null,
        IReadOnlyList<(string Name, ITypeSymbol Type)>? extraParams = null,
        int nameConvention = 0,
        IReadOnlyList<(string Target, bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>? mapPropertyExtras = null,
        bool skipNullSourceMembers = false,
        bool allowNonPublic = false,
        bool explicitOnly = false,
        bool ignoreObsolete = false,
        Dictionary<string, string>? stringFormats = null)
    {
        // IgnoreObsoleteMembers: drop [Obsolete] destination members from mapping by folding them into the
        // ignore set — every downstream check (auto-match, read-only-loss, explicit-target validation) already
        // honours `ignores`, so this one addition covers them all. An obsolete member that IS explicitly
        // targeted (by [MapProperty]/[MapValue]) is left OUT of the ignore set, so the developer can opt a
        // specific one back in without tripping the ignore-vs-explicit conflict (DWARF012).
        if (ignoreObsolete)
        {
            var explicitTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var em in explicitMaps) explicitTargets.Add(em.Target);
            if (mapValues is not null)
                foreach (var mv in mapValues)
                    explicitTargets.Add(mv.Target);

            ignores = new HashSet<string>(ignores, StringComparer.Ordinal);
            foreach (var name in ObsoleteMemberNames(targetType))
                if (!explicitTargets.Contains(name))
                    ignores.Add(name);
        }

        var extrasByTarget =
            new Dictionary<string, (bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>(
                StringComparer.Ordinal);
        if (mapPropertyExtras is not null)
            foreach (var e in mapPropertyExtras)
                extrasByTarget[e.Target] = (e.HasNullSub, e.NullSub, e.When, e.NullSubLiteral);
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        // NameConvention.Flexible: match on a normalized key (strip '_', lowercase) so PascalCase/camelCase/
        // snake_case/UPPER_CASE are interchangeable. Auto-match only; explicit/flatten paths stay exact.
        var flexible = nameConvention == 1;

        var sourceGroups = flexible
            ? ReadableMembers(sourceType, compilation, allowNonPublic)
                .GroupBy(m => NormalizeName(m.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal)
            : ReadableMembers(sourceType, compilation, allowNonPublic)
                .GroupBy(m => m.Name, comparer)
                .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var writableByName = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType, compilation, allowNonPublic)) writableByName[m.Name] = m.Type;

        var result = new List<MemberMap>();
        var handledTargets = new HashSet<string>(StringComparer.Ordinal);
        // Intermediate roots already opened by an unflatten leaf — additional leaves into the same root
        // are allowed (City + Street → Address); only a DIRECT mapping of the root conflicts (DWARF046).
        var unflattenRoots = new HashSet<string>(StringComparer.Ordinal);
        // Phase 5: which additional parameters were consumed by a destination (the rest → DWARF047).
        var consumedExtraParams = new HashSet<string>(comparer);

        var comparerForLeaves = comparer; // same comparer used for member matching
        var flattenInfos = new List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves)>();
        foreach (var root in flattenRoots)
        {
            var match = ReadableMembers(sourceType)
                .Where(m => comparerForLeaves.Equals(m.Name, root))
                .Select(m => ((string Name, ITypeSymbol Type)?)m)
                .FirstOrDefault();
            if (match is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }

            var rootType = match.Value.Type;
            // Scalars (string, primitives, enums) are not flattenable roots — flattening their
            // BCL members (e.g. string.Length) is never intended and must not happen silently.
            if (rootType.SpecialType != SpecialType.None || rootType.TypeKind == TypeKind.Enum)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }

            var leaves = ReadableMembers(rootType).ToList();
            if (leaves.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }

            // A [Flatten] over a nullable-reference root emits unguarded `src.Root.Leaf` accesses that NRE
            // at runtime if the root is null. The dotted [MapProperty] path warns DWARF044 for the same
            // hazard; the [Flatten] path must be consistent (loud, never silent).
            if (SourceMayBeNullRef(rootType))
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathNullableHop, location,
                    $"[Flatten] source '{root}' is a nullable reference; a null value throws at runtime when its flattened members are read"));
            flattenInfos.Add((match.Value.Name, leaves));
        }

        // EXPLICIT: [MapProperty] pairs take precedence and are matched by exact name.
        var explicitSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (srcName, tgtName, useMethod) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                // More than one [MapProperty] for the same destination.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            // Unflatten: a dotted TARGET path (e.g. "Address.City") assigns the leaf through a synthesized
            // intermediate (single level). The intermediate must be a writable class with a public
            // parameterless constructor; it is instantiated post-construction by the emitter.
            if (tgtName.IndexOf('.') >= 0)
            {
                // When / NullSubstitute are not supported on an unflatten (dotted) target — the unflatten
                // path does not read these extras, so catch the unsupported combination loudly rather than
                // silently dropping the annotation.
                if (extrasByTarget.TryGetValue(tgtName, out var uex) && (uex.When is not null || uex.HasNullSub))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnflattenInvalid, location,
                        $"[MapProperty(When/NullSubstitute)] is not supported on the unflatten target '{tgtName}'; apply it to a direct member"));
                    continue;
                }

                ResolveUnflattenTarget(
                    sourceType, targetType, srcName, tgtName, useMethod, compilation, location, diagnostics,
                    handledTargets, unflattenRoots, writableByName, allMethods, autoCandidates, enumStrategy,
                    synthesized,
                    nullStrategy, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull, implicitConversions,
                    result);
                continue;
            }

            handledTargets.Add(tgtName);

            // If this explicit mapping targets a constructor parameter (already consumed), skip it here
            // UNLESS the member is `required` and the ctor lacks [SetsRequiredMembers] — in that case
            // the member must also appear in the object initializer to satisfy CS9035.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(tgtName)
                                               && (requiredMustInitialize is null ||
                                                   !requiredMustInitialize.Contains(tgtName)))
                continue;

            if (ignores.Contains(tgtName))
            {
                // Contradictory: [MapIgnore] and [MapProperty] target the same member.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }

            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }

            ITypeSymbol? srcMatch;
            if (srcName.IndexOf('.') >= 0)
            {
                // Deep source path, e.g. "Customer.Name" → resolve hop-by-hop (member names never contain
                // dots, so this is unambiguous). The leaf type drives the conversion; the dotted SourceName
                // is emitted verbatim as `s.Customer.Name` (a null interior hop throws at runtime — DWARF044
                // warns when that is possible).
                if (!TryResolveSourcePath(sourceType, srcName, out srcMatch, out var nullableHop, out var badSegment))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathSegmentNotFound, location,
                        $"[MapProperty] source path '{srcName}' has no member '{badSegment}'"));
                    continue;
                }

                if (nullableHop)
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathNullableHop, location,
                        $"[MapProperty] source path '{srcName}' traverses a nullable member; a null interior value throws at runtime"));
            }
            else
            {
                srcMatch = ReadableMembers(sourceType, compilation, allowNonPublic)
                    .Where(m => StringComparer.Ordinal.Equals(m.Name, srcName))
                    .Select(m => (ITypeSymbol?)m.Type)
                    .FirstOrDefault();
            }

            if (srcMatch is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, tgtName, diagnostics, out var conv,
                    out var nullH, out var convNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve,
                    isSetNull: isSetNull, implicitConversions: implicitConversions))
            {
                // [MapProperty(StringFormat="…")]: replace the resolved converter with a format-aware
                // src.ToString(format, InvariantCulture). Only valid for an IFormattable source into a string
                // target, and not alongside Use= (which already owns the transform). An invalid use reports
                // DWARF073 (an Error — so no output is emitted — hence leaving the default converter in place
                // rather than skipping the member avoids a spurious second diagnostic).
                if (stringFormats is not null && stringFormats.TryGetValue(tgtName, out var fmt))
                {
                    if (tgtType.SpecialType != SpecialType.System_String)
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StringFormatInvalid, location,
                            $"[MapProperty(StringFormat=\"{fmt}\")] for '{tgtName}' needs a string destination, but it is '{tgtType.ToDisplayString()}'"));
                    else if (useMethod is not null)
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StringFormatInvalid, location,
                            $"[MapProperty(StringFormat=…)] for '{tgtName}' cannot be combined with Use= — the converter already produces the value"));
                    else if (!ParsableConverter.SupportsStringFormat(srcMatch))
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.StringFormatInvalid, location,
                            $"[MapProperty(StringFormat=…)] for '{tgtName}' needs a source implementing IFormattable; '{srcMatch.ToDisplayString()}' does not"));
                    else
                        conv = ParsableConverter.AddFormattedToString(synthesized, srcMatch, fmt);
                }

                // Phase 8: NullSubstitute (direct-assignable only) and When (guarded assignment).
                string? nullSubLit = null;
                string? whenPred = null;
                if (extrasByTarget.TryGetValue(tgtName, out var ex))
                {
                    if (ex.HasNullSub)
                    {
                        if (conv is not null)
                            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NullSubstituteInvalid, location,
                                $"[MapProperty(NullSubstitute=)] for '{tgtName}' is not supported together with a converter (Use=)"));
                        else if (ex.NullSubLiteral is not null)
                            nullSubLit = ex.NullSubLiteral;
                        else if (!TryFormatConstant(ex.NullSub, tgtType, compilation, out var lit, out var why))
                            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NullSubstituteInvalid, location,
                                why));
                        else
                            nullSubLit = lit;
                    }

                    if (ex.When is not null)
                    {
                        var ok = false;
                        foreach (var m in allMethods)
                            if (StringComparer.Ordinal.Equals(m.Name, ex.When)
                                && m.ReturnType.SpecialType == SpecialType.System_Boolean
                                && HasImplicitConversion(compilation, sourceType, m.ParamType))
                            {
                                ok = true;
                                break;
                            }

                        if (!ok)
                            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.WhenPredicateInvalid, location,
                                $"[MapProperty(When = \"{ex.When}\")] for '{tgtName}' must name a bool-returning method that takes the source"));
                        else
                        {
                            whenPred = ex.When;
                            // Item 14 (DWARF066): a When guard on a non-nullable reference target leaves it at
                            // its default (null) when the predicate is false — a latent null in a non-null
                            // contract. Restricted to non-nullable reference targets; Info to limit false
                            // positives (a member with its own default initializer is fine).
                            if (tgtType.IsReferenceType && tgtType.NullableAnnotation != NullableAnnotation.Annotated)
                                diagnostics.Add(new DiagnosticInfo(
                                    DiagnosticDescriptors.WhenLeavesNonNullableDefault, location, tgtName));
                        }
                    }
                }

                result.Add(new MemberMap(tgtName, srcName, conv, nullH, convNeedsCtx,
                    SourceMayBeNullRef(srcMatch), NullSubstituteLiteral: nullSubLit, WhenPredicate: whenPred,
                    // NullSubstitute already coalesces the null away (`src.X ?? literal`), so the assignment
                    // is provably non-null and needs neither the '!' nor DWARF070.
                    NullRefIntoNonNullable: nullSubLit is null
                                            && IsDirectNullRefAssign(conv, nullH, srcMatch, tgtType)));
            }
        }

        // MAPVALUE: constant / computed values assigned to a destination member (no source). Processed
        // after [MapProperty] (so conflicts are caught) and before AUTO matching. A [MapValue]'d target
        // counts as mapped, suppressing DWARF001.
        foreach (var mv in mapValues ?? Array.Empty<(string Target, bool IsConstant, TypedConstant Value,
                     string? Use, string? ConstLiteral)>())
        {
            var mvTgt = mv.Target;
            if (!handledTargets.Add(mvTgt))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] target '{mvTgt}' conflicts with another mapping for the same member"));
                continue;
            }

            if (ignores.Contains(mvTgt))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] target '{mvTgt}' is also [MapIgnore]d"));
                continue;
            }

            if (consumedCtorParams is not null && consumedCtorParams.Contains(mvTgt))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] cannot target constructor parameter '{mvTgt}' yet (object-initialized members only)"));
                continue;
            }

            if (mvTgt.IndexOf('.') >= 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] does not support a dotted target path '{mvTgt}'; assign the leaf member directly or use [MapProperty] for unflattening"));
                continue;
            }

            if (!writableByName.TryGetValue(mvTgt, out var mvTgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] target '{mvTgt}' is not a writable destination member"));
                continue;
            }

            // Item 12 (DWARF064): the [MapValue] shadows a real same-named source member that would have
            // auto-matched. The constant/provider silently masks the source data — usually a leftover stub
            // from before the source member existed (DWARF039 source-coverage does not fire here).
            if (sourceGroups.ContainsKey(flexible ? NormalizeName(mvTgt) : mvTgt))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MapValueShadowsSource, location, mvTgt));
            }

            if (mv.IsConstant)
            {
                string literal;
                if (mv.ConstLiteral is not null)
                {
                    literal = mv.ConstLiteral;
                }
                else if (!TryFormatConstant(mv.Value, mvTgtType, compilation, out literal, out var why))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueTypeMismatch, location, why));
                    continue;
                }

                result.Add(new MemberMap(mvTgt, "", ValueExpression: literal));
            }
            else if (mv.Use is not null)
            {
                var provider = (valueProviders ?? Array.Empty<(string Name, ITypeSymbol ReturnType)>())
                    .FirstOrDefault(p => StringComparer.Ordinal.Equals(p.Name, mv.Use));
                if (provider.Name is null || !HasImplicitConversion(compilation, provider.ReturnType, mvTgtType))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueUseInvalid, location,
                        $"[MapValue(Use = \"{mv.Use}\")] for '{mvTgt}' must name a parameterless method whose return type is assignable to '{mvTgtType.ToDisplayString()}'"));
                    continue;
                }

                result.Add(new MemberMap(mvTgt, "", ValueExpression: mv.Use + "()"));
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] for '{mvTgt}' provides neither a constant value nor Use="));
            }
        }

        // AUTO: remaining writable targets matched by name under the comparer.
        var targets = WritableMembers(targetType, compilation, allowNonPublic)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var target in targets)
        {
            // Skip members already consumed as constructor parameters (positional record members appear
            // as both ctor params AND init properties — must not double-assign).
            // EXCEPTION: `required` members whose ctor lacks [SetsRequiredMembers] must also be set in
            // the object initializer (CS9035), so do NOT skip them.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(target.Name)
                                               && (requiredMustInitialize is null ||
                                                   !requiredMustInitialize.Contains(target.Name)))
                continue;

            if (handledTargets.Contains(target.Name) || ignores.Contains(target.Name)) continue;

            // Phase 5: an additional parameter matching this target by name wins over a by-name source
            // member. Emitted as the parameter name directly (or a scalar conversion of it). Converters
            // that need recursion context are not used here (extra params are not propagated to nesting).
            if (extraParams is not null)
            {
                // Extra parameters match destinations case-insensitively (e.g. param `tenant` → `Tenant`),
                // independent of the mapper's member-matching case sensitivity.
                (string Name, ITypeSymbol Type) ep = default;
                foreach (var cand in extraParams)
                    if (StringComparer.OrdinalIgnoreCase.Equals(cand.Name, target.Name))
                    {
                        ep = cand;
                        break;
                    }

                if (ep.Name is not null
                    && TryResolveConversion(compilation, ep.Type!, target.Type, null, allMethods, autoCandidates,
                        enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics,
                        out var epConv, out _, out var epNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve,
                        isSetNull: isSetNull, implicitConversions: implicitConversions)
                    && !epNeedsCtx)
                {
                    var valueExpr = epConv is null ? ep.Name : epConv + "(" + ep.Name + ")";
                    result.Add(new MemberMap(target.Name, "", ValueExpression: valueExpr));
                    handledTargets.Add(target.Name);
                    consumedExtraParams.Add(ep.Name);
                    continue;
                }
            }

            if (!sourceGroups.TryGetValue(flexible ? NormalizeName(target.Name) : target.Name, out var matches))
            {
                var flatMatches = new List<(string Root, string Leaf, ITypeSymbol LeafType)>();
                foreach (var fi in flattenInfos)
                foreach (var leaf in fi.Leaves)
                    if (comparer.Equals(leaf.Name, target.Name))
                        flatMatches.Add((fi.Root, leaf.Name, leaf.Type));

                if (flatMatches.Count > 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten, location, target.Name));
                    continue;
                }

                if (flatMatches.Count == 1)
                {
                    var fm = flatMatches[0];
                    if (TryResolveConversion(compilation, fm.LeafType, target.Type, null, allMethods, autoCandidates,
                            enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var fconv,
                            out var fnull, out var fneedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve,
                            isSetNull: isSetNull, implicitConversions: implicitConversions))
                        result.Add(new MemberMap(target.Name, fm.Root + "." + fm.Leaf, fconv, fnull, fneedsCtx,
                            SourceMayBeNullRef(fm.LeafType),
                            NullRefIntoNonNullable:
                            IsDirectNullRefAssign(fconv, fnull, fm.LeafType, target.Type)));
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name,
                    MemberName: target.Name));
                continue;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(flexible
                    ? new DiagnosticInfo(DiagnosticDescriptors.AmbiguousNormalizedMatch, location,
                        $"target '{target.Name}' matches multiple source members under NameConvention.Flexible ("
                        + string.Join(", ", matches.Select(m => m.Name)) + "); disambiguate with [MapProperty]")
                    : new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, target.Name));
                continue;
            }

            var source = matches[0];
            if (reinterpretMembers.Contains(target.Name))
            {
                if (source.Type is IArrayTypeSymbol sa && target.Type is IArrayTypeSymbol ta
                                                       && sa.ElementType.IsUnmanagedType &&
                                                       ta.ElementType.IsUnmanagedType)
                {
                    var blit = CollectionConverter.SynthesizeBlit(synthesized, source.Type, sa.ElementType,
                        ta.ElementType);
                    result.Add(new MemberMap(target.Name, source.Name, blit,
                        SourceIsNullableRef: SourceMayBeNullRef(source.Type)));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid, location,
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
            if (explicitOnly)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.AutoMatchDisabled, location, target.Name, MemberName: target.Name));
                continue;
            }

            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var conv,
                    out var nullH, out var needsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve,
                    isSetNull: isSetNull, implicitConversions: implicitConversions))
                result.Add(new MemberMap(target.Name, source.Name, conv, nullH, needsCtx,
                    SourceMayBeNullRef(source.Type),
                    NullRefIntoNonNullable: IsDirectNullRefAssign(conv, nullH, source.Type, target.Type)));
        }

        // READ-ONLY destinations with a matching source (silent-loss guard).
        // A read-only member satisfied via a constructor parameter is already mapped — no diagnostic.
        foreach (var readOnly in ReadOnlyMembers(targetType, compilation, allowNonPublic)
                     .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            if (handledTargets.Contains(readOnly.Name) || ignores.Contains(readOnly.Name)) continue;
            // Satisfied via ctor param → not a silent loss.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(readOnly.Name)) continue;
            if (sourceGroups.ContainsKey(readOnly.Name))
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReadOnlyDestinationMember, location,
                    readOnly.Name));
        }

        // A [Reinterpret] name that matches no writable destination member is a typo — never silently ignore it.
        // A [Reinterpret] member that is ALSO in [MapIgnore] is a contradiction — report DWARF012.
        if (reinterpretMembers.Count > 0)
        {
            var writableNames =
                new HashSet<string>(WritableMembers(targetType, compilation, allowNonPublic).Select(m => m.Name),
                    StringComparer.Ordinal);
            foreach (var rm in reinterpretMembers)
                if (ignores.Contains(rm))
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, rm));
                else if (!writableNames.Contains(rm))
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid, location, rm));
        }

        // Phase 5: an additional parameter that matched no destination member is a suggestion (DWARF047).
        if (extraParams is not null)
            foreach (var ep in extraParams)
                if (!consumedExtraParams.Contains(ep.Name))
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnusedMappingParameter, location,
                        $"mapping parameter '{ep.Name}' matched no destination member"));

        // [DwarfMapper(SkipNullSourceMembers = true)]: a null source member must keep the destination's
        // default rather than overwrite it. Mark each simple, nullable-source, post-construction-settable
        // member so the emitter guards it with `if (src.X is not null) dst.X = …;`. Non-nullable value-type
        // sources (never null) and required/init-only/read-only targets (cannot be deferred) are left as-is.
        if (skipNullSourceMembers && result.Count > 0)
        {
            var srcTypeByName = new Dictionary<string, ITypeSymbol>(comparer);
            foreach (var (sName, sType) in ReadableMembers(sourceType, compilation, allowNonPublic))
                srcTypeByName[sName] = sType;

            var deferrableTargets = new HashSet<string>(StringComparer.Ordinal);
            for (var t = targetType; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                foreach (var tm in t.GetMembers())
                    if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: false } && !p.IsRequired)
                        deferrableTargets.Add(p.Name);
                    else if (tm is IFieldSymbol f && !f.IsReadOnly && !f.IsConst && !f.IsRequired)
                        deferrableTargets.Add(f.Name);

            for (var i = 0; i < result.Count; i++)
            {
                var m = result[i];
                if (string.IsNullOrEmpty(m.SourceName) || m.SourceName.IndexOf('.') >= 0
                                                       || m.ValueExpression is not null ||
                                                       m.UnflattenIntermediateFqn is not null
                                                       || m.WhenPredicate is not null || m.SkipIfSourceNull
                                                       || !deferrableTargets.Contains(m.TargetName))
                    continue;

                if (srcTypeByName.TryGetValue(m.SourceName, out var st)
                    && (st.IsReferenceType || IsNullableValue(st, out _)))
                    // The emitter now guards this with `if (src.X is not null) dst.X = …;`, so inside that
                    // guard flow analysis already proves non-null: no CS8601, hence no '!' and no DWARF070.
                    // SkipNullSourceMembers IS the fix DWARF070 would have told them to apply.
                    result[i] = m with { SkipIfSourceNull = true, NullRefIntoNonNullable = false };
            }
        }

        // DWARF070: a nullable reference source raw-assigned into a non-nullable reference target. Reported
        // here, once, after every other pass has had its chance to handle the null (NullSubstitute, a
        // converter, SkipNullSourceMembers), so the diagnostic only fires when the null genuinely survives to
        // the destination. Ordered by target name to keep generator output deterministic.
        foreach (var m in result.Where(m => m.NullRefIntoNonNullable)
                     .OrderBy(m => m.TargetName, StringComparer.Ordinal))
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.NullableRefSourceToNonNullableTarget, location, m.SourceName));

        return result;
    }

    /// <summary>
    ///     For each constructor parameter, find a matching source member and resolve the conversion.
    ///     Every parameter is mandatory — if any fails, DWARF024 is reported and the method returns false.
    /// </summary>
    private static bool ResolveConstructorArguments(
        IMethodSymbol ctor,
        ITypeSymbol sourceType,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        bool caseInsensitive,
        IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy,
        Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy,
        bool autoNest,
        NestedMappingRegistry? nestedRegistry,
        out MemberMap[] ctorArgs,
        out HashSet<string> consumedParams,
        bool nullAsNull = false,
        bool isPreserve = false,
        bool isSetNull = false,
        bool implicitConversions = true)
    {
        // Constructor parameters are matched case-insensitively by default. C# convention is camelCase
        // parameters (`name`) binding PascalCase source/target members (`Name`) — the dominant record /
        // primary-constructor shape — so case-sensitive binding would fail the most common ctor mapping.
        // The class-level CaseInsensitive flag governs property-to-property matching; ctor binding is always
        // insensitive (a genuine case-only collision still surfaces as DWARF010 AmbiguousMatch below).
        _ = caseInsensitive;
        var comparer = StringComparer.OrdinalIgnoreCase;

        // Build explicit-maps index: target (param) name → source name (exact match).
        var explicitForParams = new Dictionary<string, (string Source, string? Use)>(StringComparer.Ordinal);
        foreach (var (srcName, tgtName, use) in explicitMaps) explicitForParams[tgtName] = (srcName, use);

        var readableByName = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var args = new List<MemberMap>();
        // Case-insensitive set for deduplication (positional record param names can differ in case).
        consumedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allOk = true;

        foreach (var param in ctor.Parameters)
        {
            // 1. Check for an explicit [MapProperty(src, paramName)] override.
            if (explicitForParams.TryGetValue(param.Name, out var explicitInfo))
            {
                var srcList = ReadableMembers(sourceType)
                    .Where(m => StringComparer.Ordinal.Equals(m.Name, explicitInfo.Source))
                    .ToList();
                if (srcList.Count == 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location,
                        explicitInfo.Source));
                    allOk = false;
                    continue;
                }

                var srcType = srcList[0].Type;
                if (TryResolveConversion(compilation, srcType, param.Type, explicitInfo.Use,
                        allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy,
                        location, param.Name, diagnostics, out var eConv, out var eNull,
                        out var eNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull,
                        implicitConversions: implicitConversions))
                {
                    args.Add(new MemberMap(param.Name, explicitInfo.Source, eConv, eNull, eNeedsCtx,
                        SourceMayBeNullRef(srcType)));
                    consumedParams.Add(param.Name);
                }
                else
                {
                    allOk = false;
                }

                continue;
            }

            // 2. Auto-match by name under the configured comparer.
            if (!readableByName.TryGetValue(param.Name, out var matches) || matches.Count == 0)
            {
                // No matching source member. If the parameter is OPTIONAL (author-declared default)
                // or a params array, omit it from the emitted call so C# supplies the default /
                // empty array. That honors the type author's intent and is not data loss — only a
                // MANDATORY unmatched parameter breaks completeness (DWARF024).
                if (param.HasExplicitDefaultValue || param.IsParams)
                {
                    // Account for it (positional record params also surface as init/get properties;
                    // marking consumed excludes the matching property from the object-initializer AND
                    // from completeness diagnostics) but do NOT add it to args — the emitted call omits
                    // it so C# supplies the declared default / empty params array.
                    consumedParams.Add(param.Name);
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ConstructorParameterUnmapped,
                    location, param.Name));
                allOk = false;
                continue;
            }

            if (matches.Count > 1)
            {
                // Ambiguous under case-insensitive matching — report as AmbiguousMatch.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, param.Name));
                allOk = false;
                continue;
            }

            var srcMember = matches[0];
            if (TryResolveConversion(compilation, srcMember.Type, param.Type, null,
                    allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy,
                    location, param.Name, diagnostics, out var conv, out var nullH,
                    out var needsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull,
                    implicitConversions: implicitConversions))
            {
                args.Add(new MemberMap(param.Name, srcMember.Name, conv, nullH, needsCtx,
                    SourceMayBeNullRef(srcMember.Type)));
                consumedParams.Add(param.Name);
            }
            else
            {
                allOk = false;
            }
        }

        ctorArgs = args.ToArray();
        return allOk;
    }

    /// <summary>
    ///     Reads [MapDerivedType&lt;TSource,TTarget&gt;] (generic) and
    ///     [MapDerivedType(typeof(TSource),typeof(TTarget))] (non-generic) annotations from a method.
    ///     Returns raw pairs of (srcType, tgtType) INamedTypeSymbol — not yet validated.
    /// </summary>
    private static List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt)> ReadDerivedTypeAttributes(
        IMethodSymbol method, Compilation compilation)
    {
        var result = new List<(INamedTypeSymbol, INamedTypeSymbol)>();
        foreach (var attr in method.GetAttributes())
        {
            var cls = attr.AttributeClass;
            if (cls is null) continue;

            // Generic form: MapDerivedTypeAttribute<TSource, TTarget>
            if (cls.IsGenericType
                && cls.ConstructedFrom?.ToDisplayString().StartsWith(
                    "DwarfMapper.MapDerivedTypeAttribute<", StringComparison.Ordinal) == true
                && cls.TypeArguments.Length == 2
                && cls.TypeArguments[0] is INamedTypeSymbol gSrc
                && cls.TypeArguments[1] is INamedTypeSymbol gTgt)
            {
                result.Add((gSrc, gTgt));
                continue;
            }

            // Non-generic form: [MapDerivedType(typeof(TSource), typeof(TTarget))]
            var fqn = cls.ToDisplayString();
            if (fqn == KnownNames.MapDerivedTypeFqn
                && attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol nSrc
                && attr.ConstructorArguments[1].Value is INamedTypeSymbol nTgt)
                result.Add((nSrc, nTgt));
        }

        return result;
    }

    /// <summary>
    ///     Returns the inheritance depth of <paramref name="type" /> (number of base classes between
    ///     it and System.Object). Interfaces return depth 0.
    /// </summary>
    private static int InheritanceDepth(ITypeSymbol type)
    {
        var depth = 0;
        var current = type.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            depth++;
            current = current.BaseType;
        }

        return depth;
    }

    /// <summary>
    ///     Sorts derived-type arms so that more-derived (more-specific) types appear before
    ///     less-derived ones (most-derived-first).  For class hierarchies, uses
    ///     <see cref="InheritanceDepth" />.  For interface hierarchies (where all depths are 0),
    ///     uses pairwise <see cref="HasImplicitConversion" /> assignability:
    ///     if A is assignable to B (A is more derived / more specific than B), A comes first.
    ///     Stable for unrelated/equal pairs (preserves declaration order).
    /// </summary>
    private static List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)>
        SortArmsMostDerivedFirst(
            List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)> arms,
            Compilation compilation)
    {
        // Assign a derived-order score per pair: a type that is pairwise more specific
        // than every other type gets a higher score.  We use an O(n^2) insertion-sort-style
        // comparison since arm lists are small (typically ≤ 10).
        var indexed = arms.Select((arm, idx) => (arm, idx)).ToList();
        indexed.Sort((a, b) =>
        {
            // Primary sort: pairwise assignability (A more derived than B → A before B)
            var aToB = HasImplicitConversion(compilation, a.arm.Src, b.arm.Src); // A assignable to B
            var bToA = HasImplicitConversion(compilation, b.arm.Src, a.arm.Src); // B assignable to A
            if (aToB && !bToA) return -1; // A is more derived than B → A first
            if (bToA && !aToB) return 1; // B is more derived than A → B first
            // Neither or both assignable: fall back to class-hierarchy depth, then declaration order.
            var depthDiff = InheritanceDepth(b.arm.Src) - InheritanceDepth(a.arm.Src);
            if (depthDiff != 0) return depthDiff;
            return a.idx - b.idx; // stable: preserve original declaration order
        });
        return indexed.Select(x => x.arm).ToList();
    }

    /// <summary>
    ///     DWARF036: detects mutually-unorderable interface or abstract source arms.
    ///     Two arm source types A and B are "ambiguous" when:
    ///     1. Neither HasImplicitConversion(A,B) nor HasImplicitConversion(B,A) — they are unorderable.
    ///     2. At least one of A or B is an interface or abstract class — meaning a concrete type
    ///     could simultaneously satisfy both arms (e.g. class C : IFoo, IBar).
    ///     Rationale: if both types are concrete (non-abstract classes), a concrete runtime instance
    ///     can match at most ONE arm by TypeKind/IsAbstract rules (its exact runtime type is one class),
    ///     so unrelated concrete-vs-concrete arms are not ambiguous in practice.
    ///     Fires per-pair so multiple ambiguous pairings each produce a separate diagnostic.
    /// </summary>
    private static void DetectAmbiguousInterfaceArms(
        List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)> arms,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics)
    {
        for (var i = 0; i < arms.Count; i++)
        for (var j = i + 1; j < arms.Count; j++)
        {
            var a = arms[i].Src;
            var b = arms[j].Src;

            // Check orderability: if either is assignable to the other the sort gives a stable order.
            var aToB = HasImplicitConversion(compilation, a, b);
            var bToA = HasImplicitConversion(compilation, b, a);
            if (aToB || bToA) continue; // orderable → not ambiguous

            // Check if at least one is an interface or abstract class.
            // A concrete class (TypeKind=Class, IsAbstract=false) can't be implemented/inherited
            // by another independent type at runtime, so two concrete unrelated classes are safe.
            var aIsAbstractOrInterface = a.TypeKind == TypeKind.Interface || a.IsAbstract;
            var bIsAbstractOrInterface = b.TypeKind == TypeKind.Interface || b.IsAbstract;

            if (!aIsAbstractOrInterface && !bIsAbstractOrInterface) continue; // both concrete → safe

            var aFqn = a.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var bFqn = b.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.AmbiguousDerivedType,
                location,
                $"[MapDerivedType] source types '{aFqn}' and '{bFqn}' are both interfaces or abstract types that are mutually unorderable (neither inherits from the other); any concrete type implementing both would dispatch ambiguously to whichever arm appears first. Make one a subtype of the other, remove one, or change both to concrete types."));
        }
    }

    /// <summary>
    ///     Synthesizes the source code for a Preserve-mode dispatch wrapper that accepts a shared
    ///     <c>DwarfRefContext</c> and threads it to arm converters.
    ///     The wrapper does the identity-map TryGetReference/SetReference dance around the dispatch switch
    ///     so that when a container helper (e.g. <c>__DwarfMap_Obj_Container_*</c>) calls the wrapper
    ///     twice with the SAME source reference, the second call returns the already-mapped target — i.e.
    ///     <see cref="Assert.Same" /> topology fidelity under <c>ReferenceHandling = Preserve</c>.
    ///     Pattern (for src=PsvAnimal, tgt=PsvAnimalDto):
    ///     <code>
    ///   private PsvAnimalDto __DwarfMap_Disp_...(PsvAnimal a, DwarfRefContext ctx, int depth)
    ///   {
    ///       if (a is null) return null!;
    ///       if (ctx.TryGetReference(a, out var __dwarf_cached)) return (PsvAnimalDto)__dwarf_cached;
    ///       if (depth >= ctx.MaxDepth) throw new DwarfMappingDepthException(...);
    ///       var __dwarf_t = a switch { PsvDog __s => __DwarfMap_Obj_PsvDog_PsvDogDto_*(ctx,depth+1), ... };
    ///       ctx.SetReference(a, __dwarf_t);
    ///       return __dwarf_t;
    ///   }
    /// </code>
    /// </summary>
    private static string BuildDispatchWrapperCode(MapMethodModel dispatchMethod, string wrapperName)
    {
        var sb = new StringBuilder();
        var p = dispatchMethod.ParameterName;
        var src = dispatchMethod.ParameterTypeFullName;
        var tgt = dispatchMethod.ReturnTypeFullName;
        var arms = dispatchMethod.DerivedTypeArms;

        sb.Append("    private ").Append(tgt).Append(' ').Append(wrapperName)
            .Append('(').Append(src).Append(' ').Append(p)
            .AppendLine(", global::DwarfMapper.DwarfRefContext ctx, int depth)");
        sb.AppendLine("    {");
        if (dispatchMethod.ParameterIsReferenceType)
            sb.Append("        if (").Append(p).AppendLine(" is null) return null!;");
        sb.Append("        if (ctx.TryGetReference(").Append(p)
            .Append(", out var __dwarf_cached)) return (").Append(tgt).AppendLine(")__dwarf_cached;");
        sb.AppendLine("        if (depth >= ctx.MaxDepth)");
        sb.AppendLine("            throw new global::DwarfMapper.DwarfMappingDepthException(ctx.MaxDepth, depth);");
        sb.Append("        var __dwarf_t = ").Append(p).AppendLine(" switch");
        sb.AppendLine("        {");
        foreach (var arm in arms)
        {
            sb.Append("            ").Append(arm.SrcFqn).Append(" __s => ").Append(arm.ConverterMethod).Append("(__s");
            if (arm.ConverterNeedsDepthCtx)
                sb.Append(", ctx, depth + 1");
            sb.AppendLine("),");
        }

        // Wildcard arm matching the public dispatch method's throw.
        sb.Append("            _ => throw new global::System.ArgumentException(")
            .Append("\"DwarfMapper: no [MapDerivedType] registered for runtime type '\" + ")
            .Append(p).Append(".GetType() + \"' mapping to '").Append(tgt).Append("'.\", nameof(")
            .Append(p).AppendLine(")),");
        sb.AppendLine("        };");
        sb.Append("        ctx.SetReference(").Append(p).AppendLine(", __dwarf_t);");
        sb.AppendLine("        return __dwarf_t;");
        sb.AppendLine("    }");
        return sb.ToString();
    }
}
