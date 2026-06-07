// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a partial class as a DwarfMapper. The generator implements the
/// partial mapping methods declared on it at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, source and destination member names are matched
    /// case-insensitively (ordinal-ignore-case). Defaults to <c>false</c>
    /// (exact, case-sensitive matching).
    /// </summary>
    public bool CaseInsensitive { get; set; }
}
