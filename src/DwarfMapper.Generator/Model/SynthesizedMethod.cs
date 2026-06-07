// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// A generator-synthesized helper method (e.g. an enum conversion) emitted into
/// the mapper class. <see cref="Code"/> is the full, indented method declaration.
/// </summary>
public sealed record SynthesizedMethod(string Name, string Code) : System.IEquatable<SynthesizedMethod>;
