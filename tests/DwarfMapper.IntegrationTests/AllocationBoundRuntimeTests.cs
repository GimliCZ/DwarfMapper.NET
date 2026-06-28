// SPDX-License-Identifier: GPL-2.0-only
using System;
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public struct AllocPointSrc { public int X { get; set; } public int Y { get; set; } public long Z { get; set; } }
public struct AllocPointDst { public int X { get; set; } public int Y { get; set; } public long Z { get; set; } }

[DwarfMapper]
[GenerateMap<AllocPointSrc, AllocPointDst>]
public partial class AllocStructMapper { }

/// <summary>
/// IMPROVEMENT-PLAN item 16 (allocation half): a value-type (struct) map must allocate ZERO heap bytes — no
/// boxing, no hidden capture, no temporary. Measured with GC.GetAllocatedBytesForCurrentThread() (per-thread,
/// so unaffected by other parallel tests); the test class is in its own collection to avoid sharing the
/// measuring thread with another test in the same collection. Complements the static AOT-token meta-test
/// (ReflectionFreeMetaTests) with a dynamic zero-allocation guarantee.
/// </summary>
[Collection("allocation-isolated")]
public class AllocationBoundRuntimeTests
{
    [Fact]
    public void Blittable_struct_map_allocates_zero_bytes()
    {
        var mapper = new AllocStructMapper();
        var src = new AllocPointSrc { X = 1, Y = 2, Z = 3 };

        // Warm up: force JIT of Map + this method so the measured window is steady-state.
        AllocPointDst sink = default;
        for (var i = 0; i < 200; i++) sink = mapper.Map(src);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            sink = mapper.Map(src);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Keep the result observable so the JIT cannot elide the loop.
        Assert.Equal(3, sink.Z);
        Assert.Equal(0, allocated);
    }
}

[CollectionDefinition("allocation-isolated", DisableParallelization = true)]
public sealed class AllocationIsolatedCollection { }
