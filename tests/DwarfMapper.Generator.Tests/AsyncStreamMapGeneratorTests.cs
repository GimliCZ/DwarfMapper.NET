// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Async streaming map: IAsyncEnumerable&lt;D&gt; Map(IAsyncEnumerable&lt;S&gt; src) — emitted as an async
///     iterator that lazily transforms the source sequence element-by-element.
/// </summary>
public class AsyncStreamMapGeneratorTests
{
    [Fact]
    public void Async_stream_map_emits_async_iterator()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } public long Score { get; set; } }
                           public class D { public int Id { get; set; } public int Score { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // async iterator shape.
        Assert.Contains("async partial", generated, StringComparison.Ordinal);
        // ISSUE-025: library code must not capture the caller's synchronization context. This assertion used to
        // pin the bare `await foreach (var __item in src)` — i.e. it actively locked in the defect.
        Assert.Contains("ConfigureAwait(src, false)", generated, StringComparison.Ordinal);
        Assert.Contains("yield return", generated, StringComparison.Ordinal);
    }

    // ISSUE-025 — an IAsyncEnumerable map with no way to cancel is a real API defect, but the generated half
    // must match the user's partial signature exactly, so the token can only exist when the user declares it.
    // When they do, it must be threaded through WithCancellation AND carry [EnumeratorCancellation] — without
    // that attribute the token a consumer passes to WithCancellation on the RESULT never reaches this iterator.
    [Fact]
    public void Async_stream_map_honours_a_declared_CancellationToken()
    {
        const string src = """
                           using System.Collections.Generic;
                           using System.Threading;
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int Id { get; set; } }
                           public class D { public int Id { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src, CancellationToken ct); }
                           """;
        var (diags, generated) = GeneratorTestHarness.Run(src);

        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        GeneratorAssert.EmitsCompilableCode(src);
        Assert.Contains("EnumeratorCancellation", generated, StringComparison.Ordinal);
        Assert.Contains("WithCancellation(src, ct).ConfigureAwait(false)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Async_stream_scalar_element_is_direct()
    {
        const string src = """
                           using System.Collections.Generic;
                           using DwarfMapper;
                           namespace Demo;
                           [DwarfMapper]
                           public partial class M { public partial IAsyncEnumerable<long> Map(IAsyncEnumerable<int> src); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("yield return __item;", generated, StringComparison.Ordinal); // implicit int→long
    }
}
