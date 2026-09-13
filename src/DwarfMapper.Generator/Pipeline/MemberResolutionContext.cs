// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     What member resolution was ASKED to do: the two types, the directives read off the attributes, and
        ///     the surrounding facts a pass needs to judge a member. No pass reassigns any of it, and only
        ///     <see cref="NestedRegistry" /> is mutated at all -- see the remarks, which say how and why.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Three bundles, matching <see cref="MapperDeclarations" /> / <see cref="MapperAccumulators" />
        ///         rather than inventing a shape: this one is the request, <see cref="MemberLookups" /> is what the
        ///         prologue derives from it, and <see cref="MemberAccumulators" /> is what the passes fill.
        ///     </para>
        ///     <para>
        ///         The split was measured, not chosen. Every parameter of <c>ResolveMembers</c> was checked for
        ///         write-sites inside the body: <c>diagnostics</c> and <c>synthesized</c> are written by the passes
        ///         and are therefore accumulators, not request fields, however much they read like inputs.
        ///         <c>ignores</c> IS written — but only in the prologue, folding in the obsolete members — so by the
        ///         time this record is built it is settled, and it belongs here.
        ///     </para>
        ///     <para>
        ///         <b><see cref="NestedRegistry" /> is the exception, and is named rather than glossed.</b> A
        ///         write-site grep over this method's own text reports it read-only, which is true and misleading:
        ///         the passes hand it to helpers that register nested maps into it, so it IS mutated, just not
        ///         here. It stays in the request because the passes' relationship to it is ask-and-register — a
        ///         collaborator they consult and extend — rather than an output channel they fill for the caller to
        ///         drain, which is what the accumulators are. The general lesson is worth more than the placement:
        ///         a textual write-site scan cannot see mutation through a callee, so for anything with mutating
        ///         members the argument positions have to be checked too.
        ///     </para>
        ///     <para>
        ///         Three of <c>ResolveMembers</c>' parameters are deliberately absent: <c>flattenRoots</c>,
        ///         <c>mapPropertyExtras</c> and <c>mapperReservedConverters</c> are consumed by the prologue to
        ///         build <see cref="MemberLookups" /> and no pass reads them again. A bundle that carries fields
        ///         nobody reads teaches a reader the wrong thing about what resolution depends on.
        ///     </para>
        ///     <para>
        ///         This is NOT <c>ResolveMembers</c>' own signature refactored -- that method still takes its
        ///         parameters one by one, and every caller is untouched. This record is built from them in the
        ///         prologue, purely so a pass can be lifted out without acquiring twenty parameters. Collapsing the
        ///         signature itself is a separate change with a much wider blast radius; see
        ///         <c>Issues/round27/SEAM-STAGE.md</c>.
        ///     </para>
        /// </remarks>
        private sealed record MemberRequest(
            ITypeSymbol SourceType,
            INamedTypeSymbol TargetType,
            HashSet<string> Ignores,
            Compilation Compilation,
            LocationInfo? Location,
            MapperOptions Options,
            IReadOnlyList<(string Source, string Target, string? Use)> ExplicitMaps,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AllMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AutoCandidates,
            EnumPolicy EnumPolicy,
            NullStrategy NullStrategy,
            List<string> ReinterpretMembers,
            // Destination members carrying an explicit [MapShare]. Empty, never null: "the caller forced nothing"
            // is a real answer every pass reads the same way, and a nullable set here would put a `?? false` at
            // each of them.
            HashSet<string> ShareMembers,
            // Destination members carrying a [MapDenseEnumKeys], mapped to the Offset each one declared. Empty,
            // never null, for ShareMembers' reason; a DICTIONARY rather than a set because the directive carries
            // a value the emitted arithmetic depends on, and validation has already refused two applications
            // naming one member.
            Dictionary<string, int> DenseEnumMembers,
            HashSet<string>? ConsumedCtorParams,
            HashSet<string>? RequiredMustInitialize,
            NestedMappingRegistry? NestedRegistry,
            // Never null: ResolveMembers settles "no list" as "an empty list" before this is built.
            IReadOnlyList<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>
                MapValues,
            IReadOnlyList<(string Name, ITypeSymbol ReturnType)>? ValueProviders,
            IReadOnlyList<(string Name, ITypeSymbol Type)>? ExtraParams,
            Dictionary<string, string>? StringFormats,
            bool RequiredMembersAlreadySatisfied,
            IReadOnlyCollection<string>? FactoryExcludedMembers,
            // Source members disowned by [MapIgnoreSource]; read only by the DWARF064 shadow rule.
            HashSet<string>? IgnoredSourceMembers);

        /// <summary>
        ///     The name lookups the prologue derives from a <see cref="MemberRequest" /> and every pass then reads.
        ///     Nothing writes to these after construction.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>ReservedConverters</c> is the field worth naming: it looks mutable, and it is -- but its only
        ///         three write-sites are in the prologue, so it is settled before any pass runs and belongs on this
        ///         side of the line. That was established by grepping write-sites, which is also how
        ///         <c>diagnostics</c> was kept OUT of this record.
        ///     </para>
        ///     <para>
        ///         A BUNDLE OVER THE CALLER'S OWN INSTANCES, not a copy. <c>ResolveMembers</c> keeps using its
        ///         locals directly and hands the passes this view of them; the collections are the same objects,
        ///         which is what lets a pass be lifted out without disturbing the code left behind.
        ///     </para>
        /// </remarks>
        private sealed record MemberLookups(
            StringComparer Comparer,
            bool Flexible,
            Dictionary<string, ITypeSymbol> WritableByName,
            Dictionary<string, List<(string Name, ITypeSymbol Type)>> SourceGroups,
            List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves, bool NullableHop)> FlattenInfos,
            HashSet<string> ReservedConverters,
            Dictionary<string, (bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>
                ExtrasByTarget);

        /// <summary>
        ///     What the passes FILL: the member list being built, the diagnostics they raise, the helpers they
        ///     synthesize, and the three sets recording what has already been claimed -- so a later pass does not
        ///     claim it twice and the leftovers can be reported.
        /// </summary>
        /// <remarks>
        ///     Membership here is by MEASURED write-site, not by how a name reads. <c>Diagnostics</c> and
        ///     <c>Synthesized</c> arrive as parameters of <c>ResolveMembers</c> and look like inputs; the passes
        ///     write to both, so they are outputs. <c>ExtractCore</c>'s bundles draw the same line for the same
        ///     two names.
        /// </remarks>
        private sealed record MemberAccumulators(
            List<MemberMap> Result,
            List<DiagnosticInfo> Diagnostics,
            Dictionary<string, SynthesizedMethod> Synthesized,
            HashSet<string> HandledTargets,
            HashSet<string> ConsumedExtraParams,
            HashSet<string> ConsumedFlattenRoots);
    }
}
