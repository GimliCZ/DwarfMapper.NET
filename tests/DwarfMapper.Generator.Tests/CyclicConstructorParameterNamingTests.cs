// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// DWARF030 names the constructor argument that carries the reference cycle — and only that one (owner ruling
// 2026-09-13: "for Node(int v, List<Node> kids) correct them to be exact").
//
// Before: `Node(int v, List<Node> kids)` named `v` and never `kids`; `record ImmutableNode(int V, ImmutableNode? Next)`
// named `V` beside `Next`; `Node(string name, Node? next, Address home)` named all three; and `TreeDto(int v,
// List<TreeDto> kids)` mapped from a distinct `Tree` reported nothing at all — the cycle ran through a collection
// helper the call graph had no node for, so a cyclic Tree silently became two TreeDto instances.
namespace DwarfMapper.Generator.Tests
{
    public class CyclicConstructorParameterNamingTests
    {
        private const string Preserve = "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]";

        private static string[] NamedParameters(string src)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            return diagnostics
                .Where(d => d.Id == "DWARF030")
                .Select(d => Regex.Match(d.GetMessage(CultureInfo.InvariantCulture), "^Member '([^']+)'").Groups[1].Value)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        [Fact]
        public void Self_map_through_a_list_names_the_list_not_the_scalar()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Node
                               {
                                   public Node(int v, List<Node> kids) { V = v; Kids = kids; }
                                   public int V { get; }
                                   public List<Node> Kids { get; }
                               }
                               """ + Preserve + " public partial class M { public partial Node Map(Node n); }";

            Assert.Equal(["kids"], NamedParameters(src));
        }

        [Fact]
        public void Record_self_map_names_the_back_edge_not_the_scalar()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public record ImmutableNode(int V, ImmutableNode? Next);
                               """ + Preserve + " public partial class M { public partial ImmutableNode Map(ImmutableNode n); }";

            Assert.Equal(["Next"], NamedParameters(src));
        }

        [Fact]
        public void Self_map_names_neither_a_string_nor_an_unrelated_reference()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Address { public string Street { get; set; } = ""; }
                               public class Node
                               {
                                   public Node(string name, Node? next, Address home) { Name = name; Next = next; Home = home; }
                                   public string Name { get; }
                                   public Node? Next { get; }
                                   public Address Home { get; }
                               }
                               """ + Preserve + " public partial class M { public partial Node Map(Node n); }";

            Assert.Equal(["next"], NamedParameters(src));
        }

        [Fact]
        public void Self_map_names_a_dictionary_value_and_an_array_element_back_edge()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Node
                               {
                                   public Node(int v, Dictionary<string, Node> byName, Node[] arr) { V = v; ByName = byName; Arr = arr; }
                                   public int V { get; }
                                   public Dictionary<string, Node> ByName { get; }
                                   public Node[] Arr { get; }
                               }
                               """ + Preserve + " public partial class M { public partial Node Map(Node n); }";

            Assert.Equal(["arr", "byName"], NamedParameters(src));
        }

        [Fact]
        public void Distinct_types_cycling_through_a_list_constructor_argument_are_refused()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Tree { public int V { get; set; } public List<Tree> Kids { get; set; } = new(); }
                               public class TreeDto
                               {
                                   public TreeDto(int v, List<TreeDto> kids) { V = v; Kids = kids; }
                                   public int V { get; }
                                   public List<TreeDto> Kids { get; }
                               }
                               """ + Preserve + " public partial class M { public partial TreeDto Map(Tree t); }";

            Assert.Equal(["kids"], NamedParameters(src));
        }

        [Fact]
        public void Distinct_types_cycling_through_a_dictionary_constructor_argument_are_refused()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Tree { public int V { get; set; } public Dictionary<string, Tree> Kids { get; set; } = new(); }
                               public class TreeDto
                               {
                                   public TreeDto(int v, Dictionary<string, TreeDto> kids) { V = v; Kids = kids; }
                                   public int V { get; }
                                   public Dictionary<string, TreeDto> Kids { get; }
                               }
                               """ + Preserve + " public partial class M { public partial TreeDto Map(Tree t); }";

            Assert.Equal(["kids"], NamedParameters(src));
        }

        [Fact]
        public void Distinct_types_naming_the_back_edge_through_a_direct_member()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public Src? Self { get; set; } public int V { get; set; } }
                               public record Dst(Dst? Self, int V);
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            Assert.Equal(["Self"], NamedParameters(src));
        }

        [Fact]
        public void A_nested_pair_names_its_own_back_edge_and_not_the_outer_argument_that_reaches_it()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Child { public int X { get; set; } public Child? Next { get; set; } }
                               public record ChildDto(int X, ChildDto? Next);
                               public class Src { public int Id { get; set; } public Child C { get; set; } = new(); }
                               public record Dst(int Id, ChildDto C);
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            Assert.Equal(["Next"], NamedParameters(src));
        }

        [Fact]
        public void A_struct_self_map_through_a_list_of_itself_is_not_a_reference_cycle()
        {
            // A struct has no identity to register, and the list it holds registers itself before it is filled — so
            // the list's own cycle is reconstructed and there is nothing an argument could fail to point back to.
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public readonly record struct P(int X, List<P> Kids);
                               """ + Preserve + " public partial class M { public partial P Map(P p); }";

            Assert.Empty(NamedParameters(src));
        }

        [Fact]
        public void An_explicitly_mapped_constructor_argument_is_named_by_the_same_rule()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Node
                               {
                                   public Node(int v, Node? next) { V = v; Next = next; }
                                   public int V { get; }
                                   public Node? Next { get; }
                               }
                               """ + Preserve + """
                                                 public partial class M
                                                 {
                                                     [MapProperty("Next", "next")] [MapProperty("V", "v")]
                                                     public partial Node Map(Node n);
                                                 }
                                                 """;

            Assert.Equal(["next"], NamedParameters(src));
        }

        [Fact]
        public void An_explicitly_mapped_struct_self_map_through_a_list_of_itself_is_not_a_reference_cycle()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public readonly struct P
                               {
                                   public P(int x, List<P> kids) { X = x; Kids = kids; }
                                   public int X { get; }
                                   public List<P> Kids { get; }
                               }
                               """ + Preserve + " public partial class M { [MapProperty(\"Kids\", \"kids\")] public partial P Map(P p); }";

            Assert.Empty(NamedParameters(src));
        }

        [Fact]
        public void A_source_back_reference_the_target_never_maps_is_not_a_cycle()
        {
            // Customer.Orders leads back to Order, but CustomerDto has no Orders: mapping the argument never returns
            // to Map(Order). A source-type walk alone would refuse the most common entity shape there is.
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Customer { public string Name { get; set; } = ""; public List<Order> Orders { get; set; } = new(); }
                               public class Order { public int Id { get; set; } public Customer Customer { get; set; } = new(); }
                               public record CustomerDto(string Name);
                               public record OrderDto(int Id, CustomerDto Customer);
                               """ + Preserve + " public partial class M { public partial OrderDto Map(Order o); }";

            Assert.Empty(NamedParameters(src));
            GeneratorAssert.EmitsCompilableCode(src);
        }

        [Fact]
        public void An_overloaded_self_map_names_only_its_own_back_edge()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Other { public int X { get; set; } }
                               public class OtherDto { public int X { get; set; } }
                               public class Node
                               {
                                   public Node(int v, List<Node> kids) { V = v; Kids = kids; }
                                   public int V { get; }
                                   public List<Node> Kids { get; }
                               }
                               """ + Preserve + " public partial class M { public partial Node Map(Node n); public partial OtherDto Map(Other o); }";

            Assert.Equal(["kids"], NamedParameters(src));
        }

        [Fact]
        public void A_cycle_through_a_list_of_an_overloaded_declared_element_map_is_found_by_its_exact_overload()
        {
            // List<Leaf> → List<LeafDto> adopts the declared Map(Leaf); the helper's edge must be keyed to that
            // overload, or the bare overloaded name matches no node and Leaf.Owner → Map(Tree) is never followed.
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Tree { public int V { get; set; } public List<Leaf> Leaves { get; set; } = new(); }
                               public class Leaf { public Tree? Owner { get; set; } }
                               public class LeafDto { public TreeDto? Owner { get; set; } }
                               public class TreeDto
                               {
                                   public TreeDto(int v, List<LeafDto> leaves) { V = v; Leaves = leaves; }
                                   public int V { get; }
                                   public List<LeafDto> Leaves { get; }
                               }
                               """ + Preserve + " public partial class M { public partial TreeDto Map(Tree t); public partial LeafDto Map(Leaf l); }";

            Assert.Equal(["leaves"], NamedParameters(src));
        }

        // ── Init-only and required members: filled in the object initializer, before the instance is registered ──

        [Fact]
        public void An_init_only_self_map_names_the_back_edge_not_the_scalar()
        {
            // Emitted as `new Node { V = n.V, Next = n.Next }`: the target's Next is the SOURCE node — the D2 record
            // shape through a property instead of a constructor parameter, and it was accepted without a word.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Node { public int V { get; init; } public Node? Next { get; init; } }
                               """ + Preserve + " public partial class M { public partial Node Map(Node n); }";

            Assert.Equal(["Next"], NamedParameters(src));
        }

        [Fact]
        public void Distinct_types_cycling_through_an_init_only_member_are_refused()
        {
            // Accepted before, and emitted `__dwarf_t.Next = …` after construction: CS8852 in the consumer's .g.cs.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int V { get; set; } public Src? Next { get; set; } }
                               public class Dst { public int V { get; init; } public Dst? Next { get; init; } }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            Assert.Equal(["Next"], NamedParameters(src));
        }

        [Fact]
        public void Distinct_types_cycling_through_a_required_list_are_refused()
        {
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int V { get; set; } public List<Src> Kids { get; set; } = new(); }
                               public class Dst { public int V { get; set; } public required List<Dst> Kids { get; set; } }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            Assert.Equal(["Kids"], NamedParameters(src));
        }

        [Fact]
        public void Distinct_types_cycling_through_a_required_field_are_refused()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int V { get; set; } public Src? Next { get; set; } }
                               public class Dst { public int V { get; set; } public required Dst? Next; }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            Assert.Equal(["Next"], NamedParameters(src));
        }

        [Fact]
        public void A_required_member_also_bound_by_the_constructor_is_named_once()
        {
            // Without [SetsRequiredMembers] the member is written twice — `new Dst(kids: …) { Kids = … }` — and must
            // not be reported twice under two spellings.
            const string src = """
                               using System.Collections.Generic;
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public List<Src> Kids { get; set; } = new(); }
                               public class Dst
                               {
                                   public Dst(List<Dst> kids) { Kids = kids; }
                                   public required List<Dst> Kids { get; init; }
                               }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            Assert.Equal(["kids"], NamedParameters(src));
        }

        [Fact]
        public void Acyclic_init_only_members_and_a_settable_cycle_beside_an_init_only_scalar_are_not_named()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr { public string City { get; set; } = ""; }
                               public class AddrDto { public string City { get; init; } = ""; }
                               public class Src { public int V { get; set; } public Addr A { get; set; } = new(); public Src? Next { get; set; } }
                               public class Dst { public int V { get; init; } public AddrDto A { get; init; } = new(); public Dst? Next { get; set; } }
                               """ + Preserve + " public partial class M { [MapValue(\"V\", 7)] public partial Dst Map(Src s); }";

            Assert.Empty(NamedParameters(src));
        }

        // ── Where the error is anchored ───────────────────────────────────────────────────────────────────────

        private static int AnchorLine(string src)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(src);
            var dwarf030 = Assert.Single(diagnostics, d => d.Id == "DWARF030");
            Assert.NotEqual(Location.None, dwarf030.Location);
            return dwarf030.Location.GetLineSpan().StartLinePosition.Line;
        }

        private static int LineOf(string src, string marker) =>
            Array.FindIndex(src.Split('\n'), l => l.Contains(marker, StringComparison.Ordinal));

        [Fact]
        public void The_error_is_anchored_at_the_declared_self_map()
        {
            var src = """
                      using DwarfMapper;
                      namespace Demo;
                      public record ImmutableNode(int V, ImmutableNode? Next);
                      """ + "\n" + Preserve + """

                                              public partial class M
                                              {
                                                  public partial string Other(int x);
                                                  public partial ImmutableNode Map(ImmutableNode n); // anchor
                                              }
                                              """;

            Assert.Equal(LineOf(src, "// anchor"), AnchorLine(src));
        }

        [Fact]
        public void A_nested_pair_error_is_anchored_at_the_declared_method_that_reached_it()
        {
            var src = """
                      using DwarfMapper;
                      namespace Demo;
                      public class Child { public int X { get; set; } public Child? Next { get; set; } }
                      public record ChildDto(int X, ChildDto? Next);
                      public class Src { public int Id { get; set; } public Child C { get; set; } = new(); }
                      public record Dst(int Id, ChildDto C);
                      public class Unrelated { public int A { get; set; } }
                      public class UnrelatedDto { public int A { get; set; } }
                      """ + "\n" + Preserve + """

                                              public partial class M
                                              {
                                                  public partial UnrelatedDto First(Unrelated u);
                                                  public partial Dst Map(Src s); // anchor
                                              }
                                              """;

            Assert.Equal(LineOf(src, "// anchor"), AnchorLine(src));
        }

        [Fact]
        public void A_cycle_reached_through_a_collection_helper_is_anchored_at_the_declared_method()
        {
            var src = """
                      using System.Collections.Generic;
                      using DwarfMapper;
                      namespace Demo;
                      public class Tree { public int V { get; set; } public List<Tree> Kids { get; set; } = new(); }
                      public class TreeDto
                      {
                          public TreeDto(int v, List<TreeDto> kids) { V = v; Kids = kids; }
                          public int V { get; }
                          public List<TreeDto> Kids { get; }
                      }
                      """ + "\n" + Preserve + """

                                              public partial class M
                                              {
                                                  public partial TreeDto Map(Tree t); // anchor
                                              }
                                              """;

            Assert.Equal(LineOf(src, "// anchor"), AnchorLine(src));
        }

        // ── The type walk behind an argument copied by reference in a self-map ──────────────────────────────

        private const string WalkSource = """
                                          using System;
                                          using System.Collections.Generic;
                                          namespace T
                                          {
                                              public enum Color { Red }
                                              public class Node { public int V { get; set; } }
                                              public class Wrapper { public Node Inner { get; set; } = new(); }
                                              public class Loop { public Loop? Next { get; set; } public string Name { get; set; } = ""; }
                                              public interface IHolder { Node N { get; } }
                                              public struct Pair { public int A { get; set; } public Wrapper W { get; set; } }
                                              public class Rec<T> { public Rec<Rec<T>>? X { get; set; } }
                                              public class InitBase { public int Inherited { get; init; } }
                                              public class InitShapes : InitBase
                                              {
                                                  public int GetOnly { get; }
                                                  public int Settable { get; set; }
                                                  public required int RequiredField;
                                                  public int PlainField;
                                                  public void Method() { }
                                              }
                                          }
                                          """;

        private static readonly Compilation WalkCompilation = CSharpCompilation.Create("CyclicConstructorParameterWalk",
            [CSharpSyntaxTree.ParseText(WalkSource)],
            AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        private static INamedTypeSymbol Named(string metadataName) =>
            WalkCompilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException(metadataName + " not found");

        private static INamedTypeSymbol Generic(string metadataName, params ITypeSymbol[] arguments) =>
            Named(metadataName).Construct(arguments);

        private static bool Reaches(ITypeSymbol memberType) =>
            MapperExtractor.SourceMemberReachesType(memberType, Named("T.Node"), WalkCompilation, allowNonPublic: false);

        [Fact]
        public void Walk_finds_the_source_type_itself_and_its_annotated_reference()
        {
            Assert.True(Reaches(Named("T.Node")));
            Assert.True(Reaches(Named("T.Node").WithNullableAnnotation(NullableAnnotation.Annotated)));
        }

        [Fact]
        public void Walk_follows_array_elements_generic_arguments_and_both_dictionary_halves()
        {
            var node = Named("T.Node");
            var str = WalkCompilation.GetSpecialType(SpecialType.System_String);
            var list = Generic("System.Collections.Generic.List`1", node);

            Assert.True(Reaches(WalkCompilation.CreateArrayTypeSymbol(node)));
            Assert.True(Reaches(Generic("System.Collections.Generic.List`1", list)));
            Assert.True(Reaches(Generic("System.Collections.Generic.Dictionary`2", node, str)));
            Assert.True(Reaches(Generic("System.Collections.Generic.Dictionary`2", str, node)));
            Assert.True(Reaches(Generic("System.Collections.Generic.IEnumerable`1", node)));
        }

        [Fact]
        public void Walk_descends_class_and_struct_members_declared_in_the_compilation()
        {
            Assert.True(Reaches(Named("T.Wrapper")));
            Assert.True(Reaches(Named("T.Pair")));
            Assert.True(Reaches(WalkCompilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(Named("T.Pair"))));
        }

        [Fact]
        public void Walk_stops_at_scalars_enums_delegates_interfaces_foreign_types_and_self_cycles()
        {
            Assert.False(Reaches(WalkCompilation.GetSpecialType(SpecialType.System_Int32)));
            Assert.False(Reaches(WalkCompilation.GetSpecialType(SpecialType.System_String)));
            Assert.False(Reaches(Named("T.Color")));
            Assert.False(Reaches(Generic("System.Func`1", Named("T.Node"))));
            Assert.False(Reaches(Named("T.IHolder")));
            Assert.False(Reaches(Named("System.Globalization.CultureInfo")));
            Assert.False(Reaches(Named("T.Loop")));
            Assert.False(Reaches(Generic("System.Collections.Generic.List`1", WalkCompilation.GetSpecialType(SpecialType.System_Int32))));
            Assert.False(Reaches(Named("System.Collections.Generic.List`1").TypeParameters[0]));
        }

        [Fact]
        public void Initializer_only_members_are_init_accessors_and_required_members_anywhere_in_the_hierarchy()
        {
            var shapes = Named("T.InitShapes");

            Assert.True(MapperExtractor.IsInitializerOnlyMember(shapes, "Inherited"));
            Assert.True(MapperExtractor.IsInitializerOnlyMember(shapes, "RequiredField"));
            Assert.False(MapperExtractor.IsInitializerOnlyMember(shapes, "GetOnly"));
            Assert.False(MapperExtractor.IsInitializerOnlyMember(shapes, "Settable"));
            Assert.False(MapperExtractor.IsInitializerOnlyMember(shapes, "PlainField"));
            Assert.False(MapperExtractor.IsInitializerOnlyMember(shapes, "Method"));
            Assert.False(MapperExtractor.IsInitializerOnlyMember(shapes, "Missing"));
        }

        [Fact]
        public async Task Walk_terminates_on_a_generic_type_that_grows_with_every_member_hop()
        {
            // Rec<int>.X is Rec<Rec<int>>, whose X is Rec<Rec<Rec<int>>> — a new constructed symbol per hop, so a
            // visited set alone never closes. The walk runs for every Preserve constructor argument, so this would hang
            // the generator (and the IDE hosting it) on any mapper with such an argument.
            var recOfInt = Generic("T.Rec`1", WalkCompilation.GetSpecialType(SpecialType.System_Int32));
            var walk = Task.Run(() => Reaches(recOfInt));

            var finished = await Task.WhenAny(walk, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(true);
            Assert.Same(walk, finished);
            Assert.False(await walk.ConfigureAwait(true));
        }
    }
}
