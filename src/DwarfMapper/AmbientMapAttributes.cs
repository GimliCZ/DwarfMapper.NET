// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Emitted by the generator (one per ambient-registered map) onto the declaring assembly: a machine-
/// readable manifest of which maps that assembly self-registers. The validation root reads these from
/// every referenced assembly to verify cross-assembly linkage at compile time (DWARF061). Hand-authoring
/// is not required.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class DwarfProvidesMapAttribute : Attribute
{
    /// <summary>Declares that this assembly registers a map from <paramref name="source"/> to
    /// <paramref name="destination"/>.</summary>
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
/// Emitted by the generator onto the declaring assembly: a manifest of the cross-assembly maps that
/// assembly consumes through <see cref="IDwarfMapper"/> (auto-detected from call sites and/or declared via
/// <see cref="UsesMapAttribute"/>). The validation root cross-checks these against the available
/// <see cref="DwarfProvidesMapAttribute"/> set.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class DwarfRequiresMapAttribute : Attribute
{
    /// <summary>Declares that this assembly consumes a map from <paramref name="source"/> to
    /// <paramref name="destination"/> through the ambient facade.</summary>
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
/// Explicitly declares that this assembly/type consumes the ambient map <typeparamref name="TSource"/> -&gt;
/// <typeparamref name="TDestination"/>. Use when the consumption can't be auto-detected from a direct
/// <see cref="IDwarfMapper"/> call site (e.g. the source is handled as a base type or <c>object</c>). The
/// generator folds these into the <see cref="DwarfRequiresMapAttribute"/> manifest.
/// </summary>
/// <typeparam name="TSource">The consumed map's source type.</typeparam>
/// <typeparam name="TDestination">The consumed map's destination type.</typeparam>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UsesMapAttribute<TSource, TDestination> : Attribute
{
}

/// <summary>Non-generic form of <see cref="UsesMapAttribute{TSource,TDestination}"/>.</summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UsesMapAttribute : Attribute
{
    /// <summary>Declares that this assembly/type consumes the ambient map from <paramref name="source"/>
    /// to <paramref name="destination"/>.</summary>
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
/// Marks the composition-root assembly (the one that references every map-providing and map-consuming
/// assembly). Only in this compilation does the generator perform the whole-graph cross-assembly linkage
/// validation (DWARF061) and emit <c>DwarfMap.Validate()</c>. Mid-tier assemblies see only a partial graph
/// and therefore only emit manifests.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DwarfMapperValidationRootAttribute : Attribute
{
    /// <summary>When true, the root emits a module initializer that calls <c>DwarfMap.Validate()</c> as the
    /// root module loads, turning a missing ambient map into a fail-fast startup exception. Default false.
    /// <para>Validation automatically follows the link flow: it checks exactly the ambient maps the graph
    /// actually consumes (auto-detected from call sites and <see cref="UsesMapAttribute{TSource,TDestination}"/>),
    /// so a pair used in both directions is validated both ways (round-trip) and a one-way use is validated
    /// one-way — no configuration.</para>
    /// <para>Caveat: module-initializer ordering across assemblies is not guaranteed, so this is reliable for
    /// eagerly-referenced providers but can false-positive on a genuinely lazy-loaded plugin whose
    /// registration hasn't run yet. For DI apps prefer <c>services.ValidateDwarfMaps()</c> (runs when the
    /// container is built); for lazy plugins call <c>DwarfMap.Validate()</c> after the plugin loads.</para></summary>
    public bool AutoValidate { get; set; }
}
