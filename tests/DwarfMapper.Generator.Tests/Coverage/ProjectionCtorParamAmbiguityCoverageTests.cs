// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Projection.cs's TryBindProjectionCtorParam. A constructor parameter with no exact
// source match is retried case-insensitively. When that retry finds TWO source members differing only by case, the
// binding is ambiguous and reported (DWARF010), not settled by picking one.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionCtorParamAmbiguityCoverageTests
    {
        [Fact]
        public void A_parameter_matching_two_case_variant_source_members_is_ambiguous()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Linq;
                                  namespace Demo;
                                  public class S { public int Value { get; set; } public int VALUE { get; set; } }
                                  public class D { public D(int value) { Value = value; } public int Value { get; } }
                                  [DwarfMapper]
                                  public partial class M { public partial IQueryable<D> Project(IQueryable<S> src); }
                                  """;

            var d = Assert.Single(GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics, x => x.Id == "DWARF010");

            Assert.Equal("Destination member 'value' matches more than one source member under case-insensitive matching; rename or use [MapProperty]", d.GetMessage(CultureInfo.InvariantCulture));
        }
    }
}
