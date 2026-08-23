// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    public class MapPropertyTests
    {
        [Fact]
        public void Rename_maps_differently_named_members()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string FullName { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("FullName", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("Name = s.FullName", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Rename_suppresses_DWARF001_for_the_target()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string FullName { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("FullName", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF001");
        }

        [Fact]
        public void Unknown_target_reports_DWARF008()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string FullName { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("FullName", "Nope")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF008" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Nope", StringComparison.Ordinal));
        }

        [Fact]
        public void Unknown_source_reports_DWARF009()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string FullName { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Ghost", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF009" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Ghost", StringComparison.Ordinal));
        }

        [Fact]
        public void Duplicate_explicit_target_reports_DWARF011_and_compiles()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string A { get; set; } = ""; public string B { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("A", "Name")]
                                   [MapProperty("B", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF011" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Name", StringComparison.Ordinal));
            // No duplicate object-initializer member (no CS1912).
            Assert.DoesNotContain(GeneratorTestHarness.RunAndGetCompilationErrors(src), d => d.Id == "CS1912");
        }

        [Fact]
        public void MapIgnore_and_MapProperty_same_target_reports_DWARF012()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Full { get; set; } = ""; }
                               public class Target { public string Name { get; set; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapIgnore("Name")]
                                   [MapProperty("Full", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics,
                d => d.Id == "DWARF012" &&
                     d.GetMessage(CultureInfo.InvariantCulture).Contains("Name", StringComparison.Ordinal));
        }

        [Fact]
        public void MapProperty_incompatible_types_reports_DWARF005()
        {
            // string→int now auto-resolves via IParsable<int>; use truly incompatible types
            // (a custom class that is not implicitly convertible and not IParsable/IFormattable).
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Box { public int Value { get; set; } }
                               public class Source { public Box Full { get; set; } = new(); }
                               public class Target { public int Name { get; set; } }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Full", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF005");
        }

        [Fact]
        public void MapProperty_to_readonly_target_reports_DWARF008()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public string Full { get; set; } = ""; }
                               public class Target { public string Name { get; } = ""; }
                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty("Full", "Name")]
                                   public partial Target Map(Source s);
                               }
                               """;
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            // read-only target is not in writableByName -> treated as unknown/un-writable target
            Assert.Contains(diagnostics, d => d.Id == "DWARF008");
        }

        // ---------------------------------------------------------------------------------------------
        // Regression: found migrating the Round-18 consumer (~300 maps) off AutoMapper 14.
        //
        // A pair-scoped [MapProperty<S,T>(src, tgt, Use = M)] names ONE destination member. If the same
        // SOURCE member also auto-matches a DIFFERENT destination member by name, the converter must not
        // reach that second member — the attribute did not name it.
        //
        // Why this matters more than it looks: the failure is silent. The build stays green, no diagnostic
        // fires, and the only symptom is wrong data. In the case that surfaced it, DispatchDetails.DispatchId
        // fed both PremiumDocument.Id (via a Use= that prefixes a date, "20260811_<guid>") and
        // PremiumDocument.DispatchId (a plain auto-matched copy). Leaking the converter would have written
        // the decorated document id into the plain donation-id column of every new premium record.
        // ---------------------------------------------------------------------------------------------

        [Fact]
        public void Pair_scoped_Use_converter_applies_only_to_the_member_it_names()
        {
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public Guid Code { get; set; } }
                               public class Target
                               {
                                   public string Tag { get; set; } = "";
                                   public string Code { get; set; } = "";
                               }

                               [DwarfMapper]
                               [GenerateMap<Source, Target>]
                               [MapProperty<Source, Target>(nameof(Source.Code), nameof(Target.Tag), Use = nameof(Decorate))]
                               public partial class M
                               {
                                   private static string Decorate(Guid g) => "X_" + g;
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            // The named member gets the converter...
            Assert.Contains("Tag = Decorate(", generated, StringComparison.Ordinal);

            // ...and the member that merely shares the SOURCE does not.
            Assert.DoesNotContain("Code = Decorate(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Use_converter_named_on_one_method_does_not_leak_into_another_method()
        {
            // The same rule across METHODS. ToA dedicates Decorate to its Code member; ToB never mentions
            // it and must get the plain built-in Guid->string conversion.
            //
            // Two methods rather than two class-level [GenerateMap] pairs because the latter shape is
            // DWARF060 (one source, two targets, and C# cannot overload by return type) — nothing is
            // generated at all, so it cannot express this question.
            const string src = """
                               using System;
                               using DwarfMapper;
                               namespace Demo;
                               public class Source { public Guid Code { get; set; } }
                               public class TargetA { public string Code { get; set; } = ""; }
                               public class TargetB { public string Code { get; set; } = ""; }

                               [DwarfMapper]
                               public partial class M
                               {
                                   [MapProperty(nameof(Source.Code), nameof(TargetA.Code), Use = nameof(Decorate))]
                                   public partial TargetA ToA(Source s);

                                   public partial TargetB ToB(Source s);

                                   private static string Decorate(Guid g) => "X_" + g;
                               }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            // ToA asked for it.
            Assert.Contains("Decorate(", generated, StringComparison.Ordinal);

            // ToB did not. Exactly one call site total.
            var occurrences = generated.Split("Decorate(").Length - 1;
            Assert.Equal(1, occurrences);
        }
    }
}
