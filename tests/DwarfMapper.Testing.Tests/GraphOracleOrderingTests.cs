// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;

namespace DwarfMapper.Testing.Tests;

/// <summary>
///     Meta-tests for the ORACLE itself (ISSUE-030). The oracle used to sort any collection whose first element
///     was scalar, applying set semantics to ordered <c>List&lt;T&gt;</c>/<c>T[]</c> too — so it could not tell a
///     list from a hash set, and a generator regression that REORDERED a scalar list during mapping still
///     compared equal. Every fuzz/property suite that trusts this oracle was therefore blind to list-order bugs:
///     "all tests pass" did not imply "scalar-list order is preserved". These pin both halves of the contract.
/// </summary>
public class GraphOracleOrderingTests
{
    private sealed class Holder<T>
    {
        public T? Val { get; set; }
    }

    private static Holder<T> Wrap<T>(T value)
    {
        return new Holder<T> { Val = value };
    }

    [Fact]
    public void Oracle_detects_a_reordered_scalar_list()
    {
        // A List<int> is ORDERED: reordering is a real difference and must be reported.
        Assert.False(GraphOracleComparer.ValueEqual(
            Wrap(new List<int> { 1, 2, 3 }),
            Wrap(new List<int> { 3, 2, 1 })));
    }

    private static readonly string[] Ab = { "a", "b" };
    private static readonly string[] Ba = { "b", "a" };

    [Fact]
    public void Oracle_detects_a_reordered_scalar_array()
    {
        Assert.False(GraphOracleComparer.ValueEqual(Wrap(Ab), Wrap(Ba)));
    }

    [Fact]
    public void Oracle_still_accepts_an_identical_scalar_list()
    {
        Assert.True(GraphOracleComparer.ValueEqual(
            Wrap(new List<int> { 1, 2, 3 }),
            Wrap(new List<int> { 1, 2, 3 })));
    }

    [Fact]
    public void Oracle_treats_a_hashset_as_unordered()
    {
        // A set's iteration order is genuinely unspecified — enumeration order is not a difference.
        Assert.True(GraphOracleComparer.ValueEqual(
            Wrap(new HashSet<int> { 1, 2, 3 }),
            Wrap(new HashSet<int> { 3, 2, 1 })));
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
            Wrap(new HashSet<int> { 1, 2, 3 }),
            Wrap(new HashSet<int> { 1, 2, 4 })));
    }

    [Fact]
    public void Cross_type_set_to_list_compares_order_insensitively()
    {
        // Mapping HashSet<int> -> List<int>: the SOURCE order is unspecified, so requiring positional equality
        // would be meaningless. If either side is unordered, both are sorted before comparing.
        var diffs = GraphOracleComparer.CrossTypeDiff(
            new HashSet<int> { 3, 1, 2 }, new List<int> { 1, 2, 3 },
            typeof(HashSet<int>), typeof(List<int>));
        Assert.Empty(diffs);
    }
}
