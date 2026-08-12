// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     A <c>[GenerateMap&lt;S,T&gt;]</c> pair is a mapper for those types, and everything that maps those types
///     must go through it.
/// </summary>
/// <remarks>
///     <para>
///         It did not. Element and member resolution searched declared partial METHODS only, and a class-level
///         pair is not one — so a collection pair over a declared element pair synthesized a FRESH element
///         mapper that could not see the declared pair's configuration. Concretely: with a
///         <c>[MapConstructor]</c> factory on the element pair, <c>Map(item)</c> called the factory and
///         <c>Map(list)[0]</c> did not. One class, one pair of types, two different mappings, green build.
///     </para>
///     <para>
///         In the migration that found it (Round 18) the same gap presented as a catch-22 rather than a
///         divergence: the synthesized element mapper failed <c>DWARF024</c> on a constructor parameter it
///         could not bind, while binding that parameter on the factory-bearing declared pair was rejected by
///         <c>DWARF008</c> because there the factory owns construction. "The element pair is simultaneously
///         'has a factory' and 'must construct itself', and no attribute satisfies both." It was worked around
///         consumer-side by giving up the factory.
///     </para>
/// </remarks>
public class DeclaredPairReuseTests
{
    /// <summary>A factory-bearing element pair plus a collection pair over it — the shape that diverged.</summary>
    private const string FactoryElementPair = """
        using System.Collections.Generic;
        using DwarfMapper;
        namespace Demo;
        public class Item { public int V { get; set; } }
        public class ItemDto
        {
            public ItemDto(int v) { V = v; }
            public int V { get; }
        }

        [DwarfMapper]
        [GenerateMap<Item, ItemDto>]
        [MapConstructor<Item, ItemDto>(nameof(Create))]
        [GenerateMap<List<Item>, List<ItemDto>>]
        public partial class M
        {
            private static ItemDto Create(Item i) => new ItemDto(i.V + 100);
        }
        """;

    [Fact]
    public void A_collection_pair_routes_its_elements_through_the_declared_pair()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(FactoryElementPair);

        // The element route is the public Map — which is where the factory lives.
        Assert.Contains("__r.Add(Map(__item))", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void No_second_mapper_is_synthesized_for_a_pair_that_is_already_declared()
    {
        // The divergence itself, stated directly: a private __DwarfMap_Obj_* for Item -> ItemDto would be a
        // SECOND mapping of the same two types on the same class, and the one the collection actually used.
        var generated = GeneratorAssert.EmitsCompilableCode(FactoryElementPair);

        Assert.DoesNotContain("__DwarfMap_Obj_global__Demo_Item_global__Demo_ItemDto", generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_factory_reaches_the_element_under_Preserve_too()
    {
        // Preserve and SetNull deliberately REFUSE to call a public method from a collection helper: a fresh
        // DwarfRefContext per element would reset the identity map and the depth guard, so a cyclic graph
        // routed through the collection edge would recurse until the stack ended. The element therefore has
        // to go through a synthesized helper even after resolution learned to reuse declared pairs — which
        // makes construction the one place the two routes could still disagree. Both must run the factory.
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Node { public int V { get; set; } public List<Node> Kids { get; set; } = new(); }
            public class NodeDto
            {
                public NodeDto(int v) { V = v; }
                public int V { get; }
                public List<NodeDto> Kids { get; set; } = new();
            }

            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            [GenerateMap<Node, NodeDto>]
            [MapConstructor<Node, NodeDto>(nameof(Create))]
            public partial class M
            {
                private static NodeDto Create(Node n) => new NodeDto(n.V + 100);
            }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        // The public entry.
        Assert.Contains("var __dwarf_t = Create(src);", generated, StringComparison.Ordinal);

        // …and the synthesized helper the collection element reaches. Before the fix this line was
        // `new NodeDto(v: s.V)`: the same pair, constructed two different ways in one class.
        Assert.Contains("var __dwarf_t = Create(s);", generated, StringComparison.Ordinal);

        // Nothing constructs NodeDto directly any more.
        Assert.DoesNotContain("new global::Demo.NodeDto(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserve_with_a_factory_used_to_emit_code_that_did_not_compile()
    {
        // The same defect's louder half, pinned separately because it is a different failure: the
        // register-before-populate path ignored the factory and emitted `new T()`. When T had a parameterless
        // constructor that silently dropped the factory; when it did not — the usual reason for writing one —
        // it emitted `new NodeDto()` against a type with no such constructor, i.e. CS7036 in generated code,
        // with nothing in the message naming [MapConstructor].
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Node { public int V { get; set; } public List<Node> Kids { get; set; } = new(); }
            public class NodeDto
            {
                public NodeDto(int v) { V = v; }
                public int V { get; }
                public List<NodeDto> Kids { get; set; } = new();
            }

            [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
            [GenerateMap<Node, NodeDto>]
            [MapConstructor<Node, NodeDto>(nameof(Create))]
            public partial class M
            {
                private static NodeDto Create(Node n) => new NodeDto(n.V + 100);
            }
            """;

        GeneratorAssert.CompilesClean(src);
    }

    [Fact]
    public void The_catch_22_is_gone_a_factory_pair_under_a_collection_compiles()
    {
        // ImplementationRecord.txt:8421. The target's only constructor takes a parameter the source cannot
        // supply, which is exactly why the author wrote a factory. Before the fix the synthesized element
        // mapper had to construct it anyway and failed DWARF024, while satisfying DWARF024 on the declared
        // pair was refused by DWARF008 because the factory owns construction there.
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Item { public int V { get; set; } }
            public class ItemDto
            {
                public ItemDto(int v, string provenance) { V = v; Provenance = provenance; }
                public int V { get; }
                public string Provenance { get; }
            }

            [DwarfMapper]
            [GenerateMap<Item, ItemDto>]
            [MapConstructor<Item, ItemDto>(nameof(Create))]
            [GenerateMap<List<Item>, List<ItemDto>>]
            public partial class M
            {
                private static ItemDto Create(Item i) => new ItemDto(i.V, "imported");
            }
            """;

        GeneratorAssert.CompilesClean(src);
    }

    [Fact]
    public void A_nested_member_of_a_declared_pair_type_routes_through_it()
    {
        // Not only collection elements: an ordinary nested member had the same blind spot.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Item { public int V { get; set; } }
            public class ItemDto { public ItemDto(int v) { V = v; } public int V { get; } }
            public class Box { public Item Only { get; set; } = new(); }
            public class BoxDto { public ItemDto Only { get; set; } = new(0); }

            [DwarfMapper]
            [GenerateMap<Item, ItemDto>]
            [MapConstructor<Item, ItemDto>(nameof(Create))]
            [GenerateMap<Box, BoxDto>]
            public partial class M
            {
                private static ItemDto Create(Item i) => new ItemDto(i.V + 100);
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        Assert.Contains("Only = Map(src.Only", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_partial_method_still_wins_over_the_pair_it_duplicates()
    {
        // Seeding the pair ALONGSIDE a declared method for the same types would put two matching candidates in
        // front of the ambiguity check and turn a legal shape into DWARF013. The declared method wins, which
        // is the precedence the rest of resolution already uses.
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }

            [DwarfMapper]
            [GenerateMap<List<Item>, List<ItemDto>>]
            public partial class M
            {
                public partial ItemDto One(Item source);
            }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        Assert.Contains("__r.Add(One(__item))", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_like_pair_does_not_resolve_to_itself()
    {
        // A pair resolved as a WHOLE — a collection, dictionary or value-like target — must not find itself
        // among the candidates. `[GenerateMap<int, long>]` finding its own Map emits `return Map(src);`:
        // compiles, reports nothing, recurses until the stack ends.
        const string src = """
            using DwarfMapper;
            namespace Demo;

            [DwarfMapper]
            [GenerateMap<int, long>]
            public partial class M { }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        Assert.DoesNotContain("return Map(src)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_collection_pair_does_not_resolve_to_itself_either()
    {
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }

            [DwarfMapper]
            [GenerateMap<List<Item>, List<ItemDto>>]
            public partial class M { }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        Assert.DoesNotContain("return Map(src)", generated, StringComparison.Ordinal);
    }
}
