// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     THE SELF-PARSE INVARIANT, and the proof that it is switched on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="GeneratorTestHarness.AssertGeneratedSourceParses" /> carries the invariant and the
    ///         measurement that decided where it lives. This file answers the two questions the invariant cannot
    ///         answer about itself: does it actually FIRE on unparseable output, and is it actually WIRED into
    ///         every path that drives a generator — a check that is never called and a check that never fails are
    ///         indistinguishable from the outside, and this round has now recorded five instruments cited for
    ///         coverage they did not have.
    ///     </para>
    /// </remarks>
    public class GeneratedSourceParsesTests
    {
        /// <summary>
        ///     How many harness methods drive a generator. Pinned so a new one is a deliberate act that shows up
        ///     in review, on the precedent <c>stryker-config.codefixes.json</c> already states for its own
        ///     four-project list: "adding a fifth is a deliberate act that shows up in review".
        /// </summary>
        private const int ExpectedDriverMethods = 7;

        /// <summary>
        ///     Harness methods that drive a generator. Each MUST call the invariant, or every test that goes
        ///     through it is exempt from the strongest guarantee this project makes about its own output.
        /// </summary>
        private static readonly string[] DriverMethods =
        [
            nameof(GeneratorTestHarness.Run),
            nameof(GeneratorTestHarness.RunAll),
            nameof(GeneratorTestHarness.RunMapToWithSource),
            nameof(GeneratorTestHarness.RunAndGetSource),
            nameof(GeneratorTestHarness.RunAndGetCompilationErrors),
            nameof(GeneratorTestHarness.GeneratedCodeWarnings),
            nameof(GeneratorTestHarness.EmitAssembly)
        ];

        /// <summary>
        ///     The check FIRES on output that does not parse — driven with a hand-broken source rather than by
        ///     un-fixing the generator, so the RED is reproducible without a revert.
        /// </summary>
        /// <remarks>
        ///     The text is the shape <c>b888cc3</c> shipped: an unescaped keyword where an identifier belongs.
        ///     A compilation is built around it whose tree carries a <c>.g.cs</c> path, because that suffix is
        ///     the harness's own discriminator for "the generator wrote this" — a fixture without it would pass
        ///     for reasons that have nothing to do with the check.
        /// </remarks>
        [Fact]
        public void The_invariant_fails_on_source_that_does_not_parse()
        {
            const string broken = """
                                  namespace Demo;
                                  public partial class M
                                  {
                                      public global::Demo.Dst Map(global::Demo.Src src)
                                      {
                                          return new global::Demo.Dst { class = src.class, };
                                      }
                                  }
                                  """;

            var compilation = CSharpCompilation.Create("SelfParseRedAsm",
                [CSharpSyntaxTree.ParseText(broken, path: "Demo.M.g.cs")],
                GeneratorTestHarness.ReferenceSet);

            var thrown = Record.Exception(() => GeneratorTestHarness.AssertGeneratedSourceParses(compilation));

            Assert.NotNull(thrown);
            Assert.Contains("DOES NOT PARSE", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("Demo.M.g.cs", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The check PASSES on output that parses — so the assertion above is measuring the source and not
        ///     an unconditional throw.
        /// </summary>
        [Fact]
        public void The_invariant_passes_on_source_that_parses()
        {
            var compilation = CSharpCompilation.Create("SelfParseGreenAsm",
                [CSharpSyntaxTree.ParseText("namespace Demo { public partial class M { } }", path: "Demo.M.g.cs")],
                GeneratorTestHarness.ReferenceSet);

            Assert.Null(Record.Exception(() => GeneratorTestHarness.AssertGeneratedSourceParses(compilation)));
        }

        /// <summary>
        ///     It looks only at GENERATED trees. A test fixture is allowed to be broken C# — plenty deliberately
        ///     are — and attributing the consumer's own syntax error to the generator would make the invariant
        ///     fire on sources it has nothing to say about.
        /// </summary>
        [Fact]
        public void The_invariant_ignores_the_users_own_unparseable_source()
        {
            var compilation = CSharpCompilation.Create("SelfParseUserAsm",
                [CSharpSyntaxTree.ParseText("public class Broken { int class; }")],
                GeneratorTestHarness.ReferenceSet);

            Assert.Null(Record.Exception(() => GeneratorTestHarness.AssertGeneratedSourceParses(compilation)));
        }

        /// <summary>
        ///     Every harness method that drives a generator calls the invariant.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Read out of the harness SOURCE rather than by running it, because "did this call happen" is
        ///         not observable from a green run: the check is silent on correct output, so a deleted call
        ///         looks exactly like a passing one.
        ///     </para>
        ///     <para>
        ///         The population is COMPUTED — every method body containing a
        ///         <c>RunGeneratorsAndUpdateCompilation</c> call — and then compared against a pinned list, so a
        ///         new driver method fails this test until it is both guarded and declared. A pinned list on its
        ///         own would go stale silently; a computation on its own would accept a newly-added unguarded
        ///         method the moment someone guarded it, without anyone having looked.
        ///     </para>
        /// </remarks>
        [Fact]
        public void Every_harness_method_that_drives_a_generator_asserts_the_invariant()
        {
            var source = File.ReadAllText(Path.Combine(RepoPaths.Tests, "DwarfMapper.Generator.Tests", "GeneratorTestHarness.cs"));

            var found = new List<string>();
            var unguarded = new List<string>();

            foreach (var (name, body) in MethodBodies(source))
            {
                if (!body.Contains("RunGeneratorsAndUpdateCompilation(", StringComparison.Ordinal))
                {
                    continue;
                }

                found.Add(name);
                if (!body.Contains(nameof(GeneratorTestHarness.AssertGeneratedSourceParses) + "(", StringComparison.Ordinal))
                {
                    unguarded.Add(name);
                }
            }

            // Non-vacuity (B9): a scan that finds no driver at all would pass every assertion below while
            // guarding nothing. The count is pinned for the same reason the emitter inventory is.
            Assert.True(found.Count == ExpectedDriverMethods,
                $"Expected {ExpectedDriverMethods} harness method(s) that drive a generator, found {found.Count}:\n  " +
                string.Join("\n  ", found) +
                "\n\nA new one must be guarded by " + nameof(GeneratorTestHarness.AssertGeneratedSourceParses) +
                " and added to DriverMethods before this count moves.");

            Assert.True(unguarded.Count == 0,
                "Harness method(s) that drive a generator without asserting the self-parse invariant:\n  " +
                string.Join("\n  ", unguarded) +
                $"\n\nAdd {nameof(GeneratorTestHarness.AssertGeneratedSourceParses)} immediately after the " +
                "RunGeneratorsAndUpdateCompilation call. Every test that goes through an unguarded method is " +
                "exempt from the strongest guarantee this project makes about its own output.");

            Assert.Equal(DriverMethods.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                found.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList());
        }

        /// <summary>
        ///     Every method declaration in <paramref name="source" />, as (name, body text).
        /// </summary>
        /// <remarks>
        ///     ASKED OF THE COMPILER, not of a regular expression, and both alternatives were tried before this
        ///     one stuck. Searching for the method NAME found <c>RunMapToWithSource</c> inside <c>RunMapTo</c>'s
        ///     one-line body and read an empty region; a regex anchored on the declaration keyword could not
        ///     cross the parenthesis in a tuple RETURN TYPE — <c>public static (ImmutableArray&lt;Diagnostic&gt;
        ///     …) Run(</c> — so it silently skipped the very methods it existed to find and named
        ///     <c>BuildReferences</c> instead. This project has a Roslyn dependency; a scan over C# that guesses
        ///     at C# syntax is a choice, and the wrong one.
        /// </remarks>
        private static IEnumerable<(string Name, string Body)> MethodBodies(string source)
        {
            return CSharpSyntaxTree.ParseText(source).GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Select(m => (m.Identifier.ValueText, m.ToString()));
        }
    }
}
