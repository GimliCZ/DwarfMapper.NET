// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Diagnostics;

namespace DwarfMapper.Generator.Roslyn512.Tests.Unions
{
    /// <summary>
    ///     Round 35 T23: a declared map from one C# 15 union to another, on the Roslyn 5.12 build - a switch over the source
    ///     union's case types whose arms produce the target union's case types. Every executed test compiles clean (no
    ///     warning inside a generated file) and then RUNS the map.
    /// </summary>
    public sealed class UnionEndpointTests
    {
        private const string Pets = """
            #nullable enable
            using DwarfMapper;
            namespace Demo;
            public record class Cat(string Name);
            public record class Dog(string Name);
            public record class CatDto(string Name);
            public record class DogDto(string Name);
            public union Pet(Cat, Dog);
            public union PetDto(CatDto, DogDto);

            """;

        private const string ShowPetDto = """
            public static class Show
            {
                public static string Of(object? value) => value?.ToString() ?? "empty";
            }

            """;

        private static string Run(GeneratorResult run, params CompiledLibrary[] libraries)
        {
            return (string)Roslyn512Harness.Invoke(run, "Demo.Probe", "Run", libraries)!;
        }

        private static IEnumerable<string> Messages(GeneratorResult run, string id)
        {
            return run.GeneratorDiagnostics
                .Where(d => string.Equals(d.Id, id, StringComparison.Ordinal))
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void The_union_endpoint_DWARF113_refused_maps_on_the_512_build_through_the_declared_case_maps()
        {
            // The red half is the Roslyn 5.0 build: UnionTypeRefusalTests' "union endpoint" row, DWARF113 there.
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + """
                [DwarfMapper] public partial class M
                {
                    public partial PetDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var m = new M();
                        return Show.Of(m.Map(new Pet(new Cat("tom"))).Value) + "|" + Show.Of(m.Map(new Pet(new Dog("rex"))).Value) + "|" + Show.Of(m.Map(default(Pet)).Value);
                    }
                }
                """);

            Assert.Equal("CatDto { Name = tom }|DogDto { Name = rex }|empty", Run(run));
            Assert.Contains("global::Demo.Cat __s => Map(__s),", run.Generated, StringComparison.Ordinal);
            Assert.Contains("null => default(global::Demo.PetDto),", run.Generated, StringComparison.Ordinal);
            Assert.DoesNotContain("_ => throw", run.Generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Explicit_arms_name_the_pairs_and_an_arm_with_no_declared_method_is_nested_as_any_pair_is()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + """
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType<Cat, CatDto>]
                    [MapDerivedType<Dog, DogDto>]
                    public partial PetDto Map(Pet p);
                }
                public static class Probe
                {
                    public static string Run() => Show.Of(new M().Map(new Pet(new Cat("tom"))).Value) + "|" + Show.Of(new M().Map(new Pet(new Dog("rex"))).Value);
                }
                """);

            Assert.Equal("CatDto { Name = tom }|DogDto { Name = rex }", Run(run));
        }

        [Fact]
        public void A_case_both_unions_share_passes_through_as_the_same_instance()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public union PetView(Cat, DogDto);
                [DwarfMapper] public partial class M
                {
                    public partial PetView Map(Pet p);
                    public partial DogDto Map(Dog d);
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var cat = new Cat("tom");
                        var view = new M().Map(new Pet(cat));
                        return ReferenceEquals(view.Value, cat) ? "same" : "copied";
                    }
                }
                """);

            Assert.Equal("same", Run(run));
            Assert.Contains("global::Demo.Cat __s => __s,", run.Generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Value_cases_take_typeof_arms_and_a_plain_conversion_is_written_to_the_target_case_type()
        {
            var run = Roslyn512Harness.CompilesClean("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                public union IntOrString(int, string);
                public union LongOrString(long, string);
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType(typeof(int), typeof(long))]
                    public partial LongOrString Map(IntOrString v);
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var a = new M().Map(new IntOrString(5)).Value;
                        var b = new M().Map(new IntOrString("x")).Value;
                        return a + ":" + a!.GetType().Name + "|" + b;
                    }
                }
                """);

            Assert.Equal("5:Int64|x", Run(run));
            Assert.Contains("int __s => (long)__s,", run.Generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_case_type_gets_the_null_arm_and_no_CS8655()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + """
                public union MaybeCat(Cat?, Dog);
                public union MaybeCatDto(CatDto?, DogDto);
                [DwarfMapper] public partial class M
                {
                    public partial MaybeCatDto Map(MaybeCat p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                public static class Probe
                {
                    public static string Run() => Show.Of(new M().Map(new MaybeCat((Cat?)null)).Value) + "|" + Show.Of(new M().Map(new MaybeCat(new Cat("tom"))).Value);
                }
                """);

            Assert.Equal("empty|CatDto { Name = tom }", Run(run));
        }

        [Fact]
        public void A_constructed_generic_union_pairs_its_substituted_case_types()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + """
                public record class None;
                public union Option<T>(T, None);
                [DwarfMapper] public partial class M
                {
                    public partial Option<CatDto> Map(Option<Cat> o);
                    public partial CatDto Map(Cat c);
                }
                public static class Probe
                {
                    public static string Run() => Show.Of(new M().Map(new Option<Cat>(new Cat("tom"))).Value) + "|" + Show.Of(new M().Map(new Option<Cat>(new None())).Value);
                }
                """);

            Assert.Equal("CatDto { Name = tom }|None { }", Run(run));
        }

        [Fact]
        public void Overlapping_case_types_put_the_derived_arm_first()
        {
            var run = Roslyn512Harness.CompilesClean("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                public record class Animal(string Name);
                public record class Kitten(string Name) : Animal(Name);
                public record class AnimalDto(string Name);
                public record class KittenDto(string Name);
                public union Pets(Animal, Kitten);
                public union PetsDto(AnimalDto, KittenDto);
                [DwarfMapper] public partial class M
                {
                    public partial PetsDto Map(Pets p);
                    public partial AnimalDto Map(Animal a);
                    public partial KittenDto Map(Kitten k);
                }
                public static class Probe
                {
                    public static string Run() => new M().Map(new Pets(new Kitten("k"))).Value + "|" + new M().Map(new Pets(new Animal("a"))).Value;
                }
                """);

            Assert.Equal("KittenDto { Name = k }|AnimalDto { Name = a }", Run(run));
            Assert.True(run.Generated.IndexOf("global::Demo.Kitten __s", StringComparison.Ordinal) <
                        run.Generated.IndexOf("global::Demo.Animal __s", StringComparison.Ordinal),
                "the Kitten arm must come before the Animal arm (CS8510 otherwise)");
        }

        [Fact]
        public void A_union_declared_in_a_referenced_assembly_maps_as_one_declared_here()
        {
            var library = Roslyn512Harness.Compile("""
                #nullable enable
                namespace Lib
                {
                    public record class Cat(string Name);
                    public record class Dog(string Name);
                    public record class CatDto(string Name);
                    public record class DogDto(string Name);
                    public union Pet(Cat, Dog);
                    public union PetDto(CatDto, DogDto);
                }
                """);
            var run = Roslyn512Harness.CompilesClean("""
                #nullable enable
                using DwarfMapper;
                namespace Demo;
                [DwarfMapper] public partial class M
                {
                    public partial Lib.PetDto Map(Lib.Pet p);
                    public partial Lib.CatDto Map(Lib.Cat c);
                    public partial Lib.DogDto Map(Lib.Dog d);
                }
                public static class Probe
                {
                    public static string Run() => new M().Map(new Lib.Pet(new Lib.Dog("rex"))).Value?.ToString() ?? "empty";
                }
                """, polyfill: false, libraries: [library]);

            Assert.Equal("DogDto { Name = rex }", Run(run, library));
        }

        private const string ProviderTarget = """
            [System.Runtime.CompilerServices.Union]
            public sealed class ProvPetDto : ProvPetDto.IUnionMembers
            {
                private object? _value;
                public interface IUnionMembers
                {
                    public static ProvPetDto Create(CatDto value) => new ProvPetDto { _value = value };
                    public static ProvPetDto Create(DogDto value) => new ProvPetDto { _value = value };
                    public object? Value { get; }
                }
                object? IUnionMembers.Value => _value;
            }

            """;

        [Fact]
        public void A_member_provider_class_union_as_the_target_is_built_through_its_Create_factories()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + ProviderTarget + """
                [DwarfMapper] public partial class M
                {
                    public partial ProvPetDto? Map(Pet p);
                    public partial ProvPetDto Strict(Pet p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var m = new M();
                        var cat = ((ProvPetDto.IUnionMembers)m.Map(new Pet(new Cat("tom")))!).Value;
                        var empty = m.Map(default(Pet)) is null ? "null" : "not null";
                        var strict = ((ProvPetDto.IUnionMembers)m.Strict(new Pet(new Dog("rex")))).Value;
                        string thrown;
                        try { m.Strict(default(Pet)); thrown = "no throw"; }
                        catch (System.Runtime.CompilerServices.SwitchExpressionException) { thrown = "SwitchExpressionException"; }
                        return Show.Of(cat) + "|" + empty + "|" + Show.Of(strict) + "|" + thrown;
                    }
                }
                """);

            Assert.Equal("CatDto { Name = tom }|null|DogDto { Name = rex }|SwitchExpressionException", Run(run));
        }

        [Fact]
        public void A_source_that_can_be_empty_into_a_non_nullable_class_union_is_DWARF113()
        {
            var run = Roslyn512Harness.Run(Pets + ProviderTarget + """
                public union MaybeCat(Cat?, Dog);
                [DwarfMapper] public partial class M
                {
                    public partial ProvPetDto Map(MaybeCat p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                """);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("has nothing to map to", StringComparison.Ordinal) &&
                                                             m.Contains("Make the destination nullable", StringComparison.Ordinal));
        }

        [Fact]
        public void Hooks_on_a_union_map_run_as_on_a_derived_dispatch()
        {
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + """
                [DwarfMapper] public partial class M
                {
                    public static int Before;
                    public partial PetDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                    [BeforeMap] private void Count(Pet p) => Before++;
                    [AfterMap] private void Rename(Pet p, ref PetDto d) => d = new CatDto("after");
                }
                public static class Probe
                {
                    public static string Run() => Show.Of(new M().Map(new Pet(new Dog("rex"))).Value) + "|" + M.Before.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                """);

            Assert.Equal("CatDto { Name = after }|1", Run(run));
        }

        [Fact]
        public void A_case_with_no_arm_is_DWARF114_naming_it_and_the_two_remedies()
        {
            var run = Roslyn512Harness.Run(Pets + """
                [DwarfMapper] public partial class M
                {
                    public partial PetDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                }
                """);

            var message = Assert.Single(Messages(run, "DWARF114"));
            Assert.Contains("its case type(s) 'Demo.Dog' have no arm", message, StringComparison.Ordinal);
            Assert.Contains("Name the arm with [MapDerivedType]", message, StringComparison.Ordinal);
            Assert.Contains("cases are never paired by their members or their names", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_case_two_declared_methods_could_map_is_DWARF114_naming_both()
        {
            var run = Roslyn512Harness.Run(Pets + """
                public record class KittenDto(string Name);
                public union PetOrKittenDto(CatDto, DogDto, KittenDto);
                [DwarfMapper] public partial class M
                {
                    public partial PetOrKittenDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                    public partial KittenDto AsKitten(Cat c);
                    public partial DogDto Map(Dog d);
                }
                """);

            var message = Assert.Single(Messages(run, "DWARF114"));
            Assert.Contains("'Map' into 'Demo.CatDto'", message, StringComparison.Ordinal);
            Assert.Contains("'AsKitten' into 'Demo.KittenDto'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_explicit_arm_whose_source_is_no_case_type_is_DWARF035()
        {
            var run = Roslyn512Harness.Run(Pets + """
                public record class Horse(string Name);
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType<Horse, CatDto>]
                    public partial PetDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                """);

            Assert.Contains(Messages(run, "DWARF035"), m => m.Contains("is not a case type of the union 'Demo.Pet'", StringComparison.Ordinal));
        }

        [Fact]
        public void An_explicit_arm_whose_target_does_not_convert_to_the_target_union_is_DWARF035()
        {
            var run = Roslyn512Harness.Run(Pets + """
                public record class HorseDto(string Name);
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType<Cat, HorseDto>]
                    public partial PetDto Map(Pet p);
                    public partial DogDto Map(Dog d);
                }
                """);

            Assert.Contains(Messages(run, "DWARF035"), m => m.Contains("does not convert to the union 'Demo.PetDto'", StringComparison.Ordinal));
        }

        [Fact]
        public void A_union_map_declared_between_nullable_unions_is_DWARF113_pointing_at_the_union_types()
        {
            var run = Roslyn512Harness.Run(Pets + """
                [DwarfMapper] public partial class M { public partial PetDto? Map(Pet? p); }
                """);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("A union map is declared between the union types themselves", StringComparison.Ordinal));
        }

        [Fact]
        public void A_union_map_with_a_second_parameter_is_refused_as_an_invalid_map_method()
        {
            var run = Roslyn512Harness.Run(Pets + """
                [DwarfMapper] public partial class M { public partial PetDto Map(Pet p, int extra); }
                """);

            Assert.Contains(run.GeneratorDiagnostics, d => string.Equals(d.Id, DiagnosticDescriptors.InvalidMapMethod.Id, StringComparison.Ordinal));
        }

        [Fact]
        public void A_source_case_named_by_two_explicit_arms_is_DWARF035()
        {
            var run = Roslyn512Harness.Run(Pets + """
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType<Cat, CatDto>]
                    [MapDerivedType<Cat, DogDto>]
                    public partial PetDto Map(Pet p);
                    public partial DogDto Map(Dog d);
                }
                """);

            Assert.Contains(Messages(run, "DWARF035"), m => m.Contains("duplicate source type 'global::Demo.Cat'", StringComparison.Ordinal));
        }

        [Fact]
        public void An_explicit_arm_no_rule_can_map_is_DWARF035()
        {
            // The arm is valid on both sides - Dog is a case of Pet, IAnimalDto a case of AnimalDto - but no declared
            // method maps Dog into IAnimalDto, Dog does not implement it, and an interface cannot be built member by member.
            var run = Roslyn512Harness.Run(Pets + """
                public interface IAnimalDto { }
                public union AnimalDto(CatDto, IAnimalDto);
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType<Dog, IAnimalDto>]
                    public partial AnimalDto Map(Pet p);
                    public partial CatDto Map(Cat c);
                }
                """);

            Assert.Contains(Messages(run, "DWARF035"), m => m.Contains("pair ('global::Demo.Dog', 'global::Demo.IAnimalDto') is not mappable", StringComparison.Ordinal));
        }

        private const string ProviderSource = """
            [System.Runtime.CompilerServices.Union]
            public sealed class ProvPet : ProvPet.IUnionMembers
            {
                private object? _value;
                public interface IUnionMembers
                {
                    public static ProvPet Create(Cat value) => new ProvPet { _value = value };
                    public static ProvPet Create(Dog value) => new ProvPet { _value = value };
                    // Neither of the next two creates a case: the cases are the one-parameter STATIC factories.
                    public static ProvPet Create(Cat first, Dog second) => new ProvPet { _value = first };
                    public ProvPet Create(int ignored) => new ProvPet();
                    public object? Value { get; }
                }
                object? IUnionMembers.Value => _value;
            }

            """;

        [Fact]
        public void A_member_provider_union_as_the_source_takes_its_cases_from_its_one_parameter_static_factories()
        {
            // Into a non-nullable class union the map asks whether the source can be empty, which for a member-provider
            // source it reads from those factories' parameters: none is nullable here, so the switch has no null arm.
            var run = Roslyn512Harness.CompilesClean(Pets + ShowPetDto + ProviderTarget + ProviderSource + """
                [DwarfMapper] public partial class M
                {
                    public partial ProvPetDto Map(ProvPet p);
                    public partial CatDto Map(Cat c);
                    public partial DogDto Map(Dog d);
                }
                public static class Probe
                {
                    public static string Run()
                    {
                        var m = new M();
                        var cat = ((ProvPetDto.IUnionMembers)m.Map(ProvPet.IUnionMembers.Create(new Cat("tom")))).Value;
                        var dog = ((ProvPetDto.IUnionMembers)m.Map(ProvPet.IUnionMembers.Create(new Dog("rex")))).Value;
                        return Show.Of(cat) + "|" + Show.Of(dog);
                    }
                }
                """);

            Assert.Equal("CatDto { Name = tom }|DogDto { Name = rex }", Run(run));
        }

        [Fact]
        public void A_nullable_value_case_can_leave_the_union_empty_so_into_a_non_nullable_class_union_it_is_DWARF113()
        {
            var run = Roslyn512Harness.Run(Pets + """
                public union MaybeNumber(int?, string);
                [System.Runtime.CompilerServices.Union]
                public sealed class NumberDto : NumberDto.IUnionMembers
                {
                    private object? _value;
                    public interface IUnionMembers
                    {
                        public static NumberDto Create(long value) => new NumberDto { _value = value };
                        public static NumberDto Create(string value) => new NumberDto { _value = value };
                        public object? Value { get; }
                    }
                    object? IUnionMembers.Value => _value;
                }
                [DwarfMapper] public partial class M
                {
                    [MapDerivedType(typeof(int), typeof(long))]
                    public partial NumberDto Map(MaybeNumber n);
                }
                """);

            Assert.Contains(Messages(run, "DWARF113"), m => m.Contains("its case type 'int?' can be null", StringComparison.Ordinal));
        }

        [Fact]
        public void A_value_case_is_tested_before_an_interface_case_it_boxes_to()
        {
            // Declared interface-first; the switch must test int first, or the IFormattable arm would take every int and
            // the int arm would be CS8510.
            var run = Roslyn512Harness.CompilesClean(Pets + """
                public union Measure(System.IFormattable, int);
                public union MeasureView(System.IFormattable, int);
                [DwarfMapper] public partial class M { public partial MeasureView Map(Measure m); }
                public static class Probe
                {
                    public static string Run()
                    {
                        var m = new M();
                        var number = m.Map(new Measure(42)).Value;
                        var date = m.Map(new Measure((System.IFormattable)new System.DateTime(2026, 11, 15))).Value;
                        return number!.GetType().Name + "|" + date!.GetType().Name;
                    }
                }
                """);

            Assert.Equal("Int32|DateTime", Run(run));
            var intArm = run.Generated.IndexOf("int __s => ", StringComparison.Ordinal);
            var formattableArm = run.Generated.IndexOf("global::System.IFormattable __s => ", StringComparison.Ordinal);
            Assert.True(intArm >= 0 && formattableArm > intArm, "The int arm must come first:\n" + run.Generated);
        }
    }
}
