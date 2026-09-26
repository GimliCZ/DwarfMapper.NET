// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;

// Unit tests for MapperExtractor.ExpandBareOverloadedEdges, extracted from DetectDeclaredMethodsOnRecursionCycle (owner
// ruling 2026-09-13: extract, expose, test — no deletion). The loop existed three times — inline for members, inline for
// constructor arguments, and once over the whole graph — and every copy went to zero hits once c56b9e5 and 3dfe5c5 gave
// each user-declared converter edge its exact overload. Probed with overloaded names first (the shape behind 7635d71
// and the unflatten-leaf hole): an overloaded Use= on an unflatten leaf WAS still bare, which is 3dfe5c5; after it no
// surface input produced a bare overloaded edge. The two inline copies were exactly what the graph-wide pass does next,
// so they now add the bare name and leave the expansion to the one copy tested here.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ExpandBareOverloadedEdgesUnitTests
    {
        private static MapMethodModel Declared(string name, string parameterType, bool isPartial = true) =>
            new(name, "public", "global::Demo.Dto", parameterType, "s", true,
                EquatableArray.From(Array.Empty<MemberMap>()),
                EquatableArray.From(Array.Empty<string>()),
                EquatableArray.From(Array.Empty<HookCall>()),
                false,
                "",
                IsPartial: isPartial);

        private static readonly List<MapMethodModel> Methods =
        [
            Declared("Map", "global::Demo.A"),
            Declared("Map", "global::Demo.B"),
            Declared("Map", "global::Demo.Ignored", isPartial: false),
            Declared("Other", "global::Demo.C")
        ];

        private static readonly Dictionary<string, int> DeclaredNameCount = new(StringComparer.Ordinal) { ["Map"] = 2, ["Other"] = 1 };

        private static Dictionary<string, HashSet<string>> Graph(params (string Caller, string[] Edges)[] nodes)
        {
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (caller, edges) in nodes)
                graph[caller] = new HashSet<string>(edges, StringComparer.Ordinal);
            return graph;
        }

        [Fact]
        public void A_bare_overloaded_edge_becomes_every_declared_overload_but_the_caller()
        {
            var graph = Graph(("__DwarfMap_Obj_X", ["Map"]), ("Map§global::Demo.A", ["Map"]));

            MapperExtractor.ExpandBareOverloadedEdges(graph, Methods, DeclaredNameCount);

            Assert.Equal(new[] { "Map§global::Demo.A", "Map§global::Demo.B" }, graph["__DwarfMap_Obj_X"].OrderBy(e => e, StringComparer.Ordinal));
            Assert.Equal(new[] { "Map§global::Demo.B" }, graph["Map§global::Demo.A"]);
        }

        [Fact]
        public void Non_overloaded_synthesized_and_exact_edges_are_left_alone()
        {
            var graph = Graph(("Other", ["Other", "__DwarfMap_Obj_Y", "Map§global::Demo.B", "Unknown"]));

            MapperExtractor.ExpandBareOverloadedEdges(graph, Methods, DeclaredNameCount);

            Assert.Equal(new[] { "Map§global::Demo.B", "Other", "Unknown", "__DwarfMap_Obj_Y" }, graph["Other"].OrderBy(e => e, StringComparer.Ordinal));
        }
    }
}
