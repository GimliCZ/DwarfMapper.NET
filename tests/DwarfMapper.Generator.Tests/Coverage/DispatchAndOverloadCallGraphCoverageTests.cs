// SPDX-License-Identifier: GPL-2.0-only

// Coverage suite for MapperExtractor.Phases.cs arms that need a [MapDerivedType] arm or an OVERLOADED mapping-method
// name to reach:
//   - ThreadContextThroughDispatchArms: an arm resolved to a self-recursive declared method is redirected to that
//     method's depth companion;
//   - SynthesizePreserveDispatchWrappers: a ctor arg with NO converter sits beside the one it patches;
//   - the call graph: a ctor arg calling an overloaded declared method, and a synthesized helper calling one, both
//     expand the bare name into per-overload edges (skipping partial methods with a different name).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class DispatchAndOverloadCallGraphCoverageTests
    {
        [Fact]
        public void Dispatch_arm_resolved_to_a_self_recursive_declared_method_calls_its_depth_companion()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public Dog? Next { get; set; } }
                               public class DogDto : AnimalDto { public DogDto? Next { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto ToDto(Animal a);
                                   public partial DogDto ToDog(Dog d);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            // Calling ToDog directly would start a fresh depth count for every arm and never trip the guard.
            Assert.Contains("global::Demo.Dog __s => __DwarfMap_Depth_ToDog(__s, __dwarf_ctx, 0),", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Preserve_dispatch_wrapper_patch_leaves_a_converter_less_ctor_arg_alone()
        {
            // ACYCLIC on purpose. With a self-referencing member (Animal.Friend) ToDto is on a real cycle through its
            // own arm, its callers are redirected to its depth companion first, and the wrapper patch never runs.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               public class Zoo { public string Title { get; set; } = ""; public Animal Star { get; set; } = new Dog(); }
                               public class ZooDto { public ZooDto(string title, AnimalDto star) { Title = title; Star = star; } public string Title { get; } public AnimalDto Star { get; } }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto ToDto(Animal a);
                                   public partial DogDto ToDog(Dog d);
                                   public partial ZooDto ToZoo(Zoo z);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("title: z.Title,", generated, StringComparison.Ordinal);
            Assert.Contains("star: __DwarfMap_Disp_global__Demo_Animal_global__Demo_AnimalDto_", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Ctor_arg_calling_an_overloaded_declared_method_binds_the_matching_overload()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Child { public int A { get; set; } }
                               public class ChildDto { public int A { get; set; } }
                               public class Holder { public Child Child { get; set; } = new(); }
                               public class HolderDto { public HolderDto(ChildDto child) { Child = child; } public ChildDto Child { get; } }
                               public class Other { public int B { get; set; } }
                               public class OtherDto { public int B { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial HolderDto Map(Holder h);
                                   public partial ChildDto Map(Child c);
                                   public partial OtherDto Convert(Other o);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            // Acyclic: the overload edges must not manufacture a cycle, so the ctor arg calls the public overload.
            Assert.Contains("child: Map(h.Child));", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Synthesized_helper_calling_an_overloaded_declared_method_maps_the_member_through_it()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Child { public int A { get; set; } }
                               public class ChildDto { public int A { get; set; } }
                               public class Inner { public Child Child { get; set; } = new(); }
                               public class InnerDto { public ChildDto Child { get; set; } = new(); }
                               public class Holder { public Inner Inner { get; set; } = new(); }
                               public class HolderDto { public InnerDto Inner { get; set; } = new(); }
                               public class Other { public int B { get; set; } }
                               public class OtherDto { public int B { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   public partial HolderDto Map(Holder h);
                                   public partial ChildDto Map(Child c);
                                   public partial OtherDto Convert(Other o);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            // Acyclic: the helper adopted Map(Child), and only Map(Child). Before its edge carried the adopted
            // overload it was fanned out to Map(Holder) too, which manufactured Map(Holder) → helper → Map(Holder):
            // a depth companion for both overloads and a DwarfRefContext allocated on every call, for a graph with
            // no cycle in it.
            Assert.Contains("Child = Map(s.Child", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMap_Depth_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
        }
    }
}
