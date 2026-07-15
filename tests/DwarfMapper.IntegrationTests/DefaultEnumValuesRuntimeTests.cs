// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// Every option-enum DEFAULT value, exercised explicitly at run time. Their behaviour is hit implicitly by every
// mapper that doesn't override them, but nothing NAMED them in an integration test, so the runtime enum-value
// gate (RuntimeCoverageScanTests.R3) had no real reference. These declare each default and assert what it does.

public enum DevColor { Red = 1, Green = 2 }
public enum DevColorDto { Red = 1, Green = 2 }

public sealed class DevEnumSrc { public DevColor C { get; set; } }
public sealed class DevEnumDst { public DevColorDto C { get; set; } }

public sealed class DevNullSrc { public int? V { get; set; } }
public sealed class DevNullDst { public int V { get; set; } }

public sealed class DevNode { public int V { get; set; } public DevNode? Next { get; set; } }
public sealed class DevNodeDto { public int V { get; set; } public DevNodeDto? Next { get; set; } }

public sealed class DevPlainSrc { public int A { get; set; } public string B { get; set; } = ""; }
public sealed class DevPlainDst { public int A { get; set; } public string B { get; set; } = ""; }

[DwarfMapper(EnumStrategy = EnumStrategy.ByName)]
public partial class DevEnumMapper { public partial DevEnumDst Map(DevEnumSrc s); }

[DwarfMapper(NullStrategy = NullStrategy.Throw)]
public partial class DevNullMapper { public partial DevNullDst Map(DevNullSrc s); }

[DwarfMapper(OnCycle = OnCycleStrategy.Throw, ReferenceHandling = ReferenceHandlingStrategy.None, MaxDepth = 16)]
public partial class DevCycleMapper { public partial DevNodeDto Map(DevNode n); }

// NameConvention.Exact + ReferenceHandlingStrategy.None + RequiredMappingStrategy.Target — all the "plain
// mapping" defaults, named on one mapper that just maps correctly.
[DwarfMapper(NameConvention = NameConvention.Exact,
    ReferenceHandling = ReferenceHandlingStrategy.None,
    RequiredMapping = RequiredMappingStrategy.Target)]
public partial class DevPlainMapper { public partial DevPlainDst Map(DevPlainSrc s); }

public class DefaultEnumValuesRuntimeTests
{
    [Fact]
    public void EnumStrategy_ByName_maps_by_member_name()
    {
        var dto = new DevEnumMapper().Map(new DevEnumSrc { C = DevColor.Green });
        Assert.Equal(DevColorDto.Green, dto.C);
    }

    [Fact]
    public void NullStrategy_Throw_throws_on_a_null_value_source()
    {
        // int? -> int under the default NullStrategy.Throw: a null source throws rather than defaulting.
        Assert.ThrowsAny<Exception>(() => new DevNullMapper().Map(new DevNullSrc { V = null }));

        // A non-null value maps straight through.
        Assert.Equal(7, new DevNullMapper().Map(new DevNullSrc { V = 7 }).V);
    }

    [Fact]
    public void OnCycleStrategy_Throw_throws_a_catchable_depth_exception_on_a_cycle()
    {
        var node = new DevNode { V = 1 };
        node.Next = node; // self-cycle

        // Under ReferenceHandling.None + OnCycle.Throw (the defaults), a cycle is a catchable exception at
        // MaxDepth — never an uncatchable StackOverflow.
        Assert.Throws<DwarfMappingDepthException>(() => new DevCycleMapper().Map(node));
    }

    [Fact]
    public void The_plain_defaults_map_correctly()
    {
        var dto = new DevPlainMapper().Map(new DevPlainSrc { A = 3, B = "x" });
        Assert.Equal(3, dto.A);
        Assert.Equal("x", dto.B);
    }
}
