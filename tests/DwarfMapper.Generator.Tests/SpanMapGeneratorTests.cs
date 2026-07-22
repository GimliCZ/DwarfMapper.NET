// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Zero-alloc span mapping: void Map(ReadOnlySpan&lt;S&gt; src, Span&lt;D&gt; dst). Element-wise map into a
///     caller buffer with a defensive length guard; element conversion reuses the resolution pipeline.
/// </summary>
public class SpanMapGeneratorTests
{
    [Fact]
    public void Scalar_span_map_emits_length_guard_and_element_loop()
    {
        const string src = """
                           using System;
                           using DwarfMapper;
                           namespace Demo;
                           [DwarfMapper]
                           public partial class M { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // Defensive length guard (no silent truncation).
        Assert.Contains("dst.Length < src.Length", generated, StringComparison.Ordinal);
        Assert.Contains("ArgumentException", generated, StringComparison.Ordinal);
        // Element loop, direct (implicit int→long) assignment.
        Assert.Contains("for (int __i = 0; __i < src.Length; __i++)", generated, StringComparison.Ordinal);
        Assert.Contains("dst[__i] = src[__i];", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Struct_span_map_routes_elements_through_nested_mapper()
    {
        const string src = """
                           using System;
                           using DwarfMapper;
                           namespace Demo;
                           public struct P { public int X; }
                           public struct Q { public long X; }
                           [DwarfMapper]
                           public partial class M { public partial void Map(ReadOnlySpan<P> src, Span<Q> dst); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // Element goes through a synthesized nested mapper applied to src[__i].
        Assert.Contains("dst[__i] = __DwarfMap_Obj_", generated, StringComparison.Ordinal);
        Assert.Contains("(src[__i])", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlySpan_source_and_writable_Span_dest_required()
    {
        // A ReadOnlySpan destination is not a valid map target (can't write) → falls through to the
        // normal invalid-method path (DWARF003), not treated as a span map.
        const string src = """
                           using System;
                           using DwarfMapper;
                           namespace Demo;
                           [DwarfMapper]
                           public partial class M { public partial void Map(ReadOnlySpan<int> src, ReadOnlySpan<long> dst); }
                           """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Contains(diags, d => d.Id == "DWARF003");
    }
}
