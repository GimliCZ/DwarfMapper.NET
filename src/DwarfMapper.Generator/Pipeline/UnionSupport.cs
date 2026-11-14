// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     C# 15 unions: the half both builds of the generator share, and the declarations each build implements once.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Round 35 T23. The generator ships twice, built against Roslyn 5.0 (the compiler floor README promises) and
    ///         against Roslyn 5.12, the first Roslyn whose API says what a union is (<c>ITypeSymbol.IsUnion</c>,
    ///         <c>UnionCaseTypes</c>). The SDK loads one of the two. Both compile this file; the partial methods below are
    ///         implemented in <c>UnionSupport.Floor.cs</c>, compiled into the Roslyn 5.0 build only, and in
    ///         <c>UnionSupport.Roslyn512.cs</c> in the Roslyn512 project, compiled into the 5.12 build only. The seam is
    ///         files, not <c>#if</c>, so that neither build carries a branch only the other can reach.
    ///     </para>
    /// </remarks>
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     True when union mapping is on for <paramref name="compilation" />: this is the Roslyn 5.12 build, and the
        ///     compilation is C# 15 or newer. The Roslyn 5.0 build answers false for every compilation.
        /// </summary>
        private static partial bool UnionMappingAvailable(Compilation compilation);

        /// <summary>True when <paramref name="type" /> is a C# 15 union type (see each build's implementation for how).</summary>
        internal static partial bool IsUnionType(ITypeSymbol type);

        /// <summary>
        ///     The DWARF113 message when the pair <paramref name="srcType" /> into <paramref name="tgtType" /> has a union
        ///     on either side that this build cannot map at this site, else <see langword="null" />. Nullability and
        ///     <c>Nullable&lt;T&gt;</c> are looked through on both sides, so <c>Pet?</c> into <c>Pet</c> is the same union,
        ///     never a refusal. <paramref name="what" /> names the member or method in the message;
        ///     <paramref name="inProjection" /> is true at an <c>IQueryable</c> projection, where no union is mapped.
        /// </summary>
        internal static partial string? UnionRefusal(ITypeSymbol srcType, ITypeSymbol tgtType, string what, Compilation compilation, bool inProjection);

        /// <summary>
        ///     True when <paramref name="conversion" /> is a C# 15 union conversion: case type to union, carried out by
        ///     calling the union's creation member.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Shared by both builds because both meet union conversions. A Roslyn 5.9 or newer compiler classifies
        ///         one as IMPLICIT even in a C# 14 compilation (measured on Roslyn 5.12, 2026-11-14), while C# 14 code
        ///         that uses one fails with CS9327 - so a check that trusts "implicit" would write CS9327 into a
        ///         consumer's <c>.g.cs</c>. <see cref="HasImplicitConversion" /> asks this before it counts a conversion.
        ///     </para>
        ///     <para>
        ///         Read from the Roslyn 5.0 API on purpose: a union conversion is the one implicit conversion that is
        ///         neither user-defined nor a method-group conversion and still carries a method - the union's
        ///         constructor, or its <c>IUnionMembers.Create</c> factory. Every other implicit conversion between two
        ///         types carries none.
        ///     </para>
        /// </remarks>
        internal static bool IsUnionConversion(Conversion conversion)
        {
            return conversion.IsImplicit && !conversion.IsUserDefined && !conversion.IsMethodGroup && conversion.MethodSymbol is not null;
        }

        /// <summary>
        ///     The DWARF113 message for a <c>[MapTo]</c> member whose pair has a union on either side, else
        ///     <see langword="null" />. The registry generator maps no union in either build: the same union on both sides
        ///     is copied, and a case type converts into its union where <see cref="HasImplicitConversion" /> lets a union
        ///     conversion count (the Roslyn 5.12 build, C# 15). Everything else is refused here.
        /// </summary>
        /// <remarks>
        ///     Round 35 T23, found while gating union conversions: round 31's DWARF113 guarded the class model only, and
        ///     the registry resolver classifies conversions itself. Measured on Roslyn 5.12 before this check:
        ///     <c>Pet</c> into <c>object?</c> assigned the union WRAPPER (the exact silent boxing DWARF113 exists for),
        ///     <c>Pet</c> into <c>PetDto</c> went through a synthesized struct map with no diagnostic, and <c>Cat</c> into
        ///     <c>Pet</c> in a C# 14 project wrote CS9327 into the generated file.
        /// </remarks>
        internal static string? RegistryUnionRefusal(ITypeSymbol srcType, ITypeSymbol tgtType, string what, Compilation compilation)
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

            if (tgtIsUnion && !srcIsUnion && HasImplicitConversion(compilation, srcType, tgtType))
            {
                return null;
            }

            return "'" + what + "' maps '" + src.ToDisplayString() + "' to '" + tgt.ToDisplayString() + "', and '" +
                   (srcIsUnion ? src : tgt).ToDisplayString() + "' is a C# 15 union type. [MapTo] does not map union " +
                   "types: an automatic mapping would treat the union as an ordinary struct and copy or box the wrapper " +
                   "instead of its case value. Map the pair in a [DwarfMapper] class, which maps one union into another " +
                   "under C# 15 on the .NET 11 SDK, or keep the same union type on both sides, which is copied as-is";
        }
    }
}
