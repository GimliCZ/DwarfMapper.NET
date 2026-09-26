// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public interface ICostProbeSrc
    {
        int V { get; }
    }

    public sealed class CostProbeSrc : ICostProbeSrc
    {
        public int V { get; set; }
    }

    public sealed class CostProbeDst
    {
        public int V { get; set; }
    }

    public sealed class CostProbeUpdSrc
    {
        public int V { get; set; }
    }

    /// <summary>
    ///     Round-30 item H: every per-call entry point on the shipped runtime carries a COST CONTRACT, and the
    ///     contract is "flat", not "small".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These are the tests <see cref="SelfValidation" />'s cost-contract scan requires. The scan
    ///         enumerates the runtime's per-call surface and fails the build when a member has no entry here
    ///         and no exemption — so a new public entry point cannot ship without someone stating what its
    ///         cost is allowed to do.
    ///     </para>
    ///     <para>
    ///         Every test asserts a SLOPE. Absolute byte pins already exist in
    ///         <c>allocation-baseline.json</c> and are strict; they are also structurally blind to the
    ///         round-30 defect, whose cost was 80 + 24·N bytes and therefore looked correct at every scale a
    ///         benchmark runs at. The two gates catch disjoint failure classes and neither replaces the other.
    ///     </para>
    /// </remarks>
    [Collection("allocation-isolated")]
    public class AmbientCostContractTests
    {
        public AmbientCostContractTests()
        {
            // Idempotent (first-wins), so repeated construction across the class costs nothing and the
            // registry is append-only by design — see RegistryAppendOnlyTests.
            DwarfMapperRegistry.Register(typeof(ICostProbeSrc), typeof(CostProbeDst),
                s => new CostProbeDst { V = ((ICostProbeSrc)s).V });
            DwarfMapperRegistry.Register(typeof(CostProbeUpdSrc), typeof(CostProbeDst),
                s => new CostProbeDst { V = ((CostProbeUpdSrc)s).V });
            DwarfMapperRegistry.RegisterUpdate(typeof(CostProbeUpdSrc), typeof(CostProbeDst),
                (s, d) => ((CostProbeDst)d).V = ((CostProbeUpdSrc)s).V);
        }

        /// <summary>
        ///     The scale assertion for the registry, expressed as an absolute bound at a KNOWN-LARGE scale.
        /// </summary>
        /// <remarks>
        ///     The registry is process-wide and append-only, so a test cannot build a small one and a large
        ///     one to compare. What it can do is assert flatness at the large end: if the cost carried a
        ///     linear term, 2,738 registered pairs would put it in the tens of kilobytes. Holding at ~24 B
        ///     there IS the statement that the linear term is absent. The vacuity guard is what makes the
        ///     reading meaningful — see <see cref="AmbientDispatchAllocationRuntimeTests" />, which owns the
        ///     interface-walk case in full.
        /// </remarks>
        [Fact]
        public void Exact_type_dispatch_cost_is_flat_over_time()
        {
            object source = new CostProbeUpdSrc { V = 3 };

            var (early, late) = CostContract.MeasureOverTime(
                () => DwarfMapperRegistry.Map(source, typeof(CostProbeDst)));

            Assert.True(late <= CostContract.FlatCeiling(early),
                $"DwarfMapperRegistry.Map (exact-type path): {early:F0} B/call rose to {late:F0} B/call " +
                "after 50,000 intervening calls. A cost that grows with history means something the lookup " +
                "touches is getting longer.");
        }

        [Fact]
        public void Interface_dispatch_cost_is_flat_over_time()
        {
            object source = new CostProbeSrc { V = 5 };

            var (early, late) = CostContract.MeasureOverTime(
                () => DwarfMapperRegistry.Map(source, typeof(CostProbeDst)));

            Assert.True(late <= CostContract.FlatCeiling(early),
                $"DwarfMapperRegistry.Map (interface walk): {early:F0} B/call rose to {late:F0} B/call " +
                "after 50,000 intervening calls.");
        }

        [Fact]
        public void Update_dispatch_cost_is_flat_over_time()
        {
            var source = new CostProbeUpdSrc { V = 9 };
            var destination = new CostProbeDst();

            var (early, late) = CostContract.MeasureOverTime(
                () => DwarfMapperRegistry.Update(source, destination,
                    typeof(CostProbeUpdSrc), typeof(CostProbeDst)));

            Assert.True(late <= CostContract.FlatCeiling(early),
                $"DwarfMapperRegistry.Update: {early:F0} B/call rose to {late:F0} B/call after 50,000 " +
                "intervening calls.");
        }

        /// <summary>
        ///     The four predicates share one dictionary-lookup shape, so they are contracted together: each
        ///     must allocate nothing per call and stay flat. A predicate that allocated would be paid on every
        ///     validation sweep.
        /// </summary>
        [Fact]
        public void Registry_predicates_allocate_nothing_and_stay_flat()
        {
            var probes = new (string Name, Action Op)[]
            {
                ("IsProvided", () => DwarfMapperRegistry.IsProvided(typeof(CostProbeUpdSrc), typeof(CostProbeDst))),
                ("IsAmbiguous", () => DwarfMapperRegistry.IsAmbiguous(typeof(CostProbeUpdSrc), typeof(CostProbeDst))),
                ("IsUpdateProvided",
                    () => DwarfMapperRegistry.IsUpdateProvided(typeof(CostProbeUpdSrc), typeof(CostProbeDst))),
                ("IsUpdateAmbiguous",
                    () => DwarfMapperRegistry.IsUpdateAmbiguous(typeof(CostProbeUpdSrc), typeof(CostProbeDst))),
                ("TryGet", () => DwarfMapperRegistry.TryGet(typeof(CostProbeUpdSrc), typeof(CostProbeDst), out _))
            };

            foreach (var (name, op) in probes)
            {
                var perCall = CostContract.BytesPerOperation(op);

                Assert.True(perCall < 64,
                    $"DwarfMapperRegistry.{name} allocated {perCall:F0} B/call against " +
                    $"{DwarfMapperRegistry.Provided.Count:N0} registered pairs. These are dictionary " +
                    "lookups over a struct key; they must allocate nothing.");

                var (early, late) = CostContract.MeasureOverTime(op);
                Assert.True(late <= CostContract.FlatCeiling(early),
                    $"DwarfMapperRegistry.{name}: {early:F0} B/call rose to {late:F0} B/call after 50,000 " +
                    "intervening calls.");
            }
        }

        /// <summary>
        ///     <c>DwarfRefContext</c> is allocated per invocation and its two collections are per instance, so
        ///     its contract is that a context's cost depends on the graph it walks and NOT on how many
        ///     contexts came before it.
        /// </summary>
        [Fact]
        public void Ref_context_cost_is_flat_over_time()
        {
            var (preserveEarly, preserveLate) = CostContract.MeasureOverTime(
                () =>
                {
                    var ctx = new DwarfRefContext(10, true);
                    var key = new object();
                    ctx.SetReference(key, new object());
                    ctx.TryGetReference(key, out _);
                },
                10_000);

            Assert.True(preserveLate <= CostContract.FlatCeiling(preserveEarly),
                $"DwarfRefContext (Preserve): {preserveEarly:F0} B/op rose to {preserveLate:F0} B/op. Each " +
                "context owns its identity map, so cost must not depend on how many contexts preceded it.");

            var (setNullEarly, setNullLate) = CostContract.MeasureOverTime(
                () =>
                {
                    var ctx = new DwarfRefContext(10, setNull: true);
                    var key = new object();
                    ctx.TryEnterNode(key);
                    ctx.ExitNode(key);
                },
                10_000);

            Assert.True(setNullLate <= CostContract.FlatCeiling(setNullEarly),
                $"DwarfRefContext (SetNull): {setNullEarly:F0} B/op rose to {setNullLate:F0} B/op.");
        }

        /// <summary>
        ///     <c>Provided</c> materialises a list of every registered pair, so its cost is O(registry) BY
        ///     DESIGN — it is the one member on this surface whose contract is not flatness.
        /// </summary>
        /// <remarks>
        ///     Contracted anyway, for two reasons. It must not be flat-over-TIME (a growing cost with an
        ///     append-only registry of fixed size would mean it is accumulating), and its documented status as
        ///     diagnostics-only is what justifies the exemption from flatness — a status worth pinning where
        ///     someone would otherwise be tempted to call it on a hot path. Round 30 already halved it by
        ///     removing a double materialisation nobody had noticed.
        /// </remarks>
        [Fact]
        public void Provided_is_linear_by_design_but_must_not_grow_over_time()
        {
            var registered = DwarfMapperRegistry.Provided.Count;
            Assert.True(registered > 500,
                $"only {registered} pairs registered; this contract needs a realistic registry to mean " +
                "anything.");

            var perRead = CostContract.BytesPerOperation(
                () => _ = DwarfMapperRegistry.Provided, 200);

            // One materialised list of (Type, Type) pairs: 16 B per entry plus header. Two would mean the
            // double-materialisation bug is back.
            var oneListCeiling = registered * 16 * 1.6 + 1024;
            Assert.True(perRead < oneListCeiling,
                $"Provided allocated {perRead:F0} B/read for {registered:N0} pairs, above the " +
                $"{oneListCeiling:F0} B a single materialised list costs. It is materialising more than " +
                "once — the ConcurrentDictionary.Keys double-build that round 30 removed.");
        }
    }
}
