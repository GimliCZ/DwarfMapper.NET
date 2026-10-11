// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Registry
{
    /// <summary>
    ///     Where, if anywhere, assigning a value of one type to a member of another disagrees about nullable reference
    ///     annotations in a way C# warns about. Ordered: every value above <see cref="WarnsOnly" /> is a place a null can
    ///     land that the destination forbids.
    /// </summary>
    internal enum NullLanding
    {
        /// <summary>
        ///     The annotations agree, or differ only where C# accepts it: <c>string</c> into <c>string?</c>, or a
        ///     covariant widening such as <c>string[]</c> into <c>string?[]</c>.
        /// </summary>
        None = 0,

        /// <summary>
        ///     No null can land, but an invariant type argument differs - <c>List&lt;string&gt;</c> into
        ///     <c>List&lt;string?&gt;</c> - so C# still warns (CS8619).
        /// </summary>
        WarnsOnly = 1,

        /// <summary>The value itself is a nullable reference and the destination is not (CS8601).</summary>
        Value = 2,

        /// <summary>An element - an array's, or a collection type argument's - is nullable where the destination's is not.</summary>
        Element = 3,

        /// <summary>A dictionary's value type is nullable where the destination's is not.</summary>
        DictionaryValue = 4
    }

    /// <summary>
    ///     Finds the <see cref="NullLanding" /> of a direct assignment - what the registry emits for an identity or
    ///     implicit conversion. Walked in parallel over array element types and generic type arguments, the positions
    ///     such a conversion carries annotations through; a generic destination is matched against the source viewed
    ///     as that destination's definition (itself, a base type or an interface), so <c>List&lt;string?&gt;</c> into
    ///     <c>IReadOnlyList&lt;string&gt;</c> compares <c>string?</c> with <c>string</c>.
    /// </summary>
    /// <remarks>
    ///     Variance decides only whether C# WARNS about a difference, never whether a null lands: a nullable element
    ///     into a non-nullable one lands through a covariant <c>IReadOnlyList&lt;out T&gt;</c> exactly as through an
    ///     invariant <c>List&lt;T&gt;</c>. The one position that is fine the other way round is a contravariant
    ///     (<c>in</c>) argument, which consumes values instead of holding them.
    /// </remarks>
    internal static class NullabilityLanding
    {
        public static NullLanding Find(ITypeSymbol source, ITypeSymbol destination)
        {
            return IsNullableRef(source) && IsNonNullableRef(destination)
                ? NullLanding.Value
                : Inside(source, destination);
        }

        private static NullLanding Inside(ITypeSymbol source, ITypeSymbol destination)
        {
            if (source is IArrayTypeSymbol sourceArray && destination is IArrayTypeSymbol destinationArray)
            {
                return Position(sourceArray.ElementType, destinationArray.ElementType, VarianceKind.Out, NullLanding.Element);
            }

            if (destination is not INamedTypeSymbol { IsGenericType: true } named || ViewAs(source, named.OriginalDefinition) is not { } view)
            {
                return NullLanding.None;
            }

            var parameters = named.OriginalDefinition.TypeParameters;
            var result = NullLanding.None;
            for (var i = 0; i < parameters.Length; i++)
            {
                var landing = Position(view.TypeArguments[i],
                    named.TypeArguments[i],
                    parameters[i].Variance,
                    i == 1 && IsDictionary(named) ? NullLanding.DictionaryValue : NullLanding.Element);
                // The first position a null lands at names the diagnostic; a mere warning never outranks one.
                if (landing > NullLanding.WarnsOnly)
                {
                    return landing;
                }

                if (landing == NullLanding.WarnsOnly)
                {
                    result = NullLanding.WarnsOnly;
                }
            }

            return result;
        }

        /// <summary>
        ///     One type-argument position under its variance. A null landing anywhere inside it is reported as this
        ///     position (<paramref name="landingHere" />), because this position's declared type is what the consumer
        ///     edits to fix it.
        /// </summary>
        private static NullLanding Position(ITypeSymbol source, ITypeSymbol destination, VarianceKind variance, NullLanding landingHere)
        {
            if (IsNullableRef(source) && IsNonNullableRef(destination))
            {
                return variance == VarianceKind.In ? NullLanding.None : landingHere;
            }

            if (IsNonNullableRef(source) && IsNullableRef(destination) && variance != VarianceKind.Out)
            {
                return NullLanding.WarnsOnly;
            }

            var inner = Inside(source, destination);
            return inner > NullLanding.WarnsOnly ? landingHere : inner;
        }

        private static INamedTypeSymbol? ViewAs(ITypeSymbol source, INamedTypeSymbol definition)
        {
            for (var t = source as INamedTypeSymbol; t is not null; t = t.BaseType)
                if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, definition))
                {
                    return t;
                }

            foreach (var i in source.AllInterfaces)
                if (SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, definition))
                {
                    return i;
                }

            return null;
        }

        private static bool IsDictionary(INamedTypeSymbol type)
        {
            return IsDictionaryInterface(type) || type.AllInterfaces.Any(IsDictionaryInterface);
        }

        private static bool IsDictionaryInterface(INamedTypeSymbol type)
        {
            return type.TypeArguments.Length == 2 &&
                   type.Name is "IDictionary" or "IReadOnlyDictionary" &&
                   KnownNames.IsNamespace(type.ContainingNamespace, "System.Collections.Generic");
        }

        private static bool IsNullableRef(ITypeSymbol type)
        {
            return type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
        }

        private static bool IsNonNullableRef(ITypeSymbol type)
        {
            return type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;
        }
    }
}
