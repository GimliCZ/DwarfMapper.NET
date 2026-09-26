// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage for three refusals of MapperExtractor.ResolveConstructorArguments that had never executed in the full suite.
// Each is the constructor-parameter twin of a member-path refusal that was pinned, and each must name the parameter:
//   - an explicit [MapProperty] onto a parameter from a source member that does not exist (DWARF009);
//   - a parameter matching two source members under the constructor's case-insensitive binding (DWARF010);
//   - an auto-matched parameter whose source member has no conversion (DWARF005).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ConstructorArgumentRefusalCoverageTests
    {
        [Fact]
        public void An_explicit_binding_from_a_missing_source_member_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public record Dst(int A);
                               [DwarfMapper]
                               public partial class M { [MapProperty("Nope", "A")] public partial Dst Map(Src s); }
                               """;

            var refusal = Assert.Single(GeneratorTestHarness.Run(src).Diagnostics, d => d.Id == "DWARF009");
            Assert.Contains("'Nope'", refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void A_parameter_matching_two_source_members_case_insensitively_is_ambiguous()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int Name { get; set; } public int name { get; set; } }
                               public record Dst(int Name);
                               [DwarfMapper]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            var refusal = Assert.Single(GeneratorTestHarness.Run(src).Diagnostics, d => d.Id == "DWARF010");
            Assert.Contains("'Name'", refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void An_auto_matched_parameter_without_a_conversion_is_refused()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public Guid A { get; set; } }
                               public record Dst(int A);
                               [DwarfMapper]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            var refusal = Assert.Single(GeneratorTestHarness.Run(src).Diagnostics, d => d.Id == "DWARF005");
            Assert.Contains("'A'", refusal.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
