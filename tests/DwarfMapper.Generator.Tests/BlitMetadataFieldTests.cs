// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>R25-06</c> — the metadata-struct question, measured rather than assumed.
    ///     <para>
    ///         The RFC proposed punching an allowlist through <c>BlittableProof.IsSourceSequential</c>'s
    ///         in-source requirement so that <c>Guid</c>, <c>decimal</c> and <c>Nullable&lt;T&gt;</c> could
    ///         blit. Measuring first showed the cases worth having ALREADY work, for a reason nothing had
    ///         pinned: <c>LayoutIdentical</c> returns true immediately for two IDENTICAL types, so a
    ///         <c>decimal</c> field against a <c>decimal</c> field never reaches the in-source check at all.
    ///         Only the top-level element type is required to be source-declared.
    ///     </para>
    ///     <para>
    ///         These tests exist because that behaviour is load-bearing and was accidental — nothing stopped a
    ///         future tightening of the recursion from silently dropping every DTO that carries a money or an
    ///         id. Same-type element pairs (<c>Guid[] → Guid[]</c>) are not covered here: those are identity
    ///         and already take the <c>Clone()</c> memmove, which is the same <c>Buffer.Memmove</c> underneath.
    ///     </para>
    /// </summary>
    public class BlitMetadataFieldTests
    {
        [Fact]
        public void A_struct_carrying_decimal_and_Guid_fields_blits()
        {
            // The realistic DTO shape the RFC was actually reaching for — a money and an identifier.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct Money { public decimal Amount; public System.Guid Currency; }
                             public struct MoneyDto { public decimal Amount; public System.Guid Currency; }
                             public class C { public Money[] V { get; set; } = System.Array.Empty<Money>(); }
                             public class D { public MoneyDto[] V { get; set; } = System.Array.Empty<MoneyDto>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.Contains("MemoryMarshal.Cast<", GeneratorAssert.CompilesClean(s), StringComparison.Ordinal);
        }

        [Fact]
        public void A_struct_carrying_nullable_value_type_fields_blits()
        {
            // Nullable<T> is itself a metadata struct, and matching nullability on both sides means the field
            // types are identical — so the recursion short-circuits before layout is ever questioned.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct P { public int? A; public int? B; }
                             public struct PDto { public int? A; public int? B; }
                             public class C { public P[] V { get; set; } = System.Array.Empty<P>(); }
                             public class D { public PDto[] V { get; set; } = System.Array.Empty<PDto>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.Contains("MemoryMarshal.Cast<", GeneratorAssert.CompilesClean(s), StringComparison.Ordinal);
        }

        [Fact]
        public void CROSS_nullability_must_never_blit()
        {
            // The load-bearing refusal, and the one test here that would fail if the allowlist were ever added
            // carelessly. `int?` and `int` are different sizes AND different meanings; reinterpreting one as
            // the other would read the has-value flag as data.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct P { public int? A; public int? B; }
                             public struct PDto { public int A; public int B; }
                             public class C { public P[] V { get; set; } = System.Array.Empty<P>(); }
                             public class D { public PDto[] V { get; set; } = System.Array.Empty<PDto>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var (_, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_DateTime_field_does_not_blit_however_byte_like_it_looks()
        {
            // DateTime is [StructLayout(Auto)], so its layout is not guaranteed. It reaches the recursion as
            // an IDENTICAL type on both sides and short-circuits to true — which is sound precisely BECAUSE
            // the two sides are the same type: whatever layout the runtime picks, it picks the same one for
            // both. What must not happen is a DateTime being treated as interchangeable with a look-alike.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct Stamp { public System.DateTime At; }
                             public struct StampDto { public long At; }
                             public class C { public Stamp[] V { get; set; } = System.Array.Empty<Stamp>(); }
                             public class D { public StampDto[] V { get; set; } = System.Array.Empty<StampDto>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var (_, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }
    }
}
