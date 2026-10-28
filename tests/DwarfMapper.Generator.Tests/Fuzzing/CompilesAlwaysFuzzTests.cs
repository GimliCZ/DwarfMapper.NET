// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.Generator.Tests.Fuzzing
{
    public class CompilesAlwaysFuzzTests
    {
        // 1,000 seeds — see TestPopulation.CompilesAlwaysSeeds.
        public static IEnumerable<object[]> Seeds()
        {
            return Enumerable.Range(0, TestPopulations.Count(TestPopulation.CompilesAlwaysSeeds))
                .Select(i => new object[]
                {
                    i
                });
        }

        // 250 seeds — see TestPopulation.CompilesAlwaysAdvancedSeeds.
        public static IEnumerable<object[]> AdvancedSeeds()
        {
            return Enumerable.Range(0, TestPopulations.Count(TestPopulation.CompilesAlwaysAdvancedSeeds))
                .Select(i => new object[]
                {
                    i
                });
        }

        /// <summary>
        ///     The seeds are dealt into two classes (round 34 E12, <see cref="TheoryParts" />): the 1,000 and the
        ///     advanced theory's 250 shared one class.
        /// </summary>
        private const int Parts = 2;

        public static IEnumerable<object[]> Seeds(int part)
        {
            return TheoryParts.Of(Seeds(), part, Parts);
        }

        internal static void MapperAlwaysCompiles(int seed)
        {
            var src = SyntheticSchema.Generate(seed);
            var (diagnostics, generated, compileErrors) = GeneratorTestHarness.RunWithCompilationErrors(src);
            var dwarfErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
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
            var (diagnostics, generated, compileErrors) = GeneratorTestHarness.RunWithCompilationErrors(src);
            var dwarfErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(
                dwarfErrors.Count == 0 && compileErrors.Length == 0,
                $"seed={seed} (advanced) produced errors.\n" +
                $"DWARF: {string.Join(", ", dwarfErrors.Select(d => d.Id))}\n" +
                $"CS: {string.Join(", ", compileErrors.Select(d => d.Id + ' ' + d.GetMessage(CultureInfo.InvariantCulture)))}\n" +
                $"--- generated ---\n{generated}\n" +
                $"--- source ---\n{src}");
        }
    }

    // Round 34 (E12): CompilesAlwaysFuzzTests's heavy theory, dealt into parts by TheoryParts so the parts run in
    // parallel; each part is a class of its own because xunit runs one class's tests one after another.
    public sealed class CompilesAlwaysFuzzTestsPart0
    {
        [Theory]
        [MemberData(nameof(CompilesAlwaysFuzzTests.Seeds), 0, MemberType = typeof(CompilesAlwaysFuzzTests))]
        public void Generated_mapper_always_compiles(int seed)
        {
            CompilesAlwaysFuzzTests.MapperAlwaysCompiles(seed);
        }
    }

    public sealed class CompilesAlwaysFuzzTestsPart1
    {
        [Theory]
        [MemberData(nameof(CompilesAlwaysFuzzTests.Seeds), 1, MemberType = typeof(CompilesAlwaysFuzzTests))]
        public void Generated_mapper_always_compiles(int seed)
        {
            CompilesAlwaysFuzzTests.MapperAlwaysCompiles(seed);
        }
    }
}
