// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>DWARF120: a mapper attribute written on a record is reported</b> (round 35, T22).
    ///     <para>
    ///         Every pipeline of the generator reads class declarations, so a record carrying <c>[DwarfMapper]</c> or
    ///         a co-located <c>[GenerateMap]</c> was passed over without a diagnostic: the first failed with the
    ///         compiler's CS8795 and no word from DwarfMapper, the second had no maps and said nothing at all.
    ///     </para>
    /// </summary>
    public class MapperOnRecordTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Order { public int Id { get; set; } }
                                     public class OrderDto { public int Id { get; set; } }
                                     """;

        [Theory]
        [InlineData("[DwarfMapper] public partial record M { public partial OrderDto Map(Order o); }", "M", "[DwarfMapper]")]
        [InlineData("[DwarfMapper] public partial record class M { public partial OrderDto Map(Order o); }", "M", "[DwarfMapper]")]
        [InlineData("[GenerateMap<Order, OrderDto>] public record Host(int Id);", "Host", "[GenerateMap]")]
        public void A_mapper_attribute_on_a_record_is_reported_at_the_record(string declaration, string name, string attribute)
        {
            var diagnostics = GeneratorTestHarness.Run(Types + "\n" + declaration).Diagnostics;

            var report = Assert.Single(diagnostics, d => d.Id == "DWARF120");
            Assert.Equal(DiagnosticSeverity.Error, report.Severity);
            Assert.Equal(
                $"'{name}' is a record, and {attribute} is read on classes only: nothing is generated for it. Declare the mapper as " +
                "a partial class; a record can still be the source or the destination of a map declared on one.",
                report.GetMessage(CultureInfo.InvariantCulture));
            Assert.Equal(4, report.Location.GetLineSpan().StartLinePosition.Line);
        }

        // Both attributes on one record: each is reported, once.
        [Fact]
        public void A_record_carrying_both_attributes_is_reported_for_each()
        {
            var diagnostics = GeneratorTestHarness.Run(Types + "\n[DwarfMapper] [GenerateMap<Order, OrderDto>] public partial record M;").Diagnostics;

            Assert.Equal(new[] { "[DwarfMapper]", "[GenerateMap]" },
                diagnostics.Where(d => d.Id == "DWARF120").Select(d => d.GetMessage(CultureInfo.InvariantCulture).Contains("[DwarfMapper]", StringComparison.Ordinal) ? "[DwarfMapper]" : "[GenerateMap]").OrderBy(a => a, StringComparer.Ordinal));
        }

        // A record as the source or the destination of a map declared on a class is as it always was.
        [Fact]
        public void A_record_mapped_by_a_class_is_not_reported()
        {
            var source = """
                         using DwarfMapper;
                         namespace Demo;
                         public record Order(int Id);
                         public record OrderDto(int Id);
                         [DwarfMapper] [GenerateMap<Order, OrderDto>] public partial class M;
                         """;
            var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF120");
            GeneratorAssert.EmitsCompilableCode(source);
        }
    }
}
