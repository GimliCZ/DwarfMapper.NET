// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.IntegrationTests;

// Security regression tests tied to docs/SECURITY.md — they prove, at runtime, the two failure modes that
// hit the reference libraries are avoided:
//   • CVE-2026-32933 (AutoMapper 14 uncontrolled recursion → uncatchable StackOverflow DoS): cyclic input
//     must produce a CATCHABLE DwarfMappingDepthException, never a process-killing StackOverflow.
//   • Culture footgun: generated Parse/ToString must be invariant, NOT pick up ambient CurrentCulture
//     (where "1.5" parses to 15 under de-DE).

public class Sec_Node
{
    public int V { get; set; }
    public Sec_Node? Next { get; set; }
}

public class Sec_NodeDto
{
    public int V { get; set; }
    public Sec_NodeDto? Next { get; set; }
}

[DwarfMapper]
public partial class Sec_CycleMapper
{
    public partial Sec_NodeDto Map(Sec_Node n);
}

public class Sec_ParseSrc
{
    public string D { get; set; } = "";
}

public class Sec_ParseDst
{
    public double D { get; set; }
}

[DwarfMapper]
public partial class Sec_ParseMapper
{
    public partial Sec_ParseDst Map(Sec_ParseSrc s);
}

public class Sec_FmtSrc
{
    public double D { get; set; }
}

public class Sec_FmtDst
{
    public string D { get; set; } = "";
}

[DwarfMapper]
public partial class Sec_FmtMapper
{
    public partial Sec_FmtDst Map(Sec_FmtSrc s);
}

public class SecurityRegressionRuntimeTests
{
    [Fact]
    public void Cyclic_input_throws_catchable_exception_not_stackoverflow()
    {
        // A reference cycle (node points at itself) — the exact shape that crashes AutoMapper 14.
        var node = new Sec_Node { V = 1 };
        node.Next = node;

        // Must be a CATCHABLE exception (the test process survives), never an uncatchable StackOverflow.
        Assert.Throws<DwarfMappingDepthException>(() => new Sec_CycleMapper().Map(node));
    }

    [Fact]
    public void Parsing_is_culture_invariant_under_de_DE()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); // "." is a thousands sep here
            // Invariant parse → 1.5. A culture-sensitive Parse would read "1.5" as 15 (or throw) under de-DE.
            Assert.Equal(1.5d, new Sec_ParseMapper().Map(new Sec_ParseSrc { D = "1.5" }).D);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Formatting_is_culture_invariant_under_de_DE()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); // would format with "," decimal
            // Invariant ToString → "1234.5" with a '.', not de-DE's "1234,5".
            Assert.Equal("1234.5", new Sec_FmtMapper().Map(new Sec_FmtSrc { D = 1234.5d }).D);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
