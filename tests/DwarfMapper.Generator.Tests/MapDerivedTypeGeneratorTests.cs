// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests;

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
        Assert.Contains(diagnostics, d => d.Id == "DWARF035"
                                          && d.GetMessage(CultureInfo.InvariantCulture)
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
        Assert.Contains(diagnostics, d => d.Id == "DWARF035"
                                          && d.GetMessage(CultureInfo.InvariantCulture)
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
        Assert.Contains(diagnostics, d => d.Id == "DWARF035"
                                          && d.GetMessage(CultureInfo.InvariantCulture).Contains("duplicate",
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
}
