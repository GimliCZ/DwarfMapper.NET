// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>Under <c>ReferenceHandling = Preserve</c> no static fast path stands in for the registering helper.</b>
    ///     <para>
    ///         Preserve rebuilds the source's topology: the general collection helper registers its target before filling
    ///         it, so one source collection reached twice is one target collection (EmitArray's B3 guarantee). The block
    ///         copies and the SIMD widen are static helpers with no context to register in, and they were reached under
    ///         Preserve anyway - two members sharing one source array came out as two arrays. Found in round 33 T02, whose
    ///         own list shapes were gated off Preserve from the start; these are the paths that were not.
    ///     </para>
    ///     <para>
    ///         Every row is asserted both ways: under Preserve the fast path gives way, and without it the same shape still
    ///         takes the fast path - so a row cannot pass by describing a shape that never reached a fast path at all.
    ///     </para>
    /// </summary>
    public class FastPathsUnderPreserveTests
    {
        private const string Types = """
                                     public struct SrcV { public int X; public int Y; }
                                     public struct DstV { public int X; public int Y; }
                                     public enum SrcE { A = 1, B = 2 }
                                     public enum DstE { A = 1, B = 2 }
                                     """;

        private static string Source(string srcT, string dstT, bool preserve)
        {
            // ByValue, because an enum array is a reinterpret only there (round 25 T1); harmless for the other rows.
            var options = preserve
                ? "ReferenceHandling = ReferenceHandlingStrategy.Preserve, EnumStrategy = EnumStrategy.ByValue"
                : "EnumStrategy = EnumStrategy.ByValue";
            return "using System.Collections.Generic;\nusing DwarfMapper;\nnamespace Demo;\n" + Types + "\n" +
                   "public class S { public " + srcT + " V { get; set; } = default!; }\n" +
                   "public class D { public " + dstT + " V { get; set; } = default!; }\n" +
                   "[DwarfMapper(" + options + ")] public partial class M { public partial D Map(S s); }\n";
        }

        [Theory]
        [InlineData("SrcV[]", "DstV[]", "__DwarfBlit_")]
        [InlineData("SrcE[]", "DstE[]", "__DwarfBlit_")]
        [InlineData("SrcV[]", "List<DstV>", "__DwarfBlitL_")]
        [InlineData("List<SrcV>", "DstV[]", "__DwarfBlitL_")]
        [InlineData("int[]", "long[]", "__DwarfWiden_")]
        public void Under_Preserve_a_fast_path_gives_way_to_the_registering_helper(string srcT, string dstT, string fastHelper)
        {
            var preserved = GeneratorAssert.CompilesClean(Source(srcT, dstT, preserve: true));
            Assert.DoesNotContain(fastHelper, preserved, StringComparison.Ordinal);
            Assert.Contains("ctx.TryGetReference(src, out var __cc)", preserved, StringComparison.Ordinal);

            // The control: without Preserve the very same shape is a fast path.
            Assert.Contains(fastHelper, GeneratorAssert.CompilesClean(Source(srcT, dstT, preserve: false)), StringComparison.Ordinal);
        }

        [Fact]
        public void Under_Preserve_a_near_miss_is_not_reported_because_no_rename_would_reach_a_block_copy()
        {
            // DWARF100 says the pair is "one rename away from a block copy". Under Preserve no block copy is taken, so
            // that rename would change nothing and the advice would be false. Without Preserve it is reported as before.
            const string types = """
                                 public struct Addr { public int Street, City, Zip, Country; }
                                 public struct Order { public long Id; public Addr? Ship; }
                                 public struct OrderDto { public long Id; public Addr Ship; }
                                 public class Src { public Order[] Items { get; set; } = System.Array.Empty<Order>(); }
                                 public class Dst { public OrderDto[] Items { get; set; } = System.Array.Empty<OrderDto>(); }
                                 """;

            static string Mapper(string options) => "using DwarfMapper;\nnamespace Demo;\n" + types + "\n[DwarfMapper" + options +
                                                    "] public partial class M { public partial Dst Map(Src s); }\n";

            var preserved = GeneratorTestHarness.Run(Mapper("(ReferenceHandling = ReferenceHandlingStrategy.Preserve)")).Diagnostics;
            Assert.DoesNotContain(preserved, d => d.Id == "DWARF100");
            Assert.DoesNotContain(preserved, d => d.Severity == DiagnosticSeverity.Error);

            Assert.Single(GeneratorAssert.Reports(Mapper(""), "DWARF100"));
        }
    }
}
