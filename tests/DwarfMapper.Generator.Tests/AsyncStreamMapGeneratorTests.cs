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
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // async iterator shape.
        Assert.Contains("async partial", generated, StringComparison.Ordinal);
        Assert.Contains("await foreach (var __item in src)", generated, StringComparison.Ordinal);
        Assert.Contains("yield return", generated, StringComparison.Ordinal);
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
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("yield return __item;", generated, StringComparison.Ordinal); // implicit int→long
    }
}
