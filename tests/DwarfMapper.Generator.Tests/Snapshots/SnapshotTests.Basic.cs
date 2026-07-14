// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Basic: public properties (flat) ──────────────────────────────────────
    [Fact]
    public Task Snap_Flat_Properties()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
                           public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M { public partial PersonDto Map(Person p); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── Public fields ─────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Public_Fields()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public int X; public string Y = ""; }
                           public class Dst { public int X; public string Y = ""; }
                           [DwarfMapper]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── CaseInsensitive = true ────────────────────────────────────────────────
    [Fact]
    public Task Snap_CaseInsensitive()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Src { public int Value { get; set; } }
                           public class Dst { public int value { get; set; } }
                           [DwarfMapper(CaseInsensitive = true)]
                           public partial class M { public partial Dst Map(Src s); }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── MapProperty rename ────────────────────────────────────────────────────
    [Fact]
    public Task Snap_MapProperty_Rename()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public string FullName { get; set; } = ""; }
                           public class Target { public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("FullName", "Name")]
                               public partial Target Map(Source s);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── MapProperty Use= converter ────────────────────────────────────────────
    [Fact]
    public Task Snap_MapProperty_Use_Converter()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Source { public string Amount { get; set; } = ""; }
                           public class Target { public int Amount { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapProperty("Amount", "Amount", Use = nameof(ParseInt))]
                               public partial Target Map(Source s);
                               private static int ParseInt(string v) => int.Parse(v);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }

    // ── MapIgnore ─────────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_MapIgnore()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class Person { public int Age { get; set; } }
                           public class PersonDto { public int Age { get; set; } public string Name { get; set; } = ""; }
                           [DwarfMapper]
                           public partial class M
                           {
                               [MapIgnore("Name")]
                               public partial PersonDto Map(Person p);
                           }
                           """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verify(generated);
    }
}
