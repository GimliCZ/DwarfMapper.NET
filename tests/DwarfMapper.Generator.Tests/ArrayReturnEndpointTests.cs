// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

// A top-level ARRAY target is a collection target like any other. The member-level conversion always accepted
// `T[]`, but two gates in front of the top-level route asked for a named type first:
//   - a declared `partial int[] Map(List<int> s)` was refused as DWARF003 ("must be a partial instance method with a
//     non-void return type and exactly one parameter") — every word of which the method satisfied;
//   - `[GenerateMap<List<Src>, Dto[]>]` was dropped by the pair collector without a word: no method, no diagnostic.
// Opening the route also surfaced two holes the List route already had or narrowly avoided: an extra parameter on a
// top-level collection map was dropped from the implementing half (CS0759), and a source no collection shape accepts
// resolved the method to ITSELF through the user-declared-conversion scan.
namespace DwarfMapper.Generator.Tests
{
    public class ArrayReturnEndpointTests
    {
        private const string Types = """
                                     using DwarfMapper;
                                     using System.Collections.Generic;
                                     namespace Demo;
                                     public class Src { public int A { get; set; } }
                                     public class Dto { public int A { get; set; } }

                                     """;

        [Fact]
        public void Declared_int_array_return_from_a_list_is_a_collection_map_not_DWARF003()
        {
            var src = Types + "[DwarfMapper] public partial class M { public partial int[] Map(List<int> s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            GeneratorAssert.DoesNotReport(src, "DWARF003");
            Assert.Contains("public partial int[] Map(global::System.Collections.Generic.List<int> s)", generated, StringComparison.Ordinal);
            Assert.Contains("return __DwarfMapColl_", generated, StringComparison.Ordinal);
            Assert.Contains("if (src is null) return global::System.Array.Empty<int>();", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Declared_object_array_return_maps_each_element()
        {
            var src = Types + "[DwarfMapper] public partial class M { public partial Dto[] MapAll(IEnumerable<Src> s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("public partial global::Demo.Dto[] MapAll(global::System.Collections.Generic.IEnumerable<global::Demo.Src> s)", generated, StringComparison.Ordinal);
            Assert.Contains("return new global::Demo.Dto", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Declared_nullable_array_return_keeps_a_null_source_null_under_AsNull()
        {
            var src = "#nullable enable\n" + Types +
                      "[DwarfMapper(NullCollections = NullCollectionStrategy.AsNull)] public partial class M { public partial Dto[]? Map(List<Src>? s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("public partial global::Demo.Dto[]? Map(global::System.Collections.Generic.List<global::Demo.Src>? s)", generated, StringComparison.Ordinal);
            Assert.Contains("if (src is null) return null;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Declared_jagged_array_return_maps_the_inner_arrays()
        {
            var src = Types + "[DwarfMapper] public partial class M { public partial int[][] Map(List<List<int>> s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("if (src is null) return global::System.Array.Empty<int[]>();", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void Declared_multidimensional_array_return_is_refused_as_an_unsupported_collection()
        {
            var src = Types + "[DwarfMapper] public partial class M { public partial int[,] Map(List<int> s); }";

            GeneratorAssert.DoesNotReport(src, "DWARF003");
            var message = Assert.Single(GeneratorAssert.Reports(src, "DWARF027")).GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("'Map'", message, StringComparison.Ordinal);
        }

        [Fact]
        public void GenerateMap_with_an_array_target_is_emitted_rather_than_dropped()
        {
            var src = Types + "[DwarfMapper] [GenerateMap<Src, Dto>] [GenerateMap<List<Src>, Dto[]>] public partial class M { }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);
            Assert.Contains("public global::Demo.Dto Map(global::Demo.Src src)", generated, StringComparison.Ordinal);
            Assert.Contains("public global::Demo.Dto[] Map(global::System.Collections.Generic.List<global::Demo.Src> src)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void GenerateMap_array_to_array_is_emitted()
        {
            var src = Types + "[DwarfMapper] [GenerateMap<Src[], Dto[]>] public partial class M { }";

            Assert.Contains("public global::Demo.Dto[] Map(global::Demo.Src[] src)", GeneratorAssert.EmitsCompilableCode(src), StringComparison.Ordinal);
        }

        [Fact]
        public void Co_located_GenerateMap_with_an_array_target_is_emitted_into_the_host_mapper()
        {
            var src = Types + "[GenerateMap<List<Src>, Dto[]>] public sealed class Host { }";

            Assert.Contains("public global::Demo.Dto[] Map(global::System.Collections.Generic.List<global::Demo.Src> src)", GeneratorAssert.EmitsCompilableCode(src), StringComparison.Ordinal);
        }

        [Fact]
        public void GenerateWrapperMap_wraps_an_array_target_pair()
        {
            var src = Types + "public class Env<T> { public T Payload { get; set; } = default!; }\n" +
                      "[DwarfMapper] [GenerateMap<List<Src>, Dto[]>] [GenerateWrapperMap(typeof(Env<>))] public partial class M { }";

            Assert.Contains("public global::Demo.Env<global::Demo.Dto[]> Map(global::Demo.Env<global::System.Collections.Generic.List<global::Demo.Src>> src)",
                GeneratorAssert.EmitsCompilableCode(src), StringComparison.Ordinal);
        }

        [Fact]
        public void An_array_return_from_a_span_source_is_refused_instead_of_calling_itself()
        {
            var src = Types + "[DwarfMapper] public partial class M { public partial Dto[] Map(System.ReadOnlySpan<Src> s); }";

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF005");
            Assert.DoesNotContain("__DwarfMap_Depth_Map", generated, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Dto[]")]
        [InlineData("List<Dto>")]
        public void An_extra_parameter_on_a_top_level_collection_map_is_refused_instead_of_dropped(string returnType)
        {
            var src = Types + "[DwarfMapper] public partial class M { public partial " + returnType + " Map(List<Src> s, int x); }";

            var (diagnostics, generated) = GeneratorTestHarness.Run(src);
            Assert.Contains(diagnostics, d => d.Id == "DWARF003");
            Assert.DoesNotContain("Map(global::System.Collections.Generic.List<global::Demo.Src> s)", generated, StringComparison.Ordinal);
        }
    }
}
