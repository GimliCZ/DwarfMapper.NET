// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The <c>List&lt;T&gt;</c> fill strategy (round 26).
    ///     <para>
    ///         The list is pre-sized, so <c>Add</c> can never grow it — yet every element pays <c>Add</c>'s
    ///         bookkeeping: <c>_version++</c>, an <c>_items</c> reload, a capacity check that cannot fail, and
    ///         <c>_size++</c>. Writing through the span skips all four. Measured 1.44–1.61x for value elements.
    ///     </para>
    ///     <para>
    ///         Restricted to VALUE element types on measurement, not taste: for reference elements the win is
    ///         zero — 1.00x, then 0.92x on a second run — because allocating the destination objects dominates.
    ///         Half of these tests therefore assert that the OLD shape is still emitted, which is the half that
    ///         would catch someone "simplifying" the predicate away.
    ///     </para>
    /// </summary>
    public class ListFillStrategyTests
    {
        private const string ValueElements = """
                                             using System.Collections.Generic;
                                             using DwarfMapper;
                                             namespace Demo;
                                             public class C { public int[] V { get; set; } = System.Array.Empty<int>(); }
                                             public class D { public List<long> V { get; set; } = new(); }
                                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                                             """;

        private const string ReferenceElements = """
                                                 using System.Collections.Generic;
                                                 using DwarfMapper;
                                                 namespace Demo;
                                                 public class Item { public int Id { get; set; } public string? Name { get; set; } }
                                                 public class ItemDto { public int Id { get; set; } public string? Name { get; set; } }
                                                 public class C { public Item[] V { get; set; } = System.Array.Empty<Item>(); }
                                                 public class D { public List<ItemDto> V { get; set; } = new(); }
                                                 [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                 """;

        [Fact]
        public void A_value_element_list_fills_through_the_span()
        {
            var gen = GeneratorAssert.CompilesClean(ValueElements);

            Assert.Contains("CollectionsMarshal.SetCount", gen, StringComparison.Ordinal);
            Assert.Contains("__d[__i++]", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_REFERENCE_element_list_still_uses_Add()
        {
            // Measured, not assumed: the span fill buys nothing here, so emitting it would be extra shapes
            // for no gain. If someone widens the predicate to "any element type", this fails.
            var gen = GeneratorAssert.CompilesClean(ReferenceElements);

            Assert.DoesNotContain("CollectionsMarshal.SetCount", gen, StringComparison.Ordinal);
            Assert.Contains(".Add(", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void An_UNKNOWN_count_source_still_uses_Add()
        {
            // IEnumerable<T> has no cheap count, so there is no n to SetCount to. The pre-sizing condition and
            // the span-fill condition are the same condition, and this pins that they stay tied.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class C { public IEnumerable<int> V { get; set; } = System.Array.Empty<int>(); }
                             public class D { public List<long> V { get; set; } = new(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            Assert.DoesNotContain("__d[__i++]", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_PRESERVE_list_still_uses_Add_because_the_context_can_observe_it()
        {
            // THE safety boundary. Under ReferenceHandling.Preserve the helper publishes __r into the context
            // BEFORE filling, so a cycle can reach the list mid-fill. SetCount would make it report a Count
            // over a default-valued tail that a cycle could read as real data. Add grows it honestly instead.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Node { public int Id { get; set; } public List<Node> Children { get; set; } = new(); }
                             public class NodeDto { public int Id { get; set; } public List<NodeDto> Children { get; set; } = new(); }
                             [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                             public partial class M { public partial NodeDto Map(Node n); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            Assert.DoesNotContain("__d[__i++]", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void The_span_is_taken_AFTER_SetCount_and_the_index_never_outruns_it()
        {
            // Shape pin. AsSpan before SetCount would hand back a zero-length span and every write would throw;
            // the ordering is what makes the fill valid, so it is asserted rather than assumed.
            var gen = GeneratorAssert.CompilesClean(ValueElements);

            var setCount = gen.IndexOf("CollectionsMarshal.SetCount", StringComparison.Ordinal);
            var asSpan = gen.IndexOf("CollectionsMarshal.AsSpan(__r)", StringComparison.Ordinal);
            var write = gen.IndexOf("__d[__i++]", StringComparison.Ordinal);

            Assert.True(setCount > 0 && asSpan > setCount, "AsSpan must follow SetCount");
            Assert.True(write > asSpan, "the write must follow the span");

            // The list is sized from the SAME expression the loop walks, so the index cannot outrun the span.
            Assert.Contains("var __n = src.Length;", gen, StringComparison.Ordinal);
            Assert.Contains("SetCount(__r, __n)", gen, StringComparison.Ordinal);
        }
    }
}
