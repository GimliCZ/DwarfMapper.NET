// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using DwarfMapper.Generator.Collections;

// Coverage suite for DwarfMapper.Generator.Collections.EquatableArray<T>.
// Techniques: unit, adversary, deterministic, defensive, fuzzy (seeded), fixture.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class EquatableArrayCoverageTests
    {
        // ─── Construction: From(array) / From(IEnumerable) ────────────────────────

        [Fact]
        public void From_array_wraps_elements()
        {
            var arr = new[]
            {
                1, 2, 3
            };
            var ea = new EquatableArray<int>(arr);
            Assert.Equal(3, ea.Count);
            Assert.Equal(1, ea[0]);
            Assert.Equal(2, ea[1]);
            Assert.Equal(3, ea[2]);
        }

        [Fact]
        public void From_IEnumerable_wraps_elements()
        {
            var seq = Enumerable.Range(10, 4); // 10, 11, 12, 13
            var ea = EquatableArray.From(seq);
            Assert.Equal(4, ea.Count);
            Assert.Equal(10, ea[0]);
            Assert.Equal(13, ea[3]);
        }

        [Fact]
        public void From_empty_array_yields_count_zero()
        {
            var ea = new EquatableArray<int>(Array.Empty<int>());
            Assert.Equal(0, ea.Count);
        }

        [Fact]
        public void From_null_array_yields_count_zero()
        {
            var ea = new EquatableArray<int>(null);
            Assert.Equal(0, ea.Count);
        }

        // ─── Equality: same elements → true; differences → false ─────────────────

        [Fact]
        public void Equal_when_same_elements_in_same_order()
        {
            var a = new EquatableArray<int>(new[]
            {
                1, 2, 3
            });
            var b = new EquatableArray<int>(new[]
            {
                1, 2, 3
            });
            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
        }

        [Fact]
        public void Not_equal_when_different_element()
        {
            var a = new EquatableArray<int>(new[]
            {
                1, 2, 3
            });
            var b = new EquatableArray<int>(new[]
            {
                1, 2, 9
            });
            Assert.False(a.Equals(b));
            Assert.False(a == b);
            Assert.True(a != b);
        }

        [Fact]
        public void Not_equal_when_different_length()
        {
            var a = new EquatableArray<int>(new[]
            {
                1, 2, 3
            });
            var b = new EquatableArray<int>(new[]
            {
                1, 2
            });
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Not_equal_when_reordered()
        {
            var a = new EquatableArray<int>(new[]
            {
                1, 2, 3
            });
            var b = new EquatableArray<int>(new[]
            {
                3, 2, 1
            });
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equal_when_both_null_backed()
        {
            var a = new EquatableArray<int>(null);
            var b = new EquatableArray<int>(null);
            Assert.True(a.Equals(b));
            Assert.True(a == b);
        }

        [Fact]
        public void Equal_when_one_null_one_empty()
        {
            var nullBacked = new EquatableArray<int>(null);
            var emptyBacked = new EquatableArray<int>(Array.Empty<int>());

            // ISSUE-029. This test previously asserted the OPPOSITE — it pinned the defect. A null-backed and an
            // empty-backed array are both "no items", i.e. the same VALUE; declaring them unequal made two
            // structurally identical generator models compare unequal and cost a spurious incremental-cache miss.
            Assert.True(nullBacked.Equals(emptyBacked));
            Assert.Equal(nullBacked.GetHashCode(), emptyBacked.GetHashCode());
        }

        [Fact]
        public void Not_equal_when_one_null_one_nonempty()
        {
            var a = new EquatableArray<int>(null);
            var b = new EquatableArray<int>(new[]
            {
                1
            });
            Assert.False(a.Equals(b));
            Assert.False(b.Equals(a));
        }

        // ─── Equals(object?) ──────────────────────────────────────────────────────

        [Fact]
        public void Equals_object_with_boxed_equal_returns_true()
        {
            var a = new EquatableArray<int>(new[]
            {
                7
            });
            object b = new EquatableArray<int>(new[]
            {
                7
            });
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_object_with_null_returns_false()
        {
            var a = new EquatableArray<int>(new[]
            {
                1
            });
            Assert.False(a.Equals(null));
        }

        [Fact]
        public void Equals_object_with_wrong_type_returns_false()
        {
            var a = new EquatableArray<int>(new[]
            {
                1
            });
            object notAnArray = "not an EquatableArray";
            Assert.False(a.Equals(notAnArray));
        }

        // ─── GetHashCode ──────────────────────────────────────────────────────────

        [Fact]
        public void GetHashCode_same_elements_produces_same_hash()
        {
            var a = new EquatableArray<int>(new[]
            {
                5, 6, 7
            });
            var b = new EquatableArray<int>(new[]
            {
                5, 6, 7
            });
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GetHashCode_null_backed_matches_empty()
        {
            // ISSUE-029: third test that pinned the old `null -> 0` hash. Null-backed and empty are one value now.
            var a = new EquatableArray<int>(null);
            Assert.Equal(new EquatableArray<int>(Array.Empty<int>()).GetHashCode(), a.GetHashCode());
        }

        [Fact]
        public void GetHashCode_empty_array_backed_is_stable()
        {
            var a = new EquatableArray<int>(Array.Empty<int>());
            var h1 = a.GetHashCode();
            var h2 = a.GetHashCode();
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void GetHashCode_different_elements_typically_differ()
        {
            // For these specific small values the hash function must produce different results.
            var a = new EquatableArray<int>(new[]
            {
                1, 2, 3
            });
            var b = new EquatableArray<int>(new[]
            {
                4, 5, 6
            });
            Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
        }

        /// <summary>
        ///     The fold has to carry the accumulator FORWARD, not shrink it.
        ///     <para>
        ///         Every other assertion in this section states the equals/hashcode contract — equal arrays hash
        ///         alike — and a fold that divides the accumulator instead of multiplying it satisfies that
        ///         contract perfectly while collapsing the hash onto the array's LAST element (an accumulator
        ///         below the multiplier truncates to zero on every step). That is not a contract violation, it
        ///         is a quality collapse, and quality is the whole reason this type exists:
        ///         <c>EquatableArray</c> backs incremental-generator model caching, so every model whose last
        ///         member agrees would land in one bucket and the cache lookups it exists to make cheap would
        ///         degrade to a linear scan through structurally different models.
        ///     </para>
        ///     <para>
        ///         Two statements of the same property, neither of which pins a literal hash value (that would
        ///         pin the implementation rather than the behaviour): a leading element must reach the result,
        ///         and a small exhaustive family of distinct arrays must not collide.
        ///     </para>
        /// </summary>
        [Fact]
        public void GetHashCode_mixes_every_element_not_only_the_last()
        {
            // Differ ONLY in the first element: a hash that forgets its prefix reports these as one value.
            Assert.NotEqual(
                new EquatableArray<int>(new[]
                {
                    1, 9
                }).GetHashCode(),
                new EquatableArray<int>(new[]
                {
                    4, 9
                }).GetHashCode());

            // Same shape across a length boundary: a leading element must not be free to appear or vanish.
            Assert.NotEqual(
                new EquatableArray<int>(new[]
                {
                    3
                }).GetHashCode(),
                new EquatableArray<int>(new[]
                {
                    0, 3
                }).GetHashCode());

            // And exhaustively over a small square: 64 distinct arrays, 64 distinct hash codes. A fold that
            // keeps only the tail produces 8.
            var hashes = new HashSet<int>();
            var arrays = 0;
            for (var i = 0; i < 8; i++)
            for (var j = 0; j < 8; j++)
            {
                arrays++;
                hashes.Add(new EquatableArray<int>(new[]
                {
                    i, j
                }).GetHashCode());
            }

            Assert.True(hashes.Count == arrays,
                $"only {hashes.Count} distinct hash codes for {arrays} distinct two-element arrays — the fold is " + "dropping earlier elements, which is exactly what makes a hash useless as an incremental-cache key");
        }

        [Fact]
        public void GetHashCode_deterministic_across_calls()
        {
            var a = EquatableArray.From(new[]
            {
                "alpha", "beta", "gamma"
            });
            var h1 = a.GetHashCode();
            var h2 = a.GetHashCode();
            Assert.Equal(h1, h2);
        }

        // ─── Enumeration ──────────────────────────────────────────────────────────

        [Fact]
        public void Foreach_enumerates_all_elements()
        {
            var ea = new EquatableArray<int>(new[]
            {
                10, 20, 30
            });
            var collected = new List<int>();
            foreach (var x in ea)
                collected.Add(x);
            Assert.Equal(new[]
                {
                    10, 20, 30
                },
                collected);
        }

        [Fact]
        public void Foreach_on_null_backed_enumerates_nothing()
        {
            var ea = new EquatableArray<int>(null);
            var collected = new List<int>();
            foreach (var x in ea)
                collected.Add(x);
            Assert.Empty(collected);
        }

        [Fact]
        public void GetEnumerator_nongeneric_enumerates_elements()
        {
            var ea = new EquatableArray<int>(new[]
            {
                1, 2
            });
            // Exercise the IEnumerable.GetEnumerator() path
            IEnumerable nonGeneric = ea;
            var list = new List<object>();
            foreach (var x in nonGeneric)
                list.Add(x);
            Assert.Equal(2, list.Count);
            Assert.Equal(1, (int)list[0]);
            Assert.Equal(2, (int)list[1]);
        }

        // ─── Default struct value ─────────────────────────────────────────────────

        [Fact]
        public void Default_struct_has_count_zero()
        {
            var d = default(EquatableArray<int>);
            Assert.Equal(0, d.Count);
        }

        [Fact]
        public void Default_struct_enumerates_nothing()
        {
            var d = default(EquatableArray<int>);
            Assert.Empty(d);
        }

        [Fact]
        public void Default_struct_hashes_like_an_empty_array()
        {
            // Also previously pinned the defect (`Assert.Equal(0, …)`). Equals now treats default and empty as one
            // value, so GetHashCode MUST agree or the equals/hashcode contract is broken.
            var d = default(EquatableArray<int>);
            Assert.Equal(new EquatableArray<int>(Array.Empty<int>()).GetHashCode(), d.GetHashCode());
        }

        [Fact]
        public void Two_default_structs_are_equal()
        {
            var a = default(EquatableArray<int>);
            var b = default(EquatableArray<int>);
            Assert.True(a.Equals(b));
            Assert.True(a == b);
        }

        // ─── Elements containing nulls (use string which satisfies IEquatable<string>) ────

        [Fact]
        public void Elements_with_same_strings_compare_correctly()
        {
            // Use non-nullable string (satisfies IEquatable<string> constraint).
            var a = EquatableArray.From(new[]
            {
                "x", "y", "z"
            });
            var b = EquatableArray.From(new[]
            {
                "x", "y", "z"
            });
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Elements_with_different_strings_not_equal()
        {
            var a = EquatableArray.From(new[]
            {
                "x", "y"
            });
            var b = EquatableArray.From(new[]
            {
                "x", "Z"
            });
            Assert.False(a.Equals(b));
        }

        [Fact]
        public void GetHashCode_with_zero_value_elements_is_deterministic()
        {
            // GetHashCode uses (item?.GetHashCode() ?? 0), so 0-value ints exercise the zero path.
            var a = new EquatableArray<int>(new[]
            {
                0, 1, 0
            });
            var b = new EquatableArray<int>(new[]
            {
                0, 1, 0
            });
            // Same elements → same hash code (deterministic, no crash).
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        // ─── Adversary: pathological sizes ───────────────────────────────────────

        [Fact]
        public void Single_element_equality()
        {
            var a = new EquatableArray<int>(new[]
            {
                42
            });
            var b = new EquatableArray<int>(new[]
            {
                42
            });
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Large_array_equality()
        {
            var data = Enumerable.Range(0, 1000).ToArray();
            var a = new EquatableArray<int>(data);
            var b = new EquatableArray<int>(data.ToArray()); // distinct array, same values
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Duplicate_elements_counted_separately()
        {
            var a = new EquatableArray<int>(new[]
            {
                1, 1, 1
            });
            var b = new EquatableArray<int>(new[]
            {
                1, 1
            });
            Assert.False(a.Equals(b));
        }

        // ─── Seeded fuzzy: random small arrays always agree equality ↔ hashcode ───

        [Fact]
        public void Fuzz_seeded_equal_implies_same_hashcode()
        {
            var rng = new Random(20240101);
            for (var trial = 0; trial < 200; trial++)
            {
                var len = rng.Next(0, 8);
                var arr = Enumerable.Range(0, len).Select(_ => rng.Next(0, 5)).ToArray();
                var a = new EquatableArray<int>(arr);
                var b = new EquatableArray<int>(arr.ToArray());
                // Structural equality must hold
                Assert.True(a.Equals(b), $"Trial {trial}: equal arrays reported not equal");
                // Equal → same hash code
                Assert.Equal(a.GetHashCode(), b.GetHashCode());
            }
        }

        [Fact]
        public void Fuzz_seeded_different_arrays_not_equal()
        {
            var rng = new Random(20240202);
            for (var trial = 0; trial < 100; trial++)
            {
                var len = rng.Next(1, 6);
                var arr1 = Enumerable.Range(0, len).Select(_ => rng.Next(0, 3)).ToArray();
                var arr2 = arr1.ToArray();
                arr2[rng.Next(0, len)] += 10; // guaranteed different
                var a = new EquatableArray<int>(arr1);
                var b = new EquatableArray<int>(arr2);
                Assert.False(a.Equals(b), $"Trial {trial}: known-different arrays reported equal");
            }
        }

        // ─── Fixture: known golden cases ─────────────────────────────────────────

        [Fact]
        public void Fixture_string_sequence_exact_match()
        {
            var a = EquatableArray.From(new[]
            {
                "alpha", "beta", "gamma"
            });
            var b = EquatableArray.From(new[]
            {
                "alpha", "beta", "gamma"
            });
            Assert.True(a.Equals(b));
            Assert.Equal(3, a.Count);
            Assert.Equal("beta", a[1]);
        }

        [Fact]
        public void Fixture_operator_symmetry()
        {
            var a = new EquatableArray<int>(new[]
            {
                100
            });
            var b = new EquatableArray<int>(new[]
            {
                200
            });
            Assert.True(a != b);
            Assert.False(a == b);
        }
        /// <summary>
        ///     The hash is the documented fold, ELEMENT BY ELEMENT: start at 17 and, for each item,
        ///     <c>hash * 31 + item</c>. Stated as an absolute value because nothing weaker can hold it - two
        ///     different arrays hashing differently is true of a great many wrong formulas, including one that
        ///     SUBTRACTS each element, so an inequality test says nothing about the mixing.
        ///     <para>
        ///         Element type int on purpose: int.GetHashCode() is the value itself, while string hashing is
        ///         randomised per process and would make an absolute expectation flaky by design.
        ///     </para>
        /// </summary>
        [Fact]
        public void The_hash_folds_each_element_into_the_documented_accumulator()
        {
            // 17 -> 17*31 + 1 = 528 -> 528*31 + 2 = 16370
            Assert.Equal(16370,
                new EquatableArray<int>(new[]
                {
                    1,
                    2
                }).GetHashCode());

            // Order is part of the value: the same elements the other way round fold differently.
            // 17 -> 17*31 + 2 = 529 -> 529*31 + 1 = 16400
            Assert.Equal(16400,
                new EquatableArray<int>(new[]
                {
                    2,
                    1
                }).GetHashCode());

            // The empty fold is the seed, and a default-backed array must agree with it - the equals/hashcode
            // contract this type had broken once before.
            Assert.Equal(17, new EquatableArray<int>(Array.Empty<int>()).GetHashCode());
            Assert.Equal(17, new EquatableArray<int>(null!).GetHashCode());
        }

    }
}
