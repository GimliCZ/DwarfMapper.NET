// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
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
        ///     <see cref="HasImplicitConversion" />, where a C# 15 union conversion also counts when this build maps unions
        ///     in this compilation (the Roslyn 5.12 build, C# 15 or newer). For the places a value goes into a union slot:
        ///     a member assigned into a union, an explicit converter's parameter, an arm's target.
        /// </summary>
        /// <remarks>
        ///     One implementation per build, so that neither carries a branch only the other can reach: the Roslyn 5.0
        ///     build maps no union, so there it IS <see cref="HasImplicitConversion" /> - which is what keeps a union
        ///     conversion out of a C# 14 consumer's generated code when a Roslyn 5.9 compiler, one that classifies it as
        ///     implicit, loads that build.
        /// </remarks>
        private static partial bool HasImplicitOrUnionConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol target);

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
        ///     Claims a declared create map between two different unions and records its case dispatch (or its refusal);
        ///     <see langword="false" /> for every other method. The Roslyn 5.0 build claims nothing: its
        ///     <see cref="UnionRefusal" /> has already refused every pair with a union.
        /// </summary>
        private static partial bool TryHandleUnionEndpoint(
            DeclaredEndpoint endpoint,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            ref bool classIgnoreLivenessBlinded);

        /// <summary>
        ///     Builds the helper for a nested pair of two different unions - a member, a collection element, a
        ///     <c>Nullable&lt;T&gt;</c> value - as a case dispatch, adds it to <paramref name="pendingNestedModels" />, and
        ///     returns <see langword="true" />; <see langword="false" /> for every other pair, which the drain maps as an
        ///     object. The Roslyn 5.0 build builds none: no union pair reaches its queue.
        /// </summary>
        private static partial bool TryBuildUnionHelper(
            (ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, bool AutoNest, LocationInfo? Origin) pending,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            List<(MapMethodModel Model, string MethodName, LocationInfo? Origin)> pendingNestedModels);

        /// <summary>
        ///     A declared create map as an endpoint handler reads it: the method, its source and destination types, where
        ///     it reports, and where its diagnostics begin in the class's list (for
        ///     <c>TryScopeCompletenessRefusalToItsMethod</c>). One bundle, so that the handler stays within the six
        ///     parameters a pipeline method may take (<c>ResolverParameterDisciplineTests</c>).
        /// </summary>
        private sealed record DeclaredEndpoint(
            IMethodSymbol Method,
            ITypeSymbol Source,
            ITypeSymbol Target,
            LocationInfo? Location,
            int DiagnosticsStart);

        /// <summary>
        ///     True when <paramref name="conversion" /> is a C# 15 union conversion: case type to union, carried out by
        ///     calling the union's creation member.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Shared by both builds because both meet union conversions. A Roslyn 5.9 or newer compiler classifies
        ///         one as IMPLICIT even in a C# 14 compilation (measured on Roslyn 5.12, 2026-11-14), while C# 14 code
        ///         that uses one fails with CS9327 - so a check that trusts "implicit" would write CS9327 into a
        ///         consumer's <c>.g.cs</c>. <see cref="HasImplicitConversion" /> never counts one, and <see cref="HasImplicitOrUnionConversion" /> only where union mapping is on.
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
        ///     Gives every member lifted through <c>Nullable&lt;T&gt;</c> into a union - its converter a union dispatch of
        ///     this class, under any of the names the passes before this one call it by - that union's type in
        ///     <see cref="MemberMap.LiftTargetFqn" />, so the emitter writes the lift's null arm as <c>default(T?)</c>.
        /// </summary>
        /// <remarks>
        ///     A pass over the finished models rather than a field set at resolution: the lift is decided inside
        ///     <c>TryResolveConversion</c>, which hands back a converter NAME, and the converter is a method of this very
        ///     class whose model knows its return type. A no-op in the Roslyn 5.0 build, which builds no union dispatch, and
        ///     for every member whose converter is not one.
        /// </remarks>
        private static void TypeTheUnionLifts(List<MapMethodModel> methods)
        {
            var unionReturns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in methods)
            {
                if (m.UnionEmpty == UnionEmptyArm.NotUnion)
                {
                    continue;
                }

                // The method itself, its depth companion, and its Preserve dispatch wrapper: the names a member's
                // converter can carry by now.
                unionReturns[m.MethodName] = m.ReturnTypeFullName;
                unionReturns[GeneratedNames.Depth + m.MethodName] = m.ReturnTypeFullName;
                unionReturns[NestedMappingRegistry.BuildDispatchWrapperName(m.ParameterTypeFullName, m.ReturnTypeFullName)] = m.ReturnTypeFullName;
            }

            if (unionReturns.Count == 0)
            {
                return;
            }

            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                var members = Typed(m.Members, unionReturns, out var membersChanged);
                var arguments = Typed(m.ConstructorArguments, unionReturns, out var argumentsChanged);
                if (membersChanged || argumentsChanged)
                {
                    methods[i] = m with
                    {
                        Members = members,
                        ConstructorArguments = arguments
                    };
                }
            }
        }

        private static EquatableArray<MemberMap> Typed(EquatableArray<MemberMap> maps, Dictionary<string, string> unionReturns, out bool changed)
        {
            changed = false;
            if (maps.Count == 0)
            {
                return maps;
            }

            var array = maps.ToArray();
            for (var i = 0; i < array.Length; i++)
                if (array[i].NullHandling == NullHandling.NullableProject && array[i].ConverterMethod is { } converter &&
                    unionReturns.TryGetValue(converter, out var union))
                {
                    array[i] = array[i] with
                    {
                        LiftTargetFqn = union
                    };
                    changed = true;
                }

            return changed ? EquatableArray.From(array) : maps;
        }

        /// <summary>
        ///     The DWARF113 message for a <c>[MapTo]</c> member whose pair has a union on either side, else
        ///     <see langword="null" />. The registry generator maps no union in either build: the same union on both sides
        ///     is copied, and a case type converts into its union where <see cref="HasImplicitOrUnionConversion" /> lets a union
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

            if (tgtIsUnion && !srcIsUnion && HasImplicitOrUnionConversion(compilation, srcType, tgtType))
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
