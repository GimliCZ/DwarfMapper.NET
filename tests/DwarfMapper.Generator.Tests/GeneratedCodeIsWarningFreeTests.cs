// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DwarfMapper.Generator.Tests.Fuzzing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Generated code must not merely COMPILE — it must compile <b>without warnings</b>, in a nullable-annotated
/// context, because that is the build every real consumer has.
/// <para>
/// The suite already asserted that emitted code compiles, but "compiles" was defined as
/// <see cref="DiagnosticSeverity.Error" />. A generated file that only WARNS slipped through every tier — and
/// a warning out of generated code is strictly worse than one out of hand-written code: the consumer cannot
/// edit the file to fix it, cannot annotate it, and with <c>TreatWarningsAsErrors</c> (this repo's own default,
/// and a very common one) it is a hard build break with no remedy. DwarfMapper leaked exactly such a CS8601 for
/// every nullable-reference member mapped to a non-nullable one, and nothing caught it.
/// </para>
/// <para>
/// So the bar is: whatever DwarfMapper emits is clean. If a mapping is genuinely risky, DwarfMapper says so in
/// its OWN diagnostic (DWARF070 et al.) — actionable, pointing at the user's DTO, suppressible per-rule — and
/// leaves the compiler with nothing to complain about. Diagnostics arising in the user's own source are
/// excluded; only <c>.g.cs</c> is held to this standard.
/// </para>
/// </summary>
public class GeneratedCodeIsWarningFreeTests
{
    public static IEnumerable<object[]> AllCells() =>
        CombinatorialSchema.DepthOneMatrix()
            .Concat(CombinatorialSchema.DepthTwoMatrix())
            .Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllCells))]
    public void Combinatorial_cell_emits_warning_free_code(MatrixCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        AssertClean(cell.Source, $"combinatorial cell [{cell.BasicType} / {cell.ShapeName} / {cell.Variant}]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void Fuzz_seed_emits_warning_free_code(int seed)
    {
        AssertClean(SyntheticSchema.Generate(seed), $"fuzz seed {seed} (compile schema)");
        AssertClean(SyntheticSchema.GenerateBehavioral(seed), $"fuzz seed {seed} (behavioural schema)");
    }

    private static void AssertClean(string source, string what)
    {
        var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source, NullableContextOptions.Enable);

        Assert.True(
            warnings.Length == 0,
            $"The generator emitted code that WARNS in {what}. The consumer cannot edit generated code to "
            + "silence this, and under TreatWarningsAsErrors it is an unfixable build break. Either emit clean "
            + "code, or — if the mapping is genuinely risky — suppress the compiler diagnostic and report a "
            + "DWARF diagnostic against the user's own source instead (as DWARF070 does).\n  "
            + string.Join("\n  ", warnings
                .Select(d => $"{d.Id} at {d.Location.SourceTree?.FilePath}: "
                             + d.GetMessage(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal)));
    }
}
