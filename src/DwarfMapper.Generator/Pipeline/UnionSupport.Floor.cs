// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The Roslyn 5.0 build's half of <c>UnionSupport.cs</c>. Compiled into that build only (the Roslyn512 project
    ///     excludes every <c>*.Floor.cs</c>): it maps no union and refuses every pair that has one.
    /// </summary>
    internal static partial class MapperExtractor
    {
        private const string UnionAttributeFqn = "System.Runtime.CompilerServices.UnionAttribute";

        /// <summary>This build loads in compilers older than Roslyn 5.12, which cannot compile what a union map is made of.</summary>
        private static partial bool UnionMappingAvailable(Compilation compilation)
        {
            return false;
        }

        /// <summary>
        ///     By the attribute's full name: Roslyn 5.0 has no symbol API for unions, and the attribute is what makes a type
        ///     a union in C# 15 - whether the compiler lowered a <c>union</c> declaration or someone wrote the type by hand.
        /// </summary>
        internal static partial bool IsUnionType(ITypeSymbol type)
        {
            foreach (var a in type.GetAttributes())
                if (KnownNames.IsAttributeClass(a.AttributeClass, UnionAttributeFqn))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     Every pair of two different types with a union on either side, at every site (round 31 T24 measured what
        ///     happened before: <c>Pet</c> into <c>object</c> boxed the union wrapper, and <c>Pet</c> into <c>PetDto</c> was
        ///     refused as DWARF025, the wrong problem).
        /// </summary>
        internal static partial string? UnionRefusal(ITypeSymbol srcType, ITypeSymbol tgtType, string what, Compilation compilation, bool inProjection)
        {
            var src = Unwrapped(srcType);
            var tgt = Unwrapped(tgtType);
            if (SymbolEqualityComparer.Default.Equals(src, tgt))
            {
                return null;
            }

            var union = IsUnionType(src) ? src : IsUnionType(tgt) ? tgt : null;
            if (union is null)
            {
                return null;
            }

            return "'" + what + "' maps '" + src.ToDisplayString() + "' to '" + tgt.ToDisplayString() + "', and '" +
                   union.ToDisplayString() + "' is a C# 15 union type. Mapping a union needs a C# 15 compiler - the .NET 11 " +
                   "SDK's, Roslyn 5.12 or newer - which loads the DwarfMapper build that maps unions; this compiler loaded " +
                   "the build for older compilers, which maps none: an automatic mapping would treat the union as an " +
                   "ordinary struct and copy or box the wrapper instead of its case value. Convert it yourself with " +
                   "[MapProperty(Use = nameof(...))], or keep the same union type on both sides, which is copied as-is";
        }
    }
}
