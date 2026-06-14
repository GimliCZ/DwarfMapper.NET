// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// On a forward mapping method <c>T Forward(S s)</c>, declares that a separately-declared inverse partial
/// method <c>S Back(T t)</c> should inherit the <b>inverted</b> simple <c>[MapProperty]</c> renames
/// (a rename <c>A → B</c> becomes <c>B → A</c>). Only single-member renames invert automatically;
/// non-invertible forward configuration (<c>Use=</c> converters, dotted paths, <c>NullSubstitute</c>,
/// <c>When</c>) is reported as <c>DWARF051</c> so you can declare the reverse explicitly. If no inverse
/// partial method exists, the forward method reports <c>DWARF052</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ReverseMapAttribute : Attribute
{
}
