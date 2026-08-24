// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>[Reinterpret]</c> is the one place a consumer can override the blit's layout proof, so it is the
    ///     one place a consumer can reach an unsafe emission from their own source. These pin where the
    ///     override stops.
    ///     <para>
    ///         What it MAY override: the field-NAME correspondence, which the caller can genuinely know and the
    ///         generator cannot — differing member names across an assembly boundary, say.
    ///     </para>
    ///     <para>
    ///         What it may NOT override: whether the bytes line up. A caller cannot know that either, and a
    ///         mismatched pair does not fail loudly — <c>MemoryMarshal.Cast&lt;int, long&gt;</c> HALVES the span
    ///         length, so the copy fills half the destination and leaves the rest zeroed. Silent data loss.
    ///     </para>
    /// </summary>
    public class ReinterpretSafetyTests
    {
        [Fact]
        public void Mismatched_element_SIZES_are_refused_at_compile_time()
        {
            // THE regression test. Same-size was always the documented contract (DWARF022's help text) but was
            // enforced only by a runtime guard inside the emitted copy. When that guard was deleted — correctly,
            // since type safety belongs to the analyzer — this shape briefly emitted a blit with no check
            // anywhere, compile-time or runtime.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class C { public int[] Data { get; set; } = System.Array.Empty<int>(); }
                             public class D { public long[] Data { get; set; } = System.Array.Empty<long>(); }
                             [DwarfMapper] public partial class M
                             {
                                 [Reinterpret("Data")]
                                 public partial D Map(C c);
                             }
                             """;
            var (diagnostics, gen) = GeneratorTestHarness.Run(s);

            Assert.Contains(diagnostics, d => d.Id == "DWARF022");
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Differing_field_NAMES_are_still_overridable_which_is_the_whole_point()
        {
            // The behaviour that must survive the fix. [Reinterpret] exists to assert a correspondence the
            // by-name proof rejects; narrowing it to "layout-identical including names" would make it useless.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public float A; public float B; }
                             public struct DstV { public float X; public float Y; }
                             public class C { public SrcV[] Data { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] Data { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M
                             {
                                 [Reinterpret("Data")]
                                 public partial D Map(C c);
                             }
                             """;
            var (diagnostics, gen) = GeneratorTestHarness.Run(s);

            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF022");
            Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_struct_pair_with_differing_field_TYPES_is_refused_even_though_the_total_size_matches()
        {
            // The subtle one. Two 8-byte structs, but {long} against {int,int}: the totals agree while the
            // fields do not, so a byte copy would reinterpret one long as two ints. A size-only check would
            // wave this through; a layout check does not.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public long A; }
                             public struct DstV { public int X; public int Y; }
                             public class C { public SrcV[] Data { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] Data { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M
                             {
                                 [Reinterpret("Data")]
                                 public partial D Map(C c);
                             }
                             """;
            var (diagnostics, gen) = GeneratorTestHarness.Run(s);

            Assert.Contains(diagnostics, d => d.Id == "DWARF022");
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_refusal_names_the_member_so_the_reader_can_find_it()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class C { public int[] Payload { get; set; } = System.Array.Empty<int>(); }
                             public class D { public long[] Payload { get; set; } = System.Array.Empty<long>(); }
                             [DwarfMapper] public partial class M
                             {
                                 [Reinterpret("Payload")]
                                 public partial D Map(C c);
                             }
                             """;
            var (diagnostics, _) = GeneratorTestHarness.Run(s);

            var d022 = Assert.Single(diagnostics.Where(d => d.Id == "DWARF022"));
            Assert.Contains("Payload", d022.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
