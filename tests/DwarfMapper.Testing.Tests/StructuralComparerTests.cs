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

        [Fact]
        public void Render_rejects_null()
        {
            Assert.Equal("diffs",
                Assert.Throws<ArgumentNullException>(() => StructuralComparer.Render(null!)).ParamName);
        }
    }
}
