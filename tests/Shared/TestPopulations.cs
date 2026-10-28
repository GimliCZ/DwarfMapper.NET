// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.TestInfrastructure
{
    /// <summary>
    ///     The sized fuzz / property / torture populations, one entry each. Every population whose size is a choice MUST
    ///     be declared here: call sites ask for <see cref="TestPopulations.Count(TestPopulation)" /> rather than writing
    ///     the number in place, so a size and the measurement behind it live in exactly one place, and the self-test can
    ///     pin every size and prove each entry has a call site.
    ///     <para>
    ///         One size per population, in every run (round 34; the owner, 2026-10-28: "convert preliminary tests into
    ///         full deep ones ... so we have single way of testing entire infra"). Until then each population had a fast
    ///         size, which routine <c>dotnet test</c>, CI's per-push jobs and Stryker ran, and a deep size, which
    ///         <c>DWARF_DEEP=1</c> selected for the nightly job, <c>housekeeping.ps1 -Deep</c> and the IDE. Every
    ///         population now runs what was its deep size, everywhere, Stryker's sessions included. The variable is gone,
    ///         and <c>TestPopulationsSelfTests</c> keeps it gone.
    ///     </para>
    /// </summary>
    public enum TestPopulation
    {
        /// <summary>
        ///     <c>FeatureCombinationFuzzTests</c>: the maximum subset cardinality enumerated EXHAUSTIVELY, the
        ///     all-16-features case always included on top. 3: singles, pairs (the order where the deferred-member bugs
        ///     hid) and all C(16,3) = 560 triples per emit-path consumer. Every subset is
        ///     <c>Full_power_set_compiles_on_every_emit_path</c>, which runs in a job of its own.
        /// </summary>
        FeatureCombinationSubsetOrder,

        /// <summary>Fuzzing/BehavioralFuzzTests seeds: 600, ten times the shape space of the 60 the fast tier ran.</summary>
        BehavioralSeeds,

        /// <summary>Fuzzing/CombinatorialEngineTests (ExtendedBehavioralFuzzTests) seeds: 400 (the fast tier ran 40).</summary>
        ExtendedBehavioralSeeds,

        /// <summary>
        ///     Fuzzing/AllEmitPathsAgreeFuzzTests seeds: 250, five times the fast tier's 50 rather than ten: at ~300 ms per
        ///     seed (four assemblies compiled and cross-compared per case) ten times would have made this class the
        ///     longest in the run. Round 34 E12 deals the seeds across four classes.
        /// </summary>
        AllEmitPathsSeeds,

        /// <summary>
        ///     Fuzzing/CompilesAlwaysFuzzTests broad seeds: 1,000, five times the fast tier's 200 rather than ten, because
        ///     both of the class's theories grew together and ran one after another in one class.
        /// </summary>
        CompilesAlwaysSeeds,

        /// <summary>Fuzzing/CompilesAlwaysFuzzTests advanced-schema seeds: 250 (the fast tier ran 50); same class as above.</summary>
        CompilesAlwaysAdvancedSeeds,

        /// <summary>Fuzzing/DeterminismFuzzTests broad seeds: 1,200 (the fast tier ran 120; ~18 ms per seed, cheap).</summary>
        DeterminismBroadSeeds,

        /// <summary>Fuzzing/IndependenceOracleFuzzTests seeds: 600 (the fast tier ran 60).</summary>
        IndependenceSeeds,

        /// <summary>
        ///     Fuzzing/MetamorphicPropertyFuzzTests seeds: 250, five times the fast tier's 50 rather than ten, because two
        ///     theories share the class (~86 ms per case). Round 34 E12 deals the round trips across two classes.
        /// </summary>
        MetamorphicSeeds,

        /// <summary>Fuzzing/TopologyOracleFuzzTests graph seeds (three theories): 120 (the fast tier ran 12).</summary>
        TopologyGraphSeeds,

        /// <summary>Fuzzing/FlattenGraphFuzzTests homogeneous-graph seeds: 250 (the fast tier ran 25).</summary>
        FlattenHomoSeeds,

        /// <summary>Fuzzing/FlattenGraphFuzzTests heterogeneous-graph seeds: 150 (the fast tier ran 15).</summary>
        FlattenHeteroSeeds,

        /// <summary>
        ///     Fuzzing/CrossConfigFuzzTests update-mode seeds: 200 (the fast tier ran 20). The config PAIR matrix in the
        ///     same class is exhaustive already, all C(10,2) fragment pairs, so it has no entry.
        /// </summary>
        CrossConfigUpdateSeeds,

        /// <summary>Fuzzing/CultureInvarianceFuzzTests behavioural-schema seeds (tr-TR leg): 80 (the fast tier ran 8).</summary>
        CultureBehavioralSeeds,

        /// <summary>Fuzzing/CultureInvarianceFuzzTests advanced-schema seeds (de-DE leg): 40 (the fast tier ran 4).</summary>
        CultureAdvancedSeeds,

        /// <summary>
        ///     SelfValidation/DocPipelinePropertyTests CsCheck <c>iter</c> for the three injection properties: 5,000 (the
        ///     fast tier ran 500). CsCheck iterations are in-process text transforms, the cheapest cases in the registry.
        /// </summary>
        DocPipelineIters,

        /// <summary>SelfValidation/DocPipelinePropertyTests CsCheck <c>iter</c> for the scanner property: 2,000 (the fast tier ran 200).</summary>
        DocPipelineScanIters,

        /// <summary>
        ///     RegistryConcurrencyTortureTests create-table rounds: 240. The fast tier's 60 was the measured-power count in
        ///     the class remarks; four times it is the multiplier the update side needed to go from a one-round detection
        ///     margin to a comfortable one, applied to the side that never got it.
        /// </summary>
        TortureCreateRounds,

        /// <summary>
        ///     RegistryConcurrencyTortureTests update-table rounds: 960. The fast tier's 240 was measured power
        ///     (check-then-act caught 23/54/58 of 240 against 1/6/7 of 60); four times it gives more samples inside the
        ///     contested window.
        /// </summary>
        TortureUpdateRounds,

        /// <summary>
        ///     PolymorphicMemberFuzzTests graph seeds: 45 distinct ObjectFactoryV2 seeds over the same abstract-membered
        ///     graph (the fast tier ran the original five, which are among them).
        /// </summary>
        PolymorphicGraphSeeds,

        /// <summary>ObjectFactoryV2DistributionTests sample count per type: 4,000 (the fast tier ran 400; in-process sampling, cheap).</summary>
        ObjectFactoryDistributionSeeds,

        /// <summary>
        ///     RegistryPropertyTests CsCheck <c>iter</c> for the three ambient-registry contract properties: 2,000 (the
        ///     fast tier ran 200). Each iteration is a handful of <c>ConcurrentDictionary</c> operations against
        ///     pre-declared closed types, in-process and allocation-light, the same cost class as
        ///     <see cref="DocPipelineIters" />.
        /// </summary>
        RegistryPropertyIters,

        /// <summary>
        ///     CompilerTests/TypeGraphSmokeTests CsCheck <c>iter</c> (round-22 K0): 250 (the fast tier ran 25). Each
        ///     iteration renders a sampled type graph and runs BOTH generators plus a full in-memory compile (~15-45 ms
        ///     serial per sample, CsCheck-parallelized across cores); measured before entering the catalog per the round-21
        ///     rule: 250 ≈ 2 s in-class on the 12-core reference machine (2026-08-22).
        /// </summary>
        CompilerGraphSmokeSeeds,

        /// <summary>
        ///     CompilerTests/TypeGraphSmokeTests' PROJECTION leg (round-23 I18): the same K0 must-compile-or-refuse smoke
        ///     over graphs whose mapper declares <c>IQueryable&lt;D0&gt; Project(IQueryable&lt;S0&gt;)</c> beside
        ///     <c>Map</c>. Its own entry rather than a wider <see cref="CompilerGraphSmokeSeeds" /> because emitting
        ///     <c>Project</c> into the existing population would re-cut its case set (the projection refuses a strictly
        ///     wider grammar, and one error-severity refusal makes the whole run "refused" — accepted cases would silently
        ///     stop being compile-checked); see <c>TypeGraphRenderer.Render</c>'s <c>withProjection</c> remarks. 1,000.
        ///     MEASURED before entering the catalog per the round-21 rule (2026-08-23, 12-core reference machine), 25 / 50
        ///     / 100 / 200 / 1000: <b>2</b>/23, <b>7</b>/43, <b>21</b>/79, <b>48</b>/152 and <b>233</b>/767
        ///     accepted/refused, at ≈1 s, ≈1 s, ≈1 s, ≈2 s and ≈7 s in-class. The projection refuses a much wider grammar
        ///     than <c>Map</c> (a HashSet member, a converter, a hook, reference handling — DWARF028's whole reason list),
        ///     so at 25 the leg would have carried its compile-clean invariant on TWO accepted cases; the count is chosen
        ///     off the accept column, not the wall column. 1,000 is ≈7 s in-class and 233 accepts.
        /// </summary>
        CompilerProjectionSmokeSeeds,

        /// <summary>
        ///     CompilerTests/DifferentialOracleTests CsCheck <c>iter</c> (round-22 K1): 1,000, the plan's full-count
        ///     target (the fast tier ran 20). Each iteration is a K0 smoke sample PLUS an in-memory emit, an assembly load,
        ///     a name-keyed population, the generated map's execution and the naive-oracle comparison — the heaviest
        ///     per-sample cost class in this registry. Measured before entering the catalog per the round-21 rule
        ///     (2026-08-22, 12-core reference machine, 3 runs): 1000/1000 in ~10 s in-class. The 10,000 variant stays a
        ///     knob value only, unmeasured and therefore unused.
        /// </summary>
        CompilerOracleSeeds,

        /// <summary>
        ///     CompilerTests/ProjectionAgreementTests (round-23 I18): the ENDPOINT-AGREEMENT relation — <c>Project</c> over
        ///     a one-element <c>AsQueryable()</c> must produce what <c>Map</c> produces from the same source instance. K1's
        ///     cost class (render + both generators + compile + emit + load + populate + map + projection + structural
        ///     diff).
        ///     <para>
        ///         The relation the smoke leg cannot express: K0 proves a projection COMPILES, this proves it is RIGHT.
        ///         Stated limit (B19): the tree is executed by LINQ-to-Objects, so this proves it compiles and evaluates,
        ///         never that an ORM provider translates it.
        ///     </para>
        ///     1,000: 233 executions, ≈14 s in-class (2026-08-23, 12-core reference machine). Measured on ITS OWN
        ///     generator, which draws graph+population-seed PAIRS where the smoke leg draws graphs only — and the
        ///     execution counts came out IDENTICAL to the smoke leg's accept counts (25 → 2, 50 → 7, 100 → 21, 1000 →
        ///     233), because the pair generator draws the graph from the same stream first and the extra seed does not
        ///     perturb it. A coincidence of the generator's shape, not a guarantee: a future generator edit can decouple
        ///     them, so re-measure rather than assuming the two entries move together. The two deterministic anchors (the
        ///     accepted <c>PinnedCorpus</c> rows) execute regardless of where the sampled split falls.
        /// </summary>
        CompilerProjectionAgreementSeeds,

        /// <summary>
        ///     CompilerTests/MetamorphicTests MR-1 CsCheck <c>iter</c> (round-22 K2): 250 (the fast tier ran 10). Each
        ///     iteration is TWO full K1-style runs (render + both generators + compile + emit + load + populate + map +
        ///     fingerprint) — base graph and its member-order-reversed variant. Measured 2026-08-22, 12-core reference
        ///     machine: ≈ 8 s in-class.
        /// </summary>
        CompilerMrMemberOrderSeeds,

        /// <summary>
        ///     CompilerTests/MetamorphicTests MR-2 CsCheck <c>iter</c> (round-22 K2): 250 (the fast tier ran 10). Two runs
        ///     per iteration, same cost class as <see cref="CompilerMrMemberOrderSeeds" /> (the variant adds one scalar
        ///     member per non-dest-reachable node); ≈ 14 s in-class.
        /// </summary>
        CompilerMrUnmappedMemberSeeds,

        /// <summary>
        ///     CompilerTests/MetamorphicTests MR-3 CsCheck <c>iter</c> (round-22 K2): 150 (the fast tier ran 8). The
        ///     heaviest relation: up to THREE full runs per iteration (Class/Record/RecordStruct uniform re-kinds; two when
        ///     the graph uses inheritance), so 150 prices out near MR-1's 250 pairs in run count; ≈ 4 s in-class.
        /// </summary>
        CompilerMrRekindSeeds,

        /// <summary>
        ///     CompilerTests/GeneratorCompileCostTests (round 23, S3): how many MAPPERS the cost corpus puts in ONE
        ///     compilation. Not a seed count — the whole population is a single compilation, so this entry scales the SIZE
        ///     of one case rather than the number of cases. 1,000 is the per-1000-mappers figure the row exists to measure
        ///     (the fast tier ran 40).
        /// </summary>
        CompilerCostCorpusMappers
    }

    /// <summary>
    ///     The size of each <see cref="TestPopulation" />: one size per population, in every run (see the enum's remarks
    ///     for how the fast and deep sizes became one). <c>TestPopulationsSelfTests</c> pins every size, so a change to
    ///     one is a decision someone makes in review rather than a side effect of an edit.
    /// </summary>
    public static class TestPopulations
    {
        // The five Compiler* entries changed MECHANISM in round 22 (finding I11) without changing a count: they are
        // PINNED CASE INDEXES (CompilerTests/PinnedSampling — one deterministic CsCheck draw per index, run in parallel)
        // rather than CsCheck iterations of a single random sample, because a random draw is a gate on a
        // nondeterministic oracle (invariant R4). Raising a count only ADDS cases. Measured on the 12-core reference
        // machine, 2026-08-22: the CompilerTests project alone at these counts 31.1 / 34.4 / 32.4 s across 3 runs.
        private static readonly Dictionary<TestPopulation, int> Catalog = new()
        {
            [TestPopulation.FeatureCombinationSubsetOrder] = 3,
            [TestPopulation.BehavioralSeeds] = 600,
            [TestPopulation.ExtendedBehavioralSeeds] = 400,
            [TestPopulation.AllEmitPathsSeeds] = 250,
            [TestPopulation.CompilesAlwaysSeeds] = 1000,
            [TestPopulation.CompilesAlwaysAdvancedSeeds] = 250,
            [TestPopulation.DeterminismBroadSeeds] = 1200,
            [TestPopulation.IndependenceSeeds] = 600,
            [TestPopulation.MetamorphicSeeds] = 250,
            [TestPopulation.TopologyGraphSeeds] = 120,
            [TestPopulation.FlattenHomoSeeds] = 250,
            [TestPopulation.FlattenHeteroSeeds] = 150,
            [TestPopulation.CrossConfigUpdateSeeds] = 200,
            [TestPopulation.CultureBehavioralSeeds] = 80,
            [TestPopulation.CultureAdvancedSeeds] = 40,
            [TestPopulation.DocPipelineIters] = 5000,
            [TestPopulation.DocPipelineScanIters] = 2000,
            [TestPopulation.TortureCreateRounds] = 240,
            [TestPopulation.TortureUpdateRounds] = 960,
            [TestPopulation.PolymorphicGraphSeeds] = 45,
            [TestPopulation.ObjectFactoryDistributionSeeds] = 4000,
            [TestPopulation.RegistryPropertyIters] = 2000,
            [TestPopulation.CompilerGraphSmokeSeeds] = 250,
            [TestPopulation.CompilerProjectionSmokeSeeds] = 1000,
            [TestPopulation.CompilerOracleSeeds] = 1000,
            [TestPopulation.CompilerProjectionAgreementSeeds] = 1000,
            [TestPopulation.CompilerMrMemberOrderSeeds] = 250,
            [TestPopulation.CompilerMrUnmappedMemberSeeds] = 250,
            [TestPopulation.CompilerMrRekindSeeds] = 150,
            [TestPopulation.CompilerCostCorpusMappers] = 1000
        };

        /// <summary>Every registered population, for the self-test's exhaustive sweep.</summary>
        public static IReadOnlyCollection<TestPopulation> Populations => Catalog.Keys;

        /// <summary>A population's size. An unregistered population throws (loud, not vacuous) rather than defaulting.</summary>
        public static int Count(TestPopulation population)
        {
            return Catalog[population];
        }
    }
}
