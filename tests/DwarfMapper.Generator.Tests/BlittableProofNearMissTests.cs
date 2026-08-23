// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>DWARF100</c> — the blit near-miss hint (round 25, T0-B).
    ///     <para>
    ///         Half of these tests assert SILENCE, and they are the more important half. An informational
    ///         diagnostic is only worth having while it is rare: one that fired on every ordinary struct-array
    ///         mapping would be suppressed wholesale by the first consumer who met it, taking the cases worth
    ///         reading with it. So the scope is pinned from both sides — the shapes that must report, and the
    ///         shapes that must not.
    ///     </para>
    /// </summary>
    public class BlittableProofNearMissTests
    {
        private static bool ReportsNearMiss(string source)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source);
            return diagnostics.Any(d => d.Id == "DWARF100");
        }

        [Fact]
        public void Auto_layout_reports_the_near_miss_while_the_mapping_still_succeeds()
        {
            // The headline shape: names, types, count and pack all agree, and only [StructLayout(Auto)] —
            // which lets the runtime reorder fields — stops the layout being PROVABLY identical. The mapping
            // works, so without this hint nothing in the build mentions the block copy one attribute away.
            const string s = """
                             using System.Runtime.InteropServices;
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             [StructLayout(LayoutKind.Auto)]
                             public struct DstV { public int X; public int Y; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var (diagnostics, _) = GeneratorTestHarness.Run(s);

            var hint = Assert.Single(diagnostics.Where(d => d.Id == "DWARF100"));
            Assert.Equal(DiagnosticSeverity.Info, hint.Severity);

            // Informational, never a build break: DWARF070 already taught this project what a warning costs
            // under TreatWarningsAsErrors.
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void A_name_mismatch_reconciled_by_MapProperty_still_reports_the_near_miss()
        {
            // The case that earns the diagnostic. A bare name mismatch already fails loudly as DWARF001, so
            // the hint would be redundant beside it — but once the caller has reconciled the names the
            // mapping SUCCEEDS, and the fact that it is copying element-by-element goes unsaid again. The
            // pair is byte-identical; only DwarfMapper's by-name contract keeps it off the fast path.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public int A; public int B; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper]
                             [MapProperty<SrcV, DstV>("X", "A")]
                             [MapProperty<SrcV, DstV>("Y", "B")]
                             public partial class M { public partial D Map(C c); }
                             """;
            Assert.True(ReportsNearMiss(s));
        }

        [Fact]
        public void A_provable_pair_blits_and_says_nothing()
        {
            // Nothing was missed, so there is nothing to report.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public float X; public float Y; }
                             public struct DstV { public float X; public float Y; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);
            Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
            Assert.False(ReportsNearMiss(s));
        }

        [Fact]
        public void A_pair_with_different_field_counts_is_silent()
        {
            // Not a missed fast path — just two different structs. Reporting here is what would make the
            // diagnostic noise, and noise is what gets a whole id suppressed.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public int X; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.False(ReportsNearMiss(s));
        }

        [Fact]
        public void A_pair_whose_field_types_differ_is_silent()
        {
            // Same names and count, but int against long is a CONVERSION. The element loop is the right
            // answer and always was, so there is no fast path being missed.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public long X; public long Y; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.False(ReportsNearMiss(s));
        }

        [Fact]
        public void Two_unrelated_METADATA_structs_are_silent()
        {
            // The regression this exists for. The layout blockers used to be tested BEFORE the shape, so any
            // two distinct metadata structs answered "declared in metadata" without anything having looked at
            // their fields — and `decimal` is not in IsPrimitive, so decimal against Guid is a reachable pair
            // of structs with nothing whatever in common that announced itself as nearly layout-identical.
            // Shape is checked first now, and a pair that is not shaped alike stays silent whatever else is
            // true of it.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class C { public decimal[] V { get; set; } = System.Array.Empty<decimal>(); }
                             public class D { public System.Guid[] V { get; set; } = System.Array.Empty<System.Guid>(); }
                             [DwarfMapper] public partial class M
                             {
                                 [MapProperty("V", "V", Use = nameof(Conv))]
                                 public partial D Map(C c);
                                 private static System.Guid[] Conv(decimal[] v) => System.Array.Empty<System.Guid>();
                             }
                             """;
            Assert.False(ReportsNearMiss(s));
        }

        [Fact]
        public void A_pack_mismatch_reports_the_near_miss()
        {
            // Identical names and types on both sides; only the declared packing differs, so the two layouts
            // genuinely are not the same bytes. Its own branch, so its own test.
            const string s = """
                             using System.Runtime.InteropServices;
                             using DwarfMapper;
                             namespace Demo;
                             [StructLayout(LayoutKind.Sequential, Pack = 1)]
                             public struct SrcV { public byte A; public int X; }
                             [StructLayout(LayoutKind.Sequential, Pack = 4)]
                             public struct DstV { public byte A; public int X; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.True(ReportsNearMiss(s));
        }

        [Fact]
        public void A_managed_element_is_silent()
        {
            // A reference field is a categorical refusal, not a near-miss: no rename or attribute brings this
            // pair anywhere near a block copy.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public string? S; }
                             public struct DstV { public int X; public string? S; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.False(ReportsNearMiss(s));
        }

        [Fact]
        public void The_same_type_on_both_sides_is_silent()
        {
            // Identity already takes the Clone() memmove, so no fast path is being missed.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.False(ReportsNearMiss(s));
        }

        [Fact]
        public void Reinterpret_keeps_DWARF022_as_the_only_voice()
        {
            // The explicit form has its own diagnostic and its own semantics. [Reinterpret] forces the
            // positional copy and returns before conversion resolution runs, so the hint must not appear
            // beside it — a caller who has already declared their intent does not need advice about it.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public int A; public int B; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M
                             {
                                 [Reinterpret("V")]
                                 public partial D Map(C c);
                             }
                             """;
            Assert.False(ReportsNearMiss(s));
        }
    }
}
