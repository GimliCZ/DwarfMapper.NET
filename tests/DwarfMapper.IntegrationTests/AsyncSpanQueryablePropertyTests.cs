// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public class Item19Src { public int Id { get; set; } public string Name { get; set; } = ""; public DayOfWeek Day { get; set; } }
public class Item19Dst { public int Id { get; set; } public string Name { get; set; } = ""; public DayOfWeek Day { get; set; } }

public struct Item19StructSrc { public int A { get; set; } public long B { get; set; } public double C { get; set; } }
public struct Item19StructDst { public int A { get; set; } public long B { get; set; } public double C { get; set; } }

[DwarfMapper]
[GenerateMap<Item19Src, Item19Dst>]
[GenerateMap<Item19StructSrc, Item19StructDst>]
public partial class Item19Mapper
{
    public partial IAsyncEnumerable<Item19Dst> MapStream(IAsyncEnumerable<Item19Src> src);
    public partial IQueryable<Item19Dst> Project(IQueryable<Item19Src> src);
    public partial void MapSpan(ReadOnlySpan<Item19StructSrc> src, Span<Item19StructDst> dst);
}

/// <summary>
/// IMPROVEMENT-PLAN item 19: property coverage (N random inputs) for async-stream, Span and IQueryable-
/// projection maps — fixed schemas (Span is a ref struct, so the shape cannot be fuzz-generated), random
/// DATA. Each transform must agree element-by-element with the per-element create map.
/// </summary>
public class AsyncSpanQueryablePropertyTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 30).Select(i => new object[] { i });

    private static List<Item19Src> RandomItems(int seed)
    {
        var rng = new Random(seed);
        var n = rng.Next(0, 25);
        var list = new List<Item19Src>(n);
        for (var i = 0; i < n; i++)
            list.Add(new Item19Src { Id = rng.Next(), Name = "n" + rng.Next(1000), Day = (DayOfWeek)rng.Next(0, 7) });
        return list;
    }

    private static async IAsyncEnumerable<Item19Src> ToAsync(List<Item19Src> items)
    {
        foreach (var x in items) { await Task.Yield(); yield return x; }
    }

    private static void AssertEqual(Item19Src s, Item19Dst d)
    {
        Assert.Equal(s.Id, d.Id);
        Assert.Equal(s.Name, d.Name);
        Assert.Equal(s.Day, d.Day);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Async_stream_map_transforms_each_element(int seed)
    {
        var items = RandomItems(seed);
        var mapper = new Item19Mapper();

        var result = new List<Item19Dst>();
        await foreach (var d in mapper.MapStream(ToAsync(items)))
            result.Add(d);

        Assert.Equal(items.Count, result.Count);
        for (var i = 0; i < items.Count; i++) AssertEqual(items[i], result[i]);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Queryable_projection_transforms_each_element(int seed)
    {
        var items = RandomItems(seed);
        var mapper = new Item19Mapper();

        var projected = mapper.Project(items.AsQueryable()).ToList();

        Assert.Equal(items.Count, projected.Count);
        for (var i = 0; i < items.Count; i++) AssertEqual(items[i], projected[i]);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Span_map_writes_each_element_into_caller_memory(int seed)
    {
        var rng = new Random(seed);
        var n = rng.Next(0, 25);
        var src = new Item19StructSrc[n];
        for (var i = 0; i < n; i++)
            src[i] = new Item19StructSrc { A = rng.Next(), B = rng.NextInt64(), C = rng.NextDouble() };
        var dst = new Item19StructDst[n];

        var mapper = new Item19Mapper();
        mapper.MapSpan(src, dst);

        for (var i = 0; i < n; i++)
        {
            Assert.Equal(src[i].A, dst[i].A);
            Assert.Equal(src[i].B, dst[i].B);
            Assert.Equal(src[i].C, dst[i].C);
        }
    }

    [Fact]
    public void Span_map_into_caller_memory_allocates_zero_bytes()
    {
        var src = new Item19StructSrc[64];
        for (var i = 0; i < src.Length; i++) src[i] = new Item19StructSrc { A = i, B = i * 1000L, C = i * 0.5 };
        var dst = new Item19StructDst[64];
        var mapper = new Item19Mapper();

        for (var i = 0; i < 100; i++) mapper.MapSpan(src, dst); // warm up

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) mapper.MapSpan(src, dst);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(64, dst.Length);
        Assert.Equal(0, allocated); // writes into caller-provided memory — no allocation
    }
}
