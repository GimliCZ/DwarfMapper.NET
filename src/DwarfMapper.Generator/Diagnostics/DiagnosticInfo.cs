// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

/// <summary>Value-equatable diagnostic carrier; converted to a real <see cref="Diagnostic" /> at output time.</summary>
public sealed record DiagnosticInfo(
    // IMPORTANT — descriptor identity equality:
    // C# record equality uses REFERENCE identity for DiagnosticDescriptor (it is a class with no custom Equals).
    // This is correct ONLY because callers MUST pass the static readonly singletons from DiagnosticDescriptors.
    // Constructing a DiagnosticDescriptor inline at call sites (e.g. new DiagnosticDescriptor("DWARF005", ...))
    // would create a distinct instance and silently break Roslyn's incremental-cache equality checks,
    // causing the generator to re-run unnecessarily or produce stale output.
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    string MessageArg,
    // Optional per-instance severity override. Used by DWARF038 (implicit-conversion suggestion):
    // Info by default, escalated to Error when [DwarfMapper(ImplicitConversions = false)] is set.
    // Null → use the descriptor's DefaultSeverity. Part of value equality (so the incremental cache
    // distinguishes a suggestion from an error for the same descriptor).
    DiagnosticSeverity? SeverityOverride = null,
    // The destination member this diagnostic is about, surfaced to code fixes as the "Member" property.
    // Code fixes used to recover it by PARSING the message between the first pair of quotes, which couples a
    // fix to the exact wording of a human-readable string: rewording a message (or localising it) silently
    // breaks the fix with no compile error and no failing test. A plain string keeps the record
    // value-equatable, which the incremental cache depends on — hence a single field rather than a dictionary.
    string? MemberName = null)
{
    /// <summary>Property bag key under which <see cref="MemberName" /> reaches a CodeFixProvider.</summary>
    public const string MemberPropertyKey = "Member";

    public bool IsError => (SeverityOverride ?? Descriptor.DefaultSeverity) == DiagnosticSeverity.Error;

    public Diagnostic ToDiagnostic()
    {
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        var properties = MemberName is null
            ? null
            : ImmutableDictionary<string, string?>.Empty.Add(MemberPropertyKey, MemberName);

        return SeverityOverride is { } sev
            ? Diagnostic.Create(Descriptor, location, sev, null, properties, MessageArg)
            : Diagnostic.Create(Descriptor, location, properties, MessageArg);
    }
}
