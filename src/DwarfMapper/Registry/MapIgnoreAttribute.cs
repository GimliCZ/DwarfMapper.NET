// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Registry;

/// <summary>
/// EXPERIMENTAL (v23 prototype). Excludes the annotated source member from mapping for one target. Stack
/// it with <see cref="MapPropertyAttribute"/>; the directives are read <b>in source order, aligned
/// positionally to the targets in <c>[MapTo(...)]</c></b>. A single <c>[MapIgnore]</c> ignores the member
/// in every target; mixed (e.g. <c>[MapProperty("Name"), MapIgnore]</c>) maps it in some targets and
/// ignores it in others.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public sealed class MapIgnoreAttribute : Attribute
{
}
