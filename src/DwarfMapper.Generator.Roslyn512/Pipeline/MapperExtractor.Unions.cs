// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     One C# 15 union into another: the Roslyn 5.12 build's case dispatch.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Round 35 T23. A union map is a switch over the source union's case types, most-derived first. Each arm's
    ///         value has exactly one of the target union's case types and becomes the target union by the language's own
    ///         union conversion; an empty source union (its <c>Value</c> null) becomes an empty target union where the
    ///         target can hold one. The arms are the <c>[MapDerivedType]</c> dispatch's own model
    ///         (<see cref="DerivedTypeArm" />), so the context threading, the dispatch wrapper and the recursion passes
    ///         that serve a derived dispatch serve this one too.
    ///     </para>
    ///     <para>
    ///         <b>Pairing is strict</b> (anti-mislinking; DWARF114's remarks already rule that arms are not inferred). A
    ///         source case is paired by, in order: an explicit <c>[MapDerivedType]</c> arm naming it; the same type among
    ///         the target's cases (passed through); exactly one declared method from it to one of the target's cases
    ///         (that pair is then resolved the way any member of it would be). Nothing is paired by member shape or by
    ///         name: two records with a <c>Name</c> each are not evidence that they mean the same thing. A case left
    ///         unpaired is covered by an arm for a type it derives from, or it is DWARF114.
    ///     </para>
    /// </remarks>
    internal static partial class MapperExtractor
    {
        /// <summary>One union arm before ordering.</summary>
        /// <param name="Pattern">The type the arm's pattern tests: the case type without <c>Nullable&lt;T&gt;</c> or annotation.</param>
        /// <param name="TargetCase">The target case type the arm's value has.</param>
        /// <param name="Converter">The converter to call, or empty for a pass-through arm.</param>
        /// <param name="ConverterParamTypeFqn">The declared overload the converter was adopted from, or null.</param>
        /// <param name="NeedsCtx">Whether the converter takes the shared context.</param>
        private readonly record struct UnionArmPlan(
            ITypeSymbol Pattern,
            ITypeSymbol TargetCase,
            string Converter,
            string? ConverterParamTypeFqn,
            bool NeedsCtx);

        /// <summary>
        ///     The two unions of a union map, the subject its messages open with (<c>'Map'</c> for a declared method, a
        ///     phrase for a synthesized one), and where it reports. One bundle, so that each step below stays within the
        ///     six parameters a pipeline method may take.
        /// </summary>
        private sealed record UnionPair(ITypeSymbol Source, ITypeSymbol Target, string Subject, LocationInfo? Location);

        private static partial bool TryHandleUnionEndpoint(
            DeclaredEndpoint endpoint,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            ref bool classIgnoreLivenessBlinded)
        {
            var method = endpoint.Method;
            var sourceType = endpoint.Source;
            var targetType = endpoint.Target;
            var methodLocation = endpoint.Location;
            var compilation = ctx.SemanticModel.Compilation;
            var src = Unwrapped(sourceType);
            var tgt = Unwrapped(targetType);
            if (!UnionMappingAvailable(compilation) || SymbolEqualityComparer.Default.Equals(src, tgt) || !IsUnionType(src) || !IsUnionType(tgt))
            {
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(src, sourceType) || !SymbolEqualityComparer.Default.Equals(tgt, targetType))
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnionTypeNotMapped,
                    methodLocation,
                    "'" + method.Name + "' maps '" + sourceType.ToDisplayString() + "' to '" + targetType.ToDisplayString() +
                    "'. A union map is declared between the union types themselves: declare it between '" + src.ToDisplayString() +
                    "' and '" + tgt.ToDisplayString() + "', and a member of the nullable union type maps through it, keeping " +
                    "a null apart from an empty union"));
                return true;
            }

            if (method.Parameters.Length > 1)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                return true;
            }

            var settings = PolicySettings(policy, compilation, ReadMethodAutoNest(method, policy.ClassAutoNest));
            var pair = new UnionPair(src, tgt, "'" + method.Name + "'", methodLocation);
            var explicitArms = ReadDerivedTypeAttributes(method).Select(p => ((ITypeSymbol)p.Src, (ITypeSymbol)p.Tgt)).ToList();
            var arms = ResolveUnionArms(settings, pair, explicitArms, decls, acc);
            var empty = UnionEmptyArmFor(pair, DeclaresNullableRefReturn(method), acc.Diagnostics);
            var (before, after) = CollectDispatchHooks(compilation, decls, sourceType, targetType, methodLocation, acc.Diagnostics);
            var withheld = TryScopeCompletenessRefusalToItsMethod(acc.Diagnostics, endpoint.DiagnosticsStart, method.Name, methodLocation);

            acc.Methods.Add(new MapMethodModel(
                method.Name,
                AccessibilityText(method.DeclaredAccessibility),
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Name,
                sourceType.IsReferenceType,
                EquatableArray.From(Array.Empty<MemberMap>()),
                EquatableArray.From(before),
                EquatableArray.From(after),
                false,
                "",
                EquatableArray.From(Array.Empty<MemberMap>()),
                true,
                targetType.IsReferenceType,
                DerivedTypeArms: EquatableArray.From(arms),
                Withheld: withheld,
                ParameterTypeSignature: sourceType.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                ReturnTypeSignature: DeclaredReturnSignature(method),
                ReturnIsNullableRef: DeclaresNullableRefReturn(method),
                UnionEmpty: empty));
            // The arms are resolved here, out of sight of the class-site DWARF095 walk, as a derived dispatch's are.
            classIgnoreLivenessBlinded = true;
            return true;
        }

        /// <remarks>
        ///     <para>
        ///         The helper is a private dispatch method with the same arms a declared union map of the pair would have:
        ///         that map's own <c>[MapDerivedType]</c> arms when the class declares one (a helper exists beside a
        ///         declared map where the route has to carry the shared context, Preserve and SetNull), the implicit arms
        ///         otherwise. Collection, dictionary, <c>Nullable&lt;T&gt;</c> and update-into routes call it like any
        ///         converter, and the recursion passes treat it like any dispatch.
        ///     </para>
        ///     <para>
        ///         Beside a declared map, what the arms have to report was reported at the map, so the helper's own
        ///         resolution reports into a list nobody reads - the object path's rule for a declared pair.
        ///     </para>
        /// </remarks>
        private static partial bool TryBuildUnionHelper(
            (ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, bool AutoNest, LocationInfo? Origin) pending,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            List<(MapMethodModel Model, string MethodName, LocationInfo? Origin)> pendingNestedModels)
        {
            var compilation = ctx.SemanticModel.Compilation;
            var (src, tgt, name, autoNest, origin) = pending;
            if (!UnionMappingAvailable(compilation) || SameType(src, tgt) || !IsUnionType(src) || !IsUnionType(tgt))
            {
                return false;
            }

            var declared = DeclaredPartialFor(decls, src, tgt);
            var reporting = declared is null ? acc : acc with { Diagnostics = new List<DiagnosticInfo>() };
            var pair = new UnionPair(src,
                tgt,
                declared is null
                    ? $"The map of '{src.ToDisplayString()}' to '{tgt.ToDisplayString()}' synthesized for a member or element"
                    : "'" + declared.Name + "'",
                origin);
            var explicitArms = declared is null
                ? new List<(ITypeSymbol, ITypeSymbol)>()
                : ReadDerivedTypeAttributes(declared).Select(p => ((ITypeSymbol)p.Src, (ITypeSymbol)p.Tgt)).ToList();

            // Arms resolved under this pair's name, so that the helpers they reserve are this helper's edges in the
            // recursion graph, as an object helper's members' are.
            acc.NestedRegistry.SetCurrentPair(name);
            var arms = ResolveUnionArms(PolicySettings(policy, compilation, autoNest), pair, explicitArms, decls, reporting);
            acc.NestedRegistry.ClearCurrentPair();
            var empty = UnionEmptyArmFor(pair, false, reporting.Diagnostics);
            var (before, after) = CollectDispatchHooks(compilation, decls, src, tgt, origin, reporting.Diagnostics);

            pendingNestedModels.Add((new MapMethodModel(
                name,
                "private",
                tgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                "s",
                src.IsReferenceType,
                EquatableArray.From(Array.Empty<MemberMap>()),
                EquatableArray.From(before),
                EquatableArray.From(after),
                false,
                "",
                EquatableArray.From(Array.Empty<MemberMap>()),
                false,
                tgt.IsReferenceType,
                DerivedTypeArms: EquatableArray.From(arms),
                UnionEmpty: empty), name, origin));
            return true;
        }

        /// <summary>
        ///     The arms of a dispatch from one union of <paramref name="pair" /> to the other, most-derived first.
        ///     <paramref name="explicitArms" /> are the declared union map's <c>[MapDerivedType]</c> pairs (none when no map
        ///     is declared for the pair). Reports DWARF035 for an explicit arm that cannot be one, DWARF114 for a case left
        ///     without an arm.
        /// </summary>
        private static DerivedTypeArm[] ResolveUnionArms(
            ResolutionSettings settings,
            UnionPair pair,
            IReadOnlyList<(ITypeSymbol Src, ITypeSymbol Tgt)> explicitArms,
            MapperDeclarations decls,
            MapperAccumulators acc)
        {
            var src = pair.Source;
            var tgt = pair.Target;
            var location = pair.Location;
            var compilation = settings.Compilation;
            var srcCases = src.UnionCaseTypes;
            var tgtCases = tgt.UnionCaseTypes;
            var plans = new List<UnionArmPlan>();
            var paired = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var (armSrc, armTgt) in explicitArms)
            {
                var pattern = PatternType(armSrc);
                var srcFqn = armSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var tgtFqn = armTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!srcCases.Any(c => SameType(PatternType(c), pattern) || IsStrictSubtype(compilation, pattern, PatternType(c)) || IsStrictSubtype(compilation, PatternType(c), pattern)))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        location,
                        $"[MapDerivedType] source type '{srcFqn}' is not a case type of the union '{src.ToDisplayString()}' ({CaseList(srcCases)}), and neither derives from nor is a base of one; a union map's arms match the source union's case types."));
                    continue;
                }

                if (!HasImplicitOrUnionConversion(compilation, armTgt, tgt))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        location,
                        $"[MapDerivedType] target type '{tgtFqn}' does not convert to the union '{tgt.ToDisplayString()}': it is not one of its case types ({CaseList(tgtCases)})."));
                    continue;
                }

                if (!paired.Add(pattern))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        location,
                        $"[MapDerivedType] duplicate source type '{srcFqn}'; each source type may only be registered once per dispatch method."));
                    continue;
                }

                if (ResolveUnionArm(settings, pattern, armTgt, decls, acc, location) is not { } plan)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType,
                        location,
                        $"[MapDerivedType] pair ('{srcFqn}', '{tgtFqn}') is not mappable: no declared partial overload and not auto-nestable."));
                    continue;
                }

                plans.Add(plan);
            }

            var unpaired = new List<ITypeSymbol>();
            var contested = new List<string>();
            foreach (var c in srcCases)
            {
                var pattern = PatternType(c);
                if (paired.Contains(pattern))
                {
                    continue;
                }

                var same = tgtCases.FirstOrDefault(t => SameType(t, c));
                if (same is not null)
                {
                    paired.Add(pattern);
                    plans.Add(new UnionArmPlan(pattern, same, "", null, false));
                    continue;
                }

                var candidates = decls.AllMethods
                    .Where(m => !decls.MapperReservedConverters.Contains(m.Name) &&
                                (SameType(m.ParamType, c) || SameType(m.ParamType, pattern)) &&
                                tgtCases.Any(t => SameType(t, m.ReturnType)))
                    .GroupBy(m => m.Name + "|" + m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();
                if (candidates.Count == 1 &&
                    ResolveUnionArm(settings, pattern, tgtCases.First(t => SameType(t, candidates[0].ReturnType)), decls, acc, location) is { } adopted)
                {
                    paired.Add(pattern);
                    plans.Add(adopted);
                    continue;
                }

                if (candidates.Count > 1)
                {
                    contested.Add($"'{c.ToDisplayString()}' (" + string.Join(", ", candidates.Select(m => $"'{m.Name}' into '{m.ReturnType.ToDisplayString()}'")) + ")");
                    continue;
                }

                unpaired.Add(c);
            }

            var uncovered = unpaired
                .Where(c => !plans.Any(p => IsStrictSubtype(compilation, PatternType(c), p.Pattern)))
                .Select(c => "'" + c.ToDisplayString() + "'")
                .ToList();
            if (uncovered.Count > 0)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ClosedHierarchyArmMissing,
                    location,
                    $"{pair.Subject} maps the union '{src.ToDisplayString()}' to the union '{tgt.ToDisplayString()}', and its case type(s) {string.Join(", ", uncovered)} have no arm: none is a case type of '{tgt.ToDisplayString()}', and no declared method maps it to one. " +
                    $"Name the arm with [MapDerivedType] on a declared union map, or declare a method from the case type to a case type of '{tgt.ToDisplayString()}'; cases are never paired by their members or their names. A value of an unpaired case would fail the switch at run time."));
            }

            if (contested.Count > 0)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ClosedHierarchyArmMissing,
                    location,
                    $"{pair.Subject} maps the union '{src.ToDisplayString()}' to the union '{tgt.ToDisplayString()}', and its case type(s) {string.Join(", ", contested)} could each map through more than one declared method. " +
                    "Name the one each takes with [MapDerivedType] on a declared union map."));
            }

            return SortUnionArms(plans, compilation)
                .Select(p => new DerivedTypeArm(
                    p.Pattern.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    p.Converter,
                    p.NeedsCtx,
                    p.ConverterParamTypeFqn,
                    p.Converter.Length == 0 && !SameType(p.Pattern, p.TargetCase) ? p.TargetCase.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null))
                .ToArray();
        }

        /// <summary>
        ///     Resolves one arm the way a member of the pair <paramref name="pattern" /> into <paramref name="targetCase" />
        ///     would be resolved: a declared method, a synthesized helper (which Preserve and SetNull need, to carry the
        ///     shared context), or a plain conversion, which makes the arm a pass-through.
        /// </summary>
        /// <returns>The arm, or <see langword="null" /> when the pair does not resolve.</returns>
        private static UnionArmPlan? ResolveUnionArm(
            ResolutionSettings settings,
            ITypeSymbol pattern,
            ITypeSymbol targetCase,
            MapperDeclarations decls,
            MapperAccumulators acc,
            LocationInfo? location)
        {
            var resolved = TryResolveConversion(settings,
                pattern,
                targetCase,
                null,
                decls.AllMethods,
                decls.MapperMethods,
                acc.Synthesized,
                location,
                pattern.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                acc.Diagnostics,
                out var converter,
                out _,
                out var needsCtx,
                out var paramType,
                acc.NestedRegistry,
                allowInterfaceSrc: true,
                reservedConverters: decls.MapperReservedConverters);
            return resolved ? new UnionArmPlan(pattern, targetCase, converter ?? "", paramType, needsCtx) : null;
        }

        /// <summary>
        ///     How an empty source union ends: <c>default</c> of the target where the target can hold an empty union (a
        ///     struct union, or a nullable class union); no null arm where it cannot and the source cannot be empty by its
        ///     case types (the switch is exhaustive, and a <c>default</c> struct throws as the same switch written by hand
        ///     does); DWARF113 where it cannot and the source can.
        /// </summary>
        private static UnionEmptyArm UnionEmptyArmFor(UnionPair pair, bool returnIsNullableRef, List<DiagnosticInfo> diagnostics)
        {
            var src = pair.Source;
            var tgt = pair.Target;
            if (tgt.IsValueType || returnIsNullableRef)
            {
                return UnionEmptyArm.Default;
            }

            var nullableCase = NullableCase(src);
            if (nullableCase is null)
            {
                return UnionEmptyArm.None;
            }

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnionTypeNotMapped,
                pair.Location,
                $"{pair.Subject} maps '{src.ToDisplayString()}' to '{tgt.ToDisplayString()}'. An empty '{src.ToDisplayString()}' - its case type '{nullableCase}' can be null - has nothing to map to: '{tgt.ToDisplayString()}' is a class union, and the destination is not nullable. Make the destination nullable, or map into a struct union"));
            return UnionEmptyArm.None;
        }

        /// <summary>
        ///     A case type of <paramref name="union" /> that can be null, as written, or <see langword="null" /> when none
        ///     can - the union's <c>Value</c> is "maybe null" exactly when one can (the unions spec, "Nullability").
        /// </summary>
        /// <remarks>
        ///     Read from the union's creation members, not from <c>UnionCaseTypes</c>: Roslyn 5.12 reports a case written
        ///     <c>Cat?</c> as <c>Cat</c> with no annotation there, while the constructor's (or <c>Create</c> factory's)
        ///     parameter keeps it (measured, 2026-11-14). A <c>Nullable&lt;T&gt;</c> case is nullable as a type, and an
        ///     unconstrained type parameter may be.
        /// </remarks>
        private static string? NullableCase(ITypeSymbol union)
        {
            var provider = union.GetTypeMembers("IUnionMembers").FirstOrDefault(t => t.TypeKind == TypeKind.Interface);
            var creationParameters = provider is not null
                ? provider.GetMembers("Create").OfType<IMethodSymbol>().Where(m => m.IsStatic && m.Parameters.Length == 1).Select(m => m.Parameters[0])
                : union.GetMembers(WellKnownMemberNames.InstanceConstructorName).OfType<IMethodSymbol>().Where(m => m.Parameters.Length == 1).Select(m => m.Parameters[0]);
            foreach (var parameter in creationParameters)
            {
                if (!union.UnionCaseTypes.Any(c => SameType(c, parameter.Type)))
                {
                    continue;
                }

                if (parameter.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValue(parameter.Type, out _) ||
                    parameter.Type is ITypeParameterSymbol { HasNotNullConstraint: false, HasValueTypeConstraint: false, HasUnmanagedTypeConstraint: false })
                {
                    return parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat.WithMiscellaneousOptions(
                        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
                }
            }

            return null;
        }

        /// <summary>Most-derived first: an arm whose pattern derives from another's is placed before it (CS8510 otherwise).</summary>
        private static List<UnionArmPlan> SortUnionArms(List<UnionArmPlan> plans, Compilation compilation)
        {
            var remaining = new List<UnionArmPlan>(plans);
            var sorted = new List<UnionArmPlan>(plans.Count);
            while (remaining.Count > 0)
            {
                var pick = 0;
                for (var i = 0; i < remaining.Count; i++)
                {
                    var candidate = remaining[i].Pattern;
                    if (!remaining.Where((_, j) => j != i).Any(other => IsStrictSubtype(compilation, other.Pattern, candidate)))
                    {
                        pick = i;
                        break;
                    }
                }

                sorted.Add(remaining[pick]);
                remaining.RemoveAt(pick);
            }

            return sorted;
        }

        /// <summary>The type a case's pattern tests: no <c>Nullable&lt;T&gt;</c> (CS8116) and no nullable annotation.</summary>
        private static ITypeSymbol PatternType(ITypeSymbol c)
        {
            return IsNullableValue(c, out var underlying) ? underlying : c.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        private static bool SameType(ITypeSymbol a, ITypeSymbol b)
        {
            return SymbolEqualityComparer.Default.Equals(a, b);
        }

        /// <summary>True when every value of <paramref name="sub" /> is also a <paramref name="sup" /> and the two differ.</summary>
        private static bool IsStrictSubtype(Compilation compilation, ITypeSymbol sub, ITypeSymbol sup)
        {
            if (SameType(sub, sup))
            {
                return false;
            }

            var conversion = ((CSharpCompilation)compilation).ClassifyConversion(sub, sup);
            return conversion.IsImplicit && (conversion.IsReference || conversion.IsBoxing);
        }

        private static string CaseList(System.Collections.Immutable.ImmutableArray<ITypeSymbol> cases)
        {
            return string.Join(", ", cases.Select(c => c.ToDisplayString()));
        }
    }
}
