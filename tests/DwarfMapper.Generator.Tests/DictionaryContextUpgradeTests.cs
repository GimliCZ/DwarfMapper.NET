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
    }
}
