// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// Coverage suite for MapperExtractor.Projection.cs's ReportProjectionNullRefIntoNonNullable. Projection's DWARF070 is
// once per source member per method, whichever binding reached it first. The dedup scan was only ever asked with no
// earlier DWARF070 for the same member and no diagnostic of another kind ahead of it:
//   - one nullable source member feeding TWO non-nullable destinations (an explicit rename and the auto-match) is
//     reported once, and both assignments are still null-forgiven;
//   - an unrelated diagnostic already in the list does not count as the member's report.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ProjectionNullRefReportOnceCoverageTests
    {
        private static string Proj(string types, string methodAttrs) =>
            "using DwarfMapper;\nusing System.Linq;\nnamespace Demo;\n" + types + "\n[DwarfMapper]\npublic partial class M\n{\n" + methodAttrs +
            "\n    public partial IQueryable<D> Project(IQueryable<S> src);\n}\n";

        [Fact]
        public void A_nullable_source_member_bound_twice_is_reported_once()
        {
            var source = Proj("""
                              public class S { public string? A { get; set; } }
                              public class D { public string A { get; set; } = ""; public string Copy { get; set; } = ""; }
                              """, "[MapProperty(\"A\", \"Copy\")]");

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            var d = Assert.Single(diagnostics, x => x.Id == "DWARF070");
            Assert.StartsWith("Source member 'A' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Contains("Copy = __s.A!,", generated, StringComparison.Ordinal);
            Assert.Contains("A = __s.A!,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unrelated_diagnostic_ahead_does_not_stand_in_for_the_report()
        {
            var source = Proj("""
                              public class S { public string? A { get; set; } public int N { get; set; } }
                              public class D { public string A { get; set; } = ""; public int B { get; set; } }
                              """, "[MapProperty(\"Nope\", \"B\")]");

            var diagnostics = GeneratorTestHarness.Run(source, NullableContextOptions.Enable).Diagnostics;

            Assert.Single(diagnostics, x => x.Id == "DWARF009");
            var d = Assert.Single(diagnostics, x => x.Id == "DWARF070");
            Assert.StartsWith("Source member 'A' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
