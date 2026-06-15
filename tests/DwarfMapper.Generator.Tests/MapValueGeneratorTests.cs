// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// [MapValue] — assign a constant or computed value to a destination member with no source (Phase 2).
/// Constants are type-checked (DWARF040) and formatted as C# literals; Use= names a parameterless
/// provider whose return type must be assignable (DWARF041); conflicts/unknown targets are DWARF042.
/// A [MapValue]'d target counts as mapped (suppresses DWARF001).
/// </summary>
public class MapValueGeneratorTests
{
    private static Diagnostic? Find(System.Collections.Generic.IEnumerable<Diagnostic> diags, string id)
        => diags.FirstOrDefault(d => d.Id == id);

    // ── Deterministic happy paths ──────────────────────────────────────────────

    [Fact]
    public void Constant_string_emits_literal_and_satisfies_completeness()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public string Source { get; set; } = ""; }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Source), "api-v2")]
                public partial D Map(S s);
            }
            """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Source = \"api-v2\"", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Constant_widening_numeric_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public long Score { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Score), 5)]
                public partial D Map(S s);
            }
            """;
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Float_constant_is_cast_and_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public float Ratio { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Ratio), 1.5f)]
                public partial D Map(S s);
            }
            """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("(float)(1.5)", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Enum_constant_is_cast_and_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public enum Color { Red, Green, Blue }
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public Color C { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.C), Color.Green)]
                public partial D Map(S s);
            }
            """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains(")(1)", gen, StringComparison.Ordinal); // (global::Demo.Color)(1)
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    [Fact]
    public void Constant_null_to_nullable_reference_emits_null()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public string? Note { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Note), null)]
                public partial D Map(S s);
            }
            """;
        var (diags, gen) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("Note = null", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Computed_use_method_emits_call_and_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public System.DateTime CreatedAt { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.CreatedAt), Use = nameof(Now))]
                public partial D Map(S s);
                private static System.DateTime Now() => default;
            }
            """;
        var (_, gen) = GeneratorTestHarness.Run(src);
        Assert.Contains("CreatedAt = Now()", gen, StringComparison.Ordinal);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }

    // ── Adversarial / diagnostics ───────────────────────────────────────────────

    [Fact]
    public void Constant_type_mismatch_reports_DWARF040()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public int Count { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Count), "not-an-int")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF040"));
    }

    [Fact]
    public void Null_to_non_nullable_value_type_reports_DWARF040()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public int Count { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Count), null)]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF040"));
    }

    [Theory]
    [InlineData("Use = nameof(WithParam)", "private static int WithParam(int x) => x;")] // not parameterless
    [InlineData("Use = nameof(Voidy)", "private static void Voidy() { }")]                // void return
    [InlineData("Use = \"DoesNotExist\"", "")]                                            // missing method
    [InlineData("Use = nameof(WrongType)", "private static string WrongType() => \"x\";")] // unassignable return
    public void Invalid_use_provider_reports_DWARF041(string useArg, string member)
    {
        var src = $$"""
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public int Count { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Count), {{useArg}})]
                public partial D Map(S s);
                {{member}}
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF041"));
    }

    [Theory]
    [InlineData("[MapProperty(nameof(S.Extra), nameof(D.X))]")] // conflict with MapProperty
    [InlineData("[MapIgnore(nameof(D.X))]")]                    // conflict with MapIgnore
    [InlineData("[MapValue(nameof(D.X), 2)]")]                  // duplicate MapValue
    public void Conflicting_mapvalue_reports_DWARF042(string conflicting)
    {
        var src = $$"""
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } public int Extra { get; set; } }
            public class D { public int Id { get; set; } public int X { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.X), 1)]
                {{conflicting}}
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF042"));
    }

    [Fact]
    public void Unknown_target_reports_DWARF042()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue("Nonexistent", 1)]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF042"));
    }

    [Fact]
    public void Targeting_constructor_parameter_reports_DWARF042()
    {
        // Source supplies Tag so the constructor resolves successfully; the [MapValue] then targets that
        // already-consumed constructor parameter, which is the DWARF042 case (object-init members only).
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } public string Tag { get; set; } = ""; }
            public record D(int Id, string Tag);
            [DwarfMapper] public partial class M
            {
                [MapValue("Tag", "x")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF042"));
    }

    [Fact]
    public void Constant_on_a_public_field_target_compiles()
    {
        // [MapValue] targets a public FIELD, not only a property (both are mapped destinations).
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id; public string Source = ""; }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.Source), "api-v2")]
                public partial D Map(S s);
            }
            """;
        var (diags, gen) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("Source = \"api-v2\"", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Dotted_target_path_reports_DWARF042()
    {
        // MapValue does not support unflatten/dotted target paths — caught with a clear message.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public Addr Address { get; set; } = new(); }
            [DwarfMapper] public partial class M
            {
                [MapValue("Address.City", "x")]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.NotNull(Find(diags, "DWARF042"));
    }

    // ── Combinatorial fuzz: valid (type, literal) pairs all compile cleanly ─────

    [Theory]
    [InlineData("string", "\"hello\"")]
    [InlineData("int", "42")]
    [InlineData("long", "42")]        // int literal widening to long
    [InlineData("double", "42")]      // int literal to double
    [InlineData("double", "3.14")]
    [InlineData("float", "3.14f")]
    [InlineData("decimal", "10")]     // int literal to decimal
    [InlineData("bool", "true")]
    [InlineData("char", "'z'")]
    [InlineData("string?", "null")]
    public void Valid_constant_pairs_compile_without_error(string targetType, string literal)
    {
        var src = $$"""
            using DwarfMapper;
            #nullable enable
            namespace Demo;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } public {{targetType}} V { get; set; } }
            [DwarfMapper] public partial class M
            {
                [MapValue(nameof(D.V), {{literal}})]
                public partial D Map(S s);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
    }
}
