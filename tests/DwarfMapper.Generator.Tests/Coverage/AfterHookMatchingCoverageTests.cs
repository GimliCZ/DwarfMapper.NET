// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage suite for the [AfterMap] "does this hook apply to this pair?" loop, which MapperExtractor.Phases.cs
// repeats at five HookCall construction sites: the [MapDerivedType] dispatch method, the update-into map, the
// [GenerateMap] pair, the synthesized nested pair and the projection (where any applicable hook is DWARF028).
// Each site had only ever seen hooks that APPLIED: a two-parameter hook whose source or target does not convert,
// a by-value hook on a struct target, and a `ref` hook whose target type is merely convertible (DWARF109) were
// exercised at the declared create map and nowhere else.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class AfterHookMatchingCoverageTests
    {
        private const string Dwarf028 = "DWARF028";
        private const string Dwarf109 = "DWARF109";

        private const string Animals = """
                                       public abstract class Animal { public string Name { get; set; } = ""; }
                                       public class Dog : Animal { public string Breed { get; set; } = ""; }
                                       public class AnimalDto { public string Name { get; set; } = ""; }
                                       public class DogDto : AnimalDto { public string Breed { get; set; } = ""; }
                                       """;

        private static string Message109(string src) =>
            GeneratorAssert.Reports(src, Dwarf109)[0].GetMessage(CultureInfo.InvariantCulture);

        // ── [MapDerivedType] dispatch method ────────────────────────────────────────────────────────────────

        [Fact]
        public void Dispatch_method_skips_a_two_parameter_hook_whose_source_does_not_convert()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n" + Animals + """
                                                                         public class Other { }
                                                                         [DwarfMapper]
                                                                         public partial class M
                                                                         {
                                                                             [MapDerivedType<Dog, DogDto>]
                                                                             public partial AnimalDto Map(Animal a);
                                                                             public partial DogDto Map(Dog d);
                                                                             [AfterMap] private static void Finish(Other o, AnimalDto d) { }
                                                                         }
                                                                         """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Dispatch_method_refuses_a_ref_hook_whose_target_only_converts()
        {
            var src = "using DwarfMapper;\nnamespace Demo;\n" + Animals + """
                                                                         [DwarfMapper]
                                                                         public partial class M
                                                                         {
                                                                             [MapDerivedType<Dog, DogDto>]
                                                                             public partial AnimalDto Map(Animal a);
                                                                             public partial DogDto Map(Dog d);
                                                                             [AfterMap] private static void Finish(ref object d) { }
                                                                         }
                                                                         """;

            var message = Message109(src);
            Assert.Contains("'Finish'", message, StringComparison.Ordinal);
            Assert.Contains("global::Demo.AnimalDto", message, StringComparison.Ordinal);
        }

        [Fact]
        public void Dispatch_method_skips_a_by_value_hook_on_a_struct_target()
        {
            // The generic attribute constrains TTarget to a class, but the typeof form does not, and nothing in
            // [MapDerivedType] validation requires a class return: every arm maps INTO the same struct, so the
            // dispatch target is a value type and a by-value hook would mutate a copy.
            var src = "using DwarfMapper;\nnamespace Demo;\n" + Animals + """
                                                                         public struct Card { public string Name { get; set; } }
                                                                         [DwarfMapper(AutoNest = true)]
                                                                         public partial class M
                                                                         {
                                                                             [MapDerivedType(typeof(Dog), typeof(Card))]
                                                                             public partial Card Map(Animal a);
                                                                             [AfterMap] private static void Finish(Card c) { }
                                                                         }
                                                                         """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        // ── Update-into ─────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Update_into_skips_a_two_parameter_hook_whose_source_does_not_convert()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               public class Other { }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial void Update(Src s, Dst d);
                                   [AfterMap] private static void Finish(Other o, Dst d) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Update_into_refuses_a_ref_hook_whose_target_only_converts()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial void Update(Src s, Dst d);
                                   [AfterMap] private static void Finish(ref object d) { }
                               }
                               """;

            var message = Message109(src);
            Assert.Contains("'Finish'", message, StringComparison.Ordinal);
            Assert.Contains("global::Demo.Dst", message, StringComparison.Ordinal);
        }

        // ── [GenerateMap] pair ──────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Generate_map_pair_skips_a_two_parameter_hook_whose_source_does_not_convert()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               public class Other { }
                               [DwarfMapper]
                               [GenerateMap<Src, Dst>]
                               public partial class M
                               {
                                   [AfterMap] private static void Finish(Other o, Dst d) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Generate_map_pair_skips_a_by_value_hook_on_a_struct_target()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public struct Dst { public int A { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<Src, Dst>]
                               public partial class M
                               {
                                   [AfterMap] private static void Finish(Dst d) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Generate_map_pair_refuses_a_ref_hook_whose_target_only_converts()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<Src, Dst>]
                               public partial class M
                               {
                                   [AfterMap] private static void Finish(ref object d) { }
                               }
                               """;

            var message = Message109(src);
            Assert.Contains("'Finish'", message, StringComparison.Ordinal);
            Assert.Contains("global::Demo.Dst", message, StringComparison.Ordinal);
        }

        // ── Synthesized nested pair ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Nested_pair_skips_a_two_parameter_hook_whose_source_does_not_convert()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public int A { get; set; } }
                               public class Src { public Inner I { get; set; } = new(); }
                               public class Dst { public InnerDto I { get; set; } = new(); }
                               public class Other { }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   public partial Dst Map(Src s);
                                   [AfterMap] private static void Finish(Other o, InnerDto d) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_pair_skips_a_by_value_hook_on_a_struct_target()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public struct InnerDto { public int A { get; set; } }
                               public class Src { public Inner I { get; set; } = new(); }
                               public class Dst { public InnerDto I { get; set; } }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   public partial Dst Map(Src s);
                                   [AfterMap] private static void Finish(InnerDto d) { }
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.DoesNotContain("Finish(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_pair_refuses_a_ref_hook_whose_target_only_converts()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Inner { public int A { get; set; } }
                               public class InnerDto { public int A { get; set; } }
                               public class Src { public Inner I { get; set; } = new(); }
                               public class Dst { public InnerDto I { get; set; } = new(); }
                               [DwarfMapper(AutoNest = true)]
                               public partial class M
                               {
                                   public partial Dst Map(Src s);
                                   [AfterMap] private static void Finish(ref object d) { }
                               }
                               """;

            var messages = GeneratorAssert.Reports(src, Dwarf109)
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .ToList();
            Assert.Contains(messages, m => m.Contains("global::Demo.InnerDto", StringComparison.Ordinal));
        }

        // ── Projection ──────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Projection_reports_an_after_hook_even_when_no_before_hook_applies()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Linq;
                               namespace Demo;
                               public class Person { public int Age { get; set; } }
                               public class PersonDto { public int Age { get; set; } }
                               public class Other { }
                               [DwarfMapper]
                               public partial class M
                               {
                                   public partial IQueryable<PersonDto> Project(IQueryable<Person> src);
                                   [AfterMap] private static void Unrelated(Other o, PersonDto d) { }
                                   [AfterMap] private static void WrongTarget(Person p, Other o) { }
                                   [AfterMap] private static void Finish(PersonDto d) { }
                               }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, Dwarf028)).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("hook", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
