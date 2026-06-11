// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

public class C1Node { public int V { get; set; } public C1Node? Next { get; set; } }
public class C1NodeDto { public int V { get; set; } public C1NodeDto? Next { get; set; } }

[DwarfMapper(MaxDepth = 8)]
public partial class C1DepthMapper { public partial C1NodeDto Map(C1Node n); }

public class DepthSafetyRuntimeTests
{
    private static C1Node Chain(int n)
    {
        C1Node? head = null;
        for (var i = 0; i < n; i++) head = new C1Node { V = i, Next = head };
        return head!;
    }

    [Fact]
    public void Within_cap_maps()
    {
        var dst = new C1DepthMapper().Map(Chain(5));
        Assert.Equal(4, dst.V);
    }

    [Fact]
    public void Beyond_cap_throws_catchable_depth_exception_not_stackoverflow()
    {
        var ex = Record.Exception(() => new C1DepthMapper().Map(Chain(50)));
        Assert.NotNull(ex);
        Assert.IsType<DwarfMappingDepthException>(ex);
    }

    [Fact]
    public void Cyclic_data_throws_depth_exception_not_stackoverflow()
    {
        var a = new C1Node { V = 1 };
        var b = new C1Node { V = 2 };
        a.Next = b; b.Next = a; // cycle
        var ex = Record.Exception(() => new C1DepthMapper().Map(a));
        Assert.IsType<DwarfMappingDepthException>(ex);
    }
}
