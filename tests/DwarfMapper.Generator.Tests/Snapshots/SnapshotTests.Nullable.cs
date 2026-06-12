// SPDX-License-Identifier: GPL-2.0-only
using System.Threading.Tasks;
using VerifyXunit;

namespace DwarfMapper.Generator.Tests;

public partial class SnapshotSuite
{
    // ── int? → int — throw on null (default) ─────────────────────────────────
    [Fact]
    public Task Snap_Nullable_IntToInt_Throw()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── int? → int — SetDefault strategy ─────────────────────────────────────
    [Fact]
    public Task Snap_Nullable_IntToInt_SetDefault()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public int N { get; set; } }
            [DwarfMapper(NullStrategy = NullStrategy.SetDefault)]
            public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }

    // ── T? → U? NullableProject ternary ──────────────────────────────────────
    [Fact]
    public Task Snap_Nullable_NullableToNullable_Ternary()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int? N { get; set; } }
            public class B { public long? N { get; set; } }
            [DwarfMapper]
            public partial class M { public partial B Map(A a); }
            """;
        var (_, generated) = GeneratorTestHarness.Run(src);
        return Verifier.Verify(generated);
    }
}
