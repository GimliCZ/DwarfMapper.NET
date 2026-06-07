// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public enum SrcColor { Red, Green, Blue }
public enum DstColor { Red, Green, Blue }

public class EnumSrc { public SrcColor Color { get; set; } public SrcColor Code { get; set; } public string Name { get; set; } = ""; }
public class EnumDst { public DstColor Color { get; set; } public int Code { get; set; } public DstColor Name { get; set; } }

[DwarfMapper]
public partial class EnumMapper
{
    public partial EnumDst Map(EnumSrc s);  // enum->enum (by name), enum->int, string->enum
}

public class EnumRuntimeTests
{
    [Fact]
    public void Enum_conversions_run()
    {
        var d = new EnumMapper().Map(new EnumSrc { Color = SrcColor.Green, Code = SrcColor.Blue, Name = "Red" });
        Assert.Equal(DstColor.Green, d.Color);
        Assert.Equal(2, d.Code);            // Blue == 2 via enum->int
        Assert.Equal(DstColor.Red, d.Name); // "Red" via string->enum
    }
}
