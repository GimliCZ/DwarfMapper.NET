// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── long → int CreateChecked numeric narrowing ────────────────────────────
    [Fact]
    public Task Snap_Conversion_LongToInt_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public long V { get; set; } }
            public class B { public int V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── string → int via IParsable ────────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_StringToInt_IParsable()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string V { get; set; } = ""; }
            public class B { public int V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── string → Guid via IParsable ───────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_StringToGuid_IParsable()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class A { public string V { get; set; } = ""; }
            public class B { public Guid V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── int → string via IFormattable ─────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_IntToString_IFormattable()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int V { get; set; } }
            public class B { public string V { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── DateTime → string "o" format ─────────────────────────────────────────
    [Fact]
    public Task Snap_Conversion_DateTimeToString_Iso8601()
    {
        const string src = """
            using DwarfMapper;
            using System;
            namespace Demo;
            public class A { public DateTime V { get; set; } }
            public class B { public string V { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
