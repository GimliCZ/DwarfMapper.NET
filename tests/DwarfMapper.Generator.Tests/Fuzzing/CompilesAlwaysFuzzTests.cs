// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Fuzzing;

public class CompilesAlwaysFuzzTests
{
    public static IEnumerable<object[]> Seeds()
    {
        return Enumerable.Range(0, 200).Select(i => new object[] { i });
    }

    public static IEnumerable<object[]> AdvancedSeeds()
    {
        return Enumerable.Range(0, 50).Select(i => new object[] { i });
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Generated_mapper_always_compiles(int seed)
    {
        var src = SyntheticSchema.Generate(seed);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        var dwarfErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.True(
            dwarfErrors.Count == 0 && compileErrors.Length == 0,
            $"seed={seed} produced errors.\n" +
            $"DWARF: {string.Join(", ", dwarfErrors.Select(d => d.Id))}\n" +
            $"CS: {string.Join(", ", compileErrors.Select(d => d.Id + ' ' + d.GetMessage(CultureInfo.InvariantCulture)))}\n" +
            $"--- generated ---\n{generated}\n" +
            $"--- source ---\n{src}");
    }

    [Theory]
    [MemberData(nameof(AdvancedSeeds))]
    public void Generated_advanced_mapper_always_compiles(int seed)
    {
        var src = SyntheticSchema.GenerateWithAdvancedFeatures(seed);
        var (diagnostics, generated) = GeneratorTestHarness.Run(src);
        var dwarfErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var compileErrors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
        Assert.True(
            dwarfErrors.Count == 0 && compileErrors.Length == 0,
            $"seed={seed} (advanced) produced errors.\n" +
            $"DWARF: {string.Join(", ", dwarfErrors.Select(d => d.Id))}\n" +
            $"CS: {string.Join(", ", compileErrors.Select(d => d.Id + ' ' + d.GetMessage(CultureInfo.InvariantCulture)))}\n" +
            $"--- generated ---\n{generated}\n" +
            $"--- source ---\n{src}");
    }
}
