// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.TestInfrastructure;
using DwarfMapper.Testing;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     K1 — the differential oracle over K0's generated graphs (R22-01), two legs per sampled graph.
///     <para>
///         <b>Leg 1 (must-compile-or-refuse):</b> a loud generator refusal is a valid outcome; generator
///         silence plus CS errors in the output is a seed-replayable red — the pinned-at-0
///         <c>EmittedInvalidCode</c> ratchet generalized from the surface matrix's 866 cells to the
///         generated space. The deterministic proof that the refusal branch executes at all is
///         <see cref="PinnedCorpus.UnmappedDestinationMemberRefusal" /> (K0 disclosed the branch had never
///         been taken).
///     </para>
///     <para>
///         <b>Leg 2 (the oracle):</b> DwarfMapper's executed map versus <see cref="ReflectionOracle" />'s
///         deliberately naive reflective copy, compared with the SHIPPED <c>[RoundTrip]</c> differ
///         (<see cref="StructuralComparer" /> — <c>RoundTrip.Verify</c>'s own member-path diff), dogfooding
///         the <c>DwarfMapper.Testing</c> package instead of growing a parallel comparer, per the audit's
///         binding correction. Any disagreement replays from its pinned case index
///         (<see cref="PinnedSampling" />, I11) and is then classified: generator bug → pinned corpus row
///         + I-row; undocumented semantic → corpus row + documentation-shaped I-row. Never ratified by
///         silently teaching the oracle.
///     </para>
///     <para>
///         <b>Differ depth arithmetic</b> (StructuralComparer.MaxDepth = 12 returns SILENTLY past the cap,
///         so the sampled space must fit under it): at the generator default of maxNodes = 4 per side, the
///         longest possible dest path is root → 3 nested hops, and each hop costs at most 3 levels
///         (member +1, collection index +1, KeyValuePair.Value +1 for a Dictionary edge) plus the leaf
///         member: 3 × 3 + 1 = 10 &lt; 12. Raising maxNodes for this leg would break that arithmetic —
///         re-derive it before touching the generator call below.
///     </para>
/// </summary>
public class DifferentialOracleTests
{
    private readonly ITestOutputHelper _output;

    public DifferentialOracleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Sampled_graphs_compile_or_refuse_and_the_executed_map_agrees_with_the_naive_oracle()
    {
        var executed = 0;
        var refused = 0;

        // Population seed sampled WITH the graph so one pinned case index replays both together.
        PinnedSampling.Run(DeepPopulation.CompilerOracleSeeds,
            Gen.Select(TypeGraphGen.MirroredPair(), Gen.Int[0, int.MaxValue - 1]),
            sample => sample.Item1.Describe()
                      + "|" + sample.Item2.ToString(CultureInfo.InvariantCulture),
            sample =>
            {
                var (graph, seed) = sample;
                var units = TypeGraphRenderer.Render(graph);
                var (result, assembly) = CompilerTestHarness.RunAndEmit(units);

                // Leg 1 — must compile or refuse loudly (K0's smoke contract, restated over this leg's
                // own samples because the emit path must also hold it before leg 2 may execute).
                if (result.RefusedLoudly)
                {
                    Interlocked.Increment(ref refused);
                    return;
                }

                Assert.True(result.CompilationErrors.Length == 0,
                    "seed-replayable silent miscompilation: the generators were silent but the output has ["
                    + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct())
                    + "]\n--- graph ---\n" + graph.Describe()
                    + "\n--- units ---\n" + string.Join("\n--- next unit ---\n", units)
                    + "\n--- generated ---\n" + result.GeneratedSource);
                Assert.NotNull(assembly);

                // Leg 2 — the differential oracle.
                RunOracleLeg(assembly, graph, seed, result.GeneratedSource);
                Interlocked.Increment(ref executed);
            }, _output, "K1 oracle");

        // Vacuity guard, mirroring the smoke's: the oracle only has teeth when maps EXECUTE. A loose
        // floor, not an exact pin — the accept/refuse split is a property of the sampled GRAMMAR, so it
        // is reported rather than gated; the product-side gate is the comparison itself.
        Assert.True(executed > 0,
            $"no sampled graph executed ({refused} refusals, 0 executions) — the sampled space has "
            + "drifted into the refusal grammar and the differential leg is vacuous.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sampled {executed + refused}: {executed} executed and compared, {refused} refused loudly"));
    }

    /// <summary>
    ///     The two ACCEPTED deterministic corpus rows run through the full oracle as well — a
    ///     seed-independent anchor: if the differential leg ever breaks on these, it broke on a shape whose
    ///     history is already written down. Divergence-pinned and refusal-pinned rows have nothing to
    ///     execute and are skipped by their own contracts.
    /// </summary>
    [Theory]
    [InlineData("P5-K0-partial-split-struct-pair")]
    [InlineData("A11-representation-mirror")]
    public void Accepted_pinned_corpus_rows_agree_with_the_naive_oracle(string id)
    {
        var row = PinnedCorpus.Rows.Single(r => r.Id == id);
        Assert.True(row.KnownSilentCsIds is null && row.ExpectedRefusalIds is null,
            $"row '{id}' is not an accepted row any more — move this anchor deliberately");

        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(row.Graph));
        Assert.False(result.RefusedLoudly);
        Assert.True(result.CompilationErrors.Length == 0);
        Assert.NotNull(assembly);

        RunOracleLeg(assembly, row.Graph, seed: 12345, result.GeneratedSource);
    }

    /// <summary>
    ///     The NullCollections switch's own deterministic regression pin, independent of sampling (the
    ///     sampled coverage rides on the populator's null-probability constants, which could drift): a
    ///     hand-forced NULL source collection must map to an EMPTY destination collection — the documented
    ///     default (docs/options.md, class-options row <c>NullCollections</c>: "Null source collection →
    ///     AsEmpty (never throws)") — on BOTH sides of the differential, and the naive no-switch answer
    ///     (null) must disagree, proving the switch is load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public void Null_source_collection_maps_to_empty_on_both_sides_per_the_documented_default()
    {
        var graph = new GraphSpec(
            [
                new NodeSpec("S0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.List, 1)],
                    BaseRef: null),
                new NodeSpec("S1", TypeKind.Class,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null),
                new NodeSpec("D0", TypeKind.Class,
                    [new MemberSpec("M0_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.List, 3)],
                    BaseRef: null),
                new NodeSpec("D1", TypeKind.Class,
                    [new MemberSpec("M1_0", "int", Nullable: false, MemberShape.AutoProp, CollShape.None, null)],
                    BaseRef: null)
            ],
            "S0", "D0");

        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(graph));
        Assert.False(result.RefusedLoudly);
        Assert.True(result.CompilationErrors.Length == 0);
        Assert.NotNull(assembly);

        var sourceType = assembly.GetType("T.S0")!;
        var source = Activator.CreateInstance(sourceType)!; // M0_0 stays NULL — the forced case
        var mapped = CompilerTestHarness.InvokeMap(assembly, source);

        // The product side: the documented default materializes an empty list.
        var destMember = assembly.GetType("T.D0")!.GetProperty("M0_0")!.GetValue(mapped);
        var asEnumerable = Assert.IsAssignableFrom<IEnumerable>(destMember);
        Assert.Empty(asEnumerable.Cast<object>());

        // The oracle side, through the switch: agreement, zero diffs.
        var expected = ReflectionOracle.NaiveMap(
            source, assembly.GetType("T.D0")!, ReflectionOracle.OracleOptions.DocumentedDefaults);
        Assert.Empty(StructuralComparer.Diff(expected, mapped));

        // And the switch is load-bearing: the naive AsNull answer disagrees with the product default.
        var naiveNull = ReflectionOracle.NaiveMap(
            source, assembly.GetType("T.D0")!,
            new ReflectionOracle.OracleOptions(NullCollectionStrategy.AsNull));
        Assert.NotEmpty(StructuralComparer.Diff(naiveNull, mapped));
    }

    /// <summary>
    ///     The I7 runtime-divergence pin (TASKS.md I7; found by this leg's FIRST 1,000-sample deep run,
    ///     seed 0vihQF5Vee7b, minimized by a 6-cell kind-pair probe): a plain nullable nested member
    ///     across a RE-KINDED pair throws on a null value instead of lifting null → null, in BOTH
    ///     directions, although the destination member is nullable-capable in both. The same-kind
    ///     diagonals lift correctly and stay covered by sampling. These assertions pin the CURRENT
    ///     behaviour exactly — they go red the moment the product lifts (or refuses) instead, and that red
    ///     is the signal to delete the matching I7 sampled-space exclusion in TypeGraphGen, flip these
    ///     expectations, and close the I-row in the same commit. Never a blanket skip; the oracle is NOT
    ///     taught this semantics because no documented sentence states it.
    /// </summary>
    [Theory]
    [InlineData("I7-nullable-rekind-value-to-reference", "Source member 'M0_0' was null")]
    [InlineData("I7-nullable-rekind-reference-to-value",
        "Cannot map a null 'global::T.S1' to value-type 'global::T.D1'.")]
    public void I7_pinned_runtime_divergence_null_across_rekinded_pair_throws(string id, string expectedMessage)
    {
        var row = PinnedCorpus.Rows.Single(r => r.Id == id);
        var (result, assembly) = CompilerTestHarness.RunAndEmit(TypeGraphRenderer.Render(row.Graph));
        Assert.False(result.RefusedLoudly, $"I7 row '{id}' now REFUSES — re-file, don't absorb");
        Assert.True(result.CompilationErrors.Length == 0, $"I7 row '{id}' no longer compiles clean");
        Assert.NotNull(assembly);

        var source = Activator.CreateInstance(assembly.GetType("T.S0")!)!; // M0_0 stays null — the pinned case
        var thrown = Assert.Throws<InvalidOperationException>(() => CompilerTestHarness.InvokeMap(assembly, source));
        Assert.Equal(expectedMessage, thrown.Message);
    }

    private static void RunOracleLeg(
        System.Reflection.Assembly assembly, GraphSpec graph, int seed, string generatedSource)
    {
        var sourceType = assembly.GetType("T." + graph.RootSource)
                         ?? throw new InvalidOperationException($"no type T.{graph.RootSource} in emitted assembly");
        var destType = assembly.GetType("T." + graph.RootDest)
                       ?? throw new InvalidOperationException($"no type T.{graph.RootDest} in emitted assembly");

        var source = ReflectionOracle.Populate(sourceType, seed);
        object? actual;
        try
        {
            actual = CompilerTestHarness.InvokeMap(assembly, source);
        }
        catch (Exception e)
        {
            // The generated map THREW — a runtime-behaviour fact about the product on this input, which is
            // as much a differential outcome as a wrong value. Rethrown with the full repro context so the
            // shrunk sample is classifiable (documented semantics vs finding) without re-running.
            throw new InvalidOperationException(
                $"generated map THREW {e.GetType().Name}: {e.Message}\n"
                + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
                + "\n--- graph ---\n" + graph.Describe()
                + "\n--- generated ---\n" + generatedSource, e);
        }

        var expected = ReflectionOracle.NaiveMap(
            source, destType, ReflectionOracle.OracleOptions.DocumentedDefaults);

        var diffs = StructuralComparer.Diff(expected, actual);
        Assert.True(diffs.Count == 0,
            "seed-replayable ORACLE DISAGREEMENT (shrink, then classify: generator bug -> pinned corpus "
            + "row + I-row; undocumented semantic -> corpus row + documentation-shaped I-row; never teach "
            + "the oracle silently):\n"
            + StructuralComparer.Render(diffs)
            + "--- population seed ---\n" + seed.ToString(CultureInfo.InvariantCulture)
            + "\n--- graph ---\n" + graph.Describe()
            + "\n--- generated ---\n" + generatedSource);
    }
}
