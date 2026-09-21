// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Testing.Tests
{
    public class Box
    {
        public int X { get; set; }

        public string S { get; set; } = "";

        public List<int> Xs { get; set; } = new();
    }

    /// <summary>
    ///     A type with a WRITE-ONLY property: <c>Sink</c> has no getter, and records what it was given in <c>Last</c>.
    ///     Internal because CA1044 rejects a write-only property on an externally visible type; the comparer reads
    ///     public properties by reflection, so the type's own visibility does not change what it sees.
    /// </summary>
    internal sealed class WriteOnlyBox
    {
        public int Last { get; private set; }

        public int Sink
        {
            set => Last = value;
        }
    }

    /// <summary>
    ///     A node whose members are public FIELDS. The comparer walks fields in a loop of its own, separate from
    ///     the property loop, and nothing had ever compared two objects that carry one.
    /// </summary>
    public sealed class FieldBox
    {
        public int Value;

        public FieldBox? Next;
    }

    /// <summary>A node whose only edge is a COLLECTION, so the walk descends through the enumerable arm.</summary>
    public sealed class ListNode
    {
        public int Id { get; set; }

        public List<ListNode> Kids { get; set; } = new();
    }

    /// <summary>Floating members, so the comparer's two epsilon comparisons can be driven at their boundary.</summary>
    public sealed class NumericBox
    {
        public double D { get; set; }

        public float F { get; set; }
    }

    public class StructuralComparerTests
    {
        /// <summary>
        ///     A write-only property is skipped rather than read. <c>PropertyInfo.GetValue</c> on a property with
        ///     no getter throws, so a comparer that did not filter on <c>CanRead</c> would crash on any consumer
        ///     type carrying one. The readable property beside it is still compared, which is the control.
        /// </summary>
        [Fact]
        public void A_write_only_property_is_skipped_and_the_readable_ones_are_still_compared()
        {
            var diffs = StructuralComparer.Diff(new WriteOnlyBox { Sink = 1 }, new WriteOnlyBox { Sink = 2 });

            var last = Assert.Single(diffs);
            Assert.Equal("root.Last", last.Path);
            Assert.Equal("1", last.Expected);
            Assert.Equal("2", last.Actual);
        }

        /// <summary>
        ///     The floating-point tolerance applies only when BOTH sides have the same floating type. A double
        ///     against an int, or a float against a double, falls through to <c>Equals</c>, which is false across
        ///     boxed types. So the pair is reported, even though both sides render as "1". A comparer that applied
        ///     the tolerance on the expected side's type alone would call a type mismatch equal.
        /// </summary>
        [Fact]
        public void A_floating_value_against_a_different_numeric_type_is_a_difference()
        {
            // The controls: the same floating type on both sides, a hair apart, is inside the tolerance.
            Assert.Empty(StructuralComparer.Diff(1.0, 1.0 + 1e-12));
            Assert.Empty(StructuralComparer.Diff(1f, 1.0000005f));

            var doubleVsInt = Assert.Single(StructuralComparer.Diff(1.0, 1));
            Assert.Equal("root", doubleVsInt.Path);

            var floatVsDouble = Assert.Single(StructuralComparer.Diff(1f, 1.0));
            Assert.Equal("root", floatVsDouble.Path);
        }

        [Fact]
        public void Equal_objects_have_no_diffs()
        {
            var a = new Box
            {
                X = 1,
                S = "a",
                Xs = new List<int>
                {
                    1,
                    2
                }
            };
            var b = new Box
            {
                X = 1,
                S = "a",
                Xs = new List<int>
                {
                    1,
                    2
                }
            };
            Assert.Empty(StructuralComparer.Diff(a, b));
        }

        [Fact]
        public void Scalar_difference_is_reported_with_path()
        {
            var a = new Box
            {
                X = 1,
                S = "a"
            };
            var b = new Box
            {
                X = 2,
                S = "a"
            };
            var diffs = StructuralComparer.Diff(a, b);
            Assert.Contains(diffs,
                d => d.Path.Contains('X', StringComparison.Ordinal) && d.Expected == "1" && d.Actual == "2");
        }

        [Fact]
        public void Collection_element_difference_is_reported_with_index()
        {
            var a = new Box
            {
                Xs = new List<int>
                {
                    1,
                    2,
                    3
                }
            };
            var b = new Box
            {
                Xs = new List<int>
                {
                    1,
                    9,
                    3
                }
            };
            var diffs = StructuralComparer.Diff(a, b);
            Assert.Contains(diffs, d => d.Path.Contains("Xs[1]", StringComparison.Ordinal));
        }

        /// <summary>
        ///     <b>Render's null arms: the one branch whose whole job is to name absence.</b>
        ///     <para>
        ///         <c>Render</c> writes <c>d.Expected ?? "&lt;null&gt;"</c> and <c>d.Actual ?? "&lt;null&gt;"</c>.
        ///         Every existing test compares two objects that both HAVE values, so the right-hand side of each
        ///         <c>??</c> was never evaluated — the round-29 testing mutation leg reported exactly those two
        ///         positions as its only NoCoverage mutants, and the rendered text is what a CONSUMER reads when
        ///         their own mapper fails a comparison.
        ///     </para>
        ///     <para>
        ///         A dump that printed an empty string where a value is missing would be actively misleading:
        ///         "expected , actual 5" reads like a formatting bug rather than like a null.
        ///     </para>
        /// </summary>
        [Fact]
        public void Render_names_a_null_on_either_side_rather_than_printing_nothing()
        {
            var expectedNull = StructuralComparer.Diff(new Box { S = null! }, new Box { S = "b" });
            var actualNull = StructuralComparer.Diff(new Box { S = "a" }, new Box { S = null! });

            var expectedText = StructuralComparer.Render(expectedNull);
            var actualText = StructuralComparer.Render(actualNull);

            Assert.Contains("expected <null>", expectedText, StringComparison.Ordinal);
            Assert.Contains("actual <null>", actualText, StringComparison.Ordinal);

            // And the non-null side still prints its value, so the arm did not swallow both.
            Assert.Contains("actual b", expectedText, StringComparison.Ordinal);
            Assert.Contains("expected a", actualText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The separators are part of the contract too: a reader parses "  path: expected X, actual Y", and
        ///     the mutation leg's surviving string mutants on this line are the ones that would quietly turn it
        ///     into something else. Asserted as a whole line rather than as fragments.
        /// </summary>
        [Fact]
        public void Render_writes_one_indented_line_per_diff_in_the_documented_shape()
        {
            var diffs = StructuralComparer.Diff(new Box { X = 1 }, new Box { X = 2 });

            var text = StructuralComparer.Render(diffs);

            Assert.Contains("  root.X: expected 1, actual 2" + (char)10, text, StringComparison.Ordinal);
        }

        [Fact]
        public void Render_produces_readable_lines()
        {
            var diffs = StructuralComparer.Diff(new Box
                {
                    X = 1
                },
                new Box
                {
                    X = 2
                });
            var text = StructuralComparer.Render(diffs);
            Assert.Contains("X", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Collection_count_difference_is_reported_at_the_collection_path()
        {
            var diffs = StructuralComparer.Diff(new Box
                {
                    Xs = new List<int>
                    {
                        1,
                        2,
                        3
                    }
                },
                new Box
                {
                    Xs = new List<int>
                    {
                        1,
                        2
                    }
                });

            var count = Assert.Single(diffs, d => string.Equals(d.Path, "root.Xs.Count", StringComparison.Ordinal));
            Assert.Equal("3", count.Expected);
            Assert.Equal("2", count.Actual);
        }

        [Fact]
        public void A_self_referencing_graph_terminates_at_the_depth_cap()
        {
            // The comparer keeps no visited set; a cycle is walked until MaxDepth and must not recurse forever.
            var a = new CycleNode { Id = 1 };
            a.Next = a;
            var b = new CycleNode { Id = 1 };
            b.Next = b;

            Assert.Empty(StructuralComparer.Diff(a, b));

            b.Id = 2;
            var diffs = StructuralComparer.Diff(a, b);
            Assert.Contains(diffs, d => string.Equals(d.Path, "root.Id", StringComparison.Ordinal));
            Assert.All(diffs, d => Assert.EndsWith(".Id", d.Path, StringComparison.Ordinal));
        }

        /// <summary>
        ///     A difference in a public FIELD is reported, with the same path shape a property difference gets.
        ///     The comparer walks fields in a loop of its own and no test had ever given it one to walk, so the
        ///     whole loop - its member filter, its recursion and the path it builds - was unmeasured.
        /// </summary>
        [Fact]
        public void A_public_field_difference_is_reported_with_its_path()
        {
            var diff = Assert.Single(StructuralComparer.Diff(new FieldBox { Value = 1 }, new FieldBox { Value = 2 }));

            Assert.Equal("root.Value", diff.Path);
            Assert.Equal("1", diff.Expected);
            Assert.Equal("2", diff.Actual);
        }

        /// <summary>
        ///     WHERE the depth cap falls, from both sides. A difference at the deepest level the comparer still
        ///     reaches is reported; the next level down is not looked at.
        ///     <para>
        ///         The existing cycle test proves the walk TERMINATES, which a cap one level out in either
        ///         direction would also do. This states the boundary itself: the root is compared at depth 0 and
        ///         each nesting level adds one, so a node's own members at chain position 11 are compared at
        ///         depth 12 - the last value the guard admits - and position 12 is the first the guard refuses.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_depth_cap_falls_between_the_last_compared_level_and_the_first_skipped_one()
        {
            var lastCompared = Assert.Single(StructuralComparer.Diff(Chain(14, -1), Chain(14, 11)));
            Assert.Equal("1", lastCompared.Expected);
            Assert.Equal("99", lastCompared.Actual);

            Assert.Empty(StructuralComparer.Diff(Chain(14, -1), Chain(14, 12)));
        }

        /// <summary>
        ///     The double tolerance is EXCLUSIVE: two values exactly one epsilon apart are a difference, and
        ///     anything closer is not. A tolerance that admitted its own boundary would be a different contract,
        ///     and nothing said which one this is.
        /// </summary>
        [Fact]
        public void Two_doubles_exactly_one_epsilon_apart_are_a_difference()
        {
            Assert.Single(StructuralComparer.Diff(new NumericBox { D = 0 }, new NumericBox { D = 1e-9 }));
            Assert.Empty(StructuralComparer.Diff(new NumericBox { D = 0 }, new NumericBox { D = 9e-10 }));
        }

        /// <summary>The same contract for float, whose tolerance is its own constant.</summary>
        [Fact]
        public void Two_floats_exactly_one_epsilon_apart_are_a_difference()
        {
            Assert.Single(StructuralComparer.Diff(new NumericBox { F = 0f }, new NumericBox { F = 1e-6f }));
            Assert.Empty(StructuralComparer.Diff(new NumericBox { F = 0f }, new NumericBox { F = 9e-7f }));
        }

        /// <summary>
        ///     A differing STRING is one difference, not one per character. A string is a scalar here AND an
        ///     IEnumerable, so the scalar arm has to stop the walk: without that, the comparer would report the
        ///     string, then walk it as a character sequence and report every character that differs - and, for
        ///     strings of different lengths, a .Count difference as well.
        /// </summary>
        [Fact]
        public void A_differing_string_is_one_difference_and_not_one_per_character()
        {
            var one = Assert.Single(StructuralComparer.Diff(new Box { S = "abc" }, new Box { S = "abd" }));

            Assert.Equal("root.S", one.Path);
            Assert.Equal("abc", one.Expected);
            Assert.Equal("abd", one.Actual);

            // Different lengths too, where the character walk would also report a count.
            var other = Assert.Single(StructuralComparer.Diff(new Box { S = "ab" }, new Box { S = "abcd" }));

            Assert.Equal("root.S", other.Path);
        }

        /// <summary>
        ///     The depth cap holds through COLLECTION edges too, and each collection costs two levels: one for
        ///     the property that holds it, one for the element inside. So the node at position 5 is still
        ///     compared and the one at position 6 is past the cap - a walk whose depth ran backwards would report
        ///     both.
        /// </summary>
        [Fact]
        public void The_depth_cap_holds_through_a_chain_of_collections()
        {
            var lastCompared = Assert.Single(StructuralComparer.Diff(ListChain(9, -1), ListChain(9, 5)));
            Assert.Equal("1", lastCompared.Expected);
            Assert.Equal("99", lastCompared.Actual);

            Assert.Empty(StructuralComparer.Diff(ListChain(9, -1), ListChain(9, 6)));
        }

        /// <summary>
        ///     ...and through FIELD edges, which the comparer walks in a loop of its own. One level per node
        ///     here, so the boundary sits where the property chain's does.
        /// </summary>
        [Fact]
        public void The_depth_cap_holds_through_a_chain_of_fields()
        {
            var lastCompared = Assert.Single(StructuralComparer.Diff(FieldChain(14, -1), FieldChain(14, 11)));
            Assert.Equal("1", lastCompared.Expected);
            Assert.Equal("99", lastCompared.Actual);

            Assert.Empty(StructuralComparer.Diff(FieldChain(14, -1), FieldChain(14, 12)));
        }

        /// <summary>A chain linked through a one-element collection; the node at <paramref name="differentAt" /> holds 99.</summary>
        private static ListNode ListChain(int length, int differentAt)
        {
            var root = new ListNode { Id = differentAt == 0 ? 99 : 1 };
            var current = root;
            for (var i = 1; i < length; i++)
            {
                var next = new ListNode { Id = differentAt == i ? 99 : 1 };
                current.Kids.Add(next);
                current = next;
            }

            return root;
        }

        /// <summary>A chain linked through a public FIELD; the node at <paramref name="differentAt" /> holds 99.</summary>
        private static FieldBox FieldChain(int length, int differentAt)
        {
            var root = new FieldBox { Value = differentAt == 0 ? 99 : 1 };
            var current = root;
            for (var i = 1; i < length; i++)
            {
                var next = new FieldBox { Value = differentAt == i ? 99 : 1 };
                current.Next = next;
                current = next;
            }

            return root;
        }

        /// <summary>A chain of <paramref name="length" /> nodes; the one at <paramref name="differentAt" /> holds 99.</summary>
        private static CycleNode Chain(int length, int differentAt)
        {
            var root = new CycleNode { Id = differentAt == 0 ? 99 : 1 };
            var current = root;
            for (var i = 1; i < length; i++)
            {
                var next = new CycleNode { Id = differentAt == i ? 99 : 1 };
                current.Next = next;
                current = next;
            }

            return root;
        }

        [Fact]
        public void Render_rejects_null()
        {
            Assert.Equal("diffs",
                Assert.Throws<ArgumentNullException>(() => StructuralComparer.Render(null!)).ParamName);
        }
    }
}
