// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper;

namespace DwarfMapper.IntegrationTests;

public struct SrcVec { public float X; public float Y; public float Z; }
public struct DstVec { public float X; public float Y; public float Z; }

public class BlitSrc { public SrcVec[] Points { get; set; } = System.Array.Empty<SrcVec>(); }
public class BlitDst { public DstVec[] Points { get; set; } = System.Array.Empty<DstVec>(); }

[DwarfMapper]
public partial class BlitMapper
{
    public partial BlitDst Map(BlitSrc s);
}

public class BlitRuntimeTests
{
    [Fact]
    public void Reinterpret_blit_copies_values()
    {
        var dst = new BlitMapper().Map(new BlitSrc
        {
            Points = new[]
            {
                new SrcVec { X = 1f, Y = 2f, Z = 3f },
                new SrcVec { X = 4f, Y = 5f, Z = 6f },
            },
        });

        Assert.Equal(2, dst.Points.Length);
        Assert.Equal(1f, dst.Points[0].X);
        Assert.Equal(3f, dst.Points[0].Z);
        Assert.Equal(4f, dst.Points[1].X);
        Assert.Equal(6f, dst.Points[1].Z);
    }
}
