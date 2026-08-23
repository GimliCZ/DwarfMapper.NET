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

    public class ObjectFactoryTests
    {
        // ---------------------------------------------------------------------------------------------
        // Regression: found migrating a ~300-map codebase off AutoMapper.
        //
        // The factory used to return null for any abstract or interface type, which made polymorphic
        // graphs untestable — exactly the shape [MapDerivedType] exists to map. A
        // Dictionary<K, AbstractValue> came out with null VALUES, so every fixture built from it exercised
        // the null path rather than the dispatch path, and then looked like a real behavioural difference
        // when replayed against a mapper that (correctly) refuses nulls.
        // ---------------------------------------------------------------------------------------------

        [Fact]
        public void Substitutes_a_concrete_type_for_an_abstract_member()
        {
            var h = ObjectFactory.Create<HoldsAbstract>(7);

            Assert.NotNull(h.Shape);
            Assert.IsType<Square>(h.Shape);
            Assert.NotEqual(0, h.Shape.Sides);
        }

        [Fact]
        public void Substitutes_a_concrete_type_for_an_interface_member()
        {
            var h = ObjectFactory.Create<HoldsAbstract>(7);

            Assert.NotNull(h.Titled);
            Assert.IsType<Article>(h.Titled);
            Assert.NotEqual("", h.Titled.Title);
        }

        [Fact]
        public void Dictionary_with_an_abstract_value_type_gets_populated_values()
        {
            // The case that surfaced this: null values here are indistinguishable from a mapper bug.
            var h = ObjectFactory.Create<HoldsAbstract>(11);

            Assert.NotEmpty(h.ByName);
            Assert.All(h.ByName.Values, Assert.NotNull);
        }

        [Fact]
        public void Abstract_substitution_stays_deterministic_for_a_seed()
        {
            // Candidates are ordered by full name before the draw, so the choice must not drift with
            // whatever order the runtime happens to enumerate assemblies in.
            var a = ObjectFactory.Create<HoldsAbstract>(3);
            var b = ObjectFactory.Create<HoldsAbstract>(3);

            Assert.Equal(a.Shape.GetType(), b.Shape.GetType());
            Assert.Equal(a.Shape.Sides, b.Shape.Sides);
        }

        [Fact]
        public void Populates_all_members()
        {
            var s = ObjectFactory.Create<Sample>(1);
            Assert.NotEqual(0, s.Id);
            Assert.NotEqual("", s.Name);
            Assert.NotNull(s.Maybe);
            Assert.NotEmpty(s.Nums);
            Assert.NotEqual("", s.Child.City);
        }

        [Fact]
        public void Same_seed_is_deterministic()
        {
            var a = ObjectFactory.Create<Sample>(42);
            var b = ObjectFactory.Create<Sample>(42);
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Child.City, b.Child.City);
        }

        [Fact]
        public void Different_seeds_differ()
        {
            var a = ObjectFactory.Create<Sample>(1);
            var b = ObjectFactory.Create<Sample>(2);
            Assert.NotEqual(a.Id, b.Id);
        }
    }
}
