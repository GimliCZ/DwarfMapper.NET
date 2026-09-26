// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.MarkRecursionCapableCallers' self-recursive branch, widened from private to internal
// (owner ruling 2026-09-13: extract, expose, test — no deletion).
//
// Why no generator fixture reaches its "already marked" member and constructor-argument arms: they need a DECLARED
// self-recursive method whose converter arrived with ConverterNeedsDepthCtx already set at resolution, and the two
// halves never meet. Under Preserve the self-call resolves to a synthesized __DwarfMap_Obj_* helper, so the declared
// method is never self-recursive (probe P1: a Next self-call beside List<int> → List<long> as member and ctor arg — the
// helpers arrived flagged, the branch never ran). In None mode the declared method IS self-recursive, but a collection
// helper resolves without ctx and is only upgraded later (probe P1b). SetNull, lists of recursive auto-nested elements
// and overloaded names were probed in earlier rounds with the same result. The arms stay as the idempotence guard their
// comment names ("previously patched"), and the branch's contract is pinned here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MarkRecursionCapableCallersUnitTests
    {
        private const string Collection = "__DwarfMapColl_Kids";
        private const string Helper = "__DwarfMap_Obj_Child";

        private static MapMethodModel Declared(string name, MemberMap[] members, MemberMap[] ctorArgs) =>
            new(name, "public", "global::Demo.NodeDto", "global::Demo.Node", "n", true,
                EquatableArray.From(members),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                EquatableArray.From(ctorArgs),
                IsPartial: true,
                MaxDepth: 64);

        private static HashSet<string> Names(params string[] names) => new(names, StringComparer.Ordinal);

        private static MemberMap[] Edges(string prefix) =>
        [
            new($"{prefix}Plain", $"{prefix}Plain"),
            new($"{prefix}Kids", $"{prefix}Kids", Collection, ConverterNeedsDepthCtx: true),
            new($"{prefix}Next", $"{prefix}Next", "Map"),
            new($"{prefix}Peer", $"{prefix}Peer", "Other"),
            new($"{prefix}Child", $"{prefix}Child", Helper),
            new($"{prefix}Name", $"{prefix}Name", "ToUpper")
        ];

        private static List<MapMethodModel> Run()
        {
            var methods = new List<MapMethodModel> { Declared("Map", Edges("M"), Edges("c")) };
            MapperExtractor.MarkRecursionCapableCallers(methods, Names(Helper), Names("Map", "Other"), 128);
            return methods;
        }

        private static void AssertPatched(MemberMap[] edges, string prefix)
        {
            Assert.Equal(new MemberMap($"{prefix}Plain", $"{prefix}Plain"), edges[0]);
            Assert.Equal(new MemberMap($"{prefix}Kids", $"{prefix}Kids", Collection, ConverterNeedsDepthCtx: true), edges[1]);
            Assert.Equal(new MemberMap($"{prefix}Next", $"{prefix}Next", "__DwarfMap_Depth_Map", ConverterNeedsDepthCtx: true), edges[2]);
            Assert.Equal(new MemberMap($"{prefix}Peer", $"{prefix}Peer", "__DwarfMap_Depth_Other", ConverterNeedsDepthCtx: true), edges[3]);
            Assert.Equal(new MemberMap($"{prefix}Child", $"{prefix}Child", Helper, ConverterNeedsDepthCtx: true), edges[4]);
            Assert.Equal(new MemberMap($"{prefix}Name", $"{prefix}Name", "ToUpper"), edges[5]);
        }

        [Fact]
        public void A_self_recursive_declared_method_keeps_already_flagged_edges_and_redirects_the_rest()
        {
            var methods = Run();

            var declared = methods[0];
            Assert.True(declared.IsRecursionCapable);
            Assert.Equal(128, declared.MaxDepth);
            AssertPatched(declared.Members.ToArray(), "M");
            AssertPatched(declared.ConstructorArguments.ToArray(), "c");
        }

        [Fact]
        public void A_self_recursive_declared_method_gets_a_private_non_partial_depth_companion_with_the_same_edges()
        {
            var methods = Run();

            Assert.Equal(2, methods.Count);
            var companion = methods[1];
            Assert.Equal("__DwarfMap_Depth_Map", companion.MethodName);
            Assert.Equal("private", companion.Accessibility);
            Assert.False(companion.IsPartial);
            Assert.True(companion.IsRecursionCapable);
            Assert.Equal(128, companion.MaxDepth);
            AssertPatched(companion.Members.ToArray(), "M");
            AssertPatched(companion.ConstructorArguments.ToArray(), "c");
        }
    }
}
