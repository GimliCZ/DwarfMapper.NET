// SPDX-License-Identifier: GPL-2.0-only

// The [DwarfMapper] option readers (MapperExtractor.Attributes.cs) each match a named argument by key and then by the
// value's type: `named.Key == "NullStrategy" && named.Value.Value is int`. The second half had never been false in the
// suite: every fixture passed a well-typed value. A mistyped argument is exactly what a consumer has on screen while
// editing — the compilation already carries CS0029 — and Roslyn still hands the generator the named argument, holding a
// value of the wrong type. The generator runs on that compilation, so every reader has to fall back to its default
// rather than cast a string to an enum or throw.
namespace DwarfMapper.Generator.Tests.Coverage
{
    public class MistypedMapperOptionsCoverageTests
    {
        [Fact]
        public void Every_mapper_option_with_a_mistyped_value_falls_back_to_its_default()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int A { get; set; } public List<int> Items { get; set; } = new(); }
                               public class Dst { public int A { get; set; } public List<int> Items { get; set; } = new(); }
                               [DwarfMapper(EnumStrategy = "x", EnumStringSource = "x", NullStrategy = "x", CaseInsensitive = "x",
                                   RegisterCollectionShapes = "x", GenerateExtensions = "x", MaxDepth = "x", AutoNest = "x",
                                   AutoMatchMembers = "x", IgnoreObsoleteMembers = "x", SkipNullSourceMembers = "x", AllowNonPublic = "x",
                                   NullCollections = "x", ReferenceHandling = "x", OnCycle = "x", ImplicitConversions = "x",
                                   RequiredMapping = "x", NameConvention = "x")]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);

            // Defaults, not garbage: auto-matching stays on (AutoMatchMembers), no reference handling (a ctx-free
            // collection helper), and the mapping itself is generated rather than withheld behind an error.
            Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("DWARF", StringComparison.Ordinal) && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            Assert.Contains("public partial global::Demo.Dst Map(global::Demo.Src s)", generated, StringComparison.Ordinal);
            Assert.Contains("A = s.A,", generated, StringComparison.Ordinal);
            Assert.Contains("Items = __DwarfMapColl_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
        }
    }
}
