// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.Globalization;
using CsCheck;
using DwarfMapper.CompilerTests.TypeGraphs;
using DwarfMapper.Generator;
using DwarfMapper.Generator.Registry;
using DwarfMapper.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace DwarfMapper.CompilerTests;

/// <summary>
///     What the generator costs a consumer whose solution declares a THOUSAND mappers — round 23, S3.
///     <para>
///         <b>What is gated and what is only recorded.</b> The row this implements asked for a wall-clock
///         ratio against a pinned baseline. It is not gated here, and the reason is the repository's own
///         rule: invariant <b>R4</b> forbids a gate whose oracle is nondeterministic, and H7's discipline is
///         that no gate reads a clock. A 1.5× wall-clock ratio on a shared runner reddens for a noisy
///         neighbour and stays green through a real 40 % regression, so it would be a gate that fails
///         honestly only by accident. S2 set the precedent for the wall-clock half — alert-only, recorded,
///         never build-failing — and the same split applies here: the TIME is measured and reported
///         (<see cref="Time_to_first_emit_is_recorded_never_gated" />), and what is GATED is the
///         deterministic property that actually decides the cost at scale.
///     </para>
///     <para>
///         <b>The deterministic gate.</b> A source generator's consumer-facing cost is not one cold build;
///         it is what happens on every keystroke afterwards. Editing ONE mapper in a thousand-mapper
///         solution must re-extract ONE mapper and re-emit ONE file (plus the single aggregate facade that
///         lists them all, which is one file by design). That count is a pure function of the pipeline's
///         value-equality, it is identical in the fast and deep tiers — MEASURED at both 40 and 1,000
///         mappers, see <see cref="Editing_one_mapper_recomputes_a_constant_amount_of_work" /> — and it is
///         what a wall-clock ratio would only ever indirectly observe. If it ever becomes O(N), a
///         thousand-mapper solution re-emits its whole output on every keystroke, and no clock is needed to
///         say so.
///     </para>
///     <para>
///         The corpus is K0's <see cref="TypeGraphRenderer" /> over <see cref="PinnedSampling" />'s
///         deterministic draws — the same case set the K0 smoke leg samples, at index <c>k</c> — rendered
///         one mapper per namespace into ONE compilation. No second renderer, and no hand-written corpus:
///         the shapes are the ones the fuzzers already explore, so the cost is measured against realistic
///         mappers rather than a thousand copies of the easiest one.
///     </para>
/// </summary>
public class GeneratorCompileCostTests
{
    private readonly ITestOutputHelper _output;

    public GeneratorCompileCostTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    ///     Steps of DwarfMapper's OWN pipeline. Matched by prefix rather than by importing
    ///     <c>DwarfGenerator</c>'s tracking-name constants, which are <c>internal</c> and visible only to
    ///     <c>DwarfMapper.Generator.Tests</c>. A prefix match can go vacuous, so
    ///     <see cref="OurSteps" /> refuses to return fewer than <see cref="MinimumTrackedSteps" /> of them.
    ///     <para>
    ///         Roslyn's own steps are deliberately outside the set: <c>Compilation</c> and
    ///         <c>compilationAndGroupedNodes_ForAttributeWithMetadataName</c> re-run on ANY compilation
    ///         change — measured non-cached even on the unrelated-edit case where every DwarfMapper step is
    ///         cached — so asserting over them would assert a property of the host, not of this generator.
    ///     </para>
    /// </summary>
    private const string OurStepPrefix = "DwarfMapper";

    /// <summary>The source-emit step, named by Roslyn itself; the one non-prefixed step that is ours.</summary>
    private const string SourceOutputStep = "SourceOutput";

    /// <summary>
    ///     Measured 2026-08-23 at 40 and at 1,000 mappers: six <c>DwarfMapper*</c> steps plus SourceOutput.
    ///     Floored at 5 so a renamed step is loud rather than silently shrinking the asserted set to nothing.
    /// </summary>
    private const int MinimumTrackedSteps = 5;

    /// <summary>
    ///     After a one-mapper edit: exactly one per-mapper extraction re-runs. MEASURED at both tiers
    ///     (40 mappers and 1,000), identical — which is the whole claim, so it is pinned as a constant and
    ///     not as a fraction of the corpus.
    /// </summary>
    private const int ExpectedExtractRecomputes = 1;

    /// <summary>
    ///     After a one-mapper edit: exactly two source outputs re-run — the edited mapper's own file, and
    ///     the single aggregate facade that lists every mapper (one file by design, so it necessarily
    ///     re-emits whenever any mapper changes). Also measured identical at 40 and at 1,000.
    /// </summary>
    private const int ExpectedSourceOutputRecomputes = 2;

    // ─────────────────────────────────────────────────────────────────────────
    // The corpus, built once. Every fact below re-runs the SAME primed driver against a different edit, so
    // parsing a thousand units and priming the pipeline are paid once per class rather than once per fact.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record Corpus(
        int MapperCount,
        CSharpCompilation Compilation,
        GraphSpec[] Graphs,
        GeneratorDriver Primed,
        TimeSpan ParseTime,
        TimeSpan TimeToFirstEmit,
        int GeneratedTreeCount,
        int GeneratorDiagnosticCount);

    private static readonly Lazy<Corpus> Shared = new(BuildCorpus, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Corpus BuildCorpus()
    {
        var mapperCount = DeepTier.Count(DeepPopulation.CompilerCostCorpusMappers);
        var graphs = new GraphSpec[mapperCount];
        var gen = TypeGraphGen.MirroredPair();

        // H7: the loop variant is the case index, and every inner Sample runs exactly one iteration.
        Parallel.For(0, mapperCount, i =>
            gen.Sample(g => graphs[i] = g, seed: PinnedSampling.SeedFor(i), iter: 1, threads: 1));

        var parseWatch = Stopwatch.StartNew();
        var trees = new List<SyntaxTree>();
        for (var i = 0; i < graphs.Length; i++) trees.AddRange(TreesFor(graphs[i], i));
        var compilation = CSharpCompilation.Create("CostCorpusAsm", trees,
            CompilerTestHarness.MetadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        parseWatch.Stop();

        // Time-to-first-emit is measured on a PLAIN driver: a consumer's build does not track steps, and
        // GeneratorDriverOptions(trackIncrementalGeneratorSteps: true) adds bookkeeping to every one. The
        // tracked driver below is primed separately and its cost is deliberately not in this number.
        var emitWatch = Stopwatch.StartNew();
        var plain = CSharpGeneratorDriver.Create(new DwarfGenerator(), new MapToGenerator())
            .RunGenerators(compilation);
        emitWatch.Stop();
        var run = plain.GetRunResult();

        return new Corpus(mapperCount, compilation, graphs,
            TrackedDriver().RunGenerators(compilation),
            parseWatch.Elapsed, emitWatch.Elapsed,
            run.GeneratedTrees.Length, run.Diagnostics.Length);
    }

    private static List<SyntaxTree> TreesFor(GraphSpec graph, int index) =>
        TypeGraphRenderer.Render(graph, withProjection: false, namespaceName: Namespace(index))
            .Select(u => CSharpSyntaxTree.ParseText(u)).ToList();

    private static string Namespace(int index) => FormattableString.Invariant($"G{index}");

    private static CSharpGeneratorDriver TrackedDriver() =>
        CSharpGeneratorDriver.Create(
            [new DwarfGenerator().AsSourceGenerator(), new MapToGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    /// <summary>
    ///     Every tracked step belonging to this generator, keyed by name, with its outputs' reasons
    ///     flattened. Throws rather than returning a short set: a renamed step would otherwise shrink every
    ///     assertion below to a claim about nothing, which is the one failure mode a caching test cannot
    ///     afford.
    /// </summary>
    private static Dictionary<string, List<IncrementalStepRunReason>> OurSteps(GeneratorDriver driver)
    {
        var steps = new Dictionary<string, List<IncrementalStepRunReason>>(StringComparer.Ordinal);

        foreach (var result in driver.GetRunResult().Results)
        foreach (var (name, runs) in result.TrackedSteps)
        {
            if (!name.StartsWith(OurStepPrefix, StringComparison.Ordinal)
                && !string.Equals(name, SourceOutputStep, StringComparison.Ordinal)) continue;

            if (!steps.TryGetValue(name, out var reasons)) steps[name] = reasons = [];
            reasons.AddRange(runs.SelectMany(r => r.Outputs).Select(o => o.Reason));
        }

        if (steps.Count < MinimumTrackedSteps)
            throw new InvalidOperationException(
                $"only {steps.Count} tracked step(s) matched '{OurStepPrefix}*' or '{SourceOutputStep}', "
                + $"expected at least {MinimumTrackedSteps}. The pipeline's tracking names have changed and "
                + "these caching assertions are now asserting almost nothing — re-point the match rather than "
                + "lowering the floor.");

        return steps;
    }

    /// <summary>
    ///     Outputs whose VALUE changed — <c>Cached</c> (not re-run) and <c>Unchanged</c> (re-run, equal
    ///     result) both count as no change, which is the repository's established idiom
    ///     (<c>GeneratorCacheAssert</c>).
    ///     <para>
    ///         <b>A stricter "did it run at all" metric was tried first and is wrong here — measured, not
    ///         assumed.</b> Counting anything but <c>Cached</c> reports <c>DwarfMapperExtract</c> re-running
    ///         for ALL N mappers on a clean, unmodified generator, because
    ///         <c>ForAttributeWithMetadataName</c>'s transform takes a <c>GeneratorAttributeSyntaxContext</c>
    ///         carrying a semantic model: Roslyn re-executes it for every attributed class whenever the
    ///         compilation changes at all, and no generator can opt out of that. So the strict count
    ///         measures the host, not this pipeline, and it reads N/N at baseline — a gate that is red on a
    ///         healthy tree is not a gate.
    ///     </para>
    ///     <para>
    ///         What DwarfMapper controls, and what therefore decides the consumer's cost, is whether the
    ///         re-run produces an EQUAL model: an equal model leaves every downstream emit
    ///         <c>Cached</c>/<c>Unchanged</c> and nothing is re-emitted, while a model that stopped being
    ///         value-equatable re-emits all N files on every keystroke. That is the property below.
    ///     </para>
    /// </summary>
    private static int Changed(IEnumerable<IncrementalStepRunReason> reasons) =>
        reasons.Count(r => r is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));

    /// <summary>The per-mapper extraction step: the one whose output count is the corpus size.</summary>
    private static KeyValuePair<string, List<IncrementalStepRunReason>> PerMapperStep(
        Dictionary<string, List<IncrementalStepRunReason>> steps, int mapperCount)
    {
        var candidates = steps.Where(s => s.Value.Count == mapperCount).ToList();
        Assert.True(candidates.Count == 1,
            $"expected exactly one tracked step with one output per mapper ({mapperCount}); found "
            + candidates.Count + " among [" + string.Join(", ",
                steps.Select(s => $"{s.Key}={s.Value.Count}")) + "]. Without it there is no evidence the "
            + "corpus registered as one model per mapper, and the per-mapper claims are unanchored.");
        return candidates[0];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The recorded measurement.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Time_to_first_emit_is_recorded_never_gated()
    {
        var corpus = Shared.Value;

        // The per-1,000 normalisation is only printed where it means something. Extrapolating it from the
        // fast tier is a lie the first measurement told: 880 ms at 40 mappers normalises to 22.0 s per
        // 1,000, while the deep tier actually measures 3.6 s per 1,000 — a 6x overstatement, because at
        // N=40 the figure is almost entirely one-off JIT and first-run warm-up. A number that wrong would
        // be quoted anyway if it were printed, so it is not printed.
        var normalised = corpus.MapperCount >= 500
            ? string.Create(CultureInfo.InvariantCulture,
                $"({corpus.TimeToFirstEmit.TotalMilliseconds * 1000.0 / corpus.MapperCount:F0} ms per 1,000 mappers)")
            : "(not normalised to 1,000 at this size: dominated by one-off JIT/warm-up — run with DWARF_DEEP=1)";

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"S3 generator compile cost: mappers={corpus.MapperCount} "
            + $"parse+compilation={corpus.ParseTime.TotalMilliseconds:F0} ms "
            + $"time-to-first-emit={corpus.TimeToFirstEmit.TotalMilliseconds:F0} ms "
            + $"{normalised} "
            + $"generated={corpus.GeneratedTreeCount} diagnostics={corpus.GeneratorDiagnosticCount}"));

        // NOT a clock assertion, and it must never become one (R4/H7): what is asserted is that the run was
        // not vacuous — a corpus that emitted nothing would report a wonderful time.
        Assert.True(corpus.MapperCount >= 40, "the cost corpus is too small to mean anything.");
        Assert.True(corpus.GeneratedTreeCount >= corpus.MapperCount,
            $"{corpus.MapperCount} mappers produced only {corpus.GeneratedTreeCount} generated file(s) — the "
            + "corpus is not exercising one emission per mapper, so the recorded time measures something "
            + "smaller than it claims.");
        Assert.True(corpus.TimeToFirstEmit > TimeSpan.Zero, "the stopwatch recorded no elapsed time at all.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The deterministic gates.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_identical_rerun_recomputes_nothing_at_corpus_scale()
    {
        var corpus = Shared.Value;
        var steps = OurSteps(corpus.Primed.RunGenerators(corpus.Compilation));

        foreach (var (name, reasons) in steps)
            Assert.True(Changed(reasons) == 0,
                $"step '{name}' produced {Changed(reasons)} changed output(s) of {reasons.Count} on an "
                + $"IDENTICAL re-run over {corpus.MapperCount} mappers. A model that stopped being "
                + "value-equatable disables caching for every consumer, and at this scale that is the "
                + "whole build.");

        // Anchors the sweep above: without a step carrying one output per mapper, "nothing changed" could
        // be true of a pipeline that never saw the corpus.
        PerMapperStep(steps, corpus.MapperCount);
    }

    [Fact]
    public void An_unrelated_edit_leaves_every_mapper_in_the_corpus_cached()
    {
        var corpus = Shared.Value;
        var edited = corpus.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "namespace Other { public class Unrelated { public int Z; } }"));

        var steps = OurSteps(corpus.Primed.RunGenerators(edited));

        foreach (var (name, reasons) in steps)
            Assert.True(Changed(reasons) == 0,
                $"step '{name}' produced {Changed(reasons)} changed output(s) of {reasons.Count} after an "
                + $"edit that touched no mapper at all, in a {corpus.MapperCount}-mapper compilation.");

        // The assertion the sabotage demo reds: a model that stopped being value-equatable re-emits every
        // mapper in the solution after an edit that touched none of them.
        var (perMapperName, perMapper) = PerMapperStep(steps, corpus.MapperCount);
        Assert.True(Changed(perMapper) == 0,
            $"step '{perMapperName}' produced {Changed(perMapper)} changed model(s) of {corpus.MapperCount} "
            + "after an edit that touched no mapper at all. Every keystroke anywhere in a consumer's "
            + "solution would re-emit their whole mapper surface.");
    }

    [Fact]
    public void Editing_one_mapper_recomputes_a_constant_amount_of_work()
    {
        var corpus = Shared.Value;

        // ReplaceSyntaxTree, not Remove+Add: appending puts the replacement LAST and reorders every tree
        // after it, which was measured to recompute all N mappers — an artefact of how the edit was
        // modelled, not of the generator. A real editor replaces a document in place.
        var target = corpus.Compilation.SyntaxTrees.First(
            t => t.ToString().Contains("namespace " + Namespace(0) + ";", StringComparison.Ordinal));
        var editedUnit = TypeGraphRenderer.Render(
            corpus.Graphs[0], withProjection: true, namespaceName: Namespace(0))[0];
        Assert.NotEqual(target.ToString(), editedUnit);

        var steps = OurSteps(corpus.Primed.RunGenerators(
            corpus.Compilation.ReplaceSyntaxTree(target, CSharpSyntaxTree.ParseText(editedUnit))));

        var (perMapperName, perMapper) = PerMapperStep(steps, corpus.MapperCount);
        Assert.True(Changed(perMapper) == ExpectedExtractRecomputes,
            $"editing ONE mapper re-extracted {Changed(perMapper)} of {corpus.MapperCount} "
            + $"(step '{perMapperName}'), expected exactly {ExpectedExtractRecomputes}. This number is "
            + "measured identical at 40 and at 1,000 mappers; if it now scales with the corpus, a "
            + "thousand-mapper solution re-analyses every mapper on every keystroke.");

        var sourceOutput = steps[SourceOutputStep];
        Assert.True(Changed(sourceOutput) == ExpectedSourceOutputRecomputes,
            $"editing ONE mapper re-emitted {Changed(sourceOutput)} of {sourceOutput.Count} source file(s), "
            + $"expected exactly {ExpectedSourceOutputRecomputes} — the edited mapper's own file plus the one "
            + "aggregate facade that lists them all. A third re-emission means some other mapper's output "
            + "depends on this one, and the dependency is O(N).");

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"S3 one-mapper edit at {corpus.MapperCount} mappers: "
            + $"{perMapperName} {Changed(perMapper)}/{perMapper.Count} re-ran, "
            + $"{SourceOutputStep} {Changed(sourceOutput)}/{sourceOutput.Count} re-emitted"));
    }
}
