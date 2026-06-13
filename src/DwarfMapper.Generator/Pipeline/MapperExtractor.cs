// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal enum NullStrategy
{
    Throw = 0,
    SetDefault = 1,
}

internal enum NullCollectionsBehavior
{
    AsEmpty = 0,
    AsNull  = 1,
}

internal static class MapperExtractor
{
    public static MapperClassModel Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        var isPartial = classSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MapperNotPartial,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));
        }

        var classIgnores = ReadIgnores(classSymbol).ToList();
        var classIgnoreSources = ReadIgnoreSources(classSymbol).ToList();
        var requiredMapping = ReadRequiredMapping(ctx.Attributes); // 0 = Target (default), 1 = Both
        var caseInsensitive = ReadCaseInsensitive(ctx.Attributes);
        var enumStrategy = ReadEnumStrategy(ctx.Attributes);
        var nullStrategy = ReadNullStrategy(ctx.Attributes);
        var classAutoNest = ReadAutoNest(ctx.Attributes);
        var nullCollections = ReadNullCollections(ctx.Attributes);
        var maxDepth = ReadMaxDepth(ctx.Attributes);
        var referenceHandling = ReadReferenceHandling(ctx.Attributes);
        var isPreserveMode = referenceHandling == 1; // 1 = ReferenceHandlingStrategy.Preserve
        var onCycle = ReadOnCycle(ctx.Attributes);   // 0 = Throw, 1 = SetNull
        var implicitConversions = ReadImplicitConversions(ctx.Attributes); // default true (permissive)
        // SetNull is only meaningful in None mode; under Preserve, cycles are reconstructed and
        // OnCycle is ignored → DWARF037 (loud, not a silent no-op).
        var isSetNullMode = onCycle == 1 && !isPreserveMode;
        if (onCycle == 1 && isPreserveMode)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.OnCycleIgnoredUnderPreserve,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));
        }
        var synthesized = new Dictionary<string, SynthesizedMethod>(System.StringComparer.Ordinal);
        var allMethods = CollectMethods(classSymbol);
        var mapperMethods = CollectMapperMethods(classSymbol);
        var valueProviders = CollectValueProviders(classSymbol); // parameterless methods for [MapValue(Use=)]
        var (beforeHookDefs, afterHookDefs) = CollectHooks(classSymbol, diagnostics);
        var methods = new List<MapMethodModel>();

        // NestedMappingRegistry: local to this Extract call (contains ISymbol — never stored in model).
        var nestedRegistry = new NestedMappingRegistry();

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition)
            {
                continue;
            }

            var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None);

            // ── Zero-alloc span map: void Map(ReadOnlySpan<S>/Span<S> src, Span<D> dst) ──
            // Maps element-wise into a caller-provided destination buffer (no allocation). The
            // destination must be a writable Span<D>; a too-small destination throws (never silent
            // truncation). The element conversion reuses the full resolution pipeline.
            if (method.ReturnsVoid && method.Parameters.Length == 2
                && TryGetSpanElement(method.Parameters[0].Type, out var spanSrcElem, out _)
                && TryGetSpanElement(method.Parameters[1].Type, out var spanDstElem, out var dstIsReadOnly)
                && !dstIsReadOnly)
            {
                var spanComp = ctx.SemanticModel.Compilation;
                var spanAutoNest = ReadMethodAutoNest(method, classAutoNest);
                if (!TryResolveConversion(spanComp, spanSrcElem, spanDstElem, null, allMethods, mapperMethods,
                        enumStrategy, synthesized, nullStrategy, methodLocation, method.Name, diagnostics,
                        out var spanConv, out var spanNull, out var spanNeedsCtx, spanAutoNest, nestedRegistry,
                        nullAsNull: false, isPreserve: false))
                {
                    // Element pair not mappable → diagnostic (e.g. DWARF005) already added.
                    continue;
                }

                var spanElemMember = new MemberMap(
                    TargetName: "", SourceName: "", ConverterMethod: spanConv,
                    NullHandling: spanNull, ConverterNeedsDepthCtx: spanNeedsCtx);

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    false,
                    EquatableArray.From(new[] { spanElemMember }),
                    EquatableArray.From(System.Array.Empty<string>()),
                    EquatableArray.From(System.Array.Empty<HookCall>()),
                    false,
                    "",
                    IsSpanMap: true,
                    SpanTargetParameterName: method.Parameters[1].Name));
                continue;
            }

            // ── Update-into-existing: void/T Map(S src, T dest) ─────────────────────
            // Maps onto an EXISTING reference-type target instance (no construction; identity kept).
            // Return is void OR the destination type. v1 is None-semantics (no Preserve/SetNull on the
            // update method itself) and reference-type targets only (a struct dest by value can't
            // observe mutations). Resolution reuses ResolveMembers; only the emission differs.
            if (method.Parameters.Length == 2
                && method.Parameters[1].Type is INamedTypeSymbol updTgt
                && method.Parameters[1].Type.IsReferenceType
                && (method.ReturnsVoid
                    || SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[1].Type)))
            {
                var updSrc = method.Parameters[0].Type;
                var comp = ctx.SemanticModel.Compilation;
                var updIgnores = new HashSet<string>(classIgnores);
                foreach (var ig in ReadIgnores(method)) { updIgnores.Add(ig); }
                var updExplicit = ReadExplicitMaps(method);
                var updAutoNest = ReadMethodAutoNest(method, classAutoNest);

                var updMembers = ResolveMembers(
                    updSrc, updTgt, updIgnores, comp, methodLocation, diagnostics, caseInsensitive,
                    updExplicit, allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                    System.Array.Empty<string>(), new List<string>(),
                    consumedCtorParams: null, requiredMustInitialize: null, updAutoNest, nestedRegistry,
                    nullCollections == NullCollectionsBehavior.AsNull, isPreserve: false, isSetNull: false);

                var updBefore = new List<string>();
                foreach (var h in beforeHookDefs)
                    if (HasImplicitConversion(comp, updSrc, h.ParamType)) updBefore.Add(h.Name);

                var updAfter = new List<HookCall>();
                foreach (var h in afterHookDefs)
                {
                    bool applies; bool takesSource;
                    if (h.P1 is null) { applies = HasImplicitConversion(comp, updTgt, h.P0); takesSource = false; }
                    else { applies = HasImplicitConversion(comp, updSrc, h.P0) && HasImplicitConversion(comp, updTgt, h.P1); takesSource = true; }
                    if (!applies) continue;
                    // Target is a reference type → by-value is fine (mutations propagate); ref optional.
                    updAfter.Add(new HookCall(h.Name, takesSource, TargetByRef: h.TargetRefKind == RefKind.Ref));
                }

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    updTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    updSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    updSrc.IsReferenceType,
                    EquatableArray.From(updMembers),
                    EquatableArray.From(updBefore),
                    EquatableArray.From(updAfter),
                    false,
                    "",
                    IsUpdateInto: true,
                    UpdateTargetParameterName: method.Parameters[1].Name,
                    UpdateReturnsVoid: method.ReturnsVoid,
                    MaxDepth: maxDepth));
                continue;
            }

            // ── Async streaming map: IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src) ──
            // Emitted as an async iterator (await foreach … yield return conv(item)) that lazily
            // transforms the source sequence — preserves streaming/back-pressure, no buffering.
            if (method.Parameters.Length == 1 && !method.ReturnsVoid
                && TryGetAsyncEnumerableElement(method.Parameters[0].Type, out var asSrcElem)
                && TryGetAsyncEnumerableElement(method.ReturnType, out var asDstElem))
            {
                var asComp = ctx.SemanticModel.Compilation;
                var asAutoNest = ReadMethodAutoNest(method, classAutoNest);
                if (!TryResolveConversion(asComp, asSrcElem, asDstElem, null, allMethods, mapperMethods,
                        enumStrategy, synthesized, nullStrategy, methodLocation, method.Name, diagnostics,
                        out var asConv, out var asNull, out var asNeedsCtx, asAutoNest, nestedRegistry,
                        nullAsNull: false, isPreserve: false))
                {
                    continue; // element pair not mappable → diagnostic already added
                }

                var asElemMember = new MemberMap(
                    TargetName: "", SourceName: "", ConverterMethod: asConv,
                    NullHandling: asNull, ConverterNeedsDepthCtx: asNeedsCtx);

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    false,
                    EquatableArray.From(new[] { asElemMember }),
                    EquatableArray.From(System.Array.Empty<string>()),
                    EquatableArray.From(System.Array.Empty<HookCall>()),
                    false,
                    "",
                    IsAsyncStreamMap: true));
                continue;
            }

            if (method.ReturnsVoid || method.Parameters.Length != 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            if (IsQueryable(method.ReturnType, out var projTarget)
                && IsQueryable(method.Parameters[0].Type, out var projSource)
                && projTarget is INamedTypeSymbol projTargetNamed)
            {
                var projIgnores = new HashSet<string>(classIgnores);
                foreach (var i in ReadIgnores(method)) { projIgnores.Add(i); }

                // Plan 19D: DWARF028 — ReferenceHandling != None is incompatible with projection
                // (a stateful identity map cannot live inside an expression tree).
                if (referenceHandling != 0)
                {
                    EmitDWARF028(diagnostics, methodLocation, method.Name,
                        "reference handling is not supported in projection (stateful identity map cannot live in an expression tree); use ReferenceHandling=None or map at runtime");
                    // Still add the method with empty projection members so no further cascades.
                    methods.Add(new MapMethodModel(
                        method.Name,
                        AccessibilityText(method.DeclaredAccessibility),
                        method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Name,
                        true,
                        EquatableArray.From(System.Array.Empty<MemberMap>()),
                        EquatableArray.From(System.Array.Empty<string>()),
                        EquatableArray.From(System.Array.Empty<HookCall>()),
                        true,
                        projTargetNamed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ProjectionMembers: EquatableArray.From(System.Array.Empty<ProjectionMemberMap>())));
                    continue;
                }

                // VF5: Hooks (before/after) cannot live inside an expression tree.
                // Detect any applicable hook for this projection's source/target and emit DWARF028
                // instead of silently dropping it. Per thesis: loud, never silent.
                var hasApplicableHook = false;
                foreach (var h in beforeHookDefs)
                {
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.ParamType))
                    {
                        hasApplicableHook = true;
                        break;
                    }
                }
                if (!hasApplicableHook)
                {
                    foreach (var h in afterHookDefs)
                    {
                        bool applies = h.P1 is null
                            ? HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P0)
                            : HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.P0)
                              && HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P1);
                        if (applies)
                        {
                            hasApplicableHook = true;
                            break;
                        }
                    }
                }
                if (hasApplicableHook)
                {
                    EmitDWARF028(diagnostics, methodLocation, method.Name,
                        "hooks (BeforeMap/AfterMap) are not supported in IQueryable projection (expression trees cannot contain hook calls); move hooks to a runtime mapper or remove them");
                }

                var projExplicitMaps = ReadExplicitMaps(method);
                var projMembers = ResolveProjectionMembers(
                    projSource, projTargetNamed, projIgnores, ctx.SemanticModel.Compilation,
                    methodLocation, diagnostics, caseInsensitive, projExplicitMaps, enumStrategy,
                    referenceHandling, "__s");

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    true,
                    EquatableArray.From(System.Array.Empty<MemberMap>()),
                    EquatableArray.From(System.Array.Empty<string>()),
                    EquatableArray.From(System.Array.Empty<HookCall>()),
                    true,
                    projTargetNamed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ProjectionMembers: EquatableArray.From(projMembers.ToArray())));
                continue;
            }

            if (method.ReturnType is not INamedTypeSymbol targetType)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            var sourceType = method.Parameters[0].Type;

            // Read methodAutoNest early — needed by both Plan 21 (derived dispatch) and the normal path.
            var methodAutoNest = ReadMethodAutoNest(method, classAutoNest);

            // ── Plan 22: early-detect heterogeneous [FlattenGraph] ───────────
            // If [FlattenGraph] is present on the same method, [MapDerivedType] attrs apply
            // to the GRAPH NODE types (not the root method type), so Plan 21's validation
            // (derived-src assignable to method source type = root type) would falsely reject them.
            // Read FlattenGraph attrs now so we can skip Plan 21 for hetero-FlattenGraph methods.
            var flattenGraphRawEarly = ReadFlattenGraphAttributes(method);
            var isHeteroFlattenGraph = flattenGraphRawEarly.Count > 0;

            // ── Plan 21: [MapDerivedType] dispatch ───────────────────────────
            var rawDerivedPairs = ReadDerivedTypeAttributes(method, ctx.SemanticModel.Compilation);
            if (rawDerivedPairs.Count > 0 && !isHeteroFlattenGraph)
            {
                var resolvedArms = new List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)>();
                var seenSrcTypes = new HashSet<string>(System.StringComparer.Ordinal);

                foreach (var (derivedSrc, derivedTgt) in rawDerivedPairs)
                {
                    var srcFqn = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var tgtFqn = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    // 1. srcDerived must be assignable to method source type.
                    if (!HasImplicitConversion(ctx.SemanticModel.Compilation, derivedSrc, sourceType))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] source type '{srcFqn}' is not assignable to method source type '{sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; derived source must inherit from or implement the method's source type."));
                        continue;
                    }

                    // 2. tgtDerived must be assignable to method return type.
                    if (!HasImplicitConversion(ctx.SemanticModel.Compilation, derivedTgt, targetType))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] target type '{tgtFqn}' is not assignable to method return type '{targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; derived target must inherit from or implement the method's return type."));
                        continue;
                    }

                    // 3. No duplicate source types.
                    if (!seenSrcTypes.Add(srcFqn))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] duplicate source type '{srcFqn}'; each derived source type may only be registered once per dispatch method."));
                        continue;
                    }

                    // 4. Resolve converter via TryResolveConversion.
                    // SF-ORDER fix: pass allowInterfaceSrc=true so that [MapDerivedType] arms
                    // with an interface source (e.g. [MapDerivedType(typeof(IFoo), typeof(FooDto))])
                    // are synthesized correctly. The DWARF033 guard is suppressed here because the
                    // user explicitly opted in via the attribute.
                    bool resolved = TryResolveConversion(
                        ctx.SemanticModel.Compilation,
                        derivedSrc, derivedTgt,
                        null,
                        allMethods, mapperMethods,
                        enumStrategy, synthesized,
                        nullStrategy,
                        methodLocation, srcFqn, diagnostics,
                        out var armConverter, out _, out var armNeedsCtx,
                        methodAutoNest, nestedRegistry,
                        nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode,
                        allowInterfaceSrc: true, isSetNull: isSetNullMode, implicitConversions: implicitConversions);

                    if (!resolved || armConverter is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] pair ('{srcFqn}', '{tgtFqn}') is not mappable: no declared partial overload and not auto-nestable."));
                        continue;
                    }

                    resolvedArms.Add((derivedSrc, derivedTgt, armConverter, armNeedsCtx));
                }

                // Sort arms most-derived-first.
                var sortedArms = SortArmsMostDerivedFirst(resolvedArms, ctx.SemanticModel.Compilation);

                // DWARF036: detect mutually-unorderable interface/abstract source arms.
                // If two arm source types are neither assignable to each other AND at least one
                // is an interface or abstract class, a concrete type could implement/inherit both
                // and would dispatch non-deterministically (whichever arm is first wins).
                // Concrete-to-concrete pairs are NOT ambiguous: a concrete instance has exactly
                // one runtime type, so at most one arm can match at runtime.
                DetectAmbiguousInterfaceArms(sortedArms, ctx.SemanticModel.Compilation, methodLocation, diagnostics);

                var armModels = sortedArms
                    .Select(a => new DerivedTypeArm(
                        a.Src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        a.ConverterMethod,
                        a.NeedsCtx))
                    .ToArray();

                // Collect applicable hooks.
                var derivedBefore = new List<string>();
                foreach (var h in beforeHookDefs)
                {
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                        derivedBefore.Add(h.Name);
                }
                var derivedAfter = new List<HookCall>();
                foreach (var h in afterHookDefs)
                {
                    bool applies;
                    bool takesSource;
                    if (h.P1 is null)
                    {
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P0);
                        takesSource = false;
                    }
                    else
                    {
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0)
                            && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
                        takesSource = true;
                    }
                    if (!applies) continue;
                    var targetIsRef = h.TargetRefKind == RefKind.Ref;
                    if (targetType.IsValueType && !targetIsRef) continue;
                    derivedAfter.Add(new HookCall(h.Name, takesSource, TargetByRef: targetIsRef));
                }

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    sourceType.IsReferenceType,
                    EquatableArray.From(System.Array.Empty<MemberMap>()),
                    EquatableArray.From(derivedBefore),
                    EquatableArray.From(derivedAfter),
                    IsProjection: false,
                    ElementTargetTypeFullName: "",
                    ConstructorArguments: EquatableArray.From(System.Array.Empty<MemberMap>()),
                    IsPartial: true,
                    ReturnIsReferenceType: targetType.IsReferenceType,
                    DerivedTypeArms: EquatableArray.From(armModels)));
                continue;
            }
            // ── End Plan 21 ──────────────────────────────────────────────────

            // ── Fix 1: Top-level collection/dictionary-returning method ──────────────────
            // If the return type is a recognized collection or dictionary TARGET shape,
            // route through TryResolveConversion to get a synthesized helper, then emit
            // "return helper(param);" instead of running ConstructorSelector → DWARF007.
            // Detection: call CollectionConverter.TryResolve / DictionaryConverter.TryResolve
            // with targetType as both src and target — they check the TARGET shape first.
            // Scope: only fires when the return type IS a collection/dict; object/record/scalar
            // return types fail both TryResolve calls and fall through unchanged.
            var isCollReturn = CollectionConverter.TryResolve(targetType, targetType,
                out _, out _, out _, false);
            var isDictReturn = !isCollReturn && DictionaryConverter.TryResolve(targetType, targetType,
                out _, out _, out _, out _, out _);

            if (isCollReturn || isDictReturn)
            {
                bool tlResolved = TryResolveConversion(
                    ctx.SemanticModel.Compilation,
                    sourceType, targetType,
                    null,
                    allMethods, mapperMethods,
                    enumStrategy, synthesized,
                    nullStrategy,
                    methodLocation, method.Name, diagnostics,
                    out var tlConverter, out _, out var tlNeedsCtx,
                    methodAutoNest, nestedRegistry,
                    nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode,
                    isSetNull: isSetNullMode, implicitConversions: implicitConversions);

                if (!tlResolved || tlConverter is null)
                {
                    // Element conversion failed (diagnostic already reported). Skip this method.
                    continue;
                }

                var tlMember = new MemberMap(
                    TargetName: "",
                    SourceName: "", // sentinel: emit helper(param) not helper(param.Member)
                    ConverterMethod: tlConverter,
                    ConverterNeedsDepthCtx: tlNeedsCtx);

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    sourceType.IsReferenceType,
                    EquatableArray.From(new[] { tlMember }),
                    EquatableArray.From(System.Array.Empty<string>()),
                    EquatableArray.From(System.Array.Empty<HookCall>()),
                    IsProjection: false,
                    ElementTargetTypeFullName: "",
                    ConstructorArguments: EquatableArray.From(System.Array.Empty<MemberMap>()),
                    IsPartial: true,
                    ReturnIsReferenceType: targetType.IsReferenceType,
                    IsTopLevelCollectionConversion: true));
                continue;
            }
            // ── End Fix 1 ────────────────────────────────────────────────────────────────

            // Choose construction strategy for the target type.
            var ctor = ConstructorSelector.Select(targetType, diagnostics, methodLocation, out var objInitOnly);
            if (ctor is null)
            {
                continue;
            }

            var ignores = new HashSet<string>(classIgnores);
            foreach (var i in ReadIgnores(method))
            {
                ignores.Add(i);
            }

            var explicitMaps = ReadExplicitMaps(method);
            var mapValues = ReadMapValues(method);
            var flattenRoots = ReadFlattenRoots(method);
            var reinterpretMembers = ReadReinterpretMembers(method);

            // ── Plan 20 / 22: [FlattenGraph] ─────────────────────────────────
            // Read and resolve [FlattenGraph] directives BEFORE ResolveMembers so that
            // target collection members can be added to ignores and skipped from normal mapping.
            // flattenGraphRawEarly was already read above (for hetero-detection); reuse it.
            var flattenGraphRaw = flattenGraphRawEarly;
            var flattenGraphConsumed = new HashSet<string>(System.StringComparer.Ordinal);
            List<Model.FlattenGraphDirective> resolvedFgDirectives;
            List<MemberMap> fgInjectedMembers;

            if (flattenGraphRaw.Count > 0)
            {
                (resolvedFgDirectives, fgInjectedMembers) = ResolveFlattenGraphDirectives(
                    sourceType, targetType, flattenGraphRaw, ctx.SemanticModel.Compilation,
                    methodLocation, diagnostics, allMethods, mapperMethods,
                    enumStrategy, synthesized, nullStrategy,
                    methodAutoNest, nestedRegistry,
                    nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode,
                    flattenGraphConsumed,
                    rawDerivedPairs);

                // Add consumed targets to ignores so ResolveMembers skips them and
                // does not emit DWARF001 (unmapped) for them.
                foreach (var consumed in flattenGraphConsumed)
                    ignores.Add(consumed);
            }
            else
            {
                resolvedFgDirectives = new List<Model.FlattenGraphDirective>();
                fgInjectedMembers = new List<MemberMap>();
            }

            // Resolve constructor arguments (empty set when objInitOnly).
            MemberMap[] ctorArgs;
            HashSet<string> consumedParams;
            // Members that are `required` AND satisfied via a ctor param but whose ctor is NOT annotated
            // [SetsRequiredMembers]: C# requires them to ALSO be set in the object initializer (CS9035).
            // These must NOT be excluded from the initializer even though they are in consumedParams.
            HashSet<string> requiredMustInitialize;
            if (objInitOnly)
            {
                ctorArgs = System.Array.Empty<MemberMap>();
                consumedParams = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                requiredMustInitialize = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(ctor, sourceType, ctx.SemanticModel.Compilation,
                    methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                    enumStrategy, synthesized, nullStrategy, methodAutoNest, nestedRegistry, out ctorArgs, out consumedParams,
                    nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions))
                {
                    // At least one parameter was unmappable → DWARF024 already reported; skip emit.
                    continue;
                }

                // Compute which consumed-param members are `required` and whose ctor lacks [SetsRequiredMembers].
                // Those must still be emitted in the object initializer to satisfy the C# `required` rule.
                requiredMustInitialize = ComputeRequiredMustInitialize(ctor, targetType, consumedParams);
            }

            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                enumStrategy, synthesized, nullStrategy, flattenRoots, reinterpretMembers,
                consumedParams, requiredMustInitialize, methodAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions,
                mapValues: mapValues, valueProviders: valueProviders);

            // Append FlattenGraph-injected member maps (traversal helper calls).
            // These come AFTER normal members so the object initializer order is:
            //   normal scalars/nested first, then flat-graph collections.
            members.AddRange(fgInjectedMembers);

            // ── Source-member coverage (RequiredMapping = Both) ───────────────────────────
            // The source-side mirror of the DWARF001 completeness gate: under `Both`, every readable
            // source member must be read by some destination (member OR constructor argument). A source
            // consumed by nothing surfaces DWARF039 (Info suggestion), unless suppressed by
            // [MapIgnoreSource]. Dotted source names (flattened leaves) mark their root consumed.
            if (requiredMapping == 1) // RequiredMappingStrategy.Both
            {
                var ignoreSources = new HashSet<string>(classIgnoreSources, System.StringComparer.Ordinal);
                foreach (var s in ReadIgnoreSources(method))
                    ignoreSources.Add(s);

                var consumed = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var m in members)
                    AddConsumed(consumed, m.SourceName);
                foreach (var m in ctorArgs)
                    AddConsumed(consumed, m.SourceName);

                foreach (var (name, _) in ReadableMembers(sourceType))
                {
                    if (!consumed.Contains(name) && !ignoreSources.Contains(name))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.UnconsumedSourceMember, methodLocation, name));
                    }
                }
            }

            var applicableBefore = new List<string>();
            foreach (var h in beforeHookDefs)
            {
                if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                {
                    applicableBefore.Add(h.Name);
                }
            }
            var applicableAfter = new List<HookCall>();
            foreach (var h in afterHookDefs)
            {
                bool applies;
                bool takesSource;
                if (h.P1 is null)
                {
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P0);
                    takesSource = false;
                }
                else
                {
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0)
                        && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
                    takesSource = true;
                }

                if (!applies)
                {
                    continue;
                }

                var targetIsValue = targetType.IsValueType;
                var targetIsRef = h.TargetRefKind == RefKind.Ref;

                if (targetIsValue && !targetIsRef)
                {
                    // Silent correctness bug: struct target passed by value — mutations would be lost.
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AfterMapValueTargetByValue,
                        methodLocation,
                        targetType.Name));
                    // Skip: do not emit this hook.
                    continue;
                }

                applicableAfter.Add(new HookCall(h.Name, takesSource, TargetByRef: targetIsRef));
            }

            methods.Add(new MapMethodModel(
                method.Name,
                AccessibilityText(method.DeclaredAccessibility),
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Name,
                sourceType.IsReferenceType,
                EquatableArray.From(members),
                EquatableArray.From(applicableBefore),
                EquatableArray.From(applicableAfter),
                false,
                "",
                EquatableArray.From(ctorArgs),
                FlattenGraphDirectives: EquatableArray.From(resolvedFgDirectives.ToArray())));
        }

        // ── [GenerateMap<TSrc, TTgt>] — low-ceremony attribute-declared mappers ──────
        // For each [GenerateMap<S,T>] on the mapper class, synthesize a public `T Map(S)` overload
        // (EmitAsNonPartial → emitted as a full method, not a partial impl) with the SAME completeness
        // gate, conversions, nested/collection handling, constructor mapping, and hooks as a declared
        // partial mapper. Source/target types stay plain POCOs (no attributes on them) — migrating from
        // e.g. AutoMapper's CreateMap<A,B>() is a near-mechanical 1:1 replace with [GenerateMap<A,B>].
        var genComp = ctx.SemanticModel.Compilation;
        var genLoc = LocationInfo.From(classSyntax.Identifier.GetLocation());
        var emptyExplicit = System.Array.Empty<(string Source, string Target, string? Use)>();
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not { Name: "GenerateMapAttribute" } ac
                || ac.TypeArguments.Length != 2
                || ac.ContainingNamespace?.Name != "DwarfMapper")
                continue;
            if (ac.TypeArguments[1] is not INamedTypeSymbol genTgt) continue;
            var genSrc = ac.TypeArguments[0];

            var genCtor = ConstructorSelector.Select(genTgt, diagnostics, genLoc, out var genObjInitOnly);
            if (genCtor is null) continue;

            MemberMap[] genCtorArgs;
            HashSet<string> genConsumed;
            HashSet<string> genRequiredInit;
            if (genObjInitOnly)
            {
                genCtorArgs = System.Array.Empty<MemberMap>();
                genConsumed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                genRequiredInit = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(genCtor, genSrc, genComp, genLoc, diagnostics,
                        caseInsensitive, emptyExplicit, allMethods, mapperMethods, enumStrategy, synthesized,
                        nullStrategy, classAutoNest, nestedRegistry, out genCtorArgs, out genConsumed,
                        nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions))
                    continue;
                genRequiredInit = ComputeRequiredMustInitialize(genCtor, genTgt, genConsumed);
            }

            var genMembers = ResolveMembers(
                genSrc, genTgt, new HashSet<string>(classIgnores), genComp, genLoc, diagnostics,
                caseInsensitive, emptyExplicit, allMethods, mapperMethods, enumStrategy, synthesized,
                nullStrategy, System.Array.Empty<string>(), new List<string>(),
                genConsumed, genRequiredInit, classAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions);

            var genBefore = new List<string>();
            foreach (var h in beforeHookDefs)
                if (HasImplicitConversion(genComp, genSrc, h.ParamType)) genBefore.Add(h.Name);
            var genAfter = new List<HookCall>();
            foreach (var h in afterHookDefs)
            {
                bool applies; bool takesSource;
                if (h.P1 is null) { applies = HasImplicitConversion(genComp, genTgt, h.P0); takesSource = false; }
                else { applies = HasImplicitConversion(genComp, genSrc, h.P0) && HasImplicitConversion(genComp, genTgt, h.P1); takesSource = true; }
                if (!applies) continue;
                var tIsRef = h.TargetRefKind == RefKind.Ref;
                if (genTgt.IsValueType && !tIsRef) continue;
                genAfter.Add(new HookCall(h.Name, takesSource, TargetByRef: tIsRef));
            }

            methods.Add(new MapMethodModel(
                "Map",
                "public",
                genTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                genSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                "src",
                genSrc.IsReferenceType,
                EquatableArray.From(genMembers),
                EquatableArray.From(genBefore),
                EquatableArray.From(genAfter),
                IsProjection: false,
                ElementTargetTypeFullName: "",
                ConstructorArguments: EquatableArray.From(genCtorArgs),
                IsPartial: true,
                ReturnIsReferenceType: genTgt.IsReferenceType,
                EmitAsNonPartial: true));
        }

        // ── Drain the NestedMappingRegistry queue ────────────────────────────────
        // User-declared partial methods are already registered in mapperMethods (autoCandidates).
        // We process synthesized pairs AFTER declared methods so user methods always win.
        // Each dequeued pair may enqueue further pairs → loop until empty (terminates because
        // each pair is registered-before-built, so revisits hit the memoization branch).
        // We also track dependency edges (nestedRegistry.SetCurrentPair) so that after the
        // drain we can compute which pairs are recursion-capable (Plan 19 C1).
        // Temporary list: collect models before we know their IsRecursionCapable flag.
        var pendingNestedModels = new List<(MapMethodModel Model, string MethodName)>();

        while (nestedRegistry.HasPending)
        {
            ct.ThrowIfCancellationRequested();

            var (nestedSrc, nestedTgt, nestedName, pairAutoNest) = nestedRegistry.Dequeue();

            // Inform the registry that we are now building this pair's body,
            // so subsequent GetOrReserve calls record edges in the dependency graph.
            nestedRegistry.SetCurrentPair(nestedName);

            // C3: use the first declared method's location as the diagnostic anchor for
            // nested diagnostics (not null, so DWARF030 has a non-null location).
            LocationInfo? nestedLocation = null;
            for (var mi = 0; mi < methods.Count; mi++)
            {
                if (methods[mi].IsPartial)
                {
                    // We don't have the original method symbol here, but we can use
                    // a null location — the requirement is only for DWARF030 to be non-null.
                    // For DWARF001/005/007, the path prefix is more important than location.
                    // (LocationInfo from method model is not stored; use null per existing contract.)
                    break;
                }
            }

            // Choose construction strategy for the nested target type.
            var nestedCtor = ConstructorSelector.Select(nestedTgt, diagnostics, nestedLocation, out var nestedObjInitOnly);
            if (nestedCtor is null)
            {
                // DWARF025/026 already reported; skip body emission for this pair.
                nestedRegistry.ClearCurrentPair();
                continue;
            }

            MemberMap[] nestedCtorArgs;
            HashSet<string> nestedConsumed;
            HashSet<string> nestedRequiredMustInit;

            if (nestedObjInitOnly)
            {
                nestedCtorArgs = System.Array.Empty<MemberMap>();
                nestedConsumed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                nestedRequiredMustInit = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // C1: use the per-pair autoNest value (pairAutoNest), NOT classAutoNest.
                if (!ResolveConstructorArguments(nestedCtor, nestedSrc, ctx.SemanticModel.Compilation,
                    nestedLocation, diagnostics, caseInsensitive, System.Array.Empty<(string, string, string?)>(),
                    allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                    pairAutoNest, nestedRegistry, out nestedCtorArgs, out nestedConsumed,
                    nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions))
                {
                    nestedRegistry.ClearCurrentPair();
                    continue;
                }
                nestedRequiredMustInit = ComputeRequiredMustInitialize(nestedCtor, nestedTgt, nestedConsumed);
            }

            // C1: use the per-pair autoNest value (pairAutoNest), NOT classAutoNest.
            var nestedMembers = ResolveMembers(
                nestedSrc, nestedTgt,
                new HashSet<string>(System.StringComparer.Ordinal), // no ignores for synthesized
                ctx.SemanticModel.Compilation,
                nestedLocation, diagnostics, caseInsensitive,
                System.Array.Empty<(string, string, string?)>(), // no explicit maps
                allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                new List<string>(), new List<string>(), // no flatten/reinterpret
                nestedConsumed, nestedRequiredMustInit,
                pairAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions);

            nestedRegistry.ClearCurrentPair();

            // Build a private (non-partial) MapMethodModel for this synthesized pair.
            // IsRecursionCapable is set to false here and patched below after ComputeRecursionCapability().
            var nestedModel = new MapMethodModel(
                MethodName: nestedName,
                Accessibility: "private",
                ReturnTypeFullName: nestedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ParameterTypeFullName: nestedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ParameterName: "s",
                ParameterIsReferenceType: nestedSrc.IsReferenceType,
                Members: EquatableArray.From(nestedMembers),
                BeforeHooks: EquatableArray.From(System.Array.Empty<string>()),
                AfterHooks: EquatableArray.From(System.Array.Empty<HookCall>()),
                IsProjection: false,
                ElementTargetTypeFullName: "",
                ConstructorArguments: EquatableArray.From(nestedCtorArgs),
                IsPartial: false,
                ReturnIsReferenceType: nestedTgt.IsReferenceType,
                IsRecursionCapable: false); // patched below

            pendingNestedModels.Add((nestedModel, nestedName));
        }

        // ── Recursion-capability analysis ────────────────────────────────────────
        // Now that the full dependency graph is known, compute which pairs are on cycles.
        nestedRegistry.ComputeRecursionCapability();

        // ── Plan 19 C2 fix: Preserve-mode universal ctx threading ───────────────
        // Under ReferenceHandling=Preserve, EVERY auto-synthesized object mapper
        // (__DwarfMap_Obj_*) must receive and thread ctx/depth — not just those that are
        // recursion-capable (on a type-graph cycle). Rationale: a shared (diamond) instance
        // has no back-edge and thus is NOT recursion-capable, yet its mapper must still
        // register-before-populate in the identity map so that two references to the same
        // source object deduplicate to ONE target instance (Assert.Same). Restricting ctx
        // to "recursion-capable" pairs leaves non-cyclic shared objects untracked → two
        // distinct target copies → CS7036 when their callers lack ctx (the root bug).
        // Fix: force-mark ALL __DwarfMap_Obj_* pairs as recursion-capable so they all get
        // the (s, ctx, depth) signature and the register-before-populate emission path.
        // None mode is unaffected: isPreserveMode=false skips this block.
        if (isPreserveMode)
        {
            foreach (var (_, name) in pendingNestedModels)
            {
                // Only object-mapper pairs (__DwarfMap_Obj_* prefix). Collection helpers
                // (__DwarfMapColl_*) and dict helpers (__DwarfMapDict_*) already receive
                // the preserve treatment via isPreserve=true in CollectionConverter/DictionaryConverter.
                if (name.StartsWith("__DwarfMap_Obj_", System.StringComparison.Ordinal))
                    nestedRegistry.ForceRecursionCapable(name);
            }
            // Re-run to incorporate the newly forced entries.
            nestedRegistry.ComputeRecursionCapability();
        }

        // Build a set of method names that are recursion-capable (for the public method check).
        var recursionCapableNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (model, name) in pendingNestedModels)
        {
            var isRC = nestedRegistry.IsRecursionCapable(name);
            if (isRC) recursionCapableNames.Add(name);
            // Rebuild the model with the correct IsRecursionCapable flag.
            methods.Add(model with { IsRecursionCapable = isRC });
        }

        // ── Also detect declared public methods on a recursion cycle ────────────
        // Two cases:
        //   Direct: Map(Node n) has member.ConverterMethod == "Map" (self-call).
        //   Indirect: Map(A) calls __DwarfMap_Obj_B which calls Map (mutual cycle).
        //
        // We extend the call graph to include declared methods and run reachability.
        // All_methods_graph: maps methodKey → set of methods it calls.
        //
        // KEY DISAMBIGUATION: Overloaded declared methods (e.g. ToDto(Person) and ToDto(Addr))
        // share the same base name but must be tracked separately. We use:
        //   Single overload  → key = MethodName  (e.g. "Map")
        //   Multiple overloads → key = MethodName + "§" + ParameterTypeFullName
        //     (e.g. "ToDto§global::Demo.Person", "ToDto§global::Demo.Addr")
        //
        // Synthesized methods always use their unique auto-generated name as key.
        // When a synthesized method has a converter referencing a SINGLE-overload declared method,
        // the edge target is just the method name. Callers targeting an overloaded name may
        // traverse all overloads (conservative: at least one variant is reachable).

        // Count declared methods per name to detect overloads.
        var declaredNameCount = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;
            declaredNameCount.TryGetValue(m.MethodName, out var prev);
            declaredNameCount[m.MethodName] = prev + 1;
        }

        // Helper: get the graph key for a declared method.
        string DeclKey(MapMethodModel mm)
            => declaredNameCount.TryGetValue(mm.MethodName, out var cnt) && cnt > 1
               ? mm.MethodName + "§" + mm.ParameterTypeFullName
               : mm.MethodName;

        var allCallGraph = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);

        // Seed with synthesized method edges (already computed in registry, but not accessible here).
        // Re-derive from pending models (synthesized methods always have unique names).
        foreach (var (model, _) in pendingNestedModels)
        {
            var callerName = model.MethodName;
            if (!allCallGraph.ContainsKey(callerName))
                allCallGraph[callerName] = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var mem in model.Members)
                if (mem.ConverterMethod is not null)
                    allCallGraph[callerName].Add(mem.ConverterMethod);
            foreach (var arg in model.ConstructorArguments)
                if (arg.ConverterMethod is not null)
                    allCallGraph[callerName].Add(arg.ConverterMethod);
        }

        // Add declared methods to the graph using disambiguation keys.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;

            var callerKey = DeclKey(m);
            if (!allCallGraph.ContainsKey(callerKey))
                allCallGraph[callerKey] = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var mem in m.Members)
            {
                if (mem.ConverterMethod is null) continue;
                // Resolve the edge target: if the converter is an overloaded declared method,
                // we can't determine which overload without param-type info, so we add edges
                // to ALL overloads of that name.  For non-overloaded names and synthesized
                // names, add the name directly.
                if (declaredNameCount.TryGetValue(mem.ConverterMethod, out var oc) && oc > 1)
                {
                    // Add edges to all OTHER overloads (not the method itself — a converter can't be
                    // a self-call when it was auto-matched to a DIFFERENT overload by parameter type).
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, mem.ConverterMethod, System.StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, System.StringComparison.Ordinal))
                            allCallGraph[callerKey].Add(ovKey);
                    }
                }
                else
                {
                    allCallGraph[callerKey].Add(mem.ConverterMethod);
                }
            }
            foreach (var arg in m.ConstructorArguments)
            {
                if (arg.ConverterMethod is null) continue;
                if (declaredNameCount.TryGetValue(arg.ConverterMethod, out var oc) && oc > 1)
                {
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, arg.ConverterMethod, System.StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, System.StringComparison.Ordinal))
                            allCallGraph[callerKey].Add(ovKey);
                    }
                }
                else
                {
                    allCallGraph[callerKey].Add(arg.ConverterMethod);
                }
            }
        }

        // Inject helper → element-method edges for None-mode ctx-upgrade candidates. A collection/dict
        // helper's internal call to its element method is invisible to this graph (helpers aren't method
        // models), so a cycle routed ONLY through a collection/dict edge (Map → helper → Map) would go
        // undetected and the element method would never get a depth companion. Adding the edge makes the
        // cycle visible → the element method is flagged self-recursive → companion synthesized → the
        // re-synthesis pass below upgrades the helper to depth-guarded ctx threading (no silent SO).
        foreach (var cand in nestedRegistry.CtxUpgradeCandidates)
        {
            if (!allCallGraph.ContainsKey(cand.HelperName))
                allCallGraph[cand.HelperName] = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var em in cand.ElemMethods)
            {
                // Only inject NON-overloaded element methods: a raw overloaded name would be expanded
                // to edges for ALL overloads, manufacturing a false self-cycle (e.g. Map(Person) →
                // List<Addr> helper → Map(Addr) wrongly resolving to Map(Person)). Overloaded
                // self-map-through-collection falls back to the documented None-mode behaviour.
                if (em is not null && !(declaredNameCount.TryGetValue(em, out var oc) && oc > 1))
                    allCallGraph[cand.HelperName].Add(em);
            }
        }

        // For synthesized methods calling overloaded declared methods, also expand edges
        // so DFS can follow the full cycle. If synth-method calls "Map" and there are
        // two overloads "Map§A" and "Map§B", add edges to all variants except self.
        foreach (var callerKey in allCallGraph.Keys.ToList())
        {
            var edges = allCallGraph[callerKey];
            var expandedEdges = new System.Collections.Generic.List<string>();
            foreach (var edge in edges)
            {
                if (declaredNameCount.TryGetValue(edge, out var oc) && oc > 1)
                {
                    // Replace simple name with qualified variants (excluding self to avoid false cycles).
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, edge, System.StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, System.StringComparison.Ordinal))
                            expandedEdges.Add(ovKey);
                    }
                }
                else
                {
                    expandedEdges.Add(edge);
                }
            }
            edges.Clear();
            foreach (var e in expandedEdges) edges.Add(e);
        }

        // Find which declared methods are on a cycle (can reach themselves in allCallGraph).
        var selfRecursivePublicMethods = new HashSet<string>(System.StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;

            var key = DeclKey(m);
            if (CanReach(allCallGraph, key, key))
            {
                selfRecursivePublicMethods.Add(m.MethodName);
                // Add the companion name so call-sites in synthesized methods can reference it.
                var companionName = "__DwarfMap_Depth_" + m.MethodName;
                recursionCapableNames.Add(companionName);
            }
        }

        // ── None+Throw: upgrade collection/dict helpers whose element method is self-recursive ──
        // Now that selfRecursivePublicMethods is known, re-synthesize any recorded None-mode helper
        // whose element/key/value resolved to a now-self-recursive public method: route it through the
        // depth-guarded `__DwarfMap_Depth_<method>` companion and thread (ctx, depth). Adding the helper
        // name to recursionCapableNames makes every referencing member/method pick up ctx threading via
        // the existing patching loops below — so a deep/cyclic graph through a collection edge throws
        // DwarfMappingDepthException instead of a silent StackOverflow. Non-recursive collections are
        // never recorded, so they stay zero-overhead.
        foreach (var cand in nestedRegistry.CtxUpgradeCandidates)
        {
            // Self-recursive AND non-overloaded (overloaded names can't be safely disambiguated to a
            // single companion here — see the call-graph injection note above).
            bool Upgradeable(string? em) => em is not null && selfRecursivePublicMethods.Contains(em)
                && !(declaredNameCount.TryGetValue(em, out var oc) && oc > 1);
            if (!cand.ElemMethods.Any(Upgradeable))
                continue;
            cand.ReSynth(name => Upgradeable(name) ? "__DwarfMap_Depth_" + name : name);
            recursionCapableNames.Add(cand.HelperName);
        }

        // Re-check synthesized methods using the full allCallGraph (which includes declared methods).
        // The registry's edge graph only tracks synthesized→synthesized edges; it misses cycles that
        // go through declared methods (e.g. __DwarfMap_Obj_B → Map → __DwarfMap_Obj_B).
        // Any synthesized method on such a mixed cycle must also be recursion-capable.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (m.IsPartial) continue; // only synthesized methods

            if (!recursionCapableNames.Contains(m.MethodName)
                && CanReach(allCallGraph, m.MethodName, m.MethodName))
            {
                recursionCapableNames.Add(m.MethodName);
                // Re-mark the method model as recursion-capable.
                methods[i] = m with { IsRecursionCapable = true };
            }
        }

        // ── Mark public methods and synthesized methods that call recursion-capable pairs ─
        // The public Map(S s) method needs to create a DwarfRefContext if it calls (directly
        // or indirectly through its members) a recursion-capable synthesized pair.
        // We patch the already-added method models here.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];

            // For self-recursive declared methods: generate a companion and redirect.
            if (m.IsPartial && selfRecursivePublicMethods.Contains(m.MethodName))
            {
                var companionName = "__DwarfMap_Depth_" + m.MethodName;

                // Patch the members/ctor-args of the declared method:
                // (a) self-calls → redirect to companion with depth ctx
                // (b) calls to other self-recursive declared methods → redirect to their companions
                // (c) calls to recursion-capable synthesized methods → add depth ctx
                // (d) already-set ConverterNeedsDepthCtx (e.g. Preserve-mode collection helpers) → keep as-is
                var newMembers2 = m.Members.ToArray();
                for (var mi = 0; mi < newMembers2.Length; mi++)
                {
                    var mem = newMembers2[mi];
                    if (mem.ConverterMethod is null) continue;

                    if (mem.ConverterNeedsDepthCtx)
                    {
                        // Already marked (Preserve collection helper or previously patched).
                    }
                    else if (string.Equals(mem.ConverterMethod, m.MethodName, System.StringComparison.Ordinal))
                    {
                        // Self-call: redirect to companion.
                        newMembers2[mi] = mem with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    }
                    else if (selfRecursivePublicMethods.Contains(mem.ConverterMethod))
                    {
                        // Indirect recursive declared method: redirect to its companion.
                        newMembers2[mi] = mem with { ConverterMethod = "__DwarfMap_Depth_" + mem.ConverterMethod, ConverterNeedsDepthCtx = true };
                    }
                    else if (recursionCapableNames.Contains(mem.ConverterMethod))
                    {
                        // Recursion-capable synthesized method: pass depth ctx.
                        newMembers2[mi] = mem with { ConverterNeedsDepthCtx = true };
                    }
                }
                var newCtorArgs2 = m.ConstructorArguments.ToArray();
                for (var ci = 0; ci < newCtorArgs2.Length; ci++)
                {
                    var arg = newCtorArgs2[ci];
                    if (arg.ConverterMethod is null) continue;

                    if (arg.ConverterNeedsDepthCtx)
                    {
                        // Already marked (Preserve collection helper or previously patched).
                    }
                    else if (string.Equals(arg.ConverterMethod, m.MethodName, System.StringComparison.Ordinal))
                    {
                        newCtorArgs2[ci] = arg with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    }
                    else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs2[ci] = arg with { ConverterMethod = "__DwarfMap_Depth_" + arg.ConverterMethod, ConverterNeedsDepthCtx = true };
                    }
                    else if (recursionCapableNames.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs2[ci] = arg with { ConverterNeedsDepthCtx = true };
                    }
                }

                // Mark public method as needing ctx creation.
                methods[i] = m with
                {
                    IsRecursionCapable = true,
                    MaxDepth = maxDepth,
                    Members = EquatableArray.From(newMembers2),
                    ConstructorArguments = EquatableArray.From(newCtorArgs2),
                };

                // Synthesize the companion: same body, but IsRecursionCapable=true, IsPartial=false.
                var companion = m with
                {
                    MethodName = companionName,
                    Accessibility = "private",
                    IsPartial = false,
                    IsRecursionCapable = true,
                    MaxDepth = maxDepth,
                    Members = EquatableArray.From(newMembers2),
                    ConstructorArguments = EquatableArray.From(newCtorArgs2),
                    // Companion's self-calls also use the companion (already patched above).
                };
                methods.Add(companion);
                continue;
            }

            // Check if any of this method's members/ctor-args uses a recursion-capable synthesized method
            // OR a self-recursive declared method (which must be redirected to the companion)
            // OR a Preserve-mode collection helper that already has ConverterNeedsDepthCtx=true.
            var needsCtx = false;
            var newMembers = m.Members.ToArray();
            for (var mi = 0; mi < newMembers.Length; mi++)
            {
                var member = newMembers[mi];
                if (member.ConverterMethod is null) continue;

                if (member.ConverterNeedsDepthCtx)
                {
                    // Already marked (e.g. Preserve-mode collection helper set by ResolveMembers).
                    needsCtx = true;
                }
                else if (recursionCapableNames.Contains(member.ConverterMethod))
                {
                    newMembers[mi] = member with { ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
                else if (selfRecursivePublicMethods.Contains(member.ConverterMethod))
                {
                    // Redirect call from declared public method to its depth-guarded companion.
                    var companionName = "__DwarfMap_Depth_" + member.ConverterMethod;
                    newMembers[mi] = member with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
            }

            var newCtorArgs = m.ConstructorArguments.ToArray();
            for (var ci = 0; ci < newCtorArgs.Length; ci++)
            {
                var arg = newCtorArgs[ci];
                if (arg.ConverterMethod is null) continue;

                if (arg.ConverterNeedsDepthCtx)
                {
                    // Already marked (e.g. Preserve-mode collection helper set by ResolveConstructorArguments).
                    needsCtx = true;
                }
                else if (recursionCapableNames.Contains(arg.ConverterMethod))
                {
                    newCtorArgs[ci] = arg with { ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
                else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                {
                    var companionName = "__DwarfMap_Depth_" + arg.ConverterMethod;
                    newCtorArgs[ci] = arg with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
            }

            if (needsCtx || (m.IsRecursionCapable && !m.IsPartial))
            {
                var newRc = m.IsRecursionCapable || needsCtx;
                methods[i] = m with
                {
                    IsRecursionCapable = newRc,
                    MaxDepth = m.IsPartial ? maxDepth : m.MaxDepth, // only public methods carry MaxDepth
                    Members = EquatableArray.From(newMembers),
                    ConstructorArguments = EquatableArray.From(newCtorArgs),
                };
                // ── Preserve-mode propagation fix ──────────────────────────────────
                // When a synthesized (non-partial) method is newly marked recursion-capable
                // (because its own members need ctx, e.g. a Preserve-mode List<T> helper),
                // record it in recursionCapableNames immediately so that public declared
                // methods processed LATER in this same loop can see it.
                // This handles the case where public methods come BEFORE their synthesized
                // callees in the methods list (declaration order: public first, synth second).
                // A second pass below handles the reverse order (synth processed first but
                // public was already visited).
                if (!m.IsPartial && newRc)
                    recursionCapableNames.Add(m.MethodName);
            }
        }

        // ── Preserve-mode second pass: propagate ctx to public methods whose synthesized callees
        // became recursion-capable during the loop above but were visited BEFORE their callee.
        // This fixes the ordering problem: public Map(SharingRoot) is added to methods[] before
        // the synthesized __DwarfMap_Obj_...Holder... pair, so the first loop processes the
        // public method before knowing the Holder mapper needs ctx. We now re-check all public
        // declared methods under Preserve mode and patch any member/ctor-arg that calls a
        // newly-added recursionCapableNames entry without ConverterNeedsDepthCtx=true.
        if (isPreserveMode)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsPartial) continue; // only public declared methods
                if (selfRecursivePublicMethods.Contains(m.MethodName)) continue; // already handled above

                var patched = false;
                var newMembers2 = m.Members.ToArray();
                for (var mi = 0; mi < newMembers2.Length; mi++)
                {
                    var mem = newMembers2[mi];
                    if (mem.ConverterMethod is null || mem.ConverterNeedsDepthCtx) continue;
                    if (recursionCapableNames.Contains(mem.ConverterMethod))
                    {
                        newMembers2[mi] = mem with { ConverterNeedsDepthCtx = true };
                        patched = true;
                    }
                }
                var newCtorArgs2 = m.ConstructorArguments.ToArray();
                for (var ci = 0; ci < newCtorArgs2.Length; ci++)
                {
                    var arg = newCtorArgs2[ci];
                    if (arg.ConverterMethod is null || arg.ConverterNeedsDepthCtx) continue;
                    if (recursionCapableNames.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs2[ci] = arg with { ConverterNeedsDepthCtx = true };
                        patched = true;
                    }
                }
                if (patched)
                {
                    methods[i] = m with
                    {
                        IsRecursionCapable = true,
                        MaxDepth = maxDepth,
                        Members = EquatableArray.From(newMembers2),
                        ConstructorArguments = EquatableArray.From(newCtorArgs2),
                    };
                }
            }
        }

        // ── MF-A fix: [MapDerivedType] dispatch method arm ctx threading ────────────
        // Now that recursion-capability is fully resolved, patch any dispatch method
        // (DerivedTypeArms.Count > 0) whose arm converters are recursion-capable (i.e.
        // need ctx+depth forwarding).  This includes Preserve-mode auto-nested pairs
        // (__DwarfMap_Obj_*) which were force-marked recursion-capable in the block above.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (m.DerivedTypeArms.Count == 0) continue; // not a dispatch method

            var patchedArms = m.DerivedTypeArms.ToArray();
            var anyArmNeedsCtx = false;
            for (var ai = 0; ai < patchedArms.Length; ai++)
            {
                var arm = patchedArms[ai];
                if (arm.ConverterNeedsDepthCtx)
                {
                    // Already marked (e.g. captured from TryResolveConversion or previously patched).
                    anyArmNeedsCtx = true;
                }
                else if (recursionCapableNames.Contains(arm.ConverterMethod))
                {
                    patchedArms[ai] = arm with { ConverterNeedsDepthCtx = true };
                    anyArmNeedsCtx = true;
                }
                else if (selfRecursivePublicMethods.Contains(arm.ConverterMethod))
                {
                    // Declared public method on a recursion cycle — redirect to its companion.
                    var companionName = "__DwarfMap_Depth_" + arm.ConverterMethod;
                    patchedArms[ai] = arm with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    anyArmNeedsCtx = true;
                }
            }

            if (anyArmNeedsCtx)
            {
                methods[i] = m with
                {
                    IsRecursionCapable = true,
                    MaxDepth = m.IsPartial ? maxDepth : m.MaxDepth,
                    DerivedTypeArms = EquatableArray.From(patchedArms),
                };
            }
        }
        // ── End MF-A fix ─────────────────────────────────────────────────────────

        // ── MF-B fix: Preserve + [MapDerivedType] dispatch wrapper synthesis ─────────
        // Problem: a container mapper under Preserve has a member like First=Map(animal) where
        // Map(PsvAnimal) is a [MapDerivedType] dispatch method. Each call to the PUBLIC dispatch
        // creates a FRESH DwarfRefContext — so two members sharing the same source object land in
        // different identity maps and never deduplicate.
        //
        // Fix: synthesize a private ctx-accepting dispatch wrapper __DwarfMap_Disp_*(s, ctx, depth)
        // for every public dispatch method that is recursion-capable (arms use ctx) in Preserve mode.
        // The wrapper:
        //   1. Null-guards the source.
        //   2. TryGetReference — returns the cached target if the source was already mapped.
        //   3. Depth-guards against infinite dispatch chains.
        //   4. Dispatches via the same switch expression (forwarding ctx+depth to arm converters).
        //   5. SetReference — caches the result keyed by the BASE source reference.
        //
        // Then patch every member/ctor-arg (in both synthesized and public methods) that calls the
        // PUBLIC dispatch by name to instead call the wrapper (with ConverterNeedsDepthCtx=true).
        // Any public method with patched members is promoted to IsRecursionCapable+IsPreserveMode
        // so the emitter creates a shared DwarfRefContext and threads it through all members.
        var dispatchWrapperByPublicName = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.Ordinal);
        if (isPreserveMode)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.DerivedTypeArms.Count == 0) continue;   // not a dispatch method
                if (!m.IsRecursionCapable) continue;           // arm converters don't need ctx
                if (!m.IsPartial) continue;                    // only public declared dispatch methods

                var wrapperName = NestedMappingRegistry.BuildDispatchWrapperName(
                    m.ParameterTypeFullName, m.ReturnTypeFullName);

                if (!synthesized.ContainsKey(wrapperName))
                {
                    var wrapperCode = BuildDispatchWrapperCode(m, wrapperName);
                    synthesized[wrapperName] = new SynthesizedMethod(wrapperName, wrapperCode);
                }

                dispatchWrapperByPublicName[m.MethodName] = wrapperName;
                recursionCapableNames.Add(wrapperName);
            }

            // Patch: redirect members/ctor-args that call a public dispatch method to the wrapper.
            if (dispatchWrapperByPublicName.Count > 0)
            {
                for (var i = 0; i < methods.Count; i++)
                {
                    var m = methods[i];
                    if (m.DerivedTypeArms.Count > 0) continue; // skip the dispatch methods themselves

                    var patched = false;
                    var newMembers = m.Members.ToArray();
                    for (var mi = 0; mi < newMembers.Length; mi++)
                    {
                        var mem = newMembers[mi];
                        if (mem.ConverterMethod is null || mem.ConverterNeedsDepthCtx) continue;
                        if (dispatchWrapperByPublicName.TryGetValue(mem.ConverterMethod, out var wn))
                        {
                            newMembers[mi] = mem with { ConverterMethod = wn, ConverterNeedsDepthCtx = true };
                            patched = true;
                        }
                    }
                    var newCtorArgs = m.ConstructorArguments.ToArray();
                    for (var ci = 0; ci < newCtorArgs.Length; ci++)
                    {
                        var arg = newCtorArgs[ci];
                        if (arg.ConverterMethod is null || arg.ConverterNeedsDepthCtx) continue;
                        if (dispatchWrapperByPublicName.TryGetValue(arg.ConverterMethod, out var wn))
                        {
                            newCtorArgs[ci] = arg with { ConverterMethod = wn, ConverterNeedsDepthCtx = true };
                            patched = true;
                        }
                    }
                    if (patched)
                    {
                        // Public methods patched to use ctx-accepting wrappers must create a shared
                        // DwarfRefContext. Promote to IsRecursionCapable+IsPreserveMode so the emitter
                        // generates: var __dwarf_ctx = new DwarfRefContext(maxDepth, true); and threads
                        // ctx into all wrapper calls via the register-before-populate path.
                        var newRc = m.IsPartial ? true : m.IsRecursionCapable;
                        methods[i] = m with
                        {
                            Members = EquatableArray.From(newMembers),
                            ConstructorArguments = EquatableArray.From(newCtorArgs),
                            IsRecursionCapable = newRc,
                            MaxDepth = m.IsPartial && !m.IsRecursionCapable ? maxDepth : m.MaxDepth,
                            IsPreserveMode = m.IsPartial ? true : m.IsPreserveMode,
                        };
                    }
                }
            }
        }
        // ── End MF-B fix ─────────────────────────────────────────────────────────

        // ── Plan 19 C2: Preserve mode post-processing ───────────────────────────
        // After recursion-capability is finalised, propagate IsPreserveMode and detect DWARF030.
        if (isPreserveMode)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];

                // ── DWARF030: detect cyclic constructor parameters ─────────────────
                // Two patterns:
                // (A) Explicit cycle: ctor arg has ConverterNeedsDepthCtx = true (calls recursion-capable
                //     synthesized method). The back-edge is injected via ctor → can't register-before-populate.
                // (B) Identity self-map cycle: S == T AND the method has ctor args that copy S? members
                //     by identity (no converter). Example: record ImmutableNode(int V, ImmutableNode? Next)
                //     mapped to itself — Next is copied as s.Next (source ref), not the target. The record
                //     is immutable so we can't fix it up. Even though this method may not be "recursion-capable"
                //     in the type-graph sense (S=T → implicit conversion → no synthesized method), the
                //     DATA can still be cyclic and the ctor arg prevents register-before-populate.
                if (m.ConstructorArguments.Count > 0)
                {
                    // Pattern A: explicit recursion-capable ctor arg whose converter is on a
                    // call-graph cycle that includes the OUTER method. Under Preserve, ALL auto-nested
                    // object mappers are forced recursion-capable for uniform topology tracking, so
                    // ConverterNeedsDepthCtx=true alone is not sufficient — we must also verify that
                    // the converter can reach back to the outer method (i.e. they are on the SAME cycle),
                    // otherwise an acyclic nested mapper (e.g. Address→AddressDto) would be falsely
                    // flagged as cyclic just because it got forced-RC for Preserve threading.
                    // A scalar ctor param (int, string, Guid, enum) will have ConverterMethod=null and
                    // never reaches this branch.
                    var outerMethodKey = DeclKey(m);
                    foreach (var ctorArg in m.ConstructorArguments)
                    {
                        if (ctorArg.ConverterMethod is not null && ctorArg.ConverterNeedsDepthCtx
                            && CanReach(allCallGraph, ctorArg.ConverterMethod, outerMethodKey))
                        {
                            var loc = (LocationInfo?)null;
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.CyclicConstructorParameter,
                                loc,
                                ctorArg.TargetName));
                        }
                    }

                    // Pattern B: self-map (S == T) with any ctor arg that has no converter.
                    // For S==T, direct-assignment ctor args copy the source reference into the target.
                    // If the source has a cycle (n.Next = n), the target's ctor arg will hold the source,
                    // not the target. Since the type is immutable (has ctor args), we can't fix this up.
                    // We only flag ctor args that are of reference type (not int/string/etc.) — but since
                    // we don't have type info here, we flag ALL ctor args when the method is S→S and
                    // recursion-capable (proven by members using ConverterNeedsDepthCtx).
                    // More precisely: the method must be recursion-capable to be affected.
                    if (m.IsRecursionCapable
                        && string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, System.StringComparison.Ordinal))
                    {
                        foreach (var ctorArg in m.ConstructorArguments)
                        {
                            // Only flag args with no converter (identity copy of potentially cyclic member).
                            // Args with a converter have already been checked above (Pattern A) or map scalars.
                            if (ctorArg.ConverterMethod is null && !ctorArg.ConverterNeedsDepthCtx)
                            {
                                var loc = (LocationInfo?)null;
                                diagnostics.Add(new DiagnosticInfo(
                                    DiagnosticDescriptors.CyclicConstructorParameter,
                                    loc,
                                    ctorArg.TargetName));
                            }
                        }
                    }
                }

                // Only recursion-capable methods need the Preserve-mode register-before-populate emission.
                if (!m.IsRecursionCapable) continue;

                // Mark the method as Preserve mode.
                methods[i] = m with { IsPreserveMode = true };
            }

            // Pattern B (public declared methods): detect S==T self-recursive declared methods
            // where the target has ctor args. These are recursion-capable by definition.
            // The check above already covers it since we iterate ALL methods.
            // Additional check: for public partial methods that are Preserve+RecursionCapable,
            // check if the SOURCE type == RETURN type with ctor args — this covers user-declared
            // self-mappers like Map(ImmutableNode n) → ImmutableNode.
            // (This is already covered by the loop above for cases where m.IsRecursionCapable.)
            //
            // Special case: S==T where the method is NOT recursion-capable (pure identity copy,
            // no auto-nest synthesized method). This happens for record self-maps. We detect it
            // separately here because the isRecursionCapable gate filters them out above.
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.ConstructorArguments.Count == 0) continue;
                if (m.IsRecursionCapable) continue; // already handled above
                if (!m.ParameterIsReferenceType) continue; // value types excluded

                // S == T (same type) with ctor args and NOT recursion-capable:
                // This is the "record ImmutableNode(ImmutableNode? Next)" self-map case.
                // The method isn't recursion-capable because S=T uses implicit conversion (no auto-nest),
                // but at RUNTIME a cyclic ImmutableNode CAN exist. Under Preserve mode, this is
                // an unsupported pattern → DWARF030 for the cyclic ctor args.
                if (string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, System.StringComparison.Ordinal))
                {
                    // Flag ctor args that have the same type as the source (cyclic back-edge).
                    // Since we don't have type info here, flag ALL non-scalar ctor args where
                    // the source name suggests it's a complex member (has a converter or is the cycle).
                    // Conservative approach: flag all ctor args with no converter when S==T.
                    // The scalar ctor args (int, string, etc.) would also get flagged — this is
                    // acceptable since the real issue is that ANY ctor arg in this scenario is suspect
                    // (the ENTIRE pattern of immutable S=T mapping with cycles is broken).
                    // In practice, DWARF030 is a COMPILE ERROR — the user MUST fix the type design.
                    foreach (var ctorArg in m.ConstructorArguments)
                    {
                        var loc = (LocationInfo?)null;
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.CyclicConstructorParameter,
                            loc,
                            ctorArg.TargetName));
                    }
                }
            }
        }

        // ── OnCycle = SetNull post-processing (None mode) ────────────────────────
        // After recursion-capability is finalised, flag every recursion-capable method so the
        // emitter wraps its body in the on-stack guard (TryEnterNode/ExitNode) and the public
        // entry allocates DwarfRefContext(maxDepth, setNull: true). Only reference-type pairs
        // can form a reference cycle, so value-type sources are left untouched (they keep the
        // plain depth-guarded None body — a struct cannot be its own ancestor on the stack).
        // This is the None-mode analogue of the Preserve post-pass above, but far simpler:
        // construction is unchanged (no register-before-populate, no DWARF030, no dispatch
        // wrapper) — the guard only nulls a re-entrant back-edge.
        if (isSetNullMode)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsRecursionCapable) continue;       // only pairs that can re-enter
                if (!m.ParameterIsReferenceType) continue; // value types never form ref cycles
                methods[i] = m with { IsSetNullMode = true };
            }
        }

        // Report DWARF031 if the registry cap was exceeded.
        if (nestedRegistry.CapExceeded)
        {
            // Use a null location — the cap is a generator-level limit, not method-specific.
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.DeepNestingLimit,
                null,
                nestedRegistry.CapTriggerType));
        }

        // CollectRoundTrips must be called before capturing diagnostics so that DWARF020/021 are included.
        var roundTrips = CollectRoundTrips(classSymbol, ctx.SemanticModel.Compilation, diagnostics);

        return new MapperClassModel(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            AccessibilityText(classSymbol.DeclaredAccessibility),
            EquatableArray.From(methods),
            EquatableArray.From(diagnostics),
            EquatableArray.From(synthesized.Values.OrderBy(m => m.Name, System.StringComparer.Ordinal)),
            EquatableArray.From(roundTrips));
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
        IReadOnlyList<(string Target, bool IsConstant, TypedConstant Value, string? Use)>? mapValues = null,
        IReadOnlyList<(string Name, ITypeSymbol ReturnType)>? valueProviders = null)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        var sourceGroups = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var writableByName = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType))
        {
            writableByName[m.Name] = m.Type;
        }

        var result = new List<MemberMap>();
        var handledTargets = new HashSet<string>(System.StringComparer.Ordinal);

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
            flattenInfos.Add((match.Value.Name, leaves));
        }

        // EXPLICIT: [MapProperty] pairs take precedence and are matched by exact name.
        var explicitSeen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var (srcName, tgtName, useMethod) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                // More than one [MapProperty] for the same destination.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            handledTargets.Add(tgtName);

            // If this explicit mapping targets a constructor parameter (already consumed), skip it here
            // UNLESS the member is `required` and the ctor lacks [SetsRequiredMembers] — in that case
            // the member must also appear in the object initializer to satisfy CS9035.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(tgtName)
                && (requiredMustInitialize is null || !requiredMustInitialize.Contains(tgtName)))
            {
                continue;
            }

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
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PathNullableHop, location,
                        $"[MapProperty] source path '{srcName}' traverses a nullable member; a null interior value throws at runtime"));
                }
            }
            else
            {
                srcMatch = ReadableMembers(sourceType)
                    .Where(m => System.StringComparer.Ordinal.Equals(m.Name, srcName))
                    .Select(m => (ITypeSymbol?)m.Type)
                    .FirstOrDefault();
            }
            if (srcMatch is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, tgtName, diagnostics, out var conv, out var nullH, out var convNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
            {
                result.Add(new MemberMap(tgtName, srcName, conv, nullH, ConverterNeedsDepthCtx: convNeedsCtx,
                    SourceIsNullableRef: SourceMayBeNullRef(srcMatch)));
            }
        }

        // MAPVALUE: constant / computed values assigned to a destination member (no source). Processed
        // after [MapProperty] (so conflicts are caught) and before AUTO matching. A [MapValue]'d target
        // counts as mapped, suppressing DWARF001.
        foreach (var mv in mapValues ?? System.Array.Empty<(string Target, bool IsConstant, TypedConstant Value, string? Use)>())
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
            if (!writableByName.TryGetValue(mvTgt, out var mvTgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid, location,
                    $"[MapValue] target '{mvTgt}' is not a writable destination member"));
                continue;
            }

            if (mv.IsConstant)
            {
                if (!TryFormatConstant(mv.Value, mvTgtType, compilation, out var literal, out var why))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueTypeMismatch, location, why));
                    continue;
                }
                result.Add(new MemberMap(mvTgt, "", ValueExpression: literal));
            }
            else if (mv.Use is not null)
            {
                var provider = (valueProviders ?? System.Array.Empty<(string Name, ITypeSymbol ReturnType)>())
                    .FirstOrDefault(p => System.StringComparer.Ordinal.Equals(p.Name, mv.Use));
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
        var targets = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();
        foreach (var target in targets)
        {
            // Skip members already consumed as constructor parameters (positional record members appear
            // as both ctor params AND init properties — must not double-assign).
            // EXCEPTION: `required` members whose ctor lacks [SetsRequiredMembers] must also be set in
            // the object initializer (CS9035), so do NOT skip them.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(target.Name)
                && (requiredMustInitialize is null || !requiredMustInitialize.Contains(target.Name)))
            {
                continue;
            }

            if (handledTargets.Contains(target.Name) || ignores.Contains(target.Name))
            {
                continue;
            }

            if (!sourceGroups.TryGetValue(target.Name, out var matches))
            {
                var flatMatches = new List<(string Root, string Leaf, ITypeSymbol LeafType)>();
                foreach (var fi in flattenInfos)
                {
                    foreach (var leaf in fi.Leaves)
                    {
                        if (comparer.Equals(leaf.Name, target.Name))
                        {
                            flatMatches.Add((fi.Root, leaf.Name, leaf.Type));
                        }
                    }
                }

                if (flatMatches.Count > 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten, location, target.Name));
                    continue;
                }
                if (flatMatches.Count == 1)
                {
                    var fm = flatMatches[0];
                    if (TryResolveConversion(compilation, fm.LeafType, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var fconv, out var fnull, out var fneedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
                    {
                        result.Add(new MemberMap(target.Name, fm.Root + "." + fm.Leaf, fconv, fnull, ConverterNeedsDepthCtx: fneedsCtx,
                            SourceIsNullableRef: SourceMayBeNullRef(fm.LeafType)));
                    }
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, target.Name));
                continue;
            }

            var source = matches[0];
            if (reinterpretMembers.Contains(target.Name))
            {
                if (source.Type is IArrayTypeSymbol sa && target.Type is IArrayTypeSymbol ta
                    && sa.ElementType.IsUnmanagedType && ta.ElementType.IsUnmanagedType)
                {
                    var blit = CollectionConverter.SynthesizeBlit(synthesized, source.Type, sa.ElementType, ta.ElementType);
                    result.Add(new MemberMap(target.Name, source.Name, blit,
                        SourceIsNullableRef: SourceMayBeNullRef(source.Type)));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid, location, target.Name));
                }
                continue;
            }
            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var conv, out var nullH, out var needsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
            {
                result.Add(new MemberMap(target.Name, source.Name, conv, nullH, ConverterNeedsDepthCtx: needsCtx,
                    SourceIsNullableRef: SourceMayBeNullRef(source.Type)));
            }
        }

        // READ-ONLY destinations with a matching source (silent-loss guard).
        // A read-only member satisfied via a constructor parameter is already mapped — no diagnostic.
        foreach (var readOnly in ReadOnlyMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (handledTargets.Contains(readOnly.Name) || ignores.Contains(readOnly.Name))
            {
                continue;
            }
            // Satisfied via ctor param → not a silent loss.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(readOnly.Name))
            {
                continue;
            }
            if (sourceGroups.ContainsKey(readOnly.Name))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReadOnlyDestinationMember, location, readOnly.Name));
            }
        }

        // A [Reinterpret] name that matches no writable destination member is a typo — never silently ignore it.
        // A [Reinterpret] member that is ALSO in [MapIgnore] is a contradiction — report DWARF012.
        if (reinterpretMembers.Count > 0)
        {
            var writableNames = new HashSet<string>(WritableMembers(targetType).Select(m => m.Name), System.StringComparer.Ordinal);
            foreach (var rm in reinterpretMembers)
            {
                if (ignores.Contains(rm))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, rm));
                }
                else if (!writableNames.Contains(rm))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid, location, rm));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// For each constructor parameter, find a matching source member and resolve the conversion.
    /// Every parameter is mandatory — if any fails, DWARF024 is reported and the method returns false.
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
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        // Build explicit-maps index: target (param) name → source name (exact match).
        var explicitForParams = new Dictionary<string, (string Source, string? Use)>(System.StringComparer.Ordinal);
        foreach (var (srcName, tgtName, use) in explicitMaps)
        {
            explicitForParams[tgtName] = (srcName, use);
        }

        var readableByName = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var args = new List<MemberMap>();
        // Case-insensitive set for deduplication (positional record param names can differ in case).
        consumedParams = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var allOk = true;

        foreach (var param in ctor.Parameters)
        {
            // 1. Check for an explicit [MapProperty(src, paramName)] override.
            if (explicitForParams.TryGetValue(param.Name, out var explicitInfo))
            {
                var srcList = ReadableMembers(sourceType)
                    .Where(m => System.StringComparer.Ordinal.Equals(m.Name, explicitInfo.Source))
                    .ToList();
                if (srcList.Count == 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, explicitInfo.Source));
                    allOk = false;
                    continue;
                }

                var srcType = srcList[0].Type;
                if (TryResolveConversion(compilation, srcType, param.Type, explicitInfo.Use,
                    allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy,
                    location, param.Name, diagnostics, out var eConv, out var eNull,
                    out var eNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
                {
                    args.Add(new MemberMap(param.Name, explicitInfo.Source, eConv, eNull, ConverterNeedsDepthCtx: eNeedsCtx,
                        SourceIsNullableRef: SourceMayBeNullRef(srcType)));
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
                out var needsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
            {
                args.Add(new MemberMap(param.Name, srcMember.Name, conv, nullH, ConverterNeedsDepthCtx: needsCtx,
                    SourceIsNullableRef: SourceMayBeNullRef(srcMember.Type)));
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
    /// Reads [MapDerivedType&lt;TSource,TTarget&gt;] (generic) and
    /// [MapDerivedType(typeof(TSource),typeof(TTarget))] (non-generic) annotations from a method.
    /// Returns raw pairs of (srcType, tgtType) INamedTypeSymbol — not yet validated.
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
                    "DwarfMapper.MapDerivedTypeAttribute<", System.StringComparison.Ordinal) == true
                && cls.TypeArguments.Length == 2
                && cls.TypeArguments[0] is INamedTypeSymbol gSrc
                && cls.TypeArguments[1] is INamedTypeSymbol gTgt)
            {
                result.Add((gSrc, gTgt));
                continue;
            }

            // Non-generic form: [MapDerivedType(typeof(TSource), typeof(TTarget))]
            var fqn = cls.ToDisplayString();
            if (fqn == "DwarfMapper.MapDerivedTypeAttribute"
                && attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol nSrc
                && attr.ConstructorArguments[1].Value is INamedTypeSymbol nTgt)
            {
                result.Add((nSrc, nTgt));
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the inheritance depth of <paramref name="type"/> (number of base classes between
    /// it and System.Object). Interfaces return depth 0.
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
    /// Sorts derived-type arms so that more-derived (more-specific) types appear before
    /// less-derived ones (most-derived-first).  For class hierarchies, uses
    /// <see cref="InheritanceDepth"/>.  For interface hierarchies (where all depths are 0),
    /// uses pairwise <see cref="HasImplicitConversion"/> assignability:
    /// if A is assignable to B (A is more derived / more specific than B), A comes first.
    /// Stable for unrelated/equal pairs (preserves declaration order).
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
            if (bToA && !aToB) return  1; // B is more derived than A → B first
            // Neither or both assignable: fall back to class-hierarchy depth, then declaration order.
            var depthDiff = InheritanceDepth(b.arm.Src) - InheritanceDepth(a.arm.Src);
            if (depthDiff != 0) return depthDiff;
            return a.idx - b.idx; // stable: preserve original declaration order
        });
        return indexed.Select(x => x.arm).ToList();
    }

    /// <summary>
    /// DWARF036: detects mutually-unorderable interface or abstract source arms.
    ///
    /// Two arm source types A and B are "ambiguous" when:
    ///   1. Neither HasImplicitConversion(A,B) nor HasImplicitConversion(B,A) — they are unorderable.
    ///   2. At least one of A or B is an interface or abstract class — meaning a concrete type
    ///      could simultaneously satisfy both arms (e.g. class C : IFoo, IBar).
    ///
    /// Rationale: if both types are concrete (non-abstract classes), a concrete runtime instance
    /// can match at most ONE arm by TypeKind/IsAbstract rules (its exact runtime type is one class),
    /// so unrelated concrete-vs-concrete arms are not ambiguous in practice.
    ///
    /// Fires per-pair so multiple ambiguous pairings each produce a separate diagnostic.
    /// </summary>
    private static void DetectAmbiguousInterfaceArms(
        List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)> arms,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics)
    {
        for (var i = 0; i < arms.Count; i++)
        {
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
    }

    /// <summary>
    /// Synthesizes the source code for a Preserve-mode dispatch wrapper that accepts a shared
    /// <c>DwarfRefContext</c> and threads it to arm converters.
    ///
    /// The wrapper does the identity-map TryGetReference/SetReference dance around the dispatch switch
    /// so that when a container helper (e.g. <c>__DwarfMap_Obj_Container_*</c>) calls the wrapper
    /// twice with the SAME source reference, the second call returns the already-mapped target — i.e.
    /// <see cref="Assert.Same"/> topology fidelity under <c>ReferenceHandling = Preserve</c>.
    ///
    /// Pattern (for src=PsvAnimal, tgt=PsvAnimalDto):
    /// <code>
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
        var sb = new System.Text.StringBuilder();
        var p   = dispatchMethod.ParameterName;
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

    private static bool TryResolveConversion(
        Compilation compilation, ITypeSymbol srcType, ITypeSymbol tgtType, string? useMethod,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics,
        out string? converterMethod, out Model.NullHandling nullHandling,
        out bool converterNeedsCtx,
        bool autoNest = false,
        NestedMappingRegistry? nestedRegistry = null,
        bool nullAsNull = false,
        bool isPreserve = false,
        bool allowInterfaceSrc = false,
        bool isSetNull = false,
        bool implicitConversions = true)
    {
        converterMethod = null;
        nullHandling = Model.NullHandling.None;
        converterNeedsCtx = false;

        if (useMethod is not null)
        {
            foreach (var m in allMethods)
            {
                if (string.Equals(m.Name, useMethod, System.StringComparison.Ordinal)
                    && HasImplicitConversion(compilation, srcType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                {
                    // B4 / DWARF032: Under Preserve mode, a Use= converter pointing to an
                    // arbitrary user function cannot participate in reference-identity tracking —
                    // the generator does not own its body and cannot thread DwarfRefContext into it.
                    // A shared/cyclic reference-type object routed through this method will be
                    // duplicated rather than de-duplicated, silently producing wrong topology.
                    // Only fire for REFERENCE-TYPE targets: scalars (int, Guid, enum, string, etc.)
                    // are never tracked by the identity map and Use= on a scalar is fine.
                    if (isPreserve && tgtType.IsReferenceType)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.ReferenceHandlingUseConverter,
                            location, targetName));
                        // Report the diagnostic AND return false so no silent wrong code is emitted.
                        return false;
                    }
                    converterMethod = m.Name;
                    return true;
                }
            }
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
            return false;
        }

        if (DictionaryConverter.TryResolve(srcType, tgtType,
                out var srcKey, out var srcVal, out var tgtKey, out var tgtVal,
                out var dictHasCount, out var dictTargetKind))
        {
            // A3: determine effective null-as-null for the OUTER dict helper based on target nullability.
            // If nullAsNull=true but the target dict type is non-nullable, fall back to AsEmpty
            // to prevent CS8601 (nullable helper assigned to non-nullable field).
            bool dictEffectiveNullAsNull = nullAsNull && IsNullableReferenceType(tgtType);

            // A1: propagate nullAsNull to nested key/value converters so nullable elements
            // (e.g. the value type List<int>? in Dictionary<string, List<int>?>) generate
            // helpers that preserve null instead of silently mapping to empty.
            if (!TryResolveConversion(compilation, srcKey, tgtKey, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var keyConv, out var keyNull, out var keyNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve: isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
                return false;
            if (!TryResolveConversion(compilation, srcVal, tgtVal, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var valConv, out var valNull, out var valNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve: isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
                return false;
            // Preserve OR SetNull: if the key/value converter is an auto-nested object mapper, force it RC
            // so it carries (ctx, depth) and the dict helper threads the shared context into it — this is
            // what lets a cycle routed through a dictionary value break (SetNull) or depth-cap.
            if ((isPreserve || isSetNull) && nestedRegistry is not null)
            {
                if (keyConv is not null && keyConv.StartsWith("__DwarfMap_Obj_", System.StringComparison.Ordinal))
                { nestedRegistry.ForceRecursionCapable(keyConv); keyNeedsCtx = true; }
                if (valConv is not null && valConv.StartsWith("__DwarfMap_Obj_", System.StringComparison.Ordinal))
                { nestedRegistry.ForceRecursionCapable(valConv); valNeedsCtx = true; }
            }
            converterMethod = DictionaryConverter.Synthesize(synthesized, srcType, tgtKey, tgtVal,
                dictHasCount, dictTargetKind, keyConv, keyNull, valConv, valNull, dictEffectiveNullAsNull,
                isPreserve, keyNeedsCtx, valNeedsCtx);
            // The dict helper threads (ctx, depth) when it register-before-fills (Preserve mutable) OR a
            // key/value converter is recursion-capable (Preserve, or None/SetNull self-referential value).
            bool isMutableDict = dictTargetKind != DictionaryConverter.DictTargetKind.ImmutableDictionary
                              && dictTargetKind != DictionaryConverter.DictTargetKind.IImmutableDictionary;
            converterNeedsCtx = (isPreserve && isMutableDict) || keyNeedsCtx || valNeedsCtx;

            // None+Throw: a key/value resolved to a PUBLIC declared method. Record a re-synthesis
            // closure so the post-pass can upgrade this dict helper if that method is self-recursive.
            if (!isPreserve && !isSetNull && nestedRegistry is not null)
            {
                bool KeyIsPublicObj = keyConv is not null && !keyNeedsCtx && !keyConv.StartsWith("__Dwarf", System.StringComparison.Ordinal)
                    && tgtKey is INamedTypeSymbol tk && IsMappableObjectPair(compilation, srcKey, tk);
                bool ValIsPublicObj = valConv is not null && !valNeedsCtx && !valConv.StartsWith("__Dwarf", System.StringComparison.Ordinal)
                    && tgtVal is INamedTypeSymbol tv && IsMappableObjectPair(compilation, srcVal, tv);
                if (KeyIsPublicObj || ValIsPublicObj)
                {
                    var hName = converterMethod!;
                    var elems = new List<string>();
                    if (KeyIsPublicObj) elems.Add(keyConv!);
                    if (ValIsPublicObj) elems.Add(valConv!);
                    var cSrc = srcType; var cTk = tgtKey; var cTv = tgtVal; var cHas = dictHasCount; var cKind = dictTargetKind;
                    var cKeyConv = keyConv; var cKeyNull = keyNull; var cValConv = valConv; var cValNull = valNull;
                    var cNullAsNull = dictEffectiveNullAsNull;
                    nestedRegistry.RecordCtxUpgradeCandidate(hName, elems.ToArray(), resolve =>
                    {
                        var nk = cKeyConv; var nkCtx = false;
                        if (KeyIsPublicObj) { var r = resolve(cKeyConv!); if (!string.Equals(r, cKeyConv, System.StringComparison.Ordinal)) { nk = r; nkCtx = true; } }
                        var nv = cValConv; var nvCtx = false;
                        if (ValIsPublicObj) { var r = resolve(cValConv!); if (!string.Equals(r, cValConv, System.StringComparison.Ordinal)) { nv = r; nvCtx = true; } }
                        DictionaryConverter.SynthesizeInPlace(synthesized, hName, cSrc, cTk, cTv, cHas, cKind,
                            nk, cKeyNull, nkCtx, nv, cValNull, nvCtx, cNullAsNull);
                    });
                }
            }
            return true;
        }

        if (CollectionConverter.TryResolve(srcType, tgtType,
                out var srcElem, out var tgtElem, out var collShape, nullAsNull))
        {
            if (collShape.Target == CollectionConverter.TargetKind.Array && collShape.SourceIsArray
                && BlittableProof.CanReinterpret(srcElem, tgtElem))
            {
                converterMethod = CollectionConverter.SynthesizeBlit(synthesized, srcType, srcElem, tgtElem);
                return true;
            }

            // SIMD widening fast-path: array→array of a lossless primitive widen pair (e.g. int[]→long[],
            // float[]→double[]) → Vector.Widen. Identical result to the scalar implicit widen; reflection-free.
            // Comes AFTER blit (same-size pairs blit; widen pairs differ in size so CanReinterpret is false).
            if (collShape.Target == CollectionConverter.TargetKind.Array && collShape.SourceIsArray
                && CollectionConverter.IsWidenPair(srcElem, tgtElem))
            {
                converterMethod = CollectionConverter.SynthesizeSimdWiden(synthesized, srcType, srcElem, tgtElem);
                return true;
            }

            // A3: determine effective null-as-null for the OUTER collection helper based on target nullability.
            // Reference-type collections: fall back to AsEmpty when target is non-nullable to prevent CS8601.
            // ImmutableArray<T>?: CollectionConverter.TryResolve already handles Nullable<ImmutableArray<T>>
            // by unwrapping it and setting nullAsNull=true in the shape, so collShape.NullAsNull is already
            // correct and we just need to preserve it.
            bool collEffectiveNullAsNull = collShape.Target == CollectionConverter.TargetKind.ImmutableArray
                // ImmutableArray: shape.NullAsNull is authoritative (set by TryResolve for Nullable<> unwrapping).
                ? collShape.NullAsNull
                // Reference-type collections: only AsNull when target field is nullable ref type.
                : (nullAsNull && IsNullableReferenceType(tgtType));

            // A1: propagate nullAsNull to the element converter so nullable elements
            // (e.g. element type List<int>? inside List<List<int>?>) generate helpers
            // that preserve null instead of silently mapping to empty.
            if (!TryResolveConversion(compilation, srcElem, tgtElem, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var elemConv, out var elemNull, out var elemNeedsCtx, autoNest, nestedRegistry, nullAsNull, isPreserve: isPreserve, isSetNull: isSetNull, implicitConversions: implicitConversions))
                return false; // element diagnostic already reported by the recursive call

            // Preserve OR SetNull: if the element converter is an auto-nested object mapper, force it
            // recursion-capable so it gets the (ctx, depth) signature — the collection helper will call it
            // with (elem, ctx, depth + 1), threading ONE shared context across the collection edge. This
            // is what lets a cycle routed through a collection break (SetNull → back-edge null) or
            // depth-cap, instead of the element re-entering the public entry (fresh context → StackOverflow).
            if ((isPreserve || isSetNull) && elemConv is not null
                && elemConv.StartsWith("__DwarfMap_Obj_", System.StringComparison.Ordinal)
                && nestedRegistry is not null)
            {
                nestedRegistry.ForceRecursionCapable(elemConv);
                elemNeedsCtx = true;
            }

            // Apply effective nullAsNull (A3: may be false even when nullAsNull=true if target is non-nullable).
            if (collEffectiveNullAsNull != nullAsNull)
                collShape = new CollectionConverter.Shape(collShape.Target, collShape.SourceIsArray, collShape.Count, collEffectiveNullAsNull);

            converterMethod = CollectionConverter.Synthesize(synthesized, srcType, srcElem, tgtElem, collShape, elemConv, elemNull, isPreserve, elemNeedsCtx);
            // Thread (ctx, depth) when the collection register-before-fills (Preserve mutable) OR its
            // element is recursion-capable (Preserve, or None/SetNull self-referential element).
            converterNeedsCtx = (isPreserve && CollectionConverter.IsMutableReferenceCollection(collShape.Target)) || elemNeedsCtx;

            // None+Throw: the element resolved to a PUBLIC declared method (e.g. a self-map `Map`).
            // Record a re-synthesis closure; if that method turns out self-recursive, the post-pass
            // upgrades this helper to thread ctx into the depth-guarded companion (no silent SO).
            if (!isPreserve && !isSetNull && !elemNeedsCtx && nestedRegistry is not null
                && elemConv is not null && !elemConv.StartsWith("__Dwarf", System.StringComparison.Ordinal)
                && tgtElem is INamedTypeSymbol tgtElemNamed
                && IsMappableObjectPair(compilation, srcElem, tgtElemNamed))
            {
                var hName = converterMethod!;
                var capSrc = srcType; var capElem = srcElem; var capTgt = tgtElem; var capShape = collShape; var capNull = elemNull;
                nestedRegistry.RecordCtxUpgradeCandidate(hName, new[] { elemConv }, resolve =>
                    CollectionConverter.SynthesizeInPlace(synthesized, hName, capSrc, capElem, capTgt, capShape, resolve(elemConv), capNull));
            }
            return true;
        }

        // ── DWARF027: target is collection/dict-shaped but not in the supported taxonomy ──
        // Check this BEFORE the implicit-conversion / object-field-mapping fallbacks so the user
        // gets a loud diagnostic instead of a wrong (or silent) mapping.
        if (IsUnsupportedCollectionTarget(tgtType))
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnsupportedCollectionTarget,
                location, targetName));
            return false;
        }

        if (HasImplicitConversion(compilation, srcType, tgtType))
        {
            // Cross-category numeric (integer ↔ floating/decimal, e.g. int→double, int→float) is implicit
            // in C# but crosses kinds (and int→float / long→double silently lose precision). Same-category
            // widening (int→long, float→double) is NOT flagged. DWARF038: suggestion / strict-mode error.
            if (IsCrossCategoryNumeric(srcType, tgtType))
                EmitImplicitConversionDiag(diagnostics, location, targetName, srcType, tgtType, "cross-category numeric", implicitConversions);
            return true; // direct assignment
        }

        // Both nullable: T? → U? with a non-implicit inner T→U. Null-preserving (null → null).
        // Must come before the source-nullable branch so that T?→U? with a synthesized inner
        // conversion resolves to NullableProject rather than ThrowIfNull/ValueOrDefault.
        if (IsNullableValue(srcType, out var bothSrcU) && IsNullableValue(tgtType, out var bothTgtU))
        {
            if (TryResolveConversion(compilation, bothSrcU, bothTgtU, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics,
                    out var innerNN, out _, out _, autoNest, nestedRegistry, nullAsNull, isPreserve: false) && innerNN is not null)
            {
                converterMethod = innerNN;
                nullHandling = Model.NullHandling.NullableProject;
                return true;
            }
            // Inner unresolved or has no converter (implicit, already caught above) — fall through.
        }

        if (IsNullableValue(srcType, out var underlying))
        {
            // First check the simple implicit-conversion path (int? → int, int? → long, etc.)
            if (HasImplicitConversion(compilation, underlying, tgtType))
            {
                nullHandling = nullStrategy == NullStrategy.SetDefault ? Model.NullHandling.ValueOrDefault : Model.NullHandling.ThrowIfNull;
                return true;
            }

            // Recurse: try to resolve a conversion from the underlying (non-nullable) type to tgtType.
            // This handles cases like E1? → E2 where E1 → E2 requires a synthesized conversion.
            // Guard: 'underlying' is not itself nullable (Nullable<Nullable<T>> is illegal in C#).
            if (TryResolveConversion(compilation, underlying, tgtType, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics,
                    out var innerConv, out _, out _, autoNest, nestedRegistry, nullAsNull, isPreserve: false))
            {
                nullHandling = nullStrategy == NullStrategy.SetDefault ? Model.NullHandling.ValueOrDefault : Model.NullHandling.ThrowIfNull;
                converterMethod = innerConv; // may be null (direct assign after unwrap) or a synthesized method
                return true;
            }

            // Fall through — let the rest of TryResolveConversion attempt further resolutions.
        }

        // Target-nullable composition: non-nullable src → T? (nullable target).
        // When the source is NOT nullable but the target IS nullable, resolve src→underlying
        // and let the implicit T→T? lift do the rest (valid C# assignment).
        // Scope: non-nullable source only. nullable-source + nullable-target (T?→U?) is a
        // documented follow-up (complex null-semantics; left as DWARF005 for now).
        if (!IsNullableValue(srcType, out _) && IsNullableValue(tgtType, out var tgtUnderlying))
        {
            if (TryResolveConversion(compilation, srcType, tgtUnderlying, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics,
                    out var innerConvT, out _, out _, autoNest, nestedRegistry, nullAsNull, isPreserve: false))
            {
                converterMethod = innerConvT; // returns U; assigned to U? field via implicit U→U?
                // nullHandling stays None — source is non-null, always yields a value
                return true;
            }
            // Did not resolve — fall through to DWARF005
            return false;
        }

        // User-provided auto-candidate methods (no Use= annotation, auto-matched by type).
        // Checked BEFORE built-in synthesized converters (NumericConverter, ParsableConverter)
        // so that a user method can intentionally shadow the built-in behavior.
        // Two sources of user candidates:
        //   1. autoCandidates  — partial mapper methods (S → D object-level mappers)
        //   2. allMethods      — non-partial scalar converter helpers (e.g. int Shrink(long v))
        //      These are already in allMethods; excluding partials avoids double-counting mappers.
        string? found = null;
        foreach (var c in autoCandidates)
        {
            if (HasImplicitConversion(compilation, srcType, c.ParamType)
                && HasImplicitConversion(compilation, c.ReturnType, tgtType))
            {
                if (found is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion, location, targetName));
                    return false;
                }
                found = c.Name;
            }
        }
        // Also search all non-partial user methods (scalar converters not declared as partial mappers).
        foreach (var m in allMethods)
        {
            // Skip methods that are already in autoCandidates (partial mapper methods).
            if (autoCandidates.Any(ac => string.Equals(ac.Name, m.Name, System.StringComparison.Ordinal)
                    && SymbolEqualityComparer.Default.Equals(ac.ParamType, m.ParamType)
                    && SymbolEqualityComparer.Default.Equals(ac.ReturnType, m.ReturnType)))
                continue;
            if (HasImplicitConversion(compilation, srcType, m.ParamType)
                && HasImplicitConversion(compilation, m.ReturnType, tgtType))
            {
                if (found is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion, location, targetName));
                    return false;
                }
                found = m.Name;
            }
        }

        if (found is not null)
        {
            // Plan 19 C2b: Under Preserve OR SetNull mode, if the found auto-candidate is a PUBLIC
            // partial mapper method (from autoCandidates) and autoNest is enabled, prefer the
            // synthesized private __DwarfMap_Obj_* form instead. Public methods don't accept the
            // shared DwarfRefContext — calling them from a collection/dict helper would create a fresh
            // context, losing identity/depth/on-stack state and causing infinite loops on cycles.
            // We fall through to the auto-nest path below only when these conditions hold;
            // user-provided converter helpers (allMethods, not autoCandidates) are always respected.
            bool foundIsAutoCandidate = (isPreserve || isSetNull) && autoNest && nestedRegistry is not null
                && autoCandidates.Any(ac => string.Equals(ac.Name, found, System.StringComparison.Ordinal))
                && tgtType is INamedTypeSymbol
                && IsMappableObjectPair(compilation, srcType, (INamedTypeSymbol)tgtType);
            if (!foundIsAutoCandidate)
            {
                converterMethod = found;
                return true;
            }
            // Fall through to synthesize a private __DwarfMap_Obj_* form.
        }

        // Integral↔integral narrowing / sign-change: emit CreateChecked (throws on overflow).
        // Must come after the implicit-conversion check (widening uses direct assign, not this)
        // and after user auto-candidates (user methods take precedence over built-in synthesis).
        // Enums have SpecialType.None — IsIntegral is false for them, so this never intercepts enums.
        var numericMethod = NumericConverter.TryCreate(srcType, tgtType, synthesized);
        if (numericMethod is not null)
        {
            converterMethod = numericMethod;
            // DWARF038: a non-implicit numeric conversion (narrowing / sign-change, via CreateChecked) is
            // a non-lossless basic-type conversion. Surface it as a suggestion (permissive) or a build
            // error (ImplicitConversions = false). Lossless widening uses the implicit/direct path above
            // and is never flagged.
            EmitImplicitConversionDiag(diagnostics, location, targetName, srcType, tgtType, "numeric (narrowing/sign-change)", implicitConversions);
            return true;
        }

        // string↔T via IParsable<T>.Parse / IFormattable.ToString (InvariantCulture, loud on bad input).
        // Wired AFTER autoCandidates (explicit Use= and auto-conversion methods still win) and BEFORE
        // EnumConverter (enum↔string routes through EnumConverter's by-name switch; ParsableConverter
        // guards against enum operands explicitly).
        var parsableMethod = ParsableConverter.TryCreate(compilation, srcType, tgtType, synthesized);
        if (parsableMethod is not null)
        {
            converterMethod = parsableMethod;
            // DWARF038: string↔T parse/format is a non-lossless basic-type conversion → suggestion / error.
            EmitImplicitConversionDiag(diagnostics, location, targetName, srcType, tgtType, "parse/format (string↔T)", implicitConversions);
            return true;
        }

        var enumMethod = EnumConverter.TryCreate(srcType, tgtType, enumStrategy, synthesized, location, targetName, diagnostics);
        if (enumMethod is not null)
        {
            converterMethod = enumMethod;
            return true;
        }

        // ── Auto-synthesized nested object mapper ─────────────────────────────
        // Placed LAST before DWARF005: only fires when nothing else resolved the pair.
        // Gate: autoNest=true AND both types are mappable named object types.
        if (autoNest && nestedRegistry is not null
            && tgtType is INamedTypeSymbol namedTgt)
        {
            if (IsMappableObjectPair(compilation, srcType, namedTgt, allowInterfaceSrc))
            {
                // C1: pass the effective autoNest value so the drain loop uses it for the pair's body.
                var synthName = nestedRegistry.GetOrReserve(srcType, namedTgt, location, autoNest);
                if (synthName is not null)
                {
                    converterMethod = synthName;
                    return true;
                }
                // GetOrReserve returned null → cap exceeded; DWARF031 will be reported after drain.
                // Fall through to DWARF005.
            }
            else if (!allowInterfaceSrc && IsAbstractOrInterfaceAutoNestSource(compilation, srcType, namedTgt))
            {
                // C2: abstract/interface source — emit DWARF033 (loud, never silent).
                // Suppressed when allowInterfaceSrc=true (e.g. [MapDerivedType] arms where the caller
                // explicitly opted in to mapping an interface source to a concrete DTO).
                var srcName = srcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AbstractSourceAutoNest, location, srcName));
                return false;
            }
        }

        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
        return false;
    }

    /// <summary>
    /// Returns true when both <paramref name="src"/> and <paramref name="tgt"/> are named types
    /// suitable for auto-nested-mapper synthesis. Excludes: scalars, enums, string, collection/
    /// IEnumerable types, Nullable&lt;T&gt;, interfaces, abstract target types, and
    /// abstract/interface source types (C2: those would silently drop derived-only members).
    /// When <paramref name="allowInterfaceSrc"/> is <see langword="true"/>, interface source types
    /// are accepted (used by [MapDerivedType] arm resolution where the caller explicitly opts in).
    /// </summary>
    private static bool IsMappableObjectPair(Compilation compilation, ITypeSymbol src, INamedTypeSymbol tgt,
        bool allowInterfaceSrc = false)
    {
        // Source must be a named type (class or struct/record, not array/pointer/etc.)
        if (src is not INamedTypeSymbol namedSrc)
            return false;

        // Source must be Class, Struct, or (when allowed) Interface.
        // Enums (TypeKind.Enum), delegates, arrays, etc. are always excluded.
        if (allowInterfaceSrc)
        {
            if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct
                && namedSrc.TypeKind != TypeKind.Interface)
                return false;
        }
        else
        {
            // Default: both must be Class or Struct (records are Class or Struct).
            // This also implicitly excludes enums (TypeKind.Enum), interfaces (TypeKind.Interface),
            // delegates, arrays, etc. — no separate enum guard needed.
            if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct)
                return false;
        }
        if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct)
            return false;

        // Not scalar / special types (string, int, Guid, etc.)
        if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None)
            return false;

        // Not Nullable<T>
        if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return false;
        if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return false;

        // C2: abstract SOURCE type — auto-nest would silently drop derived members.
        // We return false here so the caller can emit DWARF033 when appropriate.
        // Exception: interface sources are allowed when allowInterfaceSrc=true (caller explicitly
        // opted in via [MapDerivedType] dispatch, which is safe because the user controls the arms).
        if (!allowInterfaceSrc && namedSrc.IsAbstract)
            return false;

        // Not an interface target (can't construct an interface)
        if (tgt.IsAbstract)
            return false;

        // Not an IEnumerable / collection / dictionary — REGARDLESS of class vs struct.
        // (Struct collections like ImmutableArray<T> must NOT be object-field-mapped; they belong to
        //  CollectionConverter/DictionaryConverter, or fall to DWARF005 until supported. string is
        //  already excluded above by the SpecialType.None check.)
        if (ImplementsIEnumerable(compilation, namedSrc))
            return false;
        if (ImplementsIEnumerable(compilation, tgt))
            return false;

        // Target must have a constructible constructor (ConstructorSelector-compatible).
        // We do a lightweight check: at least one accessible non-static constructor.
        if (!tgt.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="src"/> is a named type that would be a mappable-object-pair
    /// source except that it is abstract or an interface — i.e. it would silently drop derived members
    /// (C2: DWARF033 guard).
    /// </summary>
    private static bool IsAbstractOrInterfaceAutoNestSource(Compilation compilation, ITypeSymbol src, INamedTypeSymbol tgt)
    {
        if (src is not INamedTypeSymbol namedSrc) return false;

        // Must pass all other IsMappableObjectPair gates except the IsAbstract-source check
        if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct) return false;
        if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct) return false;
        if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None) return false;
        if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return false;
        if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) return false;
        if (tgt.IsAbstract) return false;
        if (ImplementsIEnumerable(compilation, namedSrc)) return false;
        if (ImplementsIEnumerable(compilation, tgt)) return false;
        if (!tgt.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic)) return false;

        // The source is abstract (class abstract) or interface — this is the C2 trigger
        return namedSrc.IsAbstract;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> implements <c>IEnumerable</c> (generic or non-generic),
    /// which means it is a collection/sequence type that belongs to CollectionConverter/DictionaryConverter.
    /// </summary>
    private static bool ImplementsIEnumerable(Compilation compilation, INamedTypeSymbol type)
    {
        // Fast checks: well-known collection / dict names (all supported + well-known unsupported)
        if (type.Name is "List" or "Array" or "HashSet" or "Dictionary"
            or "IEnumerable" or "ICollection" or "IList"
            or "IReadOnlyList" or "IReadOnlyCollection"
            or "ISet" or "IReadOnlySet"
            or "ImmutableArray" or "ImmutableList" or "IImmutableList"
            or "ImmutableHashSet" or "IImmutableSet"
            or "ImmutableDictionary" or "IImmutableDictionary"
            or "IDictionary" or "IReadOnlyDictionary"
            // well-known unsupported (DWARF027)
            or "SortedSet" or "SortedDictionary" or "SortedList"
            or "Queue" or "Stack" or "LinkedList"
            or "ConcurrentDictionary" or "ConcurrentQueue" or "ConcurrentStack" or "ConcurrentBag")
            return true;

        // Check whether the type or any of its interfaces is IEnumerable<T> or IEnumerable
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
            if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is collection-shaped (implements IEnumerable,
    /// is not string, is not already handled by CollectionConverter or DictionaryConverter)
    /// → should emit DWARF027 rather than DWARF005.
    /// </summary>
    private static bool IsUnsupportedCollectionTarget(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        // Multi-dimensional array (not IArrayTypeSymbol with Rank > 1 check needed here)
        if (type is IArrayTypeSymbol arr && arr.Rank > 1)
            return true;

        if (type is not INamedTypeSymbol named)
            return false;

        // Check if it implements IEnumerable (non-string collection-shaped)
        foreach (var iface in named.AllInterfaces)
        {
            if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
            if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return true;
        }
        // Also check the type itself (IEnumerable<T> as named type)
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            return true;

        return false;
    }

    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 1)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }

    /// <summary>
    /// Collects parameterless, non-void methods on the mapper — the candidate value providers for
    /// <c>[MapValue(Use = nameof(...))]</c>. Returns <c>(Name, ReturnType)</c> pairs.
    /// </summary>
    private static List<(string Name, ITypeSymbol ReturnType)> CollectValueProviders(INamedTypeSymbol classSymbol)
    {
        var providers = new List<(string Name, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 0)
            {
                providers.Add((m.Name, m.ReturnType));
            }
        }
        return providers;
    }

    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMapperMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition
                && !m.ReturnsVoid && m.Parameters.Length == 1 && m.ReturnType is INamedTypeSymbol)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }

    private static (List<(string Name, ITypeSymbol ParamType)> Before, List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)> After)
        CollectHooks(INamedTypeSymbol classSymbol, List<DiagnosticInfo> diagnostics)
    {
        var before = new List<(string Name, ITypeSymbol ParamType)>();
        var after = new List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            var isBefore = m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.BeforeMapAttribute");
            var isAfter = m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.AfterMapAttribute");
            if (!isBefore && !isAfter)
            {
                continue;
            }
            var loc = LocationInfo.From(m.Locations.FirstOrDefault() ?? Location.None);
            if (!m.ReturnsVoid)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                continue;
            }
            if (isBefore)
            {
                if (m.Parameters.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                }
                else
                {
                    before.Add((m.Name, m.Parameters[0].Type));
                }
            }
            if (isAfter)
            {
                if (m.Parameters.Length == 1)
                {
                    // 1-param: the sole parameter is the target; capture its RefKind
                    after.Add((m.Name, m.Parameters[0].Type, null, m.Parameters[0].RefKind));
                }
                else if (m.Parameters.Length == 2)
                {
                    // 2-param: P0=source, P1=target; capture P1's RefKind
                    after.Add((m.Name, m.Parameters[0].Type, m.Parameters[1].Type, m.Parameters[1].RefKind));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                }
            }
        }
        return (before, after);
    }

    private static bool HasImplicitConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
    {
        var conversion = ((CSharpCompilation)compilation).ClassifyConversion(source, target);
        return conversion.IsImplicit && !conversion.IsUserDefined;
    }

    private static bool IsNullableValue(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            underlying = named.TypeArguments[0];
            return true;
        }
        underlying = type;
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is a nullable reference type
    /// (i.e. a reference type with <c>NullableAnnotation.Annotated</c>), e.g. <c>List&lt;int&gt;?</c>.
    /// Used to decide whether AsNull semantics are safe for a given target field (A3).
    /// </summary>
    private static bool IsNullableReferenceType(ITypeSymbol type) =>
        type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>
    /// Returns true when <paramref name="type"/> carries a nullable annotation
    /// (either a nullable reference type or a nullable value type like <c>ImmutableArray&lt;T&gt;?</c>).
    /// Used for value-type struct nullable targets such as <c>ImmutableArray&lt;T&gt;?</c> (A2/A3).
    /// </summary>
    private static bool IsNullableAnnotated(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>
    /// True when a source member of this type may be null from the compiler's point of view — a
    /// reference type that is nullable-annotated OR oblivious (<c>#nullable disable</c> context).
    /// Drives the null-forgiving <c>!</c> at synthesized-converter call sites: only such sources can
    /// trip CS8604 when the converter's parameter is non-nullable. Value types (enums, numerics,
    /// <c>Nullable&lt;T&gt;</c>) and non-nullable references never need it, so the emitter omits the
    /// otherwise-spurious <c>!</c> for them. <see cref="MemberMap.SourceIsNullableRef"/>.
    /// </summary>
    private static bool SourceMayBeNullRef(ITypeSymbol type) =>
        type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated;

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        // Interface types: walk the interface itself plus all transitively inherited interfaces.
        // Interfaces don't have a BaseType class chain, so the normal loop would only see
        // the interface's own members and miss parent-interface properties.
        if (type.TypeKind == TypeKind.Interface && type is INamedTypeSymbol ifaceType)
        {
            var ifacesToWalk = new ITypeSymbol[] { type }
                .Concat(ifaceType.AllInterfaces.Cast<ITypeSymbol>());
            foreach (var iface in ifacesToWalk)
            {
                foreach (var m in iface.GetMembers())
                {
                    if (m.IsStatic) continue;
                    switch (m)
                    {
                        case IPropertySymbol p when !p.IsIndexer && p.GetMethod is not null:
                            if (seen.Add(p.Name))
                                yield return (p.Name, p.Type);
                            break;
                        case IFieldSymbol f when !f.IsImplicitlyDeclared:
                            if (seen.Add(f.Name))
                                yield return (f.Name, f.Type);
                            break;
                    }
                }
            }
            yield break;
        }

        // Classes and structs: walk the inheritance chain.
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.SetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && !f.IsReadOnly && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadOnlyMembers(ITypeSymbol type)
    {
        var writable = new HashSet<string>(WritableMembers(type).Select(m => m.Name), System.StringComparer.Ordinal);
        return ReadableMembers(type).Where(m => !writable.Contains(m.Name));
    }

    private static bool HasAccessibleParameterlessCtor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

    private const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    /// <summary>
    /// Returns the set of member names (case-insensitive) that are <c>required</c> AND satisfied via
    /// a constructor parameter, but whose constructor does NOT carry
    /// <c>[SetsRequiredMembers]</c>. These members must also be emitted in the object initializer to
    /// avoid CS9035.
    /// </summary>
    private static HashSet<string> ComputeRequiredMustInitialize(
        IMethodSymbol ctor,
        INamedTypeSymbol targetType,
        HashSet<string> consumedParams)
    {
        // If the chosen ctor is annotated [SetsRequiredMembers], C# considers all required members
        // satisfied — no double-set needed.
        var ctorHasSetsRequired = ctor.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == SetsRequiredMembersAttribute);

        if (ctorHasSetsRequired)
        {
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        }

        // Collect required member names from the target type hierarchy.
        var required = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (var current = (ITypeSymbol)targetType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol p when p.IsRequired:
                        required.Add(p.Name);
                        break;
                    case IFieldSymbol f when f.IsRequired:
                        required.Add(f.Name);
                        break;
                }
            }
        }

        // The intersection: consumed params that are also required members.
        var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var name in consumedParams)
        {
            if (required.Contains(name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static IEnumerable<string> ReadIgnores(ISymbol symbol) =>
        symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.MapIgnoreAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)
            .Select(s => s!);

    /// <summary>
    /// Reads <c>[MapIgnoreSource("Member")]</c> names — the source-side mirror of <see cref="ReadIgnores"/>.
    /// Used to suppress the DWARF039 source-coverage suggestion for specific source members.
    /// </summary>
    private static IEnumerable<string> ReadIgnoreSources(ISymbol symbol) =>
        symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.MapIgnoreSourceAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)
            .Select(s => s!);

    private static List<(string Source, string Target, string? Use)> ReadExplicitMaps(ISymbol method)
    {
        var maps = new List<(string Source, string Target, string? Use)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "DwarfMapper.MapPropertyAttribute")
            {
                continue;
            }
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string s
                && attr.ConstructorArguments[1].Value is string t)
            {
                string? use = null;
                foreach (var na in attr.NamedArguments)
                {
                    if (na.Key == "Use" && na.Value.Value is string u)
                    {
                        use = u;
                    }
                }
                maps.Add((s, t, use));
            }
        }
        return maps;
    }

    /// <summary>
    /// Reads <c>[MapValue]</c> annotations. A two-argument form (<c>IsConstant = true</c>) carries a
    /// constant <c>Value</c>; the one-argument form carries a <c>Use</c> provider-method name. The
    /// <c>Use</c> named argument is also honoured on the two-argument form (Use wins).
    /// </summary>
    private static List<(string Target, bool IsConstant, TypedConstant Value, string? Use)> ReadMapValues(ISymbol method)
    {
        var result = new List<(string, bool, TypedConstant, string?)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "DwarfMapper.MapValueAttribute")
            {
                continue;
            }
            if (attr.ConstructorArguments.Length == 0
                || attr.ConstructorArguments[0].Value is not string target)
            {
                continue;
            }
            string? use = null;
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "Use" && na.Value.Value is string u)
                {
                    use = u;
                }
            }
            // Two-arg ctor → constant value in [1]; one-arg ctor → Use-driven.
            var isConstant = attr.ConstructorArguments.Length == 2 && use is null;
            var value = attr.ConstructorArguments.Length == 2 ? attr.ConstructorArguments[1] : default;
            result.Add((target, isConstant, value, use));
        }
        return result;
    }

    /// <summary>
    /// Formats a <c>[MapValue]</c> constant as a C# literal assignable to <paramref name="targetType"/>,
    /// or fails with a reason. Only attribute-legal constants are supported (string, bool, char, numeric,
    /// enum, null); arrays/typeof and non-assignable values fail (the caller emits DWARF040). Floating/
    /// decimal targets are cast to the target type so an un-suffixed literal (e.g. <c>1.5</c>) compiles.
    /// </summary>
    private static bool TryFormatConstant(
        TypedConstant tc, ITypeSymbol targetType, Compilation compilation, out string literal, out string why)
    {
        literal = "";
        why = "";
        if (tc.Kind is TypedConstantKind.Array or TypedConstantKind.Type or TypedConstantKind.Error)
        {
            why = $"[MapValue] constant for '{targetType.ToDisplayString()}' must be a string, bool, char, numeric, enum, or null";
            return false;
        }

        if (tc.IsNull)
        {
            var acceptsNull = targetType.IsReferenceType
                || (targetType is INamedTypeSymbol nt && nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
            if (!acceptsNull)
            {
                why = $"[MapValue] cannot assign null to non-nullable type '{targetType.ToDisplayString()}'";
                return false;
            }
            literal = "null";
            return true;
        }

        if (tc.Kind == TypedConstantKind.Enum)
        {
            if (tc.Type is null || !HasImplicitConversion(compilation, tc.Type, targetType))
            {
                why = $"[MapValue] enum constant of type '{tc.Type?.ToDisplayString()}' is not assignable to '{targetType.ToDisplayString()}'";
                return false;
            }
            var enumFqn = tc.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            literal = $"({enumFqn})({SymbolDisplay.FormatPrimitive(tc.Value!, quoteStrings: false, useHexadecimalNumbers: false)})";
            return true;
        }

        // Primitive (string/bool/char/numeric).
        if (tc.Type is not null && !HasImplicitConversion(compilation, tc.Type, targetType))
        {
            why = $"[MapValue] constant of type '{tc.Type.ToDisplayString()}' is not assignable to '{targetType.ToDisplayString()}'";
            return false;
        }
        var formatted = SymbolDisplay.FormatPrimitive(tc.Value!, quoteStrings: true, useHexadecimalNumbers: false);
        // Floating/decimal targets need an explicit cast — an un-suffixed literal like "1.5" is a double
        // and would not compile when assigned to float/decimal.
        literal = targetType.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
            ? $"({targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({formatted})"
            : formatted;
        return true;
    }

    /// <summary>
    /// Resolves a dotted source path (e.g. <c>"Customer.Name"</c>) hop-by-hop from <paramref name="root"/>,
    /// returning the leaf member's type. <paramref name="nullableHop"/> is set when an <i>interior</i> hop
    /// (any but the last) is a nullable/oblivious reference — dereferencing it can throw at runtime
    /// (DWARF044). On failure, <paramref name="badSegment"/> names the first unresolved segment (DWARF043).
    /// Segments are matched by exact ordinal name (member names never contain dots).
    /// </summary>
    private static bool TryResolveSourcePath(
        ITypeSymbol root, string dottedPath, out ITypeSymbol? leafType, out bool nullableHop, out string badSegment)
    {
        leafType = null;
        nullableHop = false;
        badSegment = "";
        var segments = dottedPath.Split('.');
        ITypeSymbol current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var member = ReadableMembers(current)
                .Where(m => System.StringComparer.Ordinal.Equals(m.Name, seg))
                .Select(m => ((string Name, ITypeSymbol Type)?)m)
                .FirstOrDefault();
            if (member is null)
            {
                badSegment = seg;
                return false;
            }
            if (i < segments.Length - 1
                && member.Value.Type.IsReferenceType
                && member.Value.Type.NullableAnnotation != NullableAnnotation.NotAnnotated)
            {
                nullableHop = true;
            }
            current = member.Value.Type;
        }
        leafType = current;
        return true;
    }

    private static List<string> ReadFlattenRoots(ISymbol method)
    {
        var roots = new List<string>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.FlattenAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string s)
            {
                roots.Add(s);
            }
        }
        return roots;
    }

    // ── Plan 20: [FlattenGraph] ───────────────────────────────────────────────

    /// <summary>
    /// Reads raw [FlattenGraph(srcNav, tgtColl)] annotation pairs from a method symbol.
    /// </summary>
    private static List<(string SourceNavigation, string TargetCollection)> ReadFlattenGraphAttributes(ISymbol method)
    {
        var result = new List<(string, string)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.FlattenGraphAttribute"
                && attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string src
                && attr.ConstructorArguments[1].Value is string tgt)
            {
                result.Add((src, tgt));
            }
        }
        return result;
    }

    /// <summary>
    /// FNV-1a hash for generating unique helper method name suffixes.
    /// Same algorithm as CollectionConverter.
    /// </summary>
    private static string FlattenGraphHash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Checks whether <paramref name="t"/> is a named generic type with the given
    /// <paramref name="name"/> and <paramref name="ns"/> with exactly <paramref name="arity"/>
    /// type arguments. Returns the first type argument via <paramref name="firstArg"/> if matched.
    /// </summary>
    private static bool IsExactNamedTypeHelper(
        ITypeSymbol t, string name, string ns, int arity, out ITypeSymbol? firstArg)
    {
        firstArg = null;
        if (t is INamedTypeSymbol n
            && n.Name == name
            && n.TypeArguments.Length == arity
            && n.ContainingNamespace?.ToDisplayString() == ns)
        {
            firstArg = n.TypeArguments[0];
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves and validates [FlattenGraph] directives for a single method, synthesizes
    /// the required BFS traversal and flat-node helpers, and returns the resolved directives
    /// plus the MemberMap entries to inject into the method's normal member list.
    /// <para>
    /// Mutates: <paramref name="synthesized"/> (adds helpers), <paramref name="consumedTargets"/>
    /// (adds target collection member names so ResolveMembers skips them).
    /// </para>
    /// </summary>
    private static (List<Model.FlattenGraphDirective> Directives, List<MemberMap> InjectedMembers)
        ResolveFlattenGraphDirectives(
            ITypeSymbol sourceType,
            INamedTypeSymbol targetType,
            IReadOnlyList<(string SourceNavigation, string TargetCollection)> rawDirectives,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            EnumStrategy enumStrategy,
            Dictionary<string, SynthesizedMethod> synthesized,
            NullStrategy nullStrategy,
            bool autoNest,
            NestedMappingRegistry? nestedRegistry,
            bool nullAsNull,
            bool isPreserve,
            HashSet<string> consumedTargets,
            IReadOnlyList<(INamedTypeSymbol Src, INamedTypeSymbol Tgt)>? rawDerivedPairs = null)
    {
        var directives = new List<Model.FlattenGraphDirective>();
        var injected = new List<MemberMap>();

        foreach (var (srcNavName, tgtCollName) in rawDirectives)
        {
            // 1. Resolve source navigation member on sourceType
            ITypeSymbol? srcNavType = null;
            foreach (var m in ReadableMembers(sourceType))
            {
                if (string.Equals(m.Name, srcNavName, System.StringComparison.Ordinal))
                {
                    srcNavType = m.Type;
                    break;
                }
            }

            if (srcNavType is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] source navigation '{srcNavName}' does not exist or is not readable on '{sourceType.Name}'"));
                continue;
            }

            // 2. Determine TNode type: directly a named reference type, or element of a collection
            ITypeSymbol nodeType;
            bool srcNavIsCollection;
            // MF-B: track whether the source nav is specifically an array so we can emit
            // the correct traversal helper parameter type:
            //   array      → TNode[]?          (T[] is both IEnumerable<T> and exact)
            //   other coll → IEnumerable<TNode>? (handles List<T>, HashSet<T>, IReadOnlyList<T>, …)
            //   dict        → use srcNavType directly, seed BFS from .Values
            //   single ref  → TNode?
            bool srcNavIsArray;
            // SF-F3: when the source nav is a Dictionary<K,V> where V is a reference type, seed
            // the BFS from the dictionary's values (.Values) rather than enumerating KeyValuePairs.
            bool srcNavIsDict;

            if (srcNavType is IArrayTypeSymbol arrNav && arrNav.Rank == 1
                && arrNav.ElementType.IsReferenceType)
            {
                nodeType = arrNav.ElementType;
                srcNavIsCollection = true;
                srcNavIsArray = true;
                srcNavIsDict = false;
            }
            else if (DictionaryConverter.TryGetDictionaryValueType(srcNavType, out var dictNavNodeType)
                     && dictNavNodeType.IsReferenceType)
            {
                // SF-F3: Dictionary<K, Node> source nav — node type is V, seed from .Values.
                nodeType = dictNavNodeType;
                srcNavIsCollection = true;
                srcNavIsArray = false;
                srcNavIsDict = true;
            }
            else if (CollectionConverter.TryGetEnumerableElement(srcNavType, out var navElemType, out _)
                     && navElemType.IsReferenceType)
            {
                nodeType = navElemType;
                srcNavIsCollection = true;
                srcNavIsArray = false;
                srcNavIsDict = false;
            }
            else if (srcNavType.IsReferenceType && srcNavType.TypeKind != TypeKind.Array)
            {
                nodeType = srcNavType;
                srcNavIsCollection = false;
                srcNavIsArray = false;
                srcNavIsDict = false;
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] source navigation '{srcNavName}' must be a reference type or a collection of a reference type (the node type)"));
                continue;
            }

            // 3. Resolve target collection member on targetType
            ITypeSymbol? tgtCollType = null;
            foreach (var m in WritableMembers(targetType))
            {
                if (string.Equals(m.Name, tgtCollName, System.StringComparison.Ordinal))
                {
                    tgtCollType = m.Type;
                    break;
                }
            }

            if (tgtCollType is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] target collection '{tgtCollName}' does not exist or is not writable on '{targetType.Name}'"));
                continue;
            }

            // 4. Determine nodeDtoType: element of the target collection, and the suffix kind
            ITypeSymbol nodeDtoType;
            bool needsToArray;

            if (tgtCollType is IArrayTypeSymbol arrTgt && arrTgt.Rank == 1)
            {
                nodeDtoType = arrTgt.ElementType;
                needsToArray = true;
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "List", "System.Collections.Generic", 1, out var listElem))
            {
                nodeDtoType = listElem!;
                needsToArray = false;
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IReadOnlyList", "System.Collections.Generic", 1, out var rlElem))
            {
                nodeDtoType = rlElem!;
                needsToArray = false; // List<T> implements IReadOnlyList<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "ICollection", "System.Collections.Generic", 1, out var icElem))
            {
                nodeDtoType = icElem!;
                needsToArray = false; // List<T> implements ICollection<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IReadOnlyCollection", "System.Collections.Generic", 1, out var ircElem))
            {
                nodeDtoType = ircElem!;
                needsToArray = false; // List<T> implements IReadOnlyCollection<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IList", "System.Collections.Generic", 1, out var ilElem))
            {
                nodeDtoType = ilElem!;
                needsToArray = false; // List<T> implements IList<T>
            }
            else if (IsExactNamedTypeHelper(tgtCollType, "IEnumerable", "System.Collections.Generic", 1, out var ieElem))
            {
                nodeDtoType = ieElem!;
                needsToArray = false; // List<T> implements IEnumerable<T>
            }
            else
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] target collection '{tgtCollName}' on '{targetType.Name}' must be " +
                    $"List<T>, T[], IReadOnlyList<T>, ICollection<T>, IReadOnlyCollection<T>, IList<T>, or IEnumerable<T>"));
                continue;
            }

            // ── Plan 22: Heterogeneous branch ────────────────────────────────
            // Detect hetero mode: abstract/interface node base OR [MapDerivedType] pairs present.
            var nodeIsAbstractOrInterface =
                nodeType.TypeKind == TypeKind.Interface || nodeType.IsAbstract;
            var effectiveDerivedPairs = rawDerivedPairs ?? System.Array.Empty<(INamedTypeSymbol, INamedTypeSymbol)>();
            var isHetero = nodeIsAbstractOrInterface || effectiveDerivedPairs.Count > 0;

            if (isHetero)
            {
                // Validate: abstract/interface node base requires at least one [MapDerivedType]
                if (nodeIsAbstractOrInterface && effectiveDerivedPairs.Count == 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                        $"[FlattenGraph] node base type '{nodeType.Name}' is abstract or an interface; " +
                        $"add [MapDerivedType<TNodeDerived, TNodeDerivedDto>] for each concrete node type."));
                    continue;
                }

                // Validate arms and collect resolved arms
                var heteroArms = new List<(INamedTypeSymbol NodeDerived, INamedTypeSymbol DtoDerived,
                    string FlatNodeHelperName,
                    List<(string Name, bool IsCollection, bool IsDictValue)> EdgeMembers,
                    List<(string Name, ITypeSymbol Type)> LeafMembers)>();
                var seenSrcFqns = new HashSet<string>(System.StringComparer.Ordinal);
                var anyArmError = false;

                foreach (var (derivedSrc, derivedTgt) in effectiveDerivedPairs)
                {
                    var srcFqn = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var tgtFqnArm = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    // 1. derivedSrc must be assignable to nodeType (the node base)
                    if (!HasImplicitConversion(compilation, derivedSrc, nodeType))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType, location,
                            $"[MapDerivedType] source type '{srcFqn}' is not assignable to node base type " +
                            $"'{nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                            $"each derived node type must inherit from or implement the node base."));
                        anyArmError = true;
                        continue;
                    }

                    // 2. derivedTgt must be assignable to nodeDtoType (the base DTO = collection element)
                    if (!HasImplicitConversion(compilation, derivedTgt, nodeDtoType))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType, location,
                            $"[MapDerivedType] target type '{tgtFqnArm}' is not assignable to target collection " +
                            $"element type '{nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; " +
                            $"each derived DTO type must inherit from or implement the base DTO."));
                        anyArmError = true;
                        continue;
                    }

                    // 3. No duplicate derived source types
                    if (!seenSrcFqns.Add(srcFqn))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapDerivedType, location,
                            $"[MapDerivedType] duplicate derived source type '{srcFqn}'; each concrete node type may only be registered once."));
                        anyArmError = true;
                        continue;
                    }

                    // 4. Compute EDGE members for this concrete derived type.
                    // An EDGE member is any readable member whose type is assignable to nodeType, or a
                    // collection thereof (including inherited base edges).
                    // SF-F3 fix: also detect Dictionary<K,V> where V is assignable to nodeType.
                    var nodeBaseNoAnnot = nodeType.WithNullableAnnotation(NullableAnnotation.None);
                    var derivedEdgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue)>();
                    var derivedLeafMembers = new List<(string Name, ITypeSymbol Type)>();

                    foreach (var nm in ReadableMembers(derivedSrc))
                    {
                        var memberTypeNoAnnot = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                        // Single-ref edge: type assignable to nodeBase (includes exact type and subtypes)
                        if (HasImplicitConversion(compilation, memberTypeNoAnnot, nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, false));
                            continue;
                        }

                        // Nullable<T> where T is assignable to nodeBase
                        if (nm.Type is INamedTypeSymbol nmNamed
                            && nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                            && HasImplicitConversion(compilation, nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None), nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, false));
                            continue;
                        }

                        // SF-F3: Dictionary<K,V> where V is assignable to nodeBase → dict-value edge.
                        if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValTypeD)
                            && HasImplicitConversion(compilation, dictValTypeD.WithNullableAnnotation(NullableAnnotation.None), nodeType))
                        {
                            derivedEdgeMembers.Add((nm.Name, false, true));
                            continue;
                        }

                        // Collection edge: element type assignable to nodeBase
                        bool isEdgeColl = false;
                        if (nm.Type is IArrayTypeSymbol arrEdge && arrEdge.Rank == 1
                            && HasImplicitConversion(compilation, arrEdge.ElementType.WithNullableAnnotation(NullableAnnotation.None), nodeType))
                        {
                            isEdgeColl = true;
                        }
                        else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _)
                            && HasImplicitConversion(compilation, edgeElem.WithNullableAnnotation(NullableAnnotation.None), nodeType))
                        {
                            isEdgeColl = true;
                        }

                        if (isEdgeColl)
                            derivedEdgeMembers.Add((nm.Name, true, false));
                        else
                            derivedLeafMembers.Add((nm.Name, nm.Type));
                    }

                    // 5. Build the __DwarfMap_FlatNode_<TypeName>_<hash> helper for this concrete type
                    var armHashKey = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                  + "=>"
                                  + derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                  + "@FG";
                    var armHash = FlattenGraphHash(armHashKey);
                    var typeName = derivedSrc.Name;
                    var perTypeHelperName = "__DwarfMap_FlatNode_" + typeName + "_" + armHash;

                    if (!synthesized.ContainsKey(perTypeHelperName))
                    {
                        var nodeFqDerived = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var dtoFqDerived  = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        var dtoWritableDerived = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
                        foreach (var wm in WritableMembers(derivedTgt))
                            dtoWritableDerived[wm.Name] = wm.Type;

                        var sbArm = new System.Text.StringBuilder();
                        sbArm.Append("    private ").Append(dtoFqDerived).Append(' ').Append(perTypeHelperName)
                             .Append('(').Append(nodeFqDerived).AppendLine(" n)");
                        sbArm.AppendLine("    {");
                        sbArm.Append("        return new ").Append(dtoFqDerived).AppendLine();
                        sbArm.AppendLine("        {");

                        // Leaf members: map with conversion where available.
                        // MF-D fix: use throw-away synth dict + skip complex synthesized converters.
                        // SF-LEAFDIAG fix: propagate unmappable-leaf errors to real diagnostics.
                        foreach (var leaf in derivedLeafMembers)
                        {
                            if (!dtoWritableDerived.TryGetValue(leaf.Name, out var dtoMemberType))
                                continue;
                            var leafThrowAwaySynth = new Dictionary<string, SynthesizedMethod>(System.StringComparer.Ordinal);
                            var leafTestDiags = new List<DiagnosticInfo>();
                            bool leafResolved = TryResolveConversion(compilation, leaf.Type, dtoMemberType, null,
                                    allMethods, autoCandidates, enumStrategy, leafThrowAwaySynth, nullStrategy,
                                    location, leaf.Name, leafTestDiags, out var leafConv, out var leafNull, out _,
                                    autoNest, nestedRegistry, nullAsNull: false, isPreserve: false);
                            if (!leafResolved)
                            {
                                diagnostics.AddRange(leafTestDiags);
                                continue;
                            }
                            // MF-D: skip only COMPLEX helpers (Obj/Coll/Dict) that may become 3-param.
                            // Numeric/enum/parsable helpers are always single-arg and are safe.
                            if (leafConv is not null
                                && (leafConv.StartsWith("__DwarfMap_Obj_", System.StringComparison.Ordinal)
                                    || leafConv.StartsWith("__DwarfMap_Coll_", System.StringComparison.Ordinal)
                                    || leafConv.StartsWith("__DwarfMap_Dict_", System.StringComparison.Ordinal)))
                                continue; // complex synthesized helper — skip (topology degradation)
                            foreach (var kv in leafThrowAwaySynth)
                                if (!synthesized.ContainsKey(kv.Key)) synthesized[kv.Key] = kv.Value;
                            sbArm.Append("            ").Append(leaf.Name).Append(" = ");
                            AppendFlatNodeMemberExpr(sbArm, "n", leaf.Name, leafConv, leafNull);
                            sbArm.AppendLine(",");
                        }

                        // Edge members on derived DTO: null them (topology degradation)
                        foreach (var edge in derivedEdgeMembers)
                        {
                            if (!dtoWritableDerived.ContainsKey(edge.Name))
                                continue;
                            sbArm.Append("            ").Append(edge.Name).AppendLine(" = null,");
                        }

                        sbArm.AppendLine("        };");
                        sbArm.AppendLine("    }");
                        synthesized[perTypeHelperName] = new SynthesizedMethod(perTypeHelperName, sbArm.ToString());
                    }

                    heteroArms.Add((derivedSrc, derivedTgt, perTypeHelperName, derivedEdgeMembers, derivedLeafMembers));
                }

                if (anyArmError && heteroArms.Count == 0)
                    continue; // all arms had errors; skip this directive

                // Sort arms most-derived-first (reuse Plan-21 sort)
                var sortedHeteroArms = heteroArms
                    .Select((arm, idx) => (arm, idx, depth: InheritanceDepth(arm.NodeDerived)))
                    .OrderByDescending(x => x.depth)
                    .ThenBy(x => x.idx)
                    .Select(x => x.arm)
                    .ToList();

                // Build dispatch helper name and traversal helper name from the hetero hash
                var heteroHashKey = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                  + "=>"
                                  + nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                                  + "@Hetero";
                var heteroHash = FlattenGraphHash(heteroHashKey);

                var dispatchHelperName  = "__DwarfMap_FlatNodeDispatch_" + heteroHash;
                var traversalHelperNameH = "__DwarfMap_FlattenGraph_" + heteroHash;

                // Synthesize __DwarfMap_FlatNodeDispatch_<hash>(TBase n) => n switch { ... }
                if (!synthesized.ContainsKey(dispatchHelperName))
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq  = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var sbDisp = new System.Text.StringBuilder();
                    sbDisp.Append("    private ").Append(dtoBaseFq).Append(' ').Append(dispatchHelperName)
                          .Append('(').Append(nodeBaseFq).AppendLine(" n)");
                    sbDisp.AppendLine("        => n switch");
                    sbDisp.AppendLine("        {");
                    foreach (var arm in sortedHeteroArms)
                    {
                        var armSrcFq = arm.NodeDerived.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        sbDisp.Append("            ").Append(armSrcFq).Append(" __s => ")
                              .Append(arm.FlatNodeHelperName).AppendLine("(__s),");
                    }
                    sbDisp.Append("            _ => throw new global::System.ArgumentException(")
                          .Append("\"DwarfMapper [FlattenGraph]: no [MapDerivedType] registered for runtime node type '\" + ")
                          .Append("n.GetType() + \"'.\", nameof(n)),");
                    sbDisp.AppendLine();
                    sbDisp.AppendLine("        };");
                    synthesized[dispatchHelperName] = new SynthesizedMethod(dispatchHelperName, sbDisp.ToString());
                }

                // Synthesize __DwarfMap_FlattenGraph_<hash>(TBase entry) BFS traversal
                if (!synthesized.ContainsKey(traversalHelperNameH))
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq  = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var listFqH    = "global::System.Collections.Generic.List<" + dtoBaseFq + ">";
                    var queueFqH   = "global::System.Collections.Generic.Queue<" + nodeBaseFq + ">";
                    var hashSetFqH = "global::System.Collections.Generic.HashSet<object>";

                    var sbBfs = new System.Text.StringBuilder();
                    sbBfs.Append("    private ").Append(listFqH).Append(' ').Append(traversalHelperNameH)
                         .Append('(');
                    // MF-B: use the correct parameter type for the entry parameter.
                    if (srcNavIsArray)
                        sbBfs.Append(nodeBaseFq).AppendLine("[]? entry)");
                    else if (srcNavIsDict)
                        sbBfs.Append(srcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine("? entry)");
                    else if (srcNavIsCollection)
                        sbBfs.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeBaseFq).AppendLine(">? entry)");
                    else
                        sbBfs.Append(nodeBaseFq).AppendLine("? entry)");
                    sbBfs.AppendLine("    {");
                    sbBfs.Append("        var __result = new ").Append(listFqH).AppendLine("();");

                    if (srcNavIsDict)
                    {
                        // SF-F3: dict source nav — seed BFS from dict values.
                        sbBfs.AppendLine("        if (entry is null) return __result;");
                        sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                             .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                        sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                        sbBfs.AppendLine("        foreach (var __kv in entry) if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                    }
                    else if (srcNavIsCollection)
                    {
                        sbBfs.AppendLine("        if (entry is null) return __result;");
                        sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                             .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                        sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                        sbBfs.AppendLine("        foreach (var __seed in entry) if (__seed is not null && __visited.Add(__seed)) __queue.Enqueue(__seed);");
                    }
                    else
                    {
                        sbBfs.AppendLine("        if (entry is null) return __result;");
                        sbBfs.Append("        var __visited = new ").Append(hashSetFqH)
                             .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                        sbBfs.Append("        var __queue = new ").Append(queueFqH).AppendLine("();");
                        sbBfs.AppendLine("        __visited.Add(entry);");
                        sbBfs.AppendLine("        __queue.Enqueue(entry);");
                    }

                    sbBfs.AppendLine("        while (__queue.Count > 0)");
                    sbBfs.AppendLine("        {");
                    sbBfs.AppendLine("            var __n = __queue.Dequeue();");
                    sbBfs.Append("            __result.Add(").Append(dispatchHelperName).AppendLine("(__n));");

                    // Edge enumeration: runtime-type switch over concrete node types (most-derived-first)
                    sbBfs.AppendLine("            switch (__n)");
                    sbBfs.AppendLine("            {");
                    foreach (var arm in sortedHeteroArms)
                    {
                        var armSrcFq = arm.NodeDerived.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        // Use braces around each case block to create a new scope (avoids CS0128
                        // when two arms both introduce locals with the same base name, e.g. __e_Parent).
                        sbBfs.Append("                case ").Append(armSrcFq).AppendLine(" __t:");
                        sbBfs.AppendLine("                {");
                        // Enqueue each edge member of this arm
                        foreach (var edge in arm.EdgeMembers)
                        {
                            if (edge.IsDictValue)
                            {
                                // SF-F3: Dictionary<K,V> where V is a node — traverse values.
                                sbBfs.Append("                    if (__t.").Append(edge.Name)
                                     .Append(" is { } __d_").Append(edge.Name)
                                     .Append(") foreach (var __kv in __d_").Append(edge.Name)
                                     .AppendLine(") if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                            }
                            else if (!edge.IsCollection)
                            {
                                sbBfs.Append("                    if (__t.").Append(edge.Name)
                                     .Append(" is { } __e_").Append(edge.Name)
                                     .Append(" && __visited.Add(__e_").Append(edge.Name)
                                     .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                            }
                            else
                            {
                                sbBfs.Append("                    if (__t.").Append(edge.Name)
                                     .Append(" is { } __c_").Append(edge.Name)
                                     .Append(") foreach (var __x in __c_").Append(edge.Name)
                                     .AppendLine(") if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);");
                            }
                        }
                        sbBfs.AppendLine("                    break;");
                        sbBfs.AppendLine("                }");
                    }
                    sbBfs.AppendLine("            }");

                    sbBfs.AppendLine("        }");
                    sbBfs.AppendLine("        return __result;");
                    sbBfs.AppendLine("    }");
                    synthesized[traversalHelperNameH] = new SynthesizedMethod(traversalHelperNameH, sbBfs.ToString());
                }

                // For array targets, synthesize a thin .ToArray() wrapper
                string converterHelperNameH;
                if (needsToArray)
                {
                    var nodeBaseFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var dtoBaseFq  = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var wrapperNameH = "__DwarfMap_FlattenGraphArr_" + heteroHash;
                    if (!synthesized.ContainsKey(wrapperNameH))
                    {
                        var sbWr = new System.Text.StringBuilder();
                        sbWr.Append("    private ").Append(dtoBaseFq).Append("[] ").Append(wrapperNameH)
                            .Append('(');
                        // MF-B: match the traversal helper's parameter type.
                        if (srcNavIsArray)
                            sbWr.Append(nodeBaseFq).AppendLine("[]? entry)");
                        else if (srcNavIsCollection)
                            sbWr.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeBaseFq).AppendLine(">? entry)");
                        else
                            sbWr.Append(nodeBaseFq).AppendLine("? entry)");
                        sbWr.Append("        => ").Append(traversalHelperNameH).AppendLine("(entry).ToArray();");
                        synthesized[wrapperNameH] = new SynthesizedMethod(wrapperNameH, sbWr.ToString());
                    }
                    converterHelperNameH = wrapperNameH;
                }
                else
                {
                    converterHelperNameH = traversalHelperNameH;
                }

                consumedTargets.Add(tgtCollName);
                injected.Add(new MemberMap(
                    TargetName: tgtCollName,
                    SourceName: srcNavName,
                    ConverterMethod: converterHelperNameH,
                    NullHandling: Model.NullHandling.None,
                    ConverterNeedsDepthCtx: false,
                    SourceIsNullableRef: true));
                directives.Add(new Model.FlattenGraphDirective(
                    srcNavName, tgtCollName, traversalHelperNameH, converterHelperNameH));
                continue; // skip the homogeneous path below
            }
            // ── End Plan 22 heterogeneous branch ─────────────────────────────

            // 5. Validate nodeType → nodeDtoType structural compatibility.
            // We do NOT call TryResolveConversion here because it eagerly synthesizes helpers
            // (including recursion-capable Obj mappers and collection helpers) with baked-in
            // signatures that may become inconsistent once the drain loop marks them recursion-capable.
            // Instead: check structural compatibility — at minimum, nodeDtoType must be a named type
            // with a public parameterless constructor (or be constructable), OR there must be a declared
            // mapper for this pair. The flat-node helper only maps leaf members; unmappable leaves are
            // silently skipped, so the check is just a sanity gate on type kind.
            bool nodeDtoIsConstructible = false;
            if (nodeDtoType is INamedTypeSymbol namedDtoCheck)
            {
                nodeDtoIsConstructible =
                    (namedDtoCheck.TypeKind == TypeKind.Class || namedDtoCheck.TypeKind == TypeKind.Struct)
                    && namedDtoCheck.SpecialType == SpecialType.None
                    && namedDtoCheck.InstanceConstructors.Any(c =>
                        c.DeclaredAccessibility == Accessibility.Public);
            }
            // Also accept if there's an explicit declared mapper method for this pair.
            var hasDeclaredMapper = false;
            foreach (var m in allMethods)
            {
                if (HasImplicitConversion(compilation, nodeType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, nodeDtoType))
                {
                    hasDeclaredMapper = true;
                    break;
                }
            }
            // Accept implicit conversion too (value types, same type, etc.)
            var hasImplicit = HasImplicitConversion(compilation, nodeType, nodeDtoType);

            if (!nodeDtoIsConstructible && !hasDeclaredMapper && !hasImplicit)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidFlattenGraph, location,
                    $"[FlattenGraph] node DTO type '{nodeDtoType.Name}' is not constructible " +
                    $"(must be a class or struct with a public constructor)"));
                continue;
            }

            // 6. Partition TNode readable members into edge (same type as nodeType or collection thereof) vs leaf
            // SF-F4 fix: use bidirectional assignability (HasImplicitConversion both ways) instead of exact
            //   type equality so that interface-typed edges (e.g. "INode? Link" where node : INode) and
            //   base-class-typed edges are traversed.  For edges typed as an ancestor/interface of nodeType,
            //   the BFS enqueue must cast via `is TNode __var` (since the queue holds TNode, not the interface).
            // SF-F3 fix: detect Dictionary<K,V> where V is assignable to nodeType as a dict-value edge.
            var nodeMembers = ReadableMembers(nodeType).ToList();
            // Edge tuple: (Name, IsCollection, IsDictValue, NeedsNodeCast)
            // NeedsNodeCast=true: member type is an ancestor/interface of nodeType → enqueue via `is TNode` cast.
            var edgeMembers = new List<(string Name, bool IsCollection, bool IsDictValue, bool NeedsNodeCast)>();
            var leafMembers = new List<(string Name, ITypeSymbol Type)>();

            var nodeTypeNoAnnotation = nodeType.WithNullableAnnotation(NullableAnnotation.None);

            foreach (var nm in nodeMembers)
            {
                var memberTypeNoAnnotation = nm.Type.WithNullableAnnotation(NullableAnnotation.None);

                // SF-F4 fix: Direct node reference — recognise as a graph edge if:
                //   (a) memberType is assignable to nodeType (e.g. a derived subtype field), OR
                //   (b) nodeType is assignable to memberType (e.g. edge typed as an interface/base
                //       that the node implements/derives — "INode? Link" where node is a class : INode).
                // Was: exact equality only — missed interface-typed and base-typed edges.
                bool directToNode = HasImplicitConversion(compilation, memberTypeNoAnnotation, nodeType);
                bool nodeToMember = !directToNode && HasImplicitConversion(compilation, nodeTypeNoAnnotation, memberTypeNoAnnotation);
                if (directToNode || nodeToMember)
                {
                    // NeedsNodeCast: when the member type is a base/interface (reverse direction), we
                    // must cast via `is TNode __var` before enqueuing so the Queue<TNode> accepts it.
                    edgeMembers.Add((nm.Name, false, false, nodeToMember));
                    continue;
                }

                // Nullable<TNode> (for structs — unlikely but supported)
                if (nm.Type is INamedTypeSymbol nmNamed
                    && nmNamed.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                {
                    var innerNoAnnot = nmNamed.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None);
                    bool innerToNode = HasImplicitConversion(compilation, innerNoAnnot, nodeType);
                    bool nodeToInner = !innerToNode && HasImplicitConversion(compilation, nodeTypeNoAnnotation, innerNoAnnot);
                    if (innerToNode || nodeToInner)
                    {
                        edgeMembers.Add((nm.Name, false, false, nodeToInner));
                        continue;
                    }
                }

                // SF-F3 fix: Dictionary<K,V> where V is assignable to nodeType → dict-value edge.
                // Only V assignable to nodeType qualifies; keys are not traversed (v1: values only).
                if (DictionaryConverter.TryGetDictionaryValueType(nm.Type, out var dictValType)
                    && HasImplicitConversion(compilation, dictValType.WithNullableAnnotation(NullableAnnotation.None), nodeType))
                {
                    edgeMembers.Add((nm.Name, false, true, false)); // IsDictValue=true, no cast needed
                    continue;
                }

                // Collection of TNode (SF-F4 fix: use bidirectional assignability for element type)
                bool isEdgeColl = false;
                bool collNeedsCast = false;
                if (nm.Type is IArrayTypeSymbol arrEdge && arrEdge.Rank == 1)
                {
                    var arrElemNoAnnot = arrEdge.ElementType.WithNullableAnnotation(NullableAnnotation.None);
                    if (HasImplicitConversion(compilation, arrElemNoAnnot, nodeType))
                        isEdgeColl = true;
                    else if (HasImplicitConversion(compilation, nodeTypeNoAnnotation, arrElemNoAnnot))
                    { isEdgeColl = true; collNeedsCast = true; }
                }
                else if (CollectionConverter.TryGetEnumerableElement(nm.Type, out var edgeElem, out _))
                {
                    var edgeElemNoAnnot = edgeElem.WithNullableAnnotation(NullableAnnotation.None);
                    if (HasImplicitConversion(compilation, edgeElemNoAnnot, nodeType))
                        isEdgeColl = true;
                    else if (HasImplicitConversion(compilation, nodeTypeNoAnnotation, edgeElemNoAnnot))
                    { isEdgeColl = true; collNeedsCast = true; }
                }

                if (isEdgeColl)
                {
                    edgeMembers.Add((nm.Name, true, false, collNeedsCast));
                }
                else
                {
                    leafMembers.Add((nm.Name, nm.Type));
                }
            }

            // 7. Get writable members of nodeDtoType for the flat-node helper
            var dtoWritable = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
            foreach (var m in WritableMembers(nodeDtoType))
                dtoWritable[m.Name] = m.Type;

            // 8. Build hash key and helper names
            var hashKey = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + "=>"
                        + nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var hash = FlattenGraphHash(hashKey);

            var flatNodeHelperName    = "__DwarfMap_FlatNode_" + hash;
            var traversalHelperName   = "__DwarfMap_FlattenGraph_" + hash;

            // 9. Synthesize __DwarfMap_FlatNode_HASH (maps one TNode leaf-only → TNodeDto)
            if (!synthesized.ContainsKey(flatNodeHelperName))
            {
                var nodeFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq  = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var sb = new System.Text.StringBuilder();
                sb.Append("    private ").Append(dtoFq).Append(' ').Append(flatNodeHelperName)
                  .Append('(').Append(nodeFq).AppendLine("? n)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (n is null) return null!;");
                sb.Append("        return new ").Append(dtoFq).AppendLine();
                sb.AppendLine("        {");

                // Leaf members: map with conversion where available.
                // MF-D fix: flat-node leaf synthesis must NEVER call a synthesized complex helper
                // (one that starts with "__DwarfMap_") because those helpers may later be force-marked
                // recursion-capable (3-param) by the Preserve force-marking loop, creating a
                // signature mismatch when the flat-node helper calls them with 1 arg → CS7036.
                // Strategy: use a THROW-AWAY synthesized dict for complex resolutions — this
                // prevents polluting the main dict with a shared Obj/Dict/Coll helper that will
                // later become 3-param.  Only emit the leaf if the resulting converter is either:
                //   (a) null (direct assignment — primitive/same-type),
                //   (b) a declared user method (doesn't start with "__DwarfMap_"), OR
                //   (c) a synthesized PRIMITIVE helper (enum/numeric/parsable, which are never 3-param).
                // If the resolved converter is a synthesized complex helper, skip the member
                // (leave DTO default) — that's correct topology-degraded behaviour for flat-graph.
                // SF-LEAFDIAG fix: propagate diagnostics for truly unmappable leaf members to the
                // real diagnostics list so callers get a DWARF005/etc. error rather than silence.
                foreach (var leaf in leafMembers)
                {
                    if (!dtoWritable.TryGetValue(leaf.Name, out var dtoMemberType))
                        continue;

                    // Use a throw-away synth dict so complex helpers (Obj/Dict/Coll) are NOT
                    // registered in the main dict and cannot be force-marked 3-param later.
                    var leafThrowAwaySynth = new Dictionary<string, SynthesizedMethod>(
                        System.StringComparer.Ordinal);
                    var leafTestDiags = new List<DiagnosticInfo>();
                    bool leafResolved = TryResolveConversion(compilation, leaf.Type, dtoMemberType, null,
                            allMethods, autoCandidates, enumStrategy, leafThrowAwaySynth, nullStrategy,
                            location, leaf.Name, leafTestDiags, out var leafConv, out var leafNull, out _,
                            autoNest, nestedRegistry, nullAsNull: false, isPreserve: false);

                    if (!leafResolved)
                    {
                        // SF-LEAFDIAG: propagate errors from unmappable leaf members (not silently dropped).
                        diagnostics.AddRange(leafTestDiags);
                        continue;
                    }

                    // MF-D: if the converter is a synthesized COMPLEX helper (object mapper,
                    // collection helper, or dict helper), skip this member (leave DTO default).
                    // Complex helpers may be force-marked 3-param by the Preserve post-processing,
                    // creating a signature mismatch when the flat-node helper calls them with 1 arg.
                    // Safe helpers (numeric, enum, parsable, blit) are always single-arg and never
                    // force-marked — they are allowed through.
                    // Unsafe prefixes: __DwarfMap_Obj_, __DwarfMap_Coll_, __DwarfMap_Dict_
                    // Safe prefixes:   __DwarfMap_Num_, __DwarfMap_Enum_, __DwarfMap_Pars_, __DwarfMap_Blit_
                    if (leafConv is not null
                        && (leafConv.StartsWith("__DwarfMap_Obj_", System.StringComparison.Ordinal)
                            || leafConv.StartsWith("__DwarfMap_Coll_", System.StringComparison.Ordinal)
                            || leafConv.StartsWith("__DwarfMap_Dict_", System.StringComparison.Ordinal)))
                    {
                        // Complex synthesized helper — skip to avoid future 3-param mismatch.
                        // Don't register leafThrowAwaySynth entries in main dict.
                        continue;
                    }

                    // Safe: emit the leaf member.  Merge any non-complex throw-away entries
                    // (numeric, enum, parsable helpers that are never force-marked 3-param).
                    foreach (var kv in leafThrowAwaySynth)
                    {
                        if (!synthesized.ContainsKey(kv.Key))
                            synthesized[kv.Key] = kv.Value;
                    }

                    sb.Append("            ").Append(leaf.Name).Append(" = ");
                    AppendFlatNodeMemberExpr(sb, "n", leaf.Name, leafConv, leafNull);
                    sb.AppendLine(",");
                }

                // Edge members on DTO: null them out (topology degradation — the point of [FlattenGraph])
                foreach (var edge in edgeMembers)
                {
                    if (!dtoWritable.ContainsKey(edge.Name))
                        continue;
                    sb.Append("            ").Append(edge.Name).AppendLine(" = null,");
                }

                sb.AppendLine("        };");
                sb.AppendLine("    }");
                synthesized[flatNodeHelperName] = new SynthesizedMethod(flatNodeHelperName, sb.ToString());
            }

            // 10. Synthesize __DwarfMap_FlattenGraph_HASH (BFS traversal → List<TNodeDto>)
            if (!synthesized.ContainsKey(traversalHelperName))
            {
                var nodeFq    = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq     = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var listFq    = "global::System.Collections.Generic.List<" + dtoFq + ">";
                var queueFq   = "global::System.Collections.Generic.Queue<" + nodeFq + ">";
                var hashSetFq = "global::System.Collections.Generic.HashSet<object>";

                var sb = new System.Text.StringBuilder();
                sb.Append("    private ").Append(listFq).Append(' ').Append(traversalHelperName)
                  .Append('(');
                // MF-B: use the correct parameter type for the entry parameter.
                // Array nav    → TNode[]?  (exact array type, avoids CS1503 with T[])
                // Dict nav     → DictType? (seed from .Values — SF-F3 dict source nav)
                // Non-array coll nav → IEnumerable<TNode>? (accepts List<T>, HashSet<T>, IReadOnlyList<T>, …)
                // Single-ref nav → TNode? (nullable reference)
                if (srcNavIsArray)
                    sb.Append(nodeFq).AppendLine("[]? entry)");
                else if (srcNavIsDict)
                    sb.Append(srcNavType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine("? entry)");
                else if (srcNavIsCollection)
                    sb.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeFq).AppendLine(">? entry)");
                else
                    sb.Append(nodeFq).AppendLine("? entry)");
                sb.AppendLine("    {");
                sb.Append("        var __result = new ").Append(listFq).AppendLine("();");

                if (srcNavIsDict)
                {
                    // SF-F3: dict source nav — seed BFS from dict values (not kvp pairs).
                    sb.AppendLine("        if (entry is null) return __result;");
                    sb.Append("        var __visited = new ").Append(hashSetFq)
                      .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sb.Append("        var __queue = new ").Append(queueFq).AppendLine("();");
                    sb.AppendLine("        foreach (var __kv in entry) if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                }
                else if (srcNavIsCollection)
                {
                    // Entry is a collection — seed the queue from all non-null elements
                    sb.AppendLine("        if (entry is null) return __result;");
                    sb.Append("        var __visited = new ").Append(hashSetFq)
                      .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sb.Append("        var __queue = new ").Append(queueFq).AppendLine("();");
                    sb.AppendLine("        foreach (var __seed in entry) if (__seed is not null && __visited.Add(__seed)) __queue.Enqueue(__seed);");
                }
                else
                {
                    sb.AppendLine("        if (entry is null) return __result;");
                    sb.Append("        var __visited = new ").Append(hashSetFq)
                      .AppendLine("(global::System.Collections.Generic.ReferenceEqualityComparer.Instance);");
                    sb.Append("        var __queue = new ").Append(queueFq).AppendLine("();");
                    sb.AppendLine("        __visited.Add(entry);");
                    sb.AppendLine("        __queue.Enqueue(entry);");
                }

                sb.AppendLine("        while (__queue.Count > 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            var __n = __queue.Dequeue();");
                sb.Append("            __result.Add(").Append(flatNodeHelperName).AppendLine("(__n));");

                // Enqueue reachable nodes via edge members of TNode
                foreach (var edge in edgeMembers)
                {
                    if (edge.IsDictValue)
                    {
                        // SF-F3: Dictionary<K,V> where V is a node — traverse values, not keys.
                        sb.Append("            if (__n.").Append(edge.Name)
                          .Append(" is { } __d_").Append(edge.Name)
                          .Append(") foreach (var __kv in __d_").Append(edge.Name)
                          .AppendLine(") if (__kv.Value is not null && __visited.Add(__kv.Value)) __queue.Enqueue(__kv.Value);");
                    }
                    else if (!edge.IsCollection)
                    {
                        if (edge.NeedsNodeCast)
                        {
                            // SF-F4: edge typed as interface/base → use `is TNode` pattern to cast
                            // and filter to only concrete TNode values (safe: we're BFS-ing a TNode graph).
                            sb.Append("            if (__n.").Append(edge.Name)
                              .Append(" is ").Append(nodeFq).Append(" __e_").Append(edge.Name)
                              .Append(" && __visited.Add(__e_").Append(edge.Name)
                              .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                        }
                        else
                        {
                            sb.Append("            if (__n.").Append(edge.Name)
                              .Append(" is { } __e_").Append(edge.Name)
                              .Append(" && __visited.Add(__e_").Append(edge.Name)
                              .Append(")) __queue.Enqueue(__e_").Append(edge.Name).AppendLine(");");
                        }
                    }
                    else
                    {
                        if (edge.NeedsNodeCast)
                        {
                            // SF-F4: collection of interface/base elements → cast each.
                            sb.Append("            if (__n.").Append(edge.Name)
                              .Append(" is { } __c_").Append(edge.Name)
                              .Append(") foreach (var __xi in __c_").Append(edge.Name)
                              .Append(") if (__xi is ").Append(nodeFq).AppendLine(" __x && __visited.Add(__x)) __queue.Enqueue(__x);");
                        }
                        else
                        {
                            sb.Append("            if (__n.").Append(edge.Name)
                              .Append(" is { } __c_").Append(edge.Name)
                              .Append(") foreach (var __x in __c_").Append(edge.Name)
                              .AppendLine(") if (__x is not null && __visited.Add(__x)) __queue.Enqueue(__x);");
                        }
                    }
                }

                sb.AppendLine("        }");
                sb.AppendLine("        return __result;");
                sb.AppendLine("    }");
                synthesized[traversalHelperName] = new SynthesizedMethod(traversalHelperName, sb.ToString());
            }

            // 11. For array targets, synthesize a thin .ToArray() wrapper
            string converterHelperName;
            if (needsToArray)
            {
                var nodeFq = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var dtoFq  = nodeDtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var wrapperName = "__DwarfMap_FlattenGraphArr_" + hash;
                if (!synthesized.ContainsKey(wrapperName))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("    private ").Append(dtoFq).Append("[] ").Append(wrapperName)
                      .Append('(');
                    // MF-B: match the traversal helper's parameter type.
                    if (srcNavIsArray)
                        sb.Append(nodeFq).AppendLine("[]? entry)");
                    else if (srcNavIsCollection)
                        sb.Append("global::System.Collections.Generic.IEnumerable<").Append(nodeFq).AppendLine(">? entry)");
                    else
                        sb.Append(nodeFq).AppendLine("? entry)");
                    sb.Append("        => ").Append(traversalHelperName).AppendLine("(entry).ToArray();");
                    synthesized[wrapperName] = new SynthesizedMethod(wrapperName, sb.ToString());
                }
                converterHelperName = wrapperName;
            }
            else
            {
                converterHelperName = traversalHelperName;
            }

            // 12. Mark target collection as consumed (ResolveMembers must skip it)
            consumedTargets.Add(tgtCollName);

            // 13. Build a MemberMap for the injection — emitter handles it like any other member
            //     SourceIsNullableRef=true ensures '!' is added if needed (the traversal helper handles null internally)
            injected.Add(new MemberMap(
                TargetName: tgtCollName,
                SourceName: srcNavName,
                ConverterMethod: converterHelperName,
                NullHandling: Model.NullHandling.None,
                ConverterNeedsDepthCtx: false,
                SourceIsNullableRef: true));

            // 14. Record directive (for model completeness / snapshot tests)
            directives.Add(new Model.FlattenGraphDirective(
                srcNavName, tgtCollName, traversalHelperName, converterHelperName));
        }

        return (directives, injected);
    }

    /// <summary>
    /// Appends a member-access value expression to <paramref name="sb"/> for use inside
    /// a <c>__DwarfMap_FlatNode_*</c> helper. Does NOT append trailing comma or newline.
    /// </summary>
    private static void AppendFlatNodeMemberExpr(
        System.Text.StringBuilder sb,
        string paramName,
        string memberName,
        string? conv,
        Model.NullHandling nh)
    {
        var access = paramName + "." + memberName;
        if (conv is not null)
        {
            var needsBang = conv.StartsWith("__DwarfMap_", System.StringComparison.Ordinal);
            sb.Append(conv).Append('(').Append(access).Append(needsBang ? "!" : "").Append(')');
        }
        else
        {
            switch (nh)
            {
                case Model.NullHandling.ThrowIfNull:
                    sb.Append(access)
                      .Append(" ?? throw new global::System.InvalidOperationException(\"Source member '")
                      .Append(memberName).Append("' was null\")");
                    break;
                case Model.NullHandling.ValueOrDefault:
                    sb.Append(access).Append(".GetValueOrDefault()");
                    break;
                default:
                    sb.Append(access);
                    break;
            }
        }
    }

    private static List<string> ReadReinterpretMembers(ISymbol method)
    {
        var members = new List<string>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.ReinterpretAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string m)
            {
                members.Add(m);
            }
        }
        return members;
    }

    private static EnumStrategy ReadEnumStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "EnumStrategy" && named.Value.Value is int i)
                {
                    return (EnumStrategy)i;
                }
            }
        }
        return EnumStrategy.ByName;
    }

    private static NullStrategy ReadNullStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "NullStrategy" && named.Value.Value is int i)
                {
                    return (NullStrategy)i;
                }
            }
        }
        return NullStrategy.Throw;
    }

    private static bool ReadCaseInsensitive(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "CaseInsensitive" && named.Value.Value is bool b)
                {
                    return b;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.AutoNest"/> value
    /// from the <c>[DwarfMapper]</c> attribute. Defaults to <c>true</c>.
    /// </summary>
    /// <summary>
    /// Reads <c>[DwarfMapper(MaxDepth = N)]</c>; defaults to 64; clamps to [1, 1000].
    /// </summary>
    private static int ReadMaxDepth(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "MaxDepth" && named.Value.Value is int i)
                {
                    // Clamp to [1, 1000] — matches DwarfRefContext.AbsoluteMaxDepth
                    if (i < 1) return 1;
                    if (i > 1000) return 1000;
                    return i;
                }
            }
        }
        return 64; // default
    }

    private static bool ReadAutoNest(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "AutoNest" && named.Value.Value is bool b)
                {
                    return b;
                }
            }
        }
        return true; // default: auto-nesting enabled
    }

    /// <summary>
    /// Reads <c>[DwarfMapper(NullCollections = ...)]</c>; defaults to <c>AsEmpty</c>.
    /// </summary>
    private static NullCollectionsBehavior ReadNullCollections(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "NullCollections" && named.Value.Value is int i)
                    return (NullCollectionsBehavior)i;
            }
        }
        return NullCollectionsBehavior.AsEmpty;
    }

    /// <summary>
    /// Reads <c>[DwarfMapper(ReferenceHandling = ...)]</c>; returns the integer value of the
    /// <see cref="DwarfMapper.ReferenceHandlingStrategy"/> enum (0 = None, 1 = Preserve).
    /// Defaults to 0 (None).
    /// </summary>
    private static int ReadReferenceHandling(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "ReferenceHandling" && named.Value.Value is int i)
                    return i;
            }
        }
        return 0; // None
    }

    /// <summary>
    /// Reads <c>[DwarfMapper(OnCycle = ...)]</c>; returns the integer value of the
    /// <see cref="DwarfMapper.OnCycleStrategy"/> enum (0 = Throw, 1 = SetNull).
    /// Defaults to 0 (Throw).
    /// </summary>
    private static int ReadOnCycle(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "OnCycle" && named.Value.Value is int i)
                    return i;
            }
        }
        return 0; // Throw
    }

    /// <summary>Reads <c>[DwarfMapper(ImplicitConversions = ...)]</c>; defaults to <c>true</c> (permissive).</summary>
    private static bool ReadImplicitConversions(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "ImplicitConversions" && named.Value.Value is bool b)
                    return b;
        return true;
    }

    /// <summary>
    /// Reads <c>[DwarfMapper(RequiredMapping = ...)]</c>. Returns the enum's int value:
    /// 0 = <c>Target</c> (default — destination-coverage only), 1 = <c>Both</c> (also require every
    /// source member consumed → DWARF039 for leftovers).
    /// </summary>
    private static int ReadRequiredMapping(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
            foreach (var named in attr.NamedArguments)
                if (named.Key == "RequiredMapping" && named.Value.Value is int v)
                    return v;
        return 0; // RequiredMappingStrategy.Target
    }

    /// <summary>
    /// Adds the top-level source member consumed by <paramref name="sourceName"/> to
    /// <paramref name="consumed"/>. A dotted path (a flattened leaf like <c>"Address.City"</c>) marks its
    /// root (<c>Address</c>) consumed; the empty sentinel (top-level collection / constant value) is ignored.
    /// </summary>
    private static void AddConsumed(HashSet<string> consumed, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
            return;
        var dot = sourceName.IndexOf('.');
        consumed.Add(dot < 0 ? sourceName : sourceName.Substring(0, dot));
    }

    /// <summary>
    /// Reads the per-method <c>[AutoNest(bool)]</c> attribute override, falling back to
    /// <paramref name="classDefault"/> when the attribute is absent.
    /// </summary>
    private static bool ReadMethodAutoNest(IMethodSymbol method, bool classDefault)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.AutoNestAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is bool b)
            {
                return b;
            }
        }
        return classDefault;
    }

    private static bool IsQueryable(ITypeSymbol type, out ITypeSymbol element)
    {
        element = type;
        if (type is INamedTypeSymbol n && n.Name == "IQueryable" && n.TypeArguments.Length == 1
            && n.ContainingNamespace?.ToDisplayString() == "System.Linq")
        {
            element = n.TypeArguments[0];
            return true;
        }
        return false;
    }

    // ── Plan 19D: max depth for projection recursion ─────────────────────────
    // Beyond this depth, DWARF028 is emitted instead of recursing further.
    // Keeps generated lambda bodies finite and prevents stack-overflow in the generator.
    private const int ProjectionMaxDepth = 32;

    /// <summary>
    /// Emits DWARF028 (ProjectionNotTranslatable) with a fully-formatted single-arg message.
    /// The descriptor uses "{0}" so both member name and reason are concatenated here.
    /// </summary>
    private static void EmitDWARF028(
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string memberName,
        string reason)
    {
        var msg = $"Projection member '{memberName}' cannot be translated to SQL: {reason}.";
        diagnostics.Add(new DiagnosticInfo(
            DiagnosticDescriptors.ProjectionNotTranslatable, location, msg));
    }

    /// <summary>
    /// New (Plan 19D) recursive projection resolver. Produces a list of
    /// <see cref="ProjectionMemberMap"/> with inline expression fragments (no helper calls).
    ///
    /// DWARF019 vs. DWARF028 decision:
    ///   DWARF019 (NotProjectable) is kept ONLY for attribute-conflict cases in explicit maps
    ///   ([MapProperty(Use=)] on a projection method — that is an attribute usage error).
    ///   DWARF028 (ProjectionNotTranslatable) is used for all type-conversion unsafety:
    ///   narrowing numeric, parsable string↔T, enum by-name, non-translatable collections,
    ///   reference handling, and "no translatable conversion found" fallback.
    /// </summary>
    private static List<ProjectionMemberMap> ResolveProjectionMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        EnumStrategy enumStrategy, int referenceHandling, string paramExpr)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
        var sources = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.First(), comparer);
        // C4: pass comparer to nested resolvers so CaseInsensitive propagates into nested objects.
        var writableByName = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType)) { writableByName[m.Name] = m.Type; }

        var result = new List<ProjectionMemberMap>();
        var handled = new HashSet<string>(System.StringComparer.Ordinal);
        var explicitSeen = new HashSet<string>(System.StringComparer.Ordinal);

        // ── Explicit maps ([MapProperty]) ────────────────────────────────────
        foreach (var (srcName, tgtName, use) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }
            handled.Add(tgtName);
            if (ignores.Contains(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }
            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }
            // DWARF019 for Use= (attribute conflict, not a type error)
            if (use is not null)
            {
                EmitDWARF028(diagnostics, location, tgtName,
                    "custom converter (Use=) is not translatable in projection; remove Use= or map at runtime");
                continue;
            }
            var sm = ReadableMembers(sourceType)
                .Where(m => System.StringComparer.Ordinal.Equals(m.Name, srcName))
                .Select(m => (ITypeSymbol?)m.Type).FirstOrDefault();
            if (sm is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }
            var srcExprForExplicit = paramExpr + "." + srcName;
            var inlineExpr = ResolveProjectionExpr(
                sm, tgtType, srcExprForExplicit, 0, compilation, location,
                diagnostics, tgtName, enumStrategy, comparer);
            if (inlineExpr is not null)
                result.Add(new ProjectionMemberMap(tgtName, inlineExpr));
        }

        // ── Auto-matched writable members ────────────────────────────────────

        // For IQueryable projection, member-init syntax is only SQL-translatable when the target
        // type has a public parameterless constructor (EF Core materialises via default ctor then
        // sets members). Positional records and other ctor-only types must use constructor projection.
        var hasParameterlessCtor = targetType.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && !c.IsStatic
            && c.Parameters.Length == 0);

        var writableMembers = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();

        if (writableMembers.Count == 0 || !hasParameterlessCtor)
        {
            // Try constructor projection: select the ctor with the most params matching source.
            ConstructorSelector.Select(targetType, diagnostics, location, out var ctorOnly);
            var bestCtor = targetType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length > 0)
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();

            if (bestCtor is not null)
            {
                // C4: pass comparer (carries CaseInsensitive setting) to ctor projection resolver.
                var ctorExpr = ResolveProjectionCtorExpr(
                    bestCtor, sourceType, paramExpr, 0,
                    compilation, location, diagnostics, targetType, enumStrategy, comparer);
                if (ctorExpr is not null)
                {
                    // Store as a whole-lambda body (TargetName = "")
                    result.Add(new ProjectionMemberMap("", ctorExpr));
                }
            }
            return result;
        }

        foreach (var target in writableMembers)
        {
            if (handled.Contains(target.Name) || ignores.Contains(target.Name)) { continue; }
            if (!sources.TryGetValue(target.Name, out var src))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }
            var srcAccessExpr = paramExpr + "." + src.Name;
            // C4: pass comparer so nested objects respect CaseInsensitive setting.
            var inlineExpr = ResolveProjectionExpr(
                src.Type, target.Type, srcAccessExpr, 0,
                compilation, location, diagnostics, target.Name, enumStrategy, comparer);
            if (inlineExpr is not null)
                result.Add(new ProjectionMemberMap(target.Name, inlineExpr));
        }

        return result;
    }

    /// <summary>
    /// Resolve a single inline projection expression for a source→target type pair.
    /// Returns the inline C# expression string (pure, no helper calls), or null when
    /// DWARF028 has been emitted (unsafe construct).
    ///
    /// SAFE:
    ///   1. Direct-assignable (implicit conversion incl. widening numeric).
    ///   2. Enum by-value cast: (TgtEnum)srcExpr.
    ///   3. Nested named object: new TgtType { M1 = ..., M2 = ... } (recursive).
    ///   4. Collection (projection-translatable): .Select(...).ToList()/.ToArray()/lazy.
    ///
    /// UNSAFE → DWARF028:
    ///   - Narrowing numeric (CreateChecked path).
    ///   - String↔T parsable (IParsable/IFormattable path).
    ///   - Enum by-name (switch path).
    ///   - Non-translatable collection target (HashSet/ISet/immutable/dict).
    ///   - Depth > ProjectionMaxDepth.
    ///   - No translatable conversion found.
    /// </summary>
    /// <param name="comparer">
    /// C4: the case-sensitivity comparer for member name matching; passed recursively into
    /// nested object and ctor resolvers so CaseInsensitive propagates to all depths.
    /// </param>
    private static string? ResolveProjectionExpr(
        ITypeSymbol srcType, ITypeSymbol tgtType,
        string srcExpr,
        int depth,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        string targetMemberName,
        EnumStrategy enumStrategy,
        System.StringComparer? comparer = null)
    {
        comparer ??= System.StringComparer.Ordinal;

        // ── Depth guard ───────────────────────────────────────────────────────
        if (depth > ProjectionMaxDepth)
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                $"projection nesting depth exceeded {ProjectionMaxDepth}; split into a runtime mapper");
            return null;
        }

        // ── Pre-check: collection/dictionary targets BEFORE implicit-conversion ──
        // EF Core cannot translate HashSet/Dictionary/immutable collection projections even
        // when source==target (same type is directly assignable but NOT SQL-translatable).
        // We must check collection-shaped types BEFORE the HasImplicitConversion fast-path.
        if (CollectionConverter.TryResolve(srcType, tgtType,
                out var srcElem, out var tgtElem, out var shape))
        {
            if (!CollectionConverter.IsTargetKindTranslatable(shape.Target))
            {
                var tgtTypeName = tgtType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                EmitDWARF028(diagnostics, location, targetMemberName,
                    $"collection type '{tgtTypeName}' is not translatable in projection (HashSet/ISet/immutable/Dictionary targets are not supported by EF Core)");
                return null;
            }

            // Translatable collection: emit .Select(...).ToList()/.ToArray()/lazy
            var elemParam = $"__i{depth}";
            // C4: propagate comparer into element expression resolver.
            var elemExpr = ResolveProjectionExpr(
                srcElem, tgtElem, elemParam, depth + 1,
                compilation, location, diagnostics, targetMemberName, enumStrategy, comparer);
            if (elemExpr is null) return null; // DWARF028 already emitted

            // Use fully-qualified Enumerable.Select to avoid needing 'using System.Linq' in generated code.
            var srcElemFqn = srcElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var tgtElemFqn = tgtElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var selectCall = $"global::System.Linq.Enumerable.Select<{srcElemFqn}, {tgtElemFqn}>({srcExpr}, {elemParam} => {elemExpr})";
            var collectionExpr = shape.Target switch
            {
                CollectionConverter.TargetKind.Array =>
                    $"global::System.Linq.Enumerable.ToArray<{tgtElemFqn}>({selectCall})",
                CollectionConverter.TargetKind.IEnumerable =>
                    selectCall, // lazy, no terminal
                _ =>
                    $"global::System.Linq.Enumerable.ToList<{tgtElemFqn}>({selectCall})",
            };
            // If the source collection expression is a reference type (could be null in projection),
            // guard it with a null-conditional ternary so Enumerable.Select is never called on null.
            // EF Core translates the ternary correctly. Array sources are also reference types.
            if (srcType.IsReferenceType)
            {
                return $"{srcExpr} == null ? null : {collectionExpr}";
            }
            return collectionExpr;
        }

        // ── Pre-check: Dictionary targets (always non-translatable in projection) ──
        // Check before HasImplicitConversion to catch same-type dictionary members.
        if (DictionaryConverter.TryResolve(srcType, tgtType,
                out _, out _, out _, out _, out _, out _))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "Dictionary targets are not translatable in projection; map at runtime");
            return null;
        }

        // ── 1. Direct-assignable (implicit — covers widening numeric, same-type, etc.) ──
        if (HasImplicitConversion(compilation, srcType, tgtType))
        {
            return srcExpr;
        }

        // ── 2. Enum by-value cast (enum→enum) ─────────────────────────────────
        if (srcType.TypeKind == TypeKind.Enum && tgtType.TypeKind == TypeKind.Enum
            && enumStrategy == EnumStrategy.ByValue)
        {
            var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({tgtFqn}){srcExpr}";
        }

        // ── C6: enum↔integral inline cast (SQL-translatable as a direct cast) ──
        // enum→integral (e.g. Status→int): cast to the integral type.
        // integral→enum (e.g. int→Status): cast to the enum type.
        // Only emit when the conversion is widening or same-width (safe). Narrowing (enum:long→int)
        // would need CreateChecked — fall through to DWARF028 for that case.
        if (srcType.TypeKind == TypeKind.Enum && TypeInterfaces.IsIntegral(tgtType))
        {
            // Get the enum's underlying integral type for a width-safety check.
            var enumUnderlying = ((INamedTypeSymbol)srcType).EnumUnderlyingType;
            if (enumUnderlying is not null && IsWideningOrSameWidth(enumUnderlying, tgtType))
            {
                var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"({tgtFqn}){srcExpr}";
            }
            // Narrowing cast (e.g. enum:long→int) — fall through to DWARF028.
        }
        else if (TypeInterfaces.IsIntegral(srcType) && tgtType.TypeKind == TypeKind.Enum)
        {
            // integral→enum: safe when source integral width ≤ enum underlying width.
            var enumUnderlying = ((INamedTypeSymbol)tgtType).EnumUnderlyingType;
            if (enumUnderlying is not null && IsWideningOrSameWidth(srcType, enumUnderlying))
            {
                var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"({tgtFqn}){srcExpr}";
            }
            // Narrowing — fall through to DWARF028.
        }

        // ── UNSAFE: enum by-name (enumStrategy == ByName, different enum types) ──
        if ((srcType.TypeKind == TypeKind.Enum || tgtType.TypeKind == TypeKind.Enum)
            && enumStrategy == EnumStrategy.ByName)
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "enum by-name mapping is not translatable in projection; use EnumStrategy.ByValue or map at runtime");
            return null;
        }

        // ── UNSAFE: numeric narrowing (NumericConverter would fire: both integral, no implicit) ──
        if (TypeInterfaces.IsIntegral(srcType) && TypeInterfaces.IsIntegral(tgtType))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "narrowing numeric conversion is not SQL-translatable (would need CreateChecked); map at runtime or use a widening target type");
            return null;
        }

        // ── UNSAFE: string↔T parsable (ParsableConverter would fire) ─────────
        if ((srcType.SpecialType == SpecialType.System_String
             && tgtType.TypeKind != TypeKind.Enum
             && TypeInterfaces.ImplementsIParsable(compilation, tgtType))
            || (tgtType.SpecialType == SpecialType.System_String
                && srcType.SpecialType != SpecialType.System_String
                && srcType.TypeKind != TypeKind.Enum
                && (TypeInterfaces.ImplementsIFormattable(srcType)
                    || srcType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char)))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "string parse/format is not translatable in projection (IParsable/IFormattable); map at runtime");
            return null;
        }

        // ── 3. Nested named object (recursive) ───────────────────────────────
        if (srcType is INamedTypeSymbol namedSrc && tgtType is INamedTypeSymbol namedTgt
            && IsMappableObjectPair(compilation, srcType, namedTgt))
        {
            // C4: pass comparer into nested object resolver.
            return ResolveProjectionNestedObjectExpr(
                namedSrc, namedTgt, srcExpr, depth, compilation, location, diagnostics,
                targetMemberName, enumStrategy, comparer);
        }

        // ── Nullable T? → nullable U? or non-nullable U ───────────────────────
        // C5: when source is Nullable<T> and target is also Nullable<U>, emit a null-preserving
        // HasValue ternary (SQL-translatable) instead of .Value (throws on null).
        if (IsNullableValue(srcType, out var srcUnderlying))
        {
            if (IsNullableValue(tgtType, out var tgtUnderlying))
            {
                // int?→long?: null-preserving ternary: __s.X.HasValue ? (long?)__s.X.Value : null
                var innerExpr = ResolveProjectionExpr(
                    srcUnderlying, tgtUnderlying, srcExpr + ".Value", depth,
                    compilation, location, diagnostics, targetMemberName, enumStrategy, comparer);
                if (innerExpr is null) return null;
                var tgtNullableFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"{srcExpr}.HasValue ? ({tgtNullableFqn}){innerExpr} : null";
            }
            else
            {
                // int?→long (non-nullable target): keep .Value (user asked for non-null; throws on null).
                var innerExpr = ResolveProjectionExpr(
                    srcUnderlying, tgtType, srcExpr + ".Value", depth,
                    compilation, location, diagnostics, targetMemberName, enumStrategy, comparer);
                return innerExpr;
            }
        }

        // ── Fallback: no translatable conversion found ────────────────────────
        EmitDWARF028(diagnostics, location, targetMemberName,
            "no translatable conversion found; map at runtime instead");
        return null;
    }

    /// <summary>
    /// C6 helper: returns true when a cast from <paramref name="src"/> to <paramref name="tgt"/> is
    /// widening or same-width (thus safe as a direct inline cast in SQL projection).
    /// Both must be integral types.
    /// </summary>
    private static bool IsWideningOrSameWidth(ITypeSymbol src, ITypeSymbol tgt)
    {
        // Return the bit-width rank of an integral type (0 = unknown/unsafe).
        static int Rank(ITypeSymbol t) => t.SpecialType switch
        {
            SpecialType.System_Byte    => 1,
            SpecialType.System_SByte   => 2,
            SpecialType.System_UInt16  => 3,
            SpecialType.System_Int16   => 4,
            SpecialType.System_UInt32  => 5,
            SpecialType.System_Int32   => 6,
            SpecialType.System_UInt64  => 7,
            SpecialType.System_Int64   => 8,
            _ => 0,
        };
        var sr = Rank(src);
        var tr = Rank(tgt);
        return sr > 0 && tr > 0 && sr <= tr;
    }

    /// <summary>
    /// Build an inline member-init expression for a nested object target.
    /// For nullable reference source: emits null-navigation ternary.
    /// For non-null / value-type source: emits plain member-init.
    /// </summary>
    /// <param name="comparer">
    /// C4: the case-sensitivity comparer for member name matching, propagated from the top-level
    /// call site so CaseInsensitive works at all nesting depths.
    /// </param>
    private static string? ResolveProjectionNestedObjectExpr(
        INamedTypeSymbol srcType, INamedTypeSymbol tgtType,
        string srcExpr, int depth,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        string targetMemberName, EnumStrategy enumStrategy,
        System.StringComparer? comparer = null)
    {
        comparer ??= System.StringComparer.Ordinal;
        var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // C4: use the configured comparer for member lookup so CaseInsensitive applies here.
        var srcReadable = ReadableMembers(srcType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.First(), comparer);

        // Build member-init or ctor expression for the nested object.
        // Mirror the decision logic in ResolveProjectionMembers:
        //   • If the target has NO public parameterless constructor (positional record / ctor-only),
        //     use constructor projection new T(arg0, arg1) — EF/expression-trees disallow named args.
        //   • Otherwise use member-init new T { P1 = ..., P2 = ... }.
        // The original check "writableTargetMembers.Count == 0" only catches types with no
        // settable/init properties at all; it misses positional records whose init properties
        // exist but whose constructor has no parameterless overload (CS7036 at compile time).
        var writableTargetMembers = WritableMembers(tgtType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();

        var hasParameterlessCtor = tgtType.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && !c.IsStatic
            && c.Parameters.Length == 0);

        string innerBodyExpr;

        if (writableTargetMembers.Count == 0 || !hasParameterlessCtor)
        {
            // Try ctor projection: use the public ctor with the most parameters.
            // Expression trees require POSITIONAL args (CS0853: named args not allowed).
            var bestCtor = tgtType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length > 0)
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();

            if (bestCtor is null)
            {
                EmitDWARF028(diagnostics, location, targetMemberName,
                    $"nested type '{tgtFqn}' has no writable members and no usable constructor");
                return null;
            }

            // C4: pass the configured comparer (not hardcoded Ordinal) so CaseInsensitive propagates.
            var ctorExpr = ResolveProjectionCtorExpr(
                bestCtor, srcType, srcExpr, depth,
                compilation, location, diagnostics, tgtType, enumStrategy,
                comparer);
            if (ctorExpr is null) return null;
            innerBodyExpr = ctorExpr;
        }
        else
        {
            // Member-init expression: new T { P1 = expr1, P2 = expr2 }
            var memberParts = new System.Collections.Generic.List<string>();
            var anyFailed = false;

            foreach (var tgtMember in writableTargetMembers)
            {
                if (!srcReadable.TryGetValue(tgtMember.Name, out var srcMember))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.UnmappedMember, location, targetMemberName + "." + tgtMember.Name));
                    anyFailed = true;
                    continue;
                }

                var memberSrcExpr = srcExpr + "." + srcMember.Name;
                // C4: propagate comparer into recursive member resolution.
                var memberInlineExpr = ResolveProjectionExpr(
                    srcMember.Type, tgtMember.Type, memberSrcExpr, depth + 1,
                    compilation, location, diagnostics,
                    targetMemberName + "." + tgtMember.Name, enumStrategy, comparer);

                if (memberInlineExpr is null)
                {
                    anyFailed = true;
                    continue;
                }
                memberParts.Add($"{tgtMember.Name} = {memberInlineExpr}");
            }

            if (anyFailed) return null;
            innerBodyExpr = $"new {tgtFqn} {{ {string.Join(", ", memberParts)} }}";
        }

        // Wrap with null-navigation ternary if source is a reference type (could be null in projection)
        if (srcType.IsReferenceType)
        {
            return $"{srcExpr} == null ? null : {innerBodyExpr}";
        }
        return innerBodyExpr;
    }

    /// <summary>
    /// Build an inline constructor-call expression for targets with only ctor params (records etc.).
    /// e.g. "new global::D.DstRec(x: __s.X, y: __s.Y)"
    /// </summary>
    private static string? ResolveProjectionCtorExpr(
        IMethodSymbol ctor,
        ITypeSymbol srcType,
        string srcExpr,
        int depth,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        INamedTypeSymbol tgtType,
        EnumStrategy enumStrategy,
        System.StringComparer comparer)
    {
        var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var srcReadable = ReadableMembers(srcType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.First(), comparer);

        var argParts = new System.Collections.Generic.List<string>();
        var anyFailed = false;

        foreach (var param in ctor.Parameters)
        {
            if (!srcReadable.TryGetValue(param.Name, out var srcMember))
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ConstructorParameterUnmapped, location, param.Name));
                anyFailed = true;
                continue;
            }

            var paramSrcExpr = srcExpr + "." + srcMember.Name;
            // C4: propagate comparer into ctor param expression resolver.
            var paramInlineExpr = ResolveProjectionExpr(
                srcMember.Type, param.Type, paramSrcExpr, depth + 1,
                compilation, location, diagnostics, param.Name, enumStrategy, comparer);

            if (paramInlineExpr is null)
            {
                anyFailed = true;
                continue;
            }
            // Expression trees do not allow named arguments (CS0853): emit positional args.
            argParts.Add(paramInlineExpr);
        }

        if (anyFailed) return null;
        return $"new {tgtFqn}({string.Join(", ", argParts)})";
    }

    /// <summary>
    /// DFS reachability: can we reach <paramref name="target"/> starting from <paramref name="start"/>
    /// by following edges in the call graph? Used to detect recursive method cycles.
    /// </summary>
    private static bool CanReach(
        Dictionary<string, HashSet<string>> graph,
        string start,
        string target)
    {
        var visited = new HashSet<string>(System.StringComparer.Ordinal);
        var stack = new Stack<string>();
        if (!graph.TryGetValue(start, out var startDeps)) return false;
        foreach (var dep in startDeps)
            stack.Push(dep);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (string.Equals(current, target, System.StringComparison.Ordinal)) return true;
            if (!visited.Add(current)) continue;
            if (graph.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                    stack.Push(dep);
            }
        }
        return false;
    }

    /// <summary>
    /// Recognises <c>System.Span&lt;T&gt;</c> / <c>System.ReadOnlySpan&lt;T&gt;</c>, returning the element
    /// type and whether it is the read-only form. Used to detect zero-alloc span map methods.
    /// </summary>
    private static bool TryGetSpanElement(ITypeSymbol t, out ITypeSymbol element, out bool isReadOnly)
    {
        element = null!;
        isReadOnly = false;
        if (t is INamedTypeSymbol n
            && n.TypeArguments.Length == 1
            && n.ContainingNamespace is { Name: "System" } ns
            && ns.ContainingNamespace?.IsGlobalNamespace == true
            && (string.Equals(n.Name, "Span", System.StringComparison.Ordinal)
                || string.Equals(n.Name, "ReadOnlySpan", System.StringComparison.Ordinal)))
        {
            element = n.TypeArguments[0];
            isReadOnly = string.Equals(n.Name, "ReadOnlySpan", System.StringComparison.Ordinal);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Recognises <c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c>, returning the element type.
    /// Used to detect async streaming map methods.
    /// </summary>
    private static bool TryGetAsyncEnumerableElement(ITypeSymbol t, out ITypeSymbol element)
    {
        element = null!;
        if (t is INamedTypeSymbol n
            && n.TypeArguments.Length == 1
            && string.Equals(n.Name, "IAsyncEnumerable", System.StringComparison.Ordinal)
            && n.ContainingNamespace is { Name: "Generic" } g
            && g.ContainingNamespace is { Name: "Collections" } c
            && c.ContainingNamespace is { Name: "System" } s
            && s.ContainingNamespace.IsGlobalNamespace)
        {
            element = n.TypeArguments[0];
            return true;
        }
        return false;
    }

    /// <summary>
    /// Emits DWARF038 for a non-lossless implicit basic-type conversion: an Info-level suggestion when
    /// <paramref name="implicitConversions"/> is true (permissive — the conversion is still applied), or a
    /// build Error when false (strict — the user must opt in via <c>[MapProperty(Use = …)]</c>).
    /// </summary>
    /// <summary>
    /// True when one type is integer-kind and the other is floating/decimal-kind (e.g. int↔double,
    /// long↔float, int↔decimal) — a cross-category numeric conversion. Same-category pairs (int↔long,
    /// float↔double) return false.
    /// </summary>
    private static bool IsCrossCategoryNumeric(ITypeSymbol src, ITypeSymbol tgt)
    {
        static int Cat(ITypeSymbol t) => t.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte
                or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Char => 1,                       // integer kind
            SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_Decimal => 2,                    // floating / decimal kind
            _ => 0,                                                     // not a numeric basic type
        };
        var a = Cat(src);
        var b = Cat(tgt);
        return a != 0 && b != 0 && a != b;
    }

    private static void EmitImplicitConversionDiag(
        List<DiagnosticInfo> diagnostics, LocationInfo? location, string targetName,
        ITypeSymbol srcType, ITypeSymbol tgtType, string kind, bool implicitConversions)
    {
        var src = srcType.ToDisplayString();
        var tgt = tgtType.ToDisplayString();
        var msg = implicitConversions
            ? $"Member '{targetName}': implicit {kind} conversion {src} → {tgt} is applied. Make it explicit with [MapProperty(Use = nameof(...))], or set [DwarfMapper(ImplicitConversions = false)] to require explicit conversions."
            : $"Member '{targetName}': implicit {kind} conversion {src} → {tgt} is disallowed ([DwarfMapper(ImplicitConversions = false)]). Map it explicitly with [MapProperty(Use = nameof(...))].";
        diagnostics.Add(new DiagnosticInfo(
            DiagnosticDescriptors.ImplicitConversionApplied, location, msg,
            SeverityOverride: implicitConversions ? DiagnosticSeverity.Info : DiagnosticSeverity.Error));
    }

    private static string AccessibilityText(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "public",
    };

    private static List<RoundTripPair> CollectRoundTrips(INamedTypeSymbol classSymbol, Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        var pairs = new List<RoundTripPair>();
        // Only emit a verifier when DwarfMapper.Testing is referenced — never force the test package into production.
        if (compilation.GetTypeByMetadataName("DwarfMapper.Testing.RoundTrip") is null)
        {
            return pairs;
        }

        var partials = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition && !m.ReturnsVoid && m.Parameters.Length == 1)
            .ToList();

        foreach (var fwd in partials)
        {
            if (!fwd.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.RoundTripAttribute"))
            {
                continue;
            }
            var loc = LocationInfo.From(fwd.Locations.FirstOrDefault() ?? Location.None);
            var src = fwd.Parameters[0].Type;
            var dto = fwd.ReturnType;

            var inverses = partials.Where(m =>
                !SymbolEqualityComparer.Default.Equals(m, fwd)
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, dto)
                && SymbolEqualityComparer.Default.Equals(m.ReturnType, src)).ToList();

            if (inverses.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RoundTripNoInverse, loc, fwd.Name));
                continue;
            }
            if (inverses.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RoundTripAmbiguousInverse, loc, fwd.Name));
                continue;
            }
            pairs.Add(new RoundTripPair(
                fwd.Name,
                inverses[0].Name,
                src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
        return pairs;
    }
}
