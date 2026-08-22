// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.TestInfrastructure;
using DwarfMapper.Testing;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

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
            Gen.Select(TypeGraphGen.MirroredPair(), Gen.Int[0, int.MaxValue - 1]),
            sample => sample.Item1.Describe() + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
            sample =>
            {
                var (graph, seed) = sample;
                var units = TypeGraphRenderer.Render(graph, withProjection: true);
                var (result, assembly) = CompilerTestHarness.RunAndEmit(units);

                // A loud refusal is a valid outcome and is the projection's DOCUMENTED answer for a shape
                // an expression tree cannot carry (DWARF028). Nothing to compare; not a red.
                if (result.RefusedLoudly)
                {
                    Interlocked.Increment(ref refused);
                    return;
                }

                Assert.True(result.CompilationErrors.Length == 0,
                    "seed-replayable silent miscompilation on the agreement leg's own samples: the "
                    + "generators were silent but the output has ["
                    + string.Join("; ", result.CompilationErrors
                        .Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))
                        .Distinct(StringComparer.Ordinal))
                    + "]\n--- graph ---\n" + graph.Describe()
                    + "\n--- generated ---\n" + result.GeneratedSource);
                Assert.NotNull(assembly);

                RunAgreementLeg(assembly, graph, seed, result.GeneratedSource);
                Interlocked.Increment(ref executed);
            }, _output, "I18 projection agreement");

        // Vacuity guard, same shape and same reasoning as the smoke's and K1's: the relation only has
        // teeth when projections EXECUTE. Loose floor, reported not pinned — the accept/refuse split is a
        // property of the generator's grammar, and pinning it would ratchet the product (invariant R4's
        // spirit). Note the floor is DETERMINISTIC here, not flaky: the pinned case set either contains
        // executable cases or it never will.
        Assert.True(executed > 0,
            $"no sampled graph's projection executed ({refused} refusals, 0 executions) — the projection's "
            + "refusal grammar now covers the whole sampled space and this relation is vacuous.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"projection agreement sampled {executed + refused}: {executed} executed and compared, "
            + $"{refused} refused loudly"));
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

        var units = TypeGraphRenderer.Render(row.Graph, withProjection: true);
        var (result, assembly) = CompilerTestHarness.RunAndEmit(units);

        if (result.RefusedLoudly)
        {
            // Reported, not silently passed: a reader must be able to see WHICH anchors actually ran.
            _output.WriteLine($"'{id}': projection refused ["
                              + string.Join(",", result.GeneratorDiagnostics
                                  .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                                  .Select(d => d.Id).Distinct(StringComparer.Ordinal))
                              + "] — nothing to compare at this endpoint, which is a documented outcome.");
            return;
        }

        Assert.True(result.CompilationErrors.Length == 0,
            $"'{id}' compiles for Map but not with a projection: ["
            + string.Join("; ", result.CompilationErrors
                .Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.Ordinal))
            + "]\n--- generated ---\n" + result.GeneratedSource);
        Assert.NotNull(assembly);

        RunAgreementLeg(assembly, row.Graph, seed: 12345, result.GeneratedSource);
    }

    /// <summary>
    ///     <b>I19, found by this leg's very first run and PINNED AS THE DIVERGENCE IT IS.</b> A null source
    ///     collection maps to an EMPTY destination collection through <c>Map</c> — the documented
    ///     <c>NullCollections = AsEmpty</c> default (<c>docs/options.md</c>: <i>"Null source collection →
    ///     AsEmpty (never throws)"</i>) — and to <c>null</c> through <c>Project</c>, which never consults
    ///     the option at all.
    ///     <para>
    ///         <b>This test asserts the WRONG behaviour on purpose,</b> which needs saying out loud. It is
    ///         not a ratification: it is the alarm that makes the defect impossible to fix silently or to
    ///         forget. The moment <c>Project</c> starts honouring <c>NullCollections</c>, this test goes RED
    ///         and its message says what to do — delete it, delete the <c>nullCollections: false</c>
    ///         exclusion on the sampled leg above, and close I19. The same shape as the surface matrix's
    ///         recorded divergences, which also fail the build the moment they start working.
    ///     </para>
    ///     <para>
    ///         <b>Why it was filed and not fixed here:</b> I18's task is to extend the sampled space, and
    ///         the space immediately paid for itself. Changing a projection's null-collection semantics is
    ///         a product decision with a documentation surface (the <c>NullCollections</c> row asserts one
    ///         behaviour for both endpoints), and the I14 precedent — the same member answering differently
    ///         depending on which method the caller reached for, which I14's ruling called "the defect, not
    ///         the remedy" — is the argument that it should be fixed, in its own commit, with its own
    ///         ruling.
    ///     </para>
    /// </summary>
    [Fact]
    public void I19_a_null_source_collection_diverges_between_the_endpoints()
    {
        // Minimal shrink of the sampled red (case index 1, seed daA_s6TURop8): one collection member,
        // one scalar element type, both sides classes. Everything the sampled case carried beyond this —
        // record struct source, CtorParam shape, Guid elements, three extra nodes — was irrelevant.
        var graph = new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.List, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.List, null)],
                    BaseRef: null)
            ],
            "S0", "D0");

        var units = TypeGraphRenderer.Render(graph, withProjection: true);
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

        Assert.True(mappedMember is System.Collections.IEnumerable,
            "Map no longer materialises an empty collection from a null source — that IS the documented "
            + "NullCollections=AsEmpty default, and if it changed, the documentation changed with it or a "
            + "real regression just landed on the RUNTIME endpoint.");
        Assert.Empty(((System.Collections.IEnumerable)mappedMember!).Cast<object?>());

        Assert.True(projectedMember is null,
            "I19 IS FIXED: Project now produces something other than null for a null source collection. "
            + "That is the intended end state — so finish the job rather than adjusting this assertion: "
            + "(1) delete this whole test, (2) delete the `nullCollections: false` argument in "
            + "RunAgreementLeg so the sampled leg covers the shape again, (3) delete the exclusion "
            + "paragraph on ReflectionOracle.Populate's nullCollections parameter, and (4) flip I19 to "
            + "DONE in Issues/round20/TASKS.md.");
    }

    /// <summary>
    ///     Populates one source instance, runs it through BOTH endpoints, and structurally diffs the two
    ///     results with the SHIPPED <c>[RoundTrip]</c> differ — dogfooding <c>DwarfMapper.Testing</c>
    ///     rather than growing a parallel comparer, the same binding correction K1 follows.
    /// </summary>
    private static void RunAgreementLeg(
        System.Reflection.Assembly assembly, GraphSpec graph, int seed, string generatedSource)
    {
        var sourceType = assembly.GetType("T." + graph.RootSource)
                         ?? throw new InvalidOperationException(
                             $"no type T.{graph.RootSource} in emitted assembly");

        // ONE source instance through both endpoints. Populating twice from the same seed would be
        // equivalent only while the populator is deterministic, and making the relation depend on that
        // would test the populator instead of the product.
        //
        // nullCollections: false — the ONE named exclusion on this leg, and it is a filed defect, not a
        // convenience. A null source collection diverges between the endpoints BY CONSTRUCTION today:
        // Map emits the documented NullCollections=AsEmpty helper, Project emits `x == null ? null : …`.
        // That is I19, pinned deterministically below. Sampling it would red ~20 % of every collection
        // member on every run and drown every other disagreement this relation exists to find. Delete
        // this argument with the fix.
        var source = ReflectionOracle.Populate(sourceType, seed, nullCollections: false);

        var mapped = Invoke("Map", () => CompilerTestHarness.InvokeMap(assembly, source));
        var projected = Invoke("Project", () => CompilerTestHarness.InvokeProject(assembly, sourceType, source));

        // Map is the reference (K1 holds it to the naive oracle), so "expected" is Map's answer and the
        // diff reads as what the PROJECTION got wrong.
        var diffs = StructuralComparer.Diff(mapped, projected);
        Assert.True(diffs.Count == 0,
            "seed-replayable ENDPOINT DISAGREEMENT — Map and Project produced different results for the "
            + "same source (shrink, then classify: projection bug -> pinned corpus row + I-row; "
            + "documented endpoint split -> corpus row + documentation-shaped I-row; never absorb it):\n"
            + StructuralComparer.Render(diffs)
            + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
            + "\n--- graph ---\n" + graph.Describe()
            + "\n--- generated ---\n" + generatedSource);

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
                    $"generated {endpoint} THREW {e.GetType().Name}: {e.Message}\n"
                    + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
                    + "\n--- graph ---\n" + graph.Describe()
                    + "\n--- generated ---\n" + generatedSource, e);
            }
        }
    }
}
