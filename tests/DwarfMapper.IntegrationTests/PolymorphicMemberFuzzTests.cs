// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.IntegrationTests
{
    // ── An abstract-typed MEMBER, and an abstract dictionary VALUE ───────────────────────────────────────
//
// The corpus already covers abstract types as the polymorphic ROOT of a pair — that is what
// [MapDerivedType] is for, and CombinatorialSchema/SyntheticSchema both emit `abstract class Src`.
//
// What it did NOT cover is an abstract type appearing as a MEMBER of a graph, and in particular as a
// dictionary VALUE. That is the shape Round 18 hit: a Dictionary<BotPlatform, LexiconBaseDto> whose value
// type is abstract. ObjectFactory returned null for it, so every fixture built from that graph exercised the
// null path instead of the dispatch path, and nine pairs looked like a mapper regression when the fixtures
// were the problem.
//
// The factory was fixed (it now substitutes a concrete implementation) and the whole suite came back green —
// which is the finding, not the reassurance: if the corpus had contained this shape, un-vacuum-ing it should
// have changed something. It did not, because the shape was absent. These fixtures add it.

    public abstract class AlertRule
    {
        public string Name { get; set; } = "";
    }

    public sealed class ThresholdRule : AlertRule
    {
        public int Threshold { get; set; }
    }

    public abstract class AlertRuleDto
    {
        public string Name { get; set; } = "";
    }

    public sealed class ThresholdRuleDto : AlertRuleDto
    {
        public int Threshold { get; set; }
    }

    /// <summary>A graph whose members are abstract — including a dictionary VALUE, the Round-18 shape.</summary>
    public sealed class AlertProfile
    {
        public string Owner { get; set; } = "";

        public AlertRule Primary { get; set; } = new ThresholdRule();

        public Dictionary<string, AlertRule> ByChannel { get; set; } = [];
    }

    public sealed class AlertProfileDto
    {
        public string Owner { get; set; } = "";

        public AlertRuleDto Primary { get; set; } = new ThresholdRuleDto();

        public Dictionary<string, AlertRuleDto> ByChannel { get; set; } = [];
    }

    [DwarfMapper]
    public partial class AlertProfileMappers
    {
        public partial AlertProfileDto ToDto(AlertProfile source);

        [MapDerivedType<ThresholdRule, ThresholdRuleDto>]
        public partial AlertRuleDto ToRuleDto(AlertRule source);
    }

    /// <summary>
    ///     Fuzz-driven mapping over a graph with abstract-typed members — the coverage hole the ObjectFactory fix
    ///     revealed rather than closed.
    /// </summary>
    public sealed class PolymorphicMemberFuzzTests
    {
        /// <summary>
        ///     A profile whose abstract member and dictionary values are all MATERIALISED, found by walking
        ///     seeds from <paramref name="from" />.
        /// </summary>
        /// <remarks>
        ///     These tests are about polymorphic DISPATCH, which needs a non-null abstract member to dispatch
        ///     on. Under the old ObjectFactory a fixed seed always produced one, because that factory never
        ///     returned null for anything. The merged factory draws nulls deliberately — that was the fuzz
        ///     suite's largest blind spot — so a fixed seed is no longer a reliable way to get a populated
        ///     graph, and pinning one would make these tests pass or fail on which seed happened to be lucky.
        ///     <para>
        ///         Walking is deterministic (the seed sequence is fixed), so a failure still replays exactly.
        ///         Running out of seeds fails loudly rather than silently testing the null path, which is the
        ///         precondition <c>The_fixture_factory_populates_an_abstract_member_rather_than_nulling_it</c>
        ///         exists to guard.
        ///     </para>
        /// </remarks>
        private static AlertProfile PopulatedProfile(int from)
        {
            for (var seed = from; seed < from + 500; seed++)
            {
                var p = ObjectFactoryV2.Create<AlertProfile>(seed);
                if (p.Primary is not null && p.ByChannel is { Count: > 0 } && p.ByChannel.Values.All(v => v is not null))
                {
                    return p;
                }
            }

            Assert.Fail($"No seed in [{from}, {from + 500}) produced a fully-materialised AlertProfile. Either "
                + "abstract substitution regressed, or NullProbability has been raised so far that these "
                + "dispatch tests can no longer get a graph to dispatch on.");
            return null!;
        }

        [Fact]
        public void The_fixture_factory_populates_an_abstract_member_rather_than_nulling_it()
        {
            // The precondition. If this regresses, every mapping assertion below silently starts testing the
            // null path instead — which is exactly how nine phantom failures were produced.
            var profile = PopulatedProfile(11);

            Assert.NotNull(profile.Primary);
            Assert.IsAssignableFrom<AlertRule>(profile.Primary);
        }

        [Fact]
        public void An_abstract_dictionary_value_is_populated_rather_than_nulled()
        {
            // The exact Round-18 shape: Dictionary<K, AbstractValue>. A null VALUE here is indistinguishable
            // from a mapper bug when the graph is replayed against a mapper that (correctly) refuses nulls.
            var profile = PopulatedProfile(7);

            Assert.NotEmpty(profile.ByChannel);
            Assert.All(profile.ByChannel.Values, Assert.NotNull);
        }

        /// <summary>
        ///     Fast = exactly the original five hand-picked seeds; deep (fast 5 / deep 45 — see
        ///     <see cref="DeepPopulation.PolymorphicGraphSeeds" />) extends with the contiguous seeds 9-48,
        ///     which do not repeat the fast five.
        /// </summary>
        public static IEnumerable<object[]> GraphSeeds()
        {
            int[] fastSeeds = [1, 2, 3, 5, 8];
            var n = DeepTier.Count(DeepPopulation.PolymorphicGraphSeeds);
            for (var i = 0; i < n; i++)
                yield return [i < fastSeeds.Length ? fastSeeds[i] : i + 4];
        }

        [Theory]
        [MemberData(nameof(GraphSeeds))]
        public void Mapping_a_fuzzed_abstract_membered_graph_dispatches_on_the_runtime_type(int seed)
        {
            var profile = PopulatedProfile(seed);
            var dto = new AlertProfileMappers().ToDto(profile);

            Assert.Equal(profile.Owner, dto.Owner);

            // The member's DECLARED type is abstract; its runtime type drives which arm runs. Asserting the
            // derived member survives is what makes this more than a null check.
            Assert.NotNull(dto.Primary);
            Assert.Equal(profile.Primary.Name, dto.Primary.Name);

            if (profile.Primary is ThresholdRule threshold)
            {
                var mapped = Assert.IsType<ThresholdRuleDto>(dto.Primary);
                Assert.Equal(threshold.Threshold, mapped.Threshold);
            }
        }

        [Fact]
        public void Every_abstract_dictionary_value_survives_the_map()
        {
            var profile = PopulatedProfile(13);
            var dto = new AlertProfileMappers().ToDto(profile);

            Assert.Equal(profile.ByChannel.Count, dto.ByChannel.Count);
            Assert.All(dto.ByChannel.Values, Assert.NotNull);

            foreach (var (key, rule) in profile.ByChannel)
            {
                Assert.True(dto.ByChannel.TryGetValue(key, out var mapped));
                Assert.Equal(rule.Name, mapped.Name);
            }
        }

        [Fact]
        public void The_substituted_concrete_type_is_stable_for_a_seed()
        {
            // Fixtures must be reproducible or a failure cannot be re-run. Candidates are ordered by full name
            // before the draw precisely so the choice does not drift with assembly enumeration order.
            var a = PopulatedProfile(4);
            var b = PopulatedProfile(4);

            Assert.Equal(a.Primary.GetType(), b.Primary.GetType());
            Assert.Equal(a.Primary.Name, b.Primary.Name);
        }
    }
}
