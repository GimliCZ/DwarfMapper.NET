// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

// Class node with named neighbour slots to build the owner graph A→B, B⇄C, C⇄D, B⇄D.
public class GNode
{
    public string Name { get; set; } = "";
    public GNode? B { get; set; }
    public GNode? C { get; set; }
    public GNode? D { get; set; }
}

public class GNodeDto
{
    public string Name { get; set; } = "";
    public GNodeDto? B { get; set; }
    public GNodeDto? C { get; set; }
    public GNodeDto? D { get; set; }
}

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class GraphMapper
{
    public partial GNodeDto Map(GNode n);
}

// Record nodes (value equality) to prove distinct-but-equal nodes are NOT merged.
public class RecHolder
{
    public RecNode First { get; set; } = null!;
    public RecNode Second { get; set; } = null!;
}

public record RecNode(string Name);

public class RecHolderDto
{
    public RecNodeDto First { get; set; } = null!;
    public RecNodeDto Second { get; set; } = null!;
}

public record RecNodeDto(string Name);

[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
public partial class RecGraphMapper
{
    public partial RecHolderDto Map(RecHolder h);
}

public class GraphReconstructionRuntimeTests
{
    [Fact]
    public void Owner_graph_topology_is_reconstructed_faithfully()
    {
        var a = new GNode { Name = "A" };
        var b = new GNode { Name = "B" };
        var c = new GNode { Name = "C" };
        var d = new GNode { Name = "D" };
        a.B = b;
        b.C = c;
        b.D = d;
        c.B = b;
        c.D = d;
        d.C = c;
        d.B = b;

        var ta = new GraphMapper().Map(a);

        var tb = ta.B!;
        var tc = tb.C!;
        var td = tb.D!;
        Assert.Equal("B", tb.Name);
        Assert.Equal("C", tc.Name);
        Assert.Equal("D", td.Name);

        // Exactly one instance per source node, all cross/back edges relinked to it:
        Assert.Same(tb, tc.B); // B⇄C closed
        Assert.Same(td, tc.D); // C's D == the shared D'
        Assert.Same(tc, td.C); // C⇄D closed
        Assert.Same(tb, td.B); // B⇄D closed
        Assert.Same(td, tb.D); // B's D == shared D'
        // The D reached via B and via C is the SAME instance:
        Assert.Same(tb.D, tc.D);
        // The B reached via C and via D is the SAME instance:
        Assert.Same(tc.B, td.B);
    }

    [Fact]
    public void Self_loop_is_reconstructed()
    {
        var n = new GNode { Name = "self" };
        n.B = n;
        var t = new GraphMapper().Map(n);
        Assert.Same(t, t.B);
    }

    [Fact]
    public void Distinct_but_equal_records_are_not_merged()
    {
        // Two distinct instances that are value-equal (record equality).
        var holder = new RecHolder { First = new RecNode("X"), Second = new RecNode("X") };
        Assert.Equal(holder.First, holder.Second); // value-equal
        Assert.NotSame(holder.First, holder.Second); // distinct instances

        var dto = new RecGraphMapper().Map(holder);
        // Must remain two DISTINCT target instances — reference identity, not value equality.
        Assert.NotSame(dto.First, dto.Second);
        Assert.Equal("X", dto.First.Name);
        Assert.Equal("X", dto.Second.Name);
    }
}
