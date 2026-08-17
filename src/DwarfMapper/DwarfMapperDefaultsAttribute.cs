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
///     <para>
///         <b>Not only <c>[DwarfMapper]</c> classes:</b> <c>AutoMatchMembers</c> is a trust boundary,
///         not a house style, so the <c>[MapTo]</c> registry front door honours it too — a same-named
///         destination it would otherwise auto-wire is refused with <c>DWARFR10</c>, the registry counterpart
///         of <c>DWARF072</c>. That front door has no mapper class of its own, so the assembly default is its
///         whole option list rather than a fallback layer under one.
///     </para>
/// </summary>
[DwarfSurface(SurfaceCategory.ConsumerDirective)]
// The same per-option fixtures as [DwarfMapper], for the same reason: these are the layered form of the very
// same options, and one fixture per element cannot ask twelve different questions. AutoMatchMembers and
// RegisterCollectionShapes are observable against the flat pair and therefore demand nothing.
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.AutoNest), ProbeKey = "nested-pair")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.AllowNonPublic), ProbeKey = "internal-member")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.NameConvention), ProbeKey = "snake-case-member")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.CaseInsensitive), ProbeKey = "case-mismatched-member")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.IgnoreObsoleteMembers), ProbeKey = "obsolete-member")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.SkipNullSourceMembers),
    ProbeKey = "nullable-source-nonnull-target")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.NullStrategy), ProbeKey = "nullable-value-to-nonnull")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.RequiredMapping), ProbeKey = "unconsumed-source-member")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.EnumStrategy), ProbeKey = "divergent-order-enums")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.EnumStringSource), ProbeKey = "described-enum-to-string")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.NullCollections), ProbeKey = "nullable-collection-rebuild")]
[DwarfSurfaceProbe(nameof(DwarfMapperDefaultsAttribute.ImplicitConversions), ProbeKey = "narrowing-conversion")]
[DwarfSurfaceProbe(constructorArity: 0,
    Unmeasured = "a bare [assembly: DwarfMapperDefaults] selects every option's default, so it changes no "
                 + "mapper's behaviour anywhere — silent by construction rather than by anything the "
                 + "generator decided. The twelve option cases above carry this element's questions.")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperDefaultsAttribute : Attribute
{
    /// <inheritdoc cref="DwarfMapperAttribute.CaseInsensitive" />
    public bool CaseInsensitive { get; set; }

    /// <inheritdoc cref="DwarfMapperAttribute.NameConvention" />
    public NameConvention NameConvention { get; set; } = NameConvention.Exact;

    /// <inheritdoc cref="DwarfMapperAttribute.EnumStrategy" />
    public EnumStrategy EnumStrategy { get; set; } = EnumStrategy.ByName;

    /// <inheritdoc cref="DwarfMapperAttribute.EnumStringSource" />
    public EnumStringSource EnumStringSource { get; set; } = EnumStringSource.Attribute;

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

    /// <inheritdoc cref="DwarfMapperAttribute.RegisterCollectionShapes" />
    public bool RegisterCollectionShapes { get; set; } = true;
}
