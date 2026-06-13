// SPDX-License-Identifier: GPL-2.0-only
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// ImplicitConversions policy (DWARF038): non-lossless basic-type conversions (narrowing, parse/format,
/// cross-category numeric) surface as an Info SUGGESTION by default, or a build ERROR when
/// [DwarfMapper(ImplicitConversions = false)]. Lossless same-category widening and identity are silent.
/// </summary>
public class ConversionPolicyGeneratorTests
{
    private static Diagnostic? D038(System.Collections.Generic.IEnumerable<Diagnostic> diags)
        => diags.FirstOrDefault(d => d.Id == "DWARF038");

    [Fact]
    public void Narrowing_in_permissive_mode_is_an_Info_suggestion_and_still_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long Score { get; set; } }
            public class D { public int Score { get; set; } }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = D038(diags);
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Info, d!.Severity);
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src)); // still maps (CreateChecked)
    }

    [Fact]
    public void Narrowing_in_strict_mode_is_a_build_error()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long Score { get; set; } }
            public class D { public int Score { get; set; } }
            [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = D038(diags);
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Error, d!.Severity);
    }

    [Fact]
    public void Lossless_widening_is_silent()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int A { get; set; } public float B { get; set; } }
            public class D { public long A { get; set; } public double B { get; set; } } // int→long, float→double: same-category widening
            [DwarfMapper(ImplicitConversions = false)] public partial class M { public partial D Map(S s); }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D038(diags)); // no suggestion, no error — even in strict mode
        Assert.DoesNotContain(diags, x => x.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Cross_category_numeric_is_flagged()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int N { get; set; } }
            public class D { public double N { get; set; } } // int→double: cross-category
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(D038(diags));
    }

    [Fact]
    public void Explicit_MapProperty_Use_silences_the_suggestion()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public long Score { get; set; } }
            public class D { public int Score { get; set; } }
            [DwarfMapper(ImplicitConversions = false)]
            public partial class M
            {
                [MapProperty(nameof(S.Score), nameof(D.Score), Use = nameof(Shrink))]
                public partial D Map(S s);
                private static int Shrink(long v) => (int)v;
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.Null(D038(diags)); // explicit conversion → no DWARF038, even in strict mode
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
