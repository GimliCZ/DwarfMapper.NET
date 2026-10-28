// SPDX-License-Identifier: GPL-2.0-only

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
    /// </summary>
    public class CompileOnceTests
    {
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

        private static void AssertOneCompilation(Action check)
        {
            var before = GeneratorTestHarness.CompilationsOnThisThread;
            check();
            Assert.Equal(1, GeneratorTestHarness.CompilationsOnThisThread - before);
        }
    }
}
