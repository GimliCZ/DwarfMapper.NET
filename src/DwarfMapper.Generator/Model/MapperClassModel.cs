// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;

namespace DwarfMapper.Generator.Model;

/// <summary>The full, value-equatable description of one [DwarfMapper] class.</summary>
public sealed record MapperClassModel(
    string Namespace,
    string ClassName,
    string Accessibility,
    EquatableArray<MapMethodModel> Methods,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<SynthesizedMethod> SynthesizedMethods,
    EquatableArray<RoundTripPair> RoundTrips,
    /// <summary>
    /// Class-level <c>[DwarfMapper(GenerateExtensions = …)]</c> value (default <c>true</c>). When false,
    /// the aggregate facade emitter skips this mapper's convenience extension methods.
    /// </summary>
    bool GenerateExtensions = true,
    /// <summary>
    /// Whether the mapper class has an accessible parameterless constructor, so the aggregate facade can
    /// cache a <c>new()</c> singleton of it. A mapper with only parameterized constructors is skipped by the
    /// facade (its convenience extensions can't be backed by a cached instance).
    /// </summary>
    bool HasParameterlessCtor = true) : IEquatable<MapperClassModel>
{
    public string HintName => string.IsNullOrEmpty(Namespace) ? ClassName : $"{Namespace}.{ClassName}";

    public bool HasBlockingError => Diagnostics.Any(d => d.IsError);
}
