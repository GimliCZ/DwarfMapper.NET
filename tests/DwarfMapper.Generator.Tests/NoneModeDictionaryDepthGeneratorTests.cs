// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     None+Throw mode: a self-referential type reached through a DICTIONARY edge must thread the shared
    ///     depth-guard context into the (re-synthesized) key/value mapper, exactly as
    ///     <see cref="NoneModeCollectionDepthGeneratorTests" /> pins for a list edge. The dictionary helper's
    ///     ctx-upgrade closure (<c>HandleDictionaryConversion</c>'s <c>RecordCtxUpgradeCandidate</c> callback)
    ///     has two independent arms — one per side of the pair — and a self-referential KEY type is the one
    ///     shape that exercises the key-side arm: an ordinary dictionary almost always recurses (if at all)
    ///     through its VALUE, never its key, so this fixture is the only route to that half of the closure.
    /// </summary>
    public class NoneModeDictionaryDepthGeneratorTests
    {
        [Fact]
        public void None_mode_self_referential_dictionary_key_threads_ctx_and_uses_companion()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Tag    { public string Name { get; set; } = ""; public Dictionary<Tag, int>? Children { get; set; } }
                               public class TagDto { public string Name { get; set; } = ""; public Dictionary<TagDto, int>? Children { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial TagDto Map(Tag t); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // The dictionary helper must now take (ctx, depth) and route the KEY through the depth companion.
            Assert.Contains("DwarfRefContext ctx, int depth", generated, StringComparison.Ordinal);
            Assert.Contains("__DwarfMap_Depth_Map", generated, StringComparison.Ordinal);
            // None mode → depth guard present, but NO identity map / on-stack guard.
            Assert.Contains("DwarfMappingDepthException", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("TryGetReference", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("TryEnterNode", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void None_mode_non_recursive_dictionary_stays_zero_overhead()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr    { public string City { get; set; } = ""; }
                               public class AddrDto { public string City { get; set; } = ""; }
                               public class Person    { public Dictionary<string, Addr>? Addrs { get; set; } }
                               public class PersonDto { public Dictionary<string, AddrDto>? Addrs { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial PersonDto Map(Person p); public partial AddrDto Map(Addr a); }
                               """;
            var generated = GeneratorAssert.CompilesClean(src);
            // Non-recursive element → no ctx threading anywhere (zero overhead preserved).
            Assert.DoesNotContain("DwarfRefContext", generated, StringComparison.Ordinal);
        }
    }
}
