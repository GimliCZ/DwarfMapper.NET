// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Xunit.Abstractions;

namespace DwarfMapper.Generator.Tests
{
    public class ProjectionTranslatabilityInvariantTests
    {
        private readonly ITestOutputHelper _o;

        public ProjectionTranslatabilityInvariantTests(ITestOutputHelper o)
        {
            _o = o;
        }

        [Fact]
        public void Nested_and_collection_projection_has_no_synthesized_helper()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Linq;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Addr { public string City { get; set; } = ""; }
                               public class AddrDto { public string City { get; set; } = ""; }
                               public class Line { public int Qty { get; set; } }
                               public class LineDto { public int Qty { get; set; } }
                               public class Order { public Addr Home { get; set; } = new(); public List<Line> Lines { get; set; } = new(); }
                               public class OrderDto { public AddrDto Home { get; set; } = new(); public List<LineDto> Lines { get; set; } = new(); }
                               [DwarfMapper] public partial class M { public partial IQueryable<OrderDto> P(IQueryable<Order> q); }
                               """;
            var (diags, generated) = GeneratorTestHarness.Run(src);
            var errs = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            _o.WriteLine("DIAGS: " + string.Join(", ", diags.Select(d => d.Id)));
            _o.WriteLine("ERRS: " + string.Join(" | ", errs.Select(e => e.Id)));
            _o.WriteLine(generated);
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Empty(errs);
            // The whole point: a projection must be a single inlined expression — NO helper call.
            Assert.DoesNotContain("__DwarfMap_", generated, StringComparison.Ordinal);
            Assert.Contains("new global::Demo.AddrDto", generated, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("long", "int")] // narrowing
        public void Unsafe_projection_reports_DWARF028(string srcT, string dstT)
        {
            var src = $$"""
                        using DwarfMapper;
                        using System.Linq;
                        namespace Demo;
                        public class S { public {{srcT}} X { get; set; } }
                        public class D { public {{dstT}} X { get; set; } }
                        [DwarfMapper] public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                        """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            _o.WriteLine("DIAGS: " + string.Join(", ", diags.Select(d => d.Id)));
            Assert.Contains(diags, d => d.Id == "DWARF028");
        }

        [Theory]
        [InlineData("ICollection")]
        [InlineData("IList")]
        [InlineData("IReadOnlyCollection")]
        public void Interface_collection_target_kinds_are_translatable(string targetKind)
        {
            // IsTargetKindTranslatable's switch has a true arm per translatable TargetKind; List/Array/
            // IEnumerable/IReadOnlyList are exercised elsewhere in this file, but nothing had independently
            // driven ICollection, IList or IReadOnlyCollection as a PROJECTION member's target shape.
            var src = $$"""
                        using DwarfMapper;
                        using System.Linq;
                        using System.Collections.Generic;
                        namespace Demo;
                        public class Line { public int Qty { get; set; } }
                        public class LineDto { public int Qty { get; set; } }
                        public class Order { public List<Line> Lines { get; set; } = new(); }
                        public class OrderDto { public {{targetKind}}<LineDto> Lines { get; set; } = new List<LineDto>(); }
                        [DwarfMapper] public partial class M { public partial IQueryable<OrderDto> P(IQueryable<Order> q); }
                        """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            _o.WriteLine("DIAGS: " + string.Join(", ", diags.Select(d => d.Id)));
            Assert.DoesNotContain(diags, d => d.Id == "DWARF028");
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void Reference_handling_on_projection_reports_DWARF028()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Linq;
                               namespace Demo;
                               public class S { public int X { get; set; } }
                               public class D { public int X { get; set; } }
                               [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M { public partial IQueryable<D> P(IQueryable<S> q); }
                               """;
            var (diags, _) = GeneratorTestHarness.Run(src);
            _o.WriteLine("DIAGS: " + string.Join(", ", diags.Select(d => d.Id)));
            Assert.Contains(diags, d => d.Id == "DWARF028");
        }
    }
}
