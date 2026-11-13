// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>An errors-only check keeps every error and skips the nullable walker.</b>
    ///     <para>
    ///         Round 34 (E8). The harness's checks that read only errors, and its emits, bind through
    ///         <see cref="GeneratorTestHarness.ForErrorsOnly" />, which switches Roslyn's nullable flow analysis off: the
    ///         walker reports warnings, never errors, and binding the generated code with it was three quarters of a
    ///         power-set variation's cost. One compilation here carries a binder error and a nullable warning; the
    ///         errors-only view must report the same errors and none of the walker's warnings, and the full view both.
    ///     </para>
    /// </summary>
    public class ErrorsOnlyCheckTests
    {
        private const string Source = """
                                      #nullable enable
                                      public class C
                                      {
                                          public string Maybe() { string? x = null; return x; }
                                          public int Wrong() { return "not an int"; }
                                      }
                                      """;

        [Fact]
        public void An_errors_only_check_keeps_every_error_and_drops_only_the_nullable_warnings()
        {
            var compilation = CSharpCompilation.Create("ErrorsOnlyCheck", [CSharpSyntaxTree.ParseText(Source)],
                GeneratorTestHarness.ReferenceSet, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var full = compilation.GetDiagnostics();
            var errorsOnly = GeneratorTestHarness.ForErrorsOnly(compilation).GetDiagnostics();

            // The control: with the walker on, the possible-null return is reported (CS8603), next to the error (CS0029).
            Assert.Contains(full, d => d.Id == "CS8603");
            Assert.Contains(full, d => d.Id == "CS0029");

            Assert.DoesNotContain(errorsOnly, d => d.Id == "CS8603");
            Assert.Equal(full.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.Location.GetLineSpan()),
                errorsOnly.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id + " " + d.Location.GetLineSpan()));
        }
    }
}
