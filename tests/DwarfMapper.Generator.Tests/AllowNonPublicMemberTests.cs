// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// [DwarfMapper(AllowNonPublic = true)] lets the generator read internal getters and write internal setters
/// (and fields) that the mapper's assembly can reach — not just public ones. Without the flag they stay
/// invisible (the destination member is simply not mapped, like any non-writable member).
/// </summary>
public class AllowNonPublicMemberTests
{
    [Fact]
    public void Internal_setter_is_written_with_AllowNonPublic()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; set; } public string B { get; set; } = ""; }
            public class Dst { public int A { get; internal set; } public string B { get; internal set; } = ""; }
            [DwarfMapper(AllowNonPublic = true)]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        var (diags, source) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("A =", source, System.StringComparison.Ordinal);
        Assert.Contains("B =", source, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Internal_getter_is_read_with_AllowNonPublic()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; internal set; } }
            public class Dst { public int A { get; set; } }
            [DwarfMapper(AllowNonPublic = true)]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        var (diags, source) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("A =", source, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Internal_setter_is_NOT_written_without_the_flag()
    {
        const string s = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int A { get; set; } }
            public class Dst { public int A { get; internal set; } }
            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M { }
            """;

        var (_, source) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain("A =", source, System.StringComparison.Ordinal); // internal setter invisible without opt-in
    }
}
