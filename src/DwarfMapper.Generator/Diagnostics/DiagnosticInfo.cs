// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Diagnostics;

/// <summary>Value-equatable diagnostic carrier; converted to a real <see cref="Diagnostic"/> at output time.</summary>
public sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, string MessageArg)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, MessageArg);

    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;
}
