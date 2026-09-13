// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     The fixed context every <c>[FlattenGraph]</c> directive is resolved against: the two types and the
        ///     surrounding configuration. Identical for every directive in a method, which is why it is built once
        ///     outside the loop.
        /// </summary>
        /// <remarks>
        ///     Two of <c>ResolveFlattenGraphDirectives</c>' parameters are deliberately absent. <c>rawDirectives</c>
        ///     is the loop's own sequence, and <c>nullAsNull</c> is read nowhere in the loop body -- measured, not
        ///     assumed. Carrying either would suggest a per-directive dependency that does not exist.
        /// </remarks>
        private sealed record FlattenGraphRequest(
            ITypeSymbol SourceType,
            INamedTypeSymbol TargetType,
            Compilation Compilation,
            LocationInfo? Location,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AllMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AutoCandidates,
            EnumPolicy EnumPolicy,
            NullStrategy NullStrategy,
            bool AutoNest,
            NestedMappingRegistry NestedRegistry,
            bool IsPreserve,
            bool AllowNonPublic,
            IReadOnlyList<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, bool WrittenGeneric)>? RawDerivedPairs);


        /// <summary>
        ///     What the source navigation member RESOLVED TO -- the answer the first third of
        ///     <c>ResolveOneFlattenGraphDirective</c> exists to compute, snapshotted at the moment it is complete.
        /// </summary>
        /// <remarks>
        ///     A snapshot is only sound because all seven are settled before the branch that consumes them: each is
        ///     assigned in the resolution section above and NONE is assigned afterwards. That was checked, not
        ///     assumed -- a record built from locals that are still being written would go stale silently, and
        ///     nothing downstream would notice.
        /// </remarks>
        private sealed record FlattenNavShape(
            ITypeSymbol SrcNavType,
            ITypeSymbol NodeType,
            ITypeSymbol NodeDtoType,
            bool SrcNavIsCollection,
            bool SrcNavIsArray,
            bool SrcNavIsDict,
            bool NeedsToArray);

        /// <summary>
        ///     Everything resolving a directive writes into. Four of the six outlive the call and are what the
        ///     method ultimately returns or hands back to its caller; <see cref="SeenTargets" /> exists only to let
        ///     one directive see what an earlier one already claimed.
        /// </summary>
        /// <remarks>
        ///     <see cref="Synthesized" /> and <see cref="ConsumedTargets" /> were already documented as mutated on
        ///     <c>ResolveFlattenGraphDirectives</c> itself -- the summary there names them explicitly. This record
        ///     puts that in the type system rather than in prose a reader has to find.
        /// </remarks>
        private sealed record FlattenGraphAccumulators(
            List<FlattenGraphDirective> Directives,
            List<MemberMap> Injected,
            List<DiagnosticInfo> Diagnostics,
            Dictionary<string, SynthesizedMethod> Synthesized,
            HashSet<string> ConsumedTargets,
            HashSet<string> SeenTargets);
    }
}
