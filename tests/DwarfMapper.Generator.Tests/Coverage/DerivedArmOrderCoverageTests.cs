// SPDX-License-Identifier: GPL-2.0-only

// Coverage for MapperExtractor.SortArmsMostDerivedFirst's depth tiebreak. Arms are ordered most-derived first so a switch
// never tests a base type before a subtype. When neither arm's source is assignable to the other, pairwise assignability
// cannot order them and the inheritance depth decides. That tiebreak had never executed in the full suite: every
// multi-arm fixture either related its sources or put them at the same depth.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DerivedArmOrderCoverageTests
    {
        [Fact]
        public void Unrelated_arms_at_different_depths_put_the_deeper_source_first()
        {
            // Cat : Feline : Animal is two levels down; Dog : Animal is one. Neither converts to the other.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { }
                               public abstract class Feline : Animal { }
                               public class Cat : Feline { }
                               public abstract class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { }
                               public class CatDto : AnimalDto { }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   [MapDerivedType<Cat, CatDto>]
                                   public partial AnimalDto Map(Animal a);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            var cat = generated.IndexOf("global::Demo.Cat __s =>", StringComparison.Ordinal);
            var dog = generated.IndexOf("global::Demo.Dog __s =>", StringComparison.Ordinal);
            Assert.True(cat >= 0 && dog > cat, generated);
        }
    }
}
