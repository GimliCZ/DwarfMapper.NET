// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// A mapped collection must never ALIAS the source collection.
//
// The risk is specific to this generator's design: when the element type needs no transform (int -> int)
// and the destination member's type is ASSIGNABLE from the source's (List<int> -> IEnumerable<int>), the
// tempting "optimization" is to assign the reference straight across. That is exactly the bug AutoMapper
// shipped in v3.1 (AssignableCollectionBug): mutating the destination silently corrupted the source, because
// they were the same object.
//
// The existing allocation-invariant tests cannot see this — they assert that value-type maps allocate
// NOTHING, whereas aliasing is the opposite failure (it allocates nothing precisely BECAUSE it reused the
// source instance). So this needs its own assertion: not-same, plus a mutation that must not propagate.

public sealed class AliasSrc
{
    public List<int> Items { get; set; } = new();
}

public sealed class AliasDst
{
    public List<int> Items { get; set; } = new();
}

// The assignable case: List<int> source -> IEnumerable<int> destination. Directly assignable, so this is
// the shape most likely to be aliased.
public sealed class AliasAssignableSrc
{
    public List<int> Items { get; set; } = new();
}

public sealed class AliasAssignableDst
{
    public IEnumerable<int> Items { get; set; } = Array.Empty<int>();
}

// Dictionary, SAME value type on both sides — the shape a mapper is most tempted to alias, because the member
// types are identical and the entries need no transform. This is exactly what the benchmark exposed in a
// competitor: an identical-type dictionary member returned BY REFERENCE, copying nothing. A fresh container is
// the only safe behavior; mutating the mapped dictionary must never reach back into the source.
public sealed class DictAliasSrc
{
    public Dictionary<string, int> M { get; set; } = new();
}

public sealed class DictAliasDst
{
    public Dictionary<string, int> M { get; set; } = new();
}

// HashSet, same element type — same aliasing temptation as the dictionary.
public sealed class SetAliasSrc
{
    public HashSet<int> V { get; set; } = new();
}

public sealed class SetAliasDst
{
    public HashSet<int> V { get; set; } = new();
}

// Nested collection whose ELEMENTS are themselves mapped (different element types, so a real per-element map
// runs). The container being fresh is not enough: each destination element must be a NEW mapped object, so
// mutating a destination element cannot reach the corresponding source element. A regression that reference-
// copied the elements would leave a fresh list full of the SOURCE's own objects.
public sealed class ElemSrc
{
    public int Id { get; set; }
}

public sealed class ElemDst
{
    public int Id { get; set; }
}

public sealed class NestedListSrc
{
    public List<ElemSrc> Items { get; set; } = new();
}

public sealed class NestedListDst
{
    public List<ElemDst> Items { get; set; } = new();
}

[DwarfMapper]
[GenerateMap<AliasSrc, AliasDst>]
public partial class AliasMapper;

[DwarfMapper]
[GenerateMap<AliasAssignableSrc, AliasAssignableDst>]
public partial class AliasAssignableMapper;

[DwarfMapper]
[GenerateMap<DictAliasSrc, DictAliasDst>]
public partial class DictAliasMapper;

[DwarfMapper]
[GenerateMap<SetAliasSrc, SetAliasDst>]
public partial class SetAliasMapper;

[DwarfMapper]
[GenerateMap<ElemSrc, ElemDst>]
[GenerateMap<NestedListSrc, NestedListDst>]
public partial class NestedListAliasMapper;

public class CollectionAliasingRuntimeTests
{
    [Fact]
    public void Mapped_list_is_a_new_instance_not_the_source_list()
    {
        var src = new AliasSrc { Items = { 1, 2, 3 } };

        var dst = new AliasMapper().Map(src);

        Assert.NotSame(src.Items, dst.Items);
        Assert.Equal(new[] { 1, 2, 3 }, dst.Items);
    }

    [Fact]
    public void Mutating_the_mapped_list_does_not_corrupt_the_source()
    {
        var src = new AliasSrc { Items = { 1, 2, 3 } };

        var dst = new AliasMapper().Map(src);
        dst.Items.Add(4);
        dst.Items[0] = 99;

        // The source must be untouched — this is the assertion that actually catches aliasing.
        Assert.Equal(new[] { 1, 2, 3 }, src.Items);
    }

    [Fact]
    public void Assignable_collection_target_is_still_copied_not_aliased()
    {
        // List<int> -> IEnumerable<int>: assignable, therefore the most tempting to alias.
        var src = new AliasAssignableSrc { Items = { 1, 2, 3 } };

        var dst = new AliasAssignableMapper().Map(src);

        Assert.NotSame(src.Items, dst.Items);
        Assert.Equal(new[] { 1, 2, 3 }, dst.Items);

        // Mutating the SOURCE after mapping must not change the already-mapped destination.
        src.Items.Add(4);
        Assert.Equal(new[] { 1, 2, 3 }, dst.Items);
    }

    // ── Dictionary: the benchmark's motivating case ────────────────────────────────────────────────────
    [Fact]
    public void Mapped_dictionary_is_a_new_instance_not_the_source_dictionary()
    {
        var src = new DictAliasSrc { M = { ["a"] = 1, ["b"] = 2 } };

        var dst = new DictAliasMapper().Map(src);

        Assert.NotSame(src.M, dst.M);
        Assert.Equal(1, dst.M["a"]);
        Assert.Equal(2, dst.M["b"]);
    }

    [Fact]
    public void Mutating_the_mapped_dictionary_does_not_corrupt_the_source()
    {
        var src = new DictAliasSrc { M = { ["a"] = 1, ["b"] = 2 } };

        var dst = new DictAliasMapper().Map(src);
        dst.M["a"] = 99;
        dst.M["c"] = 3;
        dst.M.Remove("b");

        // The source dictionary must be exactly as it was. This is the assertion that catches an aliased dict.
        Assert.Equal(1, src.M["a"]);
        Assert.True(src.M.ContainsKey("b"));
        Assert.False(src.M.ContainsKey("c"));
        Assert.Equal(2, src.M.Count);
    }

    // ── HashSet ────────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Mapped_hashset_is_a_new_instance_and_mutation_independent()
    {
        var src = new SetAliasSrc { V = { 1, 2, 3 } };

        var dst = new SetAliasMapper().Map(src);
        Assert.NotSame(src.V, dst.V);

        dst.V.Add(4);
        dst.V.Remove(1);

        // Source set untouched.
        Assert.Equal(new[] { 1, 2, 3 }, src.V.OrderBy(x => x));
    }

    // ── Nested collection: the per-element map produces independent destination objects ─────────────────
    // Note on scope: ElemSrc and ElemDst are DIFFERENT types, so the element cannot be reference-shared even in
    // principle (a type error) — this guards that the per-element map actually RUNS and copies the value, and
    // that the destination object is independent. The reference-aliasing risk lives in SAME-type element lists,
    // which the generator deliberately reference-copies (see IndependenceOracle's scope note); this test does
    // not contradict that contract, it covers the mapped-element path instead.
    [Fact]
    public void Mapped_element_objects_are_fresh_not_the_source_elements()
    {
        var srcElem = new ElemSrc { Id = 7 };
        var src = new NestedListSrc { Items = { srcElem } };

        var dst = new NestedListAliasMapper().Map(src);

        // The list is fresh AND its element is a distinct object (a mapped ElemDst, not the ElemSrc).
        Assert.NotSame(src.Items, dst.Items);
        Assert.Equal(7, dst.Items[0].Id);

        // Mutating the destination element must not reach the source element.
        dst.Items[0].Id = 99;
        Assert.Equal(7, srcElem.Id);
    }
}
