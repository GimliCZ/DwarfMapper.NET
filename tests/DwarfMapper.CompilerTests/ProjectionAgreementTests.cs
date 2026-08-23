// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;
using Xunit.Abstractions;
using TypeKind = DwarfMapper.CompilerTests.TypeGraphs.TypeKind;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     I18's oracle leg — ENDPOINT AGREEMENT between <c>Project</c> and <c>Map</c> over the sampled type
    ///     graphs. The projection of a one-element queryable must equal the runtime map of that same element.
    ///     <para>
    ///         <b>Why this and not a second naive oracle.</b> K1 compares <c>Map</c> against
    ///         <see cref="ReflectionOracle" />'s deliberately naive copy. Pointing a second naive oracle at
    ///         <c>Project</c> would re-litigate every documented semantic the first one already arbitrates
    ///         (null collections, re-kinded pairs, the lot) and would answer the wrong question. The question
    ///         that matters at this endpoint is the one <c>AllEmitPathsAgreeFuzzTests</c> asks of the four
    ///         runtime emit paths: <b>do two ways of expressing the same mapping produce the same answer?</b>
    ///         <c>Map</c> is the reference because it is the endpoint K1 already holds to the naive oracle —
    ///         so agreement here transitively inherits that verdict, and a disagreement localises to the
    ///         projection rather than to a contested semantic.
    ///     </para>
    ///     <para>
    ///         <b>What K0's smoke cannot see, and this can.</b> The smoke leg proves a projection COMPILES.
    ///         This proves it is RIGHT. A projection that silently drops a member, or lifts a null the way
    ///         <c>Map</c> does not, compiles perfectly — and that is exactly the shape of the I14 family: the
    ///         four nullable cells that <c>Map</c> lifted and <c>Project</c> emitted NOTHING for were a
    ///         silent disagreement between these two endpoints, found by a hand probe.
    ///     </para>
    ///     <para>
    ///         <b>Stated limit, and it bounds every projection claim in this project (B19's rule).</b>
    ///         <c>Enumerable.AsQueryable</c> makes the provider LINQ-to-Objects, so enumerating the result
    ///         COMPILES AND EVALUATES the emitted expression tree. It does not prove that any database
    ///         provider TRANSLATES it. No ORM runs here, and nothing in this file should be read as claiming
    ///         one does.
    ///     </para>
    ///     <para>
    ///         <b>Differ depth arithmetic</b> is inherited unchanged from <see cref="DifferentialOracleTests" />
    ///         — same generator defaults (maxNodes = 4), so the longest dest path is 3 × 3 + 1 = 10, under
    ///         <c>StructuralComparer.MaxDepth</c> = 12. Raising maxNodes here would break it; re-derive first.
    ///     </para>
    /// </summary>
    public class ProjectionAgreementTests
    {
        private readonly ITestOutputHelper _output;

        public ProjectionAgreementTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void The_projection_of_one_element_agrees_with_the_runtime_map_of_that_element()
        {
            var executed = 0;
            var refused = 0;

            // Graph and population seed sampled TOGETHER so one pinned case index replays both — K1's shape,
            // and the reason this population's case set differs from the smoke leg's graphs-only draw.
            PinnedSampling.Run(DeepPopulation.CompilerProjectionAgreementSeeds,
                TypeGraphGen.MirroredPair().Select(Gen.Int[0, int.MaxValue - 1]),
                sample => sample.Item1.Describe() + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
                sample =>
                {
                    var (graph, seed) = sample;
                    var units = TypeGraphRenderer.Render(graph, true);
                    var (result, assembly) = CompilerTestHarness.RunAndEmit(units);

                    // A loud refusal is a valid outcome and is the projection's DOCUMENTED answer for a shape
                    // an expression tree cannot carry (DWARF028). Nothing to compare; not a red.
                    if (result.RefusedLoudly)
                    {
                        Interlocked.Increment(ref refused);
                        return;
                    }

                    Assert.True(result.CompilationErrors.Length == 0,
                        "seed-replayable silent miscompilation on the agreement leg's own samples: the " +
                        "generators were silent but the output has [" +
                        string.Join("; ",
                            result.CompilationErrors
                                .Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))
                                .Distinct(StringComparer.Ordinal)) +
                        "]\n--- graph ---\n" +
                        graph.Describe() +
                        "\n--- generated ---\n" +
                        result.GeneratedSource);
                    Assert.NotNull(assembly);

                    RunAgreementLeg(assembly, graph, seed, result.GeneratedSource);
                    Interlocked.Increment(ref executed);
                },
                _output,
                "I18 projection agreement");

            // Vacuity guard, same shape and same reasoning as the smoke's and K1's: the relation only has
            // teeth when projections EXECUTE. Loose floor, reported not pinned — the accept/refuse split is a
            // property of the generator's grammar, and pinning it would ratchet the product (invariant R4's
            // spirit). Note the floor is DETERMINISTIC here, not flaky: the pinned case set either contains
            // executable cases or it never will.
            Assert.True(executed > 0,
                $"no sampled graph's projection executed ({refused} refusals, 0 executions) — the projection's " + "refusal grammar now covers the whole sampled space and this relation is vacuous.");
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"projection agreement sampled {executed + refused}: {executed} executed and compared, " + $"{refused} refused loudly"));
        }

        /// <summary>
        ///     The deterministic anchor, mirroring
        ///     <c>DifferentialOracleTests.Accepted_pinned_corpus_rows_agree_with_the_naive_oracle</c>: the
        ///     accepted corpus rows run through endpoint agreement too, so this relation has executions whose
        ///     existence does not depend on where the sampled split happens to fall. A row whose PROJECTION is
        ///     refused is skipped and SAYS so — the projection's refusal grammar is wider than <c>Map</c>'s,
        ///     and a row being an accepted <c>Map</c> row does not make it an accepted <c>Project</c> row.
        /// </summary>
        [Theory]
        [InlineData("P5-K0-partial-split-struct-pair")]
        [InlineData("A11-representation-mirror")]
        public void Accepted_pinned_corpus_rows_agree_across_the_two_endpoints(string id)
        {
            var row = PinnedCorpus.Rows.Single(r => r.Id == id);
            Assert.True(row.KnownSilentCsIds is null && row.ExpectedRefusalIds is null,
                $"row '{id}' is not an accepted row any more — move this anchor deliberately");

            var units = TypeGraphRenderer.Render(row.Graph, true);
            var (result, assembly) = CompilerTestHarness.RunAndEmit(units);

            if (result.RefusedLoudly)
            {
                // Reported, not silently passed: a reader must be able to see WHICH anchors actually ran.
                _output.WriteLine($"'{id}': projection refused [" +
                                  string.Join(",",
                                      result.GeneratorDiagnostics
                                          .Where(d => d.Severity == DiagnosticSeverity.Error)
                                          .Select(d => d.Id).Distinct(StringComparer.Ordinal)) +
                                  "] — nothing to compare at this endpoint, which is a documented outcome.");
                return;
            }

            Assert.True(result.CompilationErrors.Length == 0,
                $"'{id}' compiles for Map but not with a projection: [" +
                string.Join("; ",
                    result.CompilationErrors
                        .Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))
                        .Distinct(StringComparer.Ordinal)) +
                "]\n--- generated ---\n" +
                result.GeneratedSource);
            Assert.NotNull(assembly);

            RunAgreementLeg(assembly, row.Graph, 12345, result.GeneratedSource);
        }

        /// <summary>
        ///     <b>I19, FIXED — and this is the pin that replaced the alarm.</b> A null source collection
        ///     materialises an EMPTY destination collection through <c>Project</c>, exactly as it does through
        ///     <c>Map</c>: the documented <c>NullCollections = AsEmpty</c> default (<c>docs/options.md</c>:
        ///     <i>"Null source collection → AsEmpty (never throws)"</i>), which the projection endpoint used to
        ///     not read at all.
        ///     <para>
        ///         The predecessor of this test asserted the WRONG behaviour on purpose — the alarm that made
        ///         the defect impossible to fix silently. It went red the moment the option was honoured, and
        ///         its message carried the four-step cleanup: this pin, the deleted <c>nullCollections: false</c>
        ///         argument in <c>RunAgreementLeg</c>, the deleted exclusion on <c>ReflectionOracle.Populate</c>
        ///         (the parameter itself is gone, not just its documentation — no caller passed false any more,
        ///         and a dead population axis is a lie about coverage), and the I19 row.
        ///     </para>
        ///     <para>
        ///         Kept as a DETERMINISTIC pin rather than left to the sampled leg above, which now covers the
        ///         shape again: sampling proves the endpoints AGREE, and agreement is satisfied by both being
        ///         wrong together. This one names the documented VALUE — empty, not null, on both sides.
        ///     </para>
        /// </summary>
        [Fact]
        public void I19_a_null_source_collection_materialises_empty_at_both_endpoints()
        {
            // The graph the sampled red shrank to (case index 1, seed daA_s6TURop8): one collection member,
            // one scalar element type, both sides classes. Everything the sampled case carried beyond this —
            // record struct source, CtorParam shape, Guid elements, three extra nodes — was irrelevant.
            var graph = new GraphSpec(
                [
                    new NodeSpec("S0",
                        TypeKind.Class,
                        [new MemberSpec("M0_0", "int", false, MemberShape.AutoProp, CollShape.List, null)],
                        null),
                    new NodeSpec("D0",
                        TypeKind.Class,
                        [new MemberSpec("M0_0", "int", false, MemberShape.AutoProp, CollShape.List, null)],
                        null)
                ],
                "S0",
                "D0");

            var units = TypeGraphRenderer.Render(graph, true);
            var (result, assembly) = CompilerTestHarness.RunAndEmit(units);
            Assert.False(result.RefusedLoudly);
            Assert.True(result.CompilationErrors.Length == 0);
            Assert.NotNull(assembly);

            var sourceType = assembly.GetType("T.S0")!;
            var source = Activator.CreateInstance(sourceType)!;
            sourceType.GetProperty("M0_0")!.SetValue(source, null); // the whole input: a NULL collection

            var mapped = CompilerTestHarness.InvokeMap(assembly, source);
            var projected = CompilerTestHarness.InvokeProject(assembly, sourceType, source);

            var mappedMember = mapped!.GetType().GetProperty("M0_0")!.GetValue(mapped);
            var projectedMember = projected!.GetType().GetProperty("M0_0")!.GetValue(projected);

            Assert.True(mappedMember is IEnumerable,
                "Map no longer materialises an empty collection from a null source — that IS the documented " + "NullCollections=AsEmpty default, and if it changed, the documentation changed with it or a " + "real regression just landed on the RUNTIME endpoint.");
            Assert.Empty(((IEnumerable)mappedMember).Cast<object?>());

            Assert.True(projectedMember is IEnumerable,
                "I19 HAS REGRESSED: Project produced " + (projectedMember?.ToString() ?? "null") + " for a null " + "source collection instead of the documented empty one. The projection resolver reads " + "NullCollections at MapperExtractor.Projection's collection branch; if that read was removed " + "or bypassed, the two endpoints disagree about the same member again.");
            Assert.Empty(((IEnumerable)projectedMember!).Cast<object?>());
        }

        /// <summary>
        ///     Populates one source instance, runs it through BOTH endpoints, and structurally diffs the two
        ///     results with the SHIPPED <c>[RoundTrip]</c> differ — dogfooding <c>DwarfMapper.Testing</c>
        ///     rather than growing a parallel comparer, the same binding correction K1 follows.
        /// </summary>
        private static void RunAgreementLeg(
            Assembly assembly,
            GraphSpec graph,
            int seed,
            string generatedSource)
        {
            var sourceType = assembly.GetType("T." + graph.RootSource) ??
                             throw new InvalidOperationException(
                                 $"no type T.{graph.RootSource} in emitted assembly");

            // ONE source instance through both endpoints. Populating twice from the same seed would be
            // equivalent only while the populator is deterministic, and making the relation depend on that
            // would test the populator instead of the product.
            //
            // I19 CLOSED, and with it this leg's ONLY population exclusion. A null source collection used to
            // diverge between the endpoints by construction — Map materialised the documented
            // NullCollections=AsEmpty default, Project emitted `x == null ? null : …` and never read the
            // option — so the axis was excluded here (`nullCollections: false`) rather than reddening ~20 % of
            // every collection member forever. The projection reads NullCollections now, the exclusion is
            // gone from the call AND from ReflectionOracle.Populate's parameter list, and this leg samples
            // null collections like every other population axis.
            var source = ReflectionOracle.Populate(sourceType, seed);

            var mapped = Invoke("Map", () => CompilerTestHarness.InvokeMap(assembly, source));
            var projected = Invoke("Project", () => CompilerTestHarness.InvokeProject(assembly, sourceType, source));

            // Map is the reference (K1 holds it to the naive oracle), so "expected" is Map's answer and the
            // diff reads as what the PROJECTION got wrong.
            var diffs = StructuralComparer.Diff(mapped, projected);
            Assert.True(diffs.Count == 0,
                "seed-replayable ENDPOINT DISAGREEMENT — Map and Project produced different results for the " +
                "same source (shrink, then classify: projection bug -> pinned corpus row + I-row; " +
                "documented endpoint split -> corpus row + documentation-shaped I-row; never absorb it):\n" +
                StructuralComparer.Render(diffs) +
                "--- population seed ---\n" +
                seed.ToString(CultureInfo.InvariantCulture) +
                "\n--- graph ---\n" +
                graph.Describe() +
                "\n--- generated ---\n" +
                generatedSource);

            object? Invoke(string endpoint, Func<object?> call)
            {
                try
                {
                    return call();
                }
                catch (Exception e)
                {
                    // A throw from either endpoint is a runtime-behaviour fact about the product on this
                    // input — as much a differential outcome as a wrong value — and it must arrive with the
                    // full repro so the sample is classifiable without a re-run. Naming WHICH endpoint threw
                    // is the whole diagnostic value here: "Map threw" and "Project threw" are different bugs.
                    throw new InvalidOperationException(
                        $"generated {endpoint} THREW {e.GetType().Name}: {e.Message}\n" + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture) + "\n--- graph ---\n" + graph.Describe() + "\n--- generated ---\n" + generatedSource,
                        e);
                }
            }
        }
    }
}
