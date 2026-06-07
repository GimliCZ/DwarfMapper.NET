// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
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
    EquatableArray<SynthesizedMethod> SynthesizedMethods) : IEquatable<MapperClassModel>
{
    public string HintName => string.IsNullOrEmpty(Namespace) ? ClassName : $"{Namespace}.{ClassName}";

    public bool HasBlockingError => Diagnostics.Any(d => d.IsError);
}
