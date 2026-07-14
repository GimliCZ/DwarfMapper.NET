// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Hardening coverage for ConstructorSelector — the audit found the happy paths covered but the
///     AllowNonPublic accessibility matrix, obsolete-ctor exclusion, the annotated-override usability filter
///     (a latent CS1620), and the most-params auto-pick all untested.
/// </summary>
public class ConstructorSelectorHardeningTests
{
    // ── The latent CS1620 bug: an annotated ctor that is NOT usable (ref/out) must not be selected. ──
    [Fact]
    public void Annotated_ref_param_ctor_with_parameterless_falls_back_clean()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public class Dst
                         {
                             public Dst() { }
                             [DwarfMapperConstructor]
                             public Dst(ref int x) { X = x; }
                             public int X { get; set; }
                         }
                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         public partial class M { }
                         """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.DoesNotContain(errors, d => d.Id == "CS1620");
        Assert.Empty(errors); // falls back to the parameterless object-initializer path
    }

    // ── AllowNonPublic accessibility matrix ──
    [Fact]
    public void Internal_ctor_without_flag_reports_DWARF026()
    {
        var (diags, _) = GeneratorTestHarness.Run(Internal("", false));
        Assert.Contains(diags, d => d.Id == "DWARF026");
    }

    [Fact]
    public void Internal_ctor_with_flag_compiles_clean()
    {
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(Internal("internal", true));
        Assert.Empty(errors);
    }

    [Fact]
    public void Protected_internal_ctor_with_flag_compiles_clean()
    {
        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(Internal("protected internal", true));
        Assert.Empty(errors);
    }

    [Fact]
    public void Private_ctor_with_flag_still_reports_DWARF026()
    {
        // private is unreachable from the assembly scope even with the flag.
        var (diags, _) = GeneratorTestHarness.Run(OnlyCtor("private", true));
        Assert.Contains(diags, d => d.Id == "DWARF026");
    }

    [Fact]
    public void Protected_ctor_with_flag_still_reports_DWARF026()
    {
        var (diags, _) = GeneratorTestHarness.Run(OnlyCtor("protected", true));
        Assert.Contains(diags, d => d.Id == "DWARF026");
    }

    // ── Obsolete ctor exclusion ──
    [Fact]
    public void Obsolete_ctor_is_skipped_in_favour_of_a_usable_one()
    {
        const string s = """
                         using System;
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public class Dst
                         {
                             [Obsolete] public Dst(int x, string note) { X = x; }
                             public Dst(int x) { X = x; }
                             public int X { get; }
                         }
                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         public partial class M { }
                         """;

        var errors = GeneratorTestHarness.RunAndGetCompilationErrors(s);
        Assert.Empty(errors);
    }

    [Fact]
    public void Only_obsolete_ctor_reports_DWARF026()
    {
        const string s = """
                         using System;
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } }
                         public class Dst { [Obsolete] public Dst(int x) { X = x; } public int X { get; } }
                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         public partial class M { }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF026");
    }

    // ── Two annotated ctors, NO parameterless → DWARF025 (the no-parameterless ambiguity branch) ──
    [Fact]
    public void Two_annotated_ctors_without_parameterless_report_DWARF025()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
                         public class Dst
                         {
                             [DwarfMapperConstructor] public Dst(int x) { X = x; }
                             [DwarfMapperConstructor] public Dst(int x, string y) { X = x; Y = y; }
                             public int X { get; } public string Y { get; } = "";
                         }
                         [DwarfMapper]
                         [GenerateMap<Src, Dst>]
                         public partial class M { }
                         """;

        var (diags, _) = GeneratorTestHarness.Run(s);
        Assert.Contains(diags, d => d.Id == "DWARF025");
    }

    // ── Most-params auto-selection among usable ctors (Policy 5, non-tie) ──
    [Fact]
    public void Most_params_ctor_is_auto_selected()
    {
        const string s = """
                         using DwarfMapper;
                         namespace Demo;
                         public class Src { public int X { get; set; } public string Y { get; set; } = ""; }
                         public class Dst
                         {
                             public Dst(int x) { X = x; }
                             public Dst(int x, string y) { X = x; Y = y; }
                             public int X { get; } public string Y { get; } = "";
                         }
                         [DwarfMapper(CaseInsensitive = true)]
                         [GenerateMap<Src, Dst>]
                         public partial class M { }
                         """;

        var source = GeneratorTestHarness.Run(s).GeneratedSource;
        Assert.Contains("x:", source, StringComparison.Ordinal);
        Assert.Contains("y:", source, StringComparison.Ordinal); // the 2-arg ctor was chosen, not the 1-arg one
    }

    private static string Internal(string ctorAccessibility, bool flag)
    {
        return OnlyCtor(ctorAccessibility, flag,
            ctorAccessibility.Length == 0);
    }

    private static string OnlyCtor(string ctorAccessibility, bool flag, bool extraParameterless = false)
    {
        var acc = ctorAccessibility.Length == 0 ? "internal" : ctorAccessibility;
        var attr = flag ? "[DwarfMapper(AllowNonPublic = true)]" : "[DwarfMapper]";
        return $$"""
                 using DwarfMapper;
                 namespace Demo;
                 public class Src { public int X { get; set; } }
                 public class Dst
                 {
                     {{acc}} Dst() { }
                     public int X { get; set; }
                 }
                 {{attr}}
                 [GenerateMap<Src, Dst>]
                 public partial class M { }
                 """;
    }
}
