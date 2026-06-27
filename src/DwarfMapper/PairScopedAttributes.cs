// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Configures a member mapping for a specific <c>(TSource → TTarget)</c> pair <b>from the class</b>, with no
/// <c>partial</c> method to attach to. This is the pair-scoped counterpart of the method-level
/// <see cref="MapPropertyAttribute"/>: apply it next to a <c>[GenerateMap&lt;TSource, TTarget&gt;]</c> so a
/// fully-configured mapper can be declared with no methods at all.
/// <code>
/// [DwarfMapper]
/// [GenerateMap&lt;Place, PlaceDto&gt;]
/// [MapProperty&lt;Person, PersonDto&gt;(nameof(Person.Name), nameof(PersonDto.FullName))]
/// public partial class Mappers { }   // no methods; Person -> PersonDto is even mapped as a nested list element
/// </code>
/// <para>
/// The linkage applies <b>wherever</b> the <c>(TSource → TTarget)</c> pair is mapped — a top-level
/// <c>[GenerateMap]</c> pair <b>or</b> an auto-synthesized nested/collection-element pair — unless a declared
/// <c>partial</c> method for that pair overrides it. A pair-scoped attribute that matches no mapped pair is
/// <c>DWARF056</c>.
/// </para>
/// </summary>
/// <typeparam name="TSource">The source type of the pair this linkage configures.</typeparam>
/// <typeparam name="TTarget">The destination type of the pair this linkage configures.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute<TSource, TTarget> : Attribute
{
    /// <summary>Creates a pair-scoped explicit member mapping.</summary>
    /// <param name="source">Name of the source member to read from (a dotted path is allowed).</param>
    /// <param name="target">Name of the destination member to assign.</param>
    public MapPropertyAttribute(string source, string target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>Name of the source member to read from.</summary>
    public string Source { get; }

    /// <summary>Name of the destination member to assign.</summary>
    public string Target { get; }

    /// <summary>
    /// Optional name of a conversion method on the mapper that transforms the source value into the
    /// destination type (takes the source member type, returns the destination member type).
    /// </summary>
    public string? Use { get; set; }

    /// <summary>Optional constant coalesced onto a nullable source — emitted as <c>source ?? value</c>.</summary>
    public object? NullSubstitute { get; set; }

    /// <summary>Optional <c>bool</c>-returning predicate method (takes the source) that guards the assignment.</summary>
    public string? When { get; set; }
}

/// <summary>
/// Ignores a destination member of <typeparamref name="TTarget"/> <b>from the class</b>, for any pair that maps
/// to <typeparamref name="TTarget"/> — the pair-scoped counterpart of the method-level
/// <see cref="MapIgnoreAttribute"/>. Use it to suppress the completeness error (<c>DWARF001</c>) for a member
/// of a <c>[GenerateMap]</c> or nested pair without writing a <c>partial</c> method. A pair-scoped attribute
/// that matches no mapped pair is <c>DWARF056</c>.
/// </summary>
/// <typeparam name="TTarget">The destination type whose member is ignored.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapIgnoreAttribute<TTarget> : Attribute
{
    /// <summary>Ignores the destination member named <paramref name="member"/> on <typeparamref name="TTarget"/>.</summary>
    public MapIgnoreAttribute(string member) => Member = member;

    /// <summary>Name of the destination member to leave unmapped.</summary>
    public string Member { get; }
}

/// <summary>
/// Assigns a <b>constant</b> or <b>computed</b> value to a source-less member of <typeparamref name="TTarget"/>
/// <b>from the class</b> — the pair-scoped counterpart of the method-level <see cref="MapValueAttribute"/>. It
/// counts the member as mapped (suppressing <c>DWARF001</c>), so a <c>[GenerateMap]</c> pair whose target has a
/// member with no source can be completed with no <c>partial</c> method. A pair-scoped attribute that matches no
/// mapped pair is <c>DWARF056</c>.
/// </summary>
/// <typeparam name="TTarget">The destination type whose member is assigned.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapValueAttribute<TTarget> : Attribute
{
    /// <summary>Assigns a constant <paramref name="value"/> to <paramref name="target"/> on <typeparamref name="TTarget"/>.</summary>
    public MapValueAttribute(string target, object? value)
    {
        Target = target;
        Value = value;
    }

    /// <summary>Declares a <paramref name="target"/> whose value comes from the <see cref="Use"/> method.</summary>
    public MapValueAttribute(string target) => Target = target;

    /// <summary>Name of the destination member to assign.</summary>
    public string Target { get; }

    /// <summary>The constant value (when the two-argument constructor is used).</summary>
    public object? Value { get; }

    /// <summary>Name of a parameterless method on the mapper that produces the value.</summary>
    public string? Use { get; set; }
}
