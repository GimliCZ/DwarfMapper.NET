// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

public struct SrcVec
{
    public float X;
    public float Y;
    public float Z;
}

public struct DstVec
{
    public float X;
    public float Y;
    public float Z;
}

public class BlitSrc
{
    public SrcVec[] Points { get; set; } = Array.Empty<SrcVec>();
}

public class BlitDst
{
    public DstVec[] Points { get; set; } = Array.Empty<DstVec>();
}

[DwarfMapper]
public partial class BlitMapper
{
    public partial BlitDst Map(BlitSrc s);
}

public struct SrcTransform
{
    public SrcVec Pos;
    public int Id;
}

public struct DstTransform
{
    public DstVec Pos;
    public int Id;
}

public class NestedBlitSrc
{
    public SrcTransform[] Items { get; set; } = Array.Empty<SrcTransform>();
}

public class NestedBlitDst
{
    public DstTransform[] Items { get; set; } = Array.Empty<DstTransform>();
}

[DwarfMapper]
public partial class NestedBlitMapper
{
    public partial NestedBlitDst Map(NestedBlitSrc s);
}

public class BlitRuntimeTests
{
    [Fact]
    public void Nested_struct_blit_copies_values()
    {
        var dst = new NestedBlitMapper().Map(new NestedBlitSrc
        {
            Items = new[] { new SrcTransform { Pos = new SrcVec { X = 1f, Y = 2f, Z = 3f }, Id = 7 } }
        });
        Assert.Single(dst.Items);
        Assert.Equal(1f, dst.Items[0].Pos.X);
        Assert.Equal(3f, dst.Items[0].Pos.Z);
        Assert.Equal(7, dst.Items[0].Id);
    }

    [Fact]
    public void Reinterpret_blit_copies_values()
    {
        var dst = new BlitMapper().Map(new BlitSrc
        {
            Points = new[]
            {
                new SrcVec { X = 1f, Y = 2f, Z = 3f },
                new SrcVec { X = 4f, Y = 5f, Z = 6f }
            }
        });

        Assert.Equal(2, dst.Points.Length);
        Assert.Equal(1f, dst.Points[0].X);
        Assert.Equal(3f, dst.Points[0].Z);
        Assert.Equal(4f, dst.Points[1].X);
        Assert.Equal(6f, dst.Points[1].Z);
    }
}
