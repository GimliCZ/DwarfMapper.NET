// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Assembly-wide DwarfMapper options. Apply once with <c>[assembly: DwarfMapperOptions(...)]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperOptionsAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, the generated convenience extension methods (in the <c>DwarfMapper.Extensions</c>
    /// namespace) are emitted <c>public</c> — usable from <b>other assemblies</b> — for every pair whose source
    /// <b>and</b> destination types are both effectively public. A pair involving a non-public type stays
    /// assembly-internal to remain accessibility-safe (a public extension over an internal type would not
    /// compile). Defaults to <c>false</c> (all generated extensions are assembly-internal).
    /// <para>
    /// This is the opt-in for the common layered layout where mappers/DTOs live in a library and are consumed
    /// from another project. Without it, call the mapper instance method or use <c>AddDwarfMappers()</c> DI
    /// across assemblies.
    /// </para>
    /// </summary>
    public bool PublicExtensions { get; set; }
}
