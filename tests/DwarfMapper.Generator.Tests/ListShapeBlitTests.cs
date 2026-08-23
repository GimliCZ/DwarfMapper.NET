// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>R25-02</c> / T2 — the List-involved blit shapes.
    ///     <para>
    ///         The RFC framed this as "extend blit to structs". That had already shipped as Plan 15; what was
    ///         actually missing is that the gate required <c>Target == Array &amp;&amp; SourceIsArray</c>, so
    ///         three shapes stayed scalar however provable their elements were: <c>array → List</c>,
    ///         <c>List → array</c>, and <c>List → List</c>.
    ///     </para>
    ///     <para>
    ///         The element proof is untouched — the same <c>BlittableProof</c> that governs the array path — so
    ///         the refusal tests here are about the STORAGE side: an interface source cannot blit, because
    ///         <c>CollectionsMarshal.AsSpan</c> is declared on the concrete <c>List&lt;T&gt;</c>.
    ///     </para>
    /// </summary>
    public class ListShapeBlitTests
    {
        private const string Types = """
                                     using System.Collections.Generic;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public struct SrcV { public long A; public int B; public int C; }
                                     public struct DstV { public long A; public int B; public int C; }
                                     """;

        private static string Mapper(string srcProp, string dstProp) =>
            Types + $$"""

                      public class C { public {{srcProp}} V { get; set; } = default!; }
                      public class D { public {{dstProp}} V { get; set; } = default!; }
                      [DwarfMapper] public partial class M { public partial D Map(C c); }
                      """;

        [Theory]
        [InlineData("SrcV[]", "List<DstV>")]
        [InlineData("List<SrcV>", "DstV[]")]
        [InlineData("List<SrcV>", "List<DstV>")]
        [InlineData("List<SrcV>", "IList<DstV>")]
        [InlineData("List<SrcV>", "IReadOnlyList<DstV>")]
        [InlineData("List<SrcV>", "ICollection<DstV>")]
        [InlineData("List<SrcV>", "IReadOnlyCollection<DstV>")]
        public void The_List_involved_shapes_blit(string srcProp, string dstProp)
        {
            // The whole ICollection family materialises to List<T>, so one change covers all of them —
            // which is the answer to "can the other ICollection formats blit too".
            var gen = GeneratorAssert.CompilesClean(Mapper(srcProp, dstProp));
            Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_INTERFACE_source_does_not_blit()
        {
            // IReadOnlyList<T> may well be a List<T> at runtime, but the generator cannot prove it and
            // CollectionsMarshal.AsSpan is not declared on the interface. Refused rather than type-tested.
            var gen = GeneratorAssert.CompilesClean(Mapper("IReadOnlyList<SrcV>", "List<DstV>"));
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_HashSet_target_does_not_blit()
        {
            // A set has no span over its storage and no positional meaning. Out of scope by construction.
            var gen = GeneratorAssert.CompilesClean(Mapper("List<SrcV>", "HashSet<DstV>"));
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unprovable_element_pair_does_not_blit_in_a_List_shape_either()
        {
            // The storage change must not become a back door around the element proof. Field names differ,
            // so the pair is refused exactly as it would be array-to-array.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public int A; public int B; }
                             public class C { public List<SrcV> V { get; set; } = new(); }
                             public class D { public List<DstV> V { get; set; } = new(); }
                             [DwarfMapper]
                             [MapProperty<SrcV, DstV>("X", "A")]
                             [MapProperty<SrcV, DstV>("Y", "B")]
                             public partial class M { public partial D Map(C c); }
                             """;
            var (_, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void Nothing_that_can_throw_sits_between_SetCount_and_the_copy()
        {
            // CollectionsMarshal.SetCount exposes uninitialised memory until the copy completes. A throw in
            // that window would be observable, so the element-size guard is emitted BEFORE SetCount and the
            // property is pinned structurally rather than left to a comment someone can delete.
            var gen = GeneratorAssert.CompilesClean(Mapper("SrcV[]", "List<DstV>"));

            var setCount = gen.IndexOf("CollectionsMarshal.SetCount", StringComparison.Ordinal);
            var copyTo = gen.IndexOf("CopyTo", setCount, StringComparison.Ordinal);
            Assert.True(setCount >= 0 && copyTo > setCount, "expected SetCount followed by CopyTo");

            var between = gen.Substring(setCount, copyTo - setCount);
            Assert.DoesNotContain("throw", between, StringComparison.Ordinal);

            // …and the guard really is present, before it — otherwise the assertion above passes vacuously
            // on a helper that never checks sizes at all.
            Assert.Contains("Unsafe.SizeOf<", gen.Substring(0, setCount), StringComparison.Ordinal);
        }

        [Fact]
        public void An_enum_element_blits_through_a_List_shape_too()
        {
            // T1's element proof and T2's storage change compose; neither knows about the other.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public enum Status { A = 1, B = 2 }
                             public enum StatusDto { A = 1, B = 2 }
                             public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                             public class D { public List<StatusDto> V { get; set; } = new(); }
                             [DwarfMapper(EnumStrategy = EnumStrategy.ByValue)]
                             public partial class M { public partial D Map(C c); }
                             """;
            Assert.Contains("MemoryMarshal.Cast<", GeneratorAssert.CompilesClean(s), StringComparison.Ordinal);
        }

        [Fact]
        public void ByName_enums_stay_scalar_through_a_List_shape_as_well()
        {
            // The T1 refusal must not leak just because the storage changed.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public enum Status { A = 1, B = 2 }
                             public enum StatusDto { A = 1, B = 2 }
                             public class C { public Status[] V { get; set; } = System.Array.Empty<Status>(); }
                             public class D { public List<StatusDto> V { get; set; } = new(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            Assert.DoesNotContain("MemoryMarshal.Cast<", GeneratorAssert.CompilesClean(s), StringComparison.Ordinal);
        }
    }
}
