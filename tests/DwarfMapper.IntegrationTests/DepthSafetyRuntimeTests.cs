// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using DwarfMapper;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// ── Domain types for depth-safety tests ─────────────────────────────────────

public class DepNode    { public int V { get; set; } public DepNode? Next { get; set; } }
public class DepNodeDto { public int V { get; set; } public DepNodeDto? Next { get; set; } }

// MaxDepth = 8: small cap so tests stay fast
[DwarfMapper(MaxDepth = 8)]
public partial class DepthNodeMapper
{
    public partial DepNodeDto Map(DepNode n);
}

// Default MaxDepth (64)
[DwarfMapper]
public partial class DepthNodeDefaultMapper
{
    public partial DepNodeDto Map(DepNode n);
}

// Mutual recursion: A ↔ B (both recursion-capable)
public class DepA { public int V { get; set; } public DepB? Child { get; set; } }
public class DepB { public int W { get; set; } public DepA? Parent { get; set; } }
public class DepADto { public int V { get; set; } public DepBDto? Child { get; set; } }
public class DepBDto { public int W { get; set; } public DepADto? Parent { get; set; } }

[DwarfMapper(MaxDepth = 4)]
public partial class DepMutualMapper
{
    public partial DepADto Map(DepA a);
}

// ── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// Plan 19 C1: depth-safety runtime tests.
/// All tests were written BEFORE the implementation.
/// </summary>
public class DepthSafetyRuntimeTests
{
    // ── 1. HEADLINE: deep chain → DwarfMappingDepthException, NOT StackOverflow ──
    [Fact]
    public void Deep_chain_over_MaxDepth_throws_DwarfMappingDepthException_not_StackOverflow()
    {
        // Build a chain of length MaxDepth + 2 (clearly over the cap of 8)
        var mapper = new DepthNodeMapper();
        var root = BuildChain(10); // 10 > MaxDepth(8)
        var ex = Assert.Throws<DwarfMappingDepthException>(() => mapper.Map(root));
        Assert.NotNull(ex);
        // Must be catchable (not SO)
        Assert.IsType<DwarfMappingDepthException>(ex);
    }

    // ── 2. Chain within MaxDepth maps successfully ────────────────────────────────
    [Fact]
    public void Chain_within_MaxDepth_maps_successfully()
    {
        var mapper = new DepthNodeMapper();
        var root = BuildChain(5); // 5 <= MaxDepth(8)
        var result = mapper.Map(root);
        Assert.NotNull(result);
        // Verify the chain mapped correctly
        var n = result;
        var count = 0;
        while (n != null) { count++; n = n.Next; }
        Assert.Equal(5, count);
    }

    // ── 3. Cyclic data → DwarfMappingDepthException (depth cap catches it) ────────
    [Fact]
    public void Cyclic_data_with_low_MaxDepth_throws_DwarfMappingDepthException()
    {
        // Build a 2-node cycle: a.Next = b, b.Next = a
        var mapper = new DepthNodeMapper(); // MaxDepth = 8
        var a = new DepNode { V = 1 };
        var b = new DepNode { V = 2 };
        a.Next = b;
        b.Next = a; // cycle!

        // The depth cap will trip before StackOverflow
        var ex = Assert.Throws<DwarfMappingDepthException>(() => mapper.Map(a));
        Assert.NotNull(ex);
    }

    // ── 4. Default MaxDepth is 64 ─────────────────────────────────────────────────
    [Fact]
    public void Default_MaxDepth_is_64_chain_of_63_maps_ok()
    {
        var mapper = new DepthNodeDefaultMapper();
        var root = BuildChain(63); // 63 <= default MaxDepth(64)
        var result = mapper.Map(root); // must not throw
        Assert.NotNull(result);
    }

    [Fact]
    public void Default_MaxDepth_is_64_chain_of_66_throws()
    {
        var mapper = new DepthNodeDefaultMapper();
        var root = BuildChain(66); // 66 > 64
        Assert.Throws<DwarfMappingDepthException>(() => mapper.Map(root));
    }

    // ── 5. DwarfMappingDepthException is catchable (not process-killing) ─────────
    [Fact]
    public void DwarfMappingDepthException_is_catchable_Exception()
    {
        var mapper = new DepthNodeMapper();
        var root = BuildChain(20);
        Exception? caught = null;
        try
        {
            mapper.Map(root);
        }
        catch (DwarfMappingDepthException ex)
        {
            caught = ex;
        }
        Assert.NotNull(caught);
        Assert.IsType<DwarfMappingDepthException>(caught);
    }

    // ── 6. Exception message mentions depth limit ─────────────────────────────────
    [Fact]
    public void DwarfMappingDepthException_message_mentions_limit()
    {
        var mapper = new DepthNodeMapper(); // MaxDepth = 8
        var root = BuildChain(15);
        var ex = Assert.Throws<DwarfMappingDepthException>(() => mapper.Map(root));
        Assert.NotNull(ex.Message);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        // Message should mention the limit (8) or the word "depth"
        Assert.True(
            ex.Message.Contains('8', StringComparison.Ordinal) ||
            ex.Message.Contains("depth", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("MaxDepth", StringComparison.OrdinalIgnoreCase),
            $"Message should reference the depth limit. Actual: {ex.Message}");
    }

    // ── 7. Mutual recursion: deep chain throws DwarfMappingDepthException ────────
    [Fact]
    public void Mutual_recursion_deep_throws_DwarfMappingDepthException()
    {
        var mapper = new DepMutualMapper(); // MaxDepth = 4
        // Build: a1.Child=b1, b1.Parent=a2, a2.Child=b2, b2.Parent=a3, ...
        var root = new DepA { V = 1 };
        var cur = root;
        for (var i = 0; i < 6; i++)
        {
            var b = new DepB { W = i };
            cur.Child = b;
            var a = new DepA { V = i + 10 };
            b.Parent = a;
            cur = a;
        }
        Assert.Throws<DwarfMappingDepthException>(() => mapper.Map(root));
    }

    // ── 8. Values are correct for shallow chain within MaxDepth ──────────────────
    [Fact]
    public void Values_correct_for_chain_within_MaxDepth()
    {
        var mapper = new DepthNodeMapper(); // MaxDepth = 8
        // Build chain: 1 → 2 → 3 (length 3, well within cap)
        var root = new DepNode
        {
            V = 1,
            Next = new DepNode
            {
                V = 2,
                Next = new DepNode { V = 3, Next = null }
            }
        };
        var result = mapper.Map(root);
        Assert.Equal(1, result.V);
        Assert.NotNull(result.Next);
        Assert.Equal(2, result.Next!.V);
        Assert.NotNull(result.Next.Next);
        Assert.Equal(3, result.Next.Next!.V);
        Assert.Null(result.Next.Next.Next);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static DepNode BuildChain(int length)
    {
        var root = new DepNode { V = 1 };
        var cur = root;
        for (var i = 2; i <= length; i++)
        {
            cur.Next = new DepNode { V = i };
            cur = cur.Next;
        }
        return root;
    }
}
