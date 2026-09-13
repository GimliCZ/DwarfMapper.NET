// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Conversions.cs's ConverterReturnIsNullableRef, which finds the adopted converter
// by NAME. When the name is overloaded, an overload returning a VALUE type shares it, and that overload's return is
// not a reference at all. It must be passed over, not answer for the whole name: the Child overload's nullable
// reference return still earns the call its '!' and DWARF107.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class OverloadedConverterReturnNullabilityCoverageTests
    {
        [Fact]
        public void A_value_type_overload_declared_first_does_not_hide_the_reference_overloads_nullable_return()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class Child { public int A { get; set; } }
                                  public class ChildDto { public int A { get; set; } }
                                  public class Src { public Child Inner { get; set; } = new(); }
                                  public class Dst { public ChildDto Inner { get; set; } = new(); }
                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      public partial Dst Map(Src s);
                                      private static int ToDto(int x) => x;
                                      private static ChildDto? ToDto(Child c) => null;
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);
            Assert.Contains("Inner = s.Inner is null ? null! : ToDto(s.Inner)!,", generated, StringComparison.Ordinal);

            var d = Assert.Single(GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics, x => x.Id == "DWARF107");
            Assert.Contains("'ToDto' is declared to return a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
