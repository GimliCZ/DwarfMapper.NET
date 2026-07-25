// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests;

// ISSUE-025 — an async-stream map with no CancellationToken cannot be cancelled at all: a token the consumer
// passes to WithCancellation on the RESULT only reaches the iterator if the generated method declares an
// [EnumeratorCancellation] parameter. These prove the token actually flows end-to-end.

public class CancelSrc
{
    public int Id { get; set; }
}

public class CancelDst
{
    public int Id { get; set; }
}

[DwarfMapper]
public partial class CancellableStreamMapper
{
    public partial IAsyncEnumerable<CancelDst> Map(IAsyncEnumerable<CancelSrc> src, CancellationToken ct);
}

public class AsyncStreamCancellationRuntimeTests
{
    // An INFINITE source: if the token were not honoured the enumeration would never end, so this test can only
    // pass when cancellation genuinely reaches the generated iterator.
    private static async IAsyncEnumerable<CancelSrc> Infinite([EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; ; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return new CancelSrc { Id = i };
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Cancelling_the_consumer_stops_the_generated_stream()
    {
        using var cts = new CancellationTokenSource();
        var mapper = new CancellableStreamMapper();
        var seen = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mapper.Map(Infinite(cts.Token), cts.Token).WithCancellation(cts.Token))
            {
                seen++;
                if (seen == 3) await cts.CancelAsync().ConfigureAwait(false);
            }
        });

        Assert.Equal(3, seen);
    }

    [Fact]
    public async Task An_uncancelled_stream_maps_every_element()
    {
        var mapper = new CancellableStreamMapper();
        var results = new List<int>();

        await foreach (var d in mapper.Map(Finite(), CancellationToken.None))
            results.Add(d.Id);

        Assert.Equal(new[] { 0, 1, 2 }, results);
    }

    private static async IAsyncEnumerable<CancelSrc> Finite()
    {
        for (var i = 0; i < 3; i++)
        {
            yield return new CancelSrc { Id = i };
            await Task.Yield();
        }
    }
}
