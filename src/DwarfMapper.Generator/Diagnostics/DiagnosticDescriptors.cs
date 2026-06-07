// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

public static class DiagnosticDescriptors
{
    private const string Category = "DwarfMapper";

    public static readonly DiagnosticDescriptor MapperNotPartial = new(
        "DWARF002",
        "Mapper type must be partial",
        "Mapper type '{0}' must be declared 'partial'",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidMapMethod = new(
        "DWARF003",
        "Invalid mapping method signature",
        "Mapping method '{0}' must be a partial instance method with a non-void return type and exactly one parameter",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedMember = new(
        "DWARF001",
        "Destination member is not mapped",
        "Destination member '{0}' has no matching source member; map it or annotate the method with [MapIgnore(\"{0}\")]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoImplicitConversion = new(
        "DWARF005",
        "No implicit conversion between mapped members",
        "Cannot map to '{0}': no implicit conversion and no usable conversion method; declare a mapping method for the types or use [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        "DWARF006",
        "Destination type is not constructible",
        "Destination type '{0}' must have an accessible parameterless constructor",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReadOnlyDestinationMember = new(
        "DWARF007",
        "Destination member is read-only",
        "Destination member '{0}' is read-only; a matching source value cannot be assigned and would be lost (annotate the method with [MapIgnore(\"{0}\")] if intentional)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousMatch = new(
        "DWARF010",
        "Ambiguous source member",
        "Destination member '{0}' matches more than one source member under case-insensitive matching; rename or use [MapProperty]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MapPropertyUnknownTarget = new(
        "DWARF008",
        "MapProperty target not found",
        "[MapProperty] destination member '{0}' does not exist or is not writable",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MapPropertyUnknownSource = new(
        "DWARF009",
        "MapProperty source not found",
        "[MapProperty] source member '{0}' does not exist or is not readable",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateMapProperty = new(
        "DWARF011",
        "Duplicate explicit mapping",
        "Destination member '{0}' has more than one [MapProperty] mapping; keep only one",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IgnoreExplicitConflict = new(
        "DWARF012",
        "Conflicting [MapIgnore] and [MapProperty]",
        "Destination member '{0}' is both ignored via [MapIgnore] and mapped via [MapProperty]; remove one",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousConversion = new(
        "DWARF013",
        "Ambiguous conversion method",
        "Cannot map to '{0}': more than one mapping method converts these types; disambiguate with [MapProperty(Use = ...)]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseMethodInvalid = new(
        "DWARF014",
        "Conversion method not found",
        "[MapProperty(Use = ...)] method '{0}' was not found or has an incompatible signature (it must take the source member type and return the destination member type)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncompleteEnumMapping = new(
        "DWARF015",
        "Incomplete enum mapping",
        "Enum member '{0}' has no destination member of the same name (by-name enum mapping); add it or use EnumStrategy.ByValue",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FlattenRootInvalid = new(
        "DWARF016",
        "Invalid flatten source",
        "[Flatten] source member '{0}' does not exist, is not readable, or has no readable sub-members",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousFlatten = new(
        "DWARF017",
        "Ambiguous flattened member",
        "Destination member '{0}' is flattened from more than one source member; use [MapProperty] to disambiguate",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidHook = new(
        "DWARF018",
        "Invalid mapping hook signature",
        "Hook method '{0}' must be void; [BeforeMap] takes one parameter, [AfterMap] takes one or two",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotProjectable = new(
        "DWARF019",
        "Member is not projectable",
        "Projection member '{0}' is not directly assignable; IQueryable projections allow only direct member assignment (map it with a runtime mapper)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RoundTripNoInverse = new(
        "DWARF020",
        "No inverse for [RoundTrip]",
        "[RoundTrip] method '{0}' has no inverse mapping method (a partial method with the source/destination types swapped)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RoundTripAmbiguousInverse = new(
        "DWARF021",
        "Ambiguous inverse for [RoundTrip]",
        "[RoundTrip] method '{0}' has more than one candidate inverse mapping method",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
