// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Update-into-existing: void Map(S src, T dest) ─────────────────────────
    [Fact]
    public Task Snap_MapInto_Void()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } public string Name { get; set; } = ""; public long Score { get; set; } }
            public class D { public int Id { get; set; } public string Name { get; set; } = ""; public int Score { get; set; } }
            [DwarfMapper]
            public partial class M { public partial void Update(S src, D dest); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Update-into-existing: T Map(S src, T dest) (fluent return) ────────────
    [Fact]
    public Task Snap_MapInto_Return()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } }
            [DwarfMapper]
            public partial class M { public partial D Update(S src, D dest); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Zero-alloc span map: void Map(ReadOnlySpan<S> src, Span<D> dst) ───────
    [Fact]
    public Task Snap_SpanMap_Scalar()
    {
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            [DwarfMapper]
            public partial class M { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
