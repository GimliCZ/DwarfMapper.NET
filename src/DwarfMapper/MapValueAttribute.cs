// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Assigns a <b>constant</b> or <b>computed</b> value to a destination member that has no corresponding
///     source — and counts that member as mapped (suppressing <c>DWARF001</c>).
///     <list type="bullet">
///         <item>
///             <description>
///                 Constant: <c>[MapValue(nameof(Dto.Source), "api-v2")]</c> emits <c>Source = "api-v2"</c>. The value
///                 must be an attribute-legal constant (string, bool, char, numeric, enum, or null) assignable to the
///                 destination type — otherwise <c>DWARF040</c>.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Computed: <c>[MapValue(nameof(Dto.CreatedAt), Use = nameof(Now))]</c> emits <c>CreatedAt = Now()</c>,
///                 where <c>Now</c> is a <b>parameterless</b> method on the mapper whose return type is assignable to the
///                 destination — otherwise <c>DWARF041</c>.
///             </description>
///         </item>
///     </list>
///     Conflicts with <c>[MapProperty]</c>/<c>[MapIgnore]</c> on the same target, an unknown target, or a
///     missing value/<see cref="Use" /> are reported as <c>DWARF042</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapValueAttribute : Attribute
{
    /// <summary>Assigns a constant <paramref name="value" /> to <paramref name="target" />.</summary>
    public MapValueAttribute(string target, object? value)
    {
        Target = target;
        Value = value;
    }

    /// <summary>Declares a <paramref name="target" /> whose value comes from the <see cref="Use" /> method.</summary>
    public MapValueAttribute(string target)
    {
        Target = target;
    }

    /// <summary>Name of the destination member to assign.</summary>
    public string Target { get; }

    /// <summary>The constant value (when the two-argument constructor is used).</summary>
    public object? Value { get; }

    /// <summary>Name of a parameterless method on the mapper that produces the value.</summary>
    public string? Use { get; set; }
}
