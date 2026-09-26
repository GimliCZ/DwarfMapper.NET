// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests
{
    public class MapDerivedTypeGeneratorTests
    {
        [Fact]
        public void Generic_MapDerivedType_attribute_is_recognized()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                               }
                               """;
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            Assert.Empty(errors);
        }

        [Fact]
        public void NonGeneric_MapDerivedType_attribute_is_recognized()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(Dog), typeof(DogDto))]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                               }
                               """;
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            Assert.Empty(errors);
        }

        [Fact]
        public void MapDerivedType_src_not_assignable_to_base_reports_DWARF035()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Animal { public string Name { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class Unrelated { public string Name { get; set; } = ""; }
                               public class UnrelatedDto { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(Unrelated), typeof(UnrelatedDto))]
                                   public partial AnimalDto Map(Animal a);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF035" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("not assignable", StringComparison.Ordinal));
        }

        [Fact]
        public void MapDerivedType_tgt_not_assignable_to_return_reports_DWARF035()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { }
                               public class Dog : Animal { public string Name { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class WrongDto { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(Dog), typeof(WrongDto))]
                                   public partial AnimalDto Map(Animal a);
                                   [MapIgnore("Name")]
                                   public partial WrongDto Map(Dog d);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF035" &&
                     d.GetMessage(CultureInfo.InvariantCulture)
                         .Contains("not assignable", StringComparison.Ordinal));
        }

        [Fact]
        public void MapDerivedType_duplicate_src_reports_DWARF035()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { }
                               public class Dog : Animal { public string Name { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(Dog), typeof(DogDto))]
                                   [MapDerivedType(typeof(Dog), typeof(DogDto))]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF035" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("duplicate",
                         StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void MapDerivedType_unmappable_pair_reports_error()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { }
                               public class Dog : Animal { public string Name { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public int UnmappableField { get; set; } }
                               [DwarfMapper(AutoNest = false)]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(Dog), typeof(DogDto))]
                                   public partial AnimalDto Map(Animal a);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            var hasError = diagnostics.Any(d =>
                d.Id == "DWARF035" || d.Id == "DWARF001" || d.Id == "DWARF005");
            Assert.True(hasError, $"Expected DWARF035/001/005; got: {string.Join(", ", diagnostics.Select(d => d.Id))}");
        }

        [Fact]
        public Task Snap_MapDerivedType_basic_switch()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class Cat : Animal { public int Lives { get; set; } }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               public class CatDto : AnimalDto { public int Lives { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   [MapDerivedType<Cat, CatDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   public partial CatDto Map(Cat c);
                               }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            return Verify(generated);
        }

        // ── DWARF036: Ambiguous [MapDerivedType] dispatch arms ─────────────────────
        // Two unrelated interfaces both implemented by the same concrete type → DWARF036.
        [Fact]
        public void DWARF036_two_unrelated_interfaces_reports_ambiguous()
        {
            // IFoo and IBar are unrelated; a concrete class could implement both.
            // Either arm could dispatch first for such an instance → DWARF036.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public interface IBase { }
                               public interface IFoo : IBase { string Name { get; set; } }
                               public interface IBar : IBase { int Value { get; set; } }
                               public class FooDto : IBase { public string Name { get; set; } = ""; }
                               public class BarDto : IBase { public int Value { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(IFoo), typeof(FooDto))]
                                   [MapDerivedType(typeof(IBar), typeof(BarDto))]
                                   public partial IBase Map(IBase o);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF036");
        }

        // NEGATIVE: two unrelated CONCRETE classes → no DWARF036 (a Dog can never be a Cat at runtime)
        [Fact]
        public void DWARF036_two_unrelated_concrete_classes_no_diagnostic()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class Cat : Animal { public int Lives { get; set; } }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               public class CatDto : AnimalDto { public int Lives { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   [MapDerivedType<Cat, CatDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   public partial CatDto Map(Cat c);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF036");
        }

        // NEGATIVE: ordered interface hierarchy (IFoo : IBar) → IFoo is more derived than IBar → no DWARF036
        [Fact]
        public void DWARF036_ordered_interface_hierarchy_no_diagnostic()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public interface IBar { string Tag { get; set; } }
                               public interface IFoo : IBar { int Extra { get; set; } }
                               public class C : IFoo { public string Tag { get; set; } = ""; public int Extra { get; set; } }
                               public class BarDto { public string Tag { get; set; } = ""; }
                               public class FooDto : BarDto { public int Extra { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(IBar), typeof(BarDto))]
                                   [MapDerivedType(typeof(IFoo), typeof(FooDto))]
                                   public partial BarDto Map(IBar b);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF036");
        }

        // NEGATIVE: single arm → no pair to be ambiguous → no DWARF036
        [Fact]
        public void DWARF036_single_arm_no_diagnostic()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF036");
        }

        // POSITIVE: interface + abstract class where both are unrelated → DWARF036
        [Fact]
        public void DWARF036_interface_and_unrelated_abstract_class_reports_ambiguous()
        {
            // IFoo is an interface; AbstractBase is abstract (not IFoo-related).
            // A class extending AbstractBase AND implementing IFoo hits both arms → DWARF036.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public interface IBase { }
                               public interface IFoo : IBase { string Name { get; set; } }
                               public abstract class AbstractBase : IBase { public int Value { get; set; } }
                               public class FooDto : IBase { public string Name { get; set; } = ""; }
                               public class BaseDto : IBase { public int Value { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   [MapDerivedType(typeof(IFoo), typeof(FooDto))]
                                   [MapDerivedType(typeof(AbstractBase), typeof(BaseDto))]
                                   public partial IBase Map(IBase o);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF036");
        }

        // ── Round-30 coverage sweep: EmitDerivedDispatchBody's hook loops had zero executions —
        // every fixture above declares no [BeforeMap]/[AfterMap] on the dispatch method itself, so
        // `foreach (before in method.EmitBeforeHooks)`, the whole `if (hasAfter)` block (the
        // `var __dwarf_target = ... switch` form, the after-hook loop, TakesSource, TargetByRef,
        // and the trailing `return __dwarf_target;`) were all 0%.

        [Fact]
        public void MapDerivedType_BeforeMap_hook_runs_ahead_of_the_switch()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   [BeforeMap] private static void Check(Animal a) { }
                               }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("Check(a);", generated, StringComparison.Ordinal);
            // The hook must precede the switch it guards.
            Assert.True(generated.IndexOf("Check(a);", StringComparison.Ordinal) <
                        generated.IndexOf("switch", StringComparison.Ordinal));
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void MapDerivedType_AfterMap_hook_switches_to_a_local_and_returns_it()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   [AfterMap] private static void Finish(AnimalDto d) { }
                               }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            // hasAfter=true routes the switch through a local instead of `return ... switch` — EXPLICITLY
            // typed as the method's own return type (AnimalDto), not `var`: with a single arm, `var`
            // would infer the ARM's narrower type (DogDto) instead, which only "works" for a by-value
            // hook by accident of implicit upcasting (see the ref-hook test, which does not have that
            // luxury and is what surfaced this).
            Assert.Contains("global::Demo.AnimalDto __dwarf_target = a switch", generated, StringComparison.Ordinal);
            Assert.Contains("Finish(__dwarf_target);", generated, StringComparison.Ordinal);
            Assert.Contains("return __dwarf_target;", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void MapDerivedType_AfterMap_hook_with_source_param_is_called_with_both_arguments()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   [AfterMap] private static void Finish(Animal a, AnimalDto d) { }
                               }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("Finish(a, __dwarf_target);", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void MapDerivedType_AfterMap_hook_takes_the_target_by_ref()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   [AfterMap] private static void Finish(ref AnimalDto d) { }
                               }
                               """;
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("Finish(ref __dwarf_target);", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void MapDerivedType_AfterMap_ref_hook_against_the_base_type_reports_DWARF109_for_the_derived_pair()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   [AfterMap] private static void Finish(ref AnimalDto d) { }
                               }
                               """;
            Assert.NotEmpty(GeneratorAssert.Reports(src, "DWARF109"));

            // The dispatch method's own pair is unaffected — Finish still applies where the destination
            // really is AnimalDto. Only Dog's own declared pair (destination DogDto) skips it.
            var (_, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains("Finish(ref __dwarf_target);", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void MapDerivedType_AfterMap_hook_taken_by_value_instead_of_ref_clears_DWARF109()
        {
            // Same shape as the ref-mismatch case above — this is DWARF109's documented fix: dropping
            // `ref` (not a second overload) is what clears the diagnostic for every derived pair at once.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   [AfterMap] private static void Finish(AnimalDto d) { }
                               }
                               """;
            GeneratorAssert.DoesNotReport(src, "DWARF109");
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Overloaded_recursive_dispatch_method_depth_companion_forwards_its_own_ctx_and_depth()
        {
            // Regression for round 30's 7635d71, which deleted EmitDerivedDispatchBody's synthesized-recursive
            // branch as dead on the premise that a [MapDerivedType] model is always partial. It is not:
            // MarkRecursionCapableCallers keys selfRecursivePublicMethods by NAME, so a dispatch method sharing
            // the name `Map` with an overload on a cycle (Dog.Friend maps back through Map) gets a private
            // `__DwarfMap_Depth_Map(Animal a, ctx, depth)` companion carrying its arms. With the branch gone
            // that companion's arm passed the public method's `__dwarf_ctx` local — CS0103 in a .g.cs.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; public Animal? Friend { get; set; } }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; public AnimalDto? Friend { get; set; } }
                               public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                               public class Zoo { public Animal Star { get; set; } = new Dog(); }
                               public class ZooDto { public AnimalDto Star { get; set; } = new DogDto(); }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M
                               {
                                   [MapDerivedType<Dog, DogDto>]
                                   public partial AnimalDto Map(Animal a);
                                   public partial DogDto Map(Dog d);
                                   public partial ZooDto Map(Zoo z);
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            const string arm = "global::Demo.Dog __s => __DwarfMap_Obj_global__Demo_Dog_global__Demo_DogDto_";
            // The public dispatch method opens the context; its companion forwards the one it was handed.
            Assert.Contains("private global::Demo.AnimalDto __DwarfMap_Depth_Map(global::Demo.Animal a, global::DwarfMapper.DwarfRefContext ctx, int depth)", generated, StringComparison.Ordinal);
            Assert.Contains("(__s, __dwarf_ctx, 0),", generated, StringComparison.Ordinal);
            Assert.Contains("(__s, ctx, depth + 1),", generated, StringComparison.Ordinal);
            Assert.Contains(arm, generated, StringComparison.Ordinal);
            // Every caller of Map(Animal) goes through the companion, so the Preserve dispatch wrapper has no caller
            // and must not be emitted as a dead private method.
            Assert.DoesNotContain("__DwarfMap_Disp_", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Dispatch_method_on_a_cycle_through_its_own_arm_is_depth_guarded_under_distinct_names()
        {
            // The call graph had no edges for [MapDerivedType] ARMS, so ToDto -> arm -> Friend -> ToDto was not a
            // cycle in it. Callers went through the Preserve dispatch wrapper and ToDto itself had no depth companion;
            // the overloaded twin above only got one because a bare overloaded name happened to fan out into a cycle
            // elsewhere. Arms are edges now, so both spellings get the same guarded shape.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public abstract class Animal { public string Name { get; set; } = ""; public Animal? Friend { get; set; } }
                               public class Dog : Animal { public string Breed { get; set; } = ""; }
                               public class AnimalDto { public string Name { get; set; } = ""; public AnimalDto? Friend { get; set; } }
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
            Assert.Contains("private global::Demo.AnimalDto __DwarfMap_Depth_ToDto(global::Demo.Animal a, global::DwarfMapper.DwarfRefContext ctx, int depth)", generated, StringComparison.Ordinal);
            Assert.Contains("star: __DwarfMap_Depth_ToDto(z.Star!, __dwarf_ctx, 0));", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMap_Disp_", generated, StringComparison.Ordinal);
        }
    }
}
