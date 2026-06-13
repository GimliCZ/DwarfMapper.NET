// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Generator.Collections;

// Coverage suite for DwarfMapper.Generator.Collections.EquatableArray<T>.
// Techniques: unit, adversary, deterministic, defensive, fuzzy (seeded), fixture.
namespace DwarfMapper.Generator.Tests.Coverage;

public class EquatableArrayCoverageTests
{
    // ─── Construction: From(array) / From(IEnumerable) ────────────────────────

    [Fact]
    public void From_array_wraps_elements()
    {
        var arr = new[] { 1, 2, 3 };
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
        var a = new EquatableArray<int>(new[] { 1, 2, 3 });
        var b = new EquatableArray<int>(new[] { 1, 2, 3 });
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Not_equal_when_different_element()
    {
        var a = new EquatableArray<int>(new[] { 1, 2, 3 });
        var b = new EquatableArray<int>(new[] { 1, 2, 9 });
        Assert.False(a.Equals(b));
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Not_equal_when_different_length()
    {
        var a = new EquatableArray<int>(new[] { 1, 2, 3 });
        var b = new EquatableArray<int>(new[] { 1, 2 });
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Not_equal_when_reordered()
    {
        var a = new EquatableArray<int>(new[] { 1, 2, 3 });
        var b = new EquatableArray<int>(new[] { 3, 2, 1 });
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
    public void Not_equal_when_one_null_one_empty()
    {
        var nullBacked  = new EquatableArray<int>(null);
        var emptyBacked = new EquatableArray<int>(Array.Empty<int>());
        // null array vs empty array: both Count==0 but Equals follows the null-guard branch.
        // if (_array is null || other._array is null) → return _array is null && other._array is null
        // → false (one is null, other is empty array).
        Assert.False(nullBacked.Equals(emptyBacked));
    }

    [Fact]
    public void Not_equal_when_one_null_one_nonempty()
    {
        var a = new EquatableArray<int>(null);
        var b = new EquatableArray<int>(new[] { 1 });
        Assert.False(a.Equals(b));
        Assert.False(b.Equals(a));
    }

    // ─── Equals(object?) ──────────────────────────────────────────────────────

    [Fact]
    public void Equals_object_with_boxed_equal_returns_true()
    {
        var a = new EquatableArray<int>(new[] { 7 });
        object b = new EquatableArray<int>(new[] { 7 });
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_object_with_null_returns_false()
    {
        var a = new EquatableArray<int>(new[] { 1 });
        Assert.False(a.Equals((object?)null));
    }

    [Fact]
    public void Equals_object_with_wrong_type_returns_false()
    {
        var a = new EquatableArray<int>(new[] { 1 });
        Assert.False(a.Equals("not an EquatableArray"));
    }

    // ─── GetHashCode ──────────────────────────────────────────────────────────

    [Fact]
    public void GetHashCode_same_elements_produces_same_hash()
    {
        var a = new EquatableArray<int>(new[] { 5, 6, 7 });
        var b = new EquatableArray<int>(new[] { 5, 6, 7 });
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_null_backed_returns_zero()
    {
        var a = new EquatableArray<int>(null);
        Assert.Equal(0, a.GetHashCode());
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
        var a = new EquatableArray<int>(new[] { 1, 2, 3 });
        var b = new EquatableArray<int>(new[] { 4, 5, 6 });
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_deterministic_across_calls()
    {
        var a = EquatableArray.From(new[] { "alpha", "beta", "gamma" });
        var h1 = a.GetHashCode();
        var h2 = a.GetHashCode();
        Assert.Equal(h1, h2);
    }

    // ─── Enumeration ──────────────────────────────────────────────────────────

    [Fact]
    public void Foreach_enumerates_all_elements()
    {
        var ea = new EquatableArray<int>(new[] { 10, 20, 30 });
        var collected = new List<int>();
        foreach (var x in ea)
            collected.Add(x);
        Assert.Equal(new[] { 10, 20, 30 }, collected);
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
        var ea = new EquatableArray<int>(new[] { 1, 2 });
        // Exercise the IEnumerable.GetEnumerator() path
        System.Collections.IEnumerable nonGeneric = ea;
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
    public void Default_struct_GetHashCode_returns_zero()
    {
        var d = default(EquatableArray<int>);
        Assert.Equal(0, d.GetHashCode());
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
        var a = EquatableArray.From(new[] { "x", "y", "z" });
        var b = EquatableArray.From(new[] { "x", "y", "z" });
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Elements_with_different_strings_not_equal()
    {
        var a = EquatableArray.From(new[] { "x", "y" });
        var b = EquatableArray.From(new[] { "x", "Z" });
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void GetHashCode_with_zero_value_elements_is_deterministic()
    {
        // GetHashCode uses (item?.GetHashCode() ?? 0), so 0-value ints exercise the zero path.
        var a = new EquatableArray<int>(new[] { 0, 1, 0 });
        var b = new EquatableArray<int>(new[] { 0, 1, 0 });
        // Same elements → same hash code (deterministic, no crash).
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ─── Adversary: pathological sizes ───────────────────────────────────────

    [Fact]
    public void Single_element_equality()
    {
        var a = new EquatableArray<int>(new[] { 42 });
        var b = new EquatableArray<int>(new[] { 42 });
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
        var a = new EquatableArray<int>(new[] { 1, 1, 1 });
        var b = new EquatableArray<int>(new[] { 1, 1 });
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
        var a = EquatableArray.From(new[] { "alpha", "beta", "gamma" });
        var b = EquatableArray.From(new[] { "alpha", "beta", "gamma" });
        Assert.True(a.Equals(b));
        Assert.Equal(3, a.Count);
        Assert.Equal("beta", a[1]);
    }

    [Fact]
    public void Fixture_operator_symmetry()
    {
        var a = new EquatableArray<int>(new[] { 100 });
        var b = new EquatableArray<int>(new[] { 200 });
        Assert.True(a != b);
        Assert.False(a == b);
    }
}
