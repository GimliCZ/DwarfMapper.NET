// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;
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
        // allowUnsafe for the fixed-buffer fixture; it changes nothing for the others.
        private static bool ReportsNearMiss(string source)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source, allowUnsafe: true);
            return diagnostics.Any(d => d.Id == "DWARF100");
        }

        private static string NearMissReason(string source)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source, allowUnsafe: true);
            var hint = diagnostics.SingleOrDefault(d => d.Id == "DWARF100");
            return hint is null ? string.Empty : hint.GetMessage(CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     One fixture per reason the classifier can give, each paired with the phrase that identifies it.
        ///     Consumed twice: once to prove each branch reports what it claims, and once — against the
        ///     generator's own source — to prove no branch exists that nothing here reaches.
        /// </summary>
        public static TheoryData<string, string> EveryReasonBranch =>
            new()
            {
                {
                    // Shaped against Guid's ACTUAL field list — Int32, Int16, Int16, then eight Bytes — because
                    // the metadata branch sits behind the shape check and is unreachable until the fields line
                    // up. A guessed shape here would have made this case pass by reporting nothing at all.
                    "metadata", """
                                using DwarfMapper;
                                namespace Demo;
                                public struct SrcV
                                {
                                    public int A; public short B; public short C;
                                    public byte D; public byte E; public byte F; public byte G;
                                    public byte H; public byte I; public byte J; public byte K;
                                }
                                public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                public class D { public System.Guid[] V { get; set; } = System.Array.Empty<System.Guid>(); }
                                [DwarfMapper] public partial class M { public partial D Map(C c); }
                                """
                },
                {
                    "not Sequential", """
                                     using System.Runtime.InteropServices;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public struct SrcV { public int X; public int Y; }
                                     [StructLayout(LayoutKind.Auto)]
                                     public struct DstV { public int X; public int Y; }
                                     public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                     public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                     [DwarfMapper] public partial class M { public partial D Map(C c); }
                                     """
                },
                {
                    "packs to", """
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
                                """
                },
                {
                    "is named", """
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
                                """
                },
                {
                    // The split side is the DESTINATION so that the source keeps the literal `public struct SrcV {`
                    // the shape-breaking test below rewrites; the rule itself is symmetric.
                    "more than one partial declaration", """
                                                         using DwarfMapper;
                                                         namespace Demo;
                                                         public struct SrcV { public int X; public int Y; }
                                                         public partial struct DstV { public int X; }
                                                         public partial struct DstV { public int Y; }
                                                         public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                                         public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                                         [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                         """
                },
                {
                    "explicit [StructLayout] Size", """
                                                    using System.Runtime.InteropServices;
                                                    using DwarfMapper;
                                                    namespace Demo;
                                                    public struct SrcV { public int X; }
                                                    [StructLayout(LayoutKind.Sequential, Size = 32)]
                                                    public struct DstV { public int X; }
                                                    public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                                    public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                                    [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                    """
                },
                {
                    "[InlineArray] repeats its one field", """
                                                          using System.Runtime.CompilerServices;
                                                          using DwarfMapper;
                                                          namespace Demo;
                                                          public struct SrcV { public int E; }
                                                          [InlineArray(4)]
                                                          public struct DstV { public int E; }
                                                          public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                                          public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                          """
                },
                {
                    "fixed buffer's length is part of the layout", """
                                                                  using DwarfMapper;
                                                                  namespace Demo;
                                                                  public struct SrcV { public int Tag; public unsafe fixed int Buf[4]; }
                                                                  public struct DstV { public int Tag; public unsafe fixed int Buf[8]; }
                                                                  public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                                                  public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                                  """
                },
                {
                    // Round 29: a member that is Nullable<T> on exactly one side, where the unwrapped T is itself
                    // layout-identical to the other side — one `?` away from the fast path (T0.1).
                    "Nullable<T> on one side only", """
                                                   using DwarfMapper;
                                                   namespace Demo;
                                                   public struct Addr { public int X; public int Y; }
                                                   public struct SrcV { public long Id; public Addr? Ship; }
                                                   public struct DstV { public long Id; public Addr Ship; }
                                                   public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                                                   public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                                                   [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                   """
                },
            };

        [Theory]
        [MemberData(nameof(EveryReasonBranch))]
        public void Each_reason_branch_reports_the_reason_it_claims(string phrase, string source)
        {
            Assert.Contains(phrase, NearMissReason(source), StringComparison.Ordinal);
        }

        [Fact]
        public void No_reason_branch_exists_that_no_fixture_reaches()
        {
            // The pack branch shipped with NO test, and the metadata branch shipped MIS-ORDERED, and eight
            // green tests noticed neither. A per-branch fixture list only helps while it is complete, so
            // completeness is read off the generator's own source rather than trusted: every `reason =`
            // assignment in TryExplainNearMiss must have a fixture above that provokes it.
            var source = File.ReadAllText(Path.Combine(RepoPaths.Root,
                "src",
                "DwarfMapper.Generator",
                "Pipeline",
                "BlittableProof.cs"));

            var start = source.IndexOf("TryExplainNearMiss", StringComparison.Ordinal);
            Assert.True(start >= 0, "TryExplainNearMiss not found — this scan is reading the wrong file.");
            var end = source.IndexOf("private static bool LayoutIdentical", start, StringComparison.Ordinal);
            Assert.True(end > start, "Could not bound the method — the scan would read the whole file.");

            var literals = Regex.Matches(source.Substring(start, end - start), @"\breason\s*=\s*\$?""([^""]*)""")
                .Select(m => m.Groups[1].Value)
                .ToList();

            // Eleven assignment SITES for eight reason KINDS — the metadata, non-Sequential and split-declaration
            // reasons each have an 'a' arm and a 'b' arm. Counting sites would therefore be the wrong assertion;
            // what must hold is that no site can produce wording that no fixture provokes.
            Assert.NotEmpty(literals);
            var phrases = EveryReasonBranch.Select(row => (string)row[0]).ToList();
            var unreached = literals
                .Where(lit => !phrases.Exists(p => lit.Contains(p, StringComparison.Ordinal)))
                .ToList();

            Assert.True(unreached.Count == 0,
                "reason branch(es) in TryExplainNearMiss that no fixture in EveryReasonBranch provokes — add "
                + "one, or the branch ships untested the way the pack branch did:\n  "
                + string.Join("\n  ", unreached));
        }

        [Theory]
        [MemberData(nameof(EveryReasonBranch))]
        public void No_blocker_can_report_when_the_SHAPES_do_not_align(string phrase, string source)
        {
            // The ordering invariant, pinned directly rather than left to one spot fixture.
            //
            // The blockers used to be tested BEFORE the shape, so a pair could be announced as "nearly
            // layout-identical" on the strength of a layout attribute alone, without anything having compared
            // its fields. Here every blocker fixture is re-run with one extra field bolted onto the source
            // struct: the shapes no longer align, so whatever the blocker says, the answer must be silence.
            _ = phrase;
            ArgumentNullException.ThrowIfNull(source);
            var shapeBroken = Regex.Replace(source,
                @"public struct SrcV\s*\{",
                "public struct SrcV { public long __Extra;");
            Assert.NotEqual(source, shapeBroken); // the substitution must actually have happened

            Assert.False(ReportsNearMiss(shapeBroken));
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
