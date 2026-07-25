// SPDX-License-Identifier: GPL-2.0-only
using System.Collections;
using System.Collections.Generic;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

// A user collection type that only implements IEnumerable<T> (e.g. a hand-rolled ConcurrentList<T>):
// no ICollection<T>, no indexer, no Count. DwarfMapper must still accept it as a collection source.
public sealed class Bag<T> : IEnumerable<T>
{
    private readonly List<T> _items = new();
    public void Add(T item) => _items.Add(item);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class BagItem { public int V { get; set; } }
public class BagItemDto { public int V { get; set; } }

public class BagSrc { public Bag<int> Items { get; set; } = new(); }
public class BagDst { public List<int> Items { get; set; } = new(); }

[DwarfMapper]
[GenerateMap<BagSrc, BagDst>]               // member-level custom-enumerable source
[GenerateMap<Bag<int>, List<int>>]          // top-level custom-enumerable source (scalar element)
[GenerateMap<Bag<BagItem>, List<BagItemDto>>] // top-level custom-enumerable source (object element)
public partial class CustomEnumMapper { }

public class CustomEnumerableRuntimeTests
{
    [Fact]
    public void Member_level_custom_enumerable_source_maps()
    {
        var src = new BagSrc { Items = new Bag<int> { 1, 2, 3 } };
        var dst = new CustomEnumMapper().Map(src);
        Assert.Equal(new[] { 1, 2, 3 }, dst.Items);
    }

    [Fact]
    public void Top_level_custom_enumerable_source_maps_scalar_elements()
    {
        var bag = new Bag<int> { 4, 5 };
        List<int> result = new CustomEnumMapper().Map(bag);
        Assert.Equal(new[] { 4, 5 }, result);
    }

    [Fact]
    public void Top_level_custom_enumerable_source_maps_object_elements()
    {
        var bag = new Bag<BagItem> { new() { V = 7 }, new() { V = 8 } };
        List<BagItemDto> result = new CustomEnumMapper().Map(bag);
        Assert.Equal(2, result.Count);
        Assert.Equal(7, result[0].V);
        Assert.Equal(8, result[1].V);
    }
}
