// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A <c>[MapDerivedType]</c> pair used inside another map</b> - as a member, a list element, an array
    ///     element, a dictionary key and a dictionary value - in each reference-handling mode (round 35, T12 to T14).
    ///     <para>
    ///         Measured before the fixes, one process per case (<c>Issues/round35/ROUND35-TASKS.md</c>): with an
    ///         abstract base the collection helpers called the public dispatch with no context, so a cycle through the
    ///         list was a stack overflow in every mode and Preserve lost identity between elements; with a concrete
    ///         base under Preserve or SetNull the dispatch was skipped and a derived instance was mapped as its base;
    ///         and SetNull with a cycle through an arm emitted code that did not compile.
    ///     </para>
    ///     <para>The runtime half is <c>DispatchPairNestedRuntimeTests</c> in IntegrationTests.</para>
    /// </summary>
    public class DispatchPairUnderReferenceHandlingTests
    {
        public const string None = "[DwarfMapper]";
        public const string Preserve = "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]";
        public const string SetNull = "[DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]";

        // The public dispatch called on an element, a dictionary entry or an array slot: one argument, no context.
        private static readonly Regex PublicElementCall = new(@"(?<![\w.])MapAnimal\((__item|__kv\.\w+|src\[__i\])\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

        private const string CompanionElementCall = "__DwarfMap_Depth_MapAnimal(__item, ctx, depth + 1)";

        /// <summary>
        ///     A zoo holding the dispatch pair five ways. <paramref name="cyclic" /> gives the derived type a way back
        ///     to the zoo, which puts the dispatch on a recursion cycle.
        /// </summary>
        private static string Source(string mode, bool abstractBase, bool cyclic, string dispatchName = "MapAnimal")
        {
            var modifier = abstractBase ? "abstract " : "";
            var home = cyclic ? "public Zoo? Home { get; set; }" : "";
            var homeDto = cyclic ? "public ZooDto? Home { get; set; }" : "";
            // A base that can be instantiated needs an arm of its own (DWARF119).
            var baseArm = abstractBase ? "" : "[MapDerivedType<Animal, AnimalDto>]";
            return $$"""
                     using DwarfMapper;
                     using System.Collections.Generic;
                     namespace Demo;
                     public {{modifier}}class Animal { public string Name { get; set; } = ""; }
                     public class Dog : Animal { public int Bark { get; set; } {{home}} }
                     public {{modifier}}class AnimalDto { public string Name { get; set; } = ""; }
                     public class DogDto : AnimalDto { public int Bark { get; set; } {{homeDto}} }
                     public class Zoo
                     {
                         public Animal? Star { get; set; }
                         public List<Animal> All { get; set; } = new();
                         public Animal[] Arr { get; set; } = System.Array.Empty<Animal>();
                         public Dictionary<string, Animal> ByName { get; set; } = new();
                         public Dictionary<Animal, int> Rank { get; set; } = new();
                     }
                     public class ZooDto
                     {
                         public AnimalDto? Star { get; set; }
                         public List<AnimalDto> All { get; set; } = new();
                         public AnimalDto[] Arr { get; set; } = System.Array.Empty<AnimalDto>();
                         public Dictionary<string, AnimalDto> ByName { get; set; } = new();
                         public Dictionary<AnimalDto, int> Rank { get; set; } = new();
                     }
                     {{mode}}
                     public partial class M
                     {
                         [MapDerivedType<Dog, DogDto>] {{baseArm}}
                         public partial AnimalDto {{dispatchName}}(Animal a);
                         public partial ZooDto Map(Zoo z);
                     }
                     """;
        }

        public static TheoryData<string, bool, bool> EveryShape()
        {
            var data = new TheoryData<string, bool, bool>();
            foreach (var mode in new[] { None, Preserve, SetNull })
            foreach (var abstractBase in new[] { false, true })
            foreach (var cyclic in new[] { false, true })
            {
                data.Add(mode, abstractBase, cyclic);
            }

            return data;
        }

        private static string Generated(string source)
        {
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            GeneratorAssert.EmitsCompilableCode(source);
            return generated;
        }

        // T14. SetNull, a concrete base and a cycle through the arm: the arm's helper called a context-taking helper
        // with one argument (CS7036), because the pass that hands the context to callers ran once and the helper it
        // had already passed was never looked at again. Every shape is held here, not only the one that failed.
        [Theory]
        [MemberData(nameof(EveryShape))]
        public void The_generated_code_compiles_in_every_mode_and_shape(string mode, bool abstractBase, bool cyclic)
        {
            Generated(Source(mode, abstractBase, cyclic));
        }

        // T12. Where the element's method needs the shared context - it is on a recursion cycle, or the mode is
        // Preserve, which promises one target per source - the helpers call its context-taking companion. They used
        // to call the public method, each call opening a context of its own.
        [Theory]
        [MemberData(nameof(EveryShape))]
        public void A_helper_calls_the_dispatch_with_the_shared_context_where_one_is_needed(string mode, bool abstractBase, bool cyclic)
        {
            var generated = Generated(Source(mode, abstractBase, cyclic));

            if (cyclic || mode == Preserve)
            {
                Assert.Contains(CompanionElementCall, generated, StringComparison.Ordinal);
                Assert.Contains("__DwarfMap_Depth_MapAnimal(__kv.Value, ctx, depth + 1)", generated, StringComparison.Ordinal);
                Assert.Contains("__DwarfMap_Depth_MapAnimal(__kv.Key, ctx, depth + 1)", generated, StringComparison.Ordinal);
                Assert.DoesNotMatch(PublicElementCall, generated);
            }
            else
            {
                // No cycle and no identity to keep: the public method is the whole of it, and nothing is added.
                Assert.Matches(PublicElementCall, generated);
                Assert.DoesNotContain("__DwarfMap_Depth_MapAnimal", generated, StringComparison.Ordinal);
            }
        }

        // T13. Under Preserve and SetNull a concrete base made the pair "a mappable object pair", and it was handed to
        // a synthesized map of the base type's members: a Dog came out as an AnimalDto, with DWARF071 advising the
        // attribute that was already there. A map of the base type's members now exists for one caller only: the
        // dispatch's own arm for the base, which a base that can be instantiated must have (DWARF119). No member,
        // element or entry reaches it except through the dispatch.
        [Theory]
        [MemberData(nameof(EveryShape))]
        public void The_dispatch_is_never_replaced_by_a_map_of_the_base_type(string mode, bool abstractBase, bool cyclic)
        {
            var source = Source(mode, abstractBase, cyclic);
            var (diagnostics, generated) = GeneratorTestHarness.Run(source);

            var uses = BaseMapUse.Matches(generated);
            Assert.Equal(abstractBase, uses.Count == 0);
            Assert.All(uses, use => Assert.Contains(use.Groups["before"].Value, new[] { "global::Demo.Animal __s => ", "private global::Demo.AnimalDto " }));
            Assert.DoesNotContain(diagnostics, d => d.Id == "DWARF071");
        }

        // The map of the base pair, with what stands before it: the base arm's pattern, or its own declaration.
        private static readonly Regex BaseMapUse = new(@"(?<before>global::Demo\.Animal __s => |private global::Demo\.AnimalDto |)__DwarfMap_Obj_global__Demo_Animal_global__Demo_AnimalDto_\w*\(",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

        // An overloaded name cannot be told which overload its companion would stand for, here as everywhere a
        // companion is chosen by name: the helpers keep the public call, and the code still compiles.
        [Fact]
        public void An_overloaded_dispatch_name_is_left_as_the_public_call_under_Preserve()
        {
            var generated = Generated(Source(Preserve, true, false, "Map"));

            Assert.Contains("(Map(__item)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMap_Depth_Map(", generated, StringComparison.Ordinal);
        }

        // The same single pass, in a shape that has nothing to do with a dispatch: a span or async-stream map whose
        // element holds a self-referencing member. The element's helper takes the context; the span method, which
        // sits before it in the list, went on calling it with one argument (CS7036). The option matrix recorded
        // this cell as "MaxDepth silent at the span and async-stream endpoints" - it compared text and never
        // compiled it.
        [Theory]
        [InlineData("[DwarfMapper]", "new global::DwarfMapper.DwarfRefContext(64)")]
        [InlineData("[DwarfMapper(MaxDepth = 1)]", "new global::DwarfMapper.DwarfRefContext(1)")]
        public void A_span_and_a_stream_map_hand_the_context_to_an_element_with_a_recursive_member(string mode, string context)
        {
            var generated = Generated($$"""
                                        using DwarfMapper;
                                        using System;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Node { public int Id { get; set; } public Node? Next { get; set; } }
                                        public sealed class NodeDto { public int Id { get; set; } public NodeDto? Next { get; set; } }
                                        public sealed class Src { public int Id { get; set; } public Node? Root { get; set; } }
                                        public sealed class Dst { public int Id { get; set; } public NodeDto? Root { get; set; } }
                                        {{mode}}
                                        public partial class M
                                        {
                                            public partial void MapSpan(ReadOnlySpan<Src> src, Span<Dst> dst);
                                            public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> src);
                                        }
                                        """);

            Assert.Equal(2, generated.Split(context).Length - 1);
            Assert.Contains("(src[__i], __dwarf_ctx, 0)", generated, StringComparison.Ordinal);
            Assert.Contains("(__item, __dwarf_ctx, 0)", generated, StringComparison.Ordinal);
        }

        private const string Predicates = """
                                          using DwarfMapper;
                                          namespace Demo;
                                          public class Animal { } public class Dog : Animal { }
                                          public class AnimalDto { } public class DogDto : AnimalDto { }
                                          public class G { public Animal? Entry { get; set; } }
                                          public class GDto { public System.Collections.Generic.List<AnimalDto> Nodes { get; set; } = new(); }
                                          [DwarfMapper]
                                          public partial class M
                                          {
                                              [MapDerivedType<Dog, DogDto>] public partial AnimalDto Dispatch(Animal a);
                                              public partial DogDto Dispatch(Dog d);
                                              public AnimalDto Dispatch(Animal a, int x) => new();
                                              public partial AnimalDto Plain(Animal a);
                                              [MapDerivedType<Dog, DogDto>] public AnimalDto Written(Animal a) => new();
                                              [FlattenGraph(nameof(G.Entry), nameof(GDto.Nodes))] [MapDerivedType<Dog, DogDto>] public partial GDto Flat(G g);
                                              [FlattenGraph(nameof(G.Entry), nameof(GDto.Nodes))] public partial GDto Walk(G g);
                                          }
                                          """;

        // Which declared methods keep their nested route: a partial, taking exactly that type, that is a dispatch or a
        // graph flatten (T15) - the two whose body is not a member-by-member map of the pair.
        [Theory]
        [InlineData("Dispatch", "Demo.Animal", true)]
        [InlineData("Dispatch", "Demo.Dog", false)] // the overload for the arm, which carries nothing
        [InlineData("Plain", "Demo.Animal", false)] // no arms, no walk
        [InlineData("Written", "Demo.Animal", false)] // not a partial: the attribute on it is not ours to honour
        [InlineData("Flat", "Demo.G", true)] // a heterogeneous flatten: both attributes
        [InlineData("Walk", "Demo.G", true)] // a flatten alone
        [InlineData("Absent", "Demo.Animal", false)]
        public void Only_a_declared_dispatch_or_flatten_keeps_its_nested_route(string name, string parameterType, bool expected)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DispatchPredicates", Predicates, NullableContextOptions.Enable);
            var mapper = compilation.GetTypeByMetadataName("Demo.M")!;
            var parameter = compilation.GetTypeByMetadataName(parameterType)!;

            Assert.Equal(expected, MapperExtractor.IsDeclaredDispatchOrFlattenMethod(mapper, name, parameter));
        }

        // The registry answers for the extraction that set it up; one nobody set up keeps no route and requires no
        // companion, so a resolver run on its own behaves as it did before either rule existed.
        [Fact]
        public void A_registry_with_no_rule_keeps_no_declared_route_and_requires_no_companion()
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DispatchPredicates", Predicates, NullableContextOptions.Enable);
            var animal = compilation.GetTypeByMetadataName("Demo.Animal")!;
            var registry = new NestedMappingRegistry();

            Assert.False(registry.KeepsDeclaredRoute("Dispatch", animal));
            Assert.False(registry.RequiresDepthCompanion("Dispatch"));

            registry.SetDeclaredRouteRule((name, type) => name == "Dispatch" && SymbolEqualityComparer.Default.Equals(type, animal));
            Assert.True(registry.KeepsDeclaredRoute("Dispatch", animal));
            Assert.False(registry.KeepsDeclaredRoute("Plain", animal));

            Assert.Equal("__DwarfMap_Depth_Dispatch", registry.RequireDepthCompanion("Dispatch"));
            Assert.True(registry.RequiresDepthCompanion("Dispatch"));
        }
    }
}
