// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     Type-level facts an emitter needs BEFORE it writes a null test into generated code.
///     <para>
///         One named predicate rather than an inline test per call site, because the inline form is how the
///         registry front door shipped broken output: <c>MapToGenerator</c>'s nested-helper path asked
///         <c>src.IsReferenceType</c> before null-propagating, and its extension-method path asked nothing at
///         all — it wrote <c>if (source is null) throw …</c> into every method it emitted. A <c>struct</c>
///         source is legal per <c>[MapTo]</c>'s own <c>AttributeUsage</c>, so that produced a generated file
///         the C# compiler rejects (CS0037) for a placement the attribute itself permits. A guard that lives
///         on one path and not on its sibling is the shape of that defect; a shared predicate is the shape of
///         the fix.
///     </para>
/// </summary>
internal static class TypeFacts
{
    /// <summary>
    ///     Whether a value of <paramref name="type" /> can be <c>null</c> at all — the question that has to be
    ///     answered before an <c>x is null</c> test is emitted, since the compiler rejects that pattern
    ///     against a non-nullable value type with CS0037.
    ///     <para>
    ///         NOT "is a reference type". <c>Nullable&lt;T&gt;</c> IS a value type and can still be null, so a
    ///         <c>T?</c> source needs the guard exactly as a class source does — the discrimination this
    ///         predicate exists to get right, and the one a naive <c>!IsValueType</c> would get wrong in the
    ///         opposite direction.
    ///     </para>
    ///     <para>
    ///         Keyed on <see cref="ITypeSymbol.IsValueType" /> with that single exception rather than on
    ///         <c>IsReferenceType</c>, so an unconstrained type parameter — which is neither, as far as the
    ///         symbol is concerned, and is null when substituted with a reference type — answers true and
    ///         keeps its guard, while <c>T where T : struct</c> answers false. No source position the registry
    ///         reads can currently BE a type parameter (<c>[MapTo]</c> sits on a class or struct declaration),
    ///         but a predicate that is correct only for the inputs which happen to reach it today is the next
    ///         version of the defect above.
    ///     </para>
    ///     <para>
    ///         Says nothing about nullable ANNOTATIONS on purpose. A non-nullable-annotated reference source
    ///         still answers true here: the annotation is a promise the caller makes to the compiler, not one
    ///         the generated code — which is public API reachable from unannotated assemblies — may rely on.
    ///         Annotation-sensitive questions have their own predicates on the mapper path.
    ///     </para>
    /// </summary>
    internal static bool CanBeNull(ITypeSymbol type)
    {
        return !type.IsValueType || IsNullableValueType(type);
    }

    // Constructed Nullable<T> only; the unbound System.Nullable<T> definition is matched through
    // OriginalDefinition so `int?` and `SrcValue?` both answer the same way.
    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol named
               && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }
}
