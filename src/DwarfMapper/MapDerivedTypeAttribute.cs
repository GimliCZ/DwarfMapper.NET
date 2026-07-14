// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Registers a concrete derived source type <typeparamref name="TSource" /> and its DTO type
///     <typeparamref name="TTarget" /> as a dispatch arm for polymorphic mapping.
///     Apply multiple times on a <c>partial</c> mapping method whose source parameter is a
///     base class or interface. Arms are emitted most-derived-first so that a more-derived type
///     is never shadowed by a base-type arm. Unregistered runtime types throw
///     <see cref="global::System.ArgumentException" /> (loud, never silent).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapDerivedTypeAttribute<TSource, TTarget> : Attribute
    where TSource : class
    where TTarget : class
{
}

/// <summary>Non-generic form of <see cref="MapDerivedTypeAttribute{TSource,TTarget}" />.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapDerivedTypeAttribute : Attribute
{
    /// <summary>Registers <paramref name="sourceType" /> as a dispatch arm mapped to <paramref name="targetType" />.</summary>
    /// <param name="sourceType">Concrete derived source type; must be assignable to the method's source parameter type.</param>
    /// <param name="targetType">Concrete derived target DTO type; must be assignable to the method's return type.</param>
    public MapDerivedTypeAttribute(Type sourceType, Type targetType)
    {
        SourceType = sourceType;
        TargetType = targetType;
    }

    /// <summary>The concrete derived source type registered for this dispatch arm.</summary>
    public Type SourceType { get; }

    /// <summary>The concrete derived target DTO type registered for this dispatch arm.</summary>
    public Type TargetType { get; }
}
