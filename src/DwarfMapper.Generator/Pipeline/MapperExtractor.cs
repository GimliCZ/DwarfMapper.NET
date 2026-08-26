// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline
{
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

        /// <summary>
        ///     The tail of <c>DWARF090</c>'s message for the two member directives: where the unscoped form DOES
        ///     act, which is what makes its silence element-wise worth saying out loud.
        ///     <para>
        ///         Parametrized rather than baked into the message because it is a per-directive claim and this
        ///         repository verifies claims in both directions. <c>[MapNullSkip]</c>'s method form is REFUSED at
        ///         projection rather than honoured there, so the sentence below would have been false for it — and a
        ///         diagnostic that misstates where a directive works sends the reader to the wrong endpoint.
        ///     </para>
        /// </summary>
        private const string MemberDirectiveElsewhere =
            "The unscoped form is honoured at the create-map, update-into and projection endpoints, which is why " + "its silence here is worth saying out loud.";

        /// <summary>
        ///     The comparer every <c>[MapIgnore]</c> destination-name set uses, declared once.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Ordinal — an ignore name must match the member's casing exactly.</b> It was already ordinal
        ///         at every site (the default comparer for <c>string</c> is, and <c>ResolveMembers</c> /
        ///         <c>ResolveProjectionMembers</c> additionally restate it), but it was ordinal by four separate
        ///         coincidences rather than by one decision. Naming it is what stops the next reader of the set from
        ///         picking a different comparer and being right locally and wrong overall.
        ///     </para>
        ///     <para>
        ///         Hoisted because a consumer got it wrong immediately. <c>DWARF090</c>'s member-existence filter
        ///         was written with <c>OrdinalIgnoreCase</c>, reasoning that a case-differing member is one the
        ///         caller plausibly meant and so is worth reporting. That is backwards: the ignore set is ordinal,
        ///         so <c>[MapIgnore("id")]</c> against a property <c>Id</c> excludes nothing at the create map
        ///         either — there is no element-wise GAP, and reporting one told the caller to switch to a
        ///         pair-scoped form that would not work at all. A diagnostic that decides whether a directive was
        ///         DROPPED has to ask the question with the same comparer the thing that drops it uses.
        ///     </para>
        ///     <para>
        ///         <c>B20</c> and <c>B21</c> are settled (round 22 W1). B20: a name matching nothing is no longer
        ///         inert in silence — it reports <c>DWARF095</c> (see <see cref="JudgeUnscopedIgnores" />). B21:
        ///         the comparer stays ORDINAL even under <c>[DwarfMapper(CaseInsensitive = true)]</c>, because a
        ///         directive names a member exactly — the same rule every other directive name in the model
        ///         follows (<c>[MapProperty]</c> source and target names are ordinal at resolution:
        ///         <c>writableByName</c> and the explicit source lookup in <c>ResolveMembers</c> both are, under
        ///         every option). <c>CaseInsensitive</c> fuzzes AUTO-MATCHING between source and destination
        ///         names, never the binding of a name the caller wrote. What B20 changes is that the mismatch is
        ///         now loud: <c>[MapIgnore("id")]</c> against a property <c>Id</c> reports <c>DWARF095</c>
        ///         instead of silently excluding nothing — pinned in both directions in
        ///         <c>UnscopedIgnoreNoMatchTests</c>.
        ///     </para>
        /// </remarks>
        private static readonly StringComparer IgnoreNameComparer = StringComparer.Ordinal;

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
            {
                return null; // a [DwarfMapper] class — the primary pipeline emits into it directly
            }

            // Generic hosts (DWARF054) and <Host>Mapper name collisions (DWARF057) are reported loudly inside
            // ExtractCore rather than silently skipped here — see the never-silent design tenet.
            return ExtractCore(ctx, true, ct);
        }

        private static MapperClassModel ExtractCore(
            GeneratorAttributeSyntaxContext ctx,
            bool separateEmit,
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
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MapperNotPartial,
                    LocationInfo.From(classSyntax.Identifier.GetLocation()),
                    classSymbol.Name));
            }

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

                return new MapperClassModel(mapperNamespace,
                    emitClassName,
                    emitAccessibility,
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

                    return new MapperClassModel(mapperNamespace,
                        emitClassName,
                        emitAccessibility,
                        EquatableArray.From(new List<MapMethodModel>()),
                        EquatableArray.From(diagnostics),
                        EquatableArray.From(new List<SynthesizedMethod>()),
                        EquatableArray.From(new List<RoundTripPair>()));
                }
            }

            var classIgnores = ReadIgnores(classSymbol).ToList();
            var classIgnoreSources = ReadIgnoreSources(classSymbol).ToList();
            // DWARF095 (B20) bookkeeping: which CLASS-level unscoped ignore names matched a destination member of
            // at least one pair this class maps (see JudgeUnscopedIgnores), and whether the class carries an
            // endpoint whose consumption this walk cannot see — in which case the class-site verdict stands down
            // rather than guess (a false "names nothing" would break a working suppression, B19's exact genre).
            var liveClassIgnores = new HashSet<string>(IgnoreNameComparer);
            var ignorableNamesMemo = new Dictionary<ITypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
            var classIgnoreLivenessBlinded = false;

            // ReadIgnores above accepts the one-argument [MapIgnore("Member")] and drops everything else. What it
            // drops is the no-argument MEMBER form, which named nothing here and left the caller believing they
            // had excluded a member. Checked at the class symbol for the class site; the method loop below does
            // the same for each mapping method.
            ReportMemberFormDirectives(classSymbol,
                "mapper class",
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                diagnostics);

            // B18: the third placement, and the one left silent. DWARF088 was raised off the class symbol and off
            // each mapping method, never off a MEMBER of the mapper — so a [MapProperty] or [MapIgnore] written on
            // the mapper's own property or field was read by nothing and said nothing. Not on the separate-emit
            // path: there the annotated type IS a mapped type and ReadCoLocatedHostDirectives reads its members
            // (A4), which is a different question with a different answer.
            if (!separateEmit)
            {
                ReportDirectivesOnMapperMembers(classSymbol, diagnostics);
            }

            // Assembly-wide default options ([assembly: DwarfMapperDefaults(...)]) layer UNDER the mapper's own
            // options. Every option reader returns the first matching named argument across the attribute list, so
            // appending the assembly-defaults attribute AFTER the class's [DwarfMapper] attribute gives exactly the
            // precedence we want — mapper > assembly defaults > built-in default — with no reader changes. Options
            // not present on DwarfMapperDefaults (MaxDepth, ReferenceHandling, OnCycle, GenerateExtensions) simply
            // never match there and stay per-mapper.
            // The LOOKUP is AssemblyConfiguration's, shared with the [MapTo] registry front door, which had no
            // sight of assembly-level configuration at all until it called the same reader.
            var asmDefaults = AssemblyConfiguration.Defaults(ctx.SemanticModel.Compilation);
            var opts = asmDefaults is null ? ctx.Attributes : ctx.Attributes.Add(asmDefaults);

            var requiredMapping = ReadRequiredMapping(opts); // 0 = Target (default), 1 = Both
            var nameConvention = ReadNameConvention(opts); // 0 = Exact (default), 1 = Flexible
            var caseInsensitive = ReadCaseInsensitive(opts);
            var generateExtensions = ReadGenerateExtensions(opts); // default true (opt-out)
            var registerCollectionShapes = ReadRegisterCollectionShapes(opts); // default true (opt-out)
            var handWrittenProvides = CollectHandWrittenProvides(classSymbol, diagnostics);
            // The convenience facade caches a `new()` mapper singleton, so it can only be emitted for a mapper
            // that has an accessible parameterless constructor (the implicit one counts).
            // For separateEmit the cached facade singleton is `new <Host>Mapper()` — the generated mapper always
            // has an implicit parameterless constructor, regardless of the host type's own constructors.
            var hasParameterlessCtor = separateEmit ||
                                       classSymbol.InstanceConstructors.Any(c =>
                                           !c.IsStatic &&
                                           c.Parameters.Length == 0 &&
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
            var enumPolicy = new EnumPolicy(ReadEnumStrategy(opts), ReadEnumStringSource(opts));
            var nullStrategy = ReadNullStrategy(opts);
            var classAutoNest = ReadAutoNest(opts);
            var explicitOnly = !ReadAutoMatchMembers(opts); // trust-boundary guard (DWARF072)
            var ignoreObsolete = ReadIgnoreObsoleteMembers(opts);
            var skipNullSrc = ReadSkipNullSourceMembers(opts);

            // Pair- and method-scoped overrides of the class-level null-skip policy. AutoMapper's ForAllMembers
            // was configured PER MAP, so a profile mixing patch-merge maps with ordinary ones cannot translate
            // to one class-level boolean — it had to be split across two mapper classes, and that split then
            // synthesized the same nested pair twice with opposite null semantics. See MapNullSkipAttribute.
            //
            // Read ONCE here and handed to ResolveNullSkip at every endpoint, which is the whole fix for D6/D7:
            // this list used to be consulted only by the [GenerateMap] and synthesized-pair paths, so the two
            // documented scopes of one option each reached about half the endpoints and said nothing at the other
            // half. ResolveNullSkip is the single reader; do not add a second one.
            var pairNullSkips = ReadPairNullSkips(classSymbol,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                diagnostics);
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
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.OnCycleIgnoredUnderPreserve,
                    LocationInfo.From(classSyntax.Identifier.GetLocation()),
                    classSymbol.Name));
            }

            var synthesized = new Dictionary<string, SynthesizedMethod>(StringComparer.Ordinal);
            var allMethods = CollectMethods(classSymbol);

            // Converters dedicated to a specific member by Use=, anywhere on this mapper. Collected once at
            // MAPPER scope, not per method: a helper written for one method's member must not be
            // auto-adopted by a different method either, and a per-method set cannot see that.
            var mapperReservedConverters = CollectReservedConverterNames(classSymbol);
            var mapperMethods = CollectMapperMethods(classSymbol);

            // ── [GenerateMap<S,T>] pairs, collected HERE rather than at their emission loop far below ──────────
            // Collect every (source, target) pair to emit: the [GenerateMap<S,T>] attributes, then — for each
            // [GenerateWrapperMap(typeof(W<>))] — the closed wrapper instantiation W<S> -> W<T> per declared pair
            // (item 20). Open generics are never emitted; only the closed instantiations actually declared.
            var genComp = ctx.SemanticModel.Compilation;
            var genLoc = LocationInfo.From(classSyntax.Identifier.GetLocation());
            var genPairs = new List<(ITypeSymbol Src, INamedTypeSymbol Tgt)>();
            foreach (var attr in classSymbol.GetAttributes())
                if (attr.AttributeClass is { Name: KnownNames.GenerateMap } ac && ac.TypeArguments.Length == 2 && ac.ContainingNamespace?.ToDisplayString() == KnownNames.Ns && ac.TypeArguments[1] is INamedTypeSymbol gt)
                {
                    genPairs.Add((ac.TypeArguments[0], gt));
                }

            // Member-level directives written on the CO-LOCATED HOST itself, read before the wrapper expansion
            // appends synthetic pairs: a host member says something about the pairs the host DECLARED, and a
            // wrapper instantiation W<S> -> W<T> has neither the host's members nor a position a stacked
            // directive could bind to.
            var hostDirectives = separateEmit
                ? ReadCoLocatedHostDirectives(classSymbol, genPairs, diagnostics)
                : EmptyHostDirectives;

            ExpandWrapperMaps(classSymbol, genComp, genPairs, diagnostics, genLoc);

            // A [GenerateMap<S,T>] emits a `public T Map(S)` overload that is, to every caller, indistinguishable
            // from a declared partial mapper — so resolution must be able to FIND it. It could not: mapperMethods
            // held partial METHODS only, and a class-level pair is not one.
            //
            // The consequence was silent and specific. Given [GenerateMap<Item, ItemDto>] carrying a
            // [MapConstructor] factory AND [GenerateMap<List<Item>, List<ItemDto>>], the collection pair could not
            // see the element pair, so it synthesized a FRESH element mapper — which constructs ItemDto directly
            // and never calls the factory. One class, one pair of types, two different mappings, no diagnostic:
            // `Map(item)` used the factory and `Map(list)[0]` did not.
            //
            // In the migration that surfaced it the same gap presented as a catch-22 instead of a divergence: the
            // synthesized element mapper failed DWARF024 on an unbound constructor parameter, while binding that
            // parameter on the factory-bearing declared pair was rejected by DWARF008 because there the factory
            // owns construction — "the element pair is simultaneously 'has a factory' and 'must construct itself',
            // and no attribute satisfies both" (Issues/Rount18, ImplementationRecord.txt:8421).
            mapperMethods.AddRange(GeneratedPairCandidates(genPairs, mapperMethods));

            var valueProviders = CollectValueProviders(classSymbol); // parameterless methods for [MapValue(Use=)]
            var (beforeHookDefs, afterHookDefs) = CollectHooks(classSymbol, diagnostics);
            var methods = new List<MapMethodModel>();

            // I17: the index into `diagnostics` at which the method currently being resolved began reporting.
            // Everything from here on belongs to ONE method, which is what makes a completeness refusal
            // ATTRIBUTABLE to it rather than to the class — the same positional attribution I14 built for the
            // projection endpoint (TryScopeMethodRefusal). Reset at the top of every iteration.
            //
            // Deliberately NOT a post-loop pass over recorded ranges, which would have been one insertion
            // instead of six: `publicMethodLocs` below is keyed by INDEX INTO `methods`, so removing a model
            // after the fact would silently re-point every later method's location. The method is never added
            // instead.
            var methodDiagStart = 0;

            // I17's verdict for the method (or [GenerateMap] pair) being resolved: recorded on the model
            // rather than acted on with a `continue`, so `methods` keeps every DECLARED mapping and only
            // emission is affected. Declared once at this scope because the eight decision sites sit in
            // sibling blocks and C# will not let the same name be introduced twice in one method.
            var withheld = false;

            // Best-effort source location per public map method (by index into `methods`), used only by the
            // DWARF060 same-source/multi-target collision pass below. The model itself carries no location.
            var publicMethodLocs = new Dictionary<int, LocationInfo?>();

            // NestedMappingRegistry: local to this Extract call (contains ISymbol — never stored in model).
            var nestedRegistry = new NestedMappingRegistry();

            // Span and async-stream methods do not resolve members themselves — they map the ELEMENT pair through
            // an auto-synthesized mapper, and synthesized mappers do not run the source-coverage gate. So
            // RequiredMapping = Both reported unconsumed source members at every other endpoint and silently
            // nothing at these two. Record the element pairs here and run coverage when the synthesis loop has
            // actually resolved them, which reuses the real resolution instead of duplicating the matching rules.
            var elementPairsOwedCoverage =
                new List<(ITypeSymbol Src, ITypeSymbol Tgt, LocationInfo? Loc, List<string> IgnoreSources)>();

            foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                ct.ThrowIfCancellationRequested();

                if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition)
                {
                    continue;
                }

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
                methodDiagStart = diagnostics.Count;

                // Before any endpoint-specific handling, because the mistake is the same one at all of them: this
                // loop is the single point every partial mapping method passes through, and a check placed inside
                // one endpoint's branch would have refused the directive on a create map and gone on discarding it
                // on the span and stream overloads of the very same mapper.
                ReportMemberFormDirectives(method, "mapping method", methodLocation, diagnostics);

                // ── Zero-alloc span map: void Map(ReadOnlySpan<S>/Span<S> src, Span<D> dst) ──
                // Maps element-wise into a caller-provided destination buffer (no allocation). The
                // destination must be a writable Span<D>; a too-small destination throws (never silent
                // truncation). The element conversion reuses the full resolution pipeline.
                if (method.ReturnsVoid &&
                    method.Parameters.Length == 2 &&
                    TryGetSpanElement(method.Parameters[0].Type, out var spanSrcElem, out _) &&
                    TryGetSpanElement(method.Parameters[1].Type,
                        out var spanDstElem,
                        out var dstIsReadOnly) &&
                    !dstIsReadOnly)
                {
                    var spanComp = ctx.SemanticModel.Compilation;
                    var spanAutoNest = ReadMethodAutoNest(method, classAutoNest);
                    // Before the element-wise gate and before resolution, so a directive read only at another
                    // endpoint is reported whatever else this method turns out to be wrong about.
                    ReportDirectivesNotReadHere(method,
                        spanComp,
                        spanSrcElem,
                        spanDstElem,
                        MapEndpointKind.SpanMap,
                        methodLocation,
                        diagnostics);
                    // Class-site liveness only (method: null): a class-wide ignore naming a member of the ELEMENT
                    // pair is DWARF090's to report, so it must not also read as dead at the class site.
                    JudgeUnscopedIgnores(classIgnores,
                        liveClassIgnores,
                        null,
                        spanDstElem,
                        spanComp,
                        allowNonPublic,
                        methodLocation,
                        diagnostics,
                        ignorableNamesMemo);
                    if (ReportElementWiseDirectiveGaps(method,
                            classSymbol,
                            spanSrcElem,
                            spanDstElem,
                            explicitOnly,
                            spanComp,
                            allowNonPublic,
                            methodLocation,
                            diagnostics))
                    {
                        continue;
                    }

                    if (!TryResolveConversion(spanComp,
                            spanSrcElem,
                            spanDstElem,
                            null,
                            allMethods,
                            mapperMethods,
                            enumPolicy,
                            synthesized,
                            nullStrategy,
                            methodLocation,
                            method.Name,
                            diagnostics,
                            out var spanConv,
                            out var spanNull,
                            out var spanNeedsCtx,
                            spanAutoNest,
                            nestedRegistry,
                            // Reservation is mapper-wide: a converter dedicated by Use=, or a [MapConstructor]
                            // factory, must not be adopted as this element's converter either.
                            reservedConverters: mapperReservedConverters))
                        // Element pair not mappable → diagnostic (e.g. DWARF005) already added.
                    {
                        continue;
                    }

                    if (requiredMapping == 1) // RequiredMappingStrategy.Both
                    {
                        elementPairsOwedCoverage.Add(
                            (spanSrcElem, spanDstElem, methodLocation, ReadIgnoreSources(method).ToList()));
                    }

                    var spanElemMember = new MemberMap(
                        "",
                        "",
                        spanConv,
                        spanNull,
                        spanNeedsCtx);

                    // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                    // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                    // model is still recorded, because the class-level analyses downstream ask what the mapper
                    // DECLARES (see MapMethodModel.Withheld).
                    withheld = TryScopeCompletenessRefusalToItsMethod(
                        diagnostics,
                        methodDiagStart,
                        method.Name,
                        methodLocation);

                    methods.Add(new MapMethodModel(
                        method.Name,
                        AccessibilityText(method.DeclaredAccessibility),
                        method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Name,
                        false,
                        EquatableArray.From(new[]
                        {
                            spanElemMember
                        }),
                        EquatableArray.From(Array.Empty<string>()),
                        EquatableArray.From(Array.Empty<HookCall>()),
                        false,
                        "",
                        IsSpanMap: true,
                        SpanTargetParameterName: method.Parameters[1].Name,
                        // The create and update models carry MaxDepth and these did not, which was an omission
                        // relative to its siblings. Since B33 it is READ: when the element converter carries the
                        // (ctx, depth) tail, EmitElementContext sizes the shared DwarfRefContext from this value,
                        // and a cyclic element under None throws DwarfMappingDepthException at exactly this depth
                        // (pinned in ElementWiseReferenceHandlingRuntimeTests). The MaxDepth OPTION divergence in
                        // DeclaredDivergences.Reasons["MaxDepth"] is still open for the non-ctx-tailed case — a
                        // non-recursive element pair creates no context, so the option changes nothing there.
                        MaxDepth: maxDepth,
                        Withheld: withheld));
                    continue;
                }

                // ── Update-into-existing: void/T Map(S src, T dest) ─────────────────────
                // Maps onto an EXISTING reference-type target instance (no construction; identity kept).
                // Return is void OR the destination type. v1 is None-semantics (no Preserve/SetNull on the
                // update method itself) and reference-type targets only (a struct dest by value can't
                // observe mutations). Resolution reuses ResolveMembers; only the emission differs.
                if (method.Parameters.Length == 2 && method.Parameters[1].Type is INamedTypeSymbol updTgt && method.Parameters[1].Type.IsReferenceType && (method.ReturnsVoid || SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[1].Type)))
                {
                    var updSrc = method.Parameters[0].Type;
                    var comp = ctx.SemanticModel.Compilation;
                    // Before resolution, so a directive read only at another endpoint is reported whatever else
                    // this method turns out to be wrong about. An update-into does NOT reach a sibling create map.
                    ReportDirectivesNotReadHere(method,
                        comp,
                        updSrc,
                        updTgt,
                        MapEndpointKind.UpdateInto,
                        methodLocation,
                        diagnostics);
                    var updIgnores = new HashSet<string>(classIgnores, IgnoreNameComparer);
                    foreach (var ig in ReadIgnores(method)) updIgnores.Add(ig);
                    JudgeUnscopedIgnores(classIgnores,
                        liveClassIgnores,
                        method,
                        updTgt,
                        comp,
                        allowNonPublic,
                        methodLocation,
                        diagnostics,
                        ignorableNamesMemo);
                    var updExplicit = ReadExplicitMaps(method);
                    var updMapValues = ReadMapValues(method);
                    var updMapPropExtras = ReadMapPropertyExtras(method);
                    var updFlatten = ReadFlattenRoots(method);
                    var updReinterpret = ReadReinterpretMembers(method);
                    var updAutoNest = ReadMethodAutoNest(method, classAutoNest);

                    var updMembers = ResolveMembers(
                        updSrc,
                        updTgt,
                        updIgnores,
                        comp,
                        methodLocation,
                        diagnostics,
                        new MapperOptions(
                            CaseInsensitive: caseInsensitive,
                            AutoNest: updAutoNest,
                            // These were hardcoded `false` while the other ResolveMembers call sites passed the
                            // real values. Passing them is consistency, not a demonstrated fix: preserve
                            // threading actually comes from the class-level nested synthesis path, so setting
                            // them back to `false` changes no generated output and no test can catch it. Kept
                            // because a lone hardcoded literal here is a landmine the next person would have to
                            // re-derive.
                            NullAsNull: nullCollections == NullCollectionsBehavior.AsNull,
                            IsPreserve: isPreserveMode,
                            IsSetNull: isSetNullMode,
                            ImplicitConversions: implicitConversions,
                            NameConvention: nameConvention,
                            // Update-into is where patch-merge actually lives, so a method-level [MapNullSkip]
                            // matters most here.
                            SkipNullSourceMembers: ResolveNullSkip(pairNullSkips, method, updSrc, updTgt, skipNullSrc),
                            AllowNonPublic: allowNonPublic,
                            ExplicitOnly: explicitOnly,
                            IgnoreObsolete: ignoreObsolete),
                        updExplicit,
                        allMethods,
                        mapperMethods,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        updFlatten,
                        updReinterpret,
                        null,
                        null,
                        nestedRegistry,
                        updMapValues,
                        valueProviders,
                        mapPropertyExtras: updMapPropExtras,
                        stringFormats: ReadStringFormats(method),
                        mapperReservedConverters: mapperReservedConverters,
                        // Update-into writes into an instance the CALLER already constructed, so there is no
                        // object initializer to omit a member from and `required` cannot be violated here.
                        // Without this, ignoring a required member on an update-into method reported a false
                        // DWARF079 — caught by NonTrivialShapeRuntimeTests, which does exactly that legitimately.
                        requiredMembersAlreadySatisfied: true);

                    // Source-side completeness applies here too. It lived inline in the create-map branch, so
                    // RequiredMapping = Both reported unconsumed source members through .Map and said nothing
                    // through .Update on the SAME mapper. There are no constructor arguments to consider: an
                    // update writes onto an instance that already exists.
                    if (requiredMapping == 1) // RequiredMappingStrategy.Both
                    {
                        EmitSourceCoverage(
                            updSrc,
                            updMembers,
                            null,
                            classIgnoreSources,
                            ReadIgnoreSources(method),
                            ignoreObsolete,
                            comp,
                            allowNonPublic,
                            methodLocation,
                            diagnostics);
                    }

                    // Update-into assigns members post-construction, so init-only targets cannot be written
                    // (they would emit CS8852). Treat them as read-only here: drop them and surface DWARF007
                    // so the user adds [MapIgnore], consistent with get-only members. In a CREATE map init-only
                    // is writable via the object initializer, so this is update-into-specific.
                    var updInitOnly = new HashSet<string>(StringComparer.Ordinal);
                    for (var t = updTgt; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                        foreach (var tm in t.GetMembers())
                            if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: true })
                            {
                                updInitOnly.Add(p.Name);
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
                                    DiagnosticDescriptors.ReadOnlyDestinationMember,
                                    methodLocation,
                                    mm.TargetName));
                                continue; // cannot assign an init-only property post-construction
                            }

                            keptUpd.Add(mm);
                        }

                        updMembers = keptUpd;
                    }

                    // [MapCollectionKey]: turn a List<T> member's whole-collection replacement into a key-based
                    // upsert (merge in place). Applied before DWARF065 so an upserted collection is not also flagged
                    // as "replaced".
                    ApplyCollectionKeyUpserts(method,
                        updSrc,
                        updTgt,
                        comp,
                        allowNonPublic,
                        methodLocation,
                        diagnostics,
                        updMembers);

                    // Item 13 (DWARF065): update-into maps a nested object member by REPLACING dest's existing
                    // instance with a freshly-mapped one (the auto-nested __DwarfMap_Obj_* converter constructs a
                    // new object), NOT by recursively merging into it. Callers expecting a deep merge / preserved
                    // identity are warned. Info; only for synthesized object sub-maps (collections/dicts are
                    // expected to be rebuilt, and a direct scalar copy preserves nothing to merge).
                    foreach (var mm in updMembers)
                        if (mm.ConverterMethod is { } cmName && GeneratedNames.IsObjectMap(cmName))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.UpdateIntoNestedReplaced,
                                methodLocation,
                                mm.TargetName));
                        }

                    var updBefore = new List<string>();
                    foreach (var h in beforeHookDefs)
                        if (HasImplicitConversion(comp, updSrc, h.ParamType))
                        {
                            updBefore.Add(h.Name);
                        }

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

                        if (!applies)
                        {
                            continue;
                        }

                        // Target is a reference type → by-value is fine (mutations propagate); ref optional.
                        updAfter.Add(new HookCall(h.Name, takesSource, h.TargetRefKind == RefKind.Ref));
                    }

                    // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                    // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                    // model is still recorded, because the class-level analyses downstream ask what the mapper
                    // DECLARES (see MapMethodModel.Withheld).
                    withheld = TryScopeCompletenessRefusalToItsMethod(
                        diagnostics,
                        methodDiagStart,
                        method.Name,
                        methodLocation);

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
                        MaxDepth: maxDepth,
                        // Both flags were left at their defaults here while every other endpoint set them. That
                        // was invisible until update-into became ambient-registerable, at which point the
                        // registration gate rejected every merge method for types that are plainly public.
                        ParameterIsPublicType: IsEffectivelyPublic(updSrc),
                        ReturnIsPublicType: IsEffectivelyPublic(updTgt),
                        Withheld: withheld));
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
                if ((method.Parameters.Length == 1 || asCtParam is not null) &&
                    !method.ReturnsVoid &&
                    TryGetAsyncEnumerableElement(method.Parameters[0].Type,
                        out var asSrcElem) &&
                    TryGetAsyncEnumerableElement(method.ReturnType, out var asDstElem))
                {
                    var asComp = ctx.SemanticModel.Compilation;
                    var asAutoNest = ReadMethodAutoNest(method, classAutoNest);
                    // Class-site liveness only, exactly as at the span map (DWARF090 owns the method site).
                    JudgeUnscopedIgnores(classIgnores,
                        liveClassIgnores,
                        null,
                        asDstElem,
                        asComp,
                        allowNonPublic,
                        methodLocation,
                        diagnostics,
                        ignorableNamesMemo);
                    ReportDirectivesNotReadHere(method,
                        asComp,
                        asSrcElem,
                        asDstElem,
                        MapEndpointKind.AsyncStream,
                        methodLocation,
                        diagnostics);
                    if (ReportElementWiseDirectiveGaps(method,
                            classSymbol,
                            asSrcElem,
                            asDstElem,
                            explicitOnly,
                            asComp,
                            allowNonPublic,
                            methodLocation,
                            diagnostics))
                    {
                        continue;
                    }

                    if (!TryResolveConversion(asComp,
                            asSrcElem,
                            asDstElem,
                            null,
                            allMethods,
                            mapperMethods,
                            enumPolicy,
                            synthesized,
                            nullStrategy,
                            methodLocation,
                            method.Name,
                            diagnostics,
                            out var asConv,
                            out var asNull,
                            out var asNeedsCtx,
                            asAutoNest,
                            nestedRegistry,
                            reservedConverters: mapperReservedConverters))
                    {
                        continue; // element pair not mappable → diagnostic already added
                    }

                    if (requiredMapping == 1) // RequiredMappingStrategy.Both
                    {
                        elementPairsOwedCoverage.Add(
                            (asSrcElem, asDstElem, methodLocation, ReadIgnoreSources(method).ToList()));
                    }

                    var asElemMember = new MemberMap(
                        "",
                        "",
                        asConv,
                        asNull,
                        asNeedsCtx);

                    // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                    // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                    // model is still recorded, because the class-level analyses downstream ask what the mapper
                    // DECLARES (see MapMethodModel.Withheld).
                    withheld = TryScopeCompletenessRefusalToItsMethod(
                        diagnostics,
                        methodDiagStart,
                        method.Name,
                        methodLocation);

                    methods.Add(new MapMethodModel(
                        method.Name,
                        AccessibilityText(method.DeclaredAccessibility),
                        method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Name,
                        false,
                        EquatableArray.From(new[]
                        {
                            asElemMember
                        }),
                        EquatableArray.From(Array.Empty<string>()),
                        EquatableArray.From(Array.Empty<HookCall>()),
                        false,
                        "",
                        IsAsyncStreamMap: true,
                        AsyncCancellationParam: asCtParam,
                        ParameterIsPublicType: IsEffectivelyPublic(method.Parameters[0].Type),
                        ReturnIsPublicType: IsEffectivelyPublic(method.ReturnType),
                        MaxDepth: maxDepth,
                        Withheld: withheld));
                    continue;
                }

                // A construction mapper has the source as parameter 0 and may declare ADDITIONAL parameters
                // (Phase 5) used as extra named value sources — so allow >= 1, not exactly 1.
                if (method.ReturnsVoid || method.Parameters.Length < 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                        methodLocation,
                        method.Name));
                    continue;
                }

                if (method.Parameters.Length == 1 && IsQueryable(method.ReturnType, out var projTarget) && IsQueryable(method.Parameters[0].Type, out var projSource) && projTarget is INamedTypeSymbol projTargetNamed)
                {
                    // Everything this projection method reports lands at or after this index, and that is what
                    // makes a refusal ATTRIBUTABLE to one method rather than to the class (TASKS.md I14 — see
                    // TryScopeProjectionRefusalToItsMethod).
                    var projDiagStart = diagnostics.Count;

                    // Before the DWARF028 early exit below, which used to add the method with empty projection
                    // members and continue: a directive dropped here is dropped whether or not reference
                    // handling also refuses the endpoint.
                    ReportDirectivesNotReadHere(method,
                        ctx.SemanticModel.Compilation,
                        projSource,
                        projTargetNamed,
                        MapEndpointKind.Projection,
                        methodLocation,
                        diagnostics);

                    var projIgnores = new HashSet<string>(classIgnores, IgnoreNameComparer);
                    foreach (var i in ReadIgnores(method)) projIgnores.Add(i);
                    JudgeUnscopedIgnores(classIgnores,
                        liveClassIgnores,
                        method,
                        projTargetNamed,
                        ctx.SemanticModel.Compilation,
                        allowNonPublic,
                        methodLocation,
                        diagnostics,
                        ignorableNamesMemo);

                    // Plan 19D: DWARF028 — ReferenceHandling != None is incompatible with projection
                    // (a stateful identity map cannot live inside an expression tree).
                    if (referenceHandling != 0)
                    {
                        EmitDWARF028(diagnostics,
                            methodLocation,
                            method.Name,
                            "reference handling is not supported in projection (stateful identity map cannot live in an expression tree); use ReferenceHandling=None or map at runtime");
                        // The method used to be added with EMPTY projection members "so no further cascades" —
                        // which only ever worked because the DWARF028 above suppressed the whole class, so the
                        // empty model was never emitted. Now that a projection refusal is scoped to its own
                        // method, emitting it would produce `Select(q, __s => new D { })`: a projection that
                        // silently drops every member. Dropped, like every other refused projection.
                        TryScopeProjectionRefusalToItsMethod(diagnostics,
                            projDiagStart,
                            method.Name,
                            methodLocation);
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
                    {
                        foreach (var h in afterHookDefs)
                        {
                            var applies = h.P1 is null
                                ? HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P0)
                                : HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P1);
                            if (applies)
                            {
                                hasApplicableHook = true;
                                break;
                            }
                        }
                    }

                    if (hasApplicableHook)
                    {
                        EmitDWARF028(diagnostics,
                            methodLocation,
                            method.Name,
                            "hooks (BeforeMap/AfterMap) are not supported in IQueryable projection (expression trees cannot contain hook calls); move hooks to a runtime mapper or remove them");
                    }

                    // Per-method [AutoNest] override, matching every other endpoint (C1). Projection ignored
                    // the option entirely before this.
                    var projAutoNest = ReadMethodAutoNest(method, classAutoNest);
                    var projConsumedSources = new HashSet<string>(StringComparer.Ordinal);
                    var projExplicitMaps = ReadExplicitMaps(method);
                    var projMembers = ResolveProjectionMembers(
                        projSource,
                        projTargetNamed,
                        projIgnores,
                        ctx.SemanticModel.Compilation,
                        methodLocation,
                        diagnostics,
                        new MapperOptions(
                            CaseInsensitive: caseInsensitive,
                            AutoNest: projAutoNest,
                            // I19: the FIFTH reader of NullCollections, and the endpoint that never read it.
                            // Same value, same shape as the four .Map call sites — a null source collection
                            // materialises the documented AsEmpty default through .Project too.
                            NullAsNull: nullCollections == NullCollectionsBehavior.AsNull,
                            // Projection has no reference-tracking context: it builds an expression tree, and
                            // Preserve/SetNull are runtime graph behaviours with nothing to thread them onto.
                            // False rather than omitted, because the bundle admits no defaults.
                            IsPreserve: false,
                            IsSetNull: false,
                            // I20: the SIXTH reader of ImplicitConversions, and the endpoint that never read
                            // it. Same value the five .Map call sites get — a lossy cross-category conversion
                            // reports at .Project too, and breaks the build at both endpoints or at neither.
                            ImplicitConversions: implicitConversions,
                            NameConvention: nameConvention,
                            // The FOURTH call site of the one reader, and the last: projection used to pass the
                            // bare class value, which made it the third partial reader of an option written at
                            // four scopes (D6/D7). It now sees the method form and the pair-scoped form like
                            // every other endpoint.
                            //
                            // The refusal it feeds is not new: ResolveProjectionMembers already refuses an
                            // untranslatable null-skip per affected member with DWARF028, which is what
                            // [DwarfMapper(SkipNullSourceMembers = true)] and its assembly-level twin have
                            // always got here. Threading the scoped forms simply lets them reach it.
                            //
                            // This one line was built and reverted once (A6): DWARF028 is an Error, a blocking
                            // error suppresses the class's emission, the partial projection method is left
                            // unimplemented, and SurfaceProbe read the resulting CS8795 as "the compiler
                            // rejected the placement" — so landing it would have raised
                            // NotCompilableCellCeiling rather than closing anything. That was the R4 ordering
                            // defect in the instrument, not a fact about this option, and it is fixed: a
                            // CS8795 behind a blocking DWARF error now reads Refused.
                            SkipNullSourceMembers: ResolveNullSkip(pairNullSkips, method, projSource, projTargetNamed, skipNullSrc),
                            AllowNonPublic: allowNonPublic,
                            ExplicitOnly: explicitOnly,
                            IgnoreObsolete: ignoreObsolete),
                        projExplicitMaps,
                        enumPolicy,
                        referenceHandling,
                        "__s",
                        ReadMapPropertyExtras(method),
                        projConsumedSources,
                        // Both [Flatten] and [MapValue] are threaded now, and they arrive from opposite
                        // directions worth keeping distinct. A flattened leaf is `__s.Root.Leaf`, the navigation
                        // access every query provider translates (D10). A [MapValue] constant becomes a literal
                        // in the SELECT, and the object-initializer argument that makes SkipNullSourceMembers
                        // untranslatable one parameter up does NOT reach it: a constant assignment reads nothing
                        // from the destination, so there is no "current value" it could need. Its Use= form is
                        // the one part a provider cannot take, and that is refused rather than dropped.
                        //
                        // [MapValue] was built, measured and reverted once (A8) for the reason [MapNullSkip] was:
                        // DWARF042 and DWARF041 are Errors, a blocking error suppresses emission, and before R4
                        // the resulting CS8795 read as NotCompilable — so three of the four cells closed by
                        // moving into the population the parity theory judges by nothing. R4 is fixed.
                        ReadFlattenRoots(method),
                        ReadMapValues(method));

                    // Source-side completeness for projection. The resolver already knows which source members it
                    // read, so this needed tracking rather than new analysis — it was simply never asked.
                    if (requiredMapping == 1) // RequiredMappingStrategy.Both
                    {
                        EmitSourceCoverageFromConsumed(
                            projSource,
                            projConsumedSources,
                            classIgnoreSources,
                            ReadIgnoreSources(method),
                            ignoreObsolete,
                            ctx.SemanticModel.Compilation,
                            allowNonPublic,
                            methodLocation,
                            diagnostics);
                    }

                    // A refused projection member means this method has no honest body — half a projection is
                    // silently wrong data, which is worse than none. Drop the METHOD; the mapper's Map methods
                    // are untouched by anything a query provider cannot translate and are still emitted.
                    if (TryScopeProjectionRefusalToItsMethod(diagnostics,
                            projDiagStart,
                            method.Name,
                            methodLocation))
                    {
                        continue;
                    }

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
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                        methodLocation,
                        method.Name));
                    continue;
                }

                var sourceType = method.Parameters[0].Type;

                // The FIFTH call site of the one gate, and the reason it is not named after the create map any
                // more. Every branch above this one is some other endpoint, so this is the create map — and a
                // directive whose home is the UPDATE-INTO ([MapCollectionKey], finding D14) is discarded HERE
                // exactly as the create-map-only trio is discarded there. Before resolution, so the refusal does
                // not depend on what else this method turns out to be wrong about.
                ReportDirectivesNotReadHere(method,
                    ctx.SemanticModel.Compilation,
                    sourceType,
                    targetType,
                    MapEndpointKind.CreateMap,
                    methodLocation,
                    diagnostics);

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

                    foreach (var (derivedSrc, derivedTgt, _) in rawDerivedPairs)
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
                            derivedSrc,
                            derivedTgt,
                            null,
                            // Both lists, not just the mapper-method one: a partial mapping method is an ordinary
                            // one-parameter method too, so it appears in allMethods as well and the auto-adoption
                            // scan would pick it up again from there.
                            ExcludingMethod(allMethods, method.Name, sourceType, targetType),
                            // The dispatching method is not a candidate for its own arms. It matches every arm
                            // by signature — a derived source converts to the declared source type — so
                            // [MapDerivedType<AliasCommand, CommandOverviewDto>] on
                            // `partial CommandOverviewDto ToDto(Command)` resolved to ToDto itself and emitted
                            // `AliasCommand __s => ToDto(__s)`. That is a switch arm calling its own switch:
                            // it compiles, reports nothing, and overflows the stack for every AliasCommand.
                            // Excluded, the arm synthesizes a real AliasCommand -> CommandOverviewDto mapper,
                            // which is what "map this derived type differently" asked for.
                            ExcludingMethod(mapperMethods, method.Name, sourceType, targetType),
                            enumPolicy,
                            synthesized,
                            nullStrategy,
                            methodLocation,
                            srcFqn,
                            diagnostics,
                            out var armConverter,
                            out _,
                            out var armNeedsCtx,
                            methodAutoNest,
                            nestedRegistry,
                            nullCollections == NullCollectionsBehavior.AsNull,
                            isPreserveMode,
                            true,
                            isSetNullMode,
                            implicitConversions,
                            // An arm is not an invitation to reuse a converter dedicated to some other pair.
                            mapperReservedConverters);

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
                        {
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
                            applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
                            takesSource = true;
                        }

                        if (!applies)
                        {
                            continue;
                        }

                        var targetIsRef = h.TargetRefKind == RefKind.Ref;
                        if (targetType.IsValueType && !targetIsRef)
                        {
                            continue;
                        }

                        derivedAfter.Add(new HookCall(h.Name, takesSource, targetIsRef));
                    }

                    // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                    // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                    // model is still recorded, because the class-level analyses downstream ask what the mapper
                    // DECLARES (see MapMethodModel.Withheld).
                    withheld = TryScopeCompletenessRefusalToItsMethod(
                        diagnostics,
                        methodDiagStart,
                        method.Name,
                        methodLocation);

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
                        DerivedTypeArms: EquatableArray.From(armModels),
                        Withheld: withheld));
                    // A dispatch method's arm resolution is not an unscoped-ignore consumer this walk can see,
                    // so the class-site DWARF095 verdict stands down for this class (see the flag's declaration).
                    classIgnoreLivenessBlinded = true;
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
                var isCollReturn = CollectionConverter.TryResolve(targetType,
                    targetType,
                    out _,
                    out _,
                    out _);
                var isDictReturn = !isCollReturn &&
                                   DictionaryConverter.TryResolve(targetType,
                                       targetType,
                                       out _,
                                       out _,
                                       out _,
                                       out _,
                                       out _);

                if (isCollReturn || isDictReturn)
                {
                    var tlResolved = TryResolveConversion(
                        ctx.SemanticModel.Compilation,
                        sourceType,
                        targetType,
                        null,
                        allMethods,
                        // This pair is resolved as a WHOLE, so the method must not be a candidate for its own
                        // conversion — the same self-exclusion the [GenerateMap] collection path needs.
                        ExcludingPair(mapperMethods, sourceType, targetType),
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        methodLocation,
                        method.Name,
                        diagnostics,
                        out var tlConverter,
                        out _,
                        out var tlNeedsCtx,
                        methodAutoNest,
                        nestedRegistry,
                        nullCollections == NullCollectionsBehavior.AsNull,
                        isPreserveMode,
                        isSetNull: isSetNullMode,
                        implicitConversions: implicitConversions,
                        // WITHOUT this the ELEMENT conversion adopts a method dedicated to one pair. Found in a
                        // real consumer: `[MapConstructor<DbCommand, UserCommand>(nameof(CreateUserCommand))]`
                        // plus `partial ICollection<UserCommand> ToUserCommands(List<DbCommand>)` emitted
                        // `result.Add(CreateUserCommand(i))` — the bare factory, without the member assignments
                        // the real element map performs, and that factory ignores its argument. A list of blank
                        // objects, silently. The [GenerateMap] path was fixed for this; the DECLARED-METHOD path
                        // was the same bug at the other door.
                        reservedConverters: mapperReservedConverters);

                    if (!tlResolved || tlConverter is null)
                        // Element conversion failed (diagnostic already reported). Skip this method.
                    {
                        continue;
                    }

                    var tlMember = new MemberMap(
                        "",
                        "", // sentinel: emit helper(param) not helper(param.Member)
                        tlConverter,
                        ConverterNeedsDepthCtx: tlNeedsCtx);

                    // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                    // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                    // model is still recorded, because the class-level analyses downstream ask what the mapper
                    // DECLARES (see MapMethodModel.Withheld).
                    withheld = TryScopeCompletenessRefusalToItsMethod(
                        diagnostics,
                        methodDiagStart,
                        method.Name,
                        methodLocation);

                    methods.Add(new MapMethodModel(
                        method.Name,
                        AccessibilityText(method.DeclaredAccessibility),
                        targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Name,
                        sourceType.IsReferenceType,
                        EquatableArray.From(new[]
                        {
                            tlMember
                        }),
                        EquatableArray.From(Array.Empty<string>()),
                        EquatableArray.From(Array.Empty<HookCall>()),
                        false,
                        "",
                        EquatableArray.From(Array.Empty<MemberMap>()),
                        true,
                        targetType.IsReferenceType,
                        IsTopLevelCollectionConversion: true,
                        ParameterIsPublicType: IsEffectivelyPublic(sourceType),
                        ReturnIsPublicType: IsEffectivelyPublic(targetType),
                        Withheld: withheld));
                    // A top-level collection map's element pair is synthesized (pair-scoped config only), so
                    // this method consumes no unscoped ignore this walk can see — class-site DWARF095 stands
                    // down for the class rather than guess (see the flag's declaration).
                    classIgnoreLivenessBlinded = true;
                    continue;
                }
                // ── End Fix 1 ────────────────────────────────────────────────────────────────

                // Selection must see the FULL explicit-rename set — method-level [MapProperty] PLUS the
                // [ReverseMap]-inherited renames and pair-scoped [MapProperty<S,T>] renames — so a ctor parameter
                // bound ONLY via a rename still counts as satisfiable. Previously reverse/pair renames were merged
                // AFTER Select, so Select saw method-level maps alone and could reject a satisfiable wide ctor,
                // picking a narrower one — or emitting DWARF008 — for a mapping resolution would have completed
                // (ISSUE-016 audit regression). The [GenerateMap] path already passes its full genExplicit to
                // Select; this makes the declared-method path consistent. The golden fingerprint sorts diagnostics
                // by Id, so moving CollectReverseRenames' emission earlier cannot move the manifest.
                var explicitMaps = ReadExplicitMaps(method);
                // [ReverseMap]: inherit the inverted simple renames (A→B becomes B→A). Non-invertible → DWARF051.
                var reverseAdds = CollectReverseRenames(classSymbol,
                    method,
                    sourceType,
                    targetType,
                    explicitMaps,
                    diagnostics,
                    methodLocation);
                if (reverseAdds.Count > 0)
                {
                    explicitMaps.AddRange(reverseAdds);
                }

                // Pair-scoped class-level config ([MapProperty<S,T>]) also applies to a DECLARED partial method for
                // the same pair; method-level config wins, pair-scoped fills the gaps, and MatchPairProps marks them
                // consumed so DWARF056 does not fire for a pair this method already maps.
                var (pairExplicit, pairExtras) = MatchPairProps(pairProps, sourceType, targetType);
                var methodExplicitTargets = new HashSet<string>(explicitMaps.Select(m => m.Target), StringComparer.Ordinal);
                foreach (var pe in pairExplicit)
                    if (methodExplicitTargets.Add(pe.Target))
                    {
                        explicitMaps.Add(pe);
                    }

                // Before constructor selection, so the liveness marking (and a dead method-site name's report)
                // happens even when selection refuses the target.
                JudgeUnscopedIgnores(classIgnores,
                    liveClassIgnores,
                    method,
                    targetType,
                    ctx.SemanticModel.Compilation,
                    allowNonPublic,
                    methodLocation,
                    diagnostics,
                    ignorableNamesMemo);

                // Choose construction strategy for the target type, now with the full rename set visible.
                var ctor = ConstructorSelector.Select(ctx.SemanticModel.Compilation,
                    targetType,
                    diagnostics,
                    methodLocation,
                    out var objInitOnly,
                    allowNonPublic,
                    sourceType,
                    explicitMaps);
                if (ctor is null)
                {
                    continue;
                }

                var ignores = new HashSet<string>(classIgnores, IgnoreNameComparer);
                foreach (var i in ReadIgnores(method)) ignores.Add(i);
                // A forward [ReverseMap] method with no inverse declared → DWARF052.
                if (HasReverseMap(method))
                {
                    // The inverse may declare additional (Phase 5) parameters after the source, so match on
                    // parameter[0] + return type, not an exact arity of 1.
                    var hasInverse = classSymbol.GetMembers().OfType<IMethodSymbol>().Any(m =>
                        !SymbolEqualityComparer.Default.Equals(m, method) &&
                        m.Parameters.Length >= 1 &&
                        SymbolEqualityComparer.Default.Equals(
                            m.Parameters[0].Type,
                            targetType) &&
                        SymbolEqualityComparer.Default.Equals(
                            m.ReturnType,
                            sourceType));
                    if (!hasInverse)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReverseMapTargetMissing,
                            methodLocation,
                            $"[ReverseMap] on '{method.Name}' has no inverse mapping method '{sourceType.ToDisplayString()} X({targetType.ToDisplayString()})'"));
                    }
                }

                var mapPropExtras = ReadMapPropertyExtras(method);
                var stringFormats = ReadStringFormats(method);
                var mapValues = ReadMapValues(method);

                // pairExplicit / pairExtras were computed above (before Select) so the constructor selector could see
                // the pair-scoped renames; pairExtras is merged into the extras set here.
                var methodExtraTargets = new HashSet<string>(mapPropExtras.Select(e => e.Target), StringComparer.Ordinal);
                foreach (var pe in pairExtras)
                    if (methodExtraTargets.Add(pe.Target))
                    {
                        mapPropExtras.Add(pe);
                    }

                foreach (var pim in MatchPairIgnores(pairIgnores, targetType)) ignores.Add(pim);
                var methodValueTargets = new HashSet<string>(mapValues.Select(v => v.Target), StringComparer.Ordinal);
                foreach (var pv in MatchPairValues(pairValues, targetType))
                    if (methodValueTargets.Add(pv.Target))
                    {
                        mapValues.Add(pv);
                    }

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
                        sourceType,
                        targetType,
                        flattenGraphRaw,
                        ctx.SemanticModel.Compilation,
                        methodLocation,
                        diagnostics,
                        allMethods,
                        mapperMethods,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        methodAutoNest,
                        nestedRegistry,
                        nullCollections == NullCollectionsBehavior.AsNull,
                        isPreserveMode,
                        allowNonPublic,
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
                    if (!ResolveConstructorArguments(ctor,
                            sourceType,
                            ctx.SemanticModel.Compilation,
                            methodLocation,
                            diagnostics,
                            caseInsensitive,
                            allowNonPublic,
                            explicitMaps,
                            allMethods,
                            mapperMethods,
                            enumPolicy,
                            synthesized,
                            nullStrategy,
                            methodAutoNest,
                            nestedRegistry,
                            out ctorArgs,
                            out consumedParams,
                            nullCollections == NullCollectionsBehavior.AsNull,
                            isPreserveMode,
                            isSetNullMode,
                            implicitConversions))
                        // At least one parameter was unmappable → DWARF024 already reported; skip emit.
                    {
                        continue;
                    }

                    // Compute which consumed-param members are `required` and whose ctor lacks [SetsRequiredMembers].
                    // Those must still be emitted in the object initializer to satisfy the C# `required` rule.
                    requiredMustInitialize = ComputeRequiredMustInitialize(ctor, targetType, consumedParams);
                }

                var members = ResolveMembers(
                    sourceType,
                    targetType,
                    ignores,
                    ctx.SemanticModel.Compilation,
                    methodLocation,
                    diagnostics,
                    new MapperOptions(
                        CaseInsensitive: caseInsensitive,
                        AutoNest: methodAutoNest,
                        NullAsNull: nullCollections == NullCollectionsBehavior.AsNull,
                        IsPreserve: isPreserveMode,
                        IsSetNull: isSetNullMode,
                        ImplicitConversions: implicitConversions,
                        NameConvention: nameConvention,
                        SkipNullSourceMembers: ResolveNullSkip(pairNullSkips, method, sourceType, targetType, skipNullSrc),
                        AllowNonPublic: allowNonPublic,
                        ExplicitOnly: explicitOnly,
                        IgnoreObsolete: ignoreObsolete),
                    explicitMaps,
                    allMethods,
                    mapperMethods,
                    enumPolicy,
                    synthesized,
                    nullStrategy,
                    flattenRoots,
                    reinterpretMembers,
                    consumedParams,
                    requiredMustInitialize,
                    nestedRegistry,
                    mapValues,
                    valueProviders,
                    extraParams,
                    mapPropExtras,
                    stringFormats,
                    mapperReservedConverters,
                    // NOT gated on objInitOnly: a parameterless constructor can still carry
                    // [SetsRequiredMembers], and it satisfies the required members exactly as a parameterized one
                    // would. Gating here produced a false DWARF079 on that shape.
                    CtorSetsRequiredMembers(ctor));

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
                    EmitSourceCoverage(
                        sourceType,
                        members,
                        ctorArgs,
                        classIgnoreSources,
                        ReadIgnoreSources(method),
                        ignoreObsolete,
                        ctx.SemanticModel.Compilation,
                        allowNonPublic,
                        methodLocation,
                        diagnostics);
                }

                var applicableBefore = new List<string>();
                foreach (var h in beforeHookDefs)
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                    {
                        applicableBefore.Add(h.Name);
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
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
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

                    applicableAfter.Add(new HookCall(h.Name, takesSource, targetIsRef));
                }

                // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                // model is still recorded, because the class-level analyses downstream ask what the mapper
                // DECLARES (see MapMethodModel.Withheld).
                withheld = TryScopeCompletenessRefusalToItsMethod(
                    diagnostics,
                    methodDiagStart,
                    method.Name,
                    methodLocation);

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
                    ReturnIsPublicType: IsEffectivelyPublic(targetType),
                    Withheld: withheld));
                publicMethodLocs[methods.Count - 1] = methodLocation;
            }

            // ── [GenerateMap<TSrc, TTgt>] — low-ceremony attribute-declared mappers ──────
            // For each [GenerateMap<S,T>] on the mapper class, synthesize a public `T Map(S)` overload
            // (EmitAsNonPartial → emitted as a full method, not a partial impl) with the SAME completeness
            // gate, conversions, nested/collection handling, constructor mapping, and hooks as a declared
            // partial mapper. Source/target types stay plain POCOs (no attributes on them) — migrating from
            // e.g. AutoMapper's CreateMap<A,B>() is a near-mechanical 1:1 replace with [GenerateMap<A,B>].
            // genPairs / genComp / genLoc are computed near the top of Extract, because element and member
            // resolution has to be able to SEE these pairs long before this loop emits them.
            // Indexed rather than a foreach because a co-located host's MEMBER directives bind to the pairs
            // POSITIONALLY, exactly as a [MapTo] source member's do to its targets — so which pair this iteration
            // is emitting is part of the config, not just a loop variable.
            for (var genIndex = 0; genIndex < genPairs.Count; genIndex++)
            {
                var (genSrc, genTgt) = genPairs[genIndex];

                // Pair-scoped [MapProperty<S,T>] / [MapIgnore<T>] config for this declared pair.
                var (genExplicit, genExtras) = MatchPairProps(pairProps, genSrc, genTgt);
                var genIgnores = new HashSet<string>(classIgnores, IgnoreNameComparer);
                foreach (var im in MatchPairIgnores(pairIgnores, genTgt)) genIgnores.Add(im);
                // Class-site liveness against this pair's target ([GenerateMap] pairs have no method site).
                JudgeUnscopedIgnores(classIgnores,
                    liveClassIgnores,
                    null,
                    genTgt,
                    ctx.SemanticModel.Compilation,
                    allowNonPublic,
                    genLoc,
                    diagnostics,
                    ignorableNamesMemo);

                // Member-level [MapProperty("SourceMember")] / [MapIgnore] written on the co-located host itself.
                // Layered ON TOP of the pair-scoped config rather than instead of it: the two are different
                // placements of the same intent and a host may reasonably carry both. Empty for every other
                // shape, so nothing below this line behaves differently for a [DwarfMapper] class.
                Dictionary<string, string>? genFormats = null;
                if (hostDirectives.TryGetValue(genIndex, out var hostConfig))
                {
                    genExplicit.AddRange(hostConfig.Explicit);
                    genExtras.AddRange(hostConfig.Extras);
                    foreach (var hm in hostConfig.Ignores) genIgnores.Add(hm);
                    genFormats = hostConfig.StringFormats;
                }

                // Top-level collection/dictionary [GenerateMap<Coll, Coll>]: route through the collection/dict
                // converter (as a declared partial method does, see "Fix 1" above) instead of object-mapping the
                // target's members — which would e.g. flag List<T>.Capacity via DWARF001. The source may be ANY
                // IEnumerable<T> (custom user collections like a ConcurrentList<T> included), matching the
                // member-level collection handling.
                var genIsColl = CollectionConverter.TryResolve(genTgt, genTgt, out _, out _, out _);
                var genIsDict = !genIsColl && DictionaryConverter.TryResolve(genTgt, genTgt, out _, out _, out _, out _, out _);

                // An ENUM target needs the same treatment, and for the same reason: it is a VALUE to convert,
                // not an object to construct. Without this, `[GenerateMap<SrcKind, DstKind>]` emitted
                // `return new DstKind { };` — an empty object initializer over an enum, which compiles, has no
                // members to flag, and silently returns the zero value while discarding the source entirely.
                // Green build, no diagnostic, every mapped value wrong.
                //
                // The conversion machinery was never the problem: the identical pair used as a MEMBER already
                // resolves correctly through the enum converter. Only this declared-pair path constructed
                // instead of converting.
                // Every VALUE-like target, not just enums. The enum case was found first, but the bug class is
                // "a declared top-level pair whose target is a value gets object-mapped instead of converted" —
                // and a follow-up audit caught [GenerateMap<int, long>] emitting `return new long { };` for
                // exactly the same reason. SpecialType covers the primitives, string, decimal, char and bool;
                // TypeKind.Enum covers the rest of the family.
                var genIsValueLike = genTgt.TypeKind == TypeKind.Enum || genTgt.SpecialType != SpecialType.None;

                if (genIsColl || genIsDict || genIsValueLike)
                {
                    var gResolved = TryResolveConversion(
                        genComp,
                        genSrc,
                        genTgt,
                        null,
                        allMethods,
                        // This pair is resolved as a WHOLE, so it must not be a candidate for its own conversion.
                        ExcludingPair(mapperMethods, genSrc, genTgt),
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        genLoc,
                        "Map",
                        diagnostics,
                        out var gConv,
                        out _,
                        out var gNeedsCtx,
                        classAutoNest,
                        nestedRegistry,
                        nullCollections == NullCollectionsBehavior.AsNull,
                        isPreserveMode,
                        isSetNull: isSetNullMode,
                        implicitConversions: implicitConversions,
                        // Without this the ELEMENT conversion for a collection pair can adopt a method
                        // dedicated to one pair — a [MapConstructor] factory over the same types matches by
                        // signature and wins, so the loop constructs each element and assigns nothing.
                        reservedConverters: mapperReservedConverters);

                    if (!gResolved || gConv is null)
                    {
                        continue; // element/shape diagnostic already reported by the recursive call
                    }

                    var gMember = new MemberMap(
                        "",
                        "", // sentinel: emit helper(param), not helper(param.Member)
                        gConv,
                        ConverterNeedsDepthCtx: gNeedsCtx);

                    // I17 STOPS HERE, and the boundary is a measurement rather than a preference. Withholding a
                    // method is only safe while its DECLARATION survives: a partial method is declared by the
                    // CONSUMER, so a sibling that maps a nested member through it still BINDS and the single
                    // CS8795 is the whole cost. A [GenerateMap] pair has no declaration — the generator is the
                    // only source of the symbol — so withholding it made a sibling's `N = Map(o.N)` emit
                    // **CS0103, 'the name Map does not exist'**, in a file the consumer cannot edit: the
                    // EmittedInvalidCode genre, whose ceiling is exactly zero. Measured on a two-pair probe
                    // before this line was written. So the pair keeps the whole-class kill, and the CS8795 it
                    // costs a sibling stays: loud collateral beats generated code that does not compile.
                    withheld = false;

                    methods.Add(new MapMethodModel(
                        "Map",
                        "public",
                        genTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        genSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        "src",
                        genSrc.IsReferenceType,
                        EquatableArray.From(new[]
                        {
                            gMember
                        }),
                        EquatableArray.From(Array.Empty<string>()),
                        EquatableArray.From(Array.Empty<HookCall>()),
                        false,
                        "",
                        EquatableArray.From(Array.Empty<MemberMap>()),
                        true,
                        genTgt.IsReferenceType,
                        IsTopLevelCollectionConversion: true,
                        EmitAsNonPartial: true,
                        ParameterIsPublicType: IsEffectivelyPublic(genSrc),
                        ReturnIsPublicType: IsEffectivelyPublic(genTgt),
                        Withheld: withheld));
                    publicMethodLocs[methods.Count - 1] = genLoc;
                    continue;
                }

                // Pair-scoped [MapConstructor<S,T>(factory)] override: delegate construction to a user factory
                // method and only populate settable members afterward (AutoMapper ConstructUsing semantics).
                string? genFactory = null;
                foreach (var pc in pairConstructors)
                {
                    if (!SymbolEqualityComparer.Default.Equals(pc.Source, genSrc) || !SymbolEqualityComparer.Default.Equals(pc.Target, genTgt))
                    {
                        continue;
                    }

                    pc.Consumed = true;
                    var factory = allMethods.FirstOrDefault(m =>
                        string.Equals(m.Name, pc.Method, StringComparison.Ordinal) && HasImplicitConversion(genComp, genSrc, m.ParamType) && HasImplicitConversion(genComp, m.ReturnType, genTgt));
                    if (factory.Name is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConstructorInvalid,
                            pc.Loc,
                            $"[MapConstructor<{genSrc.ToDisplayString()}, {genTgt.ToDisplayString()}>(\"{pc.Method}\")] factory was not found or has an incompatible signature (it must take the source type '{genSrc.ToDisplayString()}' and return the destination type '{genTgt.ToDisplayString()}')"));
                    }
                    else
                    {
                        genFactory = factory.Name;
                    }

                    break;
                }

                MemberMap[] genCtorArgs;
                HashSet<string> genConsumed;
                HashSet<string> genRequiredInit;

                // Whether the chosen constructor already satisfies every `required` member. Tracked separately
                // from genRequiredInit because the selected ctor is scoped to the pattern below and DWARF079 asks
                // a different question of it — see CtorSetsRequiredMembers.
                var genCtorSetsRequired = false;
                HashSet<string>? genFactoryExcluded = null;

                if (genFactory is not null)
                {
                    // Factory builds the object; only settable members are assigned afterward, so init-only /
                    // required members are excluded (the factory owns them) and there are no ctor args.
                    genCtorArgs = Array.Empty<MemberMap>();
                    genConsumed = CollectFactoryExcludedMembers(genTgt);
                    genRequiredInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // Kept separately from genConsumed so DWARF080 can tell "the ctor assigns it" (no loss) from
                    // "the factory owns it and the source value is dropped" (silent loss).
                    genFactoryExcluded = genConsumed;
                }
                else if (ConstructorSelector.Select(ctx.SemanticModel.Compilation,
                             genTgt,
                             diagnostics,
                             genLoc,
                             out var genObjInitOnly,
                             allowNonPublic,
                             genSrc,
                             genExplicit) is not { } genCtor)
                {
                    continue;
                }
                else if (genObjInitOnly)
                {
                    genCtorSetsRequired = CtorSetsRequiredMembers(genCtor);
                    genCtorArgs = Array.Empty<MemberMap>();
                    genConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    genRequiredInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    if (!ResolveConstructorArguments(genCtor,
                            genSrc,
                            genComp,
                            genLoc,
                            diagnostics,
                            caseInsensitive,
                            allowNonPublic,
                            genExplicit,
                            allMethods,
                            mapperMethods,
                            enumPolicy,
                            synthesized,
                            nullStrategy,
                            classAutoNest,
                            nestedRegistry,
                            out genCtorArgs,
                            out genConsumed,
                            nullCollections == NullCollectionsBehavior.AsNull,
                            isPreserveMode,
                            isSetNullMode,
                            implicitConversions))
                    {
                        continue;
                    }

                    genRequiredInit = ComputeRequiredMustInitialize(genCtor, genTgt, genConsumed);
                    genCtorSetsRequired = CtorSetsRequiredMembers(genCtor);
                }

                var genMembers = ResolveMembers(
                    genSrc,
                    genTgt,
                    genIgnores,
                    genComp,
                    genLoc,
                    diagnostics,
                    new MapperOptions(
                        CaseInsensitive: caseInsensitive,
                        AutoNest: classAutoNest,
                        NullAsNull: nullCollections == NullCollectionsBehavior.AsNull,
                        IsPreserve: isPreserveMode,
                        IsSetNull: isSetNullMode,
                        ImplicitConversions: implicitConversions,
                        NameConvention: 0,
                        // No method: a [GenerateMap] pair is declared by the class, so there is no
                        // method-scoped annotation that could speak for it.
                        SkipNullSourceMembers: ResolveNullSkip(pairNullSkips, null, genSrc, genTgt, skipNullSrc),
                        AllowNonPublic: allowNonPublic,
                        ExplicitOnly: explicitOnly,
                        IgnoreObsolete: ignoreObsolete),
                    genExplicit,
                    allMethods,
                    mapperMethods,
                    enumPolicy,
                    synthesized,
                    nullStrategy,
                    Array.Empty<string>(),
                    new List<string>(),
                    genConsumed,
                    genRequiredInit,
                    nestedRegistry,
                    MatchPairValues(pairValues, genTgt),
                    valueProviders,
                    mapPropertyExtras: genExtras,
                    // StringFormat rides on the SAME [MapProperty] the rename does, so a path that reads the
                    // directive and does not thread this drops the format in silence — D20 in miniature.
                    stringFormats: genFormats,
                    mapperReservedConverters: mapperReservedConverters,
                    requiredMembersAlreadySatisfied: genCtorSetsRequired,
                    factoryExcludedMembers: genFactoryExcluded);

                var genBefore = new List<string>();
                foreach (var h in beforeHookDefs)
                    if (HasImplicitConversion(genComp, genSrc, h.ParamType))
                    {
                        genBefore.Add(h.Name);
                    }

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

                    if (!applies)
                    {
                        continue;
                    }

                    var tIsRef = h.TargetRefKind == RefKind.Ref;
                    if (genTgt.IsValueType && !tIsRef)
                    {
                        continue;
                    }

                    genAfter.Add(new HookCall(h.Name, takesSource, tIsRef));
                }

                // I17 STOPS HERE, and the boundary is a measurement rather than a preference. Withholding a
                // method is only safe while its DECLARATION survives: a partial method is declared by the
                // CONSUMER, so a sibling that maps a nested member through it still BINDS and the single
                // CS8795 is the whole cost. A [GenerateMap] pair has no declaration — the generator is the
                // only source of the symbol — so withholding it made a sibling's `N = Map(o.N)` emit
                // **CS0103, 'the name Map does not exist'**, in a file the consumer cannot edit: the
                // EmittedInvalidCode genre, whose ceiling is exactly zero. Measured on a two-pair probe
                // before this line was written. So the pair keeps the whole-class kill, and the CS8795 it
                // costs a sibling stays: loud collateral beats generated code that does not compile.
                withheld = false;

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
                    FactoryMethod: genFactory,
                    Withheld: withheld));
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

                // A helper synthesized for a pair the class ALSO declares must construct it the way the declared
                // pair does. Under Preserve/SetNull the element route is REQUIRED to be a synthesized helper —
                // calling the public method from a collection helper would allocate a fresh DwarfRefContext per
                // element and lose the identity map — so this is the one place the two routes can still diverge
                // after element resolution learned to reuse declared pairs. Left alone, `Map(node)` ran the
                // factory and `Map(node).Kids[0]` did not.
                //
                // Scoped to DECLARED pairs deliberately: a [MapConstructor] naming a pair with no [GenerateMap]
                // is already refused by DWARF056 ("matches no pair"), and quietly honouring it here would make
                // that diagnostic untrue.
                string? nestedFactory = null;
                if (genPairs.Exists(gp => SymbolEqualityComparer.Default.Equals(gp.Src, nestedSrc) && SymbolEqualityComparer.Default.Equals(gp.Tgt, nestedTgt)))
                {
                    foreach (var pc in pairConstructors)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(pc.Source, nestedSrc) || !SymbolEqualityComparer.Default.Equals(pc.Target, nestedTgt))
                        {
                            continue;
                        }

                        // A factory that does not resolve is already reported against the declared pair; saying
                        // it twice, once without a usable location, would only add noise.
                        var nestedFactorySym = allMethods.FirstOrDefault(m =>
                            string.Equals(m.Name, pc.Method, StringComparison.Ordinal) && HasImplicitConversion(genComp, nestedSrc, m.ParamType) && HasImplicitConversion(genComp, m.ReturnType, nestedTgt));
                        if (nestedFactorySym.Name is not null)
                        {
                            nestedFactory = nestedFactorySym.Name;
                        }

                        break;
                    }
                }

                // Choose construction strategy for the nested target type.
                IMethodSymbol? nestedCtor = null;
                var nestedObjInitOnly = false;
                if (nestedFactory is null)
                {
                    nestedCtor = ConstructorSelector.Select(ctx.SemanticModel.Compilation,
                        nestedTgt,
                        diagnostics,
                        nestedLocation,
                        out nestedObjInitOnly,
                        allowNonPublic,
                        nestedSrc,
                        nestedExplicit);
                    if (nestedCtor is null)
                    {
                        // DWARF025/026 already reported; skip body emission for this pair.
                        nestedRegistry.ClearCurrentPair();
                        continue;
                    }
                }

                MemberMap[] nestedCtorArgs;
                HashSet<string> nestedConsumed;
                HashSet<string> nestedRequiredMustInit;
                HashSet<string>? nestedFactoryExcluded = null;

                if (nestedFactory is not null)
                {
                    // The factory builds the object; only settable members are assigned afterwards, so init-only
                    // and required members are excluded — the factory owns them. Same shape as the declared path.
                    nestedCtorArgs = Array.Empty<MemberMap>();
                    nestedConsumed = CollectFactoryExcludedMembers(nestedTgt);
                    nestedRequiredMustInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    nestedFactoryExcluded = nestedConsumed;
                }
                else if (nestedObjInitOnly)
                {
                    nestedCtorArgs = Array.Empty<MemberMap>();
                    nestedConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    nestedRequiredMustInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    // C1: use the per-pair autoNest value (pairAutoNest), NOT classAutoNest.
                    if (!ResolveConstructorArguments(nestedCtor!,
                            nestedSrc,
                            ctx.SemanticModel.Compilation,
                            nestedLocation,
                            diagnostics,
                            caseInsensitive,
                            allowNonPublic,
                            nestedExplicit,
                            allMethods,
                            mapperMethods,
                            enumPolicy,
                            synthesized,
                            nullStrategy,
                            pairAutoNest,
                            nestedRegistry,
                            out nestedCtorArgs,
                            out nestedConsumed,
                            nullCollections == NullCollectionsBehavior.AsNull,
                            isPreserveMode,
                            isSetNullMode,
                            implicitConversions))
                    {
                        nestedRegistry.ClearCurrentPair();
                        continue;
                    }

                    nestedRequiredMustInit = ComputeRequiredMustInitialize(nestedCtor!, nestedTgt, nestedConsumed);
                }

                // C1: use the per-pair autoNest value (pairAutoNest), NOT classAutoNest.
                var nestedMembers = ResolveMembers(
                    nestedSrc,
                    nestedTgt,
                    nestedIgnores, // pair-scoped [MapIgnore<T>] (empty when none declared)
                    ctx.SemanticModel.Compilation,
                    nestedLocation,
                    diagnostics,
                    new MapperOptions(
                        CaseInsensitive: caseInsensitive,
                        AutoNest: pairAutoNest,
                        NullAsNull: nullCollections == NullCollectionsBehavior.AsNull,
                        IsPreserve: isPreserveMode,
                        IsSetNull: isSetNullMode,
                        ImplicitConversions: implicitConversions,
                        NameConvention: 0,
                        // A synthesized nested pair honours its own [MapNullSkip<S,T>] if the author declared
                        // one, otherwise the enclosing class's policy. Without the pair-scoped lookup the
                        // enclosing class's value is the ONLY input, which is how one logical nested pair
                        // reached from two classes ended up with opposite null semantics.
                        SkipNullSourceMembers: ResolveNullSkip(pairNullSkips, null, nestedSrc, nestedTgt, skipNullSrc),
                        AllowNonPublic: allowNonPublic,
                        // FALSE on purpose: this is the auto-synthesized NESTED mapper. Explicit-only guards
                        // the TOP-LEVEL trust boundary; reaching a nested pair already required the developer
                        // to map that edge explicitly (top-level auto-nest is blocked by DWARF072), so the
                        // nested contents map normally. Propagating it would give every nested member DWARF072
                        // — a synthesized mapper has no [MapProperty] to satisfy it — making nested objects
                        // unmappable. For a nested trust boundary, declare that pair's own
                        // [DwarfMapper(AutoMatchMembers = false)] mapper.
                        ExplicitOnly: false,
                        // IgnoreObsolete DOES propagate, unlike ExplicitOnly: skipping an obsolete nested
                        // member just leaves it at its default — safe and consistent, with no "unmappable"
                        // hazard.
                        IgnoreObsolete: ignoreObsolete),
                    nestedExplicit, // pair-scoped [MapProperty<S,T>] (empty when none declared)
                    allMethods,
                    mapperMethods,
                    enumPolicy,
                    synthesized,
                    nullStrategy,
                    new List<string>(),
                    new List<string>(), // no flatten/reinterpret
                    nestedConsumed,
                    nestedRequiredMustInit,
                    nestedRegistry,
                    MatchPairValues(pairValues, nestedTgt),
                    valueProviders,
                    mapPropertyExtras: nestedExtras,
                    // A synthesized nested mapper must not adopt a dedicated converter either — the author
                    // never wrote this pair, so they certainly did not offer it one.
                    mapperReservedConverters: mapperReservedConverters,
                    requiredMembersAlreadySatisfied: nestedCtor is not null && CtorSetsRequiredMembers(nestedCtor),
                    factoryExcludedMembers: nestedFactoryExcluded);

                // Only the pairs registered above — a genuinely NESTED member pair is deliberately left alone,
                // because source coverage has never applied at depth and turning it on for every synthesized pair
                // would be a broad behavioural change rather than closing this gap.
                foreach (var owed in elementPairsOwedCoverage)
                    if (SymbolEqualityComparer.Default.Equals(owed.Src, nestedSrc) && SymbolEqualityComparer.Default.Equals(owed.Tgt, nestedTgt))
                    {
                        EmitSourceCoverage(
                            nestedSrc,
                            nestedMembers,
                            null,
                            classIgnoreSources,
                            owed.IgnoreSources,
                            ignoreObsolete,
                            ctx.SemanticModel.Compilation,
                            allowNonPublic,
                            owed.Loc,
                            diagnostics);
                        break;
                    }

                nestedRegistry.ClearCurrentPair();

                // Hooks ([BeforeMap]/[AfterMap]) bound to THIS pair must also run when the pair is mapped as a
                // nested member or collection element — otherwise a target produced via the private helper silently
                // skips its post-processing (e.g. an AfterMap that rebuilds a dictionary), a data-loss bug.
                // Match by the same implicit-conversion rule the public pairs use (see ~line 835).
                var nestedBefore = new List<string>();
                foreach (var h in beforeHookDefs)
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, nestedSrc, h.ParamType))
                    {
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
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, nestedSrc, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, nestedTgt, h.P1);
                        takesSource = true;
                    }

                    if (!applies)
                    {
                        continue;
                    }

                    var nestedTargetIsRef = h.TargetRefKind == RefKind.Ref;
                    // Struct target passed by value would lose the hook's mutations; skip it here (the public /
                    // update-into path for the same pair surfaces the AfterMapValueTargetByValue diagnostic).
                    if (nestedTgt.IsValueType && !nestedTargetIsRef)
                    {
                        continue;
                    }

                    nestedAfter.Add(new HookCall(h.Name, takesSource, nestedTargetIsRef));
                }

                // Build a private (non-partial) MapMethodModel for this synthesized pair.
                // IsRecursionCapable is set to false here and patched below after ComputeRecursionCapability().
                var nestedModel = new MapMethodModel(
                    nestedName,
                    "private",
                    nestedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    nestedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "s",
                    nestedSrc.IsReferenceType,
                    EquatableArray.From(nestedMembers),
                    EquatableArray.From(nestedBefore),
                    EquatableArray.From(nestedAfter),
                    false,
                    "",
                    EquatableArray.From(nestedCtorArgs),
                    false,
                    nestedTgt.IsReferenceType, // patched below
                    FactoryMethod: nestedFactory);

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
                    {
                        nestedRegistry.ForceRecursionCapable(name);
                    }

                // Re-run to incorporate the newly forced entries.
                nestedRegistry.ComputeRecursionCapability();
            }

            // Build a set of method names that are recursion-capable (for the public method check).
            var recursionCapableNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (model, name) in pendingNestedModels)
            {
                var isRC = nestedRegistry.IsRecursionCapable(name);
                if (isRC)
                {
                    recursionCapableNames.Add(name);
                }

                // Rebuild the model with the correct IsRecursionCapable flag.
                methods.Add(model with
                {
                    IsRecursionCapable = isRC
                });
            }

            // ── DWARF076: declared create-map whose source and target are the same type ──
            ReportSelfMapWithoutConfiguration(methods, publicMethodLocs, diagnostics, classSymbol);
            // ── DWARF060: same-source / multiple-target signature collision ──────────
            ReportSameSourceSignatureCollisions(methods, publicMethodLocs, diagnostics);
            // ── Also detect declared public methods on a recursion cycle ────────────
            // Owned here because later phases read them; the phase below is what fills them. `nodesOnCycle`
            // is deliberately EMPTY at this point and populated inside, because it is derived from the call
            // graph the phase itself builds — ordering that the extraction made explicit.
            var declaredNameCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var allCallGraph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var nodesOnCycle = new HashSet<string>(StringComparer.Ordinal);
            var selfRecursivePublicMethods = new HashSet<string>(StringComparer.Ordinal);

            DetectDeclaredMethodsOnRecursionCycle(methods, nestedRegistry, pendingNestedModels, recursionCapableNames, declaredNameCount, nodesOnCycle, selfRecursivePublicMethods, allCallGraph);
            // ── None+Throw: upgrade collection/dict helpers whose element method is self-recursive ──
            UpgradeElementHelpersOnRecursionCycle(methods, nestedRegistry, recursionCapableNames, selfRecursivePublicMethods, nodesOnCycle, declaredNameCount);
            // ── Mark public methods and synthesized methods that call recursion-capable pairs ─
            MarkRecursionCapableCallers(methods, recursionCapableNames, selfRecursivePublicMethods, maxDepth);
            // ── Preserve-mode second pass: propagate ctx to public methods whose synthesized callees
            PropagateContextToPublicMethodsSecondPass(methods, recursionCapableNames, selfRecursivePublicMethods, maxDepth, isPreserveMode);
            // ── MF-A fix: [MapDerivedType] dispatch method arm ctx threading ────────────
            ThreadContextThroughDispatchArms(methods, recursionCapableNames, selfRecursivePublicMethods, maxDepth);
            // ── MF-B fix: Preserve + [MapDerivedType] dispatch wrapper synthesis ─────────
            SynthesizePreserveDispatchWrappers(methods, recursionCapableNames, synthesized, maxDepth, isPreserveMode);
            // ── Plan 19 C2: Preserve mode post-processing ───────────────────────────
            ReportCyclicConstructorParameters(methods, allCallGraph, diagnostics, isPreserveMode, declaredNameCount);
            // ── OnCycle = SetNull post-processing (None mode) ────────────────────────
            ApplySetNullPostPass(methods, isSetNullMode);

            // Report DWARF031 if the registry cap was exceeded.
            if (nestedRegistry.CapExceeded)
                // Use a null location — the cap is a generator-level limit, not method-specific.
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.DeepNestingLimit,
                    null,
                    nestedRegistry.CapTriggerType));
            }

            // CollectRoundTrips must be called before capturing diagnostics so that DWARF020/021 are included.
            var roundTrips = CollectRoundTrips(classSymbol, ctx.SemanticModel.Compilation, diagnostics);

            // DWARF055 (Info): a single mapper resolving a very large number of members. All extraction runs in
            // the syntax transform, so an enormous mapper can add IDE/compile latency. High threshold → only
            // genuine god-mappers trip it; suppressible. Heads-up, never a build break.
            const int LargeMapperMemberThreshold = 300;
            var mappedMemberCount = methods.Sum(m => m.Members.Count + m.ConstructorArguments.Count);
            if (mappedMemberCount > LargeMapperMemberThreshold)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MapperTooLarge,
                    LocationInfo.From(classSyntax.Identifier.GetLocation()),
                    $"mapper '{classSymbol.Name}' resolves {mappedMemberCount} mapped members across its methods " +
                    $"(> {LargeMapperMemberThreshold}); a mapper this large can add IDE/compile latency — " +
                    "consider splitting it into smaller mappers"));
            }

            // DWARF095 (B20): the class-site verdict for unscoped [MapIgnore] names, delivered only after every
            // endpoint has had its chance to mark a name live — a class-wide ignore is judged against every pair
            // the class maps, exactly as its tolerance-where-it-matches-nothing contract says (see
            // ReportElementWiseDirectiveGaps). Reported once per distinct dead name; stands down entirely when an
            // endpoint this walk cannot see is present (see classIgnoreLivenessBlinded).
            if (!classIgnoreLivenessBlinded)
            {
                var deadClassIgnores = new HashSet<string>(IgnoreNameComparer);
                foreach (var n in classIgnores)
                    if (!liveClassIgnores.Contains(n) && deadClassIgnores.Add(n))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnscopedIgnoreNoMatch,
                            LocationInfo.From(classSyntax.Identifier.GetLocation()),
                            $"[MapIgnore(\"{n}\")] on mapper '{classSymbol.Name}' names no destination member of " + "any pair this mapper maps — it excludes nothing (directive names match exactly, " + "including case); fix the name or remove the attribute"));
                    }
            }

            // DWARF056: a pair-scoped attribute that matched no mapped pair (top-level or nested) silently does
            // nothing — surface it (usually a typo'd type argument or a missing [GenerateMap]).
            foreach (var pp in pairProps)
                if (!pp.Consumed)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch,
                        pp.Loc,
                        $"[MapProperty<{pp.Source.ToDisplayString()}, {pp.Target.ToDisplayString()}>(\"{pp.SrcMember}\", \"{pp.TgtMember}\")] matches no mapped pair; add [GenerateMap<{pp.Source.ToDisplayString()}, {pp.Target.ToDisplayString()}>] (or a mapping that nests it), or fix the type arguments"));
                }

            foreach (var pi in pairIgnores)
                if (!pi.Consumed)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch,
                        pi.Loc,
                        $"[MapIgnore<{pi.Target.ToDisplayString()}>(\"{pi.Member}\")] matches no mapped pair targeting {pi.Target.ToDisplayString()}"));
                }

            foreach (var pv in pairValues)
                if (!pv.Consumed)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch,
                        pv.Loc,
                        $"[MapValue<{pv.Target.ToDisplayString()}>(\"{pv.Member}\")] matches no mapped pair targeting {pv.Target.ToDisplayString()}"));
                }

            foreach (var pc in pairConstructors)
                if (!pc.Consumed)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.PairScopedNoMatch,
                        pc.Loc,
                        $"[MapConstructor<{pc.Source.ToDisplayString()}, {pc.Target.ToDisplayString()}>(\"{pc.Method}\")] matches no [GenerateMap<{pc.Source.ToDisplayString()}, {pc.Target.ToDisplayString()}>] pair"));
                }

            // DWARF084/085: [RestatesBase] pairs, checked against the base pair they name. Runs here because it
            // needs the RESOLVED mappings — the point is to catch a restatement that is present but no longer does
            // the same thing, which no count of attributes could see.
            CheckRestatedBases(classSymbol,
                methods,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                diagnostics);

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
                EquatableArray.From(conventionRefs),
                registerCollectionShapes,
                EquatableArray.From(handWrittenProvides));
        }

        // ISSUE-044: required for the same reason as ReadableMembers/WritableMembers — this wrapper composes
        // both, so a defaulted call here drops the mapper's AllowNonPublic opt-in just as silently.
        private static IEnumerable<(string Name, ITypeSymbol Type)> ReadOnlyMembers(
            ITypeSymbol type,
            Compilation? compilation,
            bool allowNonPublic)
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
        /// <summary>
        ///     Whether the chosen constructor carries <c>[SetsRequiredMembers]</c>, which makes C# treat every
        ///     <c>required</c> member as already satisfied.
        /// </summary>
        /// <remarks>
        ///     Split out of <see cref="ComputeRequiredMustInitialize" /> because DWARF079 needs the same fact for a
        ///     different question: that method answers "which required members must ALSO appear in the
        ///     initializer", while DWARF079 asks "would omitting this required member actually break the build".
        ///     Under <c>[SetsRequiredMembers]</c> the answer to the second is no, and ignoring the member is
        ///     legitimate.
        /// </remarks>
        private static bool CtorSetsRequiredMembers(IMethodSymbol? ctor)
        {
            return ctor is not null &&
                   ctor.GetAttributes()
                       .Any(a => a.AttributeClass?.ToDisplayString() == SetsRequiredMembersAttribute);
        }

        /// <summary>
        ///     Hand-written methods marked <c>[ProvidesMap]</c>, validated to the shape the ambient registry can
        ///     hold: one parameter in, one value out, publicly nameable on both sides.
        /// </summary>
        /// <remarks>
        ///     Silently skipping a mis-shaped method would be the wrong trade — the author asked for a
        ///     registration and would get none, with no indication why. The shape is checked here and refused with
        ///     DWARF082 rather than dropped.
        /// </remarks>
        private static List<HandWrittenProvide> CollectHandWrittenProvides(
            INamedTypeSymbol classSymbol,
            List<DiagnosticInfo>? diagnostics = null)
        {
            var result = new List<HandWrittenProvide>();

            foreach (var member in classSymbol.GetMembers())
            {
                if (member is not IMethodSymbol method)
                {
                    continue;
                }

                var marked = method.GetAttributes().Any(a =>
                    string.Equals(a.AttributeClass?.Name, KnownNames.ProvidesMap, StringComparison.Ordinal) && a.AttributeClass?.ContainingNamespace?.ToDisplayString() == KnownNames.Ns);

                if (!marked)
                {
                    continue;
                }

                var shapeOk = method.Parameters.Length == 1 && !method.ReturnsVoid && method.DeclaredAccessibility == Accessibility.Public && IsEffectivelyPublic(method.Parameters[0].Type) && IsEffectivelyPublic(method.ReturnType);

                if (!shapeOk)
                {
                    diagnostics?.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.ProvidesMapInvalidShape,
                        LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None),
                        method.Name));
                    continue;
                }

                result.Add(new HandWrittenProvide(
                    method.Name,
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.IsStatic));
            }

            return result;
        }

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
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

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
                {
                    result.Add(name);
                }

            return result;
        }

        /// <summary>
        ///     The destination-member names an unscoped <c>[MapIgnore]</c> can legitimately name on a target:
        ///     WRITABLE members (excluded from mapping) plus READ-ONLY members (an ignore there suppresses the
        ///     silent-loss warning — see the read-only guard in <c>ResolveMembers</c>). Derived from the same two
        ///     member walks resolution consults, with the set's own comparer, so the question asked is "could real
        ///     resolution have read this name?" rather than a re-guess of what resolution does.
        /// </summary>
        private static HashSet<string> IgnorableMemberNames(
            ITypeSymbol target,
            Compilation compilation,
            bool allowNonPublic,
            Dictionary<ITypeSymbol, HashSet<string>> memo)
        {
            if (memo.TryGetValue(target, out var cached))
            {
                return cached;
            }

            var names = new HashSet<string>(IgnoreNameComparer);
            foreach (var m in WritableMembers(target, compilation, allowNonPublic)) names.Add(m.Name);
            foreach (var m in ReadOnlyMembers(target, compilation, allowNonPublic)) names.Add(m.Name);
            memo[target] = names;
            return names;
        }

        /// <summary>
        ///     The per-endpoint half of the <c>DWARF095</c> "names no destination member" check (B20). Marks
        ///     which CLASS-level unscoped ignore names are live against <paramref name="targetType" /> (the
        ///     class-wide verdict is delivered once, after every endpoint has been seen), and — when
        ///     <paramref name="method" /> is given — reports each METHOD-level name that matches nothing on that
        ///     method's own destination. Span and async-stream methods pass <c>null</c>: every method-site
        ///     directive there is already <c>DWARF090</c>'s to report, matched or not, and two ids about one
        ///     attribute would send the caller in two directions.
        /// </summary>
        private static void JudgeUnscopedIgnores(
            List<string> classIgnores,
            HashSet<string> liveClassIgnores,
            IMethodSymbol? method,
            ITypeSymbol targetType,
            Compilation compilation,
            bool allowNonPublic,
            LocationInfo? methodLocation,
            List<DiagnosticInfo> diagnostics,
            Dictionary<ITypeSymbol, HashSet<string>> memo)
        {
            var names = IgnorableMemberNames(targetType, compilation, allowNonPublic, memo);
            foreach (var n in classIgnores)
                if (names.Contains(n))
                {
                    liveClassIgnores.Add(n);
                }

            if (method is null)
            {
                return;
            }

            foreach (var n in ReadIgnores(method))
                if (!names.Contains(n))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnscopedIgnoreNoMatch,
                        methodLocation,
                        $"[MapIgnore(\"{n}\")] on '{method.Name}' names no destination member of " + $"'{targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' — it " + "excludes nothing (directive names match exactly, including case); fix the name or " + "remove the attribute"));
                }
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

        /// <summary>
        ///     The one gate both ELEMENT-WISE endpoints pass through: everything declared on a span or
        ///     async-stream method — or on its class — that cannot reach the auto-synthesized ELEMENT mapper.
        ///     <para>
        ///         Hoisted rather than duplicated. The <c>DWARF077</c> explicit-only check was written twice, once
        ///         in each branch, and the surface matrix then found ten more directives with the same silence at
        ///         the same two endpoints: a second copy of the reasoning is how the next one gets added to one
        ///         branch and forgotten in the other. Whatever else turns out to be dropped element-wise belongs
        ///         here, next to the two cases measured so far, not in a third place.
        ///     </para>
        ///     <para>
        ///         The readers are the SAME ones resolution uses — <see cref="ReadIgnores" /> and
        ///         <see cref="ReadExplicitMaps" /> — so this reports exactly the applications that would have been
        ///         honoured at a create map and nothing else. Re-parsing the attributes here would drift from what
        ///         is actually dropped, and would re-open the malformed-argument hole those two readers close (a
        ///         <c>[MapIgnore(null)]</c> yields no name and must reach neither the model nor a message).
        ///         The overloads they skip are already <c>DWARF088</c>'s, so nothing is reported twice.
        ///     </para>
        ///     <para>
        ///         A CLASS-scoped directive is additionally required to NAME A MEMBER of the element pair's target.
        ///         Class-wide directives are meant to be tolerated where they match nothing — a mapper declaring a
        ///         create map over one pair and a span map over an unrelated one carries an ignore that is about the
        ///         first pair only — so without that filter the gate reported a pair the caller never wrote about
        ///         and prescribed a remedy naming a member the type does not have. The method site is deliberately
        ///         NOT filtered: a directive written on the span method is about that method and nothing else, so a
        ///         name matching no member there is a mistake worth stating rather than another pair's business.
        ///     </para>
        /// </summary>
        /// <param name="method">The span or async-stream mapping method.</param>
        /// <param name="classSymbol">Its mapper class, whose class-scoped directives are dropped here too.</param>
        /// <param name="srcElement">The element pair's source type, named in the remedy.</param>
        /// <param name="tgtElement">
        ///     The element pair's target type. Named in the remedy, and — for the CLASS site — the type whose
        ///     writable members decide whether there is anything here to report at all.
        /// </param>
        /// <param name="explicitOnly"><c>[DwarfMapper(AutoMatchMembers = false)]</c> is in force.</param>
        /// <param name="compilation">
        ///     The compilation the element target's writable members are enumerated against, for the class-site
        ///     filter described above.
        /// </param>
        /// <param name="allowNonPublic">
        ///     <c>[DwarfMapper(AllowNonPublic = true)]</c>, widening that same enumeration — so a class-scoped
        ///     <c>[MapIgnore]</c> naming a non-public destination member is still recognised as one this gate has
        ///     something to say about.
        /// </param>
        /// <param name="location">The declaration site every report made here is anchored to.</param>
        /// <param name="diagnostics">Diagnostic sink; one entry per directive that cannot reach the element pair.</param>
        /// <returns>
        ///     <c>true</c> when the method must not be emitted at all. Only the explicit-only refusal returns it:
        ///     a trust boundary that cannot be enforced must not be half-applied, whereas a dropped
        ///     <c>[MapIgnore]</c> leaves a mapper that still works — and a blocking error there would strand every
        ///     partial method on the class behind <c>CS8795</c>, hiding the very refusal it was raised to deliver.
        /// </returns>
        private static bool ReportElementWiseDirectiveGaps(
            IMethodSymbol method,
            INamedTypeSymbol classSymbol,
            ITypeSymbol srcElement,
            ITypeSymbol tgtElement,
            bool explicitOnly,
            Compilation compilation,
            bool allowNonPublic,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            if (explicitOnly)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ExplicitOnlyNotElementWise,
                    location,
                    method.Name));
                return true;
            }

            var src = srcElement.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var tgt = tgtElement.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            // The destination members the element pair actually has. [MapIgnore] names a DESTINATION member, so
            // this is the same set resolution consults when deciding what an ignore excludes. Built lazily
            // because the overwhelmingly common case is a class with no unscoped [MapIgnore] at all.
            HashSet<string>? elementTargetMembers = null;

            foreach (var (symbol, site) in new[]
                     {
                         ((ISymbol)method, "mapping method"), (classSymbol, "mapper class")
                     })
            {
                var isClassSite = !ReferenceEquals(symbol, method);
                foreach (var ignored in ReadIgnores(symbol))
                {
                    // A class-level [MapIgnore] is class-WIDE, and being tolerated where it matches nothing is how
                    // it is meant to work: a mapper declaring a create map over (Src, Dst) and a span map over an
                    // unrelated (Foo, Bar) legitimately carries an ignore that is about Dst alone. Reported
                    // unfiltered, this named a pair the caller never wrote about and prescribed
                    // [MapIgnore<Bar>("Id")] for a type with no Id — as a warning this repository escalates to an
                    // error. That is the objection the [MapProperty] exclusion below already makes; it belongs to
                    // both attributes, and applying it to one was the defect.
                    //
                    // Matched with IgnoreNameComparer — the comparer the ignore set resolution consults is built
                    // with — rather than a comparer chosen here. This asks "would real resolution have honoured
                    // this name?", and only the set's own comparer can answer it. Written case-INSENSITIVE first,
                    // on the reasoning that a member differing only in case is one the caller plausibly meant:
                    // backwards, because the set is ordinal, so [MapIgnore("id")] against a property Id excludes
                    // nothing at the create map either. There is no element-wise gap to report, and reporting one
                    // prescribed a pair-scoped form that would not work at all.
                    if (isClassSite)
                    {
                        elementTargetMembers ??= new HashSet<string>(
                            MemberFacts.Writable(tgtElement, compilation, allowNonPublic).Select(m => m.Name),
                            IgnoreNameComparer);
                        if (!elementTargetMembers.Contains(ignored))
                        {
                            continue;
                        }
                    }

                    Report($"[MapIgnore(\"{ignored}\")] on this {site}",
                        $"[MapIgnore<{tgt}>(\"{ignored}\")]",
                        MemberDirectiveElsewhere);
                }

                // Only the method site: the pair-scoped [MapProperty<S,T>] IS the class form, and the class's
                // unscoped applications belong to whatever [GenerateMap] pair the class declares rather than to a
                // method — reporting them here would name a pair the caller never wrote about.
                if (isClassSite)
                {
                    continue;
                }

                foreach (var (source, target, _) in ReadExplicitMaps(symbol))
                    Report($"[MapProperty(\"{source}\", \"{target}\")] on this {site}",
                        $"[MapProperty<{src}, {tgt}>(\"{source}\", \"{target}\")]",
                        MemberDirectiveElsewhere);
            }

            // The METHOD-scoped [MapNullSkip], for the same reason and with the same remedy — the element pair takes
            // its null-skip policy from ResolveNullSkip, which has no method to consult for a pair reached
            // element-wise. Read through the same reader resolution uses, so the malformed-argument fallback is one
            // rule rather than two, and reported whatever the value is: a discarded [MapNullSkip(false)] on a class
            // that enables skipping is exactly as silent as a discarded `true`.
            //
            // The VALUE is rendered explicitly on both halves even when the caller wrote the bare form. A remedy of
            // [MapNullSkip<Src, Dst>] copied out in answer to a written [MapNullSkip(false)] would invert the
            // semantics the caller asked for, which is a worse outcome than quoting them a form they did not type.
            //
            // Only the method site: the arity-0 form is AttributeTargets.Method by AttributeUsage, and the class's
            // own [DwarfMapper(SkipNullSourceMembers = …)] does reach the element pair (as classDefault), so there
            // is nothing dropped at the class site to report.
            if (ReadMapNullSkip(method) is { } nullSkip)
            {
                var arg = nullSkip ? "true" : "false";
                // The tail states only what was MEASURED, and it has been wrong in both directions once already.
                // An early draft claimed the method form was "refused at projection" while the projection resolver
                // was deliberately not fed the scoped forms, so it was SILENT there; that was corrected to
                // "silent", and the correction went stale the moment the scoped forms were threaded. It is now
                // refused there — the resolver reports DWARF028 per affected member, because an object initializer
                // constructs the destination and "keep its current value" has no current value to keep. Pinned in
                // both directions in MapNullSkipScopeTests; do not edit this sentence without re-measuring.
                Report($"[MapNullSkip({arg})] on this mapping method",
                    $"[MapNullSkip<{src}, {tgt}>({arg})]",
                    "The method form is honoured at the create-map and update-into endpoints, and refused at " +
                    "projection (DWARF028 — an object initializer constructs the destination, so \"keep its " +
                    "current value\" has nothing to keep). " +
                    "[DwarfMapper(SkipNullSourceMembers = " +
                    arg +
                    ")] reaches the element pair too, if the " +
                    "policy is meant to be the whole mapper's.");
            }

            // The METHOD-scoped [MapValue]. Read through ReadMapValues — the reader resolution itself uses — so an
            // application whose target is absent or not a string is dropped by one rule rather than two, and never
            // reaches a message with the word "null" in it. Reported for EVERY application, malformed included: a
            // [MapValue] naming no writable member is refused as DWARF042 at the create map and refused as nothing
            // at all here, so "it does not reach the element pair" is the true statement in both cases.
            //
            // The written form is echoed faithfully — constant, Use=, or neither — for the reason the [MapNullSkip]
            // arm above records: a remedy that quietly changes what the caller asked for is worse advice than none.
            // The pair-scoped remedy was MEASURED before it was prescribed: [MapValue<Dst>("Name", "x")] on the
            // mapper class reads Honoured at SpanMap and at AsyncStream, output differing by the assigned constant.
            foreach (var mv in ReadMapValues(method))
            {
                // A constant this library does not render (an array, a typeof, an error constant) falls back to
                // the BARE form rather than to a quoted "null": the target is still named and the value is simply
                // left out, which is honest, where printing a constant the caller did not write is not.
                var constant = mv.IsConstant ? FormatWrittenConstant(mv.Value) : null;
                var written = mv.Use is not null
                    ? $"[MapValue(\"{mv.Target}\", Use = \"{mv.Use}\")]"
                    : constant is not null
                        ? $"[MapValue(\"{mv.Target}\", {constant})]"
                        : $"[MapValue(\"{mv.Target}\")]";
                var remedy = "[MapValue<" + tgt + ">" + written.Substring("[MapValue".Length);
                // The tail says "reaches", not "is honoured": a well-formed [MapValue] is assigned at those
                // endpoints, a malformed one is refused there, and both are cases of the directive ARRIVING.
                // Projection is now one of them (D9 closed) — a constant becomes a literal in the SELECT, and the
                // Use= form alone is refused there as DWARF028. The sentence is pinned in both directions,
                // because it has been wrong in both: it claimed projection while the resolver never saw the
                // directive, and then claimed silence after the threading landed.
                Report(written + " on this mapping method",
                    remedy,
                    "The unscoped form reaches the create-map, update-into and projection endpoints — the " + "constant is assigned there, and a malformed one is refused there (at projection a " + "Use= value provider is refused too, as DWARF028: a query provider cannot call a method).");
            }

            // The METHOD-scoped [Flatten]. No pair-scoped twin exists, so the remedy is the dotted source path on
            // [MapProperty<Src, Dst>], one per pulled-up leaf — measured Honoured at SpanMap and AsyncStream
            // before being prescribed, because a remedy nobody ran is how a diagnostic sends a caller in a circle.
            // Read through ReadFlattenRoots, which already drops an absent or non-string argument.
            foreach (var root in ReadFlattenRoots(method))
                Report($"[Flatten(\"{root}\")] on this mapping method",
                    $"[MapProperty<{src}, {tgt}>(\"{root}.<leaf>\", \"<leaf>\")], one per pulled-up leaf",
                    "The directive is honoured at the create-map, update-into and projection endpoints, which is " + "why its silence here is worth saying out loud.");

            // The METHOD-scoped [Reinterpret], through ReadReinterpretMembers — the reader both the create-map and
            // the update-into branches resolve with, so an application whose single argument is not a string yields
            // no directive and reaches neither the model nor a message.
            //
            // This one takes ReportWithFix rather than Report, and the reason is worth stating rather than
            // inferring: [Reinterpret] has NO pair-scoped twin, so "write it pair-scoped on the mapper class" —
            // the sentence every other arm here ends with — would name a form that does not exist. The remedy is a
            // DECLARED create map, MEASURED before it was prescribed: with [Reinterpret("Data")] on a
            // `partial Dst Map(Src s)` beside the span method, the emitted loop is `d[__i] = Map(s[__i]);` and the
            // member is assigned through __DwarfBlit_… (MemoryMarshal.Cast), where without it the same member goes
            // through a per-element numeric conversion helper. Same reading at the async stream
            // (`yield return Map(…)`). A remedy nobody ran is how a diagnostic sends a caller in a circle.
            //
            // The tail names the create map and the update-into and stops there. PROJECTION is deliberately absent:
            // it is not read there either — a blit reinterprets one array's memory as another, and only two
            // branches call this reader — and a message that claims an endpoint honours a directive it discards is
            // the exact defect this gate exists to remove.
            foreach (var member in ReadReinterpretMembers(method))
                ReportWithFix($"[Reinterpret(\"{member}\")] on this mapping method",
                    "[Reinterpret] has no pair-scoped form, so the remedy is a DECLARED create map rather than a " +
                    $"re-scoped attribute: put [Reinterpret(\"{member}\")] on a `partial {tgt} <Name>({src} s)` " +
                    "on this mapper class. An element-wise map resolves its element pair through a declared " +
                    "mapping method where one exists rather than synthesizing one, so that create map is what " +
                    "this method's loop calls and the forced blit runs per element through it.",
                    "The directive is honoured at the create-map and update-into endpoints, which is why its " + "silence here is worth saying out loud — and the create map also VALIDATES it (DWARF022 for " + "a member that is not an unmanaged array on both sides, or names no writable destination " + "member at all); nothing validated it here either.");

            return false;

            void Report(string written, string remedy, string elsewhere)
            {
                ReportWithFix(written,
                    $"Write it PAIR-SCOPED on the mapper class — {remedy} — which does apply here.",
                    elsewhere);
            }

            void ReportWithFix(string written, string fix, string elsewhere)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.DirectiveNotAppliedElementWise,
                    location,
                    $"{written} does not reach '{method.Name}'. An element-wise map resolves no members itself: it " + $"maps each '{src}' to a '{tgt}' through an auto-synthesized mapper, which is shared by every " + "route to that pair and therefore takes its configuration only from directives that name the " + $"pair. {fix} " + elsewhere));
            }
        }

        /// <summary>
        ///     The one gate every mapping method passes through: a directive the generator reads at ONE endpoint
        ///     only, written at one of the other four, where it is discarded without a word.
        ///     <para>
        ///         Hoisted from the start rather than written per endpoint, which is the shape this branch has
        ///         had to unpick seven times. The silence is not element-wise — it also covers update-into,
        ///         projection and the create map, so <see cref="ReportElementWiseDirectiveGaps" /> is the wrong
        ///         home for it — and it is not one directive: the surface matrix recorded the identical finding
        ///         for <c>[FlattenGraph]</c>, <c>[MapDerivedType]</c> and <c>[ReverseMap]</c> (D11, D8, D13),
        ///         whose home is the CREATE MAP, and then again for <c>[MapCollectionKey]</c> (D14), whose home
        ///         is the UPDATE-INTO. Five call sites of one function, so a sixth endpoint inherits the check
        ///         instead of having to be taught it.
        ///     </para>
        ///     <para>
        ///         Each arm names its own home endpoint and is skipped there. That is what makes this one gate
        ///         rather than two mirror-image ones: "read at exactly one endpoint" is the shape, and which
        ///         endpoint that is belongs to the directive, not to the gate.
        ///     </para>
        ///     <para>
        ///         The readers are the SAME ones the honouring branch resolves with, so this reports exactly the
        ///         applications that would have been read there and nothing else. Re-parsing the attributes here
        ///         would drift from what is actually dropped and would re-open the malformed-argument hole those
        ///         readers close — <c>[FlattenGraph(null, null)]</c> and <c>[MapCollectionKey(null, null)]</c>
        ///         yield no directive and must reach neither the model nor a message.
        ///     </para>
        ///     <para>
        ///         Reported per APPLICATION, malformed applications included, for the reason A8's
        ///         <c>[MapValue]</c> arm gives: a <c>[FlattenGraph]</c> naming members that do not exist is
        ///         refused as <c>DWARF034</c> at the create map and refused as nothing at all here, so "it does
        ///         not reach this endpoint" is the true statement in both cases. That asymmetry is half of what
        ///         makes the silence worth a diagnostic — at its home endpoint even nonsense is validated.
        ///     </para>
        /// </summary>
        /// <param name="method">The create-map, update-into, projection, span or async-stream method.</param>
        /// <param name="compilation">Handed to <see cref="ReadDerivedTypeAttributes" />, which takes one.</param>
        /// <param name="srcType">The pair's source type — the element type at the two element-wise endpoints.</param>
        /// <param name="tgtType">The pair's target type — the element type at the two element-wise endpoints.</param>
        /// <param name="endpoint">
        ///     Which endpoint this method is. The reader-facing endpoint name and the adoption claim are both
        ///     DERIVED from it here rather than passed: a span or async-stream map resolves its element pair
        ///     through <see cref="TryResolveConversion" />, which adopts a DECLARED mapping method for that pair
        ///     before synthesizing one, so a create map carrying the directive is what the emitted loop calls
        ///     (<c>d[__i] = Map(s[__i]);</c>) and the directive genuinely arrives. MEASURED at both of them
        ///     before the message said it. Update-into, projection and the create map resolve their own members
        ///     and never call a sibling, so there the message claims only that the home endpoint honours the
        ///     directive — A8's revert is the standing proof that a prescribed remedy nobody ran is worse than
        ///     none. The adoption is of a CREATE map specifically, so it is claimed only by arms whose home is
        ///     the create map: nothing adopts a sibling update-into.
        /// </param>
        /// <param name="location">Where to report, or null.</param>
        /// <param name="diagnostics">The list every arm appends to.</param>
        private static void ReportDirectivesNotReadHere(
            IMethodSymbol method,
            Compilation compilation,
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            MapEndpointKind endpoint,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var src = srcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var tgt = tgtType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var endpointName = endpoint switch
            {
                MapEndpointKind.CreateMap => "create-map",
                MapEndpointKind.UpdateInto => "update-into",
                MapEndpointKind.Projection => "projection",
                MapEndpointKind.SpanMap => "span-map",
                _ => "async-stream"
            };
            var adoptsACreateMap = endpoint is MapEndpointKind.SpanMap or MapEndpointKind.AsyncStream;

            // ── Arms whose home is the CREATE MAP, skipped there ──────────────────────────────────────────────
            if (endpoint != MapEndpointKind.CreateMap)
            {
                // [FlattenGraph], read through the reader the create-map branch reads with, which already drops an
                // application whose two arguments are not both strings.
                foreach (var (navigation, collection) in ReadFlattenGraphAttributes(method))
                    Report($"[FlattenGraph(\"{navigation}\", \"{collection}\")]",
                        $"A graph flatten replaces the source of the destination collection '{collection}' with a " +
                        $"breadth-first walk of '{navigation}', and only the create map resolves one. Here the " +
                        $"directive is discarded and '{collection}' is filled by ordinary direct mapping instead, " +
                        "so the same declaration produces a walked graph on one overload of this mapper and a " +
                        "shallow copy on this one.",
                        MapEndpointKind.CreateMap);

                // [MapDerivedType], in BOTH of its forms, through the reader the create-map branch reads with —
                // which is also where the WRITTEN form comes from, so the message quotes the syntax the caller
                // typed rather than normalizing one into the other. `compilation` is the reader's own parameter;
                // it is passed rather than a second reader written, because a second reader of these attributes is
                // precisely the shape that has shipped two generator crashes on this branch.
                foreach (var (derivedSrc, derivedTgt, writtenGeneric) in
                         ReadDerivedTypeAttributes(method, compilation))
                {
                    var dSrc = derivedSrc.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var dTgt = derivedTgt.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    Report(
                        writtenGeneric
                            ? $"[MapDerivedType<{dSrc}, {dTgt}>]"
                            : $"[MapDerivedType(typeof({dSrc}), typeof({dTgt}))]",
                        $"A dispatch arm decides which destination TYPE to construct — a '{dSrc}' becomes a " +
                        $"'{dTgt}' rather than a '{tgt}' — from the source's RUNTIME type, and only the create " +
                        $"map constructs one. Here the directive is discarded and a '{dSrc}' is mapped as a " +
                        $"'{src}', so every member '{dTgt}' declares beyond '{tgt}' is dropped. The create map " +
                        "also VALIDATES these arms (DWARF035 for a type that is not assignable, a duplicate " +
                        "source type, or a pair that is not mappable); nothing validated them here either.",
                        MapEndpointKind.CreateMap);
                }

                // [ReverseMap], through HasReverseMap — the same predicate CollectReverseRenames and the DWARF052
                // check both consult, so this reports exactly the methods a create map would have treated as
                // forward ones. AllowMultiple is false on this attribute, so there is one application or none.
                //
                // carriedByTheAdoptedSibling is FALSE here even at the element-wise endpoints, and that is the
                // point of the flag. The adoption sentence is true of a directive that changes what the create map
                // EMITS, because that emission is what the loop calls. [ReverseMap] changes nothing about the
                // method it sits on: it makes a SEPARATE, separately-declared inverse method inherit this one's
                // renames, inverted. Appending the sentence would have told a caller their inverse reaches the
                // span map, which is not a claim about anything.
                if (HasReverseMap(method))
                {
                    Report("[ReverseMap]",
                        $"[ReverseMap] makes a separately-declared inverse method — '{src} <Name>({tgt} t)' — " +
                        "inherit this one's simple renames with their ends swapped, and only the create map " +
                        "looks for one. The match is by signature: a forward " +
                        $"'{tgt} <Name>({src} s)' against an inverse " +
                        $"'{src} <Name>({tgt} t)', both one-parameter create maps, which no other endpoint's " +
                        "signature is. Here nothing looks for an inverse and nothing inherits a rename — and no " +
                        "DWARF052 is raised either, because that check lives on the same create-map path.",
                        MapEndpointKind.CreateMap,
                        false);
                }
            }

            // ── Arms whose home is the UPDATE-INTO, skipped there ─────────────────────────────────────────────
            //
            // [MapCollectionKey], read through ReadCollectionKeys — the reader ApplyCollectionKeyUpserts itself
            // reads with, so this reports exactly the applications the upsert path would have acted on and an
            // application whose two arguments are not both strings reaches neither.
            //
            // carriedByTheAdoptedSibling is FALSE even at the element-wise endpoints, and for a sharper reason
            // than [ReverseMap]'s: what a span or async-stream loop adopts is a declared CREATE map for the
            // element pair, and a declared update-into is not one. There is no sibling here whose emission this
            // method's loop calls, so claiming the directive arrives through one would be false.
            if (endpoint != MapEndpointKind.UpdateInto)
            {
                foreach (var (collection, key) in ReadCollectionKeys(method))
                    Report($"[MapCollectionKey(\"{collection}\", \"{key}\")]",
                        $"A key-based upsert MERGES the source elements into the '{collection}' list the " +
                        "destination already holds — matching on '" +
                        key +
                        "', replacing what matches and " +
                        "appending what does not, so untouched elements survive. That needs an existing " +
                        $"destination to merge into, and the {endpointName} endpoint builds a fresh one. Here " +
                        $"the directive is discarded and '{collection}' is built by whole-collection " +
                        "replacement, which is what it would have been without it. The update-into also " +
                        "VALIDATES this directive (DWARF074 for a member that is not mapped, is not a " +
                        "List<T> on both sides, has differing element types, or names a key the element type " +
                        "does not have); nothing validated it here either.",
                        MapEndpointKind.UpdateInto);
            }

            void Report(
                string written,
                string what,
                MapEndpointKind home,
                bool carriedByTheAdoptedSibling = true)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.DirectiveNotReadAtThisEndpoint,
                    location,
                    $"{written} on '{method.Name}' is not read at the {endpointName} endpoint. {what} " +
                    (home == MapEndpointKind.CreateMap
                        ? $"Declare it on a create map over the same pair — {written} on a " + $"`partial {tgt} <Name>({src} s)` on this mapper class — which does honour it."
                        : $"Declare it on an update-into over the same pair — {written} on a " + $"`partial void <Name>({src} s, {tgt} d)` on this mapper class — which does honour " + "it.") +
                    (adoptsACreateMap && carriedByTheAdoptedSibling && home == MapEndpointKind.CreateMap
                        ? $" An element-wise map resolves its element pair through a declared '{src}' to '{tgt}' " + "mapping method where one exists, so that create map is what this method's loop " + "calls and the directive reaches this endpoint through it."
                        : string.Empty)));
            }
        }

        /// <summary>
        ///     Whether a <c>[MapValue]</c> constant is one this library can spell at all — the ONE statement of
        ///     that rule, consulted by both readers of these attributes.
        ///     <para>
        ///         It is a shared predicate rather than a check in each place because
        ///         <see cref="TypedConstant.Value" /> <b>throws</b> <see cref="InvalidOperationException" /> for an
        ///         array kind, and <c>[MapValue("Name", new[] { 1 })]</c> is legal C# — the constructor's parameter
        ///         is <c>object?</c>. <c>TryFormatConstant</c> had guarded that since it was written; the
        ///         element-wise gate was added later, read <c>.Value</c> directly, and crashed the whole generator.
        ///         A second copy of the guard would have fixed the instance and left the shape, which on this
        ///         branch is how the same defect has arrived seven times.
        ///     </para>
        ///     <para>
        ///         <c>Type</c> (a <c>typeof(X)</c> argument) is excluded for a quieter reason: it does not throw,
        ///         it answers an <see cref="ITypeSymbol" /> that <see cref="SymbolDisplay.FormatPrimitive" /> then
        ///         declines to render — so the caller's <c>typeof</c> came back as the word <c>null</c>, which is
        ///         a different constant from the one they wrote.
        ///     </para>
        /// </summary>
        private static bool IsRenderableConstant(TypedConstant tc)
        {
            return tc.Kind is not (TypedConstantKind.Array or TypedConstantKind.Type or TypedConstantKind.Error);
        }

        /// <summary>
        ///     A <c>[MapValue]</c> constant as it should appear back in a diagnostic's quoted source — quoted for a
        ///     string, bare for a number, <c>null</c> for a written <c>null</c> — or <c>null</c> when the value is
        ///     not one this library renders, so the caller sees the bare <c>[MapValue("Target")]</c> form instead
        ///     of an invented constant.
        ///     <para>
        ///         Separate from <c>RenderConstantLiteral</c> on purpose: that one renders a literal to be COMPILED
        ///         into the generated mapper and therefore needs the destination type to cast against. This one
        ///         renders text a human reads and copies back into their own source, where no destination type is
        ///         in hand and a cast would be noise. They share <see cref="IsRenderableConstant" />, which is the
        ///         part that must not differ.
        ///     </para>
        /// </summary>
        private static string? FormatWrittenConstant(TypedConstant value)
        {
            return !IsRenderableConstant(value)
                ? null
                : value.Value is null
                    ? "null"
                    : SymbolDisplay.FormatPrimitive(value.Value, true, false);
        }

        /// <summary>
        ///     Reports <c>DWARF088</c> for every MEMBER-placement <c>[MapProperty]</c> / <c>[MapIgnore]</c> found
        ///     on a mapper class or a mapping method — the overloads <see cref="ReadExplicitMaps" /> and
        ///     <see cref="ReadIgnores" /> skip, and skipped in silence until this check existed.
        ///     <para>
        ///         ONE check for both attributes and both sites, because it is one mistake: the caller reached for
        ///         the placement that belongs on a member of a type that declares its own mapping. Splitting it
        ///         per attribute would have produced two ids saying the same sentence, and splitting it per site
        ///         would have left whichever site was written second silent — the shape the surface matrix found
        ///         it in.
        ///     </para>
        ///     <para>
        ///         Reported per APPLICATION rather than per symbol: both attributes are <c>AllowMultiple</c>, and
        ///         two wrong ones are two mistakes to fix.
        ///     </para>
        /// </summary>
        /// <param name="symbol">The mapper class, or one partial mapping method on it.</param>
        /// <param name="site">
        ///     How the message names the place — "mapper class" or "mapping method". Passed in rather than
        ///     derived from <paramref name="symbol" /> so the two call sites read as the two cases they are.
        /// </param>
        /// <param name="location">The declaration site every <c>DWARF088</c> raised here is reported at.</param>
        /// <param name="diagnostics">Diagnostic sink; one entry per misplaced attribute APPLICATION.</param>
        private static void ReportMemberFormDirectives(
            ISymbol symbol,
            string site,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                var cls = attr.AttributeClass?.ToDisplayString();
                string message;

                if (cls == KnownNames.MapPropertyFqn && attr.ConstructorArguments.Length == 1)
                {
                    // The name is a string by construction (both constructors take strings only), but a
                    // half-typed application in the IDE can hand us an error constant; fall back rather than
                    // reporting a message with the word "null" in it.
                    var name = attr.ConstructorArguments[0].Value as string ?? "…";
                    message =
                        $"[MapProperty(\"{name}\")] on this {site} uses the MEMBER-placement overload, which names " +
                        "the destination THE ANNOTATED MEMBER supplies — it is the form for a member of a " +
                        "[MapTo] source or a [GenerateMap] host, where the annotated type declares the mapping. " +
                        $"Here it binds '{name}' to itself, which is what auto-matching already does, and any " +
                        "Use / When / NullSubstitute / StringFormat written beside it is discarded with it " +
                        "(they are named arguments on this same overload). Supply both names: " +
                        $"[MapProperty(\"{name}\", \"<destination>\")].";
                }
                else if (cls == KnownNames.MapIgnoreFqn && attr.ConstructorArguments.Length == 0)
                {
                    message =
                        $"[MapIgnore] with no argument on this {site} uses the MEMBER-placement overload, where " +
                        "THE ANNOTATED MEMBER is the thing ignored — it is the form for a member of a [MapTo] " +
                        $"source or a [GenerateMap] host. On a {site} it names nothing and excludes nothing, so " +
                        "the completeness gate goes on demanding the member you meant to exclude. Name it: " +
                        "[MapIgnore(\"<destination>\")].";
                }
                else
                {
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.MemberFormDirectiveOnMapper,
                    location,
                    message));
            }
        }

        /// <summary>
        ///     <c>DWARF088</c> for a <c>[MapProperty]</c> or <c>[MapIgnore]</c> written on a MEMBER of a
        ///     <c>[DwarfMapper]</c> class (TASKS.md <c>B18</c>). Every form is reported, not just the
        ///     member-placement overloads, because at this placement every form is inert: the mapper's own
        ///     property or field belongs to neither type of any mapped pair, so there is nothing for the directive
        ///     to bind to and nothing downstream ever asks.
        ///     <para>
        ///         The remedy DIFFERS by form and is stated per form. A member-placement overload was aimed at the
        ///         wrong KIND of type — it belongs on a member of a <c>[MapTo]</c> source or a <c>[GenerateMap]</c>
        ///         host, where the annotated type declares its own mapping. A class- or method-placement overload
        ///         was aimed at the right kind of type and the wrong SYMBOL — it belongs on the mapper class or on
        ///         one of its mapping methods, both of which read it.
        ///     </para>
        ///     <para>
        ///         Not called on the separate-emit path. There the annotated type is a mapped type, its members
        ///         ARE read (<c>ReadCoLocatedHostDirectives</c>, A4), and misplacement there is <c>DWARF089</c>'s
        ///         question rather than this one. Reporting both would name one mistake twice.
        ///     </para>
        /// </summary>
        private static void ReportDirectivesOnMapperMembers(
            INamedTypeSymbol classSymbol,
            List<DiagnosticInfo> diagnostics)
        {
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is not IPropertySymbol { IsIndexer: false } && member is not IFieldSymbol { IsImplicitlyDeclared: false })
                {
                    continue;
                }

                foreach (var attr in member.GetAttributes())
                {
                    var cls = attr.AttributeClass?.ToDisplayString();
                    var isProperty = cls == KnownNames.MapPropertyFqn;
                    var isIgnore = cls == KnownNames.MapIgnoreFqn;
                    if (!isProperty && !isIgnore)
                    {
                        continue;
                    }

                    // The member-placement overloads are [MapProperty("source")] (arity 1) and [MapIgnore] (arity
                    // 0); everything else on these two attributes is a class/method form.
                    var memberForm = isProperty
                        ? attr.ConstructorArguments.Length == 1
                        : attr.ConstructorArguments.Length == 0;
                    var written = isProperty ? "[MapProperty]" : "[MapIgnore]";

                    var message =
                        $"{written} on '{classSymbol.Name}.{member.Name}' is read by nothing. This member belongs " +
                        "to the MAPPER, not to either type of any pair it maps, so no resolution step ever looks " +
                        "at it — the directive is inert and the mapping is exactly what it would be without it. " +
                        (memberForm
                            ? "This is the MEMBER-placement overload, where THE ANNOTATED MEMBER is the thing " +
                              "named: it belongs on a member of a [MapTo] source or a [GenerateMap] host, where " +
                              "the annotated type declares its own mapping. To say it here, name the " +
                              "destination and move it to the mapper class or to a mapping method: " +
                              (isProperty
                                  ? "[MapProperty(\"<source>\", \"<destination>\")]."
                                  : "[MapIgnore(\"<destination>\")].")
                            : "This overload IS the one the mapper reads — but only from the mapper CLASS or from " + "one of its mapping METHODS. Move it to either.");

                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.MemberFormDirectiveOnMapper,
                        member.Locations.FirstOrDefault() is { } loc ? LocationInfo.From(loc) : null,
                        message));
                }
            }
        }

        private static List<(string Source, string Target, string? Use)> ReadExplicitMaps(ISymbol method)
        {
            var maps = new List<(string Source, string Target, string? Use)>();
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapPropertyFqn)
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length == 2 && attr.ConstructorArguments[0].Value is string s && attr.ConstructorArguments[1].Value is string t)
                {
                    string? use = null;
                    foreach (var na in attr.NamedArguments)
                        if (na.Key == "Use" && na.Value.Value is string u)
                        {
                            use = u;
                        }

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
                if (attr.AttributeClass?.ToDisplayString() != KnownNames.MapValueFqn)
                {
                    continue;
                }

                if (attr.ConstructorArguments.Length == 0 || attr.ConstructorArguments[0].Value is not string target)
                {
                    continue;
                }

                string? use = null;
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Use" && na.Value.Value is string u)
                    {
                        use = u;
                    }

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
        /// <summary>
        ///     Renders a non-failing constant as a C# literal. Callers that can fail on assignability must
        ///     validate BEFORE calling (the MapConfig path is pre-validated by the compiler via the generic member type).
        /// </summary>
        private static string RenderConstantLiteral(object? value, ITypeSymbol? valueType, ITypeSymbol targetType, Compilation compilation)
        {
            if (value is null)
            {
                return "null";
            }

            // Roslyn 5.0 annotated SymbolDisplay.FormatPrimitive as returning string? — it answers null for a
            // value it does not recognise as a primitive. `value is null` is already handled above, so reaching
            // null here means "not a constant this renderer can spell". Falling back to the `null` literal keeps
            // the emitted code compilable and matches the early return; callers that can FAIL on assignability
            // validate before calling (see the summary), so this is the non-failing path by construction.
            if (valueType is { TypeKind: TypeKind.Enum })
            {
                var enumFqn = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var enumValue = SymbolDisplay.FormatPrimitive(value, false, false) ?? "null";
                return $"({enumFqn})({enumValue})";
            }

            var formatted = SymbolDisplay.FormatPrimitive(value, true, false) ?? "null";
            return targetType.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                ? $"({targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({formatted})"
                : formatted;
        }

        private static bool TryFormatConstant(
            TypedConstant tc,
            ITypeSymbol targetType,
            Compilation compilation,
            out string literal,
            out string why)
        {
            literal = "";
            why = "";
            if (!IsRenderableConstant(tc))
            {
                why =
                    $"[MapValue] constant for '{targetType.ToDisplayString()}' must be a string, bool, char, numeric, enum, or null";
                return false;
            }

            if (tc.IsNull)
            {
                var acceptsNull = targetType.IsReferenceType ||
                                  (targetType is INamedTypeSymbol nt &&
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

        /// <summary>
        ///     The source side of the completeness gate (<c>RequiredMapping = Both</c>): every readable source
        ///     member must be read by something, or it surfaces DWARF039.
        ///     <para>
        ///         Extracted so update-into can share it. It lived inline in the create-map branch, which meant
        ///         `RequiredMapping = Both` reported unconsumed source members through .Map and silently reported
        ///         nothing through .Update on the same mapper — the option was accepted and discarded.
        ///     </para>
        /// </summary>
        /// <summary>
        ///     Decide whether the method (or <c>[GenerateMap]</c> pair) that has just been resolved must be
        ///     WITHHELD, and confine its refusal to itself when it can be confined. The Map-endpoint twin of
        ///     <c>TryScopeProjectionRefusalToItsMethod</c>, sharing its machinery.
        /// </summary>
        /// <returns>True when the caller must NOT add this method's model.</returns>
        /// <remarks>
        ///     <para>
        ///         <b>Ruling (TASKS.md I17): DWARF001 is PER-METHOD.</b> Completeness is evaluated over one
        ///         (source, target) pair and one method-level <c>[MapIgnore]</c> set, and the diagnostic's own
        ///         remedy names the method — <i>"annotate the method with [MapIgnore(…)]"</i>. Its unit of
        ///         evaluation and its unit of remedy are both the method, so an unmapped destination member on
        ///         <c>MapBad</c> is not a statement about <c>MapGood</c> beside it. That is exactly the test I14
        ///         used the other way round to keep DWARF010 and DWARF008 class-level: those describe the SOURCE
        ///         MODEL and are equally true of every method over the same pair.
        ///     </para>
        ///     <para>
        ///         Only DWARF001 is scoped here, and only when it is the <em>sole</em> kind of error the method
        ///         raised — a range that also holds a DWARF010 keeps the whole-class kill it has always had.
        ///     </para>
        ///     <para>
        ///         <b>A synthesized pair's incompleteness stays class-level, and not by accident of indices.</b>
        ///         Nested and element pairs are resolved in the drain AFTER this loop, so their DWARF001 falls
        ///         outside every method range and is never scoped. That is also the right answer on the merits,
        ///         for the reason DWARF090 already records: a synthesized mapper is SHARED by every route that
        ///         reaches the pair, so its incompleteness is true of each of them and attributing it to one
        ///         method would be wrong rather than merely difficult. A span map and an async-stream map map
        ///         their element pair that way, which is why an incomplete element pair kills their class.
        ///     </para>
        /// </remarks>
        private static bool TryScopeCompletenessRefusalToItsMethod(
            List<DiagnosticInfo> diagnostics,
            int start,
            string methodName,
            LocationInfo? location)
        {
            return TryScopeMethodRefusal(
                diagnostics,
                start,
                methodName,
                location,
                DiagnosticDescriptors.MappingMethodNotGenerated,
                DiagnosticDescriptors.UnmappedMember);
        }

        /// <summary>
        ///     The machinery both scopers share: confine one method's errors to that method when every error it
        ///     raised is of a kind that describes the METHOD rather than the mapper, and signpost the single
        ///     <c>CS8795</c> that follows.
        /// </summary>
        /// <param name="diagnostics">
        ///     The class's diagnostic list; entries from <paramref name="start" /> on
        ///     belong to this one method.
        /// </param>
        /// <param name="start">The list's length when this method's resolution began.</param>
        /// <param name="methodName">
        ///     The mapping method's name — the one argument <paramref name="signpost" />'s message carries.
        /// </param>
        /// <param name="location">The method's own declaration site, where that signpost is reported.</param>
        /// <param name="signpost">The per-method twin of DWARF078 to report (a Warning).</param>
        /// <param name="scopable">
        ///     The error kinds that may be confined. Every error in the range must be one
        ///     of them, or the range is left alone and the class dies as before.
        /// </param>
        /// <returns>True when the caller must NOT add this method's model.</returns>
        /// <remarks>
        ///     The method is withheld either way. A method whose members did not all resolve has no honest
        ///     body: emitting it with the members that DID resolve would silently drop the rest, which is the
        ///     failure mode the completeness gate exists to refuse.
        /// </remarks>
        private static bool TryScopeMethodRefusal(
            List<DiagnosticInfo> diagnostics,
            int start,
            string methodName,
            LocationInfo? location,
            DiagnosticDescriptor signpost,
            params DiagnosticDescriptor[] scopable)
        {
            var sawError = false;
            var allAreScopable = true;

            for (var i = start; i < diagnostics.Count; i++)
            {
                var d = diagnostics[i];
                if (!d.IsError || d.ScopedToMethod)
                {
                    continue;
                }

                sawError = true;

                var isScopable = false;
                foreach (var s in scopable)
                    if (ReferenceEquals(d.Descriptor, s))
                    {
                        isScopable = true;
                        break;
                    }

                if (!isScopable)
                {
                    allAreScopable = false;
                }
            }

            if (!sawError)
            {
                return false;
            }

            if (!allAreScopable)
            {
                return true; // withheld, but the class still dies on the other error
            }

            for (var i = start; i < diagnostics.Count; i++)
                if (diagnostics[i].IsError)
                {
                    diagnostics[i] = diagnostics[i] with
                    {
                        ScopedToMethod = true
                    };
                }

            // The per-method twin of DWARF078, at the method's own location: exactly one CS8795 follows, and it
            // needs the same "this is a cascade, not a missing analyzer reference" signpost the class-wide wall
            // has always had.
            diagnostics.Add(new DiagnosticInfo(signpost, location, methodName));
            return true;
        }

        private static void EmitSourceCoverage(
            ITypeSymbol sourceType,
            IEnumerable<MemberMap> members,
            IEnumerable<MemberMap>? ctorArgs,
            IEnumerable<string> classIgnoreSources,
            IEnumerable<string> methodIgnoreSources,
            bool ignoreObsolete,
            Compilation compilation,
            bool allowNonPublic,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var ignoreSources = new HashSet<string>(classIgnoreSources, StringComparer.Ordinal);
            foreach (var s in methodIgnoreSources)
                ignoreSources.Add(s);
            // IgnoreObsoleteMembers, source side: an obsolete source member need not be consumed — you are
            // retiring it, not required to keep reading it — so it does not surface DWARF039.
            if (ignoreObsolete)
            {
                foreach (var s in ObsoleteMemberNames(sourceType))
                    ignoreSources.Add(s);
            }

            var consumed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in members)
                AddConsumed(consumed, m.SourceName);
            if (ctorArgs is not null)
            {
                foreach (var m in ctorArgs)
                    AddConsumed(consumed, m.SourceName);
            }

            ReportUnconsumed(sourceType, consumed, ignoreSources, compilation, allowNonPublic, location, diagnostics);
        }

        /// <summary>
        ///     Same gate, for a caller that already knows which source members it read. Projection resolves
        ///     expressions rather than <see cref="MemberMap" />s, so it tracks the names directly.
        /// </summary>
        private static void EmitSourceCoverageFromConsumed(
            ITypeSymbol sourceType,
            HashSet<string> consumed,
            IEnumerable<string> classIgnoreSources,
            IEnumerable<string> methodIgnoreSources,
            bool ignoreObsolete,
            Compilation compilation,
            bool allowNonPublic,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var ignoreSources = new HashSet<string>(classIgnoreSources, StringComparer.Ordinal);
            foreach (var s in methodIgnoreSources)
                ignoreSources.Add(s);
            if (ignoreObsolete)
            {
                foreach (var s in ObsoleteMemberNames(sourceType))
                    ignoreSources.Add(s);
            }

            ReportUnconsumed(sourceType, consumed, ignoreSources, compilation, allowNonPublic, location, diagnostics);
        }

        private static void ReportUnconsumed(
            ITypeSymbol sourceType,
            HashSet<string> consumed,
            HashSet<string> ignoreSources,
            Compilation compilation,
            bool allowNonPublic,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            foreach (var (name, _) in ReadableMembers(sourceType, compilation, allowNonPublic))
                if (!consumed.Contains(name) && !ignoreSources.Contains(name))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.UnconsumedSourceMember,
                        location,
                        name));
                }
        }

        /// <summary>
        ///     Which of the five mapping endpoints a gate is speaking about.
        ///     <para>
        ///         Introduced so <see cref="ReportDirectivesNotReadHere" /> can be called from ALL FIVE branches
        ///         and decide for itself what each one is: the endpoint's reader-facing name and whether it
        ///         adopts a sibling create map were two separate parameters passed by hand at every call site,
        ///         and both are functions of the endpoint alone. A per-call-site copy of a derivable fact is how
        ///         a sixth endpoint gets one of the two wrong.
        ///     </para>
        /// </summary>
        private enum MapEndpointKind
        {
            CreateMap,
            UpdateInto,
            Projection,
            SpanMap,
            AsyncStream
        }
    }
}
