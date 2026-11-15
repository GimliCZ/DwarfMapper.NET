// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>Under Preserve, which declared mapping methods a collection or dictionary helper calls through their
    ///     context-taking companion</b> (round 35, T12): every one whose element is a reference, and no other. The
    ///     dispatch and the flatten are in <c>DispatchPairUnderReferenceHandlingTests</c> and
    ///     <c>FlattenGraphNestedPairTests</c>; these are the shapes that are neither.
    /// </summary>
    public class DeclaredElementUnderPreserveTests
    {
        private const string Preserve = "ReferenceHandling = ReferenceHandlingStrategy.Preserve";

        private static string Generated(string source)
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
            return generated;
        }

        // With AutoNest off nothing is synthesized for the pair, so the declared method is every nested use.
        [Fact]
        public void A_class_pair_with_AutoNest_off_is_called_through_its_companion()
        {
            var generated = Generated($$"""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public class Item { public int V { get; set; } }
                                        public class ItemDto { public int V { get; set; } }
                                        public class H    { public List<Item> All { get; set; } = new(); public Dictionary<string, Item> ByName { get; set; } = new(); }
                                        public class HDto { public List<ItemDto> All { get; set; } = new(); public Dictionary<string, ItemDto> ByName { get; set; } = new(); }
                                        [DwarfMapper({{Preserve}}, AutoNest = false)]
                                        public partial class M
                                        {
                                            public partial ItemDto MapItem(Item i);
                                            public partial HDto Map(H h);
                                        }
                                        """);

            Assert.Contains("__DwarfMap_Depth_MapItem(__item, ctx, depth + 1)", generated, StringComparison.Ordinal);
            Assert.Contains("__DwarfMap_Depth_MapItem(__kv.Value, ctx, depth + 1)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("MapItem(__item)", generated, StringComparison.Ordinal);
        }

        // An abstract source is never auto-nested: a plain declared map of the base type's members, no dispatch.
        [Fact]
        public void A_plain_map_from_an_abstract_source_is_called_through_its_companion()
        {
            var generated = Generated($$"""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public abstract class Shape { public string Name { get; set; } = ""; }
                                        public class ShapeDto { public string Name { get; set; } = ""; }
                                        public class H    { public List<Shape> All { get; set; } = new(); }
                                        public class HDto { public List<ShapeDto> All { get; set; } = new(); }
                                        [DwarfMapper({{Preserve}})]
                                        public partial class M
                                        {
                                            public partial ShapeDto MapShape(Shape s);
                                            public partial HDto Map(H h);
                                        }
                                        """);

            Assert.Contains("__DwarfMap_Depth_MapShape(__item, ctx, depth + 1)", generated, StringComparison.Ordinal);
        }

        // A value has no identity to keep. A declared enum map, and a declared struct map with AutoNest off, are
        // called as the public methods they are, and no companion is made for them.
        [Fact]
        public void A_value_type_element_keeps_the_public_call_and_gets_no_companion()
        {
            var generated = Generated($$"""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public enum Status { Open, Closed }
                                        public enum StatusDto { Open, Closed }
                                        public struct P { public int X { get; set; } }
                                        public struct PDto { public int X { get; set; } }
                                        public class H    { public List<Status> All { get; set; } = new(); public Dictionary<Status, int> Rank { get; set; } = new(); public List<P> Points { get; set; } = new(); }
                                        public class HDto { public List<StatusDto> All { get; set; } = new(); public Dictionary<StatusDto, int> Rank { get; set; } = new(); public List<PDto> Points { get; set; } = new(); }
                                        [DwarfMapper({{Preserve}}, AutoNest = false)]
                                        public partial class M
                                        {
                                            public partial StatusDto MapStatus(Status s);
                                            public partial PDto MapPoint(P p);
                                            public partial HDto Map(H h);
                                        }
                                        """);

            Assert.Contains("(MapStatus(__item))", generated, StringComparison.Ordinal);
            Assert.Contains("[MapStatus(__kv.Key)]", generated, StringComparison.Ordinal);
            Assert.Contains("(MapPoint(__item))", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMap_Depth_", generated, StringComparison.Ordinal);
        }
    }
}
