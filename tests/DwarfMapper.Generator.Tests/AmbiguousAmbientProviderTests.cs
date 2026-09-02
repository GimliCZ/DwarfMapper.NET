// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;

// DWARF063 says "provided by more than one ASSEMBLY". It used to count OCCURRENCES: any pair that reached
// the counter twice fired it, including a pair a single assembly listed twice, or one component appearing
// twice in a reference closure. Reported from a consuming solution as 128 warnings that a clean MSBuild build of the
// same solution did not produce at all.
namespace DwarfMapper.Generator.Tests
{
    public class AmbiguousAmbientProviderTests
    {
        private static (string, string) Pair(string s, string d)
        {
            return (s, d);
        }

        [Fact]
        public void One_assembly_listing_the_same_pair_twice_is_not_ambiguous()
        {
            var referenced = new[]
            {
                (Source: "A", Destination: "B", Assembly: "Maps"),
                (Source: "A", Destination: "B", Assembly: "Maps")
            };

            var result = AmbientValidator.AmbiguousProviders([], referenced, "Root");

            Assert.Empty(result);
        }

        [Fact]
        public void The_root_providing_a_pair_a_reference_also_provides_is_ambiguous()
        {
            var result = AmbientValidator.AmbiguousProviders(
                [Pair("A", "B")],
                [(Source: "A", Destination: "B", Assembly: "Maps")],
                "Root");

            var hit = Assert.Single(result);
            Assert.Equal("A", hit.Source);
            Assert.Equal("B", hit.Destination);
        }

        [Fact]
        public void Two_distinct_referenced_assemblies_are_ambiguous_and_both_are_named()
        {
            var result = AmbientValidator.AmbiguousProviders(
                [],
                [
                    (Source: "A", Destination: "B", Assembly: "Beta"),
                    (Source: "A", Destination: "B", Assembly: "Alpha")
                ],
                "Root");

            var hit = Assert.Single(result);
            // Named, so the reader can act on it - the diagnostic carries Location.None and has no other context.
            Assert.Equal("Alpha, Beta", hit.Providers);
        }

        [Fact]
        public void The_reporting_assembly_is_named_alongside_the_reference()
        {
            var result = AmbientValidator.AmbiguousProviders(
                [Pair("A", "B")],
                [(Source: "A", Destination: "B", Assembly: "Maps")],
                "Demo.Api");

            Assert.Equal("Demo.Api, Maps", Assert.Single(result).Providers);
        }

        [Fact]
        public void A_pair_provided_exactly_once_is_never_reported()
        {
            var result = AmbientValidator.AmbiguousProviders(
                [Pair("X", "Y")],
                [(Source: "A", Destination: "B", Assembly: "Maps")],
                "Root");

            Assert.Empty(result);
        }

        [Fact]
        public void Results_are_ordered_deterministically()
        {
            var referenced = new[]
            {
                (Source: "Z", Destination: "Z2", Assembly: "One"),
                (Source: "Z", Destination: "Z2", Assembly: "Two"),
                (Source: "A", Destination: "A2", Assembly: "One"),
                (Source: "A", Destination: "A2", Assembly: "Two")
            };

            var first = AmbientValidator.AmbiguousProviders([], referenced, "Root");
            var second = AmbientValidator.AmbiguousProviders([], referenced.Reverse().ToArray(), "Root");

            Assert.Equal(["A", "Z"], first.Select(r => r.Source));
            Assert.Equal(first.Select(r => r.Source), second.Select(r => r.Source));
        }
    }
}
