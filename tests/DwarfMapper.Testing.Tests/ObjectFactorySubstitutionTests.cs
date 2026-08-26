// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Testing.Tests
{
    public class Sample
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public int? Maybe { get; set; }

        public List<int> Nums { get; set; } = new();

        public Nested Child { get; set; } = new();
    }

    public class Nested
    {
        public string City { get; set; } = "";
    }

// ── Fixtures for the abstract/interface substitution tests ────────────────────────────────────────
    public abstract class ShapeBase
    {
        public int Sides { get; set; }
    }

    public sealed class Square : ShapeBase
    {
        public string Label { get; set; } = "";
    }

    public interface IHasTitle
    {
        string Title { get; set; }
    }

    public sealed class Article : IHasTitle
    {
        public string Title { get; set; } = "";
    }

    public class HoldsAbstract
    {
        public ShapeBase Shape { get; set; } = new Square();

        public IHasTitle Titled { get; set; } = new Article();

        public Dictionary<string, ShapeBase> ByName { get; set; } = new();
    }

    /// <summary>
    ///     Abstract and interface members get a CONCRETE instance rather than being left null forever.
    ///     <para>
    ///         REGRESSION, found migrating a ~300-map codebase off AutoMapper. The factory used to return null
    ///         for any abstract or interface type, which made polymorphic graphs untestable — exactly the shape
    ///         <c>[MapDerivedType]</c> exists to map. A <c>Dictionary&lt;K, AbstractValue&gt;</c> came out with
    ///         null VALUES, so every fixture built from it exercised the null path rather than the dispatch
    ///         path, and then looked like a real behavioural difference when replayed against a mapper that
    ///         correctly refuses nulls.
    ///     </para>
    ///     <para>
    ///         CARRIED FORWARD 2026-08-26, when the two factories were merged into <c>ObjectFactoryV2</c>.
    ///         These assertions moved with the fix they guard: V2's abstract branch carried the comment
    ///         "try to pick a concrete" above a <c>return null</c>, so the regression had silently come back in
    ///         the successor while the original factory still held the tests proving it fixed.
    ///     </para>
    ///     <para>
    ///         The assertions are DISTRIBUTIONAL rather than single-seed, because the merged factory also draws
    ///         nulls deliberately (<c>NullProbability</c>) — V1 never producing one was the largest blind spot
    ///         in the fuzz suite. Across a span of seeds an abstract member must sometimes be materialised, and
    ///         every time it is, it must be a concrete subtype. That still fails loudly if substitution breaks
    ///         — "always null" is caught — while tolerating the intentional nulls, and sampling many seeds
    ///         removes the old dependency on seed 7 happening to draw non-null.
    ///     </para>
    /// </summary>
    public class ObjectFactorySubstitutionTests
    {
        private const int Seeds = 60;

        private static IEnumerable<HoldsAbstract> Graphs()
        {
            for (var seed = 0; seed < Seeds; seed++)
            {
                yield return ObjectFactoryV2.Create<HoldsAbstract>(seed);
            }
        }

        [Fact]
        public void An_abstract_member_is_sometimes_materialised_and_is_always_a_concrete_subtype()
        {
            var shapes = Graphs().Select(g => g.Shape).ToList();

            Assert.Contains(shapes, s => s is not null);
            Assert.All(shapes.Where(s => s is not null), s => Assert.IsType<Square>(s));
        }

        [Fact]
        public void An_interface_member_is_sometimes_materialised_and_is_always_a_concrete_implementation()
        {
            var titled = Graphs().Select(g => g.Titled).ToList();

            Assert.Contains(titled, t => t is not null);
            Assert.All(titled.Where(t => t is not null), t => Assert.IsType<Article>(t));
        }

        [Fact]
        public void A_dictionary_of_abstract_values_is_sometimes_populated_with_concrete_values()
        {
            // The case that surfaced the original bug: abstract dictionary VALUES were always null, which is
            // indistinguishable from a mapper defect when the fixture is replayed.
            var values = Graphs()
                .Where(g => g.ByName is not null)
                .SelectMany(g => g.ByName.Values)
                .ToList();

            Assert.Contains(values, v => v is not null);
            Assert.All(values.Where(v => v is not null), v => Assert.IsType<Square>(v));
        }

        [Fact]
        public void Substitution_is_deterministic_for_a_seed()
        {
            // Candidates are ordered by full name before the draw, so the choice must not drift with whatever
            // order the runtime happens to enumerate assemblies in.
            for (var seed = 0; seed < 10; seed++)
            {
                var a = ObjectFactoryV2.Create<HoldsAbstract>(seed);
                var b = ObjectFactoryV2.Create<HoldsAbstract>(seed);

                Assert.Equal(a.Shape?.GetType(), b.Shape?.GetType());
                Assert.Equal(a.Shape?.Sides, b.Shape?.Sides);
                Assert.Equal(a.Titled?.GetType(), b.Titled?.GetType());
            }
        }

        [Fact]
        public void A_plain_graph_is_deterministic_per_seed_and_varies_across_seeds()
        {
            var a = ObjectFactoryV2.Create<Sample>(42);
            var b = ObjectFactoryV2.Create<Sample>(42);

            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Child?.City, b.Child?.City);

            // Across seeds rather than between two of them: the merged factory draws boundary values on
            // purpose, so two particular seeds can legitimately agree on one field — comparing seed 1 against
            // seed 2 on Id alone, as the original test did, would now be flaky by construction.
            var ids = Enumerable.Range(0, Seeds).Select(s => ObjectFactoryV2.Create<Sample>(s).Id).Distinct().Count();

            Assert.True(ids > 1, $"Every one of {Seeds} seeds produced the same Id — the factory is not seeded.");
        }

        [Fact]
        public void Members_are_sometimes_populated_rather_than_uniformly_default()
        {
            // The remnant of Populates_all_members. It cannot assert "every member is non-default" any more,
            // because the merged factory draws zero, empty and null deliberately. What must still hold is that
            // it populates SOMETHING — an all-defaults factory would pass every mapping test while proving
            // nothing, which is the failure this suite exists to prevent.
            var samples = Enumerable.Range(0, Seeds).Select(ObjectFactoryV2.Create<Sample>).ToList();

            Assert.Contains(samples, s => s.Id != 0);
            Assert.Contains(samples, s => !string.IsNullOrEmpty(s.Name));
            Assert.Contains(samples, s => s.Maybe is not null);
            Assert.Contains(samples, s => s.Nums is { Count: > 0 });
            Assert.Contains(samples, s => !string.IsNullOrEmpty(s.Child?.City));
        }
    }
}
