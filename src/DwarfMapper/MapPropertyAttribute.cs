// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Explicitly maps a source member to a differently-named destination member, overriding name-based
///     matching for that destination. Two placements:
///     <list type="bullet">
///         <item>On a <b>mapping method</b> (class model): <c>[MapProperty(sourceName, targetName)]</c>.</item>
///         <item>
///             On a <b>source member</b> (the <c>[MapTo]</c> registry): <c>[MapProperty(destinationMember)]</c> —
///             the annotated member supplies that destination. Stack the attribute to bind across multiple
///             <c>[MapTo]</c> targets (positional, in source order).
///         </item>
///     </list>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>Method-placement form: maps source member <paramref name="source" /> to <paramref name="target" />.</summary>
    /// <param name="source">Name of the source member to read from.</param>
    /// <param name="target">Name of the destination member to assign.</param>
    public MapPropertyAttribute(string source, string target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>
    ///     Member-placement form (the <c>[MapTo]</c> registry): the annotated member supplies the destination
    ///     member <paramref name="target" />.
    /// </summary>
    /// <param name="target">Name of the destination member this source member supplies.</param>
    public MapPropertyAttribute(string target)
    {
        Source = target;
        Target = target;
    }

    /// <summary>Name of the source member to read from.</summary>
    public string Source { get; }

    /// <summary>Name of the destination member to assign.</summary>
    public string Target { get; }

    /// <summary>
    ///     Optional name of a conversion method declared in the mapper class used to
    ///     transform the source value into the destination type. The method must take
    ///     the source member type and return the destination member type.
    /// </summary>
    public string? Use { get; set; }

    /// <summary>
    ///     Optional constant value coalesced onto the source — emitted verbatim as <c>source ?? value</c>
    ///     (so it takes effect when the source member is null; on a non-nullable source the <c>??</c> is simply
    ///     a harmless no-op; on a non-nullable <b>value-type</b> source such as <c>int</c>, <c>??</c> is a C# compile
    ///     error (CS0019) — use it only on a nullable source). The value must be assignable to the destination type (else
    ///     <c>DWARF049</c>).
    ///     Direct-assignable members only — not combinable with a <see cref="Use" /> converter.
    /// </summary>
    public object? NullSubstitute { get; set; }

    /// <summary>
    ///     Optional name of a <c>bool</c>-returning predicate method on the mapper that takes the source; the
    ///     assignment is emitted only when it returns true (<c>if (Predicate(src)) target.Member = …;</c>),
    ///     otherwise the destination keeps its default. An invalid predicate is <c>DWARF050</c>.
    /// </summary>
    public string? When { get; set; }

    /// <summary>
    ///     Optional .NET format string applied when converting the source to a <b>string</b> destination —
    ///     emitted as <c>source.ToString(format, CultureInfo.InvariantCulture)</c>. E.g. <c>"yyyy-MM-dd"</c> for
    ///     a <c>DateTime</c>, <c>"F2"</c> for a <c>decimal</c>, <c>"N0"</c> for an <c>int</c>.
    ///     <para>
    ///         The provider is always <see cref="System.Globalization.CultureInfo.InvariantCulture" /> — by
    ///         design, so the formatted output is stable across deployments and threads rather than shifting with
    ///         the ambient culture (the same reason every generated <c>Parse</c>/<c>ToString</c> is invariant).
    ///         Only valid when the destination member is <c>string</c> and the source is
    ///         <see cref="System.IFormattable" />, and not combinable with <see cref="Use" />; otherwise
    ///         <c>DWARF073</c>.
    ///     </para>
    /// </summary>
    public string? StringFormat { get; set; }
}
