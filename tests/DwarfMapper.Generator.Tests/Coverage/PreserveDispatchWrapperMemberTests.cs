// SPDX-License-Identifier: GPL-2.0-only

// SynthesizePreserveDispatchWrappers redirects a caller of a recursion-capable public [MapDerivedType] dispatch method
// to the private ctx-accepting wrapper, so two members reaching one source object land in ONE identity map. The
// constructor-argument arm was pinned (RecursionContextPropagationCoverageTests); the MEMBER arm — the far more common
// settable-property shape — had never executed.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PreserveDispatchWrapperMemberTests
    {
        [Fact]
        public void Members_calling_an_acyclic_dispatch_method_go_through_one_shared_context_wrapper()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public abstract class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               public class Zoo { public Animal First { get; set; } = new Dog(); public Animal Second { get; set; } = new Dog(); }
                               public class ZooDto { public AnimalDto First { get; set; } = new DogDto(); public AnimalDto Second { get; set; } = new DogDto(); }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial ZooDto ToZoo(Zoo z);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            const string wrapper = "__DwarfMap_Disp_global__Demo_Animal_global__Demo_AnimalDto_";
            Assert.Contains("__dwarf_t.First = " + wrapper, generated, StringComparison.Ordinal);
            Assert.Contains("__dwarf_t.Second = " + wrapper, generated, StringComparison.Ordinal);
            Assert.Contains("(z.Second!, __dwarf_ctx, 0);", generated, StringComparison.Ordinal);
            Assert.Contains("private global::Demo.AnimalDto " + wrapper, generated, StringComparison.Ordinal);
            Assert.DoesNotContain("First = Map(", generated, StringComparison.Ordinal);
        }
    }
}
