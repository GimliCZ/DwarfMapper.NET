// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Views
{
    /// <summary>
    ///     <c>[GenerateView&lt;S,T&gt;]</c> emits a nested <c>public readonly ref struct</c> on the mapper class
    ///     whose properties evaluate the create map's member resolution lazily, against the source instance.
    ///     <para>
    ///         The <c>ref struct</c> is the CONTRACT: the compiler refuses to let the view be stored, boxed,
    ///         captured or held across an <c>await</c>, so it cannot outlive the source it borrows. Nothing here
    ///         asserts that (it is a compile-time property of the emitted type, exercised by
    ///         <c>ViewRefStructContractTests</c> in the compiler suite); these assert what the properties SAY.
    ///     </para>
    /// </summary>
    public class ViewEmissionTests
    {
        private const string Flat = """
                                    #nullable enable
                                    using DwarfMapper;
                                    namespace Demo;
                                    public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
                                    public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
                                    [DwarfMapper]
                                    [GenerateView<Src, Dst>]
                                    public partial class M { }
                                    """;

        [Fact]
        public void A_flat_view_emits_a_readonly_ref_struct_a_property_per_member_and_a_factory()
        {
            var generated = GeneratorAssert.CompilesClean(Flat, NullableContextOptions.Enable);

            Assert.Contains("public readonly ref struct DstView", generated, StringComparison.Ordinal);
            Assert.Contains("public int Id => _s.Id;", generated, StringComparison.Ordinal);
            Assert.Contains("public string? Name => _s.Name;", generated, StringComparison.Ordinal);
            Assert.Contains("public DstView View(global::Demo.Src source) => new DstView(source);",
                generated,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A view over identity members alone holds NOTHING but the source: no owner reference, because no
        ///     property names an instance member of the mapper. The field is emitted only where it is needed.
        /// </summary>
        [Fact]
        public void A_view_that_needs_no_mapper_member_holds_only_the_source()
        {
            var generated = GeneratorAssert.CompilesClean(Flat, NullableContextOptions.Enable);

            Assert.Contains("private readonly global::Demo.Src _s;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("private readonly M _m;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_view_type_name_is_the_declared_Name_when_one_is_given()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>(Name = "Row")]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public readonly ref struct Row", generated, StringComparison.Ordinal);
            Assert.Contains("public Row View(global::Demo.Src source)", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("ref struct DstView", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A scalar converter is the very same synthesized helper the create map calls, named unqualified —
        ///     which is only correct because those helpers are emitted <c>private static</c>, and a nested type
        ///     reaches its enclosing type's private statics. An instance one would be CS0120 (measured).
        /// </summary>
        [Fact]
        public void A_converted_member_calls_the_same_synthesized_helper_the_create_map_calls()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public enum Kind { A, B }
                                  public sealed class Src { public int Id { get; set; } public Kind K { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string K { get; set; } = ""; }
                                  [DwarfMapper]
                                  [GenerateMap<Src, Dst>]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public string K => __DwarfMap_EnumStr_", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("_m.__DwarfMap_EnumStr_", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A USER converter written as an instance method is reached through the owner — unqualified it is
        ///     CS0120 out of a file the consumer cannot edit, which is measured in <c>ViewProbe</c>'s negative
        ///     arm. The owner field appears exactly because this member needs it.
        /// </summary>
        [Fact]
        public void An_instance_user_converter_is_reached_through_the_mapper_and_pulls_in_the_owner_field()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public int Amount { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string Amount { get; set; } = ""; }
                                  [DwarfMapper]
                                  [MapProperty<Src, Dst>(nameof(Src.Amount), nameof(Dst.Amount), Use = nameof(Money))]
                                  [GenerateView<Src, Dst>]
                                  public partial class M
                                  {
                                      private string Money(int v) => v.ToString();
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("private readonly M _m;", generated, StringComparison.Ordinal);
            Assert.Contains("public string Amount => _m.Money(_s.Amount);", generated, StringComparison.Ordinal);
            Assert.Contains("new DstView(this, source)", generated, StringComparison.Ordinal);
        }

        /// <summary>A STATIC user converter is called unqualified — through the owner it would be CS0176.</summary>
        [Fact]
        public void A_static_user_converter_is_called_unqualified()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public int Amount { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string Amount { get; set; } = ""; }
                                  [DwarfMapper]
                                  [MapProperty<Src, Dst>(nameof(Src.Amount), nameof(Dst.Amount), Use = nameof(Money))]
                                  [GenerateView<Src, Dst>]
                                  public partial class M
                                  {
                                      private static string Money(int v) => v.ToString();
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public string Amount => Money(_s.Amount);", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("_m.Money", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nested_object_member_returns_a_nested_view_rather_than_calling_the_object_helper()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Inner { public string Label { get; set; } = ""; }
                                  public sealed class InnerDto { public string Label { get; set; } = ""; }
                                  public sealed class Src { public int Id { get; set; } public Inner Inner { get; set; } = new(); }
                                  public sealed class Dst { public int Id { get; set; } public InnerDto Inner { get; set; } = new(); }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public readonly ref struct InnerDtoView", generated, StringComparison.Ordinal);
            Assert.Contains("public InnerDtoView Inner => new InnerDtoView(_s.Inner);",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMap_Obj_", generated, StringComparison.Ordinal);

            // A nested view has no factory of its own: it is constructed by the view that holds it.
            Assert.DoesNotContain("InnerDtoView View(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nullable_nested_member_yields_the_default_view_whose_HasValue_is_false()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Inner { public string Label { get; set; } = ""; }
                                  public sealed class InnerDto { public string Label { get; set; } = ""; }
                                  public sealed class Src { public int Id { get; set; } public Inner? Inner { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public InnerDto? Inner { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("_s.Inner is null ? default : new InnerDtoView(_s.Inner)",
                generated,
                StringComparison.Ordinal);
            Assert.Contains("public bool HasValue => _s is not null;", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_MapProperty_rename_and_a_MapIgnore_reach_the_view_exactly_as_they_reach_the_create_map()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public string? Legacy { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string? Modern { get; set; } public string? Skipped { get; set; } }
                                  [DwarfMapper]
                                  [MapProperty<Src, Dst>(nameof(Src.Legacy), nameof(Dst.Modern))]
                                  [MapIgnore<Dst>(nameof(Dst.Skipped))]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public string? Modern => _s.Legacy;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("Skipped =>", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_MapValue_constant_and_a_NullSubstitute_are_expressions_and_so_reach_the_view()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
                                  [DwarfMapper]
                                  [MapValue<Dst>(nameof(Dst.Tag), "api-v2")]
                                  [MapProperty<Src, Dst>(nameof(Src.Name), nameof(Dst.Name), NullSubstitute = "(none)")]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public string Tag => \"api-v2\";", generated, StringComparison.Ordinal);
            Assert.Contains("public string Name => _s.Name ?? \"(none)\";", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     <c>When =</c> says "leave the destination's own value alone". A view has no destination instance,
        ///     so the destination's own value is <c>default</c> — the faithful expression form of the create
        ///     map's post-construction <c>if (P(s)) t.M = …;</c>.
        /// </summary>
        [Fact]
        public void A_When_predicate_becomes_a_ternary_whose_false_arm_is_the_destinations_own_default()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
                                  [DwarfMapper]
                                  [MapProperty<Src, Dst>(nameof(Src.Name), nameof(Dst.Name), When = nameof(HasName))]
                                  [GenerateView<Src, Dst>]
                                  public partial class M
                                  {
                                      private bool HasName(Src s) => s.Name is not null;
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public string? Name => _m.HasName(_s) ? _s.Name : default!;",
                generated,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A collection whose elements need NO conversion is handed back as it is — no copy, which is the
        ///     whole zero-copy claim, and a real difference from <c>Map</c>, which builds a new collection.
        /// </summary>
        [Fact]
        public void A_collection_whose_elements_need_no_conversion_is_handed_back_as_the_source_collection()
        {
            const string source = """
                                  #nullable enable
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public List<int> Tags { get; set; } = new(); }
                                  public sealed class Dst { public int Id { get; set; } public IReadOnlyList<int> Tags { get; set; } = new List<int>(); }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("Tags => _s.Tags;", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__DwarfMapColl_", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A view that reaches ITSELF is not refused, and that departs from the plan's text on purpose: a
        ///     <c>ref struct</c> whose PROPERTY returns its own type compiles and runs (measured), because the
        ///     property is an expression rather than a field. Walking a linked structure without materialising it
        ///     is the shape this feature is best at, and refusing it would refuse the best case.
        /// </summary>
        [Fact]
        public void A_view_that_reaches_itself_is_emitted_once_and_recurses_lazily()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Node { public string Name { get; set; } = ""; public Node? Next { get; set; } }
                                  public sealed class NodeDto { public string Name { get; set; } = ""; public NodeDto? Next { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Node, NodeDto>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Equal(1, CountOf(generated, "public readonly ref struct NodeDtoView"));
            Assert.Contains("public NodeDtoView Next => _s.Next is null ? default : new NodeDtoView(_s.Next);",
                generated,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     One nested pair reached from two views is emitted ONCE. Two view types of the same name in one
        ///     class is CS0102, out of a file the consumer cannot edit.
        /// </summary>
        [Fact]
        public void A_nested_pair_two_views_share_is_emitted_once()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Inner { public string Label { get; set; } = ""; }
                                  public sealed class InnerDto { public string Label { get; set; } = ""; }
                                  public sealed class SrcA { public Inner Inner { get; set; } = new(); }
                                  public sealed class SrcB { public Inner Inner { get; set; } = new(); }
                                  public sealed class DstA { public InnerDto Inner { get; set; } = new(); }
                                  public sealed class DstB { public InnerDto Inner { get; set; } = new(); }
                                  [DwarfMapper]
                                  [GenerateView<SrcA, DstA>]
                                  [GenerateView<SrcB, DstB>]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Equal(1, CountOf(generated, "public readonly ref struct InnerDtoView"));
        }

        /// <summary>
        ///     A nested view whose OWN members need the mapper, held by a parent whose members do not. The
        ///     parent must still pass the owner it does not otherwise need, or the nested constructor call is
        ///     CS7036 in a file the consumer cannot edit.
        /// </summary>
        [Fact]
        public void A_parent_that_needs_no_owner_still_passes_one_to_a_nested_view_that_does()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Inner { public int Amount { get; set; } }
                                  public sealed class InnerDto { public string Amount { get; set; } = ""; }
                                  public sealed class Src { public int Id { get; set; } public Inner Inner { get; set; } = new(); }
                                  public sealed class Dst { public int Id { get; set; } public InnerDto Inner { get; set; } = new(); }
                                  [DwarfMapper]
                                  [MapProperty<Inner, InnerDto>(nameof(Inner.Amount), nameof(InnerDto.Amount), Use = nameof(Money))]
                                  [GenerateView<Src, Dst>]
                                  public partial class M
                                  {
                                      private string Money(int v) => v.ToString();
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public InnerDtoView Inner => new InnerDtoView(_m, _s.Inner);",
                generated,
                StringComparison.Ordinal);
            Assert.Contains("private readonly M _m;", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The same hazard through a CYCLE: a self-referential view whose own members need the mapper
        ///     constructs itself, so the owner has to be threaded through the recursive call as well. This is
        ///     also what proves the fixed point terminates on a cyclic nesting graph.
        /// </summary>
        [Fact]
        public void A_cyclic_view_that_needs_the_owner_threads_it_through_its_own_recursion()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Node { public int Amount { get; set; } public Node? Next { get; set; } }
                                  public sealed class NodeDto { public string Amount { get; set; } = ""; public NodeDto? Next { get; set; } }
                                  [DwarfMapper]
                                  [MapProperty<Node, NodeDto>(nameof(Node.Amount), nameof(NodeDto.Amount), Use = nameof(Money))]
                                  [GenerateView<Node, NodeDto>]
                                  public partial class M
                                  {
                                      private string Money(int v) => v.ToString();
                                  }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("_s.Next is null ? default : new NodeDtoView(_m, _s.Next)",
                generated,
                StringComparison.Ordinal);
        }

        private static int CountOf(string haystack, string needle)
        {
            var count = 0;
            for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
                 i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                count++;

            return count;
        }
    }
}
