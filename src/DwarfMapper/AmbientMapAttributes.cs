// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Emitted by the generator (one per ambient-registered map) onto the declaring assembly: a machine-
///     readable manifest of which maps that assembly self-registers. The validation root reads these from
///     every referenced assembly to verify cross-assembly linkage at compile time (DWARF061). Hand-authoring
///     is not required.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class DwarfProvidesMapAttribute : Attribute
{
    /// <summary>
    ///     Declares that this assembly registers a map from <paramref name="source" /> to
    ///     <paramref name="destination" />.
    /// </summary>
    public DwarfProvidesMapAttribute(Type source, Type destination)
    {
        Source = source;
        Destination = destination;
    }

    /// <summary>The map source type.</summary>
    public Type Source { get; }

    /// <summary>The map destination type.</summary>
    public Type Destination { get; }
}

/// <summary>
///     Emitted by the generator onto the declaring assembly: a manifest of the cross-assembly maps that
///     assembly consumes through <see cref="IDwarfMapper" /> (auto-detected from call sites and/or declared via
///     <see cref="UsesMapAttribute" />). The validation root cross-checks these against the available
///     <see cref="DwarfProvidesMapAttribute" /> set.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class DwarfRequiresMapAttribute : Attribute
{
    /// <summary>
    ///     Declares that this assembly consumes a map from <paramref name="source" /> to
    ///     <paramref name="destination" /> through the ambient facade.
    /// </summary>
    public DwarfRequiresMapAttribute(Type source, Type destination)
    {
        Source = source;
        Destination = destination;
    }

    /// <summary>The required map source type.</summary>
    public Type Source { get; }

    /// <summary>The required map destination type.</summary>
    public Type Destination { get; }
}

/// <summary>
///     Explicitly declares that this assembly/type consumes the ambient map <typeparamref name="TSource" /> -&gt;
///     <typeparamref name="TDestination" />. Use when the consumption can't be auto-detected from a direct
///     <see cref="IDwarfMapper" /> call site (e.g. the source is handled as a base type or <c>object</c>). The
///     generator folds these into the <see cref="DwarfRequiresMapAttribute" /> manifest.
/// </summary>
/// <typeparam name="TSource">The consumed map's source type.</typeparam>
/// <typeparam name="TDestination">The consumed map's destination type.</typeparam>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UsesMapAttribute<TSource, TDestination> : Attribute
{
}

/// <summary>Non-generic form of <see cref="UsesMapAttribute{TSource,TDestination}" />.</summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UsesMapAttribute : Attribute
{
    /// <summary>
    ///     Declares that this assembly/type consumes the ambient map from <paramref name="source" />
    ///     to <paramref name="destination" />.
    /// </summary>
    public UsesMapAttribute(Type source, Type destination)
    {
        Source = source;
        Destination = destination;
    }

    /// <summary>The consumed map source type.</summary>
    public Type Source { get; }

    /// <summary>The consumed map destination type.</summary>
    public Type Destination { get; }
}

/// <summary>
///     Marks the composition-root assembly (the one that references every map-providing and map-consuming
///     assembly). Only in this compilation does the generator perform the whole-graph cross-assembly linkage
///     validation (DWARF061) and emit <c>DwarfMap.Validate()</c>. Mid-tier assemblies see only a partial graph
///     and therefore only emit manifests. Use exactly ONE validation root per consumer graph — the generated
///     <c>DwarfMap</c> and <c>ValidateDwarfMaps</c> have fixed names, so two roots referenced by a single
///     consumer would collide (CS0433).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperValidationRootAttribute : Attribute
{
    /// <summary>
    ///     When true, the root emits a module initializer that calls <c>DwarfMap.Validate()</c> as the
    ///     root module loads, turning a missing ambient map into a fail-fast startup exception. Default false.
    ///     <para>
    ///         Validation automatically follows the link flow: it checks exactly the ambient maps the graph
    ///         actually consumes (auto-detected from call sites and <see cref="UsesMapAttribute{TSource,TDestination}" />),
    ///         so a pair used in both directions is validated both ways (round-trip) and a one-way use is validated
    ///         one-way — no configuration.
    ///     </para>
    ///     <para>
    ///         Caveat: <c>Validate()</c> force-registers the root's OWN maps first, so those always validate.
    ///         A <b>referenced provider assembly</b> registers in its own module initializer, which the CLR runs
    ///         lazily on first access — a compile-time reference does NOT force it to load. So AutoValidate can
    ///         false-positive at startup for a correctly-wired <i>cross-assembly</i> map whose provider hasn't been
    ///         touched yet. For cross-assembly maps prefer <c>services.AddDwarfMappers().ValidateDwarfMaps()</c> or
    ///         call <c>DwarfMap.Validate()</c> after the provider assemblies are loaded.
    ///     </para>
    /// </summary>
    public bool AutoValidate { get; set; }
}
