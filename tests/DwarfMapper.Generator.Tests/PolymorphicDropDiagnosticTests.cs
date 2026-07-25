// SPDX-License-Identifier: GPL-2.0-only
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// DWARF071 — a concrete base class as an auto-nested source, when something in the compilation derives from it.
/// <para>
/// DWARF033 refuses an ABSTRACT/INTERFACE auto-nest source because it "silently drops members that exist only
/// on derived runtime types". A CONCRETE base carries the identical risk — an <c>Animal Pet</c> member can hold
/// a <c>Dog</c>, and the mapper, resolving members from the DECLARED type at compile time, copies the Animal
/// members and drops <c>Breed</c> — but it is instantiable, so it slips straight past the DWARF033 gate.
/// </para>
/// <para>
/// A diagnostic is only worth having if it is quiet when it should be, so most of these tests are the negative
/// cases. An Info that fires on every non-sealed DTO would be ambient noise, and ambient noise is how real
/// diagnostics get globally suppressed.
/// </para>
/// </summary>
public class PolymorphicDropDiagnosticTests
{
    private static string Source(string animalModifiers, string extra = "") => $$"""
        using DwarfMapper;
        namespace Demo;

        public {{animalModifiers}} class Animal { public string Name { get; set; } = ""; }
        public sealed class Dog : Animal { public string Breed { get; set; } = ""; }

        public sealed class AnimalDto { public string Name { get; set; } = ""; }
        public sealed class Src { public Animal Pet { get; set; } = new(); }
        public sealed class Dst { public AnimalDto Pet { get; set; } = new(); }

        [DwarfMapper]
        {{extra}}
        public partial class M { public partial Dst Map(Src s); }
        """;

    private static string[] Ids(string source) =>
        GeneratorTestHarness.Run(source).Diagnostics.Select(d => d.Id).ToArray();

    [Fact]
    public void Concrete_base_with_a_derived_type_reports_DWARF071()
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(Source(""));

        var d = Assert.Single(diagnostics.Where(x => x.Id == "DWARF071"));
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
        Assert.Contains("Animal", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Sealed_source_reports_nothing()
    {
        // A sealed type IS its runtime type. Nothing can be dropped, so nothing should be said. (Dog derives
        // from Animal in the fixture, so this variant simply removes the hierarchy by sealing the base — which
        // makes `Dog : Animal` illegal, hence the standalone source here.)
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public sealed class Animal { public string Name { get; set; } = ""; }
            public sealed class AnimalDto { public string Name { get; set; } = ""; }
            public sealed class Src { public Animal Pet { get; set; } = new(); }
            public sealed class Dst { public AnimalDto Pet { get; set; } = new(); }
            [DwarfMapper]
            public partial class M { public partial Dst Map(Src s); }
            """;

        Assert.DoesNotContain("DWARF071", Ids(source));
    }

    [Fact]
    public void Non_sealed_source_that_nobody_derives_from_reports_nothing()
    {
        // The risk is theoretical until a derived type actually exists. Firing on every non-sealed class would
        // fire on essentially every DTO ever written, and a diagnostic that always fires is one nobody reads.
        const string source = """
            using DwarfMapper;
            namespace Demo;
            public class Animal { public string Name { get; set; } = ""; }
            public sealed class AnimalDto { public string Name { get; set; } = ""; }
            public sealed class Src { public Animal Pet { get; set; } = new(); }
            public sealed class Dst { public AnimalDto Pet { get; set; } = new(); }
            [DwarfMapper]
            public partial class M { public partial Dst Map(Src s); }
            """;

        Assert.DoesNotContain("DWARF071", Ids(source));
    }

    [Fact]
    public void MapDerivedType_dispatch_reports_nothing()
    {
        // The author has told the mapper how to dispatch on the runtime type — which is exactly the fix
        // DWARF071 recommends. Having applied it, they must not still be nagged.
        const string source = """
            using DwarfMapper;
            namespace Demo;

            public class Animal { public string Name { get; set; } = ""; }
            public sealed class Dog : Animal { public string Breed { get; set; } = ""; }

            public class AnimalDto { public string Name { get; set; } = ""; }
            public sealed class DogDto : AnimalDto { public string Breed { get; set; } = ""; }

            [DwarfMapper]
            public partial class M
            {
                [MapDerivedType<Dog, DogDto>]
                public partial AnimalDto Map(Animal a);
            }
            """;

        Assert.DoesNotContain("DWARF071", Ids(source));
    }

    [Fact]
    public void DWARF071_is_Info_so_it_never_breaks_a_warnings_as_errors_build()
    {
        // Mapping base-only is frequently the intent, and an abstract source is NECESSARILY derived at run time
        // whereas a concrete one MAY genuinely be the base. That difference is why DWARF033 is an Error and
        // this is not: it is a footgun to surface, not a defect to refuse.
        var (diagnostics, _) = GeneratorTestHarness.Run(Source(""));

        Assert.DoesNotContain(diagnostics,
            d => d.Id == "DWARF071" && d.Severity >= DiagnosticSeverity.Warning);
    }
}
