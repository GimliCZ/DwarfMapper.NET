// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Positional record target ───────────────────────────────────────────────
    [Fact]
    public Task Snap_Constructor_PositionalRecord()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public record R(int X, string Y);
            [DwarfMapper]
            public partial class M { public partial R Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Record with extra init-only property ──────────────────────────────────
    [Fact]
    public Task Snap_Constructor_RecordWithExtraInit()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; public int Z { get; set; } }
            public record R(int X, string Y) { public int Z { get; init; } }
            [DwarfMapper]
            public partial class M { public partial R Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Required members ──────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Constructor_RequiredMembers()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public class D { public required int X { get; set; } public required string Y { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── DwarfMapperConstructor attribute ─────────────────────────────────────
    [Fact]
    public Task Snap_Constructor_DwarfMapperConstructor_Attribute()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int X { get; set; } public string Y { get; set; } = ""; }
            public class D
            {
                public D() { }
                [DwarfMapperConstructor]
                public D(int x, string y) { X = x; Y = y; }
                public int X { get; set; }
                public string Y { get; set; } = "";
            }
            [DwarfMapper]
            public partial class M { public partial D Map(S s); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
