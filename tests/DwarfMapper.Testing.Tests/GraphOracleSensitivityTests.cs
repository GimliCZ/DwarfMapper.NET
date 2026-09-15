// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Testing.Tests
{
    // Fixtures for the sensitivity tests. Two node shapes with the same members so that CrossTypeDiff and
    // TopologyDiff can be driven across a source/DTO pair, one shape whose edges are public FIELDS (the walkers
    // have a separate field loop), and a source/DTO pair for FlattenGraphDiff whose DTO carries every navigation
    // shape IsNavigationProperty distinguishes: a single reference, an array, and a generic collection.

    public class OracleNode
    {
        public int V { get; set; }
        public OracleNode? Left { get; set; }
        public OracleNode? Right { get; set; }
        public OracleNode? Back { get; set; }
    }

    public class OracleNodeDto
    {
        public int V { get; set; }
        public OracleNodeDto? Left { get; set; }
        public OracleNodeDto? Right { get; set; }
        public OracleNodeDto? Back { get; set; }
    }

    public class FieldNode
    {
        public int V;
        public FieldNode? Next;
    }

    public enum OracleColour
    {
        Red,
        Green
    }

    public enum OracleColourDto
    {
        Red,
        Green
    }

    public class FlatNode
    {
        public int V { get; set; }
        public FlatNode? Next { get; set; }
        public List<FlatNode> Kids { get; set; } = new();
        public Dictionary<string, FlatNode> ByName { get; set; } = new(StringComparer.Ordinal);
    }

    // CA1819: an array-typed navigation is the shape IsNavigationProperty's array arm exists for.
#pragma warning disable CA1819
    public class FlatDto
    {
        public int V { get; set; }
        public FlatDto? Next { get; set; }
        public FlatDto[]? Peers { get; set; }
        public List<FlatDto>? Kids { get; set; }
    }
#pragma warning restore CA1819

    // The two property shapes every reflection walker in the oracle has to SKIP rather than read: an indexer
    // (GetValue without index arguments throws) and a write-only property (GetValue with no getter throws).
    // Internal because CA1044 rejects a write-only property on an externally visible type; the oracle reflects
    // on public properties, so the type's own visibility does not change what it walks.
    internal sealed class OddMembersNode
    {
        public int V { get; set; }

        public int this[int index] => V + index;

        public int Sink
        {
            set => V = value;
        }
    }

    /// <summary>
    ///     Negative controls for <see cref="GraphOracleComparer" />: each proves that a specific violation IS
    ///     reported, not merely that a correct mapping is silent.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every consumer of the oracle in the tree asserts <c>Count == 0</c>. Until these were pinned no test
    ///         executed a single violation-reporting line in <c>TopologyCompare</c>, <c>FlattenGraphDiff</c> or
    ///         <c>CrossTypeCompare</c>, nor the null/count arms of <c>ValueCompare</c>, and
    ///         <c>TopologyPreserved</c> was never called — so a mutant that turned any <c>violations.Add</c> into
    ///         a no-op survived the whole suite, and the oracle would have graded every mapper green while
    ///         measuring nothing. No mutation leg covers DwarfMapper.Testing; these are the oracle's own oracle.
    ///     </para>
    ///     <para>
    ///         Each test pairs the violation with the positive control it is one edit away from, so the pin is on
    ///         the DISTINCTION and not on the oracle merely producing text.
    ///     </para>
    /// </remarks>
    public class GraphOracleSensitivityTests
    {
        // ── Topology ─────────────────────────────────────────────────────────────────

        private static (OracleNode Src, OracleNodeDto Shared, OracleNodeDto Duplicated) Diamond()
        {
            var shared = new OracleNode { V = 2 };
            var src = new OracleNode { V = 1, Left = shared, Right = shared };

            var sharedDto = new OracleNodeDto { V = 2 };
            var preserved = new OracleNodeDto { V = 1, Left = sharedDto, Right = sharedDto };

            // Value-identical to `preserved`; only the sharing differs.
            var duplicated = new OracleNodeDto
            {
                V = 1,
                Left = new OracleNodeDto { V = 2 },
                Right = new OracleNodeDto { V = 2 }
            };

            return (src, preserved, duplicated);
        }

        [Fact]
        public void Topology_reports_a_shared_source_node_mapped_to_two_target_instances()
        {
            var (src, shared, duplicated) = Diamond();

            // The value oracle cannot tell them apart -- that is the whole reason the topology oracle exists.
            Assert.Empty(GraphOracleComparer.CrossTypeDiff(src, duplicated, typeof(OracleNode), typeof(OracleNodeDto)));

            Assert.Empty(GraphOracleComparer.TopologyDiff(src, shared));
            Assert.True(GraphOracleComparer.TopologyPreserved(src, shared));

            var violation = Assert.Single(GraphOracleComparer.TopologyDiff(src, duplicated));
            Assert.StartsWith("root.Right: shared source node", violation, StringComparison.Ordinal);
            Assert.Contains("but got a different instance", violation, StringComparison.Ordinal);
            Assert.False(GraphOracleComparer.TopologyPreserved(src, duplicated));
        }

        // ── Property shapes the walkers must skip ────────────────────────────────────

        /// <summary>
        ///     The value oracle skips an indexer and a write-only property instead of reading them. Either read
        ///     would throw inside the oracle, so a consumer type carrying one would fail every comparison. The
        ///     ordinary property beside them is still compared: equal instances are silent, and a changed value
        ///     is reported at its path.
        /// </summary>
        [Fact]
        public void Value_compare_skips_indexers_and_write_only_properties_and_still_reports_a_real_difference()
        {
            Assert.Empty(GraphOracleComparer.ValueDiff(new OddMembersNode { V = 1 }, new OddMembersNode { V = 1 }));

            var diff = Assert.Single(
                GraphOracleComparer.ValueDiff(new OddMembersNode { V = 1 }, new OddMembersNode { V = 2 }));
            Assert.StartsWith("root.V", diff, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The flatten-graph oracle skips an indexer and a write-only property in BOTH of its property walks:
        ///     the breadth-first search over the source nodes, and the navigation-null check over the result DTOs.
        ///     The count check still runs, which is the control: one node flattened to one DTO passes, and to two
        ///     DTOs reports the mismatch.
        /// </summary>
        [Fact]
        public void Flatten_graph_skips_indexers_and_write_only_properties_in_both_walks()
        {
            var src = new OddMembersNode { V = 1 };

            Assert.Empty(GraphOracleComparer.FlattenGraphDiff(
                src,
                new[] { new OddMembersNode { V = 1 } },
                typeof(OddMembersNode),
                typeof(OddMembersNode)));

            var violation = Assert.Single(GraphOracleComparer.FlattenGraphDiff(
                src,
                new[] { new OddMembersNode { V = 1 }, new OddMembersNode { V = 1 } },
                typeof(OddMembersNode),
                typeof(OddMembersNode)));
            Assert.StartsWith("FlattenGraph count mismatch", violation, StringComparison.Ordinal);
        }

        [Fact]
        public void Topology_reports_a_cycle_the_target_did_not_close()
        {
            var src = new OracleNode { V = 1 };
            src.Back = src;

            var closed = new OracleNodeDto { V = 1 };
            closed.Back = closed;
            Assert.Empty(GraphOracleComparer.TopologyDiff(src, closed));

            var open = new OracleNodeDto { V = 1, Back = new OracleNodeDto { V = 1 } };
            var violation = Assert.Single(GraphOracleComparer.TopologyDiff(src, open));
            Assert.StartsWith("root.Back: shared source node", violation, StringComparison.Ordinal);
        }

        [Fact]
        public void Topology_walks_edges_held_in_public_fields()
        {
            var src = new FieldNode { V = 1 };
            src.Next = src;

            var closed = new FieldNode { V = 1 };
            closed.Next = closed;
            Assert.Empty(GraphOracleComparer.TopologyDiff(src, closed));

            var open = new FieldNode { V = 1, Next = new FieldNode { V = 1 } };
            var violation = Assert.Single(GraphOracleComparer.TopologyDiff(src, open));
            Assert.StartsWith("root.Next: shared source node", violation, StringComparison.Ordinal);
        }

        [Fact]
        public void Topology_leaves_a_null_mismatch_to_the_value_oracle()
        {
            // A missing target node is a VALUE defect; the topology oracle does not double-report it.
            Assert.Empty(GraphOracleComparer.TopologyDiff(new OracleNode { Left = new OracleNode() }, new OracleNodeDto()));
            Assert.Empty(GraphOracleComparer.TopologyDiff(new OracleNode(), null));
            Assert.True(GraphOracleComparer.TopologyPreserved(null, null));
        }

        [Fact]
        public void Topology_keeps_a_violation_found_above_its_depth_cap()
        {
            // A chain longer than the walker's depth cap (64), with a back-edge from node 10 to the root. The
            // cap stops the walk below node 64; the broken back-edge sits well above it and must still be found.
            const int length = 70;
            var srcNodes = new OracleNode[length];
            var tgtNodes = new OracleNodeDto[length];
            for (var i = length - 1; i >= 0; i--)
            {
                srcNodes[i] = new OracleNode { V = i, Left = i + 1 < length ? srcNodes[i + 1] : null };
                tgtNodes[i] = new OracleNodeDto { V = i, Left = i + 1 < length ? tgtNodes[i + 1] : null };
            }

            srcNodes[10].Back = srcNodes[0];

            tgtNodes[10].Back = tgtNodes[0];
            Assert.Empty(GraphOracleComparer.TopologyDiff(srcNodes[0], tgtNodes[0]));

            tgtNodes[10].Back = new OracleNodeDto { V = 0 };
            var violation = Assert.Single(GraphOracleComparer.TopologyDiff(srcNodes[0], tgtNodes[0]));
            var expectedPath = "root" + string.Concat(Enumerable.Repeat(".Left", 10)) + ".Back: ";
            Assert.StartsWith(expectedPath, violation, StringComparison.Ordinal);
        }

        [Fact]
        public void RenderTopologyDiff_prefixes_each_line_and_rejects_null()
        {
            var text = GraphOracleComparer.RenderTopologyDiff(new List<string> { "root.Right: x", "root.Back: y" });

            Assert.Contains("  TOPOLOGY: root.Right: x", text, StringComparison.Ordinal);
            Assert.Contains("  TOPOLOGY: root.Back: y", text, StringComparison.Ordinal);
            Assert.Equal("violations",
                Assert.Throws<ArgumentNullException>(() => GraphOracleComparer.RenderTopologyDiff(null!)).ParamName);
        }

        // ── FlattenGraph ─────────────────────────────────────────────────────────────

        private static IReadOnlyList<string> Flatten(FlatNode? entry, IEnumerable<FlatDto>? results)
        {
            return GraphOracleComparer.FlattenGraphDiff(entry, results, typeof(FlatNode), typeof(FlatDto));
        }

        [Fact]
        public void FlattenGraph_reports_a_result_count_that_does_not_match_the_reachable_set()
        {
            var c = new FlatNode { V = 3 };
            var b = new FlatNode { V = 2, Next = c };
            var a = new FlatNode { V = 1, Next = b };

            Assert.Empty(Flatten(a, new List<FlatDto> { new() { V = 1 }, new() { V = 2 }, new() { V = 3 } }));

            var violation = Assert.Single(Flatten(a, new List<FlatDto> { new() { V = 1 }, new() { V = 2 } }));
            Assert.Equal("FlattenGraph count mismatch: BFS-reachable=3, result.Count=2", violation);
        }

        [Fact]
        public void FlattenGraph_reports_every_navigation_the_result_left_populated()
        {
            var src = new FlatNode { V = 1 };

            Assert.Empty(Flatten(src, new List<FlatDto> { new() { V = 1 } }));

            // An EMPTY collection is still "not degraded": the contract is null, not empty.
            var dto = new FlatDto
            {
                V = 1,
                Next = new FlatDto(),
                Peers = Array.Empty<FlatDto>(),
                Kids = new List<FlatDto>()
            };
            var violations = Flatten(src, new List<FlatDto> { dto });

            Assert.Equal(3, violations.Count);
            Assert.Contains(violations, v => v.StartsWith("FlattenGraph edge not degraded: FlatDto.Next = ", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.StartsWith("FlattenGraph edge not degraded: FlatDto.Peers = ", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.StartsWith("FlattenGraph edge not degraded: FlatDto.Kids = ", StringComparison.Ordinal));
            Assert.All(violations, v => Assert.EndsWith(" (expected null)", v, StringComparison.Ordinal));
        }

        [Fact]
        public void FlattenGraph_reaches_nodes_held_only_as_dictionary_values()
        {
            var a = new FlatNode { V = 1 };
            a.ByName["b"] = new FlatNode { V = 2 };
            a.ByName["c"] = new FlatNode { V = 3 };

            Assert.Empty(Flatten(a, new List<FlatDto> { new() { V = 1 }, new() { V = 2 }, new() { V = 3 } }));

            var violation = Assert.Single(Flatten(a, new List<FlatDto> { new() { V = 1 } }));
            Assert.Equal("FlattenGraph count mismatch: BFS-reachable=3, result.Count=1", violation);
        }

        [Fact]
        public void FlattenGraph_rejects_null_type_arguments_and_accepts_an_empty_graph()
        {
            Assert.Equal("nodeBaseType",
                Assert.Throws<ArgumentNullException>(
                    () => GraphOracleComparer.FlattenGraphDiff(null, null, null!, typeof(FlatDto))).ParamName);
            Assert.Equal("dtoBaseType",
                Assert.Throws<ArgumentNullException>(
                    () => GraphOracleComparer.FlattenGraphDiff(null, null, typeof(FlatNode), null!)).ParamName);

            Assert.Empty(Flatten(null, null));
        }

        [Fact]
        public void RenderFlattenGraphDiff_prefixes_each_line_and_rejects_null()
        {
            var text = GraphOracleComparer.RenderFlattenGraphDiff(new List<string> { "count", "edge" });

            Assert.Contains("  FLATGRAPH: count", text, StringComparison.Ordinal);
            Assert.Contains("  FLATGRAPH: edge", text, StringComparison.Ordinal);
            Assert.Equal("violations",
                Assert.Throws<ArgumentNullException>(() => GraphOracleComparer.RenderFlattenGraphDiff(null!)).ParamName);
        }

        // ── ValueDiff ────────────────────────────────────────────────────────────────

        [Fact]
        public void ValueDiff_reports_null_against_a_value_and_accepts_null_against_null()
        {
            Assert.Empty(GraphOracleComparer.ValueDiff(null, null));
            Assert.True(GraphOracleComparer.ValueEqual(null, null));

            Assert.Equal("root: expected <null>, actual x", Assert.Single(GraphOracleComparer.ValueDiff(null, "x")));
            Assert.Equal("root: expected x, actual <null>", Assert.Single(GraphOracleComparer.ValueDiff("x", null)));
            Assert.False(GraphOracleComparer.ValueEqual("x", null));
        }

        [Fact]
        public void ValueDiff_reports_a_collection_count_mismatch_at_the_collection_path()
        {
            var diffs = GraphOracleComparer.ValueDiff(new List<int> { 1, 2 }, new List<int> { 1 });

            Assert.Equal("root.Count: expected 2, actual 1", Assert.Single(diffs));
        }

        [Fact]
        public void ValueDiff_compares_public_fields()
        {
            Assert.Empty(GraphOracleComparer.ValueDiff(new FieldNode { V = 1 }, new FieldNode { V = 1 }));

            var diff = Assert.Single(GraphOracleComparer.ValueDiff(new FieldNode { V = 1 }, new FieldNode { V = 2 }));
            Assert.Equal("root.V: expected 1, actual 2", diff);
        }

        [Fact]
        public void ValueDiff_terminates_on_a_cycle_and_reports_each_difference_once()
        {
            var a = new FieldNode { V = 1 };
            a.Next = a;
            var b = new FieldNode { V = 1 };
            b.Next = b;

            Assert.Empty(GraphOracleComparer.ValueDiff(a, b));

            b.V = 2;
            Assert.Equal("root.V: expected 1, actual 2", Assert.Single(GraphOracleComparer.ValueDiff(a, b)));
        }

        [Fact]
        public void ValueDiff_compares_enums_by_value()
        {
            Assert.Empty(GraphOracleComparer.ValueDiff(DayOfWeek.Monday, DayOfWeek.Monday));

            var diff = Assert.Single(GraphOracleComparer.ValueDiff(DayOfWeek.Monday, DayOfWeek.Tuesday));
            Assert.Equal("root: expected Monday, actual Tuesday", diff);
        }

        [Fact]
        public void ValueDiff_reports_a_difference_below_the_top_level_within_its_depth_cap()
        {
            // Deeper than the walker's depth cap (16); the difference sits at depth 4, well within it.
            const int length = 20;
            var expected = new OracleNode[length];
            var actual = new OracleNode[length];
            for (var i = length - 1; i >= 0; i--)
            {
                expected[i] = new OracleNode { V = i, Left = i + 1 < length ? expected[i + 1] : null };
                actual[i] = new OracleNode { V = i, Left = i + 1 < length ? actual[i + 1] : null };
            }

            Assert.Empty(GraphOracleComparer.ValueDiff(expected[0], actual[0]));

            actual[3].V = 30;
            var diff = Assert.Single(GraphOracleComparer.ValueDiff(expected[0], actual[0]));
            Assert.Equal("root.Left.Left.Left.V: expected 3, actual 30", diff);
        }

        [Fact]
        public void ValueDiff_orders_nulls_first_when_sorting_an_unordered_side()
        {
            var set = new HashSet<string?>(StringComparer.Ordinal) { "b", null };

            Assert.Empty(GraphOracleComparer.ValueDiff(set, new List<string?> { "b", null }));

            var diffs = GraphOracleComparer.ValueDiff(set, new List<string?> { "b", null, null });
            Assert.Equal(2, diffs.Count);
            Assert.Contains("root.Count: expected 2, actual 3", diffs);
            Assert.Contains("root[1]: expected b, actual <null>", diffs);
        }

        [Fact]
        public void RenderValueDiff_indents_each_line_and_rejects_null()
        {
            var text = GraphOracleComparer.RenderValueDiff(new List<string> { "root.V: expected 1, actual 2" });

            Assert.Contains("  root.V: expected 1, actual 2", text, StringComparison.Ordinal);
            Assert.Equal("diffs",
                Assert.Throws<ArgumentNullException>(() => GraphOracleComparer.RenderValueDiff(null!)).ParamName);
        }

        // ── CrossTypeDiff ────────────────────────────────────────────────────────────

        private static IReadOnlyList<string> Cross<TExpected, TActual>(TExpected? expected, TActual? actual)
        {
            return GraphOracleComparer.CrossTypeDiff(expected, actual, typeof(TExpected), typeof(TActual));
        }

        [Fact]
        public void CrossType_accepts_a_null_source_only_against_an_EMPTY_target_collection()
        {
            // NullCollections.AsEmpty is the documented default, so null -> [] is not a defect. null -> [x] is.
            Assert.Empty(Cross<List<int>, List<int>>(null, new List<int>()));

            Assert.Equal("root: expected <null>, actual x", Assert.Single(Cross<string, string>(null, "x")));
            Assert.Single(Cross<List<int>, List<int>>(null, new List<int> { 1 }));
        }

        [Fact]
        public void CrossType_widens_float_to_double_before_comparing()
        {
            Assert.Empty(Cross(0.1f, (double)0.1f));

            Assert.Equal("root: expected 1.5, actual 2.5", Assert.Single(Cross(1.5f, 2.5d)));
        }

        [Fact]
        public void CrossType_compares_differently_sized_integers_by_value()
        {
            Assert.Empty(Cross(7, 7L));

            Assert.Equal("root: expected 1, actual 2", Assert.Single(Cross(1, 2L)));
        }

        [Fact]
        public void CrossType_falls_back_to_scalar_equality_when_a_value_exceeds_decimal()
        {
            // 1e300 does not fit in a decimal; the numeric-widening path overflows and the scalar rule decides.
            Assert.Empty(Cross(1e300, 1e300));

            Assert.Equal("root: expected 1E+300, actual 2E+300", Assert.Single(Cross(1e300, 2e300)));
        }

        [Fact]
        public void CrossType_compares_enums_of_different_types_by_underlying_value()
        {
            Assert.Empty(Cross(OracleColour.Green, OracleColourDto.Green));

            Assert.Equal("root: expected Red, actual Green", Assert.Single(Cross(OracleColour.Red, OracleColourDto.Green)));
        }

        [Fact]
        public void CrossType_reports_a_scalar_mismatch()
        {
            Assert.Empty(Cross("a", "a"));

            Assert.Equal("root: expected a, actual b", Assert.Single(Cross("a", "b")));
        }

        [Fact]
        public void CrossType_reports_a_collection_count_mismatch()
        {
            Assert.Empty(Cross(new List<int> { 1, 2 }, new List<int> { 1, 2 }));

            var diff = Assert.Single(Cross(new List<int> { 1, 2 }, new List<int> { 1 }));
            Assert.Equal("root.Count: expected 2, actual 1", diff);
        }

        [Fact]
        public void CrossType_terminates_on_a_cycle_that_both_graphs_close()
        {
            var src = new OracleNode { V = 1 };
            src.Back = src;
            var dto = new OracleNodeDto { V = 1 };
            dto.Back = dto;

            Assert.Empty(Cross(src, dto));

            dto.V = 2;
            Assert.Equal("root.V: expected 1, actual 2", Assert.Single(Cross(src, dto)));
        }
    }
}
