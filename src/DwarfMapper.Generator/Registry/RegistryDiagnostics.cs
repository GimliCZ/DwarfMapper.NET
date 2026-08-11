// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Registry;

/// <summary>
///     Diagnostics for the <c>[MapTo]</c> registry generator. Kept in a SEPARATE class from
///     <c>DiagnosticDescriptors</c> because the DWARF0xx self-validation scans key on
///     <c>^DWARF\d{3}$</c> and these carry a <c>DWARFR</c> prefix. They are NOT unvalidated, though:
///     Scan1f/1g/1h in <c>AssemblyScanTests</c> enforce the same descriptor ↔ AnalyzerReleases sync
///     (both directions, plus severity) for this class that Scan1a/1b/1c enforce for that one.
///     A later unification could fold them into the DWARF0xx scheme.
/// </summary>
internal static class RegistryDiagnostics
{
    private const string Category = "DwarfMapper.Registry";

    public static readonly DiagnosticDescriptor InvalidTarget = new(
        "DWARFR01",
        "Invalid [MapTo] target",
        "[MapTo] target {0} is not a mappable type (must be a non-abstract class or struct, and not the source type itself)",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor UnmappedDestination = new(
        "DWARFR02",
        "Destination member is not mapped",
        "Destination member {0} has no source member (add a source member, a [MapProperty] binding, or remove it)",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ConflictingSource = new(
        "DWARFR03",
        "Conflicting sources for one destination member",
        "Destination member {0} is claimed by more than one source member — give them distinct positional [MapProperty] names",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MapPropertyArity = new(
        "DWARFR04",
        "[MapProperty] value count does not match the targets",
        "[MapProperty] on {0} must have either one value (all targets) or exactly one value per [MapTo] target, in order",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NoConversion = new(
        "DWARFR05",
        "No conversion between mapped members",
        "No built-in conversion for {0}; the source and destination member types are incompatible (use the [DwarfMapper] class model for a custom converter)",
        Category, DiagnosticSeverity.Error, true);

    // Two [MapTo] targets whose SIMPLE names collide (Foo.Order and Bar.Order) both generate `ToOrder(this Src)`
    // in one static class → CS0111 out of generated code. The MapTo<TTarget>() dispatcher is fine (it keys on
    // typeof), so only the per-target convenience methods clash.
    public static readonly DiagnosticDescriptor DuplicateTargetMethodName = new(
        "DWARFR08",
        "Two [MapTo] targets generate the same method name",
        "{0}",
        Category, DiagnosticSeverity.Error, true);

    // The registry constructs targets with `new T { … }`, which requires an accessible parameterless ctor. A
    // ctor-only target also has no writable members, so the completeness gate stays silent and the failure
    // surfaced only as CS1729 out of generated code.
    public static readonly DiagnosticDescriptor NoParameterlessConstructor = new(
        "DWARFR09",
        "[MapTo] target has no accessible parameterless constructor",
        "[MapTo] target {0} has no public parameterless constructor; the registry constructs targets with an object initializer — add one, or use the [DwarfMapper] class model (which supports constructor mapping)",
        Category, DiagnosticSeverity.Error, true);

    // Info, mirroring DWARF038 in the class model: the conversion is legal and implicit in C#, so making it an
    // error would reject code that compiles fine today — but it loses precision above the mantissa, and the
    // whole point is that the registry must not be SILENT where the class model warns.
    public static readonly DiagnosticDescriptor LossyImplicitConversion = new(
        "DWARFR07",
        "Lossy implicit numeric conversion",
        "Implicit conversion {0} crosses numeric categories and loses precision for large magnitudes (the [DwarfMapper] class model reports this as DWARF038)",
        Category, DiagnosticSeverity.Info, true);

    public static readonly DiagnosticDescriptor RecursiveNesting = new(
        "DWARFR06",
        "Recursive nested mapping is not supported by the registry",
        "Nested mapping for {0} is recursive; the registry front door does not thread a reference context — use the [DwarfMapper] class model (ReferenceHandling/OnCycle) for cyclic graphs",
        Category, DiagnosticSeverity.Error, true);
}
