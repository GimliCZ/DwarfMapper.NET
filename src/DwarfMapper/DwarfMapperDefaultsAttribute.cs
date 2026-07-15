// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Assembly-wide default options for every <c>[DwarfMapper]</c> class in the assembly. A value set here is
///     the fallback for each mapper that does not set that option itself; a mapper's own
///     <c>[DwarfMapper(...)]</c> value always wins. Precedence: <b>mapper &gt; assembly defaults &gt; built-in
///     default</b>.
///     <para>
///         Use it to establish one house style once — <c>[assembly: DwarfMapperDefaults(NameConvention =
///         NameConvention.Flexible, ImplicitConversions = false)]</c> — instead of repeating the same options on
///         every mapper. Only the policy options are layered (naming, conversion, null, and completeness
///         strategy); per-graph knobs (<c>MaxDepth</c>, <c>ReferenceHandling</c>, <c>OnCycle</c>) stay per-mapper
///         because they are usually specific to a given object graph.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperDefaultsAttribute : Attribute
{
    /// <inheritdoc cref="DwarfMapperAttribute.CaseInsensitive" />
    public bool CaseInsensitive { get; set; }

    /// <inheritdoc cref="DwarfMapperAttribute.NameConvention" />
    public NameConvention NameConvention { get; set; } = NameConvention.Exact;

    /// <inheritdoc cref="DwarfMapperAttribute.EnumStrategy" />
    public EnumStrategy EnumStrategy { get; set; } = EnumStrategy.ByName;

    /// <inheritdoc cref="DwarfMapperAttribute.NullStrategy" />
    public NullStrategy NullStrategy { get; set; } = NullStrategy.Throw;

    /// <inheritdoc cref="DwarfMapperAttribute.NullCollections" />
    public NullCollectionStrategy NullCollections { get; set; } = NullCollectionStrategy.AsEmpty;

    /// <inheritdoc cref="DwarfMapperAttribute.ImplicitConversions" />
    public bool ImplicitConversions { get; set; } = true;

    /// <inheritdoc cref="DwarfMapperAttribute.RequiredMapping" />
    public RequiredMappingStrategy RequiredMapping { get; set; } = RequiredMappingStrategy.Target;

    /// <inheritdoc cref="DwarfMapperAttribute.AllowNonPublic" />
    public bool AllowNonPublic { get; set; }

    /// <inheritdoc cref="DwarfMapperAttribute.AutoNest" />
    public bool AutoNest { get; set; } = true;

    /// <inheritdoc cref="DwarfMapperAttribute.AutoMatchMembers" />
    public bool AutoMatchMembers { get; set; } = true;

    /// <inheritdoc cref="DwarfMapperAttribute.IgnoreObsoleteMembers" />
    public bool IgnoreObsoleteMembers { get; set; }

    /// <inheritdoc cref="DwarfMapperAttribute.SkipNullSourceMembers" />
    public bool SkipNullSourceMembers { get; set; }
}
