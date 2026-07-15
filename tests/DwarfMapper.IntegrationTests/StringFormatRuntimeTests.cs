// SPDX-License-Identifier: GPL-2.0-only
#nullable enable
using System;
using Xunit;

namespace DwarfMapper.IntegrationTests;

// [MapProperty(StringFormat = "...")] — format a value into a string with an explicit .NET format, always
// with InvariantCulture (stable across deployments/threads, matching the library's culture stance).

public sealed class SfSrc
{
    public DateTime When { get; set; }
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public sealed class SfDst
{
    public string When { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Count { get; set; } = "";
}

[DwarfMapper]
public partial class StringFormatMapper
{
    [MapProperty(nameof(SfSrc.When), nameof(SfDst.When), StringFormat = "yyyy-MM-dd")]
    [MapProperty(nameof(SfSrc.Amount), nameof(SfDst.Amount), StringFormat = "F2")]
    [MapProperty(nameof(SfSrc.Count), nameof(SfDst.Count), StringFormat = "N0")]
    public partial SfDst Map(SfSrc s);
}

public class StringFormatRuntimeTests
{
    [Fact]
    public void Each_format_is_applied_with_invariant_culture()
    {
        var src = new SfSrc
        {
            When = new DateTime(2026, 7, 15),
            Amount = 1234.5m,
            Count = 1_000_000,
        };

        var dto = new StringFormatMapper().Map(src);

        Assert.Equal("2026-07-15", dto.When);
        Assert.Equal("1234.50", dto.Amount);  // F2, invariant '.' decimal separator
        Assert.Equal("1,000,000", dto.Count);  // N0, invariant ',' group separator
    }
}
