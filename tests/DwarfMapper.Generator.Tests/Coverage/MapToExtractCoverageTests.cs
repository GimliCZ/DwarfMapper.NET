// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// Coverage for MapToGenerator.Extract — the [MapTo] registry front door's reading of one annotated source — and for the
// namespace-less arm of its Emit. Four outcomes had never executed in the full suite: an application whose constructor
// argument does not bind, a member-form [MapIgnore] whose argument names nothing, the one-[MapProperty]-per-target form,
// and a source type declared in the global namespace. The registry is a separate generator, reachable only through
// GeneratorTestHarness.RunMapTo / RunMapToWithSource.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MapToExtractCoverageTests
    {
        [Fact]
        public void An_application_whose_argument_does_not_bind_declares_no_target_and_emits_nothing()
        {
            // `[MapTo(42)]` is CS1503 in the consumer's compilation; the argument reaches the generator as NO argument,
            // so there is no target to map and nothing to say beyond the compiler's own error.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Dst { public int A { get; set; } }
                               [MapTo(42)] public class Src { public int A { get; set; } }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(src);

            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARFR", StringComparison.Ordinal));
            Assert.DoesNotContain("__DwarfRegistry_Src", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_member_form_ignore_whose_argument_names_nothing_is_quoted_without_a_name()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Dst { public int A { get; set; } }
                               [MapTo(typeof(Dst))] public class Src { public int A { get; set; } [MapIgnore(null!)] public int B { get; set; } }
                               """;

            var warning = Assert.Single(GeneratorTestHarness.RunMapTo(src), d => d.Id == "DWARFR12");
            Assert.StartsWith("[MapIgnore(…)] on 'B'", warning.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        [Fact]
        public void One_map_property_per_target_binds_each_target_to_its_own_destination_in_order()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class DstA { public int X { get; set; } }
                               public class DstB { public int Y { get; set; } }
                               [MapTo(typeof(DstA), typeof(DstB))] public class Src { [MapProperty("X")] [MapProperty("Y")] public int V { get; set; } }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(src);

            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARFR", StringComparison.Ordinal));
            Assert.Contains("X = source.V,", generated, StringComparison.Ordinal);
            Assert.Contains("Y = source.V,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_source_in_the_global_namespace_gets_its_registry_class_outside_any_namespace()
        {
            const string src = """
                               using DwarfMapper;
                               public class GDst { public int A { get; set; } }
                               [MapTo(typeof(GDst))] public class GSrc { public int A { get; set; } }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(src);

            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARFR", StringComparison.Ordinal));
            Assert.Contains("internal static class __DwarfRegistry_GSrc", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("namespace ", generated, StringComparison.Ordinal);
        }
    }
}
