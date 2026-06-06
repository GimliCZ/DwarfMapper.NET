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
}
