// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.NegativeCases
{
    /// <summary>
    ///     Which suppression pathway actually silences a DwarfMapper diagnostic — asserted, not assumed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every DWARF id is reported by an <c>IIncrementalGenerator</c> through
    ///         <c>SourceProductionContext.ReportDiagnostic</c>. Roslyn does not route generator diagnostics
    ///         through the analyzer diagnostic pipeline, so the mechanisms a reader reaches for first —
    ///         <c>#pragma warning disable</c> and an editorconfig <c>dotnet_diagnostic.X.severity</c> — have no
    ///         effect on them. Only compilation-level <c>NoWarn</c> does.
    ///     </para>
    ///     <para>
    ///         That is not a footnote. It is the difference between a diagnostic whose message can be acted on
    ///         and one whose message sends the reader to a no-op: DWARF076 told people to "suppress DWARF076
    ///         here", which is a per-site pragma, which does nothing. A consumer applied it, saw the warning
    ///         survive, and reasonably concluded the generator was broken. Nothing in this suite could have
    ///         caught that, because every other test asks "does the diagnostic fire" and none asks "can the
    ///         reader make it stop".
    ///     </para>
    ///     <para>
    ///         So these tests pin the pathway itself. If Roslyn ever starts honouring pragmas for generator
    ///         diagnostics, <see cref="A_pragma_does_NOT_suppress_a_generator_diagnostic" /> fails — and that
    ///         failure is the signal to reword every message that currently steers around the limitation.
    ///     </para>
    /// </remarks>
    public class SuppressionPathwayTests
    {
        private const string SelfMapSource = """
                                             using DwarfMapper;
                                             namespace Demo;
                                             public class Thing { public int X { get; set; } }
                                             [DwarfMapper]
                                             public partial class M
                                             {
                                                 public partial Thing CloneThing(Thing source);
                                             }
                                             """;

        // The same shape with the remedy a reader would try first. Deliberately the WRONG mechanism: this is
        // the control that proves the pragma is inert rather than that the fixture stopped firing.
        private const string SelfMapWithPragma = """
                                                 using DwarfMapper;
                                                 namespace Demo;
                                                 public class Thing { public int X { get; set; } }
                                                 [DwarfMapper]
                                                 public partial class M
                                                 {
                                                 #pragma warning disable DWARF076
                                                     public partial Thing CloneThing(Thing source);
                                                 #pragma warning restore DWARF076
                                                 }
                                                 """;

        private static ImmutableArray<Diagnostic> Run(string source, params string[] noWarn)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable);

            if (noWarn.Length > 0)
            {
                options = options.WithSpecificDiagnosticOptions(
                    noWarn.ToDictionary(id => id, _ => ReportDiagnostic.Suppress, StringComparer.Ordinal));
            }

            var compilation = CSharpCompilation.Create(
                "SuppressionPathwayAsm",
                [CSharpSyntaxTree.ParseText(source, parseOptions)],
                CaseDriver.Refs.Value,
                options);

            var driver = CSharpGeneratorDriver.Create(
                [new Generator.DwarfGenerator().AsSourceGenerator()],
                parseOptions: parseOptions);

            var ran = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

            // A generator that THREW reports nothing, which would make every assertion below pass for the
            // wrong reason. Roslyn parks the exception rather than letting it escape, so it has to be read.
            foreach (var result in ran.GetRunResult().Results)
            {
                if (result.Exception is { } ex)
                {
                    throw new InvalidOperationException(
                        $"Generator threw {ex.GetType().Name}: {ex.Message}", ex);
                }
            }

            return diagnostics;
        }

        [Fact]
        public void The_fixture_really_does_report_DWARF076()
        {
            // Non-vacuity: without this, a fixture that stopped firing would make both suppression tests below
            // "pass" — one by finding nothing suppressed, the other by finding nothing at all.
            Assert.Contains(Run(SelfMapSource), d => d.Id == "DWARF076");
        }

        [Fact]
        public void NoWarn_suppresses_a_generator_diagnostic()
        {
            Assert.DoesNotContain(Run(SelfMapSource, "DWARF076"), d => d.Id == "DWARF076");
        }

        [Fact]
        public void A_pragma_does_NOT_suppress_a_generator_diagnostic()
        {
            // Documents a Roslyn limitation, not a wish. If this ever fails because the pragma started working,
            // that is good news and the trigger to reword the messages that currently route around it —
            // starting with DWARF076, which names <NoWarn> precisely because this is true today.
            Assert.Contains(Run(SelfMapWithPragma), d => d.Id == "DWARF076");
        }

        [Fact]
        public void Suppressing_one_id_leaves_the_others_reporting()
        {
            // NoWarn is per-id, so a project acknowledging one advisory keeps its cover for everything else.
            // Worth pinning: the alternative a reader might reach for is blanket-disabling the category.
            var diagnostics = Run(SelfMapSource, "DWARF055");

            Assert.Contains(diagnostics, d => d.Id == "DWARF076");
        }
    }
}
