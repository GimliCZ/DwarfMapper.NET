// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.TestInfrastructure;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     K0's own verification and the precursor of K1's leg 1: every sampled graph must either compile
///     SILENTLY CLEAN or be REFUSED LOUDLY by a generator. Generator silence plus CS errors in the output
///     is a seed-replayable red — the exact defect class the surface-matrix bug A11-F1 shipped as
///     (<c>[MapTo]</c>×struct: null-guarded value type, CS0037, never compiled until round 20).
///     <para>
///         The case set is PINNED (<see cref="PinnedSampling" />, I11): every run executes the same
///         deterministic list of graphs, so a red is replayable by case index rather than by luck, and the
///         assertion message carries the rendered source so the repro is pasteable. Any silent-CS-error
///         found here is a REAL FINDING: minimize it and pin the spec as a <see cref="PinnedCorpus" /> row
///         (and file an I-row if it is product-shaped) per house rules.
///     </para>
/// </summary>
public class TypeGraphSmokeTests
{
    private readonly ITestOutputHelper _output;

    public TypeGraphSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Sampled_graphs_compile_silently_clean_or_refuse_loudly()
    {
        var accepted = 0;
        var refused = 0;

        // Case count from the deep-tier catalog (fast = smoke, DWARF_DEEP=1 = the deep multiplier); the
        // loop is PinnedSampling's Parallel.For over [0, count) (H7: the variant is that index).
        PinnedSampling.Run(DeepPopulation.CompilerGraphSmokeSeeds, TypeGraphGen.MirroredPair(),
            graph => graph.Describe(), graph =>
        {
            var units = TypeGraphRenderer.Render(graph);
            var result = CompilerTestHarness.Run(units);

            if (result.RefusedLoudly)
            {
                // A loud refusal (error-severity DWARF/DWARFR diagnostic) is a VALID outcome: the grammar
                // deliberately reaches shapes the mapper documents as unsupported, and "says so with a
                // named diagnostic" is the contract. What it must never be is silent AND broken.
                Interlocked.Increment(ref refused);
                return;
            }

            Interlocked.Increment(ref accepted);
            Assert.True(result.CompilationErrors.Length == 0,
                "seed-replayable silent miscompilation: the generators were silent but the output has ["
                + string.Join(",", result.CompilationErrors.Select(e => e.Id).Distinct())
                + "]\n--- graph ---\n" + graph.Describe()
                + "\n--- units ---\n" + string.Join("\n--- next unit ---\n", units)
                + "\n--- generated ---\n" + result.GeneratedSource);
        }, _output, "K0 smoke");

        // Vacuity guard: the compile-clean leg only has teeth when the generator ACCEPTS. A loose floor,
        // not an exact pin — the accept/refuse split is reported, never gated: pinning it would ratchet
        // the GENERATOR's grammar (invariant R4's spirit — a gate names its oracle, and this one's oracle
        // is the sampled space, not the product).
        Assert.True(accepted > 0,
            $"the whole sample was refused ({refused} refusals, 0 accepts) — the sampled space has "
            + "drifted into the refusal grammar and the compile-clean invariant is vacuous.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sampled {accepted + refused}: {accepted} accepted (compiled clean), {refused} refused loudly"));
    }

    /// <summary>
    ///     The same contract over the <c>IQueryable</c> PROJECTION endpoint (round 23, I18): every sampled
    ///     graph whose mapper declares <c>Project</c> beside <c>Map</c> must compile silently clean or be
    ///     refused loudly by a generator.
    ///     <para>
    ///         <b>Why this exists.</b> Until this test, <c>grep -rl IQueryable</c> over the three
    ///         compiler-testing projects returned NOTHING — the renderer emitted <c>Map</c> methods only, so
    ///         K0's smoke, K1's oracle and all three of K2's metamorphic relations exercised a mapper
    ///         surface that structurally could not contain a projection. Both I14-family defects were found
    ///         by hand probes while <c>EmittedInvalidCodeCellCeiling</c> read 0 throughout, and one of them
    ///         was exactly this test's genre: <c>Src{Nested? N}</c> → <c>Dst{NestedStruct N}</c> emitted
    ///         <c>__s.N == null ? null : new NestedStruct{…}</c>, a silent CS0037 in a file the consumer
    ///         cannot edit, reported by nothing. The arc's headline claim — that the sampled space finds
    ///         what review does not — was true only of the space it sampled, and this was the largest
    ///         declared endpoint outside it.
    ///     </para>
    ///     <para>
    ///         <b>Its own population, not a widened one.</b> See <c>TypeGraphRenderer.Render</c>'s
    ///         <c>withProjection</c> remarks: the projection refuses a strictly wider grammar than
    ///         <c>Map</c>, and an error-severity refusal makes the whole run <c>RefusedLoudly</c>, so
    ///         folding <c>Project</c> into <see cref="DeepPopulation.CompilerGraphSmokeSeeds" /> would have
    ///         flipped accepted cases to refused and silently stopped compile-checking them.
    ///     </para>
    ///     <para>
    ///         <b>What a refusal means here, post-I14.</b> A projection that cannot be expressed as an
    ///         expression tree is refused with an error-severity DWARF028 and its method is DROPPED, which
    ///         leaves the partial declaration unimplemented — CS8795. That CS error is a CONSEQUENCE of the
    ///         refusal, not a silent miscompilation, and <c>RefusedLoudly</c> short-circuits before the
    ///         compile-clean leg ever sees it. The invariant is therefore unchanged from the <c>Map</c>
    ///         leg: silence plus CS errors is the red, and only that.
    ///     </para>
    /// </summary>
    [Fact]
    public void Sampled_graphs_with_a_projection_compile_silently_clean_or_refuse_loudly()
    {
        var accepted = 0;
        var refused = 0;

        PinnedSampling.Run(DeepPopulation.CompilerProjectionSmokeSeeds, TypeGraphGen.MirroredPair(),
            graph => graph.Describe(), graph =>
        {
            var units = TypeGraphRenderer.Render(graph, withProjection: true);
            var result = CompilerTestHarness.Run(units);

            if (result.RefusedLoudly)
            {
                Interlocked.Increment(ref refused);
                return;
            }

            Interlocked.Increment(ref accepted);
            Assert.True(result.CompilationErrors.Length == 0,
                "seed-replayable silent miscompilation THROUGH THE PROJECTION ENDPOINT: the generators were "
                + "silent but the output has ["
                // Messages, not bare ids: the first red this leg ever produced was a CS1069, whose ID
                // alone reads as a product defect and whose MESSAGE says "forwarded to another assembly"
                // — a missing metadata reference in the harness. An id list cannot tell those apart.
                + string.Join("; ", result.CompilationErrors
                    .Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture)).Distinct(StringComparer.Ordinal))
                + "]\n--- graph ---\n" + graph.Describe()
                + "\n--- units ---\n" + string.Join("\n--- next unit ---\n", units)
                + "\n--- generated ---\n" + result.GeneratedSource);
        }, _output, "I18 projection smoke");

        // Same loose floor as the Map leg, and it carries MORE weight here: the projection's refusal
        // grammar is wide enough that "everything refused" is a plausible outcome of a future tightening,
        // and it would make the compile-clean invariant vacuous without failing anything. Reported, never
        // pinned — pinning the split would ratchet the GENERATOR's grammar (invariant R4's spirit).
        Assert.True(accepted > 0,
            $"every sampled projection was refused ({refused} refusals, 0 accepts) — the projection's "
            + "refusal grammar now covers the whole sampled space and this leg proves nothing.");
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"projection sampled {accepted + refused}: {accepted} accepted (compiled clean), "
            + $"{refused} refused loudly"));
    }
}
