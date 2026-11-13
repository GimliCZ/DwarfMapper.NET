// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.CompilerTests.TypeGraphs;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     <b>A sample is analysed once.</b>
    ///     <para>
    ///         Round 34 hill-climb, round 1. <see cref="CompilerTestHarness.RunAndEmit" /> asked Roslyn for the whole
    ///         compilation's diagnostics and then emitted it, binding every method body twice, and 3,300 of the ~4,550
    ///         sample compilations in a CompilerTests run take that path (K1's oracle, the projection agreement and all
    ///         three metamorphic relations). Measured in the four-CPU container: 14.8 ms per sample, 5.3 of it the
    ///         diagnostics pass. These hold both seams to one pass.
    ///     </para>
    /// </summary>
    public class SingleAnalysisPassTests
    {
        [Fact]
        public void RunAndEmit_checks_and_emits_an_accepted_sample_in_one_pass()
        {
            AssertOnePass(() => Assert.NotNull(CompilerTestHarness.RunAndEmit(Units("A11-representation-mirror")).Assembly));
        }

        [Fact]
        public void RunAndEmit_reports_a_refused_sample_in_one_pass()
        {
            AssertOnePass(() =>
            {
                var (result, assembly) = CompilerTestHarness.RunAndEmit(
                    TypeGraphRenderer.Render(PinnedCorpus.UnmappedDestinationMemberRefusal.Graph));
                Assert.True(result.RefusedLoudly);
                Assert.Null(assembly);
            });
        }

        [Fact]
        public void Run_checks_a_sample_in_one_pass()
        {
            AssertOnePass(() => Assert.Empty(CompilerTestHarness.Run(Units("A11-representation-mirror")).CompilationErrors));
        }

        /// <summary>
        ///     The one pass misses nothing the two passes saw: the emit's errors equal <c>GetDiagnostics</c>' errors, id
        ///     and location, on inputs that HAVE errors of every origin — a refusal's unimplemented partial method, a
        ///     declaration error, a method-body error, and a declaration error beside a body error (the bodies must still
        ///     be bound). Sampled runs cannot show this: 0 of 300 probed samples had an error at all.
        /// </summary>
        [Fact]
        public void The_emit_reports_exactly_the_errors_the_diagnostics_pass_reports()
        {
            const string declarationError = "namespace T { public class Probe_Twice { } public class Probe_Twice { } }";
            const string bodyError = "namespace T { public static class Probe_Body { public static int F() { int x = \"text\"; return x; } } }";
            var accepted = Units("A11-representation-mirror");
            (string Label, IReadOnlyList<string> Units)[] inputs =
            [
                ("a refused map", TypeGraphRenderer.Render(PinnedCorpus.UnmappedDestinationMemberRefusal.Graph)),
                ("a declaration error", [.. accepted, declarationError]),
                ("a method-body error", [.. accepted, bodyError]),
                ("a declaration error beside a method-body error", [.. accepted, declarationError, bodyError])
            ];

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (label, units) in inputs)
            {
                var (_, output) = CompilerTestHarness.Generate(units, "TypeGraphAsm");
                var diagnosed = Errors(output.GetDiagnostics());
                using var pe = new MemoryStream();
                var emitted = Errors(output.Emit(pe).Diagnostics);

                Assert.True(diagnosed.Length > 0, label + ": the input has no error, so it proves nothing");
                Assert.Equal(diagnosed, emitted);
                seen.UnionWith(diagnosed.Select(e => e[..e.IndexOf('@', StringComparison.Ordinal)]));
            }

            Assert.Superset(new HashSet<string>(["CS0101", "CS0029"], StringComparer.Ordinal), seen);
        }

        private static string[] Errors(IEnumerable<Diagnostic> diagnostics)
        {
            return diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Id + "@" + d.Location.GetLineSpan())
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<string> Units(string id)
        {
            return TypeGraphRenderer.Render(PinnedCorpus.Rows.Single(r => r.Id == id).Graph);
        }

        private static void AssertOnePass(Action check)
        {
            var before = CompilerTestHarness.AnalysisPassesOnThisThread;
            check();
            Assert.Equal(1, CompilerTestHarness.AnalysisPassesOnThisThread - before);
        }
    }
}
