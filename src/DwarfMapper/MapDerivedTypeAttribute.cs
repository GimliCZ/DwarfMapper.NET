// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics.CodeAnalysis;

namespace DwarfMapper;

/// <summary>
///     Registers a concrete derived source type <typeparamref name="TSource" /> and its DTO type
///     <typeparamref name="TTarget" /> as a dispatch arm for polymorphic mapping.
///     Apply multiple times on a <c>partial</c> mapping method whose source parameter is a
///     base class or interface. Arms are emitted most-derived-first so that a more-derived type
///     is never shadowed by a base-type arm. Unregistered runtime types throw
///     <see cref="global::System.ArgumentException" /> (loud, never silent).
/// </summary>
// A dispatch arm needs a base/derived HIERARCHY to dispatch over; the flat DTO pair has none, so both forms
// were measured against a compilation where nothing could be more derived than anything else.
//
// The generic form's own limitation is worth stating here rather than only in the fixture: SurfaceCatalog
// renders arity 2 as <Src, Dst> for every element, and the endpoint templates fix the signature to
// `Dst Map(Src s)`, so this element's cells register the BASE as an arm of itself. That is a legal and
// degenerate arm, and it measures exactly what this element's cells claim — the directive is read at the
// create map and at no other endpoint — without measuring polymorphic dispatch. The open form below, which
// can name its types, does measure it.
[DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "polymorphic-hierarchy")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapDerivedTypeAttribute<TSource, TTarget> : Attribute
    where TSource : class
    where TTarget : class
{
}

/// <summary>Non-generic form of <see cref="MapDerivedTypeAttribute{TSource,TTarget}" />.</summary>
[DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "polymorphic-hierarchy")]
// The sampled arity-2 argument list is `typeof(Dst), typeof(Dst)` — the same type twice, and neither of them
// assignable to the method's source parameter. Against the flat pair that is DWARF035 (Error), so the cell
// read NotCompilable (CS8795) and the finding that claimed this form "acts at CreateMap" was measuring a
// refusal of nonsense. Naming the fixture's derived types asks the question the directive exists for.
[DwarfSurfaceProbe(constructorArity: 2, Arguments = "typeof(SrcDerived), typeof(DstDerived)")]
[ExcludeFromCodeCoverage(Justification = "compile-time-only attribute, consumed by the generator (round-22 P4's one "
    + "sanctioned category): read from the semantic model at build time; no runtime code path constructs or "
    + "executes it. Issues/round22/RESEARCH-97-PERCENT-GATES.md §2.2.")]
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
