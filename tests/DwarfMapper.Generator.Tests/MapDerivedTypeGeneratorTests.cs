// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

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
            && d.GetMessage(CultureInfo.InvariantCulture).Contains("not assignable", System.StringComparison.Ordinal));
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
            && d.GetMessage(CultureInfo.InvariantCulture).Contains("not assignable", System.StringComparison.Ordinal));
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
            && d.GetMessage(CultureInfo.InvariantCulture).Contains("duplicate", System.StringComparison.OrdinalIgnoreCase));
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
    public System.Threading.Tasks.Task Snap_MapDerivedType_basic_switch()
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
        return VerifyXunit.Verifier.Verify(generated);
    }
}
