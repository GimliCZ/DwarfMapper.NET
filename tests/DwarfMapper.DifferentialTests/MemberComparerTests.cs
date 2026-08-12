// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DifferentialTests;

/// <summary>
///     The comparer is load-bearing infrastructure: every claim this project makes passes through it, and a
///     comparer that misses a difference makes the whole harness a very convincing way of proving nothing.
/// </summary>
public class MemberComparerTests
{
    private sealed class Box
    {
        public string Name { get; set; } = "";

        public IReadOnlyList<int> Items { get; set; } = [];

        public Dictionary<string, int> Counts { get; set; } = [];

        public Box? Inner { get; set; }
    }

    private sealed class OtherBox
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Identical_objects_agree()
    {
        Assert.Empty(MemberComparer.Differences(
            new Box { Name = "a", Items = [1, 2] },
            new Box { Name = "a", Items = [1, 2] }));
    }

    [Fact]
    public void A_differing_scalar_is_reported_with_its_path()
    {
        var differences = MemberComparer.Differences(
            new Box { Name = "a" }, new Box { Name = "b" });

        Assert.Single(differences);
        Assert.Contains("<root>.Name", differences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_difference_deep_in_the_graph_is_reported_with_the_full_path()
    {
        // The reason the comparer is reflective rather than an Assert.Equal: the path IS the diagnosis.
        var differences = MemberComparer.Differences(
            new Box { Inner = new Box { Inner = new Box { Name = "deep" } } },
            new Box { Inner = new Box { Inner = new Box { Name = "wrong" } } });

        Assert.Contains(differences, d => d.Contains("<root>.Inner.Inner.Name", StringComparison.Ordinal));
    }

    [Fact]
    public void A_List_and_an_array_holding_the_same_values_agree()
    {
        // The policy: a member declared IReadOnlyList<T> that one mapper materialises as List<T> and another
        // as T[] holds the same data, and a consumer reading through the declared type cannot tell. Real DTOs
        // use interface-typed collections constantly, so without this the harness would cry wolf on its first
        // harvested shape.
        Assert.Empty(MemberComparer.Differences(
            new Box { Items = new List<int> { 1, 2, 3 } },
            new Box { Items = new[] { 1, 2, 3 } }));
    }

    [Fact]
    public void A_List_and_an_array_holding_DIFFERENT_values_still_disagree()
    {
        // The other half — the concession above must not have become "collections are never compared".
        var differences = MemberComparer.Differences(
            new Box { Items = new List<int> { 1, 2, 3 } },
            new Box { Items = new[] { 1, 2, 4 } });

        Assert.Contains(differences, d => d.Contains("<root>.Items[2]", StringComparison.Ordinal));
    }

    [Fact]
    public void Collections_of_different_length_disagree()
    {
        var differences = MemberComparer.Differences(
            new Box { Items = [1, 2] }, new Box { Items = [1] });

        Assert.Contains(differences, d => d.Contains("2 items vs 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Dictionaries_whose_KEYS_differ_only_in_case_disagree()
    {
        // The false-agreement hole this test exists to keep closed: looking a key up asks the ACTUAL
        // dictionary's own comparer, so an OrdinalIgnoreCase target reports "A" as present when what it holds
        // is "a" — and the two mappers would agree on a dictionary whose keys are not the same.
        var caseSensitive = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 };
        var caseInsensitive = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1 };

        var differences = MemberComparer.Differences(
            new Box { Counts = caseSensitive }, new Box { Counts = caseInsensitive });

        Assert.Contains(differences, d => d.Contains("keys", StringComparison.Ordinal));
    }

    [Fact]
    public void Dictionaries_whose_VALUES_differ_disagree()
    {
        var differences = MemberComparer.Differences(
            new Box { Counts = new Dictionary<string, int> { ["a"] = 1 } },
            new Box { Counts = new Dictionary<string, int> { ["a"] = 2 } });

        Assert.Contains(differences, d => d.Contains("<root>.Counts[a]", StringComparison.Ordinal));
    }

    [Fact]
    public void Null_versus_a_value_is_reported()
    {
        var differences = MemberComparer.Differences(
            new Box { Inner = null }, new Box { Inner = new Box() });

        Assert.Contains(differences, d => d.Contains("<root>.Inner", StringComparison.Ordinal));
    }

    [Fact]
    public void A_polymorphic_result_that_came_back_as_the_wrong_type_is_reported()
    {
        // Comparing only the members they share would hide it, and "the derived element arrived as its base"
        // is the exact user-visible failure [MapDerivedType] exists for.
        var differences = MemberComparer.Differences(new Box { Name = "a" }, new OtherBox { Name = "a" });

        Assert.Contains(differences, d => d.Contains("runtime type", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_value_tuples_that_differ_are_reported()
    {
        // The comparer walked PROPERTIES only, and a ValueTuple has none — Item1/Item2 are public FIELDS. So
        // this method used to find nothing to compare in a tuple and return "they agree" for any two tuples
        // whatsoever. The value-tuple shape added alongside this test would have passed without ever
        // comparing anything, which is the hollow test this repository detects elsewhere.
        var differences = MemberComparer.Differences((1, "a"), (1, "b"));

        Assert.Contains(differences, d => d.Contains("Item2", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_equal_value_tuples_agree()
    {
        Assert.Empty(MemberComparer.Differences((1, "a"), (1, "a")));
    }

    [Fact]
    public void A_self_referencing_graph_terminates()
    {
        // Some real shapes are cyclic, and a comparer that hangs on one is worse than no comparer.
        var left = new Box { Name = "a" };
        left.Inner = left;
        var right = new Box { Name = "a" };
        right.Inner = right;

        Assert.Empty(MemberComparer.Differences(left, right));
    }
}
