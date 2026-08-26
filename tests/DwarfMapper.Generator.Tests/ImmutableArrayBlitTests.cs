// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>T7</c> — <c>ImmutableArray&lt;T&gt;</c>, the only collection besides <c>List&lt;T&gt;</c> with a
    ///     public route to its storage.
    ///     <para>
    ///         Filed after the maintainer asked whether dictionaries and the other collection formats could
    ///         blit. Probing the BCL settled it: <c>CollectionsMarshal</c> exposes a span for <c>List&lt;T&gt;</c>
    ///         only, and <c>Dictionary</c>/<c>HashSet</c> keep their entries in a private nested struct — but
    ///         <c>ImmutableCollectionsMarshal</c> hands out <c>AsArray</c> and <c>AsImmutableArray</c>, and
    ///         <c>ImmutableArray&lt;T&gt;.AsSpan()</c> is public outright.
    ///     </para>
    /// </summary>
    public class ImmutableArrayBlitTests
    {
        private const string Types = """
                                     using System.Collections.Generic;
                                     using System.Collections.Immutable;
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
        [InlineData("SrcV[]", "ImmutableArray<DstV>")]
        [InlineData("List<SrcV>", "ImmutableArray<DstV>")]
        [InlineData("ImmutableArray<SrcV>", "ImmutableArray<DstV>")]
        [InlineData("ImmutableArray<SrcV>", "DstV[]")]
        [InlineData("ImmutableArray<SrcV>", "List<DstV>")]
        public void ImmutableArray_participates_in_the_blit_in_both_directions(string srcProp, string dstProp)
        {
            var gen = GeneratorAssert.CompilesClean(Mapper(srcProp, dstProp));
            Assert.Contains("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_wrapped_array_is_freshly_allocated_never_the_sources_own()
        {
            // The one hazard unique to this shape. ImmutableCollectionsMarshal.AsImmutableArray WRAPS the
            // array it is handed rather than copying it, so re-wrapping the source's storage would leave two
            // immutable values sharing one buffer — for an immutable type that is a correctness bug, not a
            // saved allocation. Pinned on the emitted shape: a `new DstV[...]` must precede the wrap.
            var gen = GeneratorAssert.CompilesClean(Mapper("ImmutableArray<SrcV>", "ImmutableArray<DstV>"));

            var wrap = gen.IndexOf("ImmutableCollectionsMarshal.AsImmutableArray", StringComparison.Ordinal);
            Assert.True(wrap > 0, "expected the blit to wrap an array into an ImmutableArray");

            var before = gen.Substring(0, wrap);
            Assert.Contains("new global::Demo.DstV[", before, StringComparison.Ordinal);

            // And nothing hands the SOURCE array straight through.
            Assert.DoesNotContain("AsImmutableArray(src", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_default_ImmutableArray_is_guarded_before_its_span_is_taken()
        {
            // ImmutableArray<T> is a struct, so it is never null — but `default` wraps a NULL array, and
            // AsSpan() on it faults. The guard is IsDefaultOrEmpty, not a null check.
            var gen = GeneratorAssert.CompilesClean(Mapper("ImmutableArray<SrcV>", "DstV[]"));

            var guard = gen.IndexOf("IsDefaultOrEmpty", StringComparison.Ordinal);
            var span = gen.IndexOf("src.AsSpan()", StringComparison.Ordinal);
            Assert.True(guard > 0, "expected an IsDefaultOrEmpty guard on the ImmutableArray source");
            Assert.True(span > guard, "the span must only be taken after the default guard");
        }

        [Fact]
        public void An_unprovable_element_pair_does_not_blit_into_an_ImmutableArray_either()
        {
            const string s = """
                             using System.Collections.Immutable;
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public int A; public int B; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public ImmutableArray<DstV> V { get; set; } }
                             [DwarfMapper]
                             [MapProperty<SrcV, DstV>("X", "A")]
                             [MapProperty<SrcV, DstV>("Y", "B")]
                             public partial class M { public partial D Map(C c); }
                             """;
            var (_, gen) = GeneratorTestHarness.Run(s);
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void ImmutableList_does_not_blit()
        {
            // ImmutableList<T> is a balanced tree, not contiguous storage — no span exists to reinterpret.
            var gen = GeneratorAssert.CompilesClean(Mapper("SrcV[]", "ImmutableList<DstV>"));
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }
    }
}
