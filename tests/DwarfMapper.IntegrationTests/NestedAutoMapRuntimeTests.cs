// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// ── Domain types for nested auto-map tests ───────────────────────────────────

// 3-level chain
public class City3L
{
    public string Name { get; set; } = "";
}

public class Addr3L
{
    public City3L Location { get; set; } = new();
    public int Zip { get; set; }
}

public class Person3L
{
    public Addr3L Home { get; set; } = new();
    public string FullName { get; set; } = "";
}

public record City3LDto(string Name);

public record Addr3LDto(City3LDto Location, int Zip);

public record Person3LDto(Addr3LDto Home, string FullName);

[DwarfMapper]
public partial class Person3LMapper
{
    public partial Person3LDto Map(Person3L p);
}

// Nested type conversion at depth (long→int, string→Guid)
public class InnerConv
{
    public long Count { get; set; }
    public string Id { get; set; } = "";
}

public class OuterConv
{
    public InnerConv Sub { get; set; } = new();
}

public record InnerConvDto(int Count, Guid Id);

public record OuterConvDto(InnerConvDto Sub);

[DwarfMapper]
public partial class OuterConvMapper
{
    public partial OuterConvDto Map(OuterConv o);
}

// Dedup: two members of the same nested type → one synthesized method
public class PtNested
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class LineNested
{
    public PtNested A { get; set; } = new();
    public PtNested B { get; set; } = new();
}

public record PtNDto(int X, int Y);

public record LineNDto(PtNDto A, PtNDto B);

[DwarfMapper]
public partial class LineMapper
{
    public partial LineNDto Map(LineNested l);
}

// User-declared sub-mapper overrides synthesis
public class NestInner
{
    public string V { get; set; } = "";
}

public class NestOuter
{
    public NestInner Sub { get; set; } = new();
}

public class NestInnerDst
{
    public string V { get; set; } = "";
}

public class NestOuterDst
{
    public NestInnerDst Sub { get; set; } = new();
}

[DwarfMapper]
public partial class NestOverrideMapper
{
    public partial NestOuterDst Map(NestOuter o);
    public partial NestInnerDst MapInner(NestInner i); // explicit — must win over synthesis
}

// Recursive tree
public class NestTree
{
    public int V { get; set; }
    public List<NestTree> Children { get; set; } = new();
}

public class NestTreeDto
{
    public int V { get; set; }
    public List<NestTreeDto> Children { get; set; } = new();
}

[DwarfMapper]
public partial class NestTreeMapper
{
    public partial NestTreeDto Map(NestTree t);
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class NestedAutoMapRuntimeTests
{
    [Fact]
    public void ThreeLevel_nested_graph_maps_all_values()
    {
        var mapper = new Person3LMapper();
        var src = new Person3L
        {
            FullName = "Gimli",
            Home = new Addr3L
            {
                Zip = 12345,
                Location = new City3L { Name = "Erebor" }
            }
        };
        var dst = mapper.Map(src);
        Assert.Equal("Gimli", dst.FullName);
        Assert.Equal(12345, dst.Home.Zip);
        Assert.Equal("Erebor", dst.Home.Location.Name);
    }

    [Fact]
    public void Nested_type_conversion_at_depth_maps_correctly()
    {
        var guidStr = "12345678-1234-1234-1234-123456789012";
        var mapper = new OuterConvMapper();
        var src = new OuterConv { Sub = new InnerConv { Count = 42L, Id = guidStr } };
        var dst = mapper.Map(src);
        Assert.Equal(42, dst.Sub.Count);
        Assert.Equal(Guid.Parse(guidStr), dst.Sub.Id);
    }

    [Fact]
    public void Dedup_two_members_same_nested_type_maps_both_correctly()
    {
        var mapper = new LineMapper();
        var src = new LineNested
        {
            A = new PtNested { X = 1, Y = 2 },
            B = new PtNested { X = 3, Y = 4 }
        };
        var dst = mapper.Map(src);
        Assert.Equal(1, dst.A.X);
        Assert.Equal(2, dst.A.Y);
        Assert.Equal(3, dst.B.X);
        Assert.Equal(4, dst.B.Y);
    }

    [Fact]
    public void User_declared_sub_mapper_is_called_not_synthesized()
    {
        var mapper = new NestOverrideMapper();
        var src = new NestOuter { Sub = new NestInner { V = "hello" } };
        var dst = mapper.Map(src);
        Assert.Equal("hello", dst.Sub.V);
    }

    [Fact]
    public void Recursive_tree_maps_small_tree_correctly()
    {
        var mapper = new NestTreeMapper();
        var src = new NestTree
        {
            V = 1,
            Children =
            [
                new NestTree { V = 2, Children = [new NestTree { V = 3, Children = [] }] },
                new NestTree { V = 4, Children = [] }
            ]
        };
        var dst = mapper.Map(src);
        Assert.Equal(1, dst.V);
        Assert.Equal(2, dst.Children.Count);
        Assert.Equal(2, dst.Children[0].V);
        Assert.Single(dst.Children[0].Children);
        Assert.Equal(3, dst.Children[0].Children[0].V);
        Assert.Equal(4, dst.Children[1].V);
    }
}
