// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal enum NullStrategy
{
    Throw = 0,
    SetDefault = 1
}

internal enum NullCollectionsBehavior
{
    AsEmpty = 0,
    AsNull = 1
}

internal static partial class MapperExtractor
{
    private const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    // ── Plan 19D: max depth for projection recursion ─────────────────────────
    // Beyond this depth, DWARF028 is emitted instead of recursing further.
    // Keeps generated lambda bodies finite and prevents stack-overflow in the generator.
    private const int ProjectionMaxDepth = 32;

    public static MapperClassModel Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        return ExtractCore(ctx, false, ct);
    }

    /// <summary>
    ///     Entry for a class that carries <c>[GenerateMap&lt;&gt;]</c> but is NOT a <c>[DwarfMapper]</c> mapper —
    ///     the host (e.g. a DTO) declares its mapping co-located. The mapping is emitted into a SEPARATE generated
    ///     mapper type (<c>&lt;Host&gt;Mapper</c>), so the host needs neither <c>partial</c> nor <c>[DwarfMapper]</c>;
    ///     it is consumed via the generated extension methods / DI like any other mapper. Returns <c>null</c> when
    ///     the class is also a <c>[DwarfMapper]</c> (the primary pipeline owns it). Generic hosts and
    ///     <c>&lt;Host&gt;Mapper</c> name collisions are surfaced as diagnostics (DWARF054/DWARF057) by ExtractCore.
    /// </summary>
    public static MapperClassModel? ExtractGenerateMapHost(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        if (classSymbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == KnownNames.DwarfMapperFqn))
            return null; // a [DwarfMapper] class — the primary pipeline emits into it directly
        // Generic hosts (DWARF054) and <Host>Mapper name collisions (DWARF057) are reported loudly inside
        // ExtractCore rather than silently skipped here — see the never-silent design tenet.
        return ExtractCore(ctx, true, ct);
    }

    private static MapperClassModel ExtractCore(GeneratorAttributeSyntaxContext ctx, bool separateEmit,
        CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        // separateEmit: the mapping goes to a standalone generated `<Host>Mapper` (the host needs no partial /
        // [DwarfMapper]). Otherwise it is emitted into the [DwarfMapper] partial class itself.
        var emitClassName = separateEmit ? classSymbol.Name + "Mapper" : classSymbol.Name;
        var emitAccessibility = separateEmit ? "internal" : AccessibilityText(classSymbol.DeclaredAccessibility);

        var isPartial = classSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        if (!isPartial && !separateEmit)
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MapperNotPartial,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));

        var mapperNamespace = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : classSymbol.ContainingNamespace.ToDisplayString();

        // A generic mapper class would get a generated `partial class Foo` with no `<T>` — which is NOT a
        // partial of the user's `Foo<T>` and does not compile. Refuse loudly (DWARF054) and skip generation
        // entirely rather than emitting a broken, type-parameter-less partial. (Covers a generic [DwarfMapper]
        // class AND a generic co-located [GenerateMap<>] host.)
        if (classSymbol.IsGenericType || classSymbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.GenericMapperClassUnsupported,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));

            return new MapperClassModel(mapperNamespace, emitClassName, emitAccessibility,
                EquatableArray.From(new List<MapMethodModel>()),
                EquatableArray.From(diagnostics),
                EquatableArray.From(new List<SynthesizedMethod>()),
                EquatableArray.From(new List<RoundTripPair>()));
        }

        // Co-located emit (separateEmit) is the ONLY path that introduces a new type name. If a type named
        // <Host>Mapper already exists — a hand-written mapper, or a same-named [DwarfMapper] class whose
        // generated hint name would clash and abort ALL generation — report DWARF057 (blocking) and emit nothing
        // rather than producing an opaque downstream failure.
        if (separateEmit)
        {
            var fqMapper = mapperNamespace.Length == 0 ? emitClassName : mapperNamespace + "." + emitClassName;
            if (ctx.SemanticModel.Compilation.GetTypeByMetadataName(fqMapper) is not null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.CoLocatedMapperNameCollision,
                    LocationInfo.From(classSyntax.Identifier.GetLocation()),
                    $"the co-located mapping on '{classSymbol.Name}' would generate a mapper named '{fqMapper}', but a type with that name already exists — rename the existing type, or declare a [DwarfMapper] mapper class instead"));

                return new MapperClassModel(mapperNamespace, emitClassName, emitAccessibility,
                    EquatableArray.From(new List<MapMethodModel>()),
                    EquatableArray.From(diagnostics),
                    EquatableArray.From(new List<SynthesizedMethod>()),
                    EquatableArray.From(new List<RoundTripPair>()));
            }
        }

        var classIgnores = ReadIgnores(classSymbol).ToList();
        var classIgnoreSources = ReadIgnoreSources(classSymbol).ToList();

        // Assembly-wide default options ([assembly: DwarfMapperDefaults(...)]) layer UNDER the mapper's own
        // options. Every option reader returns the first matching named argument across the attribute list, so
        // appending the assembly-defaults attribute AFTER the class's [DwarfMapper] attribute gives exactly the
        // precedence we want — mapper > assembly defaults > built-in default — with no reader changes. Options
        // not present on DwarfMapperDefaults (MaxDepth, ReferenceHandling, OnCycle, GenerateExtensions) simply
        // never match there and stay per-mapper.
        var asmDefaults = ctx.SemanticModel.Compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == KnownNames.DwarfMapperDefaultsFqn);
        var opts = asmDefaults is null ? ctx.Attributes : ctx.Attributes.Add(asmDefaults);

        var requiredMapping = ReadRequiredMapping(opts); // 0 = Target (default), 1 = Both
        var nameConvention = ReadNameConvention(opts); // 0 = Exact (default), 1 = Flexible
        var caseInsensitive = ReadCaseInsensitive(opts);
        var generateExtensions = ReadGenerateExtensions(opts); // default true (opt-out)
        // The convenience facade caches a `new()` mapper singleton, so it can only be emitted for a mapper
        // that has an accessible parameterless constructor (the implicit one counts).
        // For separateEmit the cached facade singleton is `new <Host>Mapper()` — the generated mapper always
        // has an implicit parameterless constructor, regardless of the host type's own constructors.
        var hasParameterlessCtor = separateEmit || classSymbol.InstanceConstructors.Any(c =>
            !c.IsStatic && c.Parameters.Length == 0 &&
            c.DeclaredAccessibility != Accessibility.Private &&
            c.DeclaredAccessibility != Accessibility.Protected &&
            c.DeclaredAccessibility != Accessibility.ProtectedAndInternal);
        // Pair-scoped member config declared on the class ([MapProperty<S,T>] / [MapIgnore<T>] / [MapValue<T>]).
        // It is applied to [GenerateMap] pairs AND auto-synthesized nested pairs alike, so a pair can be configured without a
        // partial method. The mutable Consumed flags drive the DWARF056 "matched nothing" check at the end.
        var pairProps = ReadPairMapProperties(classSymbol);
        var pairIgnores = ReadPairIgnores(classSymbol);
        var pairValues = ReadPairMapValues(classSymbol);
        var pairConstructors = ReadPairConstructors(classSymbol);
        // Type-safe alternative front-end: MapConfig<S,T> convention methods, read syntactically (never executed).
        var mapConfig = ReadMapConfig(classSymbol, ctx.SemanticModel.Compilation, diagnostics);
        ReportMapConfigConflicts(pairProps, pairValues, mapConfig, diagnostics);
        pairProps.AddRange(mapConfig.Props);
        pairIgnores.AddRange(mapConfig.Ignores);
        pairValues.AddRange(mapConfig.Values);
        pairConstructors.AddRange(mapConfig.Constructors);
        classIgnoreSources.AddRange(mapConfig.IgnoreSources);
        var enumStrategy = ReadEnumStrategy(opts);
        var nullStrategy = ReadNullStrategy(opts);
        var classAutoNest = ReadAutoNest(opts);
        var explicitOnly = !ReadAutoMatchMembers(opts); // trust-boundary guard (DWARF072)
        var ignoreObsolete = ReadIgnoreObsoleteMembers(opts);
        var skipNullSrc = ReadSkipNullSourceMembers(opts);
        var allowNonPublic = ReadAllowNonPublic(opts);
        var nullCollections = ReadNullCollections(opts);
        var maxDepth = ReadMaxDepth(opts);
        var referenceHandling = ReadReferenceHandling(opts);
        var isPreserveMode = referenceHandling == 1; // 1 = ReferenceHandlingStrategy.Preserve
        var onCycle = ReadOnCycle(opts); // 0 = Throw, 1 = SetNull
        var implicitConversions = ReadImplicitConversions(opts); // default true (permissive)
        // SetNull is only meaningful in None mode; under Preserve, cycles are reconstructed and
        // OnCycle is ignored → DWARF037 (loud, not a silent no-op).
        var isSetNullMode = onCycle == 1 && !isPreserveMode;
        if (onCycle == 1 && isPreserveMode)
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.OnCycleIgnoredUnderPreserve,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));
        var synthesized = new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal);
        var allMethods = CollectMethods(classSymbol);
        var mapperMethods = CollectMapperMethods(classSymbol);
        var valueProviders = CollectValueProviders(classSymbol); // parameterless methods for [MapValue(Use=)]
        var (beforeHookDefs, afterHookDefs) = CollectHooks(classSymbol, diagnostics);
        var methods = new List<MapMethodModel>();

        // Best-effort source location per public map method (by index into `methods`), used only by the
        // DWARF060 same-source/multi-target collision pass below. The model itself carries no location.
        var publicMethodLocs = new Dictionary<int, LocationInfo?>();

        // NestedMappingRegistry: local to this Extract call (contains ISymbol — never stored in model).
        var nestedRegistry = new NestedMappingRegistry();

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition) continue;

            // A generic mapping method (arity > 0) cannot be implemented: the generator emits a
            // type-parameter-less body that fails to satisfy the generic partial declaration, producing
            // a confusing downstream C# error with no DwarfMapper signal. Refuse loudly (DWARF053) and
            // skip the method so no broken implementation is emitted.
            if (method.Arity > 0 || method.TypeParameters.Length > 0)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.GenericMapperMethodUnsupported,
                    LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None),
                    method.Name));
                continue;
            }

            var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None);

            // ── Zero-alloc span map: void Map(ReadOnlySpan<S>/Span<S> src, Span<D> dst) ──
            // Maps element-wise into a caller-provided destination buffer (no allocation). The
            // destination must be a writable Span<D>; a too-small destination throws (never silent
            // truncation). The element conversion reuses the full resolution pipeline.
            if (method.ReturnsVoid && method.Parameters.Length == 2
                                   && TryGetSpanElement(method.Parameters[0].Type, out var spanSrcElem, out _)
                                   && TryGetSpanElement(method.Parameters[1].Type, out var spanDstElem,
                                       out var dstIsReadOnly)
                                   && !dstIsReadOnly)
            {
                var spanComp = ctx.SemanticModel.Compilation;
                var spanAutoNest = ReadMethodAutoNest(method, classAutoNest);
                if (!TryResolveConversion(spanComp, spanSrcElem, spanDstElem, null, allMethods, mapperMethods,
                        enumStrategy, synthesized, nullStrategy, methodLocation, method.Name, diagnostics,
                        out var spanConv, out var spanNull, out var spanNeedsCtx, spanAutoNest, nestedRegistry))
                    // Element pair not mappable → diagnostic (e.g. DWARF005) already added.
                    continue;

                var spanElemMember = new MemberMap(
                    "", "", spanConv,
                    spanNull, spanNeedsCtx);

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    false,
                    EquatableArray.From(new[] { spanElemMember }),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
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
                foreach (var ig in ReadIgnores(method)) updIgnores.Add(ig);
                var updExplicit = ReadExplicitMaps(method);
                var updMapValues = ReadMapValues(method);
                var updMapPropExtras = ReadMapPropertyExtras(method);
                var updFlatten = ReadFlattenRoots(method);
                var updReinterpret = ReadReinterpretMembers(method);
                var updAutoNest = ReadMethodAutoNest(method, classAutoNest);

                var updMembers = ResolveMembers(
                    updSrc, updTgt, updIgnores, comp, methodLocation, diagnostics, caseInsensitive,
                    updExplicit, allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                    updFlatten, updReinterpret,
                    null, null, updAutoNest, nestedRegistry,
                    nullCollections == NullCollectionsBehavior.AsNull, false, false,
                    implicitConversions, updMapValues, valueProviders,
                    nameConvention: nameConvention, mapPropertyExtras: updMapPropExtras,
                    skipNullSourceMembers: skipNullSrc, allowNonPublic: allowNonPublic,
                    explicitOnly: explicitOnly, ignoreObsolete: ignoreObsolete,
                    stringFormats: ReadStringFormats(method));

                // Update-into assigns members post-construction, so init-only targets cannot be written
                // (they would emit CS8852). Treat them as read-only here: drop them and surface DWARF007
                // so the user adds [MapIgnore], consistent with get-only members. In a CREATE map init-only
                // is writable via the object initializer, so this is update-into-specific.
                var updInitOnly = new HashSet<string>(System.StringComparer.Ordinal);
                for (INamedTypeSymbol? t = updTgt; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                {
                    foreach (var tm in t.GetMembers())
                    {
                        if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: true })
                            updInitOnly.Add(p.Name);
                    }
                }
                if (updInitOnly.Count > 0)
                {
                    var keptUpd = new List<MemberMap>(updMembers.Count);
                    foreach (var mm in updMembers)
                    {
                        if (updInitOnly.Contains(mm.TargetName) && !updIgnores.Contains(mm.TargetName))
                        {
                            // A matching source value would be lost — loud, actionable (suggests [MapIgnore]).
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.ReadOnlyDestinationMember, methodLocation, mm.TargetName));
                            continue; // cannot assign an init-only property post-construction
                        }
                        keptUpd.Add(mm);
                    }
                    updMembers = keptUpd;
                }

                // [MapCollectionKey]: turn a List<T> member's whole-collection replacement into a key-based
                // upsert (merge in place). Applied before DWARF065 so an upserted collection is not also flagged
                // as "replaced".
                ApplyCollectionKeyUpserts(method, updSrc, updTgt, comp, methodLocation, diagnostics, updMembers);

                // Item 13 (DWARF065): update-into maps a nested object member by REPLACING dest's existing
                // instance with a freshly-mapped one (the auto-nested __DwarfMap_Obj_* converter constructs a
                // new object), NOT by recursively merging into it. Callers expecting a deep merge / preserved
                // identity are warned. Info; only for synthesized object sub-maps (collections/dicts are
                // expected to be rebuilt, and a direct scalar copy preserves nothing to merge).
                foreach (var mm in updMembers)
                {
                    if (mm.ConverterMethod is { } cmName
                        && GeneratedNames.IsObjectMap(cmName))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.UpdateIntoNestedReplaced, methodLocation, mm.TargetName));
                    }
                }

                var updBefore = new List<string>();
                foreach (var h in beforeHookDefs)
                    if (HasImplicitConversion(comp, updSrc, h.ParamType))
                        updBefore.Add(h.Name);

                var updAfter = new List<HookCall>();
                foreach (var h in afterHookDefs)
                {
                    bool applies;
                    bool takesSource;
                    if (h.P1 is null)
                    {
                        applies = HasImplicitConversion(comp, updTgt, h.P0);
                        takesSource = false;
                    }
                    else
                    {
                        applies = HasImplicitConversion(comp, updSrc, h.P0) &&
                                  HasImplicitConversion(comp, updTgt, h.P1);
                        takesSource = true;
                    }

                    if (!applies) continue;
                    // Target is a reference type → by-value is fine (mutations propagate); ref optional.
                    updAfter.Add(new HookCall(h.Name, takesSource, h.TargetRefKind == RefKind.Ref));
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
            // A trailing CancellationToken is accepted (and required to be honoured): without it, nothing the
            // consumer passes to `WithCancellation` can ever reach this iterator, so the stream is uncancellable.
            // The generated half must match the user's partial signature exactly, so the token only exists when
            // the user declared it.
            var asCtParam = method.Parameters.Length == 2 && IsCancellationToken(method.Parameters[1].Type)
                ? method.Parameters[1].Name
                : null;
            if ((method.Parameters.Length == 1 || asCtParam is not null) && !method.ReturnsVoid
                                              && TryGetAsyncEnumerableElement(method.Parameters[0].Type,
                                                  out var asSrcElem)
                                              && TryGetAsyncEnumerableElement(method.ReturnType, out var asDstElem))
            {
                var asComp = ctx.SemanticModel.Compilation;
                var asAutoNest = ReadMethodAutoNest(method, classAutoNest);
                if (!TryResolveConversion(asComp, asSrcElem, asDstElem, null, allMethods, mapperMethods,
                        enumStrategy, synthesized, nullStrategy, methodLocation, method.Name, diagnostics,
                        out var asConv, out var asNull, out var asNeedsCtx, asAutoNest, nestedRegistry))
                    continue; // element pair not mappable → diagnostic already added

                var asElemMember = new MemberMap(
                    "", "", asConv,
                    asNull, asNeedsCtx);

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    false,
                    EquatableArray.From(new[] { asElemMember }),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    false,
                    "",
                    IsAsyncStreamMap: true,
                    AsyncCancellationParam: asCtParam,
                    ParameterIsPublicType: IsEffectivelyPublic(method.Parameters[0].Type),
                    ReturnIsPublicType: IsEffectivelyPublic(method.ReturnType)));
                continue;
            }

            // A construction mapper has the source as parameter 0 and may declare ADDITIONAL parameters
            // (Phase 5) used as extra named value sources — so allow >= 1, not exactly 1.
            if (method.ReturnsVoid || method.Parameters.Length < 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation,
                    method.Name));
                continue;
            }

            if (method.Parameters.Length == 1
                && IsQueryable(method.ReturnType, out var projTarget)
                && IsQueryable(method.Parameters[0].Type, out var projSource)
                && projTarget is INamedTypeSymbol projTargetNamed)
            {
                var projIgnores = new HashSet<string>(classIgnores);
                foreach (var i in ReadIgnores(method)) projIgnores.Add(i);

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
                        EquatableArray.From(Array.Empty<MemberMap>()),
                        EquatableArray.From(Array.Empty<string>()),
                        EquatableArray.From(Array.Empty<HookCall>()),
                        true,
                        projTargetNamed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ProjectionMembers: EquatableArray.From(Array.Empty<ProjectionMemberMap>())));
                    continue;
                }

                // VF5: Hooks (before/after) cannot live inside an expression tree.
                // Detect any applicable hook for this projection's source/target and emit DWARF028
                // instead of silently dropping it. Per thesis: loud, never silent.
                var hasApplicableHook = false;
                foreach (var h in beforeHookDefs)
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.ParamType))
                    {
                        hasApplicableHook = true;
                        break;
                    }

                if (!hasApplicableHook)
                    foreach (var h in afterHookDefs)
                    {
                        var applies = h.P1 is null
                            ? HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P0)
                            : HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.P0)
                              && HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P1);
                        if (applies)
                        {
                            hasApplicableHook = true;
                            break;
                        }
                    }

                if (hasApplicableHook)
                    EmitDWARF028(diagnostics, methodLocation, method.Name,
                        "hooks (BeforeMap/AfterMap) are not supported in IQueryable projection (expression trees cannot contain hook calls); move hooks to a runtime mapper or remove them");

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
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    true,
                    projTargetNamed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ProjectionMembers: EquatableArray.From(projMembers.ToArray())));
                continue;
            }

            if (method.ReturnType is not INamedTypeSymbol targetType)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation,
                    method.Name));
                continue;
            }

            var sourceType = method.Parameters[0].Type;

            // Phase 5: parameters after the source are extra named value sources, matched to destination
            // members by name (precedence: explicit > extra parameter > by-name). Pre-format their
            // signature fragments ("global::Type name") for emission.
            var extraParams = new List<(string Name, ITypeSymbol Type)>();
            var extraParamSig = new List<string>();
            for (var pi = 1; pi < method.Parameters.Length; pi++)
            {
                var ep = method.Parameters[pi];
                extraParams.Add((ep.Name, ep.Type));
                extraParamSig.Add(ep.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " " + ep.Name);
            }

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
                var resolvedArms =
                    new List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)>();
                var seenSrcTypes = new HashSet<string>(StringComparer.Ordinal);

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
                    var resolved = TryResolveConversion(
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
                        true, isSetNullMode, implicitConversions);

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
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                        derivedBefore.Add(h.Name);
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
                    derivedAfter.Add(new HookCall(h.Name, takesSource, targetIsRef));
                }

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    sourceType.IsReferenceType,
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    EquatableArray.From(derivedBefore),
                    EquatableArray.From(derivedAfter),
                    false,
                    "",
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    true,
                    targetType.IsReferenceType,
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
                out _, out _, out _);
            var isDictReturn = !isCollReturn && DictionaryConverter.TryResolve(targetType, targetType,
                out _, out _, out _, out _, out _);

            if (isCollReturn || isDictReturn)
            {
                var tlResolved = TryResolveConversion(
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
                    // Element conversion failed (diagnostic already reported). Skip this method.
                    continue;

                var tlMember = new MemberMap(
                    "",
                    "", // sentinel: emit helper(param) not helper(param.Member)
                    tlConverter,
                    ConverterNeedsDepthCtx: tlNeedsCtx);

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    sourceType.IsReferenceType,
                    EquatableArray.From(new[] { tlMember }),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    IsProjection: false,
                    ElementTargetTypeFullName: "",
                    ConstructorArguments: EquatableArray.From(Array.Empty<MemberMap>()),
                    IsPartial: true,
                    ReturnIsReferenceType: targetType.IsReferenceType,
                    IsTopLevelCollectionConversion: true,
                    ParameterIsPublicType: IsEffectivelyPublic(sourceType),
                    ReturnIsPublicType: IsEffectivelyPublic(targetType)));
                continue;
            }
            // ── End Fix 1 ────────────────────────────────────────────────────────────────

            // Choose construction strategy for the target type.
            var ctor = ConstructorSelector.Select(ctx.SemanticModel.Compilation, targetType, diagnostics,
                methodLocation, out var objInitOnly, allowNonPublic);
            if (ctor is null) continue;

            var ignores = new HashSet<string>(classIgnores);
            foreach (var i in ReadIgnores(method)) ignores.Add(i);

            var explicitMaps = ReadExplicitMaps(method);
            // [ReverseMap]: if this method is the inverse of a forward [ReverseMap] method, inherit the
            // inverted simple renames (A→B becomes B→A). Non-invertible forward config → DWARF051.
            var reverseAdds = CollectReverseRenames(classSymbol, method, sourceType, targetType, explicitMaps,
                diagnostics, methodLocation);
            if (reverseAdds.Count > 0) explicitMaps.AddRange(reverseAdds);
            // A forward [ReverseMap] method with no inverse declared → DWARF052.
            if (HasReverseMap(method))
            {
                // The inverse may declare additional (Phase 5) parameters after the source, so match on
                // parameter[0] + return type, not an exact arity of 1.
                var hasInverse = classSymbol.GetMembers().OfType<IMethodSymbol>().Any(m =>
                    !SymbolEqualityComparer.Default.Equals(m, method) && m.Parameters.Length >= 1
                                                                      && SymbolEqualityComparer.Default.Equals(
                                                                          m.Parameters[0].Type, targetType)
                                                                      && SymbolEqualityComparer.Default.Equals(
                                                                          m.ReturnType, sourceType));
                if (!hasInverse)
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReverseMapTargetMissing, methodLocation,
                        $"[ReverseMap] on '{method.Name}' has no inverse mapping method '{sourceType.ToDisplayString()} X({targetType.ToDisplayString()})'"));
            }

            var mapPropExtras = ReadMapPropertyExtras(method);
            var stringFormats = ReadStringFormats(method);
            var mapValues = ReadMapValues(method);

            // Pair-scoped class-level config ([MapProperty<S,T>] / [MapIgnore<T>]) also applies to a DECLARED
            // partial method for the same pair: method-level config wins, the pair-scoped attrs fill the gaps,
            // and — crucially — MatchPairProps marks them consumed so DWARF056 does not fire its "matches no
            // mapped pair; add [GenerateMap]" advice for a pair this partial method already maps.
            var (pairExplicit, pairExtras) = MatchPairProps(pairProps, sourceType, targetType);
            var methodExplicitTargets = new HashSet<string>(explicitMaps.Select(m => m.Target), StringComparer.Ordinal);
            foreach (var pe in pairExplicit)
                if (methodExplicitTargets.Add(pe.Target))
                    explicitMaps.Add(pe);
            var methodExtraTargets = new HashSet<string>(mapPropExtras.Select(e => e.Target), StringComparer.Ordinal);
            foreach (var pe in pairExtras)
                if (methodExtraTargets.Add(pe.Target))
                    mapPropExtras.Add(pe);
            foreach (var pim in MatchPairIgnores(pairIgnores, targetType)) ignores.Add(pim);
            var methodValueTargets = new HashSet<string>(mapValues.Select(v => v.Target), StringComparer.Ordinal);
            foreach (var pv in MatchPairValues(pairValues, targetType))
                if (methodValueTargets.Add(pv.Target))
                    mapValues.Add(pv);

            var flattenRoots = ReadFlattenRoots(method);
            var reinterpretMembers = ReadReinterpretMembers(method);

            // ── Plan 20 / 22: [FlattenGraph] ─────────────────────────────────
            // Read and resolve [FlattenGraph] directives BEFORE ResolveMembers so that
            // target collection members can be added to ignores and skipped from normal mapping.
            // flattenGraphRawEarly was already read above (for hetero-detection); reuse it.
            var flattenGraphRaw = flattenGraphRawEarly;
            var flattenGraphConsumed = new HashSet<string>(StringComparer.Ordinal);
            List<FlattenGraphDirective> resolvedFgDirectives;
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
                resolvedFgDirectives = new List<FlattenGraphDirective>();
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
                ctorArgs = Array.Empty<MemberMap>();
                consumedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                requiredMustInitialize = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(ctor, sourceType, ctx.SemanticModel.Compilation,
                        methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                        enumStrategy, synthesized, nullStrategy, methodAutoNest, nestedRegistry, out ctorArgs,
                        out consumedParams,
                        nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNullMode,
                        implicitConversions))
                    // At least one parameter was unmappable → DWARF024 already reported; skip emit.
                    continue;

                // Compute which consumed-param members are `required` and whose ctor lacks [SetsRequiredMembers].
                // Those must still be emitted in the object initializer to satisfy the C# `required` rule.
                requiredMustInitialize = ComputeRequiredMustInitialize(ctor, targetType, consumedParams);
            }

            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                enumStrategy, synthesized, nullStrategy, flattenRoots, reinterpretMembers,
                consumedParams, requiredMustInitialize, methodAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNullMode, implicitConversions,
                mapValues, valueProviders, extraParams,
                nameConvention, mapPropExtras, skipNullSrc, allowNonPublic, explicitOnly, ignoreObsolete,
                stringFormats);

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
                var ignoreSources = new HashSet<string>(classIgnoreSources, StringComparer.Ordinal);
                foreach (var s in ReadIgnoreSources(method))
                    ignoreSources.Add(s);
                // IgnoreObsoleteMembers, source side: an obsolete source member need not be consumed — you are
                // retiring it, not required to keep reading it — so it does not surface DWARF039.
                if (ignoreObsolete)
                    foreach (var s in ObsoleteMemberNames(sourceType))
                        ignoreSources.Add(s);

                var consumed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in members)
                    AddConsumed(consumed, m.SourceName);
                foreach (var m in ctorArgs)
                    AddConsumed(consumed, m.SourceName);

                foreach (var (name, _) in ReadableMembers(sourceType))
                    if (!consumed.Contains(name) && !ignoreSources.Contains(name))
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.UnconsumedSourceMember, methodLocation, name));
            }

            var applicableBefore = new List<string>();
            foreach (var h in beforeHookDefs)
                if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                    applicableBefore.Add(h.Name);

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

                if (!applies) continue;

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

                applicableAfter.Add(new HookCall(h.Name, takesSource, targetIsRef));
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
                FlattenGraphDirectives: EquatableArray.From(resolvedFgDirectives.ToArray()),
                ExtraParameters: EquatableArray.From(extraParamSig.ToArray()),
                ParameterIsPublicType: IsEffectivelyPublic(sourceType),
                ReturnIsPublicType: IsEffectivelyPublic(targetType)));
            publicMethodLocs[methods.Count - 1] = methodLocation;
        }

        // ── [GenerateMap<TSrc, TTgt>] — low-ceremony attribute-declared mappers ──────
        // For each [GenerateMap<S,T>] on the mapper class, synthesize a public `T Map(S)` overload
        // (EmitAsNonPartial → emitted as a full method, not a partial impl) with the SAME completeness
        // gate, conversions, nested/collection handling, constructor mapping, and hooks as a declared
        // partial mapper. Source/target types stay plain POCOs (no attributes on them) — migrating from
        // e.g. AutoMapper's CreateMap<A,B>() is a near-mechanical 1:1 replace with [GenerateMap<A,B>].
        var genComp = ctx.SemanticModel.Compilation;
        var genLoc = LocationInfo.From(classSyntax.Identifier.GetLocation());

        // Collect every (source, target) pair to emit: the [GenerateMap<S,T>] attributes, then — for each
        // [GenerateWrapperMap(typeof(W<>))] — the closed wrapper instantiation W<S> -> W<T> per declared pair
        // (item 20). Open generics are never emitted; only the closed instantiations actually declared.
        var genPairs = new List<(ITypeSymbol Src, INamedTypeSymbol Tgt)>();
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (attr.AttributeClass is { Name: KnownNames.GenerateMap } ac
                && ac.TypeArguments.Length == 2
                && ac.ContainingNamespace?.ToDisplayString() == KnownNames.Ns
                && ac.TypeArguments[1] is INamedTypeSymbol gt)
                genPairs.Add((ac.TypeArguments[0], gt));
        }
        ExpandWrapperMaps(classSymbol, genComp, genPairs, diagnostics, genLoc);

        foreach (var (genSrc, genTgt) in genPairs)
        {
            // Pair-scoped [MapProperty<S,T>] / [MapIgnore<T>] config for this declared pair.
            var (genExplicit, genExtras) = MatchPairProps(pairProps, genSrc, genTgt);
            var genIgnores = new HashSet<string>(classIgnores);
            foreach (var im in MatchPairIgnores(pairIgnores, genTgt)) genIgnores.Add(im);

            // Top-level collection/dictionary [GenerateMap<Coll, Coll>]: route through the collection/dict
            // converter (as a declared partial method does, see "Fix 1" above) instead of object-mapping the
            // target's members — which would e.g. flag List<T>.Capacity via DWARF001. The source may be ANY
            // IEnumerable<T> (custom user collections like a ConcurrentList<T> included), matching the
            // member-level collection handling.
            var genIsColl = CollectionConverter.TryResolve(genTgt, genTgt, out _, out _, out _, false);
            var genIsDict = !genIsColl && DictionaryConverter.TryResolve(genTgt, genTgt, out _, out _, out _, out _, out _);
            if (genIsColl || genIsDict)
            {
                bool gResolved = TryResolveConversion(
                    genComp, genSrc, genTgt, null, allMethods, mapperMethods, enumStrategy, synthesized,
                    nullStrategy, genLoc, "Map", diagnostics, out var gConv, out _, out var gNeedsCtx,
                    classAutoNest, nestedRegistry, nullCollections == NullCollectionsBehavior.AsNull,
                    isPreserveMode, isSetNull: isSetNullMode, implicitConversions: implicitConversions);

                if (!gResolved || gConv is null)
                    continue; // element/shape diagnostic already reported by the recursive call

                var gMember = new MemberMap(
                    TargetName: "",
                    SourceName: "", // sentinel: emit helper(param), not helper(param.Member)
                    ConverterMethod: gConv,
                    ConverterNeedsDepthCtx: gNeedsCtx);

                methods.Add(new MapMethodModel(
                    "Map",
                    "public",
                    genTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    genSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "src",
                    genSrc.IsReferenceType,
                    EquatableArray.From(new[] { gMember }),
                    EquatableArray.From(System.Array.Empty<string>()),
                    EquatableArray.From(System.Array.Empty<HookCall>()),
                    IsProjection: false,
                    ElementTargetTypeFullName: "",
                    ConstructorArguments: EquatableArray.From(System.Array.Empty<MemberMap>()),
                    IsPartial: true,
                    ReturnIsReferenceType: genTgt.IsReferenceType,
                    IsTopLevelCollectionConversion: true,
                    EmitAsNonPartial: true,
                    ParameterIsPublicType: IsEffectivelyPublic(genSrc),
                    ReturnIsPublicType: IsEffectivelyPublic(genTgt)));
                publicMethodLocs[methods.Count - 1] = genLoc;
                continue;
            }

            // Pair-scoped [MapConstructor<S,T>(factory)] override: delegate construction to a user factory
            // method and only populate settable members afterward (AutoMapper ConstructUsing semantics).
            string? genFactory = null;
            foreach (var pc in pairConstructors)
            {
                if (!SymbolEqualityComparer.Default.Equals(pc.Source, genSrc)
                    || !SymbolEqualityComparer.Default.Equals(pc.Target, genTgt))
                    continue;
                pc.Consumed = true;
                var factory = allMethods.FirstOrDefault(m =>
                    string.Equals(m.Name, pc.Method, StringComparison.Ordinal)
                    && HasImplicitConversion(genComp, genSrc, m.ParamType)
                    && HasImplicitConversion(genComp, m.ReturnType, genTgt));
                if (factory.Name is null)
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConstructorInvalid, pc.Loc,
                        $"[MapConstructor<{genSrc.ToDisplayString()}, {genTgt.ToDisplayString()}>(\"{pc.Method}\")] factory was not found or has an incompatible signature (it must take the source type '{genSrc.ToDisplayString()}' and return the destination type '{genTgt.ToDisplayString()}')"));
                else
                    genFactory = factory.Name;
                break;
            }

            MemberMap[] genCtorArgs;
            HashSet<string> genConsumed;
            HashSet<string> genRequiredInit;
            if (genFactory is not null)
            {
                // Factory builds the object; only settable members are assigned afterward, so init-only /
                // required members are excluded (the factory owns them) and there are no ctor args.
                genCtorArgs = Array.Empty<MemberMap>();
                genConsumed = CollectFactoryExcludedMembers(genTgt);
                genRequiredInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (ConstructorSelector.Select(ctx.SemanticModel.Compilation, genTgt, diagnostics, genLoc,
                         out var genObjInitOnly, allowNonPublic) is not { } genCtor)
            {
                continue;
            }
            else if (genObjInitOnly)
            {
                genCtorArgs = Array.Empty<MemberMap>();
                genConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                genRequiredInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(genCtor, genSrc, genComp, genLoc, diagnostics,
                        caseInsensitive, genExplicit, allMethods, mapperMethods, enumStrategy, synthesized,
                        nullStrategy, classAutoNest, nestedRegistry, out genCtorArgs, out genConsumed,
                        nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNullMode,
                        implicitConversions))
                    continue;
                genRequiredInit = ComputeRequiredMustInitialize(genCtor, genTgt, genConsumed);
            }

            var genMembers = ResolveMembers(
                genSrc, genTgt, genIgnores, genComp, genLoc, diagnostics,
                caseInsensitive, genExplicit, allMethods, mapperMethods, enumStrategy, synthesized,
                nullStrategy, Array.Empty<string>(), new List<string>(),
                genConsumed, genRequiredInit, classAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNullMode, implicitConversions,
                MatchPairValues(pairValues, genTgt), valueProviders,
                mapPropertyExtras: genExtras, skipNullSourceMembers: skipNullSrc, allowNonPublic: allowNonPublic,
                explicitOnly: explicitOnly, ignoreObsolete: ignoreObsolete);

            var genBefore = new List<string>();
            foreach (var h in beforeHookDefs)
                if (HasImplicitConversion(genComp, genSrc, h.ParamType))
                    genBefore.Add(h.Name);
            var genAfter = new List<HookCall>();
            foreach (var h in afterHookDefs)
            {
                bool applies;
                bool takesSource;
                if (h.P1 is null)
                {
                    applies = HasImplicitConversion(genComp, genTgt, h.P0);
                    takesSource = false;
                }
                else
                {
                    applies = HasImplicitConversion(genComp, genSrc, h.P0) &&
                              HasImplicitConversion(genComp, genTgt, h.P1);
                    takesSource = true;
                }

                if (!applies) continue;
                var tIsRef = h.TargetRefKind == RefKind.Ref;
                if (genTgt.IsValueType && !tIsRef) continue;
                genAfter.Add(new HookCall(h.Name, takesSource, tIsRef));
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
                false,
                "",
                EquatableArray.From(genCtorArgs),
                true,
                genTgt.IsReferenceType,
                EmitAsNonPartial: true,
                ParameterIsPublicType: IsEffectivelyPublic(genSrc),
                ReturnIsPublicType: IsEffectivelyPublic(genTgt),
                FactoryMethod: genFactory));
            publicMethodLocs[methods.Count - 1] = genLoc;
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

            // Pair-scoped member config for this synthesized pair (empty when none declared on the class), so a
            // [MapProperty<S,T>] rename applies even when S -> T is mapped as a nested/collection element.
            var (nestedExplicit, nestedExtras) = MatchPairProps(pairProps, nestedSrc, nestedTgt);
            var nestedIgnores = MatchPairIgnores(pairIgnores, nestedTgt);

            // Inform the registry that we are now building this pair's body,
            // so subsequent GetOrReserve calls record edges in the dependency graph.
            nestedRegistry.SetCurrentPair(nestedName);

            // C3: use the first declared method's location as the diagnostic anchor for
            // nested diagnostics (not null, so DWARF030 has a non-null location).
            // ISSUE-012: the loop that used to sit here scanned `methods` for the first partial one and then
            // `break`-ed out of a comment-only body, discarding the index and never assigning nestedLocation —
            // dead code that only implied a location was being computed. The method model does not carry a
            // LocationInfo, so null is the actual contract here (DWARF030 only requires non-null at its own
            // emission site).
            LocationInfo? nestedLocation = null;

            // Choose construction strategy for the nested target type.
            var nestedCtor = ConstructorSelector.Select(ctx.SemanticModel.Compilation, nestedTgt, diagnostics,
                nestedLocation, out var nestedObjInitOnly, allowNonPublic);
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
                nestedCtorArgs = Array.Empty<MemberMap>();
                nestedConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                nestedRequiredMustInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // C1: use the per-pair autoNest value (pairAutoNest), NOT classAutoNest.
                if (!ResolveConstructorArguments(nestedCtor, nestedSrc, ctx.SemanticModel.Compilation,
                        nestedLocation, diagnostics, caseInsensitive, nestedExplicit,
                        allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                        pairAutoNest, nestedRegistry, out nestedCtorArgs, out nestedConsumed,
                        nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNullMode,
                        implicitConversions))
                {
                    nestedRegistry.ClearCurrentPair();
                    continue;
                }

                nestedRequiredMustInit = ComputeRequiredMustInitialize(nestedCtor, nestedTgt, nestedConsumed);
            }

            // C1: use the per-pair autoNest value (pairAutoNest), NOT classAutoNest.
            var nestedMembers = ResolveMembers(
                nestedSrc, nestedTgt,
                nestedIgnores, // pair-scoped [MapIgnore<T>] (empty when none declared)
                ctx.SemanticModel.Compilation,
                nestedLocation, diagnostics, caseInsensitive,
                nestedExplicit, // pair-scoped [MapProperty<S,T>] (empty when none declared)
                allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                new List<string>(), new List<string>(), // no flatten/reinterpret
                nestedConsumed, nestedRequiredMustInit,
                pairAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull, isPreserveMode, isSetNullMode, implicitConversions,
                MatchPairValues(pairValues, nestedTgt), valueProviders,
                // NOT explicitOnly: this is the auto-synthesized NESTED mapper. Explicit-only guards the
                // TOP-LEVEL trust boundary; reaching a nested pair already required the developer to map that
                // edge explicitly (top-level auto-nest is blocked by DWARF072), so the nested contents map
                // normally. Propagating here would give every nested member DWARF072 — a synthesized mapper has
                // no [MapProperty] to satisfy it — making nested objects unmappable. For a nested trust
                // boundary, declare that pair's own [DwarfMapper(AutoMatchMembers = false)] mapper.
                // ignoreObsolete DOES propagate (unlike explicitOnly): skipping an obsolete nested member just
                // leaves it at its default — safe and consistent — with no "unmappable" hazard.
                mapPropertyExtras: nestedExtras, skipNullSourceMembers: skipNullSrc, allowNonPublic: allowNonPublic,
                ignoreObsolete: ignoreObsolete);

            nestedRegistry.ClearCurrentPair();

            // Hooks ([BeforeMap]/[AfterMap]) bound to THIS pair must also run when the pair is mapped as a
            // nested member or collection element — otherwise a target produced via the private helper silently
            // skips its post-processing (e.g. an AfterMap that rebuilds a dictionary), a data-loss bug.
            // Match by the same implicit-conversion rule the public pairs use (see ~line 835).
            var nestedBefore = new List<string>();
            foreach (var h in beforeHookDefs)
            {
                if (HasImplicitConversion(ctx.SemanticModel.Compilation, nestedSrc, h.ParamType))
                    nestedBefore.Add(h.Name);
            }
            var nestedAfter = new List<HookCall>();
            foreach (var h in afterHookDefs)
            {
                bool applies;
                bool takesSource;
                if (h.P1 is null)
                {
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, nestedTgt, h.P0);
                    takesSource = false;
                }
                else
                {
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, nestedSrc, h.P0)
                        && HasImplicitConversion(ctx.SemanticModel.Compilation, nestedTgt, h.P1);
                    takesSource = true;
                }

                if (!applies) continue;

                var nestedTargetIsRef = h.TargetRefKind == RefKind.Ref;
                // Struct target passed by value would lose the hook's mutations; skip it here (the public /
                // update-into path for the same pair surfaces the AfterMapValueTargetByValue diagnostic).
                if (nestedTgt.IsValueType && !nestedTargetIsRef) continue;

                nestedAfter.Add(new HookCall(h.Name, takesSource, TargetByRef: nestedTargetIsRef));
            }

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
                BeforeHooks: EquatableArray.From(nestedBefore),
                AfterHooks: EquatableArray.From(nestedAfter),
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
                // Only object-mapper pairs (__DwarfMap_Obj_* prefix). Collection helpers
                // (__DwarfMapColl_*) and dict helpers (__DwarfMapDict_*) already receive
                // the preserve treatment via isPreserve=true in CollectionConverter/DictionaryConverter.
                if (GeneratedNames.IsObjectMap(name))
                    nestedRegistry.ForceRecursionCapable(name);
            // Re-run to incorporate the newly forced entries.
            nestedRegistry.ComputeRecursionCapability();
        }

        // Build a set of method names that are recursion-capable (for the public method check).
        var recursionCapableNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (model, name) in pendingNestedModels)
        {
            var isRC = nestedRegistry.IsRecursionCapable(name);
            if (isRC) recursionCapableNames.Add(name);
            // Rebuild the model with the correct IsRecursionCapable flag.
            methods.Add(model with { IsRecursionCapable = isRC });
        }

        // ── DWARF060: same-source / multiple-target signature collision ──────────
        // Two public create-style maps that would emit an identical (name, parameter-type) signature but
        // with different return (target) types overload only by return type — illegal C# (CS0111). The
        // consumer would otherwise see a raw CS0111 inside generated code. Detect, report loudly, and drop
        // the duplicate emission so DWARF060 is the single actionable diagnostic (the build still fails).
        {
            var sigOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            var collisionDrop = new List<int>();
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                // Only public, create-style methods share the `T Map(S)` shape. Update-into / span /
                // async-stream have distinct parameter lists, so a shared source never collides there.
                if (!(m.IsPartial || m.EmitAsNonPartial)) continue;
                if (m.IsUpdateInto || m.IsSpanMap || m.IsAsyncStreamMap) continue;

                var sig = m.MethodName + "(" + m.ParameterTypeFullName + "|"
                          + string.Join(",", m.ExtraParameters) + ")";
                if (!sigOwner.TryGetValue(sig, out var ownerIdx))
                {
                    sigOwner[sig] = i;
                    continue;
                }

                var owner = methods[ownerIdx];
                // Identical (name, params, return) is a duplicate-pair concern, not a return-type clash.
                if (string.Equals(owner.ReturnTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
                    continue;

                var loc = publicMethodLocs.TryGetValue(i, out var l) ? l
                    : publicMethodLocs.TryGetValue(ownerIdx, out var l2) ? l2 : null;
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ConflictingMapSignature, loc,
                    $"Cannot generate two maps named '{m.MethodName}' from source '{m.ParameterTypeFullName}' "
                    + $"to different targets ('{owner.ReturnTypeFullName}' and '{m.ReturnTypeFullName}'): C# cannot "
                    + $"overload by return type. Give one a distinct name with a partial method, e.g. "
                    + $"'public partial {m.ReturnTypeFullName} MapToOther({m.ParameterTypeFullName} source);'."));
                collisionDrop.Add(i);
            }

            // Remove dropped methods (highest index first to keep indices valid).
            for (var k = collisionDrop.Count - 1; k >= 0; k--)
                methods.RemoveAt(collisionDrop[k]);
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
        var declaredNameCount = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;
            declaredNameCount.TryGetValue(m.MethodName, out var prev);
            declaredNameCount[m.MethodName] = prev + 1;
        }

        // Helper: get the graph key for a declared method.
        string DeclKey(MapMethodModel mm)
        {
            return declaredNameCount.TryGetValue(mm.MethodName, out var cnt) && cnt > 1
                ? mm.MethodName + "§" + mm.ParameterTypeFullName
                : mm.MethodName;
        }

        var allCallGraph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Seed with synthesized method edges (already computed in registry, but not accessible here).
        // Re-derive from pending models (synthesized methods always have unique names).
        foreach (var (model, _) in pendingNestedModels)
        {
            var callerName = model.MethodName;
            if (!allCallGraph.ContainsKey(callerName))
                allCallGraph[callerName] = new HashSet<string>(StringComparer.Ordinal);

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
                allCallGraph[callerKey] = new HashSet<string>(StringComparer.Ordinal);

            foreach (var mem in m.Members)
            {
                if (mem.ConverterMethod is null) continue;
                // Resolve the edge target: if the converter is an overloaded declared method,
                // we can't determine which overload without param-type info, so we add edges
                // to ALL overloads of that name.  For non-overloaded names and synthesized
                // names, add the name directly.
                if (declaredNameCount.TryGetValue(mem.ConverterMethod, out var oc) && oc > 1)
                    // Add edges to all OTHER overloads (not the method itself — a converter can't be
                    // a self-call when it was auto-matched to a DIFFERENT overload by parameter type).
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, mem.ConverterMethod, StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, StringComparison.Ordinal))
                            allCallGraph[callerKey].Add(ovKey);
                    }
                else
                    allCallGraph[callerKey].Add(mem.ConverterMethod);
            }

            foreach (var arg in m.ConstructorArguments)
            {
                if (arg.ConverterMethod is null) continue;
                if (declaredNameCount.TryGetValue(arg.ConverterMethod, out var oc) && oc > 1)
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, arg.ConverterMethod, StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, StringComparison.Ordinal))
                            allCallGraph[callerKey].Add(ovKey);
                    }
                else
                    allCallGraph[callerKey].Add(arg.ConverterMethod);
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
                allCallGraph[cand.HelperName] = new HashSet<string>(StringComparer.Ordinal);
            foreach (var em in cand.ElemMethods)
                // Only inject NON-overloaded element methods: a raw overloaded name would be expanded
                // to edges for ALL overloads, manufacturing a false self-cycle (e.g. Map(Person) →
                // List<Addr> helper → Map(Addr) wrongly resolving to Map(Person)). Overloaded
                // self-map-through-collection falls back to the documented None-mode behaviour.
                if (em is not null && !(declaredNameCount.TryGetValue(em, out var oc) && oc > 1))
                    allCallGraph[cand.HelperName].Add(em);
        }

        // For synthesized methods calling overloaded declared methods, also expand edges
        // so DFS can follow the full cycle. If synth-method calls "Map" and there are
        // two overloads "Map§A" and "Map§B", add edges to all variants except self.
        foreach (var callerKey in allCallGraph.Keys.ToList())
        {
            var edges = allCallGraph[callerKey];
            var expandedEdges = new List<string>();
            foreach (var edge in edges)
                if (declaredNameCount.TryGetValue(edge, out var oc) && oc > 1)
                    // Replace simple name with qualified variants (excluding self to avoid false cycles).
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, edge, StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, StringComparison.Ordinal))
                            expandedEdges.Add(ovKey);
                    }
                else
                    expandedEdges.Add(edge);

            edges.Clear();
            foreach (var e in expandedEdges) edges.Add(e);
        }

        // Find which declared methods are on a cycle (can reach themselves in allCallGraph).
        var selfRecursivePublicMethods = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;

            var key = DeclKey(m);
            if (CanReach(allCallGraph, key, key))
            {
                selfRecursivePublicMethods.Add(m.MethodName);
                // Add the companion name so call-sites in synthesized methods can reference it.
                var companionName = GeneratedNames.Depth + m.MethodName;
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
            // Two kinds of element can turn out recursion-capable, and they upgrade DIFFERENTLY:
            //
            //  * a synthesized object-map helper (`__DwarfMap_Obj_…`, what a [GenerateMap<S,T>] pair yields)
            //    gains the (ctx, depth) parameters IN PLACE — it keeps its own name. The collection helper
            //    just has to be re-emitted so it calls it with (elem, ctx, depth + 1).
            //  * a PUBLIC declared method cannot change signature (it is the user's API), so it is routed
            //    through its depth-guarded `__DwarfMap_Depth_<method>` companion instead. Must also be
            //    non-overloaded: an overloaded name can't be safely disambiguated to a single companion.
            bool UpgradeableSynth(string? em) =>
                em is not null && GeneratedNames.IsObjectMap(em) && nestedRegistry.IsRecursionCapable(em);

            bool UpgradeablePublic(string? em) =>
                em is not null && selfRecursivePublicMethods.Contains(em)
                               && !(declaredNameCount.TryGetValue(em, out var oc) && oc > 1);

            bool Upgradeable(string? em) => UpgradeableSynth(em) || UpgradeablePublic(em);

            if (!cand.ElemMethods.Any(Upgradeable))
                continue;

            // Synthesized helper → same name (now 3-param). Public method → its Depth companion.
            cand.ReSynth(name => UpgradeableSynth(name)
                ? name
                : UpgradeablePublic(name)
                    ? GeneratedNames.Depth + name
                    : name);
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
                var companionName = GeneratedNames.Depth + m.MethodName;

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
                    else if (string.Equals(mem.ConverterMethod, m.MethodName, StringComparison.Ordinal))
                    {
                        // Self-call: redirect to companion.
                        newMembers2[mi] = mem with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    }
                    else if (selfRecursivePublicMethods.Contains(mem.ConverterMethod))
                    {
                        // Indirect recursive declared method: redirect to its companion.
                        newMembers2[mi] = mem with
                        {
                            ConverterMethod = GeneratedNames.Depth + mem.ConverterMethod,
                            ConverterNeedsDepthCtx = true
                        };
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
                    else if (string.Equals(arg.ConverterMethod, m.MethodName, StringComparison.Ordinal))
                    {
                        newCtorArgs2[ci] = arg with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    }
                    else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs2[ci] = arg with
                        {
                            ConverterMethod = GeneratedNames.Depth + arg.ConverterMethod,
                            ConverterNeedsDepthCtx = true
                        };
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
                    ConstructorArguments = EquatableArray.From(newCtorArgs2)
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
                    ConstructorArguments = EquatableArray.From(newCtorArgs2)
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
                    var companionName = GeneratedNames.Depth + member.ConverterMethod;
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
                    var companionName = GeneratedNames.Depth + arg.ConverterMethod;
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
                    ConstructorArguments = EquatableArray.From(newCtorArgs)
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
                    methods[i] = m with
                    {
                        IsRecursionCapable = true,
                        MaxDepth = maxDepth,
                        Members = EquatableArray.From(newMembers2),
                        ConstructorArguments = EquatableArray.From(newCtorArgs2)
                    };
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
                    var companionName = GeneratedNames.Depth + arm.ConverterMethod;
                    patchedArms[ai] = arm with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    anyArmNeedsCtx = true;
                }
            }

            if (anyArmNeedsCtx)
                methods[i] = m with
                {
                    IsRecursionCapable = true,
                    MaxDepth = m.IsPartial ? maxDepth : m.MaxDepth,
                    DerivedTypeArms = EquatableArray.From(patchedArms)
                };
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
        var dispatchWrapperByPublicName = new Dictionary<string, string>(
            StringComparer.Ordinal);
        if (isPreserveMode)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.DerivedTypeArms.Count == 0) continue; // not a dispatch method
                if (!m.IsRecursionCapable) continue; // arm converters don't need ctx
                if (!m.IsPartial) continue; // only public declared dispatch methods

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
                            IsPreserveMode = m.IsPartial ? true : m.IsPreserveMode
                        };
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
                        if (ctorArg.ConverterMethod is not null && ctorArg.ConverterNeedsDepthCtx
                                                                && CanReach(allCallGraph, ctorArg.ConverterMethod,
                                                                    outerMethodKey))
                        {
                            var loc = (LocationInfo?)null;
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.CyclicConstructorParameter,
                                loc,
                                ctorArg.TargetName));
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
                        && string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
                        foreach (var ctorArg in m.ConstructorArguments)
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
                if (string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
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
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsRecursionCapable) continue; // only pairs that can re-enter
                if (!m.ParameterIsReferenceType) continue; // value types never form ref cycles
                methods[i] = m with { IsSetNullMode = true };
            }

        // Report DWARF031 if the registry cap was exceeded.
        if (nestedRegistry.CapExceeded)
            // Use a null location — the cap is a generator-level limit, not method-specific.
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.DeepNestingLimit,
                null,
                nestedRegistry.CapTriggerType));

        // CollectRoundTrips must be called before capturing diagnostics so that DWARF020/021 are included.
        var roundTrips = CollectRoundTrips(classSymbol, ctx.SemanticModel.Compilation, diagnostics);

        // DWARF055 (Info): a single mapper resolving a very large number of members. All extraction runs in
        // the syntax transform, so an enormous mapper can add IDE/compile latency. High threshold → only
        // genuine god-mappers trip it; suppressible. Heads-up, never a build break.
        const int LargeMapperMemberThreshold = 300;
        var mappedMemberCount = methods.Sum(m => m.Members.Count + m.ConstructorArguments.Count);
        if (mappedMemberCount > LargeMapperMemberThreshold)
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MapperTooLarge,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                $"mapper '{classSymbol.Name}' resolves {mappedMemberCount} mapped members across its methods " +
                $"(> {LargeMapperMemberThreshold}); a mapper this large can add IDE/compile latency — " +
                "consider splitting it into smaller mappers"));

        // DWARF056: a pair-scoped attribute that matched no mapped pair (top-level or nested) silently does
        // nothing — surface it (usually a typo'd type argument or a missing [GenerateMap]).
        foreach (var pp in pairProps)
            if (!pp.Consumed)
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch, pp.Loc,
                    $"[MapProperty<{pp.Source.ToDisplayString()}, {pp.Target.ToDisplayString()}>(\"{pp.SrcMember}\", \"{pp.TgtMember}\")] matches no mapped pair; add [GenerateMap<{pp.Source.ToDisplayString()}, {pp.Target.ToDisplayString()}>] (or a mapping that nests it), or fix the type arguments"));
        foreach (var pi in pairIgnores)
            if (!pi.Consumed)
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch, pi.Loc,
                    $"[MapIgnore<{pi.Target.ToDisplayString()}>(\"{pi.Member}\")] matches no mapped pair targeting {pi.Target.ToDisplayString()}"));
        foreach (var pv in pairValues)
            if (!pv.Consumed)
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch, pv.Loc,
                    $"[MapValue<{pv.Target.ToDisplayString()}>(\"{pv.Member}\")] matches no mapped pair targeting {pv.Target.ToDisplayString()}"));
        foreach (var pc in pairConstructors)
            if (!pc.Consumed)
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch, pc.Loc,
                    $"[MapConstructor<{pc.Source.ToDisplayString()}, {pc.Target.ToDisplayString()}>(\"{pc.Method}\")] matches no [GenerateMap<{pc.Source.ToDisplayString()}, {pc.Target.ToDisplayString()}>] pair"));

        // A mapper nested inside another type (e.g. inside the service that owns it) must have its generated
        // half re-declared inside that same containing type. Skipped for the co-located ([GenerateMap]) form,
        // whose emitted mapper is a brand-new class rather than the other half of the user's partial.
        var containingTypes = separateEmit
            ? new List<string>()
            : ContainingTypeDeclarations(classSymbol, classSyntax, diagnostics);

        // nameof-reference the MapConfig convention methods so a consumer's IDE0051-as-error build does not flag
        // its own compile-time config as an unused private member. Emitted in a generated static constructor —
        // so only when the class declares no static constructor of its own (that slot must be free), and not for
        // the co-located form (a brand-new emitted class, not the other half of the user's partial).
        var conventionRefs =
            !separateEmit && !classSymbol.StaticConstructors.Any(c => !c.IsImplicitlyDeclared)
                ? mapConfig.ConventionMethodNames.Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList()
                : new List<string>();

        return new MapperClassModel(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString(),
            emitClassName,
            emitAccessibility,
            EquatableArray.From(methods),
            EquatableArray.From(diagnostics),
            EquatableArray.From(synthesized.Values.OrderBy(m => m.Name, StringComparer.Ordinal)),
            EquatableArray.From(roundTrips),
            generateExtensions,
            hasParameterlessCtor,
            EquatableArray.From(containingTypes),
            EquatableArray.From(conventionRefs));
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadOnlyMembers(ITypeSymbol type,
        Compilation? compilation = null, bool allowNonPublic = false)
    {
        var writable = new HashSet<string>(WritableMembers(type, compilation, allowNonPublic).Select(m => m.Name),
            StringComparer.Ordinal);
        return ReadableMembers(type, compilation, allowNonPublic).Where(m => !writable.Contains(m.Name));
    }

    /// <summary>
    ///     Returns the set of member names (case-insensitive) that are <c>required</c> AND satisfied via
    ///     a constructor parameter, but whose constructor does NOT carry
    ///     <c>[SetsRequiredMembers]</c>. These members must also be emitted in the object initializer to
    ///     avoid CS9035.
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

        if (ctorHasSetsRequired) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect required member names from the target type hierarchy.
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var current = (ITypeSymbol)targetType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
            foreach (var member in current.GetMembers())
                switch (member)
                {
                    case IPropertySymbol p when p.IsRequired:
                        required.Add(p.Name);
                        break;
                    case IFieldSymbol f when f.IsRequired:
                        required.Add(f.Name);
                        break;
                }

        // The intersection: consumed params that are also required members.
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in consumedParams)
            if (required.Contains(name))
                result.Add(name);

        return result;
    }

    private static IEnumerable<string> ReadIgnores(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == KnownNames.MapIgnoreFqn)
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)
            .Select(s => s!);
    }

    /// <summary>
    ///     Reads <c>[MapIgnoreSource("Member")]</c> names — the source-side mirror of <see cref="ReadIgnores" />.
    ///     Used to suppress the DWARF039 source-coverage suggestion for specific source members.
    /// </summary>
    private static IEnumerable<string> ReadIgnoreSources(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == KnownNames.MapIgnoreSourceFqn)
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)
            .Select(s => s!);
    }

    private static List<(string Source, string Target, string? Use)> ReadExplicitMaps(ISymbol method)
    {
        var maps = new List<(string Source, string Target, string? Use)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapPropertyFqn) continue;
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string s
                && attr.ConstructorArguments[1].Value is string t)
            {
                string? use = null;
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Use" && na.Value.Value is string u)
                        use = u;

                maps.Add((s, t, use));
            }
        }

        return maps;
    }

    /// <summary>
    ///     Reads <c>[MapValue]</c> annotations. A two-argument form (<c>IsConstant = true</c>) carries a
    ///     constant <c>Value</c>; the one-argument form carries a <c>Use</c> provider-method name. The
    ///     <c>Use</c> named argument is also honoured on the two-argument form (Use wins).
    /// </summary>
    private static List<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>
        ReadMapValues(ISymbol method)
    {
        var result = new List<(string, bool, TypedConstant, string?, string?)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapValueFqn) continue;
            if (attr.ConstructorArguments.Length == 0
                || attr.ConstructorArguments[0].Value is not string target)
                continue;
            string? use = null;
            foreach (var na in attr.NamedArguments)
                if (na.Key == "Use" && na.Value.Value is string u)
                    use = u;

            // Two-arg ctor → constant value in [1]; one-arg ctor → Use-driven.
            var isConstant = attr.ConstructorArguments.Length == 2 && use is null;
            var value = attr.ConstructorArguments.Length == 2 ? attr.ConstructorArguments[1] : default;
            result.Add((target, isConstant, value, use, null));
        }

        return result;
    }

    /// <summary>
    ///     Formats a <c>[MapValue]</c> constant as a C# literal assignable to <paramref name="targetType" />,
    ///     or fails with a reason. Only attribute-legal constants are supported (string, bool, char, numeric,
    ///     enum, null); arrays/typeof and non-assignable values fail (the caller emits DWARF040). Floating/
    ///     decimal targets are cast to the target type so an un-suffixed literal (e.g. <c>1.5</c>) compiles.
    /// </summary>
    /// <summary>Renders a non-failing constant as a C# literal. Callers that can fail on assignability must
    /// validate BEFORE calling (the MapConfig path is pre-validated by the compiler via the generic member type).</summary>
    private static string RenderConstantLiteral(object? value, ITypeSymbol? valueType, ITypeSymbol targetType, Compilation compilation)
    {
        if (value is null) return "null";
        if (valueType is { TypeKind: TypeKind.Enum })
        {
            var enumFqn = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({enumFqn})({SymbolDisplay.FormatPrimitive(value, quoteStrings: false, useHexadecimalNumbers: false)})";
        }
        var formatted = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
        return targetType.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
            ? $"({targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({formatted})"
            : formatted;
    }

    private static bool TryFormatConstant(
        TypedConstant tc, ITypeSymbol targetType, Compilation compilation, out string literal, out string why)
    {
        literal = "";
        why = "";
        if (tc.Kind is TypedConstantKind.Array or TypedConstantKind.Type or TypedConstantKind.Error)
        {
            why =
                $"[MapValue] constant for '{targetType.ToDisplayString()}' must be a string, bool, char, numeric, enum, or null";
            return false;
        }

        if (tc.IsNull)
        {
            var acceptsNull = targetType.IsReferenceType
                              || (targetType is INamedTypeSymbol nt &&
                                  nt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
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
                why =
                    $"[MapValue] enum constant of type '{tc.Type?.ToDisplayString()}' is not assignable to '{targetType.ToDisplayString()}'";
                return false;
            }
            literal = RenderConstantLiteral(tc.Value, tc.Type, targetType, compilation);
            return true;
        }

        // Primitive (string/bool/char/numeric).
        if (tc.Type is not null && !HasImplicitConversion(compilation, tc.Type, targetType))
        {
            why =
                $"[MapValue] constant of type '{tc.Type.ToDisplayString()}' is not assignable to '{targetType.ToDisplayString()}'";
            return false;
        }
        // Floating/decimal targets need an explicit cast — an un-suffixed literal like "1.5" is a double
        // and would not compile when assigned to float/decimal.
        literal = RenderConstantLiteral(tc.Value, tc.Type, targetType, compilation);
        return true;
    }

}
