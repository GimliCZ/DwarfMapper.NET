// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── Enum by-name (switch expression) ─────────────────────────────────────
    [Fact]
    public Task Snap_Enum_ByName_Switch()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green, Blue }
            public enum ColorDto { Red, Green, Blue }
            public class A { public Color C { get; set; } }
            public class B { public ColorDto C { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Enum by-value (CreateChecked cast) ────────────────────────────────────
    [Fact]
    public Task Snap_Enum_ByValue_CreateChecked()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Status { Active = 1, Inactive = 2 }
            public enum StatusCode { Active = 1, Suspended = 3 }
            public class A { public Status S { get; set; } }
            public class B { public StatusCode S { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Enum → string ─────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_Enum_To_String()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green }
            public class A { public Color V { get; set; } }
            public class B { public string V { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── String → enum ─────────────────────────────────────────────────────────
    [Fact]
    public Task Snap_String_To_Enum()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green }
            public class A { public string V { get; set; } = ""; }
            public class B { public Color V { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── Enum → numeric (int underlying) ──────────────────────────────────────
    [Fact]
    public Task Snap_Enum_To_Numeric()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Priority { Low = 0, High = 1 }
            public class A { public Priority P { get; set; } }
            public class B { public int P { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
