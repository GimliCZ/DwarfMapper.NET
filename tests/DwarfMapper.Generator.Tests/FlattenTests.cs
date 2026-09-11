// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    public class FlattenTests
    {
        [Fact]
        public void Scalar_flatten_root_reports_DWARF016()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Customer { public string Address { get; set; } = ""; }
                             public class CustomerDto { public int Length { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [Flatten("Address")]
                                 public partial CustomerDto ToDto(Customer c);
                             }
                             """;
            var (diagnostics, gen) = GeneratorTestHarness.Run(s);
            // A string root must NOT silently flatten string.Length.
            Assert.Contains(diagnostics, d => d.Id == "DWARF016");
            Assert.DoesNotContain("Length = c.Address.Length", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Flattens_nested_member_to_top_level()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Address { public string City { get; set; } = ""; }
                             public class Customer { public Address Address { get; set; } = new(); }
                             public class CustomerDto { public string City { get; set; } = ""; }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [Flatten("Address")]
                                 public partial CustomerDto ToDto(Customer c);
                             }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);
            Assert.Contains("City = c.Address.City", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Unknown_flatten_root_reports_DWARF016()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Customer { public string Name { get; set; } = ""; }
                             public class CustomerDto { public string City { get; set; } = ""; }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [Flatten("Nope")]
                                 public partial CustomerDto ToDto(Customer c);
                             }
                             """;
            var (diagnostics, _) = GeneratorTestHarness.Run(s);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF016" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
        }

        [Fact]
        public void Ambiguous_flatten_reports_DWARF017()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class A { public string City { get; set; } = ""; }
                             public class B { public string City { get; set; } = ""; }
                             public class Customer { public A Home { get; set; } = new(); public B Work { get; set; } = new(); }
                             public class CustomerDto { public string City { get; set; } = ""; }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [Flatten("Home")]
                                 [Flatten("Work")]
                                 public partial CustomerDto ToDto(Customer c);
                             }
                             """;
            var (diagnostics, _) = GeneratorTestHarness.Run(s);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF017" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("City", StringComparison.Ordinal));
            // The ambiguity arm `continue`s: the member IS accounted for (by two roots, which is the
            // problem), so it must not ALSO be reported as having no source at all.
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
        }

        [Fact]
        public void A_nullable_flattened_leaf_into_a_converter_is_reported_by_its_dotted_path()
        {
            // DWARF070's noun for a flattened leaf is the ROOT.LEAF path the emitted access spells
            // (`s.Home.Inner`), not the leaf's bare name — the reader has to find the member on the source
            // graph, and 'Inner' alone names nothing on Src. The separator is a one-character literal in the
            // flatten arm, and the mutation leg blanked it without a failure: "HomeInner" reads as a member
            // that does not exist.
            const string src = """
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public class Child { public int V { get; set; } }
                               public class ChildDto { public int V { get; set; } }
                               public class Home { public Child? Inner { get; set; } public string Street { get; set; } = ""; }
                               public class Src { public Home Home { get; set; } = new(); }
                               public class Dst { public ChildDto Inner { get; set; } = new(); public string Street { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [Flatten("Home")]
                                   public partial Dst Map(Src s);
                                   public partial ChildDto ToDto(Child c);
                               }
                               """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src, NullableContextOptions.Enable);
            var d = Assert.Single(diagnostics.Where(x => x.Id == "DWARF070"));
            Assert.Contains("Source member 'Home.Inner' is a nullable reference", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            Assert.Contains("Inner = ToDto(s.Home.Inner!)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flattened_leaf_uses_conversion()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum Color { Red, Green }
                             public class Inner { public Color Shade { get; set; } }
                             public class Src { public Inner Inner { get; set; } = new(); }
                             public class Dst { public int Shade { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [Flatten("Inner")]
                                 public partial Dst ToDto(Src s);
                             }
                             """;
            var (diagnostics, _) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(s); // enum->int on the flattened leaf
        }
    }
}
