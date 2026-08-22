// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

public static class DiagnosticDescriptors
{
    private const string Category = "DwarfMapper";

    // Every diagnostic links to its own anchor in the diagnostics reference so the IDE
    // "learn more" lightbulb lands on a concrete explanation + fix. Anchor is the lower-cased id,
    // e.g. DWARF001 -> .../docs/diagnostics.md#dwarf001. Concatenation stays a compile-time
    // constant so the descriptor id remains a literal (satisfies analyzer rule RS1017).
    private const string HelpBase =
        "https://github.com/GimliCZ/DwarfMapper.NET/blob/master/docs/diagnostics.md#";

    public static readonly DiagnosticDescriptor MapperNotPartial = new(
        "DWARF002",
        "Mapper type must be partial",
        "Mapper type '{0}' must be declared 'partial'",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf002");

    public static readonly DiagnosticDescriptor InvalidMapMethod = new(
        "DWARF003",
        "Invalid mapping method signature",
        "Mapping method '{0}' must be a partial instance method with a non-void return type and exactly one parameter",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf003");

    public static readonly DiagnosticDescriptor UnmappedMember = new(
        "DWARF001",
        "Destination member is not mapped",
        "Destination member '{0}' has no matching source member; map it or annotate the method with [MapIgnore(\"{0}\")]",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf001");

    public static readonly DiagnosticDescriptor NoImplicitConversion = new(
        "DWARF005",
        "No implicit conversion between mapped members",
        "Cannot map to '{0}': no implicit conversion and no usable conversion method; declare a mapping method for the types or use [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf005");

    // DWARF006 removed: superseded by DWARF026 (NoMappableConstructor). Reserved but no descriptor.

    public static readonly DiagnosticDescriptor ReadOnlyDestinationMember = new(
        "DWARF007",
        "Destination member is read-only",
        "Destination member '{0}' is read-only; a matching source value cannot be assigned and would be lost (annotate the method with [MapIgnore(\"{0}\")] if intentional)",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf007");

    public static readonly DiagnosticDescriptor AmbiguousMatch = new(
        "DWARF010",
        "Ambiguous source member",
        "Destination member '{0}' matches more than one source member under case-insensitive matching; rename or use [MapProperty]",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf010");

    public static readonly DiagnosticDescriptor MapPropertyUnknownTarget = new(
        "DWARF008",
        "MapProperty target not found",
        "[MapProperty] destination member '{0}' does not exist or is not writable",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf008");

    public static readonly DiagnosticDescriptor MapPropertyUnknownSource = new(
        "DWARF009",
        "MapProperty source not found",
        "[MapProperty] source member '{0}' does not exist or is not readable",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf009");

    public static readonly DiagnosticDescriptor DuplicateMapProperty = new(
        "DWARF011",
        "Duplicate explicit mapping",
        "Destination member '{0}' has more than one [MapProperty] mapping; keep only one",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf011");

    public static readonly DiagnosticDescriptor IgnoreExplicitConflict = new(
        "DWARF012",
        "Conflicting [MapIgnore] and [MapProperty]",
        "Destination member '{0}' is both ignored via [MapIgnore] and mapped via [MapProperty]; remove one",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf012");

    public static readonly DiagnosticDescriptor AmbiguousConversion = new(
        "DWARF013",
        "Ambiguous conversion method",
        "Cannot map to '{0}': more than one mapping method converts these types; disambiguate with [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf013");

    public static readonly DiagnosticDescriptor UseMethodInvalid = new(
        "DWARF014",
        "Conversion method not found",
        "[MapProperty(Use = ...)] method '{0}' was not found or has an incompatible signature (it must take the source member type and return the destination member type)",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf014");

    public static readonly DiagnosticDescriptor IncompleteEnumMapping = new(
        "DWARF015",
        "Incomplete enum mapping",
        "Enum member '{0}' has no destination member of the same name (by-name enum mapping); add it or use EnumStrategy.ByValue",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf015");

    public static readonly DiagnosticDescriptor FlattenRootInvalid = new(
        "DWARF016",
        "Invalid flatten source",
        "[Flatten] source member '{0}' does not exist, is not readable, or has no readable sub-members",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf016");

    public static readonly DiagnosticDescriptor AmbiguousFlatten = new(
        "DWARF017",
        "Ambiguous flattened member",
        "Destination member '{0}' is flattened from more than one source member; use [MapProperty] to disambiguate",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf017");

    public static readonly DiagnosticDescriptor InvalidHook = new(
        "DWARF018",
        "Invalid mapping hook signature",
        "Hook method '{0}' must be void; [BeforeMap] takes one parameter, [AfterMap] takes one or two",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf018");

    // DWARF019 (NotProjectable) — retired; superseded by DWARF028 (ProjectionNotTranslatable),
    // which carries a specific reason for every non-translatable projection member. Do not reuse.

    public static readonly DiagnosticDescriptor RoundTripNoInverse = new(
        "DWARF020",
        "No inverse for [RoundTrip]",
        "[RoundTrip] method '{0}' has no inverse mapping method (a partial method with the source/destination types swapped)",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf020");

    public static readonly DiagnosticDescriptor RoundTripAmbiguousInverse = new(
        "DWARF021",
        "Ambiguous inverse for [RoundTrip]",
        "[RoundTrip] method '{0}' has more than one candidate inverse mapping method",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf021");

    // Detoxed: spell out what "unmanaged array-to-array" means in user terms; carry the deeper
    // "why" in the description + help link rather than in the one-line message.
    public static readonly DiagnosticDescriptor ReinterpretInvalid = new(
        "DWARF022",
        "Invalid [Reinterpret] target",
        "[Reinterpret] member '{0}' must map an array to an array of an unmanaged (blittable) element type — for example int[] → int[]. Arrays of reference types, or of structs that contain references, can't be reinterpreted.",
        Category, DiagnosticSeverity.Error, true,
        "[Reinterpret] forces the blittable bulk-copy fast-path, which reinterprets one array's memory as another in a single block copy. That is only sound when both element types are 'unmanaged' (no managed references, fixed layout) and the same size. When the proof can't be satisfied the generator falls back to the safe element-by-element copy, so removing [Reinterpret] is always a valid fix.",
        HelpBase + "dwarf022");

    public static readonly DiagnosticDescriptor AfterMapValueTargetByValue = new(
        "DWARF023",
        "[AfterMap] value-type target must be passed by ref",
        "[AfterMap] on a value-type target '{0}' must take the target parameter by 'ref', or its changes are lost",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf023");

    public static readonly DiagnosticDescriptor ConstructorParameterUnmapped = new(
        "DWARF024",
        "Constructor parameter has no mappable source member",
        "Constructor parameter '{0}' has no mappable source member; add a source member with a matching name or use [MapProperty(src, \"<paramName>\")]",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf024");

    public static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        "DWARF025",
        "Ambiguous constructor",
        "Destination type '{0}' has an ambiguous constructor selection: either multiple constructors tie on the maximum parameter count, or more than one constructor is annotated with [DwarfMapperConstructor]. Annotate exactly one constructor with [DwarfMapperConstructor] (and remove any duplicate annotation).",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf025");

    public static readonly DiagnosticDescriptor NoMappableConstructor = new(
        "DWARF026",
        "No mappable constructor",
        "Destination type '{0}' has no accessible non-obsolete instance constructor to use for mapping",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf026");

    public static readonly DiagnosticDescriptor UnsupportedCollectionTarget = new(
        "DWARF027",
        "Unsupported collection/dictionary target type",
        "Cannot map to '{0}': unsupported collection or dictionary target type. Use [MapProperty(Use = ...)] to supply a custom converter, or map it manually.",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf027");

    // Detoxed title: "not translatable" is IQueryable/ORM jargon. The specific per-member reason is
    // supplied at the call site (the {0} message); the description explains the concept generally.
    public static readonly DiagnosticDescriptor ProjectionNotTranslatable = new(
        "DWARF028",
        "Projection member cannot be translated to a database query",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        "An IQueryable projection (a Project method) becomes an expression tree that your database/ORM provider translates into a query. A member that needs a runtime conversion, a custom converter, a collection rebuild, or reference handling has no equivalent the provider can translate. Map those members with a runtime mapper (an ordinary Map method) instead of Project.",
        HelpBase + "dwarf028");

    // DWARF029 reserved.

    // Detoxed: dropped the internal terms "register-before-populate" and "cyclic back-edge" from the
    // user-facing message; the mechanism now lives in the description.
    public static readonly DiagnosticDescriptor CyclicConstructorParameter = new(
        "DWARF030",
        "Constructor parameter is part of a reference cycle",
        "Member '{0}' is set through a constructor parameter or init-only property, but it takes part in a reference cycle under ReferenceHandling=Preserve. A cycle can only be reconstructed when the looping member is assigned after the object is created — make '{0}' a settable property, or break the cycle.",
        Category, DiagnosticSeverity.Error, true,
        "Under ReferenceHandling=Preserve the mapper records each object before filling its members, so a cycle can point back to the object already in progress. A member supplied through a constructor argument or an init-only property is fixed at construction time — before the object can be recorded — so a cycle routed through it cannot be closed. Map that member via a settable property instead, or remove the cycle.",
        HelpBase + "dwarf030");

    // Lightly reworded from "Auto-synthesized nested mapper depth limit" internal phrasing.
    public static readonly DiagnosticDescriptor DeepNestingLimit = new(
        "DWARF031",
        "Mapping nests too deeply",
        "Mapping nests too deeply: the generator reached its limit of 512 synthesized nested mappers while processing '{0}'. Declare explicit mapping methods for deeply-nested types to bound the depth.",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf031");

    // Detoxed: removed "DwarfRefContext", "thread ... into it", "back-edges closed", "topology
    // fidelity". States the user-visible effect and the two real choices; mechanism in description.
    public static readonly DiagnosticDescriptor ReferenceHandlingUseConverter = new(
        "DWARF032",
        "Custom converter can't preserve reference identity",
        "Member '{0}' is produced by a [MapProperty(Use = ...)] converter, so under ReferenceHandling=Preserve the mapper can't track its identity: the result won't be shared with other references to the same object, and a cycle passing through it won't be reconnected. Map it without a custom converter to keep identity, or keep the converter if a duplicated (non-shared) value is acceptable.",
        Category, DiagnosticSeverity.Error, true,
        "Reference preservation works by threading an identity map through the generated mapping code. A user-supplied converter is opaque to the generator, so the identity map can't be passed into it and its output is treated as a fresh object each time. This only changes behaviour when the same object is referenced from more than one place or takes part in a cycle.",
        HelpBase + "dwarf032");

    public static readonly DiagnosticDescriptor AbstractSourceAutoNest = new(
        "DWARF033",
        "Abstract or interface source type in auto-nested mapping",
        "Source type '{0}' is abstract or an interface; auto-nested mapping only maps declared members " +
        "and silently drops members that exist only on derived runtime types. Declare an explicit mapper, " +
        "use [MapIgnore] to suppress, or make the source type concrete.",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf033");

    public static readonly DiagnosticDescriptor InvalidFlattenGraph = new(
        "DWARF034",
        "Invalid [FlattenGraph] configuration",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf034");

    public static readonly DiagnosticDescriptor InvalidMapDerivedType = new(
        "DWARF035",
        "Invalid [MapDerivedType] configuration",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf035");

    public static readonly DiagnosticDescriptor AmbiguousDerivedType = new(
        "DWARF036",
        "Ambiguous [MapDerivedType] dispatch arms",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf036");

    public static readonly DiagnosticDescriptor OnCycleIgnoredUnderPreserve = new(
        "DWARF037",
        "OnCycle is ignored under ReferenceHandling.Preserve",
        "Mapper '{0}' sets OnCycle = SetNull together with ReferenceHandling = Preserve; "
        + "OnCycle only applies in None mode (Preserve reconstructs cycles faithfully), so the setting has no effect",
        Category, DiagnosticSeverity.Warning, true,
        helpLinkUri: HelpBase + "dwarf037");

    // Severity is dynamic: Info (suggestion) by default; escalated to Error per-instance (via
    // DiagnosticInfo.SeverityOverride) when [DwarfMapper(ImplicitConversions = false)] is set.
    public static readonly DiagnosticDescriptor ImplicitConversionApplied = new(
        "DWARF038",
        "Implicit type conversion applied",
        "{0}",
        Category, DiagnosticSeverity.Info, true,
        helpLinkUri: HelpBase + "dwarf038");

    // Emitted only under [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]. Info (suggestion)
    // by default — visible, never silent, never build-breaking; escalate via
    // dotnet_diagnostic.DWARF039.severity = error in .editorconfig.
    public static readonly DiagnosticDescriptor UnconsumedSourceMember = new(
        "DWARF039",
        "Source member is read by no destination member",
        "Source member '{0}' is read by no destination member; map it, or annotate the mapper/method with [MapIgnoreSource(\"{0}\")]",
        Category, DiagnosticSeverity.Info, true,
        helpLinkUri: HelpBase + "dwarf039");

    public static readonly DiagnosticDescriptor MapValueTypeMismatch = new(
        "DWARF040",
        "Constant [MapValue] is not assignable to the destination",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf040");

    public static readonly DiagnosticDescriptor MapValueUseInvalid = new(
        "DWARF041",
        "[MapValue(Use=)] provider method is invalid",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf041");

    public static readonly DiagnosticDescriptor MapValueInvalid = new(
        "DWARF042",
        "Conflicting or invalid [MapValue]",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf042");

    public static readonly DiagnosticDescriptor PathSegmentNotFound = new(
        "DWARF043",
        "[MapProperty] source path segment not found",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf043");

    // Warning (item 8): a nullable reference on the interior of a source path can throw
    // NullReferenceException at runtime when dereferenced — a data-integrity hazard, so it is a Warning
    // (consistent with the other runtime-throwing diagnostics), not a mere suggestion. Downgrade via
    // dotnet_diagnostic.DWARF044.severity = suggestion (or none) in .editorconfig if intentional.
    public static readonly DiagnosticDescriptor PathNullableHop = new(
        "DWARF044",
        "[MapProperty] source path traverses a nullable member",
        "{0}",
        Category, DiagnosticSeverity.Warning, true,
        helpLinkUri: HelpBase + "dwarf044");

    public static readonly DiagnosticDescriptor UnflattenInvalid = new(
        "DWARF045",
        "Invalid [MapProperty] unflatten target path",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf045");

    public static readonly DiagnosticDescriptor UnflattenConflict = new(
        "DWARF046",
        "Conflicting [MapProperty] unflatten target",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf046");

    // Info: an additional mapping parameter matched no destination member. Suppress via
    // dotnet_diagnostic.DWARF047.severity = none in .editorconfig.
    public static readonly DiagnosticDescriptor UnusedMappingParameter = new(
        "DWARF047",
        "Additional mapping parameter is unused",
        "{0}",
        Category, DiagnosticSeverity.Info, true,
        helpLinkUri: HelpBase + "dwarf047");

    public static readonly DiagnosticDescriptor AmbiguousNormalizedMatch = new(
        "DWARF048",
        "Ambiguous member match under NameConvention.Flexible",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf048");

    public static readonly DiagnosticDescriptor NullSubstituteInvalid = new(
        "DWARF049",
        "Invalid [MapProperty(NullSubstitute=)]",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf049");

    public static readonly DiagnosticDescriptor WhenPredicateInvalid = new(
        "DWARF050",
        "Invalid [MapProperty(When=)] predicate",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf050");

    // Warning: a forward [MapProperty] could not be auto-inverted for [ReverseMap]; declare the reverse
    // rename explicitly. Suppress via dotnet_diagnostic.DWARF051.severity = none in .editorconfig.
    public static readonly DiagnosticDescriptor ReverseMapNonInvertible = new(
        "DWARF051",
        "[ReverseMap] cannot auto-invert this configuration",
        "{0}",
        Category, DiagnosticSeverity.Warning, true,
        helpLinkUri: HelpBase + "dwarf051");

    public static readonly DiagnosticDescriptor ReverseMapTargetMissing = new(
        "DWARF052",
        "[ReverseMap] has no inverse mapping method",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf052");

    public static readonly DiagnosticDescriptor GenericMapperMethodUnsupported = new(
        "DWARF053",
        "Generic mapping methods are not supported",
        "Generic mapping methods are not supported: '{0}' declares type parameter(s); use a closed [GenerateMap<,>] or a non-generic partial method",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf053");

    // Pathway-neutral: covers both a generic [DwarfMapper] class and a generic co-located [GenerateMap<>] host.
    public static readonly DiagnosticDescriptor GenericMapperClassUnsupported = new(
        "DWARF054",
        "Mapping is not supported on a generic class",
        "Mapping generation is not supported on a generic class: '{0}' is generic; declare it on a non-generic class",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf054");

    // Info: a single mapper resolving a very large number of members. All extraction runs in the syntax
    // transform, so an enormous mapper can add IDE/compile latency. High threshold → fires only for genuine
    // god-mappers; suppressible via dotnet_diagnostic.DWARF055.severity = none.
    public static readonly DiagnosticDescriptor MapperTooLarge = new(
        "DWARF055",
        "Mapper is very large; consider splitting it",
        "{0}",
        Category, DiagnosticSeverity.Info, true,
        helpLinkUri: HelpBase + "dwarf055");

    // Warning (not Error): the configuration compiles, but a pair-scoped member attribute
    // ([MapProperty<S,T>] / [MapIgnore<T>] / [MapValue<T>]) matched no mapped pair, so it silently does
    // nothing — almost always a typo'd type argument or a missing [GenerateMap]. Surfaced rather than ignored.
    public static readonly DiagnosticDescriptor PairScopedNoMatch = new(
        "DWARF056",
        "Pair-scoped attribute matches no mapped pair",
        "{0}",
        Category, DiagnosticSeverity.Warning, true,
        helpLinkUri: HelpBase + "dwarf056");

    // Error: a co-located [GenerateMap<>] host would emit a generated mapper named '<Host>Mapper', but a type
    // with that name already exists. Emitting it would either collide (CS0260/CS0101) or silently merge into the
    // user's partial; if it matched another generated mapper's hint name it would abort all generation. Reported
    // instead of emitted, so the user gets a clear message rather than an opaque downstream failure.
    public static readonly DiagnosticDescriptor CoLocatedMapperNameCollision = new(
        "DWARF057",
        "Generated mapper name collides with an existing type",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf057");

    // Info: two or more mappers would produce the same convenience extension signature, so it was NOT
    // generated for either (the instance methods still work). Surfaced rather than silently dropped.
    public static readonly DiagnosticDescriptor DuplicateFacadeExtension = new(
        "DWARF058",
        "Convenience extension method was not generated (ambiguous)",
        "{0}",
        Category, DiagnosticSeverity.Info, true,
        helpLinkUri: HelpBase + "dwarf058");

    // Error: [MapConstructor<S,T>(method)] names a factory that does not exist or whose signature is
    // incompatible (it must take a value assignable from the source and return one assignable to the target).
    public static readonly DiagnosticDescriptor MapConstructorInvalid = new(
        "DWARF059",
        "Constructor factory method not found",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        helpLinkUri: HelpBase + "dwarf059");

    // Two maps from the SAME source type to DIFFERENT targets would both emit `T Map(S)` — methods that
    // differ only by return type, which C# forbids (CS0111). Without this the consumer sees a raw CS0111
    // inside generated code with no DwarfMapper guidance. The full, formatted message is built at the call
    // site ({0}); the mechanism (overloading is by parameter type) lives in the description.
    public static readonly DiagnosticDescriptor ConflictingMapSignature = new(
        "DWARF060",
        "Conflicting map methods from the same source type",
        "{0}",
        Category, DiagnosticSeverity.Error, true,
        "DwarfMapper names a generated mapping method `Map` and overloads it by the SOURCE (parameter) type. Two pairs that share a source type but target different destinations therefore collide on the same signature `Map(Source)` and would differ only by return type, which the C# compiler rejects. Declare one of them as a distinctly-named partial method (`public partial Target Name(Source s);`) — its pair-scoped [MapProperty]/[MapIgnore] attributes still apply.",
        HelpBase + "dwarf060");

    // Whole-graph linkage check at the validation root: a map consumed through IDwarfMapper that no
    // referenced assembly (nor this one) provides. Turns the otherwise-runtime DwarfMapMissingException into a
    // compile-time error. Args: {0}=source, {1}=destination.
    public static readonly DiagnosticDescriptor RequiredMapNotProvided = new(
        "DWARF061",
        "Required ambient map is not provided",
        "Map '{0}' -> '{1}' is consumed through IDwarfMapper but no referenced assembly provides it. Declare [GenerateMap<{0}, {1}>] in a referenced assembly (it self-registers into the ambient registry), or reference the assembly that already declares it.",
        Category, DiagnosticSeverity.Error, true,
        "The assembly marked [assembly: DwarfMapperValidationRoot] sees the whole reference graph, so DwarfMapper verifies there that every ambient map consumed through IDwarfMapper (auto-detected from Map<TDest>(src) call sites or declared with [UsesMap]) is provided by some assembly in the graph. A pair with no provider would otherwise throw DwarfMapMissingException at runtime; this reports it at build time instead.",
        HelpBase + "dwarf061");

    // A mapper with constructor dependencies cannot be instantiated by the parameterless module-initializer
    // self-registration, so its (public-typed) maps are NOT placed in the ambient cross-assembly registry.
    // Info severity: this is a heads-up, not an error — the mapper still works via direct injection.
    public static readonly DiagnosticDescriptor AmbientMapperNotRegistered = new(
        "DWARF062",
        "Mapper not added to the ambient registry",
        "Mapper '{0}' has constructor dependencies, so its maps are not self-registered in the ambient DwarfMapper registry. Inject it directly, or give it a parameterless constructor to make its maps resolvable through IDwarfMapper.",
        Category, DiagnosticSeverity.Info, true,
        "The ambient registry is populated by generated module initializers, which run with no dependency-injection context and can only construct mappers that have an accessible parameterless constructor. A mapper that takes constructor dependencies (e.g. an object factory) is therefore left out of the ambient IDwarfMapper resolution; use the concrete mapper through DI instead.",
        HelpBase + "dwarf062");

    // Two assemblies in the graph provide an ambient map for the same (source, destination). The registry
    // keeps the first registration; the rest are ignored. Reported at the validation root. Args: {0}=source,
    // {1}=destination.
    public static readonly DiagnosticDescriptor AmbiguousAmbientProvider = new(
        "DWARF063",
        "Ambiguous ambient map provider",
        "More than one assembly provides an ambient map '{0}' -> '{1}'. The ambient registry keeps the first registration and ignores the others; ensure the duplicate definitions are intentional, or remove all but one.",
        Category, DiagnosticSeverity.Warning, true,
        helpLinkUri: HelpBase + "dwarf063");

    // Item 12: a [MapValue] supplies a constant/provider for a target that ALSO has a same-name readable
    // source member. The constant silently shadows the real source data — typically a leftover stub from
    // before the source member existed. Info (suppressible); only emitted when the shadow actually exists.
    public static readonly DiagnosticDescriptor MapValueShadowsSource = new(
        "DWARF064",
        "[MapValue] shadows an auto-matchable source member",
        "[MapValue] for '{0}' overrides the same-named source member '{0}', so the real source value is never read. Remove the [MapValue] to map the source member, or [MapIgnoreSource(\"{0}\")] if the shadow is intentional.",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf064");

    // Item 13: an update-into Map(S src, T dest) maps a nested reference member by REPLACING it with a
    // freshly-mapped instance, not by recursively updating dest's existing instance. Callers expecting a
    // deep merge (identity of the nested object preserved) are warned. Info (suppressible).
    public static readonly DiagnosticDescriptor UpdateIntoNestedReplaced = new(
        "DWARF065",
        "Update-into replaces a nested member instead of merging it",
        "Update-into maps nested member '{0}' by replacing it with a new instance; the destination's existing '{0}' object (and its identity) is discarded, not deep-merged. Map the leaf members directly, or accept the replacement.",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf065");

    // Item 14: a [MapProperty(When=)] guards a NON-nullable target member. When the predicate is false the
    // member keeps its (possibly type-default) value, which for a non-nullable reference can be a latent
    // null. Restricted to non-nullable targets; Info (suppressible) to limit false positives.
    public static readonly DiagnosticDescriptor WhenLeavesNonNullableDefault = new(
        "DWARF066",
        "[MapProperty(When=)] can leave a non-nullable member at its default",
        "[MapProperty(When=)] guards non-nullable target member '{0}': when the predicate is false the member is not assigned and keeps its default (a non-nullable reference default is null). Give the member a default initializer, make it nullable, or confirm the unset default is intended.",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf066");

    // Item 20: [GenerateWrapperMap(typeof(W<>))] requires a single-payload generic wrapper — a generic type
    // with exactly one type parameter and exactly one member of that parameter's type. A wrapper that is not
    // generic (arity != 1) or has zero / multiple payload members (e.g. a List<T> payload, which is not a
    // single non-collection payload) cannot be expanded; the attribute is skipped. Error (the opt-in is unusable).
    public static readonly DiagnosticDescriptor WrapperMapInvalid = new(
        "DWARF067",
        "[GenerateWrapperMap] wrapper is not a single-payload generic",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf067");

    public static readonly DiagnosticDescriptor MapConfigUnsupportedExpression = new(
        id: "DWARF068",
        title: "Unsupported MapConfig expression",
        // Single placeholder (mirrors DWARF067's WrapperMapInvalid) — DiagnosticInfo carries one MessageArg,
        // so the op-name and offending-expression text are combined by the caller into one formatted string.
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf068");

    public static readonly DiagnosticDescriptor MapConfigConflict = new(
        id: "DWARF069",
        title: "Conflicting member configuration",
        // Single placeholder (mirrors DWARF068's MapConfigUnsupportedExpression) — DiagnosticInfo carries one
        // MessageArg, so the caller builds the whole formatted string (member/type/reason all inline).
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf069");

    // A nullable-annotated REFERENCE source assigned to a non-nullable reference target. Per NullStrategy's
    // documented contract this raw-assigns (NullStrategy governs nullable VALUE types only), so a null lands
    // in a member whose type says it cannot be null. Two things were wrong with staying quiet about it:
    //
    //   * it is silent — the whole point of DwarfMapper is that a mapping decision the user did not make is a
    //     build-time diagnostic, not a runtime surprise; and
    //   * it was not even silent in practice: the raw assignment made the COMPILER emit CS8601 ("possible null
    //     reference assignment") from inside the generated file. A consumer with TreatWarningsAsErrors got a
    //     build break they could not fix, in code they cannot edit, with no mention of DwarfMapper.
    //
    // So the emitter now suppresses that CS8601 (the raw-assign is intentional) and DwarfMapper says it in its
    // own voice instead: actionable, pointing at the user's own DTO, and suppressible per-rule.
    //
    // Fires ONLY for Annotated -> NotAnnotated, exactly when CS8601 would. An oblivious (None) annotation on
    // either side means the user opted out of nullable analysis (#nullable disable), and the compiler stays
    // quiet there too — so we do as well, rather than flooding legacy code with warnings.
    // The polymorphic silent drop, concrete-base-class edition.
    //
    // DWARF033 already refuses an ABSTRACT or INTERFACE auto-nest source, on the grounds that it "silently
    // drops members that exist only on derived runtime types". A CONCRETE base class carries exactly the same
    // risk — a `Animal Pet` member can hold a `Dog` at run time, and the mapper, which can only see the
    // declared type, copies the Animal members and drops Breed — but it is perfectly instantiable, so it sails
    // straight through the DWARF033 gate. The caller gets a plausible object quietly missing data: precisely
    // the failure the "never silent" premise exists to prevent, and the one place where being a compile-time
    // mapper is a real disadvantage against a reflective one (which can dispatch on the runtime type).
    //
    // Info, not Error, and the distinction is principled: an abstract source is NECESSARILY a derived instance
    // at run time, so the drop is certain; a concrete base MAY genuinely hold exactly the base type, so this is
    // a risk rather than a defect, and mapping base-only is often exactly what the author intends. Per the
    // severity table that is an Info — "surfaces a footgun without forcing a change".
    //
    // Fires only when a derived type is actually present IN THE COMPILATION and no [MapDerivedType] arm covers
    // the pair; a non-sealed class nobody derives from raises nothing.
    public static readonly DiagnosticDescriptor PolymorphicSourceMayDropMembers = new(
        "DWARF071",
        "Source type has derived types whose members would be dropped",
        "Source member type '{0}' has derived types in this compilation. A mapper resolves members at compile "
        + "time from the DECLARED type, so if this member holds a derived instance at run time, the members "
        + "declared only on that derived type are dropped — silently, with no error. Add [MapDerivedType<TFrom, "
        + "TTo>] to dispatch on the runtime type, seal the source type, or accept that only the base members "
        + "are mapped.",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf071");

    // Trust-boundary / anti-over-posting guard. Under [DwarfMapper(AutoMatchMembers = false)] a destination
    // member has a same-named SOURCE member — it WOULD have auto-wired — but auto-matching is off, so the
    // generator refuses to wire it silently. This is the mass-assignment surface (OWASP API6) made visible:
    // by-name auto-matching is exactly how an attacker-controlled `IsAdmin` reaches a protected entity field,
    // and DWARF001 does NOT catch it because the field IS mapped. Distinct from DWARF001 (no source at all):
    // here a source EXISTS and the message tells the developer to make the over-post an explicit decision.
    // Error — it is the completeness gate applied at the trust boundary; the safe path (do nothing) leaves the
    // member unmapped and forces a visible [MapProperty]/[MapIgnore] choice.
    // [MapProperty(StringFormat = "...")] used where it cannot apply: the destination is not string, the source
    // is not IFormattable, or a Use= converter is also present (the converter already owns the transform).
    // [MapCollectionKey] used where the v1 key-based upsert cannot apply: the named member is not a mapped
    // destination member, is not a List<T> on both sides, the element type differs between source and target,
    // or the key member is not found on the element type. Loud rather than silently falling back to
    // whole-collection replacement.
    //
    // "Not an update-into method" was listed here for four rounds and NO call site ever implemented it:
    // ApplyCollectionKeyUpserts is reached from the update-into branch alone, so a [MapCollectionKey] written
    // anywhere else reached this id's checks not at all and was discarded in silence (finding D14). That case
    // now lives on DWARF092 — a Warning, because an Error here would suppress the whole class's emission and
    // bury the refusal under a wall of CS8795 — and is deliberately NOT this id's. Stated rather than deleted:
    // a descriptor comment that documents a check nobody wrote is how a gap reads as covered.
    // [FlattenGraph] could not flatten a data-bearing complex leaf (a nested object, collection or dictionary
    // member of a graph node). Only reachable under ReferenceHandling = Preserve, where the synthesized helper
    // for such a leaf may later be force-marked recursion-capable (3-param) and would then be called with one
    // argument from the flat-node helper. Outside Preserve the leaf IS flattened. Loud rather than leaving the
    // DTO member silently at its default — dropping data without a word is exactly what this library forbids.
    public static readonly DiagnosticDescriptor FlattenGraphLeafNotFlattened = new(
        "DWARF075",
        "[FlattenGraph] leaf member was not flattened",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf075");

    // A DECLARED create-map whose source and target are the same type. It compiles and emits a shallow copy,
    // and the completeness gate cannot object — a type trivially satisfies itself, so every destination member
    // resolves and the map is "complete". That silence is the problem: the overwhelmingly common cause is a
    // copy-paste slip ([GenerateMap<Dto, Dto>] where Entity was meant). Warning, not Error, because a shallow
    // clone is a legitimate thing to ask for; a warnings-as-errors build still fails, and a deliberate clone
    // suppresses the id. Update-into (T src, T dest) is exempt — refreshing an existing instance from another
    // of the same type is a real pattern — as are auto-synthesized nested pairs, which reflect the graph's
    // shape rather than anything the author typed and so offer nothing to fix.
    public static readonly DiagnosticDescriptor SelfMap = new(
        "DWARF076",
        "Source and target are the same type",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf076");

    // The explicit-only trust boundary ([DwarfMapper(AutoMatchMembers = false)]) guards the TOP-LEVEL pair of
    // a mapping method. Span and async-stream methods do not resolve members themselves — they map the ELEMENT
    // pair through an auto-synthesized mapper, and explicit-only deliberately does not propagate into
    // synthesized mappers (a synthesized mapper has no [MapProperty] to satisfy it, so propagating would make
    // nested objects unmappable). The consequence was that one class-level option guarded .Map and silently did
    // not guard .MapSpan on the same mapper: the developer's model is "this mapper class is explicit-only", and
    // it held at one endpoint and not the other. Refusing loudly is the only honest option — silently applying
    // half a trust boundary is how over-posting reaches production.
    public static readonly DiagnosticDescriptor ExplicitOnlyNotElementWise = new(
        "DWARF077",
        "Explicit-only mapping is not enforced element-wise",
        "Method '{0}' maps elements through an auto-synthesized mapper, which "
        + "[DwarfMapper(AutoMatchMembers = false)] does not guard, so the explicit-only trust boundary would "
        + "not apply to the element members. Declare a dedicated [DwarfMapper(AutoMatchMembers = false)] "
        + "mapper for the element pair and call it, or remove AutoMatchMembers = false from this mapper.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf077");

    /// <summary>
    ///     Signpost emitted when a mapper class is skipped because it has blocking errors.
    /// </summary>
    /// <remarks>
    ///     A generator error suppresses emission for the WHOLE class, so every partial mapping method on it
    ///     then has no implementing part and the build fills with <c>CS8795</c>. The real cause is one
    ///     <c>DWARF…</c> line further up, buried under its own cascade.
    ///     <para>
    ///         This cost real time in the field: a migration wasted a debugging session on "the analyzer isn't
    ///         wired" (the other, identical-looking cause of a CS8795 wall) when the actual signal was a
    ///         <c>DWARF007</c>/<c>DWARF026</c> above it. Naming the cascade removes the ambiguity between the
    ///         two, which no other diagnostic can do — a missing analyzer reference emits nothing at all.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor NoCodeGenerated = new(
        "DWARF078",
        "No code was generated for this mapper",
        "No code was generated for mapper '{0}' because it has unresolved DwarfMapper errors ({1}). Every "
        + "partial mapping method on it will now ALSO report CS8795 (\"must have an implementing part\") — "
        + "those are a cascade of this, not a separate problem, and they are not caused by a missing analyzer "
        + "reference. Fix the errors listed above.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf078");

    /// <summary>
    ///     <c>[MapIgnore]</c> on a <c>required</c> destination member, which cannot work.
    /// </summary>
    /// <remarks>
    ///     Ignoring the member omits it from the generated object initializer, and C# refuses that with
    ///     <c>CS9035</c> — reported against generated code the consumer never wrote, with nothing linking it
    ///     back to the attribute that caused it.
    ///     <para>
    ///         The most-repeated friction point of the Round-18 migration: three separate conversions hit it
    ///         independently and each reinvented the same workaround. It is common in migrating code
    ///         specifically because AutoMapper built targets reflectively and so bypassed the rule entirely —
    ///         <c>.Ignore()</c> on a <c>required</c> member simply left it null there.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor IgnoredRequiredMember = new(
        "DWARF079",
        "[MapIgnore] cannot ignore a required member",
        "Destination member '{0}' is `required`, so ignoring it would emit an object initializer that omits "
        + "it — which does not compile (CS9035). Supply a placeholder with [MapValue(\"{0}\", …)], map it, or "
        + "drop `required` from the member.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf079");

    /// <summary>
    ///     A <c>[MapConstructor]</c> factory owns construction, so an <c>init</c>-only or <c>required</c>
    ///     destination member cannot be assigned afterwards and silently keeps whatever the factory set.
    /// </summary>
    /// <remarks>
    ///     Reported only when a source member would actually have supplied a value, so the loss is real rather
    ///     than theoretical. Round 18 hit this twice in one codebase: an entity lost its <c>Identifier</c>
    ///     through an <c>.Empty</c> factory that minted a fresh <c>Guid</c>, and a second map "compiled green
    ///     but silently dropped Identifier, TotalArguments and IsCoreCommand" and had to be backed out.
    ///     <para>
    ///         Direct construction does not have this problem — it fills an object initializer, where
    ///         <c>init</c> members ARE assignable. Constructor-parameter binding is therefore the better
    ///         default; see <c>docs/options.md</c>.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor FactoryDropsMember = new(
        "DWARF080",
        "A [MapConstructor] factory cannot assign this member",
        "Destination member '{0}' is init-only or required, so the [MapConstructor] factory owns it and the "
        + "matching source value is discarded — '{0}' will hold whatever the factory set. Bind the "
        + "constructor parameters with [MapProperty] instead of naming a factory (init members are assignable "
        + "in the object initializer that produces), have the factory take '{0}' as a parameter, or silence "
        + "this with [MapIgnore(\"{0}\")] to state that the factory's value is intended.",
        // Info, not Warning: the generator cannot see inside the factory, so it cannot tell a factory that
        // deliberately supplies its own value from one that forgot. Both shapes are legitimate, and a Warning
        // would break every warnings-as-errors consumer using the first. Escalate with
        // dotnet_diagnostic.DWARF080.severity = warning where the stricter reading is wanted.
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf080");

    /// <summary>
    ///     <c>[RestatesBase]</c> names a pair whose base pair cannot be identified.
    /// </summary>
    /// <remarks>
    ///     Refused rather than skipped, for the same reason as <c>DWARF082</c>: the author asked for a check,
    ///     and a check that silently does not run is precisely the drift risk they were guarding against.
    /// </remarks>
    public static readonly DiagnosticDescriptor RestatesBaseUnresolved = new(
        "DWARF084",
        "[RestatesBase] cannot identify the base pair",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf084");

    /// <summary>
    ///     A pair declared with <c>[RestatesBase]</c> has drifted from the base pair it restates.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The whole point of the attribute. Restatement's cost splits into typing it — mechanical,
    ///         annoying, over once — and drifting from the base later, which is silent and only ever drifts
    ///         toward wrong data: the base pair gains a <c>Use=</c> converter and the derived one quietly keeps
    ///         mapping the raw value.
    ///     </para>
    ///     <para>
    ///         Compared on the RESOLVED mappings, not on attribute text. That is what catches a restatement
    ///         which is present but no longer does the same thing — the failure a marker-comment convention
    ///         cannot see.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor RestatedBaseDrift = new(
        "DWARF085",
        "Restated base configuration has drifted",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf085");

    /// <summary>
    ///     One logical nested pair, auto-synthesized into two mappers that do not agree.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A synthesized helper inherits the policy of the mapper that reached it, so two mappers reaching
    ///         the same <c>(S, T)</c> each get a private copy — and if their options differ, so do the copies.
    ///         Nothing in the build says so: both compile, both are correct in isolation, and the same two
    ///         types are mapped two different ways in one assembly.
    ///     </para>
    ///     <para>
    ///         Round 18 hit exactly this. <c>SkipNullSourceMembers</c> was class-scoped, a profile mixing
    ///         patch-merge maps with ordinary ones had to be split across two mapper classes, and the split
    ///         produced one null-guarded and one unguarded copy of the same nested pair — "a real behavioural
    ///         difference, not a cosmetic one", because the store could deserialize nulls into those members.
    ///     </para>
    ///     <para>
    ///         Info, not Warning: two mappers deliberately configured differently is a legitimate design, and
    ///         <c>[MapNullSkip]</c> now removes the reason the split was forced in the first place. What the
    ///         diagnostic adds is that the consequence is stated rather than discovered.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DivergentSynthesizedPair = new(
        "DWARF081",
        "The same nested pair is synthesized two different ways",
        "Mappers {0} each auto-synthesize '{1}' -> '{2}', and their copies do not agree ({3}). A synthesized "
        + "helper inherits the policy of the mapper that reached it, so one pair of types is mapped two ways "
        + "in this assembly and nothing else reports it. Declare the pair once and share it (a partial method, "
        + "or [GenerateMap] on one mapper), narrow the differing option to the pair that needs it (e.g. "
        + "[MapNullSkip<TSource, TTarget>]), or accept the divergence deliberately.",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf081");

    /// <summary>
    ///     <c>[ProvidesMap]</c> on a method the ambient registry cannot hold.
    /// </summary>
    /// <remarks>
    ///     Refused rather than silently skipped: the author asked for a registration, and dropping it quietly
    ///     would leave every facade call site for that pair throwing with nothing to explain why — which is
    ///     the exact failure this attribute exists to prevent.
    /// </remarks>
    public static readonly DiagnosticDescriptor ProvidesMapInvalidShape = new(
        "DWARF082",
        "[ProvidesMap] method cannot be registered",
        "Method '{0}' is marked [ProvidesMap] but does not have a registerable shape. It must be public, "
        + "take exactly one parameter, and return a value, with both types publicly nameable — the same shape "
        + "the generator's own maps register under, because the registry holds them the same way.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf082");

    /// <summary>
    ///     An enum's string form is not its identifier, because <c>[EnumMember]</c>/<c>[Description]</c>
    ///     redirects it — and that string is what gets persisted.
    /// </summary>
    /// <remarks>
    ///     Round 18 came within one code review of shipping this: <c>DispatchChannel.NextDay</c> carried
    ///     <c>[Description("Next-Day")]</c>, and the migration would have started writing <c>"Next-Day"</c> into a
    ///     store full of <c>"NextDay"</c>, breaking reads of every existing document. The precedence is a good
    ///     default; the hazard is that <c>[Description]</c> is usually a DISPLAY annotation.
    /// </remarks>
    public static readonly DiagnosticDescriptor EnumStringNameDiverges = new(
        "DWARF083",
        "Enum maps to strings that are not its member identifiers",
        "Enum '{0}. [EnumMember]/[Description] takes precedence over the identifier for enum-to-string "
        + "mapping, so these are the values that will be written and read. If the attribute was meant for "
        + "display rather than persistence — which is the common case — map through an explicit "
        + "[MapProperty(Use = …)] converter, or remove it from the members you persist.",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf083");

    public static readonly DiagnosticDescriptor CollectionKeyInvalid = new(
        "DWARF074",
        "[MapCollectionKey] cannot be applied here",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf074");

    public static readonly DiagnosticDescriptor StringFormatInvalid = new(
        "DWARF073",
        "[MapProperty(StringFormat=)] is not applicable here",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf073");

    public static readonly DiagnosticDescriptor AutoMatchDisabled = new(
        "DWARF072",
        "Member has a source match but auto-matching is disabled",
        "Destination member '{0}' has a same-named source member, but this mapper is explicit-only "
        + "([DwarfMapper(AutoMatchMembers = false)]) so nothing is auto-wired across the trust boundary. Map it "
        + "deliberately with [MapProperty] or skip it with [MapIgnore]. This is what stops an untrusted "
        + "same-named field (e.g. IsAdmin) from silently over-posting onto a protected member.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf072");

    public static readonly DiagnosticDescriptor NullableRefSourceToNonNullableTarget = new(
        "DWARF070",
        "Nullable source member is assigned to a non-nullable target member",
        "Source member '{0}' is a nullable reference but the destination member is non-nullable, so a null "
        + "would be stored in a member whose type forbids it. Fix it in one of: [MapProperty(NullSubstitute = …)] "
        + "for a fallback value, [DwarfMapper(SkipNullSourceMembers = true)] to keep the destination default, "
        + "or make the destination member nullable.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf070");

    /// <summary>
    ///     A cross-assembly manifest attribute the generator emits, written by hand instead.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[assembly: DwarfProvidesMap]</c> and <c>[assembly: DwarfRequiresMap]</c> are output, not
    ///         input: the generator writes one entry per map this assembly registers or consumes, and the
    ///         <c>[DwarfMapperValidationRoot]</c> compilation reads those entries out of referenced metadata to
    ///         decide DWARF061. Nothing else describes the graph, so the root can only be as truthful as the
    ///         manifest is.
    ///     </para>
    ///     <para>
    ///         A hand-written entry breaks that in the direction nothing catches. A fabricated <c>Provides</c>
    ///         row satisfies a <c>Requires</c> row for a map no assembly registers, so the compile-time check
    ///         passes and the failure moves to the first call site at run time — which is the exact failure
    ///         DWARF061 exists to pull forward. Refused rather than ignored, because an ignored entry still
    ///         reads to the next person as a supported way of declaring a map.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor HandWrittenManifestAttribute = new(
        "DWARF086",
        "Manifest attribute is emitted by the generator",
        "'{0}' is emitted by the generator onto the assembly and must not be hand-written: it declares a map "
        + "the generator never produced, so the cross-assembly manifest stops describing this assembly and "
        + "the [DwarfMapperValidationRoot] check (DWARF061) trusts the difference. Delete it. To CONSUME a "
        + "cross-assembly map, declare it with [UsesMap<TSource, TDestination>]; to PROVIDE one, declare the "
        + "map itself ([GenerateMap] on a [DwarfMapper] class, or [ProvidesMap] on a hand-written method) and "
        + "the generator writes the manifest entry for you.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf086");

    /// <summary>
    ///     Two or more <c>[FlattenGraph]</c> directives on one mapping method fill the same destination
    ///     collection.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The one defect in round 20 that was not a silence: the generator ACCEPTED the shape and emitted
    ///         <c>new Dst { Flat = …, Flat = … }</c>, which is <c>CS1912 "Duplicate initialization of member"</c>.
    ///         The consumer therefore could not build at all, and the error they were shown pointed at
    ///         <c>Demo.M.g.cs</c> — a generated file they never wrote and cannot edit. Emitting code that does
    ///         not compile is strictly worse than any silent divergence, because there is no version of the
    ///         program that runs.
    ///     </para>
    ///     <para>
    ///         Refused rather than collapsed, matching DWARF011: a repeated directive is a copy-paste mistake,
    ///         and quietly keeping one of them hides the mistake from the only person who can fix it. Keyed on
    ///         the DESTINATION collection rather than on the directive being character-identical, because that
    ///         is where the defect actually lives — <c>[FlattenGraph("Entry", "Nodes")]</c> next to
    ///         <c>[FlattenGraph("Other", "Nodes")]</c> emits the same CS1912 from two directives that are not
    ///         duplicates of each other at all. <c>[FlattenGraph]</c> stays <c>AllowMultiple</c>: several
    ///         directives naming DIFFERENT destination collections remain the supported way to flatten more
    ///         than one graph into one DTO.
    ///     </para>
    ///     <para>
    ///         Found by the <c>AllowMultiple ×2</c> axis of the surface matrix, which exists only because the
    ///         case space is derived from <c>AttributeUsage.AllowMultiple</c> rather than from a hand-written
    ///         list of scenarios worth testing. Nobody would have written that test.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DuplicateFlattenGraphTarget = new(
        "DWARF087",
        "Duplicate [FlattenGraph] destination collection",
        "Destination collection '{0}' is filled by more than one [FlattenGraph] directive on this method, "
        + "which would emit an object initializer that assigns '{0}' twice (CS1912) — code that does not "
        + "compile. Keep exactly one [FlattenGraph] per destination collection; to flatten a second graph, "
        + "name a different collection member on the destination type.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf087");

    /// <summary>
    ///     The MEMBER-placement overload of <c>[MapProperty]</c> or <c>[MapIgnore]</c>, written on a mapper
    ///     class or a mapping method where there is no annotated member for it to be about.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both attributes carry two placements behind one name, and each placement has its own
    ///         constructor. <c>[MapProperty("X")]</c> names the destination THE ANNOTATED MEMBER supplies and
    ///         a bare <c>[MapIgnore]</c> says "never read THE ANNOTATED MEMBER" — statements that only mean
    ///         something where the annotated type is itself the declaration of the mapping (the <c>[MapTo]</c>
    ///         registry, the co-located host). On a mapper class or a mapping method there is no annotated
    ///         member: the mapping is declared by the method, and the DTO pair is two ordinary types.
    ///     </para>
    ///     <para>
    ///         Until this id existed both were discarded without a word — <c>ReadExplicitMaps</c> accepts only
    ///         the two-argument application and <c>ReadIgnores</c> only the one-argument one, and each simply
    ///         skipped anything else. The <c>[MapProperty]</c> half is the worse of the two, because the named
    ///         arguments ride on that same one-argument constructor: <c>[MapProperty("X", Use = "F")]</c> is
    ///         <c>ctor(1)</c> plus a property initializer, so the converter, the <c>When</c> predicate, the
    ///         null substitute and the format string went into the same bin as the binding. The caller named a
    ///         conversion method and got auto-matching.
    ///     </para>
    ///     <para>
    ///         Refused rather than honoured, and the reasoning survives either reading. Honouring
    ///         <c>[MapProperty("X")]</c> at a method would bind <c>X</c> to itself — the identity binding
    ///         auto-matching already produces, so it is a no-op by construction and the caller cannot have
    ///         meant it. Discarding it evaporates a binding they wrote explicitly. Both are wrong; only saying
    ///         so lets them fix it. A bare <c>[MapIgnore]</c> has no honourable reading at all: it names
    ///         nothing.
    ///     </para>
    ///     <para>
    ///         An <b>Error</b>, matching its registry mirror <c>DWARFR04</c>, which is an Error for the exact
    ///         same misuse written at the other front door. It shipped as a Warning for one round and the
    ///         reason given was never a product reason: an Error suppresses the whole class's emission, so the
    ///         partial mapping method loses its implementing part and the consumer meets <c>CS8795</c> — and
    ///         while the G4/R4 ordering defect stood, the surface matrix read that cascade as "the compiler
    ///         rejected the placement" rather than as a refusal, so escalating would have moved ~25 measured
    ///         cells into a population nothing judged. R4 is fixed: a <c>CS8795</c> behind a blocking DwarfMapper
    ///         error is now read as the refusal it is, and the ratchet-avoidance argument died with it.
    ///     </para>
    ///     <para>
    ///         What is left is the product argument, and it points the other way. The cascade is paid by EVERY
    ///         blocking DwarfMapper error, including <c>DWARF011</c> (two <c>[MapProperty]</c> directives over
    ///         one destination) and <c>DWARF087</c> (two <c>[FlattenGraph]</c> directives over one destination
    ///         collection) — both Errors, both on this same class, both stranding the same partial method. A
    ///         cost every id pays cannot decide the severity of one of them. And what this id refuses is
    ///         SILENT DATA LOSS: the named arguments ride on the same one-argument constructor as the binding,
    ///         so <c>[MapProperty("Name", Use = nameof(F))]</c> on a mapper discards the converter, the
    ///         <c>When</c> predicate, the null substitute and the format string together with the binding, and
    ///         the caller gets auto-matching. A suppressible Warning is the wrong instrument for a directive
    ///         whose entire payload evaporates.
    ///     </para>
    ///     <para>
    ///         The message is composed at report time (<c>MessageFormat</c> is the pass-through <c>{0}</c>)
    ///         because the two attributes need different remedies: one says "supply both names", the other
    ///         "name the destination to exclude". One descriptor for both regardless — they are one defect
    ///         wearing two attribute names, and two ids would have said the same thing twice.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MemberFormDirectiveOnMapper = new(
        "DWARF088",
        "Member-placement directive written on a mapper",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf088");

    /// <remarks>
    ///     <para>
    ///         The exact inverse of <see cref="MemberFormDirectiveOnMapper" />, and the reason it is a second
    ///         id rather than a second message under the first: <c>DWARF088</c> says "you wrote the member
    ///         form where there is no member", this says "you wrote the method form on a member, or wrote a
    ///         member directive the declared pairs cannot receive". Same family, opposite mistake, different
    ///         remedy — folding them together would produce a title that is false for half the cells it fires
    ///         on.
    ///     </para>
    ///     <para>
    ///         A co-located <c>[GenerateMap&lt;S,T&gt;]</c> host declares its own mapping, so a member of the
    ///         host IS part of that declaration and carries the MEMBER form: <c>[MapProperty("SourceMember")]</c>
    ///         names where the annotated destination member is filled from, and a bare <c>[MapIgnore]</c>
    ///         excludes it. Everything else written there acts on nothing, and acted on nothing in silence
    ///         until this check existed (surface-matrix finding D20).
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, and the reason is this id's own rather than borrowed from
    ///         <c>DWARF088</c> — which is an <b>Error</b>, because what it refuses is a directive whose whole
    ///         payload evaporates. Here a blocking error would suppress the
    ///         whole host's emission, so the generated <c>&lt;Host&gt;Mapper</c>, its convenience extension
    ///         and its DI registration would all vanish and the refusal would reach the consumer as
    ///         <c>CS1061</c> at every call site instead. The offending directive is dropped and the rest of
    ///         the host's mapping is emitted as if it had not been written; escalate with
    ///         <c>dotnet_diagnostic.DWARF089.severity = error</c> where the stricter reading is wanted.
    ///     </para>
    ///     <para>
    ///         The message is composed at report time (<c>MessageFormat</c> is the pass-through <c>{0}</c>)
    ///         because the four shapes need four remedies — the two method-form placements, a directive count
    ///         that does not match the declared pairs, and a host that is not the destination of any pair it
    ///         declares.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MisplacedDirectiveOnCoLocatedHostMember = new(
        "DWARF089",
        "Directive on a co-located host member cannot be applied",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf089");

    /// <summary>
    ///     A member directive written on a mapping method (or its class) that the ELEMENT-WISE endpoints —
    ///     the span map and the async-stream map — cannot apply.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generalization of <see cref="ExplicitOnlyNotElementWise" />, and the same root cause: a
    ///         span or async-stream method resolves no members of its own. It maps the ELEMENT pair through an
    ///         auto-synthesized mapper, and a directive written without a pair scope belongs to the DECLARING
    ///         method rather than to that pair, so it never reaches it. <c>DWARF077</c> closed the case for the
    ///         explicit-only trust boundary alone; the surface matrix then measured the same silence for
    ///         <c>[MapIgnore("X")]</c> and <c>[MapProperty("X", "Y")]</c> (findings D1 and D2), where one mapper
    ///         dropped a member on three of its overloads and copied it on the other two, saying nothing.
    ///     </para>
    ///     <para>
    ///         Refused rather than propagated, and the reason is the one <c>DWARF077</c> already states: the
    ///         synthesized element mapper is keyed by <c>(source, target)</c> and shared by every route that
    ///         reaches that pair. Pushing one method's unscoped directive into it would silently re-configure
    ///         a nested mapping some other method owns — a worse defect than the silence, and invisible from
    ///         the declaration that caused it.
    ///     </para>
    ///     <para>
    ///         <c>[Reinterpret]</c> is the one arm with NO pair-scoped twin (finding <c>D12</c>: honoured at
    ///         the create map and the update-into, silent at both element-wise endpoints — the two whose whole
    ///         purpose is bulk element throughput, and therefore the two where a caller reaching for a forced
    ///         blit most expects it to apply). Its remedy is a DECLARED create map instead of a re-scoped
    ///         attribute, and that is not a weaker answer: an element-wise map resolves its element pair
    ///         through a declared mapping method where the class has one rather than synthesizing a fresh one,
    ///         so the loop becomes <c>d[__i] = Map(s[__i]);</c> and the blit runs per element through it.
    ///         MEASURED at both element-wise endpoints — the member is assigned through
    ///         <c>__DwarfBlit_…</c> (<c>MemoryMarshal.Cast</c>) rather than a per-element numeric conversion
    ///         helper — before the message said it.
    ///     </para>
    ///     <para>
    ///         The remedy is a form that already works here, which is what makes this a refusal a caller can
    ///         act on rather than a capability withdrawal: the PAIR-SCOPED twins
    ///         <c>[MapIgnore&lt;TTarget&gt;("X")]</c> and <c>[MapProperty&lt;TSource, TTarget&gt;("X", "Y")]</c>
    ///         are matched against every synthesized pair, including this element pair. Measured on the surface
    ///         matrix, and stated exactly because the two readings differ: <c>[MapIgnore&lt;TTarget&gt;]</c> reads
    ///         <c>Honoured</c> at both element-wise endpoints, while <c>[MapProperty&lt;TSource, TTarget&gt;]</c>
    ///         reads <c>Refused</c> there — the bind happens and a <c>DWARF038</c> about the resulting
    ///         <c>int → string</c> conversion rides along, and the classifier tests for an added diagnostic
    ///         before it compares output. The conversion warning IS the evidence the rename was applied, but it
    ///         is not the same observation as <c>Honoured</c>. The message names the exact replacement text.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, for the reason <c>DWARF089</c> is: a blocking error
    ///         suppresses the whole class's emission, so every partial mapping method on it loses its
    ///         implementing part and the consumer meets a wall of <c>CS8795</c> with this refusal buried under
    ///         it. The directive is dropped for this endpoint and the rest of the mapper is emitted; escalate
    ///         with <c>dotnet_diagnostic.DWARF090.severity = error</c> where the stricter reading is wanted.
    ///     </para>
    ///     <para>
    ///         The message is composed at report time (<c>MessageFormat</c> is the pass-through <c>{0}</c>)
    ///         because the replacement text differs per directive and per element pair.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DirectiveNotAppliedElementWise = new(
        "DWARF090",
        "Member directive is not applied element-wise",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf090");

    /// <summary>
    ///     <c>[BeforeMap]</c> or <c>[AfterMap]</c> on a partial method that has no implementing part — a
    ///     hook whose body does not exist.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>CollectHooks</c> scanned every method on the mapper class and accepted anything whose
    ///         signature fitted, which includes the partial MAPPING METHOD declarations themselves: the
    ///         generator writes those bodies, so the caller has no code there for a hook to run. The
    ///         signature filter let some through and not others purely by shape, and the three outcomes were
    ///         all wrong in different ways.
    ///     </para>
    ///     <para>
    ///         Measured on the surface matrix (finding D16). On <c>Dst Map(Src)</c> the method is not void, so
    ///         <c>DWARF018</c> fired and the build failed with a signature complaint about a method whose
    ///         signature was never the problem. On <c>void MapSpan(ReadOnlySpan&lt;S&gt;, Span&lt;D&gt;)</c> it
    ///         fitted the two-parameter after-hook shape exactly, was registered, and was never called — the
    ///         silent cell the matrix reported. And on <c>void Update(S, D)</c> it fitted too and WAS called:
    ///         the emitted body ended in <c>Update(s, d);</c>, unconditional infinite recursion that the matrix
    ///         scored as the directive being honoured.
    ///     </para>
    ///     <para>
    ///         The rule is not specific to mapping methods and does not need to be. A partial method with no
    ///         implementing part is erased by the C# compiler along with every call to it, so as a hook it can
    ///         only ever be a no-op — or, where the generator supplies the missing part, a call back into the
    ///         method being generated. Neither is what <c>[AfterMap]</c> means, at any endpoint, which is why
    ///         this replaces the <c>DWARF018</c> signature complaint rather than sitting after it.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, for the reason <c>DWARF089</c> and <c>DWARF090</c> are: an error would strand
    ///         every partial
    ///         mapping method on the class behind <c>CS8795</c>. The hook is dropped — which is what the
    ///         caller already had at three of the five endpoints, minus the recursion at the fourth — and the
    ///         mapper is emitted; escalate with <c>dotnet_diagnostic.DWARF091.severity = error</c> where the
    ///         stricter reading is wanted.
    ///     </para>
    ///     <para>
    ///         The message is composed at report time (<c>MessageFormat</c> is the pass-through <c>{0}</c>)
    ///         because it names both the attribute and the method, and <c>DiagnosticInfo</c> carries one
    ///         message argument.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor HookOnMethodWithNoBody = new(
        "DWARF091",
        "Mapping hook on a partial method with no body",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf091");

    /// <summary>
    ///     A directive the generator reads at ONE mapping endpoint only, written on a mapping method that is
    ///     one of the other four, where it is discarded.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two families share this id, and they point in opposite directions. Three directives are read
    ///         only where the destination is CONSTRUCTED and RETURNED — the create map — and are discarded on
    ///         an update-into, projection, span or async-stream method. One is read only where the
    ///         destination ALREADY EXISTS — the update-into — and is discarded everywhere else, the create
    ///         map included. What they have in common is the shape, not the direction: a directive read at
    ///         exactly one endpoint, so the identical text on the identical mapper class means one thing on
    ///         one overload and nothing on the next four.
    ///     </para>
    ///     <para>
    ///         Measured on the surface matrix, which found the create-map shape three times over (findings
    ///         <c>D8</c>, <c>D11</c> and <c>D13</c>) and the update-into shape once
    ///         (<c>[MapCollectionKey]</c>, finding <c>D14</c>). The refusal is the closure rather than the
    ///         feature in both directions: the three create-map directives are about the destination the
    ///         create map BUILDS —
    ///         <c>[FlattenGraph]</c> replaces the source of a destination collection with a graph walk and
    ///         <c>[MapDerivedType]</c> chooses which destination type to construct, so at an update-into —
    ///         which writes into an instance the caller already built — there is no construction step for
    ///         either to redirect. <c>[ReverseMap]</c> shares the endpoint, not the reason: it makes a
    ///         SEPARATELY-DECLARED inverse method inherit this one's simple renames with their ends swapped,
    ///         and the match is by signature — a forward <c>TDto Map(TSource s)</c> against an inverse
    ///         <c>TSource Back(TDto d)</c>, both one-parameter create maps, which no other endpoint's
    ///         signature is. It does not GENERATE a method, and a missing inverse is <c>DWARF052</c> rather
    ///         than a silence; task A9a's own finding entry (<c>D13</c>) asserted otherwise and was
    ///         measurably wrong, so the wrong model is restated here only to say it is wrong.
    ///     </para>
    ///     <para>
    ///         <c>[MapCollectionKey]</c> is the mirror image and is here for the same reason. A key-based
    ///         upsert merges the source elements into the list the destination already holds, matching on the
    ///         named key, so untouched elements survive; that needs a destination to merge INTO, and the
    ///         create map, the projection, the span map and the async stream all build a fresh one. Its own
    ///         <c>DWARF074</c> already validates the directive at the update-into and its documentation
    ///         already named "not an update-into method" as a case it covered — but no call site ever asked
    ///         that question, because <c>ApplyCollectionKeyUpserts</c> is only reached from the update-into
    ///         branch. The check could not live on <c>DWARF074</c> once it was written: that id is an
    ///         <b>Error</b>, and an error here suppresses the whole class's emission (see the severity note
    ///         below).
    ///     </para>
    ///     <para>
    ///         One id and one gate rather than four, for the reason <c>DWARF088</c> is one check over two
    ///         attributes and two sites: it is one mistake, made about four directives, and a per-directive
    ///         id would leave whichever directive was added last silent at whichever endpoint was written
    ///         last. All five branches call the same gate, and each ARM names its own home endpoint and is
    ///         skipped there — <c>[FlattenGraph]</c>, <c>[MapDerivedType]</c> in both of its forms and
    ///         <c>[ReverseMap]</c> are at home on the create map, <c>[MapCollectionKey]</c> on the
    ///         update-into.
    ///     </para>
    ///     <para>
    ///         <c>[ReverseMap]</c> is the one whose message carries NO transfer claim, even element-wise. The
    ///         adoption sentence is true of a directive that changes what the create map EMITS, because that
    ///         emission is what the element-wise loop calls; <c>[ReverseMap]</c> changes nothing about the
    ///         method it sits on and instead makes a separately-declared inverse inherit its renames. Saying
    ///         otherwise would have told a caller their inverse reaches the span map, which is not a claim
    ///         about anything.
    ///     </para>
    ///     <para>
    ///         The remedy the message names was MEASURED before it was prescribed. At the two ELEMENT-WISE
    ///         endpoints a create map declared beside the span or stream method over the same pair is adopted
    ///         as the element converter — the emitted loop is literally <c>d[__i] = Map(s[__i]);</c> — so
    ///         moving the directive onto that create map really does make it reach this method. At
    ///         update-into and projection nothing of the sort happens, and the message says only that the
    ///         create map honours it; a remedy nobody ran is how a diagnostic sends a caller in a circle.
    ///         <c>[MapCollectionKey]</c>'s remedy names an update-into and carries NO adoption claim at any
    ///         endpoint, which is a sharper point than <c>[ReverseMap]</c>'s: what an element-wise loop adopts
    ///         is a declared CREATE map for the element pair, and a declared update-into is not one.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, for the reason <c>DWARF089</c>, <c>DWARF090</c> and <c>DWARF091</c> are: a
    ///         blocking error suppresses the whole class's emission, so every partial mapping method on it
    ///         loses its implementing part and the consumer meets a wall of <c>CS8795</c> with this refusal
    ///         buried under it. The directive is dropped for this endpoint and the rest of the mapper is
    ///         emitted; escalate with <c>dotnet_diagnostic.DWARF092.severity = error</c> where the stricter
    ///         reading is wanted.
    ///     </para>
    ///     <para>
    ///         The message is composed at report time (<c>MessageFormat</c> is the pass-through <c>{0}</c>)
    ///         because it names the directive as written, the method, the endpoint and the pair.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DirectiveNotReadAtThisEndpoint = new(
        "DWARF092",
        "Directive is not read at this mapping endpoint",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf092");

    /// <summary>
    ///     <c>[GenerateWrapperMap(typeof(W&lt;&gt;))]</c> on a class that declares no
    ///     <c>[GenerateMap&lt;A, B&gt;]</c> pair — an opt-in with an empty list to expand.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The attribute is defined RELATIVE to <c>[GenerateMap]</c>, and that is the whole of the
    ///         argument for refusing rather than widening it. Its own documentation opens "for every
    ///         <c>[GenerateMap&lt;A, B&gt;]</c> declared on the same <c>[DwarfMapper]</c> class", and
    ///         <c>ExpandWrapperMaps</c> is literally an append to that pair list: it takes the declared pairs
    ///         and adds <c>W&lt;A&gt; -&gt; W&lt;B&gt;</c> per pair. A pair declared as a partial mapping
    ///         METHOD is a different mechanism with a different signature, and four of the five mapping
    ///         endpoints are not create maps at all — an update-into, a projection, a span map and an
    ///         async-stream map have no <c>W&lt;A&gt; -&gt; W&lt;B&gt;</c> shape to synthesize. Expanding
    ///         those would hand the caller a create map they never asked for.
    ///     </para>
    ///     <para>
    ///         Measured on the surface matrix as finding <c>D15</c>: on a <c>[DwarfMapper]</c> class at any of
    ///         the five mapper endpoints the attribute produced NOTHING and said nothing. And the mechanism
    ///         the finding gave for that was wrong. It read the <c>DWARF067</c> at the co-located host as the
    ///         generator "having an opinion about where the attribute is valid" — <c>DWARF067</c> is an
    ///         opinion about the WRAPPER TYPE, not about placement, and it fired there only because the
    ///         co-located host template declares <c>[GenerateMap&lt;Src, Dst&gt;]</c> and the sampled argument
    ///         <c>typeof(Dst)</c> is not a single-parameter generic. The mapper-endpoint silence was
    ///         <c>ExpandWrapperMaps</c> returning early on an empty pair list, before any validation at all.
    ///     </para>
    ///     <para>
    ///         Reported BEFORE the wrapper's shape is validated, deliberately. With no pairs to expand, even a
    ///         perfectly-shaped <c>Envelope&lt;T&gt;</c> expands nothing, so "there is nothing to expand" is
    ///         the actionable statement and "your wrapper is the wrong shape" would send the caller to fix
    ///         something that changes no output. It also keeps the class emitting: <c>DWARF067</c> is an
    ///         <b>Error</b>, and raising it here would strand every partial mapping method on the class behind
    ///         <c>CS8795</c>. The co-located host still validates the wrapper, because there the pair list is
    ///         not empty.
    ///     </para>
    ///     <para>
    ///         The remedy was MEASURED before it was prescribed, and so was its ONE sharp edge:
    ///         <c>[GenerateMap&lt;A, B&gt;]</c> beside <c>[GenerateWrapperMap]</c> emits the wrapper map
    ///         cleanly — but <c>[GenerateMap]</c> also emits its own <c>B Map(A)</c>, so adding it to a class
    ///         that already declares a <c>partial B Map(A)</c> over the SAME pair is <c>CS0111</c>. The
    ///         message says so rather than sending a create-map caller into a duplicate-member error.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, for the reason <c>DWARF090</c> and <c>DWARF092</c> are, and the message is
    ///         composed at report time (<c>MessageFormat</c> is the pass-through <c>{0}</c>) because it quotes
    ///         the wrapper the caller wrote.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor WrapperMapExpandsNothing = new(
        "DWARF093",
        "[GenerateWrapperMap] has no declared pair to expand",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf093");

    /// <summary>
    ///     A <c>[GenerateMap&lt;S, T&gt;]</c> would synthesize a <c>Map</c> method whose exact signature AND
    ///     return type this class already produces — either the same pair is declared twice, or the class
    ///     declares a partial mapping method over the same pair.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both shapes used to reach the compiler as a bare <c>CS0111</c> plus a <c>CS0121</c> ambiguity
    ///         cascade, reported against the GENERATED file — a collision the generator created, announced by
    ///         nobody. Filed as <b>B27</b>, and measured before it was filed: eight surface-matrix cells sat
    ///         in the <c>EmittedInvalidCode</c> population for exactly this. It is the gap between two
    ///         neighbours: <c>DWARF060</c> covers the same signature with DIFFERENT return types (an
    ///         overload-by-return-type clash between two pairs), and <c>DWARF057</c> covers the generated
    ///         mapper TYPE colliding with an existing type — a generated member duplicating an existing map
    ///         over the SAME pair was neither.
    ///     </para>
    ///     <para>
    ///         Refused rather than deduplicated, for <c>DWARF087</c>'s reason: silently keeping one of two
    ///         identical directives hides the mistake from the only person able to fix it — and here the
    ///         duplicate is not even harmless, because a co-located host's member directives bind to its
    ///         declared pairs POSITIONALLY, so a duplicated pair shifts which pair a directive configures.
    ///         The declared-partial variant matters doubly because <c>DWARF093</c>'s remedy sends a
    ///         <c>[GenerateWrapperMap]</c> caller to add <c>[GenerateMap&lt;A, B&gt;]</c>, and on a create-map
    ///         class that lands exactly here — the caller must arrive at a named refusal, not a raw compiler
    ///         error.
    ///     </para>
    ///     <para>
    ///         An <b>Error</b>, like <c>DWARF060</c> and <c>DWARF087</c>: the build could not succeed either
    ///         way, and an Error makes this the single actionable statement instead of the <c>CS0111</c> it
    ///         replaces. The full, formatted message is built at the call site (<c>{0}</c>) because it names
    ///         the colliding pair and which of the two shapes it met.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DuplicateGenerateMapSignature = new(
        "DWARF094",
        "[GenerateMap] duplicates an existing map method",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        "DwarfMapper names every [GenerateMap]-synthesized mapping method `Map` and overloads it by the SOURCE (parameter) type, so a pair declared twice — or declared beside a partial mapping method over the same pair — would emit two members with an identical signature and return type: CS0111 in a generated file the caller never wrote. The collision is reported here instead, before anything is emitted. Declare each pair exactly once: keep one [GenerateMap] per pair, and where a partial method already maps the pair, either remove the [GenerateMap] or give the partial method a different name.",
        HelpBase + "dwarf094");

    /// <summary>
    ///     An unscoped <c>[MapIgnore("Name")]</c> whose name matches no destination member anywhere it is
    ///     read — it excludes nothing, and until this id it did so in silence.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Surface-matrix finding <b>B20</b>: the destination-name set is matched with
    ///         <c>IgnoreNameComparer</c> (ordinal), so <c>[MapIgnore("id")]</c> against a property <c>Id</c>
    ///         — or <c>[MapIgnore("Typo")]</c> against nothing at all — was accepted, excluded nothing, and
    ///         produced no diagnostic at any endpoint. The caller believed they excluded a member; the
    ///         completeness gate went on demanding it, and <c>DWARF001</c>, if it fired at all, named the
    ///         member rather than the dead directive. The pair-scoped forms already had this guard
    ///         (<c>DWARF056</c>, "matches no mapped pair"), so the unscoped form being exempt was an
    ///         asymmetry, not a policy.
    ///     </para>
    ///     <para>
    ///         What counts as "matching" is derived from resolution's own consumers, not re-guessed here: an
    ///         ignore name is live when it names a WRITABLE destination member (excluded from mapping) or a
    ///         READ-ONLY one (suppressing the silent-loss warning) of a pair the set reaches. A METHOD-site
    ///         name is judged against that method's own destination; a CLASS-site name is class-WIDE and
    ///         judged against every pair the class maps — including span/async ELEMENT pairs, where a
    ///         matching name is <c>DWARF090</c>'s to report (dropped, with the pair-scoped remedy), so the
    ///         two ids never fire together. Where a class carries an endpoint whose unscoped-ignore
    ///         consumption this walk cannot see (a <c>[MapDerivedType]</c> dispatch, a top-level
    ///         collection-returning method), the class-site check stands down entirely rather than guess —
    ///         a false "names nothing" that breaks a working suppression is B19's exact genre.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, like <c>DWARF056</c>: the configuration compiles and the mapper works; what
    ///         is wrong is that a written directive does nothing. The message is composed at the call site
    ///         (<c>{0}</c>) because the method-site and class-site statements name different scopes.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor UnscopedIgnoreNoMatch = new(
        "DWARF095",
        "[MapIgnore] names no destination member",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf095");

    /// <summary>
    ///     The per-METHOD twin of <see cref="NoCodeGenerated" /> (DWARF078): one projection method was not
    ///     generated because a member of it cannot be translated, and the rest of the mapper WAS.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         DWARF028 is an error, and an error used to suppress the whole class — so a mapper declaring a
    ///         <c>Map</c> and a <c>Project</c> over the same pair generated NOTHING the moment one projected
    ///         member was untranslatable, and every method on it reported CS8795. The <c>Map</c> methods were
    ///         collateral: nothing about them was untranslatable, because nothing about them is translated
    ///         (TASKS.md I14). A refusal is now proportional to what was refused — the projection method is
    ///         dropped, the class is emitted, and this signpost explains the ONE CS8795 that follows.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, exactly like DWARF078, and for the same reason: it is a signpost, not the
    ///         defect. The DWARF028 above it is the error, and it is the one to fix.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ProjectionMethodNotGenerated = new(
        "DWARF096",
        "Projection method was not generated",
        "Projection method '{0}' was not generated because of the error(s) above; it will ALSO "
        + "report CS8795 (\"must have an implementing part\") — that is a cascade of this, not a separate "
        + "problem, and it is not caused by a missing analyzer reference. The rest of this mapper WAS "
        + "generated: only this method is missing. Fix the error(s) above, or drop the Project method and "
        + "map those members with a runtime Map method instead.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        "A projection becomes an expression tree that a database provider translates. When one member of it "
        + "has no translatable form — or has no source at all — the projection method cannot be generated, "
        + "but the ordinary Map methods on the same mapper are unaffected and are still generated. This "
        + "warning marks the one method that is missing so its CS8795 is not mistaken for a broken analyzer "
        + "reference.",
        HelpBase + "dwarf096");

    /// <summary>
    ///     The per-METHOD twin of <see cref="NoCodeGenerated" /> (DWARF078) for the <c>Map</c> endpoints:
    ///     one mapping method was not generated because a destination member of it is unmapped, and the
    ///     rest of the mapper WAS.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         DWARF001 is an error, and an error used to suppress the whole class — so one incomplete
    ///         method cost the consumer every OTHER method on the mapper, each reporting its own CS8795
    ///         (TASKS.md I17). Those siblings were collateral: completeness is evaluated over one
    ///         (source, target) pair and one method-level <c>[MapIgnore]</c> set, so an unmapped member on
    ///         one method says nothing about the next. A refusal is now proportional to what was refused —
    ///         the incomplete method is withheld, the class is emitted, and this signpost explains the ONE
    ///         CS8795 that follows.
    ///     </para>
    ///     <para>
    ///         Distinct from <see cref="ProjectionMethodNotGenerated" /> (DWARF096) rather than folded into
    ///         it, because the two prescribe different remedies: DWARF096 can suggest dropping the
    ///         <c>Project</c> method and mapping at runtime, which is nonsense advice for a <c>Map</c>
    ///         method whose destination simply has a member nobody mapped.
    ///     </para>
    ///     <para>
    ///         A <b>Warning</b>, exactly like DWARF078 and DWARF096, and for the same reason: it is a
    ///         signpost, not the defect. The DWARF001 above it is the error, and it is the one to fix.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MappingMethodNotGenerated = new(
        "DWARF097",
        "Mapping method was not generated",
        "Mapping method '{0}' was not generated because of the completeness error(s) above; if it is "
        + "declared with accessibility modifiers it will ALSO report CS8795 (\"must have an implementing "
        + "part\") — that is a cascade of this, not a separate problem, and it is not caused by a missing "
        + "analyzer reference. "
        + "The rest of this mapper WAS generated: only this method is missing. Map the destination "
        + "member(s) named above, or annotate this method with [MapIgnore(\"…\")].",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        "Completeness is a promise about ONE mapping method: it is evaluated over that method's source and "
        + "target pair and honours that method's own [MapIgnore] set. An unmapped destination member "
        + "therefore withholds the method it belongs to and leaves the mapper's other methods generated. "
        + "This warning marks the one method that is missing so its CS8795 is not mistaken for a broken "
        + "analyzer reference.",
        HelpBase + "dwarf097");
}
