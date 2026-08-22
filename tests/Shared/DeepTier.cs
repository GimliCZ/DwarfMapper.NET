// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.TestInfrastructure;

/// <summary>
///     The dialable fuzz / property / torture populations, one entry per knob. Every population that scales
///     with <see cref="DeepTier" /> MUST be declared here — call sites ask for
///     <see cref="DeepTier.Count(DeepPopulation)" /> rather than reading the environment themselves, so the
///     fast/deep pair for each population lives in exactly one place and the self-test can prove the deep
///     tier is non-vacuous for every entry. A scan test bans direct reads of the environment variable from
///     test code, so a population cannot silently opt out of the registry.
/// </summary>
public enum DeepPopulation
{
    /// <summary>
    ///     <c>FeatureCombinationFuzzTests</c>: the maximum subset cardinality enumerated EXHAUSTIVELY (the
    ///     all-16-features case is always included on top). Fast = 2 (singles + pairs — the tier where the
    ///     deferred-member bugs actually hid); deep = 3 adds all C(16,3) = 560 triples per emit-path consumer,
    ///     the next complete structural tier (~2,800 extra generator runs).
    /// </summary>
    FeatureCombinationSubsetOrder,

    /// <summary>Fuzzing/BehavioralFuzzTests seeds. Deep ×10: same generator, ten times the shape space.</summary>
    BehavioralSeeds,

    /// <summary>Fuzzing/CombinatorialEngineTests (ExtendedBehavioralFuzzTests) seeds, base 200. Deep ×10.</summary>
    ExtendedBehavioralSeeds,

    /// <summary>
    ///     Fuzzing/AllEmitPathsAgreeFuzzTests seeds. Deep ×5, not ×10: at ~300 ms per seed (four assemblies
    ///     compiled and cross-compared per case, serial within the class) ×10 would make this single class
    ///     the wall-clock ceiling of the whole deep tier.
    /// </summary>
    AllEmitPathsSeeds,

    /// <summary>
    ///     Fuzzing/CompilesAlwaysFuzzTests broad seeds. Deep ×5, not ×10: both of this class's theories dial
    ///     up together and run serially within the class, so ×5 each keeps the class inside the deep budget.
    /// </summary>
    CompilesAlwaysSeeds,

    /// <summary>Fuzzing/CompilesAlwaysFuzzTests advanced-schema seeds. Deep ×5 — same class as above.</summary>
    CompilesAlwaysAdvancedSeeds,

    /// <summary>Fuzzing/DeterminismFuzzTests broad seeds. Deep ×10 (~18 ms per seed, cheap).</summary>
    DeterminismBroadSeeds,

    /// <summary>Fuzzing/IndependenceOracleFuzzTests seeds. Deep ×10.</summary>
    IndependenceSeeds,

    /// <summary>
    ///     Fuzzing/MetamorphicPropertyFuzzTests seeds. Deep ×5, not ×10: two theories share the class
    ///     (~86 ms per case, serial within the class), so ×5 keeps the class inside the deep budget.
    /// </summary>
    MetamorphicSeeds,

    /// <summary>Fuzzing/TopologyOracleFuzzTests graph seeds (three theories). Deep ×10.</summary>
    TopologyGraphSeeds,

    /// <summary>Fuzzing/FlattenGraphFuzzTests homogeneous-graph seeds. Deep ×10.</summary>
    FlattenHomoSeeds,

    /// <summary>Fuzzing/FlattenGraphFuzzTests heterogeneous-graph seeds. Deep ×10.</summary>
    FlattenHeteroSeeds,

    /// <summary>
    ///     Fuzzing/CrossConfigFuzzTests update-mode seeds. Deep ×10. (The config PAIR matrix in the same
    ///     class is already exhaustive — all C(10,2) fragment pairs — so it has no deep tier.)
    /// </summary>
    CrossConfigUpdateSeeds,

    /// <summary>Fuzzing/CultureInvarianceFuzzTests behavioural-schema seeds (tr-TR leg). Deep ×10.</summary>
    CultureBehavioralSeeds,

    /// <summary>Fuzzing/CultureInvarianceFuzzTests advanced-schema seeds (de-DE leg). Deep ×10.</summary>
    CultureAdvancedSeeds,

    /// <summary>
    ///     SelfValidation/DocPipelinePropertyTests CsCheck <c>iter</c> for the three injection properties.
    ///     Deep ×10 — CsCheck iterations are in-process text transforms, the cheapest cases in the registry.
    /// </summary>
    DocPipelineIters,

    /// <summary>SelfValidation/DocPipelinePropertyTests CsCheck <c>iter</c> for the scanner property. Deep ×10.</summary>
    DocPipelineScanIters,

    /// <summary>
    ///     RegistryConcurrencyTortureTests create-table rounds. Fast = 60 (the measured-power count in the
    ///     class remarks); deep = ×4 — the same multiplier the update side needed to go from a one-round
    ///     detection margin to a comfortable one, applied to the side that never got it.
    /// </summary>
    TortureCreateRounds,

    /// <summary>
    ///     RegistryConcurrencyTortureTests update-table rounds. Fast = 240 (measured power: check-then-act
    ///     caught 23/54/58 of 240 vs 1/6/7 of 60); deep = ×4 for more samples inside the contested window.
    /// </summary>
    TortureUpdateRounds,

    /// <summary>
    ///     PolymorphicMemberFuzzTests graph seeds. Fast = the original five seeds exactly; deep extends the
    ///     ObjectFactory seed space to 45 distinct seeds over the same abstract-membered graph.
    /// </summary>
    PolymorphicGraphSeeds,

    /// <summary>ObjectFactoryV2DistributionTests sample count per type. Deep ×10 (in-process sampling, cheap).</summary>
    ObjectFactoryDistributionSeeds,

    /// <summary>
    ///     RegistryPropertyTests CsCheck <c>iter</c> for the three ambient-registry contract properties.
    ///     Deep ×10 — each iteration is a handful of <c>ConcurrentDictionary</c> operations against
    ///     pre-declared closed types, in-process and allocation-light, the same cost class as
    ///     <see cref="DocPipelineIters" />.
    /// </summary>
    RegistryPropertyIters,

    /// <summary>
    ///     CompilerTests/TypeGraphSmokeTests CsCheck <c>iter</c> (round-22 K0). Each iteration renders a
    ///     sampled type graph and runs BOTH generators plus a full in-memory compile (~15–45 ms serial per
    ///     sample, CsCheck-parallelized across cores). Deep ×10, measured before entering the catalog per
    ///     the round-21 rule: fast 25 ≈ 1 s in-class, deep 250 ≈ 2 s in-class on the 12-core reference
    ///     machine (2026-08-22) — cheap enough that ×10 is the right multiplier despite the per-sample cost
    ///     class being <see cref="AllEmitPathsSeeds" />', because the base count is small.
    /// </summary>
    CompilerGraphSmokeSeeds,

    /// <summary>
    ///     CompilerTests/TypeGraphSmokeTests' PROJECTION leg (round-23 I18): the same K0 must-compile-or-
    ///     refuse smoke over graphs whose mapper declares
    ///     <c>IQueryable&lt;D0&gt; Project(IQueryable&lt;S0&gt;)</c> beside <c>Map</c>. Its own entry rather
    ///     than a wider <see cref="CompilerGraphSmokeSeeds" /> because emitting <c>Project</c> into the
    ///     existing population would re-cut its case set (the projection refuses a strictly wider grammar,
    ///     and one error-severity refusal makes the whole run "refused" — accepted cases would silently
    ///     stop being compile-checked); see <c>TypeGraphRenderer.Render</c>'s <c>withProjection</c> remarks.
    ///     Same per-sample cost class as <see cref="CompilerGraphSmokeSeeds" /> — one render, both
    ///     generators, one full in-memory compile — but a HIGHER fast count than its 25, and the reason is
    ///     the measured accept/refuse split rather than the cost. MEASURED before entering the catalog per
    ///     the round-21 rule (2026-08-23, 12-core reference machine), 25 / 50 / 100 / 200 / deep 1000:
    ///     <b>2</b>/23, <b>7</b>/43, <b>21</b>/79, <b>48</b>/152 and <b>233</b>/767 accepted/refused, at
    ///     ≈1 s, ≈1 s, ≈1 s, ≈2 s and ≈7 s in-class. The projection refuses a much wider grammar than
    ///     <c>Map</c> (a HashSet member, a converter, a hook, reference handling — DWARF028's whole reason
    ///     list), so at 25 the leg would have carried its compile-clean invariant on TWO accepted cases,
    ///     one grammar tightening away from tripping its own vacuity floor. Fast <b>100</b> buys 21
    ///     accepts for the same ≈1 s — the count is chosen off the accept column, not the wall column.
    ///     Deep ×10 = 1000, ≈7 s in-class, 233 accepts.
    /// </summary>
    CompilerProjectionSmokeSeeds,

    /// <summary>
    ///     CompilerTests/DifferentialOracleTests CsCheck <c>iter</c> (round-22 K1). Each iteration is a
    ///     K0 smoke sample PLUS an in-memory emit, an assembly load, a name-keyed population, the generated
    ///     map's execution and the naive-oracle comparison — the heaviest per-sample cost class in this
    ///     registry. Fast 20 keeps the class ~2 s in the fast tier; deep 1000 is the plan's full-count
    ///     target, measured before entering the catalog per the round-21 rule (2026-08-22, 12-core
    ///     reference machine, 3 runs): the oracle theory runs 1000/1000 in ~10 s in-class, whole
    ///     CompilerTests project deep wall 16.2–16.5 s. The 10,000 variant stays a knob value only,
    ///     unmeasured and therefore unused.
    /// </summary>
    CompilerOracleSeeds,

    /// <summary>
    ///     CompilerTests/ProjectionAgreementTests (round-23 I18): the ENDPOINT-AGREEMENT relation —
    ///     <c>Project</c> over a one-element <c>AsQueryable()</c> must produce what <c>Map</c> produces from
    ///     the same source instance. K1's cost class (render + both generators + compile + emit + load +
    ///     populate + map + projection + structural diff), which is why its fast count is small.
    ///     <para>
    ///         The relation the smoke leg cannot express: K0 proves a projection COMPILES, this proves it
    ///         is RIGHT. Stated limit (B19): the tree is executed by LINQ-to-Objects, so this proves it
    ///         compiles and evaluates, never that an ORM provider translates it.
    ///     </para>
    ///     MEASURED before entering the catalog per the round-21 rule (2026-08-23, 12-core reference
    ///     machine). Measured on ITS OWN generator, which draws graph+population-seed PAIRS where the smoke
    ///     leg draws graphs only — and the execution counts came out IDENTICAL to the smoke leg's accept
    ///     counts (25 → 2, 50 → 7, 100 → 21, deep 1000 → 233), because the pair generator draws the graph
    ///     from the same stream first and the extra seed does not perturb it. Worth recording precisely
    ///     because it is a coincidence of the generator's shape, not a guarantee: a future generator edit
    ///     can decouple them, so re-measure rather than assuming the two entries move together.
    ///     Fast <b>100</b> (21 executions, ≈2 s in-class) for the same reason as the smoke leg — at 25 the
    ///     relation ran on TWO cases. Deep ×10 = 1000, 233 executions, ≈14 s in-class. The two deterministic
    ///     anchors (the accepted <c>PinnedCorpus</c> rows) execute regardless of where the sampled split
    ///     falls, so the relation is never entirely at the mercy of this count.
    /// </summary>
    CompilerProjectionAgreementSeeds,

    /// <summary>
    ///     CompilerTests/MetamorphicTests MR-1 CsCheck <c>iter</c> (round-22 K2). Each iteration is TWO
    ///     full K1-style runs (render + both generators + compile + emit + load + populate + map +
    ///     fingerprint) — base graph and its member-order-reversed variant. Measured before entering the
    ///     catalog per the round-21 rule (2026-08-22, 12-core reference machine): fast 10 ≈ 2 s in-class
    ///     (first metamorphic test in the class, so it pays the warmup); deep 250 ≈ 8 s in-class;
    ///     CompilerTests project deep wall 30.4–31.7 s across 3 runs with all three MR entries live.
    /// </summary>
    CompilerMrMemberOrderSeeds,

    /// <summary>
    ///     CompilerTests/MetamorphicTests MR-2 CsCheck <c>iter</c> (round-22 K2). Two runs per iteration,
    ///     same cost class as <see cref="CompilerMrMemberOrderSeeds" /> (the variant adds one scalar
    ///     member per non-dest-reachable node). Measured 2026-08-22 with MR-1: fast 10 ≈ 0.3 s in-class;
    ///     deep 250 ≈ 14 s in-class.
    /// </summary>
    CompilerMrUnmappedMemberSeeds,

    /// <summary>
    ///     CompilerTests/MetamorphicTests MR-3 CsCheck <c>iter</c> (round-22 K2). The heaviest relation:
    ///     up to THREE full runs per iteration (Class/Record/RecordStruct uniform re-kinds; two when the
    ///     graph uses inheritance), so deep 150 prices out near MR-1's 250 pairs in run count. Measured
    ///     2026-08-22 with MR-1: fast 8 ≈ 0.25 s in-class; deep 150 ≈ 4 s in-class.
    /// </summary>
    CompilerMrRekindSeeds
}

/// <summary>
///     The ONE reader of the <c>DWARF_DEEP</c> environment variable. Unset or anything but <c>1</c> = the
///     fast tier: byte-for-byte today's counts, so routine <c>dotnet test</c> never pays for depth.
///     <c>DWARF_DEEP=1</c> (set by <c>scripts/housekeeping.ps1 -Deep</c>) = the deep tier: every registered
///     population multiplied per its catalog entry. The fast column of the catalog is pinned by
///     <c>DeepTierSelfTests</c> to the exact historical counts — a knob edit cannot silently change the
///     fast tier — and the same self-test proves deep &gt; fast for every entry, so the deep tier cannot be
///     vacuous either.
/// </summary>
public static class DeepTier
{
    /// <summary>Read once per process: xunit enumerates MemberData at discovery, mid-run flips are not a thing.</summary>
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("DWARF_DEEP") == "1";

    // (fast, deep) per population. Fast values are the exact counts the suite ran with before the knob
    // existed; the per-entry multiplier rationale lives on the DeepPopulation member.
    //
    // The five Compiler* entries changed MECHANISM in round 22 (finding I11) without changing a count:
    // they are now PINNED CASE INDEXES (CompilerTests/PinnedSampling — one deterministic CsCheck draw per
    // index, run in parallel) rather than CsCheck iterations of a single random sample, because a random
    // draw in the fast tier is a gate on a nondeterministic oracle (invariant R4). The fast list is a
    // prefix of the deep list, so raising a deep count only ADDS cases. Re-measured on the 12-core
    // reference machine, 2026-08-22: CompilerTests project-alone deep wall 31.1 / 34.4 / 32.4 s across 3
    // runs (K2-era mechanism: 30.4–31.7 s), fast-tier project wall 2–3 s. The per-entry in-class figures
    // below are from the K2-era measurement and remain the right order of magnitude.
    private static readonly Dictionary<DeepPopulation, (int Fast, int Deep)> Catalog = new()
    {
        [DeepPopulation.FeatureCombinationSubsetOrder] = (2, 3),
        [DeepPopulation.BehavioralSeeds] = (60, 600),
        [DeepPopulation.ExtendedBehavioralSeeds] = (40, 400),
        [DeepPopulation.AllEmitPathsSeeds] = (50, 250),
        [DeepPopulation.CompilesAlwaysSeeds] = (200, 1000),
        [DeepPopulation.CompilesAlwaysAdvancedSeeds] = (50, 250),
        [DeepPopulation.DeterminismBroadSeeds] = (120, 1200),
        [DeepPopulation.IndependenceSeeds] = (60, 600),
        [DeepPopulation.MetamorphicSeeds] = (50, 250),
        [DeepPopulation.TopologyGraphSeeds] = (12, 120),
        [DeepPopulation.FlattenHomoSeeds] = (25, 250),
        [DeepPopulation.FlattenHeteroSeeds] = (15, 150),
        [DeepPopulation.CrossConfigUpdateSeeds] = (20, 200),
        [DeepPopulation.CultureBehavioralSeeds] = (8, 80),
        [DeepPopulation.CultureAdvancedSeeds] = (4, 40),
        [DeepPopulation.DocPipelineIters] = (500, 5000),
        [DeepPopulation.DocPipelineScanIters] = (200, 2000),
        [DeepPopulation.TortureCreateRounds] = (60, 240),
        [DeepPopulation.TortureUpdateRounds] = (240, 960),
        [DeepPopulation.PolymorphicGraphSeeds] = (5, 45),
        [DeepPopulation.ObjectFactoryDistributionSeeds] = (400, 4000),
        [DeepPopulation.RegistryPropertyIters] = (200, 2000),
        [DeepPopulation.CompilerGraphSmokeSeeds] = (25, 250),
        [DeepPopulation.CompilerProjectionSmokeSeeds] = (100, 1000),
        [DeepPopulation.CompilerOracleSeeds] = (20, 1000),
        [DeepPopulation.CompilerProjectionAgreementSeeds] = (100, 1000),
        [DeepPopulation.CompilerMrMemberOrderSeeds] = (10, 250),
        [DeepPopulation.CompilerMrUnmappedMemberSeeds] = (10, 250),
        [DeepPopulation.CompilerMrRekindSeeds] = (8, 150)
    };

    /// <summary>The count a call site should run with right now (fast unless <see cref="Enabled" />).</summary>
    public static int Count(DeepPopulation population)
    {
        return CountFor(population, Enabled);
    }

    /// <summary>
    ///     The pure core, split out so the self-test can pin both tiers without mutating process state:
    ///     an unregistered population throws (loud, not vacuous) rather than defaulting.
    /// </summary>
    public static int CountFor(DeepPopulation population, bool deep)
    {
        var (fast, deepCount) = Catalog[population];
        return deep ? deepCount : fast;
    }

    /// <summary>Every registered population, for the self-test's exhaustive sweep.</summary>
    public static IReadOnlyCollection<DeepPopulation> Populations => Catalog.Keys;
}
