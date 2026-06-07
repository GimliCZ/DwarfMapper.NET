// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// One resolved destination&lt;-source member assignment. When
/// <see cref="ConverterMethod"/> is non-null, the source value is passed through
/// that method (e.g. <c>Target = Convert(src.Source)</c>); otherwise it is
/// assigned directly.
/// </summary>
public sealed record MemberMap(string TargetName, string SourceName, string? ConverterMethod = null)
    : System.IEquatable<MemberMap>;
