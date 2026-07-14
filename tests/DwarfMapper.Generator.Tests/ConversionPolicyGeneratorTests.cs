// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ImplicitConversions policy (DWARF038): lossy basic-type conversions (narrowing, parse/format,
///     cross-category numeric) surface as a Warning by default (item 8), or a build ERROR when
///     [DwarfMapper(ImplicitConversions = false)]. Lossless same-category widening and identity are silent.
/// </summary>
public class ConversionPolicyGeneratorTests
{
    private static Diagnostic? D038(IEnumerable<Diagnostic> diags)
    {
        return diags.FirstOrDefault(d => d.Id == "DWARF038");
    }

    [Fact]
    public void Parse_format_conversion_warns_and_names_the_runtime_exceptions()
    {
        // Item 15: a string -> int parse conversion can throw at runtime; the message must say so.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public string Score { get; set; } = ""; }
            public class D { public int Score { get; set; } }
            [DwarfMapper] public partial class M { public partial D Map(S s); }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        var d = D038(diags);
        Assert.NotNull(d);
        Assert.Equal(DiagnosticSeverity.Warning, d!.Severity); // lossy → Warning (item 8)
        var msg = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("FormatException", msg, System.StringComparison.Ordinal);
        Assert.Contains("OverflowException", msg, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Narrowing_in_permissive_mode_is_a_Warning_and_still_compiles()
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
        // Item 8: a lossy (narrowing) implicit conversion is a Warning (data-losing behaviour), not a mere Info.
        Assert.Equal(DiagnosticSeverity.Warning, d!.Severity);
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
