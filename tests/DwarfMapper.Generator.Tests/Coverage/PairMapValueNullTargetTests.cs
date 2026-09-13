// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Tests for MapperExtractor.Pairs.cs's ReadPairMapValues.
//   - Regression: [MapValue<T>(null!, 5)] names no target member, yet it vanished with no diagnostic, while the same
//     directive naming an unknown or empty member reports DWARF042. A null name must be reported the same way.
//   - Coverage: an argument list that binds to no constructor is the compiler's error and adds no generator report;
//     Use = null is "no Use", so a one-argument [MapValue<T>] supplies nothing (DWARF042).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PairMapValueNullTargetTests
    {
        private static string Mapper(string directive) => $$"""
                                                          using DwarfMapper;
                                                          namespace Demo;
                                                          public class Inner { public int A { get; set; } public int B { get; set; } }
                                                          public class InnerDto { public int A { get; set; } public int B { get; set; } }
                                                          [DwarfMapper]
                                                          [GenerateMap<Inner, InnerDto>]
                                                          {{directive}}
                                                          public partial class M { }
                                                          """;

        private static string[] Messages(string source, string id) =>
            GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics
                .Where(d => d.Id == id)
                .Select(d => d.GetMessage(CultureInfo.InvariantCulture))
                .ToArray();

        [Fact]
        public void A_null_target_name_reports_DWARF042_like_an_empty_one()
        {
            var message = Assert.Single(Messages(Mapper("[MapValue<InnerDto>(null!, 5)]"), "DWARF042"));

            Assert.Equal(Assert.Single(Messages(Mapper("[MapValue<InnerDto>(\"\", 5)]"), "DWARF042")), message);
        }

        [Fact]
        public void An_argument_list_that_binds_to_no_constructor_is_left_to_the_compiler()
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(Mapper("[MapValue<InnerDto>()]"), NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Id is "DWARF042" or "DWARF056");
            Assert.Contains("B = src.B,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Use_null_is_no_Use_so_a_one_argument_directive_supplies_nothing()
        {
            var message = Assert.Single(Messages(Mapper("[MapValue<InnerDto>(\"B\", Use = null)]"), "DWARF042"));

            Assert.Equal("[MapValue] for 'B' provides neither a constant value nor Use=", message);
        }
    }
}
