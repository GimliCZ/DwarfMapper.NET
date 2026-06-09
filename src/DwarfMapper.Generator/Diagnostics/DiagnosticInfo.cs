// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

/// <summary>Value-equatable diagnostic carrier; converted to a real <see cref="Diagnostic"/> at output time.</summary>
public sealed record DiagnosticInfo(
    // IMPORTANT — descriptor identity equality:
    // C# record equality uses REFERENCE identity for DiagnosticDescriptor (it is a class with no custom Equals).
    // This is correct ONLY because callers MUST pass the static readonly singletons from DiagnosticDescriptors.
    // Constructing a DiagnosticDescriptor inline at call sites (e.g. new DiagnosticDescriptor("DWARF005", ...))
    // would create a distinct instance and silently break Roslyn's incremental-cache equality checks,
    // causing the generator to re-run unnecessarily or produce stale output.
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    string MessageArg)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, MessageArg);

    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;
}
