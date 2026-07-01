// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// A compile-time-only configuration surface for the <c>(TSource → TTarget)</c> map. Declare a method on a
/// <c>[DwarfMapper]</c> class taking a single <see cref="MapConfig{TSource,TTarget}"/> parameter and configure
/// members with type-safe selector lambdas; the DwarfMapper generator reads the method's body <b>syntactically
/// and never executes it</b>, so this stays reflection-free and AOT-safe. Method arguments must be member-access
/// selector lambdas (<c>t =&gt; t.A.B</c>) or method groups (<c>Convert</c>); inline converter/factory lambda
/// bodies are not supported in this version.
/// </summary>
/// <typeparam name="TSource">Source type of the configured pair.</typeparam>
/// <typeparam name="TTarget">Destination type of the configured pair.</typeparam>
public sealed class MapConfig<TSource, TTarget>
{
    private MapConfig() { }

    /// <summary>Maps <paramref name="source"/> (a member or dotted flatten path) to <paramref name="target"/>.</summary>
    public MapConfig<TSource, TTarget> Map<TMember>(Func<TTarget, TMember> target, Func<TSource, TMember> source) => this;

    /// <summary>Maps with a converter method group: source member type → target member type.</summary>
    public MapConfig<TSource, TTarget> Map<TSrcMember, TTgtMember>(
        Func<TTarget, TTgtMember> target, Func<TSource, TSrcMember> source, Func<TSrcMember, TTgtMember> convert) => this;

    /// <summary>Assigns only when <paramref name="when"/> (a predicate method group over the source) is true.</summary>
    public MapConfig<TSource, TTarget> MapWhen<TMember>(
        Func<TTarget, TMember> target, Func<TSource, TMember> source, Func<TSource, bool> when) => this;

    /// <summary>Null-substitute for a value-type member: emitted as <c>source ?? fallback</c>.</summary>
    /// <remarks>
    /// The <c>target</c>/<c>source</c> selectors are typed <c>TMember?</c> (i.e. <see cref="Nullable{TMember}"/>)
    /// rather than plain <c>TMember</c>. This is required, not cosmetic: nullable reference annotations are
    /// erased from the CLR signature, so a class-constrained <c>MapOr(Func&lt;.., TMember?&gt;, ...)</c> overload
    /// and a struct-constrained one written as <c>Func&lt;.., TMember&gt;</c> would collide (CS0111) — only
    /// <see cref="Nullable{TMember}"/> is a genuinely distinct CLR type from the erased reference-annotation form.
    /// </remarks>
    public MapConfig<TSource, TTarget> MapOr<TMember>(
        Func<TTarget, TMember?> target, Func<TSource, TMember?> source, TMember fallback) where TMember : struct => this;

    /// <summary>Null-substitute for a reference-type member: emitted as <c>source ?? fallback</c>.</summary>
    public MapConfig<TSource, TTarget> MapOr<TMember>(
        Func<TTarget, TMember?> target, Func<TSource, TMember?> source, TMember fallback) where TMember : class => this;

    /// <summary>Suppresses completeness (DWARF001) for a destination member.</summary>
    public MapConfig<TSource, TTarget> Ignore<TMember>(Func<TTarget, TMember> target) => this;

    /// <summary>Suppresses source-coverage suggestion (DWARF039) for a source member.</summary>
    public MapConfig<TSource, TTarget> IgnoreSource<TMember>(Func<TSource, TMember> source) => this;

    /// <summary>Assigns a constant to a source-less destination member.</summary>
    public MapConfig<TSource, TTarget> Value<TMember>(Func<TTarget, TMember> target, TMember value) => this;

    /// <summary>Assigns a destination member from a parameterless method group.</summary>
    public MapConfig<TSource, TTarget> Value<TMember>(Func<TTarget, TMember> target, Func<TMember> compute) => this;

    /// <summary>Constructs the destination via a factory method group: <c>TSource → TTarget</c>.</summary>
    public MapConfig<TSource, TTarget> Construct(Func<TSource, TTarget> factory) => this;
}
