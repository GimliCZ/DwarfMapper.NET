// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// DWARF070 — a nullable REFERENCE source assigned to a non-nullable reference target.
/// <para>
/// <see cref="NullStrategy" /> governs nullable VALUE types only; a nullable reference source raw-assigns, by
/// design. But nothing handled the consequence: the raw assignment made the C# compiler emit <b>CS8601</b>
/// ("possible null reference assignment") from inside the GENERATED file. A consumer with
/// <c>TreatWarningsAsErrors</c> got a build break they could not fix, in code they cannot edit, with no
/// mention of DwarfMapper — while DwarfMapper itself, whose entire promise is "never silent", said nothing.
/// </para>
/// <para>
/// The suite could not see this: <see cref="GeneratorTestHarness" /> defaults to
/// <see cref="NullableContextOptions.Disable" />, where the annotation degrades to oblivious and the compiler
/// reports the unrelated CS8632 instead; and the compile-error helper filters to
/// <see cref="DiagnosticSeverity.Error" />, so a warning was invisible either way. Every test here therefore
/// compiles with nullable ENABLED — what real consumers actually ship.
/// </para>
/// </summary>
public class NullableRefToNonNullableTargetTests
{
    private const string NullableSourceNonNullableTarget = """
        using DwarfMapper;
        namespace Demo;
        public class A { public string? Name { get; set; } }
        public class B { public string Name { get; set; } = ""; }
        [DwarfMapper] public partial class M { public partial B Map(A a); }
        """;

    /// <summary>Every C# (compiler) diagnostic of warning severity or worse in the FINAL compilation —
    /// original source plus everything the generator emitted.</summary>
    private static string[] CompilerDiagnostics(string source, NullableContextOptions ctx)
    {
        var compilation = GeneratorTestHarness.BuildCompilation("NullCtxAsm", source, ctx);
        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outCompilation, out _);

        return outCompilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning
                        && d.Id.StartsWith("CS", StringComparison.Ordinal))
            .Select(d => $"{d.Id}: {d.GetMessage(CultureInfo.InvariantCulture)}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void Generated_code_does_not_leak_CS8601_into_the_consumers_build()
    {
        var cs = CompilerDiagnostics(NullableSourceNonNullableTarget, NullableContextOptions.Enable);

        Assert.True(
            cs.All(d => !d.StartsWith("CS8601", StringComparison.Ordinal)),
            "The generated mapper raised CS8601 (possible null reference assignment) in the consumer's "
            + "compilation. That is a warning the consumer CANNOT fix — it is in generated code they cannot "
            + "edit — and it fails any build with TreatWarningsAsErrors. DwarfMapper must speak in its own "
            + "voice (DWARF070) and keep the compiler quiet.\nGot:\n  " + string.Join("\n  ", cs));
    }

    [Fact]
    public void DWARF070_is_reported_so_the_null_risk_is_never_silent()
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(
            NullableSourceNonNullableTarget, NullableContextOptions.Enable);

        var d = Assert.Single(diagnostics.Where(x => x.Id == "DWARF070"));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("Name", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void The_suppression_is_the_null_forgiving_operator_not_a_changed_runtime_semantic()
    {
        // The documented contract is that a nullable reference source RAW-ASSIGNS. Suppressing CS8601 must not
        // quietly turn into a throw or a substituted value — that would be a behaviour break. The emitted code
        // still assigns the source straight across; only the compiler's complaint is silenced.
        var (_, generated) = GeneratorTestHarness.Run(
            NullableSourceNonNullableTarget, NullableContextOptions.Enable);

        Assert.Contains("Name = a.Name!", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_nullable_source_is_untouched()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string Name { get; set; } = ""; }
            public class B { public string Name { get; set; } = ""; }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

        Assert.Empty(diagnostics.Where(x => x.Id == "DWARF070"));
        Assert.DoesNotContain("a.Name!", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_target_is_untouched()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string? Name { get; set; } }
            public class B { public string? Name { get; set; } }
            [DwarfMapper] public partial class M { public partial B Map(A a); }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

        // null -> nullable is a perfectly good mapping. Warning here would be pure noise.
        Assert.Empty(diagnostics.Where(x => x.Id == "DWARF070"));
        Assert.DoesNotContain("a.Name!", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Oblivious_source_does_not_fire_so_legacy_code_is_not_flooded()
    {
        // NullableAnnotation has THREE states. A type written under `#nullable disable` is None (oblivious):
        // the user opted out of nullable analysis and the compiler raises no CS8601 there. Firing DWARF070 on
        // oblivious code would bury a legacy codebase in warnings about a contract it never opted into — so
        // the predicate tracks the compiler exactly (Annotated -> NotAnnotated) rather than using the looser
        // `!= NotAnnotated` test that the null-forgiving-'!' logic uses for its own, different purpose.
        var (diagnostics, _) = GeneratorTestHarness.Run(
            NullableSourceNonNullableTarget, NullableContextOptions.Disable);

        Assert.Empty(diagnostics.Where(x => x.Id == "DWARF070"));
    }

    [Fact]
    public void NullSubstitute_resolves_it_and_silences_the_diagnostic()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string? Name { get; set; } }
            public class B { public string Name { get; set; } = ""; }
            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(A.Name), nameof(B.Name), NullSubstitute = "(none)")]
                public partial B Map(A a);
            }
            """;

        var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

        // `a.Name ?? "(none)"` is provably non-null: no CS8601, so neither the '!' nor the warning belongs.
        Assert.Empty(diagnostics.Where(x => x.Id == "DWARF070"));
        Assert.DoesNotContain("a.Name!", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(CompilerDiagnostics(source, NullableContextOptions.Enable),
            d => d.StartsWith("CS8601", StringComparison.Ordinal));
    }

    [Fact]
    public void SkipNullSourceMembers_resolves_it_and_silences_the_diagnostic()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class A { public string? Name { get; set; } }
            public class B { public string Name { get; set; } = ""; }
            [DwarfMapper(SkipNullSourceMembers = true)]
            public partial class M { public partial B Map(A a); }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

        // The emitter guards with `if (a.Name is not null) …`, so the destination keeps its default and the
        // null never lands. SkipNullSourceMembers IS one of the fixes DWARF070 recommends — having applied it,
        // the user must not still be nagged.
        Assert.Empty(diagnostics.Where(x => x.Id == "DWARF070"));
        Assert.DoesNotContain(CompilerDiagnostics(source, NullableContextOptions.Enable),
            d => d.StartsWith("CS8601", StringComparison.Ordinal));
    }
}
