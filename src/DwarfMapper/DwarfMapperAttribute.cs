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

    /// <summary>
    /// Strategy for enum-to-enum mapping. Defaults to <see cref="EnumStrategy.ByName"/>.
    /// </summary>
    public EnumStrategy EnumStrategy { get; set; } = EnumStrategy.ByName;

    /// <summary>
    /// How a nullable value-type source mapped to a non-nullable destination is
    /// handled when null. Defaults to <see cref="NullStrategy.Throw"/>.
    /// </summary>
    public NullStrategy NullStrategy { get; set; } = NullStrategy.Throw;

    /// <summary>
    /// When <c>true</c> (the default), a member whose type is a mappable object pair
    /// <c>(S, T)</c> with no declared mapper is automatically resolved by synthesizing
    /// a private nested mapper. Set to <c>false</c> to require explicit declarations
    /// for every nested type (the legacy opt-out behaviour, before auto-nesting became the default).
    /// </summary>
    public bool AutoNest { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, a <b>null source member never overwrites the destination's default</b>: for every
    /// nullable-source, post-construction-settable member the generator emits
    /// <c>if (src.Member is not null) dest.Member = …;</c>, so a default set in the destination's field
    /// initializer or constructor survives a null source. Defaults to <c>false</c>.
    /// <para>
    /// This is the equivalent of AutoMapper's
    /// <c>ForAllMembers(o =&gt; o.Condition((_, _, srcMember) =&gt; srcMember != null))</c> — the common
    /// "don't clobber data with nulls" guard when sanitizing/merging. Non-nullable value-type members
    /// (which can never be null) are unaffected; <c>required</c> and <c>init</c>-only members are always
    /// assigned (they cannot be deferred) and so are unaffected too.
    /// </para>
    /// </summary>
    public bool SkipNullSourceMembers { get; set; }

    /// <summary>
    /// Controls how a null source collection or dictionary is mapped.
    /// <para>
    /// <see cref="NullCollectionStrategy.AsEmpty"/> (default): a null source
    /// produces an empty target; the mapper never throws <see cref="System.NullReferenceException"/>
    /// for a null collection.
    /// </para>
    /// <para>
    /// <see cref="NullCollectionStrategy.AsNull"/>: a null source propagates as
    /// <c>null</c> on the target. The target member must be a nullable reference type.
    /// </para>
    /// </summary>
    public NullCollectionStrategy NullCollections { get; set; } = NullCollectionStrategy.AsEmpty;

    /// <summary>
    /// Controls how shared object references and cycles in the source graph are handled.
    /// <para>
    /// <see cref="ReferenceHandlingStrategy.None"/> (default): no identity map. Recursion-capable
    /// pairs still get a depth counter — cyclic data throws <see cref="DwarfMappingDepthException"/>
    /// at <see cref="MaxDepth"/>. Zero allocation overhead.
    /// </para>
    /// <para>
    /// <see cref="ReferenceHandlingStrategy.Preserve"/>: full topology reconstruction — every distinct
    /// source node mapped once, all edges (shared/cycle) relinked. One small dictionary allocation
    /// per top-level <c>Map</c> call (keyed by reference identity via
    /// <c>ReferenceEqualityComparer.Instance</c>).
    /// </para>
    /// </summary>
    public ReferenceHandlingStrategy ReferenceHandling { get; set; } = ReferenceHandlingStrategy.None;

    /// <summary>
    /// How a reference cycle in the source data is handled while
    /// <see cref="ReferenceHandlingStrategy.None"/> is active (the default reference mode).
    /// <para>
    /// <see cref="OnCycleStrategy.Throw"/> (default): a cycle (or over-deep chain) throws a
    /// catchable <see cref="DwarfMappingDepthException"/> at <see cref="MaxDepth"/>.
    /// </para>
    /// <para>
    /// <see cref="OnCycleStrategy.SetNull"/>: the re-entrant back-edge is set to <c>null</c>
    /// (≡ <c>System.Text.Json</c> <c>IgnoreCycles</c>), producing a finite acyclic projection.
    /// </para>
    /// <para>
    /// Ignored under <see cref="ReferenceHandlingStrategy.Preserve"/> (cycles are reconstructed);
    /// configuring both reports <c>DWARF037</c>.
    /// </para>
    /// </summary>
    public OnCycleStrategy OnCycle { get; set; } = OnCycleStrategy.Throw;

    /// <summary>
    /// Controls whether non-lossless implicit type conversions between differently-typed members are
    /// allowed automatically. Defaults to <c>true</c> (permissive — today's behavior).
    /// <para>
    /// When <c>true</c>: lossless same-category widening (<c>int→long</c>, <c>float→double</c>) is silent;
    /// narrowing (<c>long→int</c>), cross-category (<c>int→double</c>, <c>int→string</c>) and parse
    /// (<c>string→int</c>) conversions are still applied but surface a <c>DWARF038</c> suggestion (Info)
    /// so they are never silent.
    /// </para>
    /// <para>
    /// When <c>false</c> (strict, Mapperly-style): those same non-lossless conversions become a
    /// <c>DWARF038</c> <b>build error</b> — you must opt in per member with
    /// <c>[MapProperty(..., Use = nameof(Method))]</c>. Lossless widening and identity still map freely.
    /// </para>
    /// </summary>
    public bool ImplicitConversions { get; set; } = true;

    /// <summary>
    /// Maximum recursion depth for recursion-capable auto-synthesized mappers (default 64).
    /// When a mapping reaches this depth, a <see cref="DwarfMappingDepthException"/> is thrown
    /// instead of a silent (uncatchable) <see cref="System.StackOverflowException"/>.
    /// <para>
    /// The depth counter applies only to pairs that can form a cycle in the type graph
    /// (self-referential or mutually-recursive types). Acyclic type graphs have zero overhead.
    /// </para>
    /// <para>
    /// Hard cap: <c>1000</c>. Values above 1000 are silently clamped to 1000.
    /// Default: <c>64</c> (matching System.Text.Json / AutoMapper defaults).
    /// </para>
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// Which side(s) of the mapping must be fully covered. Defaults to
    /// <see cref="RequiredMappingStrategy.Target"/> (today's behaviour — every destination member must be
    /// mapped). Set to <see cref="RequiredMappingStrategy.Both"/> to additionally require every source
    /// member to be read by some destination; an unconsumed source member then surfaces the
    /// <c>DWARF039</c> suggestion (Info). Suppress a specific member with
    /// <c>[MapIgnoreSource("Member")]</c>; escalate to a build error via
    /// <c>dotnet_diagnostic.DWARF039.severity = error</c> in <c>.editorconfig</c>.
    /// </summary>
    public RequiredMappingStrategy RequiredMapping { get; set; } = RequiredMappingStrategy.Target;

    /// <summary>
    /// Member-name matching strategy. Defaults to <see cref="NameConvention.Exact"/> (today's behaviour).
    /// <see cref="NameConvention.Flexible"/> matches across casing styles (<c>PascalCase</c> ↔
    /// <c>camelCase</c> ↔ <c>snake_case</c> ↔ <c>UPPER_CASE</c>) by normalizing names; a post-normalization
    /// collision is the build error <c>DWARF048</c>.
    /// </summary>
    public NameConvention NameConvention { get; set; } = NameConvention.Exact;

    /// <summary>
    /// When <c>true</c> (the default), the generator also emits convenience extension methods for this
    /// mapper's simple <c>TTarget Map(TSource)</c> methods — e.g. <c>order.ToOrderDto()</c> instead of
    /// <c>new OrderMapper().ToDto(order)</c>. They live in the <c>DwarfMapper.Extensions</c> namespace
    /// (add <c>using DwarfMapper.Extensions;</c> to use them), are backed by a cached, stateless mapper
    /// instance, and are assembly-internal. Set to <c>false</c> to suppress them for this mapper.
    /// <para>
    /// Only plain single-argument maps get an extension. Update-into, span, async-streaming, projection,
    /// derived-type dispatch, and methods with extra parameters are skipped, as are pairs whose generated
    /// name would collide.
    /// </para>
    /// </summary>
    public bool GenerateExtensions { get; set; } = true;
}
