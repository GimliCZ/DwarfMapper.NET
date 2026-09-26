// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

// DWARF111: a [ProvidesMap] method left out of the ambient registry because its pair is already registered in the
// assembly. The attribute asks for a registration and got none, silently. (WHICH provider is kept is pinned in
// AmbientRegistrationGeneratorTests.) Two GENERATED maps of one pair are deliberately not reported — see the last
// two tests for the measured reason.
namespace DwarfMapper.Generator.Tests
{
    public sealed class AmbientDuplicateProviderTests
    {
        private const string Types = """
                                     using System.Collections.Generic;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Src { public int Id { get; set; } }
                                     public class Dst { public int Id { get; set; } }

                                     """;

        private static List<string> Dwarf111Messages(string mappers)
        {
            var (diags, _) = GeneratorTestHarness.Run(Types + mappers);
            var hits = diags.Where(d => d.Id == "DWARF111").ToList();
            Assert.All(hits, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
            return hits.Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToList();
        }

        [Theory]
        [InlineData("P1", "P2")]
        [InlineData("Z9", "A0")]
        public void A_ProvidesMap_shadowed_by_a_generated_map_is_reported_whichever_comes_first(string generatedHost, string providerHost)
        {
            var message = Assert.Single(Dwarf111Messages($$"""
                                                           [DwarfMapper][GenerateMap<Src, Dst>] public partial class {{generatedHost}} { }
                                                           [DwarfMapper] public partial class {{providerHost}} { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                                                           """));

            Assert.Equal(
                $"DWARF111: [ProvidesMap] method 'Demo.{providerHost}.Provide' provides 'Demo.Src' -> 'Demo.Dst', " +
                $"which the generated map 'Demo.{generatedHost}.Map' already registers, so the method is not " +
                "registered; [ProvidesMap] is for shapes the generator cannot express. Remove the attribute.",
                message);
        }

        [Fact]
        public void A_ProvidesMap_on_the_generated_map_method_itself_is_reported()
        {
            // The parity gate's cell: the endpoint method is both generated and marked. Nothing extra is registered,
            // so without this report the attribute is accepted, changes nothing and says nothing.
            var message = Assert.Single(Dwarf111Messages("""
                                                         [DwarfMapper] public partial class M { [ProvidesMap] public partial Dst Map(Src s); }
                                                         """));

            Assert.StartsWith(
                "DWARF111: [ProvidesMap] method 'Demo.M.Map' provides 'Demo.Src' -> 'Demo.Dst', which the generated map " +
                "'Demo.M.Map' already registers",
                message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_ProvidesMap_shadowed_by_a_generated_collection_shape_is_reported()
        {
            var message = Assert.Single(Dwarf111Messages("""
                                                         [DwarfMapper][GenerateMap<Src, Dst>] public partial class P1 { }
                                                         [DwarfMapper] public partial class P2 { [ProvidesMap] public static List<Dst> ProvideAll(IEnumerable<Src> s) => new(); }
                                                         """));

            Assert.StartsWith(
                "DWARF111: [ProvidesMap] method 'Demo.P2.ProvideAll' provides 'System.Collections.Generic.IEnumerable<Demo.Src>' -> " +
                "'System.Collections.Generic.List<Demo.Dst>', which the generated map 'Demo.P1.Map' already registers",
                message, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_ProvidesMap_methods_of_one_pair_report_the_one_ignored()
        {
            var message = Assert.Single(Dwarf111Messages("""
                                                         [DwarfMapper] public partial class P1 { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                                                         [DwarfMapper] public partial class P2 { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                                                         """));

            Assert.Equal(
                "DWARF111: [ProvidesMap] method 'Demo.P2.Provide' provides 'Demo.Src' -> 'Demo.Dst', which " +
                "[ProvidesMap] method 'Demo.P1.Provide' already registers, so the method is not registered. Remove all " +
                "but one.",
                message);
        }

        [Fact]
        public void A_mapper_that_cannot_self_register_is_no_competing_provider()
        {
            // P1 has constructor dependencies, so it is DWARF062's and never reaches the registration: P2's
            // [ProvidesMap] is the only provider of the pair and is registered.
            Assert.Empty(Dwarf111Messages("""
                                          public class Dep { }
                                          [DwarfMapper][GenerateMap<Src, Dst>] public partial class P1 { public P1(Dep d) { _ = d; } }
                                          [DwarfMapper] public partial class P2 { [ProvidesMap] public static Dst Provide(Src s) => new() { Id = s.Id }; }
                                          """));
        }

        [Fact]
        public void Two_generated_maps_of_one_pair_on_different_mappers_report_nothing()
        {
            // Deliberately silent. A first cut of DWARF111 reported this and broke eight of this repo's own builds —
            // AotBench's DepthMapper/PreserveMapper/SetNullMapper, DifferentialTests' by-value enum variants,
            // IntegrationTests' attribute-vs-config mappers, CleanCorpus, Conformance — every hit a mapper differing
            // only in class-level policy, which cannot be expressed on one class. Recorded as an owner question.
            Assert.Empty(Dwarf111Messages("""
                                          [DwarfMapper][GenerateMap<Src, Dst>] public partial class P1 { }
                                          [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)][GenerateMap<Src, Dst>] public partial class P2 { }
                                          [DwarfMapper] public partial class U1 { public partial void Update(Src s, Dst d); }
                                          [DwarfMapper] public partial class U2 { public partial void Update(Src s, Dst d); }
                                          """));
        }

        [Fact]
        public void Differently_named_variants_of_one_pair_on_the_same_mapper_report_nothing()
        {
            // The Gallery's own shapes (Ex27 Replace/Patch, Ex45 ToDto/Map): named partial methods are the documented
            // way to declare variants of one pair (DWARF060's remedy). The first cut broke that build too.
            Assert.Empty(Dwarf111Messages("""
                                          [DwarfMapper]
                                          public partial class M
                                          {
                                              public partial Dst ToDto(Src s);
                                              public partial Dst Map(Src s);
                                              public partial void Replace(Src s, Dst d);
                                              public partial void Patch(Src s, Dst d);
                                          }
                                          """));
        }
    }
}
