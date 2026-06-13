// SPDX-License-Identifier: GPL-2.0-only
using System;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// None+Throw mode: a self-referential type reached through a collection/dictionary edge must thread
/// the shared depth-guard context into the (re-synthesized) element mapper, so cyclic/deep data throws
/// a catchable DwarfMappingDepthException instead of a silent StackOverflow. Non-recursive collections
/// must stay zero-overhead (no ctx).
/// </summary>
public class NoneModeCollectionDepthGeneratorTests
{
    [Fact]
    public void None_mode_self_referential_list_threads_ctx_and_uses_companion()
    {
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Tree    { public int V { get; set; } public List<Tree>? Kids { get; set; } }
            public class TreeDto { public int V { get; set; } public List<TreeDto>? Kids { get; set; } }
            [DwarfMapper]
            public partial class M { public partial TreeDto Map(Tree t); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // The collection helper must now take (ctx, depth) and route through the depth companion.
        Assert.Contains("DwarfRefContext ctx, int depth", generated, StringComparison.Ordinal);
        Assert.Contains("__DwarfMap_Depth_Map", generated, StringComparison.Ordinal);
        // None mode → depth guard present, but NO identity map / on-stack guard.
        Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetReference", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void None_mode_non_recursive_object_list_stays_zero_overhead()
    {
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Addr    { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class Person    { public List<Addr>? Addrs { get; set; } }
            public class PersonDto { public List<AddrDto>? Addrs { get; set; } }
            [DwarfMapper]
            public partial class M { public partial PersonDto Map(Person p); public partial AddrDto Map(Addr a); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Non-recursive element → no ctx threading anywhere (zero overhead preserved).
        Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
    }
}
