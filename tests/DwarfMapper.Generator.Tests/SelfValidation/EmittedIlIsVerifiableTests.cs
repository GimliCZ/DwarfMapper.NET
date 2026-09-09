// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Every method DwarfMapper emits must be verifiable IL, with no exceptions list.</b>
    ///     <para>
    ///         The ILVerify housekeeping stage runs over the SHIPPED runtime and over the Gallery. The Gallery is
    ///         a documentation corpus that also contains hand-written consumer code — a <c>stackalloc</c> demo
    ///         among it — so its findings need an excuse list, and an excuse list is exactly the thing that can
    ///         quietly grow to cover an emitted construct. That is not a hypothetical: on 2026-09-09 an entry was
    ///         added to it for <c>InlineArrayAsSpan</c>, which turned out to be caused by generated code.
    ///     </para>
    ///     <para>
    ///         So the rule is enforced here instead, over a corpus built for the purpose: the golden feature
    ///         corpus, which the repository ALREADY gates for completeness (<c>GoldenFeatureCoverageTests</c>
    ///         requires a case per feature and proves each case's output contains that feature's marker).
    ///         Reusing it means this test cannot fall behind the feature surface — a new feature that ships
    ///         without a golden case fails there, and one that ships with a case is verified here the same day.
    ///     </para>
    ///     <para>
    ///         Every case is compiled into ONE assembly, each in its own namespace, and that assembly is
    ///         verified with <b>zero permitted findings</b>. The corpus sources are declarations and partial
    ///         method signatures; the bodies are the generator's. So a finding here is ours by construction, and
    ///         there is nothing to excuse.
    ///     </para>
    ///     <para>
    ///         Prerequisite: <c>ilverify</c> on PATH, the same prerequisite the housekeeping stage has. A machine
    ///         that cannot verify the IL cannot make this claim either, so absence is a loud failure, not a skip.
    ///     </para>
    /// </summary>
    public class EmittedIlIsVerifiableTests
    {
        private static readonly Regex IlError = new(@"\[IL\]:\s*Error", RegexOptions.Compiled);

        // Every case declares `namespace Demo`, so they collide when compiled together. Rewriting the
        // declaration is enough because each case is self-contained and refers to its own types unqualified.
        private static readonly Regex DemoNamespace = new(@"^namespace\s+Demo\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

        [Fact]
        public void Every_method_the_generator_emits_over_the_golden_corpus_is_verifiable()
        {
            var cases = GoldenCorpus.Cases().Where(c => c.Id.StartsWith("feat:", StringComparison.Ordinal)).ToList();

            // Non-vacuity, first: an empty corpus verifies perfectly. The floor is the feature-axis floor the
            // golden suite already enforces, restated here so a corpus that silently stopped producing cases
            // cannot make this test pass by having nothing to check.
            Assert.True(cases.Count >= 25,
                $"Only {cases.Count} feature case(s) came back from the golden corpus, expected at least 25. " +
                "A corpus that shrank to nothing verifies clean, which is the one way this test can lie.");

            var trees = new List<SyntaxTree>();
            var renamed = 0;
            foreach (var c in cases)
            {
                var ns = "Vc_" + c.Id["feat:".Length..];
                var source = DemoNamespace.Replace(c.Source, "namespace " + ns + ";", 1);
                if (!ReferenceEquals(source, c.Source) && source != c.Source)
                {
                    renamed++;
                }

                trees.Add(CSharpSyntaxTree.ParseText(source));
            }

            Assert.True(renamed == cases.Count,
                $"{cases.Count - renamed} case(s) did not carry the expected `namespace Demo;` declaration, so " +
                "they would have collided rather than being verified. The rewrite is how the corpus becomes one " +
                "assembly; a case it cannot rewrite is a case this test silently drops.");

            var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapperVerificationCorpus", trees);
            var dll = Path.Combine(Path.GetTempPath(), "dwarfmapper-ilcorpus-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                Emit(compilation, dll);
                var findings = RunIlVerify(dll).Where(l => IlError.IsMatch(l)).ToList();

                Assert.True(findings.Count == 0,
                    "Generated code produced unverifiable IL. There is no excuse list for this corpus: its only " +
                    "hand-written content is type declarations and partial method signatures, so every method " +
                    "body in the assembly is the generator's.\n  " + string.Join("\n  ", findings));
            }
            finally
            {
                if (File.Exists(dll)) { File.Delete(dll); }
            }
        }

        [Fact]
        public void The_corpus_really_reaches_ilverify_and_ilverify_really_reports()
        {
            // The control. The test above passes if ilverify is silent — and a tool that failed to load its
            // reference set, or that was handed an assembly with nothing in it, is also silent. This plants a
            // KNOWN-unverifiable construct (a `stackalloc`, the same construct the Gallery's excuse names) in an
            // otherwise identical pipeline and requires it to be reported. If this goes green while the sabotage
            // is present, the test above is measuring nothing.
            const string sabotage = """
                                    namespace Sabotage;
                                    public static class S
                                    {
                                        public static int Sum()
                                        {
                                            System.Span<int> b = stackalloc int[4];
                                            b[0] = 1;
                                            return b[0];
                                        }
                                    }
                                    """;

            var compilation = GeneratorTestHarness.BuildCompilation(
                "DwarfMapperVerificationCorpusControl",
                new[]
                {
                    CSharpSyntaxTree.ParseText(sabotage)
                });

            var dll = Path.Combine(Path.GetTempPath(), "dwarfmapper-ilcontrol-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                Emit(compilation, dll);
                var findings = RunIlVerify(dll).Where(l => IlError.IsMatch(l)).ToList();

                Assert.True(findings.Count > 0,
                    "ilverify reported nothing for an assembly containing a `stackalloc`, which is unverifiable " +
                    "by construction. The verification pipeline is not looking at what it claims to look at, so " +
                    "the clean result beside this one means nothing.");
            }
            finally
            {
                if (File.Exists(dll)) { File.Delete(dll); }
            }
        }

        private static void Emit(CSharpCompilation compilation, string path)
        {
            var driver = CSharpGeneratorDriver.Create(new DwarfMapper.Generator.DwarfGenerator(),
                                                      new DwarfMapper.Generator.Registry.MapToGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

            using var fs = File.Create(path);
            var result = output.Emit(fs);

            Assert.True(result.Success,
                "The verification corpus does not compile, so nothing was verified:\n  " +
                string.Join("\n  ",
                    result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                          .Select(d => d.ToString())
                          .Take(10)));
        }

        private static List<string> RunIlVerify(string dll)
        {
            // THE RUNTIME directory, not the ref pack. The housekeeping stage verifies assemblies the SDK built
            // against reference assemblies, so a ref pack resolves them; this corpus is compiled in-process
            // against the loaded runtime, so its assembly refs name System.Private.CoreLib — which no ref pack
            // contains. Handing ilverify the ref pack instead produced 30-odd
            // "Failed to load assembly 'System.Private.CoreLib'" lines, which LOOK like findings and are in
            // fact the tool not resolving anything: exactly the silent-instrument shape the control below
            // exists to catch.
            var psi = new ProcessStartInfo
            {
                FileName = "ilverify",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add("--system-module");
            psi.ArgumentList.Add("System.Private.CoreLib");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(Path.Combine(RuntimeDirectory(), "*.dll"));

            // And the shipped runtime plus its dependencies, which the corpus references.
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(typeof(DwarfMapperAttribute).Assembly.Location)!, "*.dll"));

            using var process = new Process();
            process.StartInfo = psi;
            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                Assert.Fail("ilverify is not on PATH (dotnet tool install -g dotnet-ilverify) — the claim that " +
                            "every emitted method is verifiable cannot be checked on this machine, and the " +
                            "housekeeping ILVerify stage has the same prerequisite. " + e.Message);
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(true);
                Assert.Fail("ilverify did not finish within 120 s over the verification corpus — killed.");
            }

            return (stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult())
                   .Split('\n')
                   .Select(l => l.TrimEnd('\r'))
                   .ToList();
        }

        /// <summary>
        ///     The directory of the runtime this test host is running on — the one holding
        ///     <c>System.Private.CoreLib.dll</c>. That is what the in-process compilation referenced, so it is
        ///     what ilverify has to resolve against.
        /// </summary>
        private static string RuntimeDirectory()
        {
            var dir = Path.GetDirectoryName(typeof(object).Assembly.Location);

            Assert.True(!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir!, "System.Private.CoreLib.dll")),
                "Could not locate the running runtime's directory (looked beside " +
                (typeof(object).Assembly.Location.Length == 0 ? "<single-file host>" : typeof(object).Assembly.Location) +
                "). Without it ilverify resolves nothing and reports load failures rather than a verdict.");

            return dir!;
        }
    }
}
