// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    public sealed class FoSource
    {
        public int Id { get; set; }

        public int Raw { get; set; }
    }

    /// <summary>
    ///     The intermediate. <c>Tag</c> is produced by a side-effecting converter and is <b>not consumed by
    ///     <see cref="FoTarget" /></b> — that asymmetry is the whole experiment.
    /// </summary>
    public sealed class FoMiddle
    {
        public int Id { get; set; }

        public string? Tag { get; set; }
    }

    public sealed class FoTarget
    {
        public int Id { get; set; }
    }

    public static class FoCounter
    {
        private static int _calls;

        public static int Calls => _calls;

        public static void Reset()
        {
            _calls = 0;
        }

        public static string Stamp(int raw)
        {
            _calls++;
            return "t" + raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    [DwarfMapper]
    public partial class FoMappers
    {
        /// <summary>The side-effecting converter, declared on the mapper because that is where Use resolves.</summary>
        public static string Stamp(int raw)
        {
            return FoCounter.Stamp(raw);
        }

        [MapProperty(nameof(FoSource.Raw), nameof(FoMiddle.Tag), Use = nameof(Stamp))]
        public partial FoMiddle SourceToMiddle(FoSource s);

        public partial FoTarget MiddleToTarget(FoMiddle m);

        public partial FoTarget SourceToTarget(FoSource s);
    }

    /// <summary>
    ///     <b>The criterion that decides most of the fusion refusal list, measured instead of argued.</b>
    ///     <para>
    ///         <c>Issues/round30/SPEC-fusion-refusal-list.md</c> left six rows marked PROVE, three of which were
    ///         variations on the same worry: a user-declared converter, a <c>[MapValue]</c> expression, and a
    ///         user constructor on the <c>A -&gt; B</c> pair "may have a side effect". Stated that way each needs
    ///         its own investigation. Stated sharply they collapse into one rule, and this test is that rule:
    ///     </para>
    ///     <blockquote>
    ///         The chained form computes <b>every</b> member of <c>B</c>. The fused form computes only what
    ///         <c>C</c> consumes. So any member of <c>B</c> that <c>C</c> does NOT read, whose production runs
    ///         user code, is a side effect that fusion deletes.
    ///     </blockquote>
    ///     <para>
    ///         That is sharper than "converters may have side effects" because it is decidable at generation
    ///         time from the two member maps alone — no interprocedural purity analysis, no attribute for the
    ///         consumer to remember. It also explains why the worry is real rather than theoretical: the
    ///         converter here is called once per mapped object in the chained form and would be called zero
    ///         times fused, and nothing about the two type declarations hints at it.
    ///     </para>
    ///     <para>
    ///         <b>This test does not test fusion</b> — there is no emitter, which is the point of the PROVE
    ///         gate. It pins the CHAINED form's observable behaviour, which is the baseline any future fused
    ///         emission has to preserve. If the emitter ever ships and this test still passes while a fused
    ///         path is taken here, the emitter is wrong.
    ///     </para>
    /// </summary>
    public class FusionObservabilityTests
    {
        [Fact]
        public void A_member_of_the_intermediate_that_the_target_ignores_still_runs_its_converter()
        {
            var m = new FoMappers();
            FoCounter.Reset();

            var mid = m.SourceToMiddle(new FoSource { Id = 1, Raw = 42 });
            var tgt = m.MiddleToTarget(mid);

            Assert.Equal(1, tgt.Id);
            Assert.Equal("t42", mid.Tag);

            // THE OBSERVABLE THING. FoTarget has no Tag, so a fused A -> C would have no reason to call Stamp
            // at all. The chained form calls it exactly once per mapped object.
            Assert.Equal(1, FoCounter.Calls);
        }

        [Fact]
        public void The_direct_map_does_not_call_it_which_is_exactly_what_fusion_would_do()
        {
            // The other half of the same fact, and the reason this is a REFUSE rather than a caveat: the direct
            // A -> C map -- which is what a fused emission produces -- is right here, and it calls Stamp zero
            // times. Chained and fused are therefore NOT interchangeable for this pair, and no amount of
            // measuring allocation would have revealed it.
            var m = new FoMappers();
            FoCounter.Reset();

            var tgt = m.SourceToTarget(new FoSource { Id = 1, Raw = 42 });

            Assert.Equal(1, tgt.Id);
            Assert.Equal(0, FoCounter.Calls);
        }

        [Fact]
        public void Per_element_the_gap_is_one_converter_call_per_item()
        {
            // Scaled to the shape fusion actually targets: an element loop. The chained form pays N calls, the
            // fused form pays none, so the divergence grows with N rather than being a one-off.
            var m = new FoMappers();
            FoCounter.Reset();

            var src = Enumerable.Range(0, 50).Select(i => new FoSource { Id = i, Raw = i }).ToArray();
            foreach (var s in src) { m.MiddleToTarget(m.SourceToMiddle(s)); }

            Assert.Equal(50, FoCounter.Calls);

            FoCounter.Reset();
            foreach (var s in src) { m.SourceToTarget(s); }

            Assert.Equal(0, FoCounter.Calls);
        }
    }
}
