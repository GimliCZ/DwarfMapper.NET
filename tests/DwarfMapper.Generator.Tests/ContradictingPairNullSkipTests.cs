// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     <b>B24 / DWARF099.</b> <c>MapNullSkipAttribute&lt;TSource, TTarget&gt;</c> is <c>AllowMultiple</c>, so
///     <c>[MapNullSkip&lt;Src, Dst&gt;(true)]</c> beside <c>[MapNullSkip&lt;Src, Dst&gt;(false)]</c> on one
///     class compiled clean: <c>ResolveNullSkip</c> returned the FIRST match by declaration order and dropped
///     the second without a word. <b>Source order decided whether a patch-merge mapper skips nulls</b>, and
///     the two orderings produced two different mappers in silence — which is the before-evidence this file
///     replaces with a refusal.
///     <para>
///         <b>Why an Error, when B31's twin next door is a Warning.</b> The V3 policy is that a discarded
///         directive is refused only where no defensible behaviour exists to keep. B31 has one — the fallback
///         construction is safe and documented — so it reports. This has none: the two declarations have
///         IDENTICAL scope, and only source order separates them, so "first wins" is an accidental rank
///         rather than a policy. Distinct from the method-versus-pair contradiction A6 DID define
///         (most-specific-wins, pinned both directions): those two forms have different scopes and therefore
///         a defensible ordering.
///     </para>
///     <para>
///         <b>IDENTICAL duplicates stay accepted, in silence.</b> A repeated declaration that says the same
///         thing discards nothing, so there is nothing to report — and that control is what keeps this from
///         being a duplicate-detector wearing a contradiction's name.
///     </para>
///     <para>
///         <b>This row was UNMEASURABLE until round 23's N6.</b> The surface matrix could not reach it: its
///         <c>×2</c> axis rendered two IDENTICAL applications, because a <c>bool</c> sampled to <c>true</c>
///         for both variants. N6 (<c>1decd32</c>) made the axis rotate distinct values, which is what turned
///         "recorded" into "askable" — and asking it is this file.
///     </para>
/// </summary>
public class ContradictingPairNullSkipTests
{
    private static string Source(string attributes) => $$"""
        using DwarfMapper;
        namespace Demo;
        public class Src { public int Id { get; set; } public string? A { get; set; } }
        public class Dst { public int Id { get; set; } public string? A { get; set; } }
        [DwarfMapper]
        {{attributes}}
        public partial class M { public partial Dst Update(Src s, Dst d); }
        """;

    private static string Message(IEnumerable<Diagnostic> diags) =>
        string.Join(" | ", diags.Where(d => d.Id == "DWARF099")
            .Select(d => d.GetMessage(CultureInfo.InvariantCulture)));

    /// <summary>
    ///     Both orderings, because order is the defect. Neither may be resolved silently, and the refusal
    ///     must read the same whichever way round the caller wrote it.
    /// </summary>
    [Theory]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    public void Two_contradicting_declarations_over_one_pair_are_refused(string first, string second)
    {
        var (diags, generated) = GeneratorTestHarness.Run(Source(
            $"[MapNullSkip<Src, Dst>({first})]\n[MapNullSkip<Src, Dst>({second})]"));

        Assert.True(diags.Count(d => d.Id == "DWARF099") == 1,
            $"[{first} then {second}] expected exactly one DWARF099; got "
            + $"{diags.Count(d => d.Id == "DWARF099")}. B24: this compiled clean and the second "
            + "declaration was discarded in silence.");
        Assert.Equal(DiagnosticSeverity.Error, diags.First(d => d.Id == "DWARF099").Severity);

        // The message names BOTH values, so the reader does not have to work out which one was in force.
        var msg = Message(diags);
        Assert.Contains($"[MapNullSkip<Src, Dst>({first})]", msg, StringComparison.Ordinal);
        Assert.Contains($"[MapNullSkip<Src, Dst>({second})]", msg, StringComparison.Ordinal);

        // An Error, so nothing is emitted — the point is that the generator does not GUESS.
        Assert.Empty(generated);
    }

    /// <summary>
    ///     The CONTROL that defines the rule's edge: two declarations that AGREE discard nothing, so they are
    ///     accepted without a word and the mapper is generated. A guard that merely counted duplicates would
    ///     fail here.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Two_agreeing_declarations_over_one_pair_are_accepted_in_silence(string value)
    {
        var source = Source($"[MapNullSkip<Src, Dst>({value})]\n[MapNullSkip<Src, Dst>({value})]");
        var (diags, generated) = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain(diags, d => d.Id == "DWARF099");
        Assert.NotEmpty(generated);
        GeneratorAssert.EmitsCompilableCode(source);
    }

    /// <summary>
    ///     The other CONTROL, and the one that keeps the check keyed on the PAIR rather than on the
    ///     attribute: opposite values over DIFFERENT pairs are not a contradiction at all — that is the
    ///     option working as designed, one policy per pair.
    /// </summary>
    [Fact]
    public void Opposite_values_over_different_pairs_are_not_a_contradiction()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Id { get; set; } public string? A { get; set; } }
            public class Dst { public int Id { get; set; } public string? A { get; set; } }
            public class Other { public int Id { get; set; } public string? A { get; set; } }
            [DwarfMapper]
            [MapNullSkip<Src, Dst>(true)]
            [MapNullSkip<Src, Other>(false)]
            public partial class M
            {
                public partial Dst Update(Src s, Dst d);
                public partial Other UpdateOther(Src s, Other d);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain(diags, d => d.Id == "DWARF099");
        GeneratorAssert.EmitsCompilableCode(source);
    }

    /// <summary>
    ///     The A6 boundary, restated as a test so DWARF099 cannot creep across it: a METHOD-scoped
    ///     <c>[MapNullSkip(bool)]</c> contradicting the pair-scoped form is NOT this defect. Those two forms
    ///     have different scopes and a defensible ranking — most-specific-wins — and A6 pinned it in both
    ///     directions. Only identical scope is unrankable.
    /// </summary>
    [Fact]
    public void A_method_scoped_contradiction_is_still_most_specific_wins_and_not_an_error()
    {
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Src { public int Id { get; set; } public string? A { get; set; } }
            public class Dst { public int Id { get; set; } public string? A { get; set; } }
            [DwarfMapper]
            [MapNullSkip<Src, Dst>(true)]
            public partial class M
            {
                [MapNullSkip(false)]
                public partial Dst Update(Src s, Dst d);
            }
            """;
        var (diags, _) = GeneratorTestHarness.Run(source);

        Assert.DoesNotContain(diags, d => d.Id == "DWARF099");
        GeneratorAssert.EmitsCompilableCode(source);
    }

    /// <summary>
    ///     Runtime evidence for the CONTROL half (B19). The refusal itself has no runtime — an Error emits
    ///     nothing — but the accepted duplicate must still MEAN what the single declaration means, or this
    ///     change would have quietly altered the behaviour it was meant to leave alone. Executed: a null
    ///     source member is skipped under <c>true</c> and written under <c>false</c>, declared twice each way.
    /// </summary>
    [Theory]
    [InlineData("true", "kept")]
    [InlineData("false", "overwritten")]
    public void An_accepted_duplicate_still_behaves_as_the_single_declaration_does(string value, string expect)
    {
        var (assembly, errors) = GeneratorTestHarness.EmitAssembly(
            Source($"[MapNullSkip<Src, Dst>({value})]\n[MapNullSkip<Src, Dst>({value})]"));
        Assert.True(assembly is not null,
            "did not emit: " + string.Join(", ",
                errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

        var srcType = assembly!.GetType("Demo.Src")!;
        var dstType = assembly.GetType("Demo.Dst")!;
        var mapper = Activator.CreateInstance(assembly.GetType("Demo.M")!)!;

        var s = Activator.CreateInstance(srcType)!;   // A left null — the whole question
        srcType.GetProperty("Id")!.SetValue(s, 1);
        var d = Activator.CreateInstance(dstType)!;
        dstType.GetProperty("A")!.SetValue(d, "existing");

        var result = assembly.GetType("Demo.M")!.GetMethod("Update")!.Invoke(mapper, [s, d])!;
        var a = (string?)dstType.GetProperty("A")!.GetValue(result);

        Assert.Equal(expect == "kept" ? "existing" : null, a);
    }
}
