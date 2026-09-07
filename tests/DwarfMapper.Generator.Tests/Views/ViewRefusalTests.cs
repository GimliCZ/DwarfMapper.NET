// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

        /// <summary>
        ///     <c>Name</c> becomes the nested type's name, so a value that is not an identifier at all makes the
        ///     .g.cs fail to parse — CS1001 pointing at code the consumer cannot edit, for a typing mistake.
        ///     DWARF108 refuses it AT THE ARGUMENT, which is the only text that is wrong.
        /// </summary>
        [Theory]
        [InlineData("3 dogs", "a leading digit and a space")]
        [InlineData("Customer-Card", "punctuation")]
        [InlineData("", "the empty string, which used to mean 'no name given' and silently took the default")]
        [InlineData("   ", "whitespace, the same silence wearing a different coat")]
        public void A_view_name_that_is_not_an_identifier_is_refused_at_the_argument(string name, string why)
        {
            var source = $$"""
                           #nullable enable
                           using DwarfMapper;
                           namespace Demo;
                           public sealed class Src { public int Id { get; set; } }
                           public sealed class Dst { public int Id { get; set; } }
                           [DwarfMapper]
                           [GenerateView<Src, Dst>(Name = "{{name}}")]
                           public partial class M { }
                           """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            var reported = Assert.Single(diagnostics.Where(d => d.Id == "DWARF108"));
            Assert.Contains($"[GenerateView(Name = \"{name}\")] is not a C# identifier",
                reported.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

            // On the ARGUMENT, not on the attribute and not on the class: `[GenerateView<Src, Dst>(` is 24
            // characters, so the argument starts at column 25. Locating it on the class would point the
            // consumer at a line where nothing is wrong. (`why` names the shape for the failure output.)
            Assert.Equal(25, reported.Location.GetLineSpan().StartLinePosition.Character + 1);
            Assert.False(string.IsNullOrEmpty(why));

            Assert.DoesNotContain("readonly ref struct", generated, StringComparison.Ordinal);
            GeneratorAssert.EmitsCompilableCode(source, NullableContextOptions.Enable);
        }

        /// <summary>
        ///     A KEYWORD is not refused — it is escaped. <c>@</c> is C#'s own mechanism for using a keyword as
        ///     an identifier, so there is nothing here for a diagnostic to report and nothing for the consumer
        ///     to work around: the type's name is still <c>record</c>, and it is spelled <c>@record</c> in the
        ///     generated file.
        ///     <para>
        ///         <c>scoped</c> is in this list on purpose. Unescaped it is the one name no syntactic check
        ///         could catch — it parses into the same tree as a good name and fails later in the binder —
        ///         and escaped the ambiguity has nothing to bind. It is the case that proves escaping is the
        ///         right mechanism rather than a more careful refusal.
        ///     </para>
        /// </summary>
        [Theory]
        [InlineData("class")]    // reserved
        [InlineData("int")]      // reserved
        [InlineData("record")]   // contextual, and breaks unescaped — via the FACTORY's return type
        [InlineData("scoped")]   // contextual, breaks unescaped, invisible to any syntax check
        [InlineData("partial")]  // contextual, breaks unescaped
        [InlineData("var")]      // contextual, would have compiled anyway — escaped, still fine
        public void A_keyword_view_name_is_escaped_rather_than_refused(string name)
        {
            var source = $$"""
                           #nullable enable
                           using DwarfMapper;
                           namespace Demo;
                           public sealed class Src { public int Id { get; set; } }
                           public sealed class Dst { public int Id { get; set; } }
                           [DwarfMapper]
                           [GenerateView<Src, Dst>(Name = "{{name}}")]
                           public partial class M { }
                           """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            // Escaped in every position the name is written, the factory's return type included — that one is
            // what actually broke before, and a fix applied only to the declaration would pass a test that
            // looked at the declaration alone.
            Assert.Contains($"public readonly ref struct @{name}", generated, StringComparison.Ordinal);
            Assert.Contains($"internal @{name}(", generated, StringComparison.Ordinal);
            Assert.Contains($"public @{name} View(", generated, StringComparison.Ordinal);
            Assert.Contains($"=> new @{name}(", generated, StringComparison.Ordinal);
        }

        /// <summary>A name the consumer already escaped is left as they wrote it, not double-escaped.</summary>
        [Fact]
        public void A_name_the_consumer_escaped_themselves_is_not_escaped_twice()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>(Name = "@class")]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public readonly ref struct @class", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("@@", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A PascalCase name that merely resembles a keyword is untouched — no escape, no refusal. The
        ///     escape is case-sensitive because <c>SyntaxFacts</c> is.
        /// </summary>
        [Fact]
        public void A_name_that_only_resembles_a_keyword_is_left_alone()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>(Name = "Record")]
                                  public partial class M { }
                                  """;

            var generated = GeneratorAssert.CompilesClean(source, NullableContextOptions.Enable);

            Assert.Contains("public readonly ref struct Record", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("@Record", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The measurement the escape rests on, kept as a test rather than a note: EVERY keyword Roslyn
        ///     knows — reserved and contextual, 127 of them at the time of writing, taken from
        ///     <c>SyntaxFacts.GetKeywordKinds()</c> rather than a hand-picked list that would go stale — names
        ///     a view that compiles. Nothing is refused; nothing leaks a CS error into the generated file.
        /// </summary>
        [Fact]
        public void Every_keyword_roslyn_knows_names_a_view_that_compiles()
        {
            var keywords = SyntaxFacts.GetKeywordKinds().Select(SyntaxFacts.GetText)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(keywords.Count > 100, $"only {keywords.Count} keywords found — the sweep stopped sweeping");

            var leaks = new List<string>();
            foreach (var name in keywords)
            {
                var source = $$"""
                               #nullable enable
                               using DwarfMapper;
                               namespace Demo;
                               public sealed class Src { public int Id { get; set; } }
                               public sealed class Dst { public int Id { get; set; } }
                               [DwarfMapper]
                               [GenerateView<Src, Dst>(Name = "{{name}}")]
                               public partial class M { }
                               """;

                if (GeneratorTestHarness.Run(source, NullableContextOptions.Enable)
                    .Diagnostics.Any(d => d.Id == "DWARF108"))
                {
                    leaks.Add($"{name}: refused, but a keyword is escapable and should not be");
                    continue;
                }

                // EmitsCompilableCode rather than a direct harness call: it is the sanctioned helper and it
                // names the offending keyword in its own failure, so nothing is lost by stopping at the first.
                GeneratorAssert.EmitsCompilableCode(source, NullableContextOptions.Enable);
            }

            Assert.True(leaks.Count == 0, string.Join("\n", leaks));
        }

        /// <summary>
        ///     A name that IS an identifier but is already taken by a type the consumer declares on the same
        ///     mapper. CS0102 in the .g.cs otherwise — the same collision DWARF102 already refuses between two
        ///     views, applied to the type the consumer wrote, which is why it is DWARF102 and not a new id.
        /// </summary>
        [Fact]
        public void A_view_name_already_used_by_a_type_on_the_mapper_is_refused()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class Src { public int Id { get; set; } }
                                  public sealed class Dst { public int Id { get; set; } }
                                  [DwarfMapper]
                                  [GenerateView<Src, Dst>(Name = "Row")]
                                  public partial class M { public sealed class Row { } }
                                  """;

            var (diagnostics, generated) = GeneratorTestHarness.Run(source, NullableContextOptions.Enable);

            var reported = Assert.Single(diagnostics.Where(d => d.Id == "DWARF102"));
            Assert.Contains("already declares a type of that name",
                reported.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
            Assert.DoesNotContain("readonly ref struct", generated, StringComparison.Ordinal);
            // The point of the refusal: nothing the generator emitted carries a compiler error, so the ONLY
            // thing the consumer sees is the DWARF108 above, at the argument they typed.
            GeneratorAssert.EmitsCompilableCode(source, NullableContextOptions.Enable);
        }
    }
}
