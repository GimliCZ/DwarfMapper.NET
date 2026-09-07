// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    // Nested inside MapperExtractor, and PRIVATE: PairProp/PairIgnore/PairValue are its private nested
    // types, so a sibling record would have needed them widened to internal — accessibility loosened purely
    // to move code between files, which is the trade this repository consistently refuses.
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     What <c>ExtractCore</c>'s per-method loop reads about the mapper CLASS — computed once, before any
        ///     method is looked at, and never written afterwards.
        /// </summary>
        /// <remarks>
        ///     Three bundles rather than one, because the per-method loop closes over 35 locals and a single
        ///     "context" holding all of them would be a god-object that moved the mess instead of resolving it. The
        ///     split is by LIFETIME AND DIRECTION, which is a real distinction a reader can use: these are read-only
        ///     facts about the class, <see cref="MapperPolicy" /> is the configuration, and
        ///     <see cref="MapperAccumulators" /> is what the loop fills.
        /// </remarks>
        private sealed record MapperDeclarations(
            INamedTypeSymbol ClassSymbol,
            List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AllMethods,
            List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> MapperMethods,
            List<string> ClassIgnores,
            List<string> ClassIgnoreSources,
            HashSet<string> MapperReservedConverters,
            List<(string Name, ITypeSymbol ReturnType)> ValueProviders,
            List<PairProp> PairProps,
            List<PairIgnore> PairIgnores,
            List<PairValue> PairValues,
            List<PairConstructor> PairConstructors,
            List<(ITypeSymbol Source, ITypeSymbol Target, bool Enabled)> PairNullSkips,
            List<(string Name, ITypeSymbol ParamType)> BeforeHookDefs,
            List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)> AfterHookDefs,
            // The [GenerateMap<S,T>] pairs the class DECLARES. Settled before this record is built
            // (ExpandWrapperMaps is the last thing that adds to the list, and it runs earlier), which is what
            // lets it sit here among the read-only facts. Round 29 T0.2c review fix 1 needed it: two of the
            // pair-scoped directives are honoured only for a declared pair, so "is this pair customized?"
            // cannot be answered without knowing which pairs are declared.
            List<(ITypeSymbol Src, INamedTypeSymbol Tgt)> GenPairs);

        /// <summary>
        ///     The mapper's configuration, as read from its attributes. Every value is a class-level default; the
        ///     loop derives per-method values from these.
        /// </summary>
        /// <remarks>
        ///     Distinct from <see cref="MapperOptions" />, which is the eleven-flag bundle the member resolvers
        ///     take. This one carries the RAW class-level reads — including the four the resolvers never see
        ///     (<c>ReferenceHandling</c>, <c>RequiredMapping</c>, <c>MaxDepth</c>, <c>EnumPolicy</c>) — and the loop
        ///     builds a <see cref="MapperOptions" /> from it per method, which is where a method-scoped override
        ///     gets to win.
        /// </remarks>
        private sealed record MapperPolicy(
            bool AllowNonPublic,
            bool CaseInsensitive,
            bool ClassAutoNest,
            bool ExplicitOnly,
            bool IgnoreObsolete,
            bool ImplicitConversions,
            bool IsPreserveMode,
            bool IsSetNullMode,
            bool SkipNullSrc,
            int MaxDepth,
            int NameConvention,
            int ReferenceHandling,
            int RequiredMapping,
            NullCollectionsBehavior NullCollections,
            NullStrategy NullStrategy,
            EnumPolicy EnumPolicy);

        /// <summary>
        ///     What the per-method loop FILLS. Every member is a mutable collection shared across iterations and
        ///     read by the post-processing phases that follow the loop.
        /// </summary>
        /// <remarks>
        ///     Passing these as a bundle rather than as eight parameters is not only ergonomics: it names the fact
        ///     that they are OUTPUTS. The earlier extractions in this round were bitten by the opposite — locals
        ///     that looked private to one phase and turned out to be inputs to three others, which only the
        ///     compiler noticed. Naming the accumulator set makes that dependency legible before it bites.
        /// </remarks>
        private sealed record MapperAccumulators(
            List<MapMethodModel> Methods,
            List<DiagnosticInfo> Diagnostics,
            Dictionary<string, SynthesizedMethod> Synthesized,
            NestedMappingRegistry NestedRegistry,
            Dictionary<int, LocationInfo?> PublicMethodLocs,
            HashSet<string> LiveClassIgnores,
            List<(ITypeSymbol Src, ITypeSymbol Tgt, LocationInfo? Loc, List<string> IgnoreSources)> ElementPairsOwedCoverage,
            Dictionary<ITypeSymbol, HashSet<string>> IgnorableNamesMemo,
            // The [GenerateView<S,T>] views to emit inside this mapper class, root and nested, flat and
            // deduplicated by view type name. Filled by ExtractViews AFTER the per-method loop and after
            // ExtractGenerateMapPairs, because a view resolves its members through the same member resolution
            // those endpoints do and must see the declared pairs they registered.
            List<ViewModel> Views);
    }
}
