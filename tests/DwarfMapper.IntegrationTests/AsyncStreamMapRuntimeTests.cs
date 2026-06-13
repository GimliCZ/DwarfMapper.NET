// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Threading.Tasks;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Async streaming map: IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src) lazily transforms an async
// sequence element-by-element (await foreach … yield return), preserving streaming/back-pressure.

public class AsSrc { public int Id { get; set; } public long Score { get; set; } }
public class AsDst { public int Id { get; set; } public int Score { get; set; } }

[DwarfMapper]
public partial class AsyncStreamMapper
{
    public partial IAsyncEnumerable<AsDst> Map(IAsyncEnumerable<AsSrc> src);
}

public class AsyncStreamMapRuntimeTests
{
    private static async IAsyncEnumerable<AsSrc> Source(params AsSrc[] items)
    {
        foreach (var i in items)
        {
            await Task.Yield();
            yield return i;
        }
    }

    [Fact]
    public async Task Streams_and_maps_each_element()
    {
        var src = Source(
            new AsSrc { Id = 1, Score = 10L },
            new AsSrc { Id = 2, Score = 20L },
            new AsSrc { Id = 3, Score = 30L });

        var result = new List<AsDst>();
        await foreach (var d in new AsyncStreamMapper().Map(src))
            result.Add(d);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.Equal(10, result[0].Score); // long → int CreateChecked
        Assert.Equal(3, result[2].Id);
        Assert.Equal(30, result[2].Score);
    }

    [Fact]
    public async Task Empty_async_sequence_yields_nothing()
    {
        var result = new List<AsDst>();
        await foreach (var d in new AsyncStreamMapper().Map(Source()))
            result.Add(d);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Is_lazy_streaming_not_buffered()
    {
        // If the mapper buffered the whole sequence eagerly, taking 1 element would still pull all.
        // We assert we can stop early after the first element (the source tracks how many it produced).
        var produced = 0;
        async IAsyncEnumerable<AsSrc> Counting()
        {
            for (var i = 0; i < 100; i++)
            {
                produced++;
                await Task.Yield();
                yield return new AsSrc { Id = i, Score = i };
            }
        }

        await foreach (var d in new AsyncStreamMapper().Map(Counting()))
        {
            Assert.Equal(0, d.Id);
            break; // stop after first
        }

        Assert.True(produced < 100, $"streaming should not pull all elements (produced={produced})");
    }
}
