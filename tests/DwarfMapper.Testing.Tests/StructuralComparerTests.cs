// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Testing.Tests
{
    public class Box
    {
        public int X { get; set; }

        public string S { get; set; } = "";

        public List<int> Xs { get; set; } = new();
    }

    public class StructuralComparerTests
    {
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
