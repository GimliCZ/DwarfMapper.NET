// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;

// Coverage suite for MapperExtractor.cs arms reached only by class-level or declaration shapes the suite had not built:
//   - DWARF031, the nested-mapper registry's cap (512 synthesized pairs), which needs a chain deeper than the cap;
//   - ComputeRequiredMustInitialize's required FIELD arm (every fixture's required member was a property);
//   - ReportMemberFormDirectives skipping an attribute on a mapper member that is not a directive at all;
//   - TryFormatConstant's refusals for a non-renderable [MapValue] constant (typeof) and an enum constant whose type
//     does not convert to the destination;
//   - the DWARF057 lookup's name for a co-located host in the global namespace;
//   - the [GenerateMap] pair collector skipping a target that is neither a named nor an array type (dynamic).
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class ExtractorClassLevelCoverageTests
    {
        [Fact]
        public void An_auto_nest_chain_deeper_than_the_registry_cap_reports_DWARF031()
        {
            const int depth = 600;
            var sb = new StringBuilder("using DwarfMapper;\nnamespace Demo;\n");
            for (var i = 0; i < depth; i++)
            {
                sb.Append("public class S").Append(i).Append(" { public S").Append(i + 1).Append("? Next { get; set; } }\n");
                sb.Append("public class D").Append(i).Append(" { public D").Append(i + 1).Append("? Next { get; set; } }\n");
            }

            sb.Append("public class S").Append(depth).Append(" { public int V { get; set; } }\n");
            sb.Append("public class D").Append(depth).Append(" { public int V { get; set; } }\n");
            sb.Append("[DwarfMapper(AutoNest = true)]\npublic partial class M { public partial D0 Map(S0 s); }\n");

            var message = Assert.Single(GeneratorAssert.Reports(sb.ToString(), "DWARF031")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("512", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_required_field_bound_by_the_constructor_is_also_set_in_the_initializer()
        {
            // C# requires a `required` member in the object initializer even when a constructor argument sets it (no
            // [SetsRequiredMembers]); the field form of `required` goes through its own arm.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int X { get; set; } }
                               public class Dst { public Dst(int x) { X = x; } public required int X; }
                               [DwarfMapper]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("x: s.X)", generated, StringComparison.Ordinal);
            Assert.Contains("X = s.X,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unrelated_attribute_on_a_mapper_member_is_not_a_misplaced_directive()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [Obsolete("not a directive")] public int Unrelated { get; set; }
                                   public partial Dst Map(Src s);
                               }
                               """;

            GeneratorAssert.DoesNotReport(src, "DWARF088");
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void A_typeof_map_value_constant_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } public System.Type? T { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapValue(nameof(Dst.T), typeof(int))]
                                   public partial Dst Map(Src s);
                               }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF040")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("must be a string, bool, char, numeric, enum, or null", message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_enum_map_value_constant_that_does_not_convert_to_the_destination_is_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum Kind { A, B }
                               public class Src { public int A { get; set; } }
                               public class Dst { public int A { get; set; } public string Label { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapValue(nameof(Dst.Label), Kind.B)]
                                   public partial Dst Map(Src s);
                               }
                               """;

            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF040")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("enum constant of type 'Demo.Kind' is not assignable to 'string'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_co_located_host_in_the_global_namespace_generates_its_mapper()
        {
            // Every co-located fixture declared a namespace, so the mapper's full name was always namespace-qualified
            // when the DWARF057 collision lookup composed it.
            const string src = """
                               using DwarfMapper;
                               public class GDst { public int Id { get; set; } }
                               [GenerateMap<GHost, GDst>]
                               public partial class GHost { public int Id { get; set; } }
                               """;

            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("global::GDst", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_generate_map_whose_target_is_dynamic_adds_no_pair()
        {
            // `dynamic` is neither a named nor an array type, so the pair is not collected. The compiler already refuses
            // the attribute (CS8970, in the caller's own source); the generator must neither crash nor add a second
            // report, and must emit no mapping.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int Id { get; set; } }
                               [DwarfMapper]
                               [GenerateMap<Src, dynamic>]
                               public partial class M;
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARF", StringComparison.Ordinal));
            Assert.DoesNotContain("Map(", generated, StringComparison.Ordinal);
        }
    }
}
