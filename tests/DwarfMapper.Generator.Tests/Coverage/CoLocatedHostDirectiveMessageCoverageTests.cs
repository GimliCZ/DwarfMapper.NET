// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// DWARF089's host-member messages quote what the consumer wrote. Two of their wording arms had no fixture:
// - a [MapIgnore(null)] names nothing, so the message quotes the placeholder rather than printing an empty string;
// - the stacked-directive count names the pair count, singular or plural.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class CoLocatedHostDirectiveMessageCoverageTests
    {
        private static string Dwarf089(string source)
        {
            return Assert.Single(GeneratorTestHarness.Run(source).Diagnostics, d => d.Id == "DWARF089")
                .GetMessage(CultureInfo.InvariantCulture);
        }

        [Fact]
        public void A_named_MapIgnore_with_a_null_name_is_quoted_with_the_placeholder()
        {
            var message = Dwarf089("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public sealed class Person { public string Full { get; set; } = ""; public int Age { get; set; } }
                                   [GenerateMap<Person, PersonDto>]
                                   public sealed class PersonDto { [MapIgnore(null)] public string Name { get; set; } = ""; public int Age { get; set; } }
                                   """);

            Assert.Contains("[MapIgnore(\"…\")] on 'Name' of the co-located host 'PersonDto'", message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(1, 2, "is the destination of 1 [GenerateMap<,>] pair declared on it")]
        [InlineData(2, 3, "is the destination of 2 [GenerateMap<,>] pairs declared on it")]
        public void The_stacked_directive_count_names_the_pairs_in_the_right_number(int pairs, int directives, string expected)
        {
            var maps = pairs == 1
                ? "[GenerateMap<Person, PersonDto>]\n"
                : "[GenerateMap<Person, PersonDto>]\n[GenerateMap<Robot, PersonDto>]\n";
            var stacked = string.Concat(Enumerable.Repeat("[MapProperty(\"Full\")]", directives));

            var message = Dwarf089("""
                                   using DwarfMapper;
                                   namespace Demo;
                                   public sealed class Person { public string Full { get; set; } = ""; }
                                   public sealed class Robot { public string Full { get; set; } = ""; }

                                   """ + maps + "public sealed class PersonDto { " + stacked + " public string Name { get; set; } = \"\"; }\n");

            Assert.Contains($"'Name' carries {directives} member-placement directives", message, StringComparison.Ordinal);
            Assert.Contains(expected, message, StringComparison.Ordinal);
        }
    }
}
