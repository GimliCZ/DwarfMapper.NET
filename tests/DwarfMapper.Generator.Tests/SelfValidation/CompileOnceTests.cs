// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Fuzzing;
using Xunit.Abstractions;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A check that needs one source's generator output compiles that source once.</b>
    ///     <para>
    ///         Round 34 (E7). Generator.Tests compiled the same input more than once in 51 % of its compilations, most of
    ///         it inside one test: <see cref="GeneratorAssert" />'s checks ran the generator for its diagnostics and again
    ///         for the compile errors (790 call sites, 3,788 compilations of 1,916 inputs), and every combinatorial cell's
    ///         row ran it for the diagnostics and again to emit. Each now answers all its questions from one driver run;
    ///         these hold them to it.
    ///     </para>
    ///     <para>
    ///         Round 34 (E11). Three fuzz families did the same in the test body: a feature-combination row ran
    ///         <c>Run</c> and then <c>EmitsCompilableCode</c>, a compiles-always row <c>Run</c> and then
    ///         <c>RunAndGetCompilationErrors</c>, a cross-config row <c>Run</c> and then <c>EmitAssembly</c>. Measured
    ///         with a probe on <c>BuildCompilation</c>, that was 1,068 of the 10,697 compilations in build-test's run and
    ///         5,032 of the 26,307 in the deep tier. One row of each is run here and held to one compilation.
    ///     </para>
    /// </summary>
    public class CompileOnceTests
    {
        private readonly ITestOutputHelper _output;

        public CompileOnceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private const string Source = """
                                      using DwarfMapper;
                                      namespace Demo;
                                      public class A { public int X { get; set; } }
                                      public class B { public int X { get; set; } }
                                      [DwarfMapper] public partial class M { public partial B Map(A a); }
                                      """;

        [Fact]
        public void CompilesClean_compiles_its_source_once()
        {
            AssertOneCompilation(() => GeneratorAssert.CompilesClean(Source));
        }

        [Fact]
        public void CompilesCleanWithDiagnostics_compiles_its_source_once()
        {
            AssertOneCompilation(() => GeneratorAssert.CompilesCleanWithDiagnostics(Source));
        }

        [Fact]
        public void EmitsCompilableCode_compiles_its_source_once()
        {
            AssertOneCompilation(() => GeneratorAssert.EmitsCompilableCode(Source));
        }

        [Fact]
        public void A_cells_diagnostics_and_assembly_come_from_one_compilation()
        {
            AssertOneCompilation(() => GeneratorTestHarness.RunAndEmit(Source));
        }

        [Fact]
        public void A_valid_feature_combination_row_compiles_its_source_once()
        {
            var row = FeatureCombinationFuzzTests.Combos().First();
            Assert.Equal("construct/none:rename", row[0]);
            AssertOneCompilation(() => new FeatureCombinationFuzzTests(_output).Feature_combination_resolves_as_expected(
                (string)row[0], (string)row[1], (string)row[2], (int[])row[3]));
        }

        [Fact]
        public void A_compiles_always_row_compiles_its_source_once()
        {
            AssertOneCompilation(() => new CompilesAlwaysFuzzTests().Generated_mapper_always_compiles(0));
            AssertOneCompilation(() => new CompilesAlwaysFuzzTests().Generated_advanced_mapper_always_compiles(0));
        }

        [Fact]
        public void A_cross_config_row_compiles_its_source_once()
        {
            var pair = CrossConfigFuzzTests.Pairs().First();
            AssertOneCompilation(() => new CrossConfigFuzzTests().Config_pair_compiles_clean_and_is_value_preserving(
                (string)pair[0], (string)pair[1], (int)pair[2]));

            var update = CrossConfigFuzzTests.UpdateModeSeeds().First();
            AssertOneCompilation(() => new CrossConfigFuzzTests().Update_into_under_preserve_or_setnull_is_value_preserving(
                (int)update[0], (string)update[1]));
        }

        private static void AssertOneCompilation(Action check)
        {
            var before = GeneratorTestHarness.CompilationsOnThisThread;
            check();
            Assert.Equal(1, GeneratorTestHarness.CompilationsOnThisThread - before);
        }
    }
}
