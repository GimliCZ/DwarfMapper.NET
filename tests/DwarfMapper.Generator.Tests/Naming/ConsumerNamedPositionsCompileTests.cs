// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Naming
{
    /// <summary>
    ///     Every position where a CONSUMER chooses a name that the generator then writes into emitted code,
    ///     with that name being a C# keyword — which is legal, because the consumer writes <c>@class</c> and
    ///     <see cref="Microsoft.CodeAnalysis.ISymbol.Name" /> hands the generator back <c>class</c> with the
    ///     <c>@</c> stripped.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each case asserts the emitted code COMPILES, not that some string contains an <c>@</c>. Position
    ///         is the whole point and a substring assertion cannot see it: a name may be escaped at its
    ///         declaration and raw at its single use two hundred lines later, and only the compiler notices.
    ///         The <c>[GenerateView]</c> endpoint's own name broke in a factory return type while its struct
    ///         declaration parsed fine — that is the shape this file is built to catch.
    ///     </para>
    ///     <para>
    ///         The file is a restoration: it was written as a throwaway probe, measured 25 of 36 positions
    ///         RED against <c>462689b</c>, and was preserved under <c>.superpowers/sdd/…/task-1.y-…preserved</c>
    ///         so the <c>[GenerateView]</c> rollback could not delete the evidence. The two cases that named
    ///         that endpoint are gone: <c>P32</c> outright (it duplicated <c>P02</c>) and <c>P16</c> rewritten
    ///         onto the family-A position it now covers better — a mapper class named after a CONTEXTUAL
    ///         keyword, which is what <see cref="DwarfMapper.Generator.Core.Identifiers.EscapeTypeName" />
    ///         exists for.
    ///     </para>
    /// </remarks>
    public class ConsumerNamedPositionsCompileTests
    {
        /// <summary>
        ///     <see cref="GeneratorAssert.CompilesClean" /> plus "and it does not WARN either".
        /// </summary>
        /// <remarks>
        ///     The errors-only check is not sufficient for the type-declaration family, and that is measured
        ///     rather than assumed: a class the generator emits as <c>public partial class record</c> is
        ///     <c>CS8860</c> — a WARNING, which <c>CompilesClean</c> collects nothing of, and which is
        ///     unsuppressible from a consumer's <c>.g.cs</c> under the <c>TreatWarningsAsErrors</c> this repo
        ///     and many consumers set. An errors-only probe would have called the unescaped emission green.
        /// </remarks>
        private static void CompilesCleanAndWarningFree(string source)
        {
            GeneratorAssert.CompilesClean(source);

            var warnings = GeneratorTestHarness.GeneratedCodeWarnings(source, NullableContextOptions.Disable, includeRegistry: true);
            Assert.True(warnings.Length == 0,
                "The generator emitted code that WARNS (unsuppressible from a .g.cs):\n  " +
                string.Join("\n  ",
                    warnings.Select(d => d.Id + ": " + d.GetMessage(CultureInfo.InvariantCulture))) +
                "\n\n--- source ---\n" + source);
        }

        [Fact]
        public void P01_source_and_target_type_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class @class { public int Id { get; set; } }
                             public class @event { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<@class, @event>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P02_mapper_class_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class @class;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P03_namespace_segments()
        {
            const string s = """
                             using DwarfMapper;
                             namespace @class.@event;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P04_partial_map_method_name_and_parameter()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial Dst @class(Src @event);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P05_extra_parameter_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } public int Class { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial Dst Map(Src s, int @class);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P06_enum_member_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum E1 { @class, @event }
                             public enum E2 { @class, @event }
                             public class Src { public E1 K { get; set; } }
                             public class Dst { public E2 K { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P07_enum_to_string_member_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum E1 { @class, @event }
                             public class Src { public E1 K { get; set; } }
                             public class Dst { public string K { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P08_string_to_enum_member_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public enum E1 { @class, @event }
                             public class Src { public string K { get; set; } }
                             public class Dst { public E1 K { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P09_generic_type_parameter_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M<@class>
                             {
                                 public partial Dst Map(Src s);
                             }
                             """;
            GeneratorAssert.Reports(s, "DWARF054");
        }

        [Fact]
        public void P10_hook_method_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial Dst Map(Src s);
                                 [BeforeMap] private static void @class(Src s) { }
                                 [AfterMap] private static void @event(Src s, Dst d) { }
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P11_converter_method_name_via_Use()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public string Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             [MapProperty<Src, Dst>("Id", "Id", Use = "class")]
                             public partial class M
                             {
                                 private static string @class(int v) => v.ToString();
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P12_predicate_method_name_via_When()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             [MapProperty<Src, Dst>("Id", "Id", When = "class")]
                             public partial class M
                             {
                                 private static bool @class(Src s) => true;
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P13_user_conversion_method_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public string Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M
                             {
                                 private static string @class(int v) => v.ToString();
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P14_flatten_path_segments()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Inner { public int @class { get; set; } }
                             public class Src { public Inner @event { get; set; } }
                             public class Dst { public int Leaf { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             [MapProperty<Src, Dst>("event.class", "Leaf")]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P15_nested_type_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class @class { public class @event { public int Id { get; set; } } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<@class.@event, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A mapper class named after a CONTEXTUAL keyword — <c>@record</c>, which the consumer may
        ///     legally write and which <c>ISymbol.Name</c> returns as <c>record</c>.
        /// </summary>
        /// <remarks>
        ///     The type-DECLARATION position differs from every member position in the file, and
        ///     <c>record</c> is the sharpest witness: <c>public partial class record</c> declares perfectly
        ///     well, so a check that looked at the declaration alone would call it safe — it breaks where the
        ///     name is READ, and it breaks as a WARNING (CS8860) rather than an error. Hence
        ///     <see cref="CompilesCleanAndWarningFree" />, and hence
        ///     <see cref="DwarfMapper.Generator.Core.Identifiers.EscapeTypeName" /> rather than
        ///     <c>Identifiers.Escape</c>, which deliberately leaves contextual keywords alone.
        /// </remarks>
        [Fact]
        public void P16_mapper_class_named_after_a_contextual_keyword()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class @record;
                             """;
            CompilesCleanAndWarningFree(s);
        }

        [Fact]
        public void P17_field_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int @class; }
                             public class Dst { public int @class; }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P18_mapper_class_in_keyword_named_containing_type()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             public partial class @class
                             {
                                 [DwarfMapper]
                                 [GenerateMap<Src, Dst>]
                                 public partial class M;
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P19_constructor_parameter_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int @class { get; set; } }
                             public class Dst { public Dst(int @class) { Value = @class; } public int Value { get; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             [MapProperty<Src, Dst>("class", "class")]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P20_collection_element_type_names()
        {
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class @class { public int @event { get; set; } }
                             public class @struct { public int @event { get; set; } }
                             public class Src { public List<@class> Items { get; set; } }
                             public class Dst { public List<@struct> Items { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P21_update_into_target_parameter_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial void Map(Src @class, Dst @event);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P22_projection_method_name_and_parameter()
        {
            const string s = """
                             using System.Linq;
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial IQueryable<Dst> @class(IQueryable<Src> @event);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P23_map_to_registry_target_and_member_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class @event { public int @class { get; set; } }
                             [MapTo(typeof(@event))]
                             public class @struct { public int @class { get; set; } }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P24_co_located_host_class_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [GenerateMap<Src, Dst>]
                             public static partial class @class;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A <c>[MapDerivedType]</c> arm whose two types are keyword-named, and whose arm converter is a
        ///     keyword-named map method.
        /// </summary>
        /// <remarks>
        ///     The attribute sits on the METHOD. It was written on the class when this file was a probe, which
        ///     is <c>CS0592</c> in the consumer's own source — so the position was reported red without ever
        ///     having been driven. Repaired on restoration; the third corpus hole this round has found by
        ///     reading the failure instead of the count.
        /// </remarks>
        [Fact]
        public void P25_derived_type_arm_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             public class @class : Src { }
                             public class @event : Dst { }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [MapDerivedType(typeof(@class), typeof(@event))]
                                 public partial Dst Map(Src s);
                                 public partial @event @struct(@class c);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P26_reverse_map_method_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [ReverseMap] public partial Dst @class(Src s);
                                 public partial Src @event(Dst d);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P27_round_trip_pair_method_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [RoundTrip] public partial Dst @class(Src o);
                                 public partial Src @event(Dst d);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P28_convention_method_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int A { get; set; } }
                             public class Dst { public int A { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 private static void @class(MapConfig<Src, Dst> c) => c.Map(t => t.A, s => s.A);
                                 public partial Dst Map(Src s);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P29_hand_written_provides_map_method_name()
        {
            const string s = """
                             using System.Collections.Generic;
                             using System.Linq;
                             using DwarfMapper;
                             namespace Demo;
                             public class Item { public int V { get; set; } }
                             public class ItemDto { public int V { get; set; } }
                             public class Doc { public List<Item> Items { get; set; } = new(); }
                             [DwarfMapper]
                             [GenerateMap<Item, ItemDto>]
                             public partial class M
                             {
                                 [ProvidesMap]
                                 public ICollection<ItemDto> @class(Doc d) => d.Items.Select(Map).ToList();
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P30_span_map_parameter_names()
        {
            const string s = """
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             [DwarfMapper]
                             public partial class M { public partial void @struct(ReadOnlySpan<int> @class, Span<long> @event); }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P31_async_stream_method_and_parameter_names()
        {
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M { public partial IAsyncEnumerable<Dst> @struct(IAsyncEnumerable<Src> @class); }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        // P32 was "a view inside a keyword-named mapper". It named [GenerateView], which was withdrawn
        // 2026-09-07, and its intent — a mapper class whose own name is a keyword — is P02 exactly. Deleted
        // rather than ported: a duplicate case that passes tells you nothing the original did not.

        [Fact]
        public void P33_map_value_provider_method_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } public int Tag { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             [MapValue<Dst>("Tag", Use = "class")]
                             public partial class M
                             {
                                 private static int @class() => 7;
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A <c>[MapCollectionKey]</c> upsert whose collection and key members are both keyword-named — the
        ///     key is read off BOTH the existing element and the incoming one, so it is emitted three times.
        /// </summary>
        /// <remarks>
        ///     The attribute is method-only and update-into-only, and was written on the class as a probe:
        ///     <c>CS0592</c> in the consumer's own source, so the position was counted red without ever having
        ///     been reached. Repaired on restoration — see <see cref="P25_derived_type_arm_names" />.
        /// </remarks>
        [Fact]
        public void P34_collection_key_member_names()
        {
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Item { public int @class { get; set; } }
                             public class Src { public List<Item> @event { get; set; } = new(); }
                             public class Dst { public List<Item> @event { get; set; } = new(); }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [MapCollectionKey("event", "class")]
                                 public partial void Merge(Src src, Dst dst);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P35_map_to_registry_with_a_hook()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class @event { public int @class { get; set; } }
                             [MapTo(typeof(@event))]
                             public class @struct
                             {
                                 public int @class { get; set; }
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        [Fact]
        public void P36_wrapper_map_type_names()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class @class<T> { public T Value { get; set; } }
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             [GenerateWrapperMap(typeof(@class<>))]
                             public partial class M
                             {
                                 public partial Dst Map(Src s);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A <c>[FlattenGraph]</c> whose node LEAF and EDGE members are keyword-named.
        /// </summary>
        /// <remarks>
        ///     Not in the restored 36. It was found by the emission scan rather than by anyone enumerating
        ///     positions — <c>MapperExtractor.Flatten.Directive.cs</c> writes <c>edge.Name</c> raw into the
        ///     synthesized BFS helper — which is the argument for a scan over a population and against a list of
        ///     scenarios. The edge name is emitted in BOTH directions in the SAME statement: <c>__n.@event</c> is
        ///     a member access and needs the escape, while <c>__e_event</c> is a composed local and must not have
        ///     it, on adjacent lines.
        /// </remarks>
        [Fact]
        public void P37_flatten_graph_node_leaf_and_edge_names()
        {
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public class Node { public int @class { get; set; } public Node @event { get; set; } public List<Node> @struct { get; set; } = new(); }
                             public class NodeDto { public int @class { get; set; } public NodeDto @event { get; set; } }
                             public class Root { public Node @class { get; set; } }
                             public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [FlattenGraph("class", "Nodes")]
                                 public partial RootDto Map(Root r);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     The same, through the HETERO arm — a node hierarchy whose edge members are keyword-named.
        /// </summary>
        /// <remarks>
        ///     A second construction site of the same statement shape, in
        ///     <c>MapperExtractor.Flatten.Hetero.cs</c>. Probed separately rather than trusted to the
        ///     homogeneous case, because "a fix applied to one of N identical construction sites" is the hazard
        ///     an earlier audit in this project actually found.
        /// </remarks>
        [Fact]
        public void P38_flatten_graph_hetero_edge_names()
        {
            const string s = """
                             using System.Collections.Generic;
                             using DwarfMapper;
                             namespace Demo;
                             public abstract class Node { public int @class { get; set; } public Node @event { get; set; } }
                             public sealed class Leaf : Node { public string @struct { get; set; } = ""; }
                             public class NodeDto { public int @class { get; set; } }
                             public class LeafDto : NodeDto { public string @struct { get; set; } = ""; }
                             public class Root { public Node @class { get; set; } }
                             public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }
                             [DwarfMapper]
                             public partial class M
                             {
                                 [FlattenGraph("class", "Nodes")]
                                 [MapDerivedType(typeof(Leaf), typeof(LeafDto))]
                                 public partial RootDto Map(Root r);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A <c>[MapConstructor&lt;S,T&gt;]</c> factory method the consumer named after a keyword.
        /// </summary>
        /// <remarks>
        ///     The old investigation's attribute table listed this one as "not separately probed". It is the
        ///     position behind <c>MapMethodModel.EmitFactoryMethod</c>, and without a case here that escape
        ///     would be a fix with nothing holding it — the repo rule is a RED→GREEN test per fix, named for
        ///     the finding.
        /// </remarks>
        [Fact]
        public void P39_map_constructor_factory_method_name()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } public int Tag { get; set; } }
                             public class Dst { public Dst(int id) { Id = id; } public int Id { get; } public int Tag { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             [MapConstructor<Src, Dst>("class")]
                             public partial class M
                             {
                                 private static Dst @class(Src s) => new Dst(s.Id);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     An async-stream map whose user-declared <c>CancellationToken</c> parameter is keyword-named.
        /// </summary>
        /// <remarks>
        ///     P31 covers the method and source parameter of the same endpoint but declares NO token, so the
        ///     <c>AsyncCancellationParam</c> branch — the parameter that carries
        ///     <c>[EnumeratorCancellation]</c> and threads through <c>WithCancellation</c> — was never reached
        ///     by any position. The generated half must match the user's partial signature exactly, so this
        ///     name is theirs to choose.
        /// </remarks>
        [Fact]
        public void P40_async_stream_cancellation_token_parameter_name()
        {
            const string s = """
                             using System.Collections.Generic;
                             using System.Threading;
                             using DwarfMapper;
                             namespace Demo;
                             public class Src { public int Id { get; set; } }
                             public class Dst { public int Id { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial IAsyncEnumerable<Dst> Map(IAsyncEnumerable<Src> src, CancellationToken @class);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A <c>[Flags]</c> enum whose members are keyword-named, in all three directions.
        /// </summary>
        /// <remarks>
        ///     P06–P08 are all NON-flags: a flags enum takes three separate emission paths — the
        ///     <c>__r |= E.@class;</c> accumulator in enum→enum, the same in the comma-splitting string→enum
        ///     parser, and a string form that must stay UNESCAPED because <c>Enum.ToString</c> produces the
        ///     bare identifier. Three of the six escaped sites in <c>EnumConverter</c> are reachable only from
        ///     here, and the fourth is the literal that must NOT be escaped, on the same line as one that must.
        /// </remarks>
        [Fact]
        public void P41_flags_enum_member_names()
        {
            const string s = """
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             [Flags] public enum E1 { None = 0, @class = 1, @event = 2 }
                             [Flags] public enum E2 { None = 0, @class = 1, @event = 2 }
                             public class Src { public E1 A { get; set; } public E1 B { get; set; } public string C { get; set; } }
                             public class Dst { public E2 A { get; set; } public string B { get; set; } public E1 C { get; set; } }
                             [DwarfMapper]
                             [GenerateMap<Src, Dst>]
                             public partial class M;
                             """;
            GeneratorAssert.CompilesClean(s);
        }

        /// <summary>
        ///     A span map whose element converter is the consumer's own keyword-named map method.
        /// </summary>
        /// <remarks>
        ///     The async-stream loop wrote its element converter through <c>EmitConverterMethod</c>; the span loop
        ///     read the raw <c>ConverterMethod</c> through a null-conditional (<c>elem?.ConverterMethod</c>), which
        ///     is a member BINDING rather than a member access, so <c>EmittedIdentifiersAreEscapedTests</c> never
        ///     saw the site. Driven here, the call came out as <c>dst[__i] = class(src[__i]);</c>.
        /// </remarks>
        [Fact]
        public void P42_span_map_element_converter_method_name()
        {
            const string s = """
                             using System;
                             using DwarfMapper;
                             namespace Demo;
                             public struct P { public int X; }
                             public struct Q { public long X; }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial void Map(ReadOnlySpan<P> src, Span<Q> dst);
                                 public partial Q @class(P p);
                             }
                             """;
            GeneratorAssert.CompilesClean(s);
        }
    }
}
