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

    /// <summary>
    ///     A non-scalar STRUCT: not a primitive, enum, string, decimal, Guid or date, so the walkers reach it as a
    ///     composite rather than as a scalar. Value types are compared without being recorded in the cycle guard,
    ///     because a value cannot form a reference cycle.
    /// </summary>
    public record struct Extent
    {
        public int W { get; set; }

        public int H { get; set; }
    }

    public class HasExtent
    {
        public int Id { get; set; }

        public Extent Size { get; set; }
    }

    /// <summary>The DTO half of <see cref="HasExtent" />, so the same shape can be driven across a type pair.</summary>
    public class HasExtentDto
    {
        public int Id { get; set; }

        public Extent Size { get; set; }
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

        public OddMembersNode? Next { get; set; }

        public int this[int index] => V + index;

        public int Sink
        {
            set => V = value;
        }
    }

    // A cross-type pair where the ACTUAL side cannot supply some of the expected members: `Missing` and
    // `MissingField` do not exist on it at all, and `Hidden` exists but is write-only. Internal for the same
    // CA1044 reason as OddMembersNode.
    internal sealed class WideExpected
    {
        public int V { get; set; }

        public int Missing { get; set; }

        public int Hidden { get; set; }

        public int MissingField;

        public int SharedField;
    }

    internal sealed class NarrowActual
    {
        public int V { get; set; }

        public int Hidden
        {
            set => V = value;
        }

        public int SharedField;
    }

    // An enumerable that is neither a collection nor hands out a disposable enumerator. Checking whether it is
    // empty means actually enumerating it, and afterwards there is nothing to dispose.
    internal sealed class BareEnumerable : System.Collections.IEnumerable
    {
        private readonly int _count;

        public BareEnumerable(int count)
        {
            _count = count;
        }

        public System.Collections.IEnumerator GetEnumerator()
        {
            return new BareEnumerator(_count);
        }

        private sealed class BareEnumerator : System.Collections.IEnumerator
        {
            private readonly int _count;
            private int _position;

            public BareEnumerator(int count)
            {
                _count = count;
            }

            public object Current => _position;

            public bool MoveNext()
            {
                return _position++ < _count;
            }

            public void Reset()
            {
                _position = 0;
            }
        }
    }

    // A flatten-graph node whose collection edges may hold ANY object, not only nodes: the breadth-first search
    // has to skip a null and a non-node element in a list edge and in a dictionary's values.
    internal sealed class LooseNode
    {
        public int V { get; set; }

        public List<object?>? Items { get; set; }

        public Dictionary<string, object?>? ByName { get; set; }

        public int[]? Numbers { get; set; }

        public List<int>? Counts { get; set; }
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
        private static readonly int[] OneTwo = { 1, 2 };

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

        /// <summary>
        ///     The flatten-graph search follows collection and dictionary edges, and it enqueues only the elements
        ///     that are nodes. A null element and a non-node element are skipped in a list edge, and so are a null
        ///     value and a non-node value in a dictionary. Exactly three nodes are reachable: the entry, the list
        ///     child and the dictionary child. So three results pass, and two report the count mismatch naming
        ///     both numbers.
        /// </summary>
        [Fact]
        public void Flatten_graph_search_skips_null_and_non_node_elements_of_list_and_dictionary_edges()
        {
            var src = new LooseNode
            {
                V = 1,
                Items = new List<object?> { null, "not a node", new LooseNode { V = 2 } },
                ByName = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["empty"] = null,
                    ["text"] = "not a node",
                    ["child"] = new LooseNode { V = 3 }
                }
            };

            Assert.Empty(GraphOracleComparer.FlattenGraphDiff(
                src,
                new[] { new LooseNode { V = 1 }, new LooseNode { V = 2 }, new LooseNode { V = 3 } },
                typeof(LooseNode),
                typeof(LooseNode)));

            var violation = Assert.Single(GraphOracleComparer.FlattenGraphDiff(
                src,
                new[] { new LooseNode { V = 1 }, new LooseNode { V = 2 } },
                typeof(LooseNode),
                typeof(LooseNode)));
            Assert.Equal("FlattenGraph count mismatch: BFS-reachable=3, result.Count=2", violation);
        }

        /// <summary>
        ///     A result DTO's array or generic collection of PLAIN data is not a navigation edge. An <c>int[]</c>
        ///     and a <c>List&lt;int&gt;</c> name no DTO type, so they may be populated after flattening. A
        ///     collection whose element type could hold a DTO is a navigation edge, and it must be null: here that
        ///     is <c>List&lt;object?&gt;</c>, since <c>object</c> is assignable from the DTO type. That is the
        ///     control.
        /// </summary>
        [Fact]
        public void Flatten_graph_does_not_treat_arrays_or_generics_of_plain_data_as_navigation_edges()
        {
            var src = new LooseNode { V = 1 };

            Assert.Empty(GraphOracleComparer.FlattenGraphDiff(
                src,
                new[] { new LooseNode { V = 1, Numbers = OneTwo, Counts = new List<int> { 3 } } },
                typeof(LooseNode),
                typeof(LooseNode)));

            var violation = Assert.Single(GraphOracleComparer.FlattenGraphDiff(
                src,
                new[] { new LooseNode { V = 1, Items = new List<object?>() } },
                typeof(LooseNode),
                typeof(LooseNode)));
            Assert.StartsWith("FlattenGraph edge not degraded: LooseNode.Items", violation, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The topology oracle's member walk skips an indexer and a write-only property, and still follows the
        ///     reference edge beside them. A self-cycle mapped to a self-cycle is preserved. The same cycle mapped
        ///     onto a SECOND instance is the shared-node violation, reported at the edge.
        /// </summary>
        [Fact]
        public void Topology_skips_indexers_and_write_only_properties_and_still_checks_the_edges()
        {
            var src = new OddMembersNode { V = 1 };
            src.Next = src;

            var closed = new OddMembersNode { V = 1 };
            closed.Next = closed;
            Assert.Empty(GraphOracleComparer.TopologyDiff(src, closed));

            var open = new OddMembersNode { V = 1, Next = new OddMembersNode { V = 1 } };
            var violation = Assert.Single(GraphOracleComparer.TopologyDiff(src, open));
            Assert.StartsWith("root.Next: shared source node", violation, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The topology walk pairs source and target members by name, and a target that cannot supply one
        ///     leaves it unpaired rather than failing. That covers a property the target lacks, a property it has
        ///     only a setter for, and a field it lacks. Each such source member is walked against null, and a
        ///     value against null carries no topology, so no violation is reported and nothing throws.
        /// </summary>
        [Fact]
        public void Topology_leaves_members_the_target_lacks_or_cannot_read_unpaired()
        {
            var src = new WideExpected { V = 1, Missing = 5, Hidden = 6, MissingField = 7, SharedField = 3 };
            var tgt = new NarrowActual { V = 1, SharedField = 3 };

            Assert.Empty(GraphOracleComparer.TopologyDiff(src, tgt));
            Assert.True(GraphOracleComparer.TopologyPreserved(src, tgt));

            // The mirror image: a missing SOURCE against a present target is a value difference, not a topology one.
            Assert.Empty(GraphOracleComparer.TopologyDiff(null, tgt));
        }

        /// <summary>
        ///     The cross-type oracle's property walk skips an indexer and a write-only property on the EXPECTED
        ///     type, and still compares the ordinary members by name. Equal instances produce no diffs; a changed
        ///     value is reported at its path.
        /// </summary>
        [Fact]
        public void Cross_type_compare_skips_indexers_and_write_only_properties_and_still_reports_a_real_difference()
        {
            Assert.Empty(GraphOracleComparer.CrossTypeDiff(new OddMembersNode { V = 1 }, new OddMembersNode { V = 1 }));

            var diff = Assert.Single(
                GraphOracleComparer.CrossTypeDiff(new OddMembersNode { V = 1 }, new OddMembersNode { V = 2 }));
            Assert.StartsWith("root.V", diff, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A member the ACTUAL type cannot supply is not compared: a property it lacks, a property it has only
        ///     a setter for, and a field it lacks. Cross-type comparison is by name across two different types, so
        ///     a destination that legitimately drops or hides a member must not read as a value difference, or
        ///     crash on a getter that is not there. The shared member is still compared, which is the control.
        /// </summary>
        [Fact]
        public void Cross_type_compare_skips_members_the_actual_type_lacks_or_cannot_read()
        {
            var expected = new WideExpected { V = 1, Missing = 5, Hidden = 6, MissingField = 7, SharedField = 3 };

            Assert.Empty(GraphOracleComparer.CrossTypeDiff(expected, new NarrowActual { V = 1, SharedField = 3 }));

            var propertyDiff = Assert.Single(
                GraphOracleComparer.CrossTypeDiff(expected, new NarrowActual { V = 2, SharedField = 3 }));
            Assert.StartsWith("root.V", propertyDiff, StringComparison.Ordinal);

            // And a field BOTH types have is still compared by the field loop that skips the missing one.
            var fieldDiff = Assert.Single(
                GraphOracleComparer.CrossTypeDiff(expected, new NarrowActual { V = 1, SharedField = 4 }));
            Assert.StartsWith("root.SharedField", fieldDiff, StringComparison.Ordinal);
        }

        /// <summary>
        ///     With no declared types, CrossTypeDiff takes each side's type from its value, and a null value has no
        ///     type to take. Two nulls are equal and produce nothing. A null against a value is reported with the
        ///     null named, rather than the missing type turning into an exception.
        /// </summary>
        [Fact]
        public void Cross_type_diff_without_declared_types_handles_null_values()
        {
            Assert.Empty(GraphOracleComparer.CrossTypeDiff(null, null));

            Assert.Equal("root: expected <null>, actual 5", Assert.Single(GraphOracleComparer.CrossTypeDiff(null, 5)));
        }

        /// <summary>
        ///     A null source against an EMPTY destination collection is the documented null-to-empty mapping, not
        ///     a difference. Whether a collection is empty has to be judged even when it is neither an
        ///     <c>ICollection</c> with a count nor an enumerable whose enumerator can be disposed. An empty bare
        ///     enumerable is accepted; one with an element is reported against the null.
        /// </summary>
        [Fact]
        public void A_null_source_against_a_bare_enumerable_is_judged_by_enumerating_it()
        {
            Assert.Empty(GraphOracleComparer.CrossTypeDiff(null, new BareEnumerable(0)));

            var diff = Assert.Single(GraphOracleComparer.CrossTypeDiff(null, new BareEnumerable(1)));
            Assert.StartsWith("root: expected <null>, actual ", diff, StringComparison.Ordinal);

            // And an empty compiler-generated iterator, whose enumerator IS disposable, is accepted the same way.
            Assert.Empty(GraphOracleComparer.CrossTypeDiff(null, NothingYielded()));
        }

        private static IEnumerable<int> NothingYielded()
        {
            yield break;
        }

        /// <summary>
        ///     The cross-type oracle stops descending at its depth cap, which is what lets it finish on a graph
        ///     deeper than any mapping test needs. Along a 20-node chain, a difference near the top is reported,
        ///     and the same difference near the bottom, below the cap, is not looked at.
        /// </summary>
        [Fact]
        public void Cross_type_compare_stops_descending_at_its_depth_cap()
        {
            var expected = Chain(20, null);

            Assert.Empty(GraphOracleComparer.CrossTypeDiff(expected, Chain(20, null)));
            Assert.Single(GraphOracleComparer.CrossTypeDiff(expected, Chain(20, 3)));
            Assert.Empty(GraphOracleComparer.CrossTypeDiff(expected, Chain(20, 18)));
        }

        /// <summary>A chain linked through <c>Left</c>, where node i holds i, except node <paramref name="differentAt" /> holds -1.</summary>
        private static OracleNode Chain(int length, int? differentAt)
        {
            var root = new OracleNode { V = differentAt == 0 ? -1 : 0 };
            var current = root;
            for (var i = 1; i < length; i++)
            {
                var next = new OracleNode { V = differentAt == i ? -1 : i };
                current.Left = next;
                current = next;
            }

            return root;
        }

        /// <summary>
        ///     A struct member is compared member-by-member, and is NOT recorded in the cycle guard: a value has
        ///     no reference identity, so two equal structs are not the same node and recording them would make the
        ///     second occurrence look like a cycle and stop the walk.
        /// </summary>
        [Fact]
        public void A_struct_member_is_compared_by_value_and_never_treated_as_a_cycle()
        {
            var expected = new HasExtent { Id = 1, Size = new Extent { W = 3, H = 4 } };

            Assert.Empty(GraphOracleComparer.ValueDiff(expected,
                new HasExtent { Id = 1, Size = new Extent { W = 3, H = 4 } }));

            var diff = Assert.Single(GraphOracleComparer.ValueDiff(expected,
                new HasExtent { Id = 1, Size = new Extent { W = 3, H = 5 } }));

            Assert.Equal("root.Size.H: expected 4, actual 5", diff);
        }

        /// <summary>The same across a type pair, which is the other walker and its own cycle guard.</summary>
        [Fact]
        public void A_struct_member_is_compared_by_value_across_a_type_pair()
        {
            var expected = new HasExtent { Id = 1, Size = new Extent { W = 3, H = 4 } };

            Assert.Empty(GraphOracleComparer.CrossTypeDiff(expected,
                new HasExtentDto { Id = 1, Size = new Extent { W = 3, H = 4 } }));

            var diff = Assert.Single(GraphOracleComparer.CrossTypeDiff(expected,
                new HasExtentDto { Id = 1, Size = new Extent { W = 4, H = 4 } }));

            Assert.Equal("root.Size.W: expected 3, actual 4", diff);
        }

        // ── Scalar equality ──────────────────────────────────────────────────────────

        /// <summary>
        ///     The value oracle's floating-point tolerance applies only when BOTH sides have the same floating
        ///     type. A double against an int, or a float against a double, falls through to <c>Equals</c>, which is
        ///     false across boxed types, so the pair is reported. The controls are the same type on both sides a
        ///     hair apart, inside the tolerance, which report nothing.
        /// </summary>
        [Fact]
        public void Value_compare_applies_the_float_tolerance_only_between_the_same_floating_type()
        {
            Assert.Empty(GraphOracleComparer.ValueDiff(1.0, 1.0 + 1e-12));
            Assert.Empty(GraphOracleComparer.ValueDiff(1f, 1.0000005f));

            Assert.StartsWith("root", Assert.Single(GraphOracleComparer.ValueDiff(1.0, 1)), StringComparison.Ordinal);
            Assert.StartsWith("root", Assert.Single(GraphOracleComparer.ValueDiff(1f, 1.0)), StringComparison.Ordinal);
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
