// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    public class EnumToEnumTests
    {
        [Fact]
        public void ByName_default_maps_matching_members()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum Src { Red, Green }
                               public enum Dst { Red, Green }
                               public class A { public Src V { get; set; } }
                               public class B { public Dst V { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial B Map(A a); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("switch", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void ByName_missing_target_member_reports_DWARF015()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum Src { Red, Green, Blue }
                               public enum Dst { Red, Green }
                               public class A { public Src V { get; set; } }
                               public class B { public Dst V { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial B Map(A a); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF015" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Blue", StringComparison.Ordinal));
        }

        [Fact]
        public void Incomplete_enum_in_two_methods_reports_DWARF015_each()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum Src { A, B, Blue }
                             public enum Dst { A, B }
                             public class X1 { public Src V { get; set; } }
                             public class Y1 { public Dst V { get; set; } }
                             public class X2 { public Src V { get; set; } }
                             public class Y2 { public Dst V { get; set; } }
                             [DwarfMapper] public partial class M
                             {
                                 public partial Y1 MapA(X1 x);
                                 public partial Y2 MapB(X2 x);
                             }
                             """;
            var (diagnostics, _) = GeneratorTestHarness.Run(s);
            Assert.True(diagnostics.Count(d => d.Id == "DWARF015") >= 2);
        }

        [Fact]
        public void ByValue_casts_without_completeness_check()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public enum Src { Red, Green, Blue }
                               public enum Dst { Red, Green }
                               public class A { public Src V { get; set; } }
                               public class B { public Dst V { get; set; } }
                               [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                               public partial class M { public partial B Map(A a); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF015");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Flags_ByName_emits_one_arm_per_value_and_skips_an_alias()
        {
            // AliasA shares A's value, so the flags accumulator tests that bit once, under the first name.
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               [Flags] public enum Src { None = 0, A = 1, AliasA = 1, B = 2 }
                               [Flags] public enum Dst { None = 0, A = 1, AliasA = 1, B = 2 }
                               public class X { public Src V { get; set; } }
                               public class Y { public Dst V { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial Y Map(X x); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Single(generated.Split('\n'), l => l.Contains("__r |= global::Demo.Dst.A;", StringComparison.Ordinal));
            Assert.DoesNotContain("Dst.AliasA", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Flags_ByName_missing_target_member_reports_DWARF015()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               [Flags] public enum Src { None = 0, A = 1, B = 2, C = 4 }
                               [Flags] public enum Dst { None = 0, A = 1, B = 2 }
                               public class X { public Src V { get; set; } }
                               public class Y { public Dst V { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial Y Map(X x); }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF015" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("'C'", StringComparison.Ordinal));
        }

        [Fact]
        public void ByName_names_a_warning_level_obsolete_member_under_a_pragma()
        {
            // [Obsolete("old", false)] is still a legal value, so the exhaustive switch names it, inside the scoped
            // CS0612/CS0618 guard. Only the error form is left out.
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public enum Src { Red, [Obsolete("old", false)] Green }
                               public enum Dst { Red, Green }
                               public class X { public Src V { get; set; } }
                               public class Y { public Dst V { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial Y Map(X x); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("#pragma warning disable CS0612, CS0618", generated, StringComparison.Ordinal);
            Assert.Contains("global::Demo.Src.Green => global::Demo.Dst.Green,", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_enum_with_an_attribute_other_than_Flags_maps_by_a_plain_switch()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               [Serializable] public enum Src { Red, Green }
                               public enum Dst { Red, Green }
                               public class X { public Src V { get; set; } }
                               public class Y { public Dst V { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial Y Map(X x); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            Assert.Contains("v switch", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__r |=", generated, StringComparison.Ordinal);
        }
    }
}
