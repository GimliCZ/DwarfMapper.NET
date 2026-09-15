// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;

namespace DwarfMapper.Testing.Tests
{
    /// <summary>
    ///     Meta-tests for the ORACLE itself (ISSUE-030). The oracle used to sort any collection whose first element
    ///     was scalar, applying set semantics to ordered <c>List&lt;T&gt;</c>/<c>T[]</c> too — so it could not tell a
    ///     list from a hash set, and a generator regression that REORDERED a scalar list during mapping still
    ///     compared equal. Every fuzz/property suite that trusts this oracle was therefore blind to list-order bugs:
    ///     "all tests pass" did not imply "scalar-list order is preserved". These pin both halves of the contract.
    /// </summary>
    public class GraphOracleOrderingTests
    {
        private static readonly string[] Ab =
        {
            "a", "b"
        };

        private static readonly string[] Ba =
        {
            "b", "a"
        };

        private static Holder<T> Wrap<T>(T value)
        {
            return new Holder<T>
            {
                Val = value
            };
        }

        [Fact]
        public void Oracle_detects_a_reordered_scalar_list()
        {
            // A List<int> is ORDERED: reordering is a real difference and must be reported.
            Assert.False(GraphOracleComparer.ValueEqual(
                Wrap(new List<int>
                {
                    1,
                    2,
                    3
                }),
                Wrap(new List<int>
                {
                    3,
                    2,
                    1
                })));
        }

        [Fact]
        public void Oracle_detects_a_reordered_scalar_array()
        {
            Assert.False(GraphOracleComparer.ValueEqual(Wrap(Ab), Wrap(Ba)));
        }

        [Fact]
        public void Oracle_still_accepts_an_identical_scalar_list()
        {
            Assert.True(GraphOracleComparer.ValueEqual(
                Wrap(new List<int>
                {
                    1,
                    2,
                    3
                }),
                Wrap(new List<int>
                {
                    1,
                    2,
                    3
                })));
        }

        [Fact]
        public void Oracle_treats_a_hashset_as_unordered()
        {
            // A set's iteration order is genuinely unspecified — enumeration order is not a difference.
            Assert.True(GraphOracleComparer.ValueEqual(
                Wrap(new HashSet<int>
                {
                    1,
                    2,
                    3
                }),
                Wrap(new HashSet<int>
                {
                    3,
                    2,
                    1
                })));
        }

        [Fact]
        public void Oracle_treats_an_immutable_set_as_unordered()
        {
            Assert.True(GraphOracleComparer.ValueEqual(
                Wrap(ImmutableHashSet.Create(1, 2, 3)),
                Wrap(ImmutableHashSet.Create(3, 2, 1))));
        }

        [Fact]
        public void Oracle_still_detects_a_genuinely_different_set()
        {
            Assert.False(GraphOracleComparer.ValueEqual(
                Wrap(new HashSet<int>
                {
                    1,
                    2,
                    3
                }),
                Wrap(new HashSet<int>
                {
                    1,
                    2,
                    4
                })));
        }

        [Fact]
        public void Cross_type_set_to_list_compares_order_insensitively()
        {
            // Mapping HashSet<int> -> List<int>: the SOURCE order is unspecified, so requiring positional equality
            // would be meaningless. If either side is unordered, both are sorted before comparing.
            var diffs = GraphOracleComparer.CrossTypeDiff(
                new HashSet<int>
                {
                    3,
                    1,
                    2
                },
                new List<int>
                {
                    1,
                    2,
                    3
                },
                typeof(HashSet<int>),
                typeof(List<int>));
            Assert.Empty(diffs);
        }

        /// <summary>
        ///     An unordered collection is sorted when its FIRST element is scalar, but the elements after it can be
        ///     anything a consumer put there. The sort key has to survive all of them:
        ///     <list type="bullet">
        ///         <item>a <c>null</c>, which sorts first;</item>
        ///         <item>a float and a double, which are keyed by round-trip text;</item>
        ///         <item>an <c>IFormattable</c> scalar, and a string, which is not <c>IFormattable</c>;</item>
        ///         <item>an object whose <c>ToString</c> returns null;</item>
        ///         <item>objects whose <c>ToString</c> throws <c>InvalidOperationException</c> or
        ///         <c>ArgumentException</c>, which compare as equal rather than taking the whole comparison down.</item>
        ///     </list>
        ///     The set is compared against itself, so the pinned outcome is "no diffs, no exception".
        /// </summary>
        [Fact]
        public void Sorting_an_unordered_set_tolerates_whatever_follows_its_scalar_first_element()
        {
            // Two null-text elements side by side, so the sort compares one against the other and the
            // null-text key is read on BOTH sides of a comparison.
            var set = new HashSet<object?>
            {
                1,
                new NullText(),
                new NullText(),
                null,
                1.5f,
                2.5,
                "text",
                new ThrowsInvalidOperationOnToString(),
                new ThrowsArgumentOnToString()
            };

            Assert.Empty(GraphOracleComparer.ValueDiff(Wrap(set), Wrap(set)));
        }

        private sealed class Holder<T>
        {
            public T? Val { get; set; }
        }

        private sealed class NullText
        {
            public override string? ToString()
            {
                return null;
            }
        }

        // CA1065: throwing from ToString is exactly the consumer shape the oracle's sort-key catch exists for.
        // MA0015: ToString has no parameter to name; the fixture only needs an ArgumentException to escape it.
#pragma warning disable CA1065, MA0015
        private sealed class ThrowsInvalidOperationOnToString
        {
            public override string ToString()
            {
                throw new InvalidOperationException("no text for this value");
            }
        }

        private sealed class ThrowsArgumentOnToString
        {
            public override string ToString()
            {
                throw new ArgumentException("no text for this value");
            }
        }
#pragma warning restore CA1065, MA0015
    }
}
