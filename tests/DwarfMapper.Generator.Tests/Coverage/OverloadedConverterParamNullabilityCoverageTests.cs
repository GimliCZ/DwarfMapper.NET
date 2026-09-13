// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Conversions.cs's ConverterParamIsNonNullableRef, which finds the adopted converter
// by NAME. When the name is overloaded, an overload taking a VALUE type shares it, and that overload's parameter is
// not a reference at all. It must be passed over, not answer for the whole name: the Child overload's non-nullable
// reference parameter still earns the member its null guard, so a null source member never reaches ToDto.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class OverloadedConverterParamNullabilityCoverageTests
    {
        [Fact]
        public void A_value_type_overload_declared_first_does_not_hide_the_reference_overloads_null_guard()
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
                                      private static ChildDto ToDto(int x) => new ChildDto();
                                      private static ChildDto ToDto(Child c) => new ChildDto { A = c.A };
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("Inner = s.Inner is null ? null! : ToDto(s.Inner),", generated, StringComparison.Ordinal);
        }
    }
}
