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
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf002");

    public static readonly DiagnosticDescriptor InvalidMapMethod = new(
        "DWARF003",
        "Invalid mapping method signature",
        "Mapping method '{0}' must be a partial instance method with a non-void return type and exactly one parameter",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf003");

    public static readonly DiagnosticDescriptor UnmappedMember = new(
        "DWARF001",
        "Destination member is not mapped",
        "Destination member '{0}' has no matching source member; map it or annotate the method with [MapIgnore(\"{0}\")]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf001");

    public static readonly DiagnosticDescriptor NoImplicitConversion = new(
        "DWARF005",
        "No implicit conversion between mapped members",
        "Cannot map to '{0}': no implicit conversion and no usable conversion method; declare a mapping method for the types or use [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf005");

    // DWARF006 removed: superseded by DWARF026 (NoMappableConstructor). Reserved but no descriptor.

    public static readonly DiagnosticDescriptor ReadOnlyDestinationMember = new(
        "DWARF007",
        "Destination member is read-only",
        "Destination member '{0}' is read-only; a matching source value cannot be assigned and would be lost (annotate the method with [MapIgnore(\"{0}\")] if intentional)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf007");

    public static readonly DiagnosticDescriptor AmbiguousMatch = new(
        "DWARF010",
        "Ambiguous source member",
        "Destination member '{0}' matches more than one source member under case-insensitive matching; rename or use [MapProperty]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf010");

    public static readonly DiagnosticDescriptor MapPropertyUnknownTarget = new(
        "DWARF008",
        "MapProperty target not found",
        "[MapProperty] destination member '{0}' does not exist or is not writable",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf008");

    public static readonly DiagnosticDescriptor MapPropertyUnknownSource = new(
        "DWARF009",
        "MapProperty source not found",
        "[MapProperty] source member '{0}' does not exist or is not readable",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf009");

    public static readonly DiagnosticDescriptor DuplicateMapProperty = new(
        "DWARF011",
        "Duplicate explicit mapping",
        "Destination member '{0}' has more than one [MapProperty] mapping; keep only one",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf011");

    public static readonly DiagnosticDescriptor IgnoreExplicitConflict = new(
        "DWARF012",
        "Conflicting [MapIgnore] and [MapProperty]",
        "Destination member '{0}' is both ignored via [MapIgnore] and mapped via [MapProperty]; remove one",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf012");

    public static readonly DiagnosticDescriptor AmbiguousConversion = new(
        "DWARF013",
        "Ambiguous conversion method",
        "Cannot map to '{0}': more than one mapping method converts these types; disambiguate with [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf013");

    public static readonly DiagnosticDescriptor UseMethodInvalid = new(
        "DWARF014",
        "Conversion method not found",
        "[MapProperty(Use = ...)] method '{0}' was not found or has an incompatible signature (it must take the source member type and return the destination member type)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf014");

    public static readonly DiagnosticDescriptor IncompleteEnumMapping = new(
        "DWARF015",
        "Incomplete enum mapping",
        "Enum member '{0}' has no destination member of the same name (by-name enum mapping); add it or use EnumStrategy.ByValue",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf015");

    public static readonly DiagnosticDescriptor FlattenRootInvalid = new(
        "DWARF016",
        "Invalid flatten source",
        "[Flatten] source member '{0}' does not exist, is not readable, or has no readable sub-members",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf016");

    public static readonly DiagnosticDescriptor AmbiguousFlatten = new(
        "DWARF017",
        "Ambiguous flattened member",
        "Destination member '{0}' is flattened from more than one source member; use [MapProperty] to disambiguate",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf017");

    public static readonly DiagnosticDescriptor InvalidHook = new(
        "DWARF018",
        "Invalid mapping hook signature",
        "Hook method '{0}' must be void; [BeforeMap] takes one parameter, [AfterMap] takes one or two",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf018");

    // DWARF019 (NotProjectable) — retired; superseded by DWARF028 (ProjectionNotTranslatable),
    // which carries a specific reason for every non-translatable projection member. Do not reuse.

    public static readonly DiagnosticDescriptor RoundTripNoInverse = new(
        "DWARF020",
        "No inverse for [RoundTrip]",
        "[RoundTrip] method '{0}' has no inverse mapping method (a partial method with the source/destination types swapped)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf020");

    public static readonly DiagnosticDescriptor RoundTripAmbiguousInverse = new(
        "DWARF021",
        "Ambiguous inverse for [RoundTrip]",
        "[RoundTrip] method '{0}' has more than one candidate inverse mapping method",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf021");

    // Detoxed: spell out what "unmanaged array-to-array" means in user terms; carry the deeper
    // "why" in the description + help link rather than in the one-line message.
    public static readonly DiagnosticDescriptor ReinterpretInvalid = new(
        "DWARF022",
        "Invalid [Reinterpret] target",
        "[Reinterpret] member '{0}' must map an array to an array of an unmanaged (blittable) element type — for example int[] → int[]. Arrays of reference types, or of structs that contain references, can't be reinterpreted.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "[Reinterpret] forces the blittable bulk-copy fast-path, which reinterprets one array's memory as another in a single block copy. That is only sound when both element types are 'unmanaged' (no managed references, fixed layout) and the same size. When the proof can't be satisfied the generator falls back to the safe element-by-element copy, so removing [Reinterpret] is always a valid fix.",
        helpLinkUri: HelpBase + "dwarf022");

    public static readonly DiagnosticDescriptor AfterMapValueTargetByValue = new(
        "DWARF023",
        "[AfterMap] value-type target must be passed by ref",
        "[AfterMap] on a value-type target '{0}' must take the target parameter by 'ref', or its changes are lost",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf023");

    public static readonly DiagnosticDescriptor ConstructorParameterUnmapped = new(
        "DWARF024",
        "Constructor parameter has no mappable source member",
        "Constructor parameter '{0}' has no mappable source member; add a source member with a matching name or use [MapProperty(src, \"<paramName>\")]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf024");

    public static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        "DWARF025",
        "Ambiguous constructor",
        "Destination type '{0}' has multiple constructors with the same maximum parameter count; annotate the intended constructor with [DwarfMapperConstructor]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf025");

    public static readonly DiagnosticDescriptor NoMappableConstructor = new(
        "DWARF026",
        "No mappable constructor",
        "Destination type '{0}' has no accessible non-obsolete instance constructor to use for mapping",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf026");

    public static readonly DiagnosticDescriptor UnsupportedCollectionTarget = new(
        "DWARF027",
        "Unsupported collection/dictionary target type",
        "Cannot map to '{0}': unsupported collection or dictionary target type. Use [MapProperty(Use = ...)] to supply a custom converter, or map it manually.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf027");

    // Detoxed title: "not translatable" is IQueryable/ORM jargon. The specific per-member reason is
    // supplied at the call site (the {0} message); the description explains the concept generally.
    public static readonly DiagnosticDescriptor ProjectionNotTranslatable = new(
        "DWARF028",
        "Projection member cannot be translated to a database query",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "An IQueryable projection (a Project method) becomes an expression tree that your database/ORM provider translates into a query. A member that needs a runtime conversion, a custom converter, a collection rebuild, or reference handling has no equivalent the provider can translate. Map those members with a runtime mapper (an ordinary Map method) instead of Project.",
        helpLinkUri: HelpBase + "dwarf028");

    // DWARF029 reserved.

    // Detoxed: dropped the internal terms "register-before-populate" and "cyclic back-edge" from the
    // user-facing message; the mechanism now lives in the description.
    public static readonly DiagnosticDescriptor CyclicConstructorParameter = new(
        "DWARF030",
        "Constructor parameter is part of a reference cycle",
        "Member '{0}' is set through a constructor parameter or init-only property, but it takes part in a reference cycle under ReferenceHandling=Preserve. A cycle can only be reconstructed when the looping member is assigned after the object is created — make '{0}' a settable property, or break the cycle.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Under ReferenceHandling=Preserve the mapper records each object before filling its members, so a cycle can point back to the object already in progress. A member supplied through a constructor argument or an init-only property is fixed at construction time — before the object can be recorded — so a cycle routed through it cannot be closed. Map that member via a settable property instead, or remove the cycle.",
        helpLinkUri: HelpBase + "dwarf030");

    // Lightly reworded from "Auto-synthesized nested mapper depth limit" internal phrasing.
    public static readonly DiagnosticDescriptor DeepNestingLimit = new(
        "DWARF031",
        "Mapping nests too deeply",
        "Mapping nests too deeply: the generator reached its limit of 512 synthesized nested mappers while processing '{0}'. Declare explicit mapping methods for deeply-nested types to bound the depth.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf031");

    // Detoxed: removed "DwarfRefContext", "thread ... into it", "back-edges closed", "topology
    // fidelity". States the user-visible effect and the two real choices; mechanism in description.
    public static readonly DiagnosticDescriptor ReferenceHandlingUseConverter = new(
        "DWARF032",
        "Custom converter can't preserve reference identity",
        "Member '{0}' is produced by a [MapProperty(Use = ...)] converter, so under ReferenceHandling=Preserve the mapper can't track its identity: the result won't be shared with other references to the same object, and a cycle passing through it won't be reconnected. Map it without a custom converter to keep identity, or keep the converter if a duplicated (non-shared) value is acceptable.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Reference preservation works by threading an identity map through the generated mapping code. A user-supplied converter is opaque to the generator, so the identity map can't be passed into it and its output is treated as a fresh object each time. This only changes behaviour when the same object is referenced from more than one place or takes part in a cycle.",
        helpLinkUri: HelpBase + "dwarf032");

    public static readonly DiagnosticDescriptor AbstractSourceAutoNest = new(
        "DWARF033",
        "Abstract or interface source type in auto-nested mapping",
        "Source type '{0}' is abstract or an interface; auto-nested mapping only maps declared members " +
        "and silently drops members that exist only on derived runtime types. Declare an explicit mapper, " +
        "use [MapIgnore] to suppress, or make the source type concrete.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf033");

    public static readonly DiagnosticDescriptor InvalidFlattenGraph = new(
        "DWARF034",
        "Invalid [FlattenGraph] configuration",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf034");

    public static readonly DiagnosticDescriptor InvalidMapDerivedType = new(
        "DWARF035",
        "Invalid [MapDerivedType] configuration",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf035");

    public static readonly DiagnosticDescriptor AmbiguousDerivedType = new(
        "DWARF036",
        "Ambiguous [MapDerivedType] dispatch arms",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf036");

    public static readonly DiagnosticDescriptor OnCycleIgnoredUnderPreserve = new(
        "DWARF037",
        "OnCycle is ignored under ReferenceHandling.Preserve",
        "Mapper '{0}' sets OnCycle = SetNull together with ReferenceHandling = Preserve; "
            + "OnCycle only applies in None mode (Preserve reconstructs cycles faithfully), so the setting has no effect",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf037");

    // Severity is dynamic: Info (suggestion) by default; escalated to Error per-instance (via
    // DiagnosticInfo.SeverityOverride) when [DwarfMapper(ImplicitConversions = false)] is set.
    public static readonly DiagnosticDescriptor ImplicitConversionApplied = new(
        "DWARF038",
        "Implicit type conversion applied",
        "{0}",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf038");

    // Emitted only under [DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]. Info (suggestion)
    // by default — visible, never silent, never build-breaking; escalate via
    // dotnet_diagnostic.DWARF039.severity = error in .editorconfig.
    public static readonly DiagnosticDescriptor UnconsumedSourceMember = new(
        "DWARF039",
        "Source member is read by no destination member",
        "Source member '{0}' is read by no destination member; map it, or annotate the mapper/method with [MapIgnoreSource(\"{0}\")]",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf039");

    public static readonly DiagnosticDescriptor MapValueTypeMismatch = new(
        "DWARF040",
        "Constant [MapValue] is not assignable to the destination",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf040");

    public static readonly DiagnosticDescriptor MapValueUseInvalid = new(
        "DWARF041",
        "[MapValue(Use=)] provider method is invalid",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf041");

    public static readonly DiagnosticDescriptor MapValueInvalid = new(
        "DWARF042",
        "Conflicting or invalid [MapValue]",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf042");

    public static readonly DiagnosticDescriptor PathSegmentNotFound = new(
        "DWARF043",
        "[MapProperty] source path segment not found",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf043");

    // Info: a nullable reference on the interior of a source path can throw NullReferenceException at
    // runtime when dereferenced. Suppress via dotnet_diagnostic.DWARF044.severity = none in .editorconfig.
    public static readonly DiagnosticDescriptor PathNullableHop = new(
        "DWARF044",
        "[MapProperty] source path traverses a nullable member",
        "{0}",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf044");

    public static readonly DiagnosticDescriptor UnflattenInvalid = new(
        "DWARF045",
        "Invalid [MapProperty] unflatten target path",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf045");

    public static readonly DiagnosticDescriptor UnflattenConflict = new(
        "DWARF046",
        "Conflicting [MapProperty] unflatten target",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf046");

    // Info: an additional mapping parameter matched no destination member. Suppress via
    // dotnet_diagnostic.DWARF047.severity = none in .editorconfig.
    public static readonly DiagnosticDescriptor UnusedMappingParameter = new(
        "DWARF047",
        "Additional mapping parameter is unused",
        "{0}",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf047");

    public static readonly DiagnosticDescriptor AmbiguousNormalizedMatch = new(
        "DWARF048",
        "Ambiguous member match under NameConvention.Flexible",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf048");

    public static readonly DiagnosticDescriptor NullSubstituteInvalid = new(
        "DWARF049",
        "Invalid [MapProperty(NullSubstitute=)]",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf049");

    public static readonly DiagnosticDescriptor WhenPredicateInvalid = new(
        "DWARF050",
        "Invalid [MapProperty(When=)] predicate",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf050");

    // Warning: a forward [MapProperty] could not be auto-inverted for [ReverseMap]; declare the reverse
    // rename explicitly. Suppress via dotnet_diagnostic.DWARF051.severity = none in .editorconfig.
    public static readonly DiagnosticDescriptor ReverseMapNonInvertible = new(
        "DWARF051",
        "[ReverseMap] cannot auto-invert this configuration",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf051");

    public static readonly DiagnosticDescriptor ReverseMapTargetMissing = new(
        "DWARF052",
        "[ReverseMap] has no inverse mapping method",
        "{0}",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf052");

    public static readonly DiagnosticDescriptor GenericMapperMethodUnsupported = new(
        "DWARF053",
        "Generic mapping methods are not supported",
        "Generic mapping methods are not supported: '{0}' declares type parameter(s); use a closed [GenerateMap<,>] or a non-generic partial method",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf053");

    public static readonly DiagnosticDescriptor GenericMapperClassUnsupported = new(
        "DWARF054",
        "[DwarfMapper] is not supported on generic classes",
        "[DwarfMapper] is not supported on generic classes: '{0}' is generic; declare the mapper on a non-generic class",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf054");

    // Info: a single mapper resolving a very large number of members. All extraction runs in the syntax
    // transform, so an enormous mapper can add IDE/compile latency. High threshold → fires only for genuine
    // god-mappers; suppressible via dotnet_diagnostic.DWARF055.severity = none.
    public static readonly DiagnosticDescriptor MapperTooLarge = new(
        "DWARF055",
        "Mapper is very large; consider splitting it",
        "{0}",
        Category, DiagnosticSeverity.Info, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf055");

    // Warning (not Error): the configuration compiles, but a pair-scoped member attribute
    // ([MapProperty<S,T>] / [MapIgnore<T>]) matched no mapped pair, so it silently does nothing — almost
    // always a typo'd type argument or a missing [GenerateMap]. Surfaced rather than ignored.
    public static readonly DiagnosticDescriptor PairScopedNoMatch = new(
        "DWARF056",
        "Pair-scoped attribute matches no mapped pair",
        "{0}",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: HelpBase + "dwarf056");
}
