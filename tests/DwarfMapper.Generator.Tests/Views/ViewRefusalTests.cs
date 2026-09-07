// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Views
{
    /// <summary>
    ///     Every shape a view cannot be, refused by <c>DWARF102</c> — never dropped in silence.
    ///     <para>
    ///         The refusal is scoped to the view: it takes the view down and leaves the <c>Map</c> methods on the
    ///         same class standing, the boundary <c>DWARF028</c>/<c>DWARF096</c> established for a projection
    ///         member that cannot be translated. Each test below asserts BOTH halves, because a refusal that
    ///         quietly took the mapper with it would be a worse defect than the one it reports.
    ///     </para>
    /// </summary>
    public class ViewRefusalTests
    {
        /// <summary>
        ///     A view holds its source in a FIELD. For a value type that field is a copy, so the view would
        ///     neither be zero-copy nor read through to the source — both halves of what the contract promises.
        ///     Measured on a hand-written probe before this refusal was written: a copy-holding view returned the
        ///     pre-mutation value where a <c>ref readonly</c> one returned the post-mutation value.
        /// </summary>
        [Fact]
        public void A_value_type_source_is_refused_because_the_view_would_hold_a_copy()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public struct Src { public int Id { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("value-type source",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_collection_whose_elements_need_conversion_is_refused_and_the_map_beside_it_survives()
        {
            const string source = """
                                  #nullable enable
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public List<int> Tags { get; set; } = new(); }
                                  public sealed class Dst { public int Id { get; set; } public List<long> Tags { get; set; } = new(); }
                                  [DwarfMapper]
                                  [GenerateMap<Src, Dst>]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("would mean allocating",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

            // Scoped: the class still generates, and the view is simply absent.
            var (_, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
            Assert.Contains(" Map(global::Demo.Src ", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("ref struct", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Two views over one source type would both emit <c>View(TSource)</c>, differing only in return
        ///     type: CS0111, out of a file the consumer cannot edit. The same collision DWARF060 refuses for two
        ///     <c>[GenerateMap]</c> pairs that share a source.
        /// </summary>
        [Fact]
        public void Two_views_over_one_source_type_are_refused_before_they_can_collide_on_the_factory()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  public sealed class DstA { public int Id { get; set; } }
                                  public sealed class DstB { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, DstA>]
                                  [GenerateView<Src, DstB>]
                                  public partial class M { }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("CS0111",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

            // The FIRST view still lands; only the colliding second one is dropped.
            var (_, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
            Assert.Contains("ref struct DstAView", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("ref struct DstBView", generated, StringComparison.Ordinal);
        }

        /// <summary>Two views that would be given the same NAME are refused: two nested types of one name is CS0102.</summary>
        [Fact]
        public void Two_views_that_would_share_a_type_name_are_refused()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class SrcA { public int Id { get; set; } }
                                  public sealed class SrcB { public int Id { get; set; } }
                                  public sealed class DstA { public int Id { get; set; } }
                                  public sealed class DstB { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<SrcA, DstA>(Name = "Row")]
                                  [GenerateView<SrcB, DstB>(Name = "Row")]
                                  public partial class M { }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("would both be named 'Row'",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A co-located <c>[GenerateMap]</c> host's mapping is emitted into a BRAND-NEW <c>&lt;Host&gt;Mapper</c>
        ///     type. A view nested there would be a <c>ref struct</c> the consumer can reach by no name they
        ///     chose, so the endpoint is refused rather than half-served — the alternative being exactly the
        ///     "claimed an endpoint and said nothing there" silence this attribute's first arrival was rejected
        ///     for.
        /// </summary>
        [Fact]
        public void GenerateView_on_a_co_located_host_is_refused_rather_than_silently_ignored()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  [GenerateMap<Src, Dst>]
                                  [GenerateView<Src, Dst>]
                                  public sealed class Dst { public int Id { get; set; } }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("co-located [GenerateMap] host",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A collection, dictionary or scalar TARGET is a value the create map CONVERTS rather than an object
        ///     it builds member by member: there is nothing to expose as properties, so there is no view.
        /// </summary>
        [Fact]
        public void A_collection_or_scalar_target_has_no_members_to_view_and_is_refused()
        {
            const string source = """
                                  #nullable enable
                                  using System.Collections.Generic;
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, List<int>>]
                                  public partial class M { }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("collection, dictionary or scalar",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     An UNFLATTEN member's destination name is a PATH (<c>Address.City</c>), which the create map
        ///     assigns by building the intermediate first. A view builds nothing, and there is no property of
        ///     that name for it to declare.
        /// </summary>
        [Fact]
        public void An_unflatten_path_member_is_refused_because_a_view_declares_no_property_of_that_name()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Address { public string City { get; set; } = ""; }
                                  public sealed class Src { public int Id { get; set; } public string AddressCity { get; set; } = ""; }
                                  public sealed class Dst { public int Id { get; set; } public Address Address { get; set; } = new(); }
                                  [DwarfMapper]
                                  [MapProperty<Src, Dst>(nameof(Src.AddressCity), "Address.City")]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("UNFLATTEN",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A converter name declared BOTH statically and as an instance method has no single spelling a
        ///     nested type can use: unqualified is CS0120 for the instance one, owner-qualified is CS0176 for the
        ///     static one, and the view cannot tell which overload the mapping picked. Refused rather than
        ///     guessed.
        /// </summary>
        [Fact]
        public void A_converter_name_that_is_both_static_and_instance_is_refused_rather_than_guessed()
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
                                      private static string Money(long v) => v.ToString();
                                  }
                                  """;

            var reported = GeneratorAssert.Reports(source, "DWARF102", NullableContextOptions.Enable);
            Assert.Contains("BOTH as a static and as an instance method",
                reported[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     Completeness is NOT weakened by laziness: an unmapped destination member is DWARF001 for a view
        ///     exactly as it is for the create map.
        /// </summary>
        [Fact]
        public void An_unmapped_destination_member_is_DWARF001_for_a_view_too()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string Missing { get; set; } = ""; }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            GeneratorAssert.Reports(source, "DWARF001", NullableContextOptions.Enable);
        }

        /// <summary>
        ///     A pair declared as BOTH a map and a view resolves twice. Every hint that resolution raises must
        ///     still be reported ONCE — the reader is being told one fact about one pair, and saying it twice
        ///     because two attributes name the pair is noise the consumer cannot act on differently.
        /// </summary>
        [Fact]
        public void A_pair_that_is_both_mapped_and_viewed_reports_each_hint_once()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } public string Name { get; set; } = ""; }
                                  [DwarfMapper]
                                  [GenerateMap<Src, Dst>]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var (diagnostics, _) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);
            var byId = diagnostics.GroupBy(d => d.Id, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(byId.Count == 0,
                "A pair declared as both a map and a view reported these ids more than once: " + string.Join(", ", byId));
        }

        /// <summary>
        ///     A NESTED pair that is itself refused leaves the parent naming a view type nothing emits. DWARF102
        ///     is ScopedToMethod, so the mapper still emits — which only holds if what it emits COMPILES, and
        ///     `public InnerDtoView Inner => new InnerDtoView(_s.Inner)` beside no `InnerDtoView` is CS0246 in a
        ///     file the consumer cannot edit. The parent has to be withdrawn with the child.
        /// </summary>
        [Fact]
        public void A_view_whose_nested_view_was_refused_is_withdrawn_rather_than_left_dangling()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public struct DeepS { public int V { get; set; } }
                                  public sealed class DeepDto { public int V { get; set; } }
                                  public sealed class Inner { public DeepS Deep { get; set; } }
                                  public sealed class InnerDto { public DeepDto Deep { get; set; } = new(); }
                                  public sealed class Src { public int Id { get; set; } public Inner Inner { get; set; } = new(); }
                                  public sealed class Dst { public int Id { get; set; } public InnerDto Inner { get; set; } = new(); }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            Assert.Contains(diagnostics, d => d.Id == "DWARF102" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("SOURCE is a value type", StringComparison.Ordinal));
            Assert.Contains(diagnostics, d => d.Id == "DWARF102" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("the view 'DstView' cannot be emitted", StringComparison.Ordinal));
            Assert.DoesNotContain("InnerDtoView", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("ref struct DstView", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Two nested pairs with the SAME target collide on the view's name — a view is named after its
        ///     target type — so the second is refused and only the first is emitted. Constructing the survivor
        ///     with the other source's value is CS1503, which is why the withdrawal checks the nested view's
        ///     SOURCE type and not merely that something of that name exists.
        /// </summary>
        [Fact]
        public void Two_nested_pairs_sharing_a_target_withdraw_the_parent_rather_than_construct_the_wrong_view()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Home { public string A { get; set; } = ""; }
                                  public sealed class Office { public string A { get; set; } = ""; }
                                  public sealed class AddressDto { public string A { get; set; } = ""; }
                                  public sealed class Src { public Home H { get; set; } = new(); public Office W { get; set; } = new(); }
                                  public sealed class Dst { public AddressDto H { get; set; } = new(); public AddressDto W { get; set; } = new(); }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>]
                                  public partial class M { }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            // The collision message names both pairs, and its remedy no longer offers [GenerateView(Name = ...)]
            // as though a NESTED pair had an attribute to put it on.
            var collision = Assert.Single(diagnostics.Where(d => d.Id == "DWARF102" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("would both be named", StringComparison.Ordinal)));
            Assert.Contains("use Map for that pair", collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

            Assert.Contains(diagnostics, d => d.Id == "DWARF102" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("the view 'DstView' cannot be emitted", StringComparison.Ordinal));
            Assert.DoesNotContain("ref struct DstView", generated, StringComparison.Ordinal);
        }
    }
}
