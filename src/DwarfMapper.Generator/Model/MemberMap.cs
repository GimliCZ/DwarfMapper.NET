// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>
/// One resolved destination&lt;-source member assignment. <see cref="ConverterMethod"/>,
/// when set, transforms the value; otherwise <see cref="NullHandling"/> controls
/// how a nullable value-type source is unwrapped.
/// </summary>
public sealed record MemberMap(
    string TargetName,
    string SourceName,
    string? ConverterMethod = null,
    NullHandling NullHandling = NullHandling.None) : System.IEquatable<MemberMap>;
