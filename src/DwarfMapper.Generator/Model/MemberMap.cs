// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>One resolved destination&lt;-source member assignment.</summary>
public sealed record MemberMap(string TargetName, string SourceName) : System.IEquatable<MemberMap>;
