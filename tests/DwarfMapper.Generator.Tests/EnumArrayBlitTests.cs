// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>R25-03</c> — enum arrays as an underlying-primitive blit.
    ///     <para>
    ///         The decisive question here is NOT layout. Two enums over the same underlying type are trivially
    ///         byte-identical; what decides is whether the SCALAR path — the permanent oracle — is itself a
    ///         reinterpret. Under <c>ByName</c> it is not, so the refusal tests below matter more than the
    ///         acceptances: an enum may legally hold any value of its underlying type, and the by-name switch
    ///         throws on one that matches no member. A blit would pass it through instead.
    ///     </para>
    /// </summary>
    public class EnumArrayBlitTests
    {
        private const string ByValueMapper = """
                                             using DwarfMapper;
                                             namespace Demo;
                                             public enum Status { A = 1, B = 2 }
                                             public enum StatusDto { A = 1, B = 2 }
                                             public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                                             public class D { public StatusDto[] V { get; set; } = System.Array.Empty<StatusDto>(); }
                                             [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                                             public partial class M { public partial D Map(C c); }
                                             """;

        [Fact]
        public void ByValue_over_the_same_underlying_type_blits()
        {
            // CreateChecked from a type to itself is the identity — it cannot throw and it preserves
            // undefined values, which is exactly what the block copy does.
            var gen = GeneratorAssert.CompilesClean(ByValueMapper);
            Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void ByName_never_blits_even_when_the_values_line_up_perfectly()
        {
            // THE refusal. Same names, same values, same underlying type — and still no blit, because the
            // by-name switch ends in `_ => throw new ArgumentOutOfRangeException(… "Unmapped enum value")`.
            // Blitting would silently pass an undefined value through where the oracle throws.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum Status { A = 1, B = 2 }
                             public enum StatusDto { A = 1, B = 2 }
                             public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                             public class D { public StatusDto[] V { get; set; } = System.Array.Empty<StatusDto>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_enum_and_its_own_underlying_primitive_blit_in_both_directions()
        {
            // enum→numeric and numeric→enum both go through CreateChecked between IDENTICAL types, so they
            // are identity conversions whatever the strategy — no ByValue opt-in needed.
            const string toUnderlying = """
                                        using DwarfMapper;
                                        namespace Demo;
                                        public enum Status { A = 1, B = 2 }
                                        public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                                        public class D { public int[] V { get; set; } = System.Array.Empty<int>(); }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """;
            const string fromUnderlying = """
                                          using DwarfMapper;
                                          namespace Demo;
                                          public enum Status { A = 1, B = 2 }
                                          public class C { public int[] V { get; set; } = System.Array.Empty<int>(); }
                                          public class D { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                          """;

            Assert.Contains("MemoryMarshal.Cast<", GeneratorAssert.CompilesClean(toUnderlying), StringComparison.Ordinal);
            Assert.Contains("MemoryMarshal.Cast<", GeneratorAssert.CompilesClean(fromUnderlying), StringComparison.Ordinal);
        }

        [Fact]
        public void A_different_underlying_type_does_not_blit()
        {
            // byte against int is a genuine conversion — CreateChecked can throw, and the sizes differ, so
            // the reinterpret would be wrong twice over.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum Small : byte { A = 1 }
                             public enum Wide : int { A = 1 }
                             public class C { public Small[] V { get; set; } = System.Array.Empty<Small>(); }
                             public class D { public Wide[] V { get; set; } = System.Array.Empty<Wide>(); }
                             [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                             public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_enum_against_a_DIFFERENT_primitive_does_not_blit()
        {
            // Status is backed by int; long is not its underlying type, so this is a widening conversion.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum Status { A = 1, B = 2 }
                             public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                             public class D { public long[] V { get; set; } = System.Array.Empty<long>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_enum_on_both_sides_still_uses_clone_not_reinterpret()
        {
            // Identity is the existing Clone() memmove. Pinned so the new gate cannot quietly capture it.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum Status { A = 1, B = 2 }
                             public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                             public class D { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                             [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                             public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }
    }
}
