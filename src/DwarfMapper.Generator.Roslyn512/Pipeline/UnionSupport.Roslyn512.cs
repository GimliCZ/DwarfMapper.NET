// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The Roslyn 5.12 build's half of <c>UnionSupport.cs</c>. Compiled into that build only.
    /// </summary>
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     C# 15 or newer. <see cref="IsUnionType" /> alone cannot tell: Roslyn 5.12 reports <c>IsUnion</c> in a C# 14
        ///     compilation too (a C# 14 project can reference a C# 15 library's union), while C# 14 code that converts to a
        ///     union or matches on one fails with CS9327 - measured on Roslyn 5.12, 2026-11-14.
        /// </summary>
        private static bool UnionMappingAvailable(Compilation compilation)
        {
            return ((CSharpCompilation)compilation).LanguageVersion >= LanguageVersion.CSharp15;
        }

        /// <summary>A union conversion counts where union mapping is on: C# 15 or newer.</summary>
        private static partial bool HasImplicitOrUnionConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
        {
            var conversion = ((CSharpCompilation)compilation).ClassifyConversion(source, target);
            return conversion.IsImplicit && !conversion.IsUserDefined &&
                   (!IsUnionConversion(conversion) || UnionMappingAvailable(compilation));
        }

        /// <summary>
        ///     The compiler's own answer. Right for a <c>union</c> declaration, a hand-written <c>[Union]</c> type and a
        ///     member-provider union, from source and from metadata (measured on Roslyn 5.12).
        /// </summary>
        internal static partial bool IsUnionType(ITypeSymbol type)
        {
            return type.IsUnion;
        }

        /// <summary>
        ///     What this build still refuses. Two different unions are mapped case by case; a case type converts into its
        ///     union by the language's own union conversion. Refused: a compilation older than C# 15, any union in a
        ///     projection, a union with no case types, and a union into or out of a type that is not a union and is not
        ///     one of its cases.
        /// </summary>
        internal static partial string? UnionRefusal(ITypeSymbol srcType, ITypeSymbol tgtType, string what, Compilation compilation, bool inProjection)
        {
            var src = Unwrapped(srcType);
            var tgt = Unwrapped(tgtType);
            if (SymbolEqualityComparer.Default.Equals(src, tgt))
            {
                return null;
            }

            var srcIsUnion = IsUnionType(src);
            var tgtIsUnion = IsUnionType(tgt);
            if (!srcIsUnion && !tgtIsUnion)
            {
                return null;
            }

            var union = srcIsUnion ? src : tgt;
            var pair = "'" + what + "' maps '" + src.ToDisplayString() + "' to '" + tgt.ToDisplayString() + "', and '" +
                       union.ToDisplayString() + "' is a C# 15 union type";
            if (!UnionMappingAvailable(compilation))
            {
                return pair + ". Mapping a union needs C# 15, and this project compiles C# " +
                       ((CSharpCompilation)compilation).LanguageVersion.ToDisplayString() + ": a union map is made of union " +
                       "conversions and pattern matches over the union's contents, which are C# 15 features (CS9327). Set " +
                       "<LangVersion> to 15 or newer (net11.0 defaults to it), or convert it yourself with " +
                       "[MapProperty(Use = nameof(...))]";
            }

            if (inProjection)
            {
                return pair + ". A union is not projected: a query provider would have to translate a match over the " +
                       "union's contents, which no LINQ provider does. Project the members the union is made from, or map " +
                       "the query's results with a create map";
            }

            var caseless = srcIsUnion && src.UnionCaseTypes.IsEmpty ? src : tgtIsUnion && tgt.UnionCaseTypes.IsEmpty ? tgt : null;
            if (caseless is not null)
            {
                return "'" + what + "' maps '" + src.ToDisplayString() + "' to '" + tgt.ToDisplayString() + "', and '" +
                       caseless.ToDisplayString() + "' is a union type with no case types (no single-parameter constructor " +
                       "and no IUnionMembers.Create): no value of it can be mapped";
            }

            if (tgtIsUnion && !srcIsUnion && HasImplicitOrUnionConversion(compilation, srcType, tgtType))
            {
                // A case type into its union: the language's own union conversion, which the caller assigns directly.
                return null;
            }

            if (srcIsUnion && tgtIsUnion)
            {
                // Two different unions: a case dispatch (MapperExtractor.Unions.cs).
                return null;
            }

            return srcIsUnion
                ? pair + ". A union maps only into another union: into a type that is not one, an automatic mapping could " +
                  "only box the union or pick one of its cases. Convert it yourself with [MapProperty(Use = nameof(...))]"
                : pair + ", and '" + src.ToDisplayString() + "' is not one of its case types and does not convert to " +
                  "one. Map it to a case type first, or convert it yourself with [MapProperty(Use = nameof(...))]";
        }
    }
}
