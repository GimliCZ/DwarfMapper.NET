// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     Generator-level checks for integral narrowing / sign-change conversions
///     via INumberBase.CreateChecked.
/// </summary>
public class NumericConversionTests
{
    // ── Narrowing: compile + CreateChecked emitted ────────────────────────────

    [Fact]
    public void Long_to_int_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public long V { get; set; } }
                           public class D { public int  V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_uint_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int  V { get; set; } }
                           public class D { public uint V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_short_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int   V { get; set; } }
                           public class D { public short V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Ulong_to_long_narrowing_compiles_and_emits_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public ulong V { get; set; } }
                           public class D { public long  V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── Widening: must NOT emit CreateChecked (stays on implicit direct-assign) ─

    [Fact]
    public void Byte_to_int_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public byte V { get; set; } }
                           public class D { public int  V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_to_long_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int  V { get; set; } }
                           public class D { public long V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Byte_to_long_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public byte V { get; set; } }
                           public class D { public long V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Uint_to_ulong_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public uint  V { get; set; } }
                           public class D { public ulong V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Ushort_to_int_widening_does_NOT_emit_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public ushort V { get; set; } }
                           public class D { public int    V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── Enum↔int still routes through EnumConverter (not NumericConverter) ─────

    [Fact]
    public void Enum_to_int_routes_through_EnumConverter_not_NumericConverter()
    {
        // Enums have SpecialType.None → IsIntegral is false → NumericConverter skips them.
        // EnumConverter intercepts and emits CreateChecked via its own path.
        // The generated code should NOT contain the NumericConverter method signature
        // (which uses the prefix "__DwarfMap_Num_"), but should contain CreateChecked from
        // EnumConverter (prefix "__DwarfMap_EnumNum_").
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public enum Color { Red, Green, Blue }
                           public class S { public Color V { get; set; } }
                           public class D { public int   V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        // Must use EnumConverter path, not NumericConverter path
        Assert.DoesNotContain("__DwarfMap_Num_", generated, StringComparison.Ordinal);
        Assert.Contains("__DwarfMap_EnumNum_", generated, StringComparison.Ordinal);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── Identity: int → int must not use CreateChecked ────────────────────────

    [Fact]
    public void Int_to_int_identity_does_NOT_emit_CreateChecked()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int V { get; set; } }
                           public class D { public int V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.DoesNotContain("CreateChecked", generated, StringComparison.Ordinal);
    }

    // ── Precedence: user auto-candidate method must win over built-in numeric ────

    [Fact]
    public void User_auto_candidate_method_wins_over_builtin_numeric()
    {
        // The mapper class contains a private static int Shrink(long v) method.
        // When long→int is needed, the user-provided Shrink must win over the
        // built-in NumericConverter synthesized method. Today NumericConverter fires
        // BEFORE autoCandidates → this FAILS (generates __DwarfMap_Num_ instead of Shrink).
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public long V { get; set; } }
                           public class D { public int  V { get; set; } }
                           [DwarfMapper]
                           public partial class M
                           {
                               public partial D Map(S s);
                               private static int Shrink(long v) => (int)v;
                           }
                           """;
        GeneratorAssert.EmitsCompilableCode(src);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        // User method must be called, not the synthesized one
        Assert.Contains("Shrink", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("__DwarfMap_Num_", generated, StringComparison.Ordinal);
    }

    // ── Nullable composition: int? → short should compose ─────────────────────

    [Fact]
    public void Nullable_int_to_short_narrowing_compiles()
    {
        const string src = """
                           using DwarfMapper;
                           namespace Demo;
                           public class S { public int?  V { get; set; } }
                           public class D { public short V { get; set; } }
                           [DwarfMapper]
                           public partial class M { public partial D Map(S s); }
                           """;
        var generated = GeneratorAssert.CompilesClean(src);
        Assert.Contains("CreateChecked", generated, StringComparison.Ordinal);
    }
}
