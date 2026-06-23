// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Registry;

/// <summary>
/// EXPERIMENTAL (v23 prototype). Diagnostics for the <c>[MapTo]</c> type-registry generator. Kept in a
/// SEPARATE class from <c>DiagnosticDescriptors</c> on purpose: the DWARF0xx self-validation scans reflect
/// only over that class, so these prototype descriptors (prefix <c>DWARFR</c>) are intentionally invisible
/// to them. If the registry model graduates from prototype, fold these into the DWARF0xx scheme.
/// </summary>
// Prototype diagnostics are deliberately NOT release-tracked (kept out of AnalyzerReleases.*.md and the
// DWARF0xx self-validation scans). Suppress the release-tracking analyzer for this experimental file.
#pragma warning disable RS2000 // Add analyzer diagnostic IDs to analyzer release
#pragma warning disable RS2001 // Ensure up-to-date entry for analyzer diagnostic IDs
internal static class RegistryDiagnostics
{
    private const string Category = "DwarfMapper.Registry";

    public static readonly DiagnosticDescriptor InvalidTarget = new(
        "DWARFR01",
        "Invalid [MapTo] target",
        "[MapTo] target {0} is not a mappable type (must be a non-abstract class or struct, and not the source type itself)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedDestination = new(
        "DWARFR02",
        "Destination member is not mapped",
        "Destination member {0} has no source member (add a source member, a [MapProperty] binding, or remove it)",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingSource = new(
        "DWARFR03",
        "Conflicting sources for one destination member",
        "Destination member {0} is claimed by more than one source member — give them distinct positional [MapProperty] names",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MapPropertyArity = new(
        "DWARFR04",
        "[MapProperty] value count does not match the targets",
        "[MapProperty] on {0} must have either one value (all targets) or exactly one value per [MapTo] target, in order",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
