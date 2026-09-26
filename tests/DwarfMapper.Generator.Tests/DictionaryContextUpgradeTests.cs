// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Dictionary helpers that have to be RE-SYNTHESIZED after the fact, because a recursion cycle runs
    ///     through the dictionary and its key or value converter must start carrying the shared context.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The dictionary arm registers a callback with the nested-map registry rather than deciding this at
    ///         resolution time, and for good reason: whether a cycle exists is not known until every pair has
    ///         been collected, which is strictly later. When the registry finds one, it calls back and the
    ///         helper is emitted again with context-threading converters.
    ///     </para>
    ///     <para>
    ///         A round-27 coverage measurement found that callback's whole body executed by nothing — roughly
    ///         forty lines, and the most intricate code in the arm. It is reachable only when a cycle passes
    ///         through a dictionary whose key or value is itself a mapped object, which is a narrow shape and
    ///         evidently one no existing test had. Both halves are exercised here, because the key and value
    ///         paths are separate code that reads almost identically — the kind of pair where a copy-paste slip
    ///         survives indefinitely.
    ///     </para>
    /// </remarks>
    public class DictionaryContextUpgradeTests
    {
        /// <summary>A cycle through the dictionary VALUE: Node holds a dictionary of Nodes.</summary>
        [Fact]
        public void A_cycle_through_a_dictionary_value_upgrades_the_helper_to_carry_context()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public Dictionary<string, Node> Children { get; set; } = new(); }
                               public class NodeDto { public int Id { get; set; } public Dictionary<string, NodeDto> Children { get; set; } = new(); }
                               [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            // Compiling is not the claim. The claim is that the cycle is tracked, which needs the shared
            // identity context reaching the dictionary helper.
            Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
        }

        /// <summary>The same cycle through the dictionary KEY, which is separate code.</summary>
        [Fact]
        public void A_cycle_through_a_dictionary_key_upgrades_the_helper_to_carry_context()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public Dictionary<Node, string> Links { get; set; } = new(); }
                               public class NodeDto { public int Id { get; set; } public Dictionary<NodeDto, string> Links { get; set; } = new(); }
                               [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The None-mode re-synthesis of an IMMUTABLE dictionary helper, which is a separate emission arm.
        /// </summary>
        /// <remarks>
        ///     <c>SynthesizeInPlace</c> is only registered when the mapper is neither Preserve nor SetNull, and every
        ///     fixture that reached it targeted a mutable <c>Dictionary&lt;,&gt;</c> — the builder arm (last write
        ///     wins, matching the mutable indexer) had never been re-emitted with the depth guard.
        /// </remarks>
        [Fact]
        public void A_None_mode_cycle_through_an_immutable_dictionary_value_re_synthesizes_the_builder_helper()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Immutable;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public ImmutableDictionary<string, Node> Children { get; set; } = ImmutableDictionary<string, Node>.Empty; }
                               public class NodeDto { public int Id { get; set; } public ImmutableDictionary<string, NodeDto> Children { get; set; } = ImmutableDictionary<string, NodeDto>.Empty; }
                               [DwarfMapper]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("ImmutableDictionary.CreateBuilder<", generated, StringComparison.Ordinal);
            Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The same re-synthesis under <c>NullCollections = AsNull</c> into a NULLABLE immutable dictionary: a
        ///     null source stays null instead of becoming <c>Empty</c>.
        /// </summary>
        [Fact]
        public void A_None_mode_cycle_through_a_nullable_immutable_dictionary_keeps_a_null_source_null()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Immutable;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public ImmutableDictionary<string, Node>? Children { get; set; } }
                               public class NodeDto { public int Id { get; set; } public ImmutableDictionary<string, NodeDto>? Children { get; set; } }
                               [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("ImmutableDictionary.CreateBuilder<", generated, StringComparison.Ordinal);
            Assert.Contains("if (src is null) return null;", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The MUTABLE twin of the test above: a None-mode cycle through a nullable <c>Dictionary&lt;,&gt;</c> under
        ///     <c>NullCollections = AsNull</c> re-synthesizes a helper that keeps a null source null.
        /// </summary>
        [Fact]
        public void A_None_mode_cycle_through_a_nullable_mutable_dictionary_keeps_a_null_source_null()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public Dictionary<string, Node>? Children { get; set; } }
                               public class NodeDto { public int Id { get; set; } public Dictionary<string, NodeDto>? Children { get; set; } }
                               [DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
            Assert.Contains("if (src is null) return null;", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A source that is only an <c>IEnumerable&lt;KeyValuePair&lt;,&gt;&gt;</c> has no <c>Count</c> to pre-size the
        ///     destination with, and the re-synthesized helper must build the dictionary without one.
        /// </summary>
        [Fact]
        public void A_None_mode_cycle_through_a_countless_pair_sequence_re_synthesizes_without_pre_sizing()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Node { public int Id { get; set; } public IEnumerable<KeyValuePair<string, Node>> Children { get; set; } = new Dictionary<string, Node>(); }
                               public class NodeDto { public int Id { get; set; } public Dictionary<string, NodeDto> Children { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M { public partial NodeDto Map(Node n); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
            Assert.Contains("global::Demo.NodeDto>();", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("src.Count", generated, StringComparison.Ordinal);
        }
    }
}
