// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Conversions.cs's AllTypesIn, the walk behind DWARF071's "does anything derive from
// this concrete base?" question. It recurses into NESTED types as well as namespaces. A hierarchy declared inside a
// containing class must be found exactly as a top-level one is, or DWARF071 would go quiet for every model grouped
// inside a static holder class.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PolymorphicDropNestedTypesCoverageTests
    {
        [Fact]
        public void A_derived_type_nested_in_a_holder_class_still_reports_DWARF071()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public static class Models
                                  {
                                      public class Animal { public string Name { get; set; } = ""; }
                                      public sealed class Dog : Animal { public string Breed { get; set; } = ""; }
                                  }
                                  public sealed class AnimalDto { public string Name { get; set; } = ""; }
                                  public sealed class Src { public Models.Animal Pet { get; set; } = new(); }
                                  public sealed class Dst { public AnimalDto Pet { get; set; } = new(); }
                                  [DwarfMapper]
                                  public partial class M { public partial Dst Map(Src s); }
                                  """;

            var d = Assert.Single(GeneratorTestHarness.Run(source).Diagnostics, x => x.Id == "DWARF071");

            Assert.Equal(DiagnosticSeverity.Info, d.Severity);
            Assert.Contains("Animal", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
