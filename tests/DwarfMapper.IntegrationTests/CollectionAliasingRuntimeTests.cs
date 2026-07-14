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

[DwarfMapper]
[GenerateMap<AliasSrc, AliasDst>]
public partial class AliasMapper;

[DwarfMapper]
[GenerateMap<AliasAssignableSrc, AliasAssignableDst>]
public partial class AliasAssignableMapper;

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
}
