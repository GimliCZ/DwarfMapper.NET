// SPDX-License-Identifier: GPL-2.0-only

// Covers: MapperExtractor.ReadMapValues, ReadFlattenRoots, ReadFlattenGraphAttributes — a null name reports what an empty name does
using System.Globalization;
using Microsoft.CodeAnalysis;

// Regression (round 34 B, the silenced-checks inventory): the method-level [MapValue], [Flatten] and [FlattenGraph]
// readers skipped any argument that was not a string. Every parameter they read is a string, so the only value that
// took the skip was null: [MapValue(null!, 1)], [Flatten(null!)] and [FlattenGraph(null!, "Items")] vanished with a
// green build, while the same directive naming an empty member is reported. d474e9f and fc58fb0 settled this for the
// pair-scoped readers on 2026-09-13: a null name is the empty name, which names nothing, and it reaches the same checks.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MethodDirectiveNullNameTests
    {
        private static string Mapper(string directive) => $$"""
                                                          using DwarfMapper;
                                                          using System.Collections.Generic;
                                                          namespace Demo;
                                                          public class Inner { public int X { get; set; } }
                                                          public class Src { public int A { get; set; } public Inner Child { get; set; } = new(); public List<Inner> Children { get; set; } = new(); }
                                                          public class Dst { public int A { get; set; } public int X { get; set; } public List<int> Items { get; set; } = new(); }
                                                          [DwarfMapper]
                                                          public partial class M
                                                          {
                                                              {{directive}}
                                                              public partial Dst Map(Src s);
                                                          }
                                                          """;

        private static string[] Reports(string directive) =>
            GeneratorTestHarness.Run(Mapper(directive), NullableContextOptions.Enable).Diagnostics
                .Where(d => d.Id.StartsWith("DWARF", StringComparison.Ordinal))
                .Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

        [Theory]
        [InlineData("[MapValue(null!, 1)]", "[MapValue(\"\", 1)]")]
        [InlineData("[Flatten(null!)]", "[Flatten(\"\")]")]
        [InlineData("[FlattenGraph(null!, \"Items\")]", "[FlattenGraph(\"\", \"Items\")]")]
        [InlineData("[FlattenGraph(\"Children\", null!)]", "[FlattenGraph(\"Children\", \"\")]")]
        public void A_null_name_reports_what_an_empty_name_reports(string withNull, string withEmpty)
        {
            var empty = Reports(withEmpty);

            // Non-vacuity: the empty name is reported beyond what the mapper reports without the directive at all,
            // so "the null one reports the same" cannot pass by both saying nothing.
            Assert.NotEqual(Reports(string.Empty), empty);
            Assert.Equal(empty, Reports(withNull));
        }
    }
}
