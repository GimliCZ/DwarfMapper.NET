// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The <c>List&lt;T&gt;</c> fill strategy under NESTING (round 26).
    ///     <para>
    ///         Every object decomposes into its members, and a collection can sit at any depth in that
    ///         decomposition. The fill decision is made per synthesized element-pair helper, so in principle it
    ///         is depth-independent — but "in principle" is how the corpus holes in this repository's history
    ///         got there, and the top-level tests would pass identically if depth silently disabled it.
    ///     </para>
    ///     <para>
    ///         So both halves are pinned at depth: the value-element list still gets the span fill three levels
    ///         down, and the reference-element list still does NOT — the predicate must not decay into
    ///         "anything nested" any more than it may decay into "anything at all".
    ///     </para>
    /// </summary>
    public class ListFillNestedTests
    {
        [Fact]
        public void A_value_element_list_three_levels_down_still_fills_through_the_span()
        {
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Leaf    { public int[] Amounts { get; set; } = System.Array.Empty<int>(); }
                             public class Middle  { public Leaf Inner { get; set; } = new(); }
                             public class Root    { public Middle M { get; set; } = new(); }
                             public class LeafD   { public List<long> Amounts { get; set; } = new(); }
                             public class MiddleD { public LeafD Inner { get; set; } = new(); }
                             public class RootD   { public MiddleD M { get; set; } = new(); }
                             [DwarfMapper] public partial class Mp { public partial RootD Map(Root r); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            Assert.Contains("__d[__i++]", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_value_element_list_INSIDE_a_collection_element_still_fills_through_the_span()
        {
            // The harder decomposition: a list of objects, each of which owns a value-element list. The outer
            // collection takes the Add path (reference elements); the inner one must still take the span.
            // Both helpers are synthesized in the same pass, so this is where a shared flag would leak.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Line   { public int[] Qty { get; set; } = System.Array.Empty<int>(); }
                             public class Order  { public List<Line> Lines { get; set; } = new(); }
                             public class LineD  { public List<long> Qty { get; set; } = new(); }
                             public class OrderD { public List<LineD> Lines { get; set; } = new(); }
                             [DwarfMapper] public partial class Mp { public partial OrderD Map(Order o); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            // the inner, value-element list
            Assert.Contains("__d[__i++]", gen, StringComparison.Ordinal);

            // …and the OUTER list of reference elements still uses Add, in the same generated file
            Assert.Contains(".Add(", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_reference_element_list_three_levels_down_still_uses_Add()
        {
            // The predicate must not decay to "anything nested". If depth were mistaken for a reason to use
            // the span, this fires.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Item    { public int Id { get; set; } public string? Name { get; set; } }
                             public class ItemD   { public int Id { get; set; } public string? Name { get; set; } }
                             public class Leaf    { public Item[] Items { get; set; } = System.Array.Empty<Item>(); }
                             public class Middle  { public Leaf Inner { get; set; } = new(); }
                             public class Root    { public Middle M { get; set; } = new(); }
                             public class LeafD   { public List<ItemD> Items { get; set; } = new(); }
                             public class MiddleD { public LeafD Inner { get; set; } = new(); }
                             public class RootD   { public MiddleD M { get; set; } = new(); }
                             [DwarfMapper] public partial class Mp { public partial RootD Map(Root r); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            Assert.DoesNotContain("__d[__i++]", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nested_STRUCT_element_list_fills_through_the_span()
        {
            // A struct that is NOT blittable-compatible (the field types differ), so the blit is refused and
            // the element loop runs — but the element is still a value type, so the fill applies. This is the
            // case that sits between the two fast paths and would be easy to lose.
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public struct Pt   { public int X; public int Y; }
                             public struct PtD  { public long X; public long Y; }
                             public class Leaf  { public Pt[] Points { get; set; } = System.Array.Empty<Pt>(); }
                             public class Root  { public Leaf Inner { get; set; } = new(); }
                             public class LeafD { public List<PtD> Points { get; set; } = new(); }
                             public class RootD { public LeafD Inner { get; set; } = new(); }
                             [DwarfMapper] public partial class Mp { public partial RootD Map(Root r); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            Assert.Contains("__d[__i++]", gen, StringComparison.Ordinal);

            // and NOT the blit — the element types differ in size, so it is a real conversion
            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);
        }
    }
}
