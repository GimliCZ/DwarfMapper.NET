// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Tests for MapperExtractor.Pairs.cs's ReadPairMapProperties.
//   - Regression: [MapProperty<S,T>(null!, "B")] and [MapProperty<S,T>("A", null!)] name no member, yet they vanished
//     with no diagnostic, while the same directive naming an unknown or empty member reports DWARF009 (source) or
//     DWARF008 (destination). A null name must be reported the same way.
//   - Coverage: When = null is "no condition", so the member is assigned unconditionally.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class PairMapPropertyNullNameTests
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
        public void A_null_source_name_reports_DWARF009_like_an_empty_one()
        {
            var message = Assert.Single(Messages(Mapper("[MapProperty<Inner, InnerDto>(null!, \"B\")]"), "DWARF009"));

            Assert.Equal(Assert.Single(Messages(Mapper("[MapProperty<Inner, InnerDto>(\"\", \"B\")]"), "DWARF009")), message);
        }

        [Fact]
        public void A_null_destination_name_reports_DWARF008_like_an_empty_one()
        {
            var message = Assert.Single(Messages(Mapper("[MapProperty<Inner, InnerDto>(\"A\", null!)]"), "DWARF008"));

            Assert.Equal(Assert.Single(Messages(Mapper("[MapProperty<Inner, InnerDto>(\"A\", \"\")]"), "DWARF008")), message);
        }

        [Fact]
        public void When_null_is_no_condition_so_the_member_is_assigned_unconditionally()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Mapper("[MapProperty<Inner, InnerDto>(\"A\", \"B\", When = null)]"));

            Assert.Contains("B = src.A,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Use_null_is_no_converter_so_the_rename_assigns_directly()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Mapper("[MapProperty<Inner, InnerDto>(\"A\", \"B\", Use = null)]"));

            Assert.Contains("B = src.A,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_argument_list_that_binds_to_no_constructor_is_left_to_the_compiler()
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(Mapper("[MapProperty<Inner, InnerDto>(\"A\")]"), NullableContextOptions.Enable);

            Assert.DoesNotContain(diagnostics, d => d.Id is "DWARF008" or "DWARF009" or "DWARF056");
            Assert.Contains("A = src.A,", generated, StringComparison.Ordinal);
            Assert.Contains("B = src.B,", generated, StringComparison.Ordinal);
        }
    }
}
