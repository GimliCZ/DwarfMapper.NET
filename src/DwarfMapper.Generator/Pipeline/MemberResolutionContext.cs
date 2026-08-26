// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     The name lookups <c>ResolveMembers</c> builds once in its prologue and every resolution pass then
        ///     reads. Nothing writes to these after construction.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Split from <see cref="MemberAccumulators" /> BY DIRECTION, the same way
        ///         <see cref="MapperDeclarations" /> is split from <see cref="MapperAccumulators" />. The split is
        ///         not a matter of taste here: it was settled by measuring where each local is written.
        ///         <c>ReservedConverters</c> looks mutable and is the one worth naming — its only writes are in the
        ///         prologue, so it belongs on this side.
        ///     </para>
        ///     <para>
        ///         This is a BUNDLE OVER THE CALLER'S OWN INSTANCES, not a copy. <c>ResolveMembers</c> keeps using
        ///         its locals directly and hands the phases this view of them; the collections are the same objects,
        ///         which is what lets a phase be lifted out without disturbing the code left behind. Fields are all
        ///         read-only in practice, so sharing them is safe by inspection rather than by convention.
        ///     </para>
        /// </remarks>
        private sealed record MemberLookups(
            StringComparer Comparer,
            bool Flexible,
            Dictionary<string, ITypeSymbol> WritableByName,
            Dictionary<string, List<(string Name, ITypeSymbol Type)>> SourceGroups,
            List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves, bool NullableHop)> FlattenInfos,
            HashSet<string> ReservedConverters);

        /// <summary>
        ///     What the resolution passes FILL: the member list being built, and the three sets recording what has
        ///     already been claimed so a later pass does not claim it twice and the leftovers can be reported.
        /// </summary>
        /// <remarks>
        ///     Every field is a mutable collection shared with the caller by reference — see
        ///     <see cref="MemberLookups" /> for why that is the point rather than a leak. A pass mutates through
        ///     these and the caller sees it, exactly as it did when the passes were inline.
        /// </remarks>
        private sealed record MemberAccumulators(
            List<Model.MemberMap> Result,
            HashSet<string> HandledTargets,
            HashSet<string> ConsumedExtraParams,
            HashSet<string> ConsumedFlattenRoots);
    }
}
