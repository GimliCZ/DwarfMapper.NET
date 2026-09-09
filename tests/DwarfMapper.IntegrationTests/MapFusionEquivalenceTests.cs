// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;

namespace DwarfMapper.IntegrationTests
{
    public sealed class FuseSource
    {
        public int Id { get; set; }

        public long Score { get; set; }

        public double Weight { get; set; }

        public int Extra { get; set; }

        public string? Label { get; set; }
    }

    /// <summary>The intermediate. The whole fusion question is whether it can be skipped.</summary>
    public sealed class FuseMiddle
    {
        public int Id { get; set; }

        public long Score { get; set; }

        public double Weight { get; set; }

        public int Extra { get; set; }

        public string? Label { get; set; }
    }

    public sealed class FuseTarget
    {
        public int Id { get; set; }

        public long Score { get; set; }

        public double Weight { get; set; }

        public int Extra { get; set; }

        public string? Label { get; set; }
    }

    [DwarfMapper]
    public partial class FusionMappers
    {
        public partial FuseMiddle SourceToMiddle(FuseSource s);

        public partial FuseTarget MiddleToTarget(FuseMiddle s);

        /// <summary>What a fused emission would produce, expressed as an ordinary member-wise map.</summary>
        public partial FuseTarget SourceToTarget(FuseSource s);
    }

    /// <summary>
    ///     <b>The oracle the fusion measurement should have shipped with, and did not.</b>
    ///     <para>
    ///         <c>Issues/round29/SPIKE-map-fusion.md</c> and <c>FusionProbeBenchmarks</c> compare a CHAINED
    ///         <c>A -&gt; B -&gt; C</c> against a DIRECT <c>A -&gt; C</c> and report the chained path allocating one
    ///         extra object per element. Both are allocation measurements. <b>Neither of them ever checked that
    ///         the two paths produce the same answer.</b>
    ///     </para>
    ///     <para>
    ///         That omission has already cost this repository once, in the same shape. <c>docs/COMPARISON.md</c>
    ///         published "DwarfMapper is 3.98x slower than Mapperly on enums" for months; the gap was real and
    ///         the comparison was not, because the two rows were running different strategies — a by-name switch
    ///         against a by-value cast — on deliberately reordered enums where the cast maps <c>Pending</c> to
    ///         <c>Closed</c>. Roughly a 4x "win" was partly the price of being wrong
    ///         (<c>benchmarks/results/2026-08-26-enum-strategy-like-for-like.md</c>).
    ///     </para>
    ///     <para>
    ///         A fusion benchmark has exactly that exposure: if <c>SourceToTarget</c> and
    ///         <c>MiddleToTarget(SourceToMiddle(x))</c> disagree on any member, then "fusion saves an allocation"
    ///         is comparing two different operations and the saving is partly the price of computing something
    ///         else. <b>Equivalence is the PREMISE of the measurement, so it is asserted here before any figure
    ///         from that measurement may be quoted.</b>
    ///     </para>
    ///     <para>
    ///         Payloads come from <see cref="ObjectFactoryV2" /> — the fuzz/fixture source the rest of the suite
    ///         uses — so the comparison sees nulls, boundary numerics and varied string lengths rather than the
    ///         uniform literals a hand-written case would supply. Structural comparison rather than member-by-member
    ///         asserts, so a member ADDED to the three types later is covered without editing this file.
    ///     </para>
    /// </summary>
    public class MapFusionEquivalenceTests
    {
        private const int Draws = 512;

        [Fact]
        public void The_chained_path_and_the_direct_path_agree_on_every_member()
        {
            var m = new FusionMappers();
            var mismatches = new List<string>();

            for (var i = 0; i < Draws; i++)
            {
                var src = ObjectFactoryV2.Create<FuseSource>(20260909 + i);

                var chained = m.MiddleToTarget(m.SourceToMiddle(src));
                var direct = m.SourceToTarget(src);

                var diffs = StructuralComparer.Diff(chained, direct);
                if (diffs.Count > 0)
                {
                    mismatches.Add($"seed {20260909 + i}:\n{StructuralComparer.Render(diffs)}");
                }
            }

            Assert.True(mismatches.Count == 0,
                "A -> B -> C and A -> C do not produce the same result, so every fusion figure in " +
                "Issues/round29/SPIKE-map-fusion.md compares two different operations and the allocation " +
                "saving is partly the price of computing something else — the enum row's mistake, in the " +
                "shape that mistake actually takes.\n" + string.Join("\n", mismatches.Take(3)));
        }

        [Fact]
        public void The_intermediate_carries_everything_the_target_needs_so_fusing_cannot_drop_a_member()
        {
            // The equivalence above holds vacuously if the chain loses a member the direct map also never
            // sets — two empty answers agree. This asserts the CHAIN is lossless in its own right: what the
            // source carries reaches the middle, and what the middle carries reaches the target. A member
            // added to FuseSource but forgotten in FuseMiddle would pass the test above and fail this one.
            var m = new FusionMappers();
            var offenders = new List<string>();

            for (var i = 0; i < Draws; i++)
            {
                var src = ObjectFactoryV2.Create<FuseSource>(777_000 + i);
                var mid = m.SourceToMiddle(src);

                if (mid.Id != src.Id || mid.Score != src.Score || mid.Extra != src.Extra ||
                    !mid.Weight.Equals(src.Weight) || !string.Equals(mid.Label, src.Label, StringComparison.Ordinal))
                {
                    offenders.Add($"seed {777_000 + i}: source -> middle lost a member");
                    continue;
                }

                var tgt = m.MiddleToTarget(mid);
                if (tgt.Id != mid.Id || tgt.Score != mid.Score || tgt.Extra != mid.Extra ||
                    !tgt.Weight.Equals(mid.Weight) || !string.Equals(tgt.Label, mid.Label, StringComparison.Ordinal))
                {
                    offenders.Add($"seed {777_000 + i}: middle -> target lost a member");
                }
            }

            Assert.True(offenders.Count == 0,
                "The chain is lossy, which makes the equivalence test above agree for the wrong reason:\n  " +
                string.Join("\n  ", offenders.Take(5)));
        }

        [Fact]
        public void The_comparison_really_compares_and_would_report_a_divergence()
        {
            // The control. Both tests above pass if StructuralComparer never reports anything — a comparer
            // handed two objects it cannot walk is also silent, and a silent oracle certifies whatever it is
            // pointed at. This plants a KNOWN divergence and requires it to be seen, naming the member.
            var a = new FuseTarget { Id = 1, Score = 2, Weight = 3.0, Extra = 4, Label = "x" };
            var b = new FuseTarget { Id = 1, Score = 2, Weight = 3.0, Extra = 4, Label = "y" };

            var diffs = StructuralComparer.Diff(a, b);

            Assert.True(diffs.Count > 0,
                "StructuralComparer reported no difference between two objects that differ in Label, so the " +
                "equivalence assertions beside this one are measuring nothing.");
            Assert.Contains(diffs, d => d.Path.Contains(nameof(FuseTarget.Label), StringComparison.Ordinal));
        }
    }
}
