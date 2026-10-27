// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>Under <c>NullCollections = AsNull</c> a null source array maps to null on the fast paths too.</b>
    ///     <para>
    ///         The general collection helper hands a null source back as null when the target member is nullable
    ///         (<c>collEffectiveNullAsNull</c>). The array → array block copies and the SIMD widen are static helpers that
    ///         answer an empty array for a null source, and their gates never asked about AsNull - only the list shapes'
    ///         did - so the same mapper turned a null <c>int[]?</c> into <c>[]</c> while a null <c>Obj[]?</c> beside it
    ///         stayed null. Found by the round-33 fast-path axis audit, the third axis after nullable annotation
    ///         (<c>5fc0a13</c>) and Preserve (<c>a4efa68</c>).
    ///     </para>
    ///     <para>
    ///         Asserted both ways, as <see cref="FastPathsUnderPreserveTests" /> is: with a nullable target the result is
    ///         null like the general path's, and with a non-nullable target - where AsNull falls back to empty on every
    ///         path - the fast path is still taken, so the fix costs no block copy it does not have to.
    ///     </para>
    /// </summary>
    public class FastPathsUnderNullAsNullTests
    {
        private const string Types = """
                                     public struct SrcV { public int X; public int Y; }
                                     public struct DstV { public int X; public int Y; }
                                     public enum SrcE { A = 1, B = 2 }
                                     public enum DstE { A = 1, B = 2 }
                                     public class Obj { public int X { get; set; } }
                                     public class ObjDto { public int X { get; set; } }
                                     """;

        // ByValue, because an enum array is a reinterpret only there (round 25 T1).
        private const string Options = "NullCollections = NullCollectionStrategy.AsNull, EnumStrategy = EnumStrategy.ByValue";

        [Fact]
        public void A_null_source_array_maps_to_null_on_every_fast_path_as_on_the_general_one()
        {
            const string src = "#nullable enable\nusing DwarfMapper;\nnamespace Demo;\n" + Types + """

                               public class S
                               {
                                   public SrcV[]? Blit { get; set; }
                                   public SrcE[]? EnumBlit { get; set; }
                                   public int[]? Widen { get; set; }
                                   public Obj[]? General { get; set; }
                               }
                               public class D
                               {
                                   public DstV[]? Blit { get; set; }
                                   public DstE[]? EnumBlit { get; set; }
                                   public long[]? Widen { get; set; }
                                   public ObjDto[]? General { get; set; }
                               }
                               """ + "\n[DwarfMapper(" + Options + ")] public partial class M { public partial D Map(S s); }\n" + """
                               public static class Probe
                               {
                                   private static string Of(System.Array? a) => a is null ? "null" : "length " + a.Length;
                                   public static string Run()
                                   {
                                       var d = new M().Map(new S());
                                       return $"Blit={Of(d.Blit)} EnumBlit={Of(d.EnumBlit)} Widen={Of(d.Widen)} General={Of(d.General)}";
                                   }
                               }
                               """;

            var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
            Assert.True(asm is not null, string.Join(", ", errors.Select(e => e.Id + " " + e.GetMessage(CultureInfo.InvariantCulture))));

            var result = (string)asm.GetType("Demo.Probe")!.GetMethod("Run")!.Invoke(null, null)!;
            Assert.Equal("Blit=null EnumBlit=null Widen=null General=null", result);
        }

        [Theory]
        [InlineData("SrcV[]", "DstV[]", "__DwarfBlit_")]
        [InlineData("SrcE[]", "DstE[]", "__DwarfBlit_")]
        [InlineData("int[]", "long[]", "__DwarfWiden_")]
        public void With_a_non_nullable_target_AsNull_still_takes_the_fast_path(string srcT, string dstT, string fastHelper)
        {
            // AsNull keeps null only where the target can hold it; a non-nullable member gets an empty array on every
            // path, so the block copy or widen is still exactly the general helper's result.
            var src = "#nullable enable\nusing DwarfMapper;\nnamespace Demo;\n" + Types + "\n" +
                      "public class S { public " + srcT + " V { get; set; } = default!; }\n" +
                      "public class D { public " + dstT + " V { get; set; } = default!; }\n" +
                      "[DwarfMapper(" + Options + ")] public partial class M { public partial D Map(S s); }\n";

            Assert.Contains(fastHelper, GeneratorAssert.CompilesClean(src, NullableContextOptions.Enable), StringComparison.Ordinal);
        }

        [Fact]
        public void Where_null_is_kept_a_near_miss_is_not_reported_because_no_rename_would_reach_a_block_copy()
        {
            // DWARF100 says the pair is "one rename away from a block copy". Where AsNull keeps a null array the block
            // copy gives way, so that rename would change nothing and the advice would be false. With a non-nullable
            // target it is reported as before.
            const string types = """
                                 public struct Addr { public int Street, City, Zip, Country; }
                                 public struct Order { public long Id; public Addr? Ship; }
                                 public struct OrderDto { public long Id; public Addr Ship; }
                                 """;

            static string Mapper(string target) =>
                "#nullable enable\nusing DwarfMapper;\nnamespace Demo;\n" + types + "\n" +
                "public class Src { public Order[]? Items { get; set; } }\n" +
                "public class Dst { public " + target + " Items { get; set; } = System.Array.Empty<OrderDto>(); }\n" +
                "[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)] public partial class M { public partial Dst Map(Src s); }\n";

            var keptNull = GeneratorTestHarness.Run(Mapper("OrderDto[]?"), NullableContextOptions.Enable).Diagnostics;
            Assert.DoesNotContain(keptNull, d => d.Id == "DWARF100");
            Assert.DoesNotContain(keptNull, d => d.Severity == DiagnosticSeverity.Error);

            Assert.Contains(GeneratorTestHarness.Run(Mapper("OrderDto[]"), NullableContextOptions.Enable).Diagnostics,
                d => d.Id == "DWARF100");
        }
    }
}
