// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Which endpoints <c>[Flatten]</c> and <c>[MapValue]</c> reach, and what they say where they cannot.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both directives were measured SILENT at projection and at the two element-wise endpoints — accepted,
    ///         no diagnostic, output byte-identical to the same mapper without them (recorded as <c>D9</c> and
    ///         <c>D10</c>). The two have different answers and the difference is not a preference: a projection
    ///         becomes an expression tree, and <c>__s.Child.X</c> is the navigation access every query provider
    ///         translates, so a flatten there is HONOURED; an element-wise map resolves no members of its own and
    ///         takes its configuration from pair-scoped directives only, so a method-scoped directive there is
    ///         REFUSED, with the pair-scoped form that does work named in the message.
    ///     </para>
    ///     <para>
    ///         The remedies below are pinned because they were measured before they were prescribed. This
    ///         repository has already shipped a <c>DWARF090</c> tail asserting an endpoint behaviour that was
    ///         false, and reverted it; a diagnostic that sends the reader to a form nobody ran is the same defect.
    ///     </para>
    /// </remarks>
    public class FlattenAndMapValueReachTests
    {
        private const string Types = """
                                     using System.Linq;
                                     using DwarfMapper;
                                     namespace Demo;
                                     public struct Inner { public int X { get; set; } }
                                     public class Src { public int Id { get; set; } public Inner Child { get; set; } }
                                     public class Dst { public int Id { get; set; } public int X { get; set; } }
                                     """;

        private const string Flat = """
                                    using System.Linq;
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class Src { public int Id { get; set; } public string Name { get; set; } }
                                    public class Dst { public int Id { get; set; } public string Name { get; set; } }
                                    """;

        // ── Projection now resolves the directive ────────────────────────────────

        [Fact]
        public void Projection_pulls_a_flattened_leaf_up_exactly_as_the_create_map_does()
        {
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper]
                                                                public partial class M
                                                                {
                                                                    [Flatten("Child")]
                                                                    public partial Dst Map(Src s);

                                                                    [Flatten("Child")]
                                                                    public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                                                }
                                                                """);

            // The runtime map has always done this; the projection did nothing at all. The create-map assertion
            // carries its "= " prefix deliberately: bare "s.Child.X" is a SUBSTRING of "__s.Child.X", so without
            // it the first check would pass on the projection's own output and pin nothing.
            Assert.Contains("= s.Child.X", generated, StringComparison.Ordinal);
            Assert.Contains("__s.Child.X", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_projection_flatten_root_that_names_nothing_is_refused_by_the_create_maps_own_guard()
        {
            // DWARF016 is not a new check written for this endpoint — ResolveFlattenInfos is the one walk both
            // resolvers call, so a root the runtime path refuses cannot be quietly accepted by the other.
            GeneratorAssert.Reports(Types +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [Flatten("NoSuchMember")]
                                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                    }
                                    """,
                "DWARF016");
        }

        [Fact]
        public void A_projection_flatten_root_that_names_a_scalar_is_refused_too()
        {
            GeneratorAssert.Reports(Types +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [Flatten("Id")]
                                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                    }
                                    """,
                "DWARF016");
        }

        [Fact]
        public void Two_flatten_roots_supplying_one_destination_member_are_ambiguous_at_projection_as_well()
        {
            GeneratorAssert.Reports("""
                                    using System.Linq;
                                    using DwarfMapper;
                                    namespace Demo;
                                    public struct Inner { public int X { get; set; } }
                                    public struct Other { public int X { get; set; } }
                                    public class Src { public int Id { get; set; } public Inner A { get; set; } public Other B { get; set; } }
                                    public class Dst { public int Id { get; set; } public int X { get; set; } }

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [Flatten("A")]
                                        [Flatten("B")]
                                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                    }
                                    """,
                "DWARF017");
        }

        [Fact]
        public void A_nullable_flatten_root_warns_at_the_create_map_and_deliberately_not_at_projection()
        {
            // DWARF044 says a null interior throws when dereferenced. True of emitted C#; false of a translated
            // path, which the provider turns into a join yielding null. The dotted [MapProperty] source already
            // makes that call at this endpoint, and ResolveFlattenInfos takes it as a parameter so the two cannot
            // drift apart. Asserted in BOTH directions: a check that only proved the silence would pass equally
            // if the whole flatten had stopped resolving.
            const string nullableRoot = """
                                        using System.Linq;
                                        using DwarfMapper;
                                        namespace Demo;
                                        public class Inner { public int X { get; set; } }
                                        public class Src { public int Id { get; set; } public Inner? Child { get; set; } }
                                        public class Dst { public int Id { get; set; } public int X { get; set; } }
                                        """;

            GeneratorAssert.Reports(nullableRoot +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [Flatten("Child")]
                                        public partial Dst Map(Src s);
                                    }
                                    """,
                "DWARF044",
                NullableContextOptions.Enable);

            GeneratorAssert.DoesNotReport(nullableRoot +
                                          """

                                          [DwarfMapper]
                                          public partial class M
                                          {
                                              [Flatten("Child")]
                                              public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                          }
                                          """,
                "DWARF044",
                NullableContextOptions.Enable);
        }

        // ── The element-wise endpoints refuse, and name a remedy that works ──────

        [Fact]
        public void A_method_scoped_Flatten_is_refused_element_wise_and_the_dotted_remedy_is_the_one_that_works()
        {
            var diagnostic = GeneratorAssert.Reports(Types +
                                                     """

                                                     [DwarfMapper]
                                                     public partial class M
                                                     {
                                                         [Flatten("Child")]
                                                         public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                                     }
                                                     """,
                "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

            Assert.Contains("[Flatten(\"Child\")] on this mapping method", diagnostic, StringComparison.Ordinal);
            Assert.Contains("[MapProperty<Src, Dst>(\"Child.<leaf>\", \"<leaf>\")]",
                diagnostic,
                StringComparison.Ordinal);

            // And the prescribed form is not merely plausible: written pair-scoped it maps the element pair.
            var generated = GeneratorAssert.EmitsCompilableCode(Types +
                                                                """

                                                                [DwarfMapper]
                                                                [MapProperty<Src, Dst>("Child.X", "X")]
                                                                public partial class M
                                                                {
                                                                    public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                                                }
                                                                """);
            Assert.Contains("Child.X", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_method_scoped_MapValue_is_refused_element_wise_and_the_pair_scoped_remedy_assigns_the_constant()
        {
            var diagnostic = GeneratorAssert.Reports(Flat +
                                                     """

                                                     [DwarfMapper]
                                                     public partial class M
                                                     {
                                                         [MapValue("Name", "probe")]
                                                         public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                                     }
                                                     """,
                "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

            Assert.Contains("[MapValue(\"Name\", \"probe\")] on this mapping method",
                diagnostic,
                StringComparison.Ordinal);
            Assert.Contains("[MapValue<Dst>(\"Name\", \"probe\")]", diagnostic, StringComparison.Ordinal);

            var generated = GeneratorAssert.EmitsCompilableCode(Flat +
                                                                """

                                                                [DwarfMapper]
                                                                [MapValue<Dst>("Name", "probe")]
                                                                public partial class M
                                                                {
                                                                    public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                                                }
                                                                """);
            Assert.Contains("Name = \"probe\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_element_wise_MapValue_message_echoes_what_was_written_and_never_the_wrong_endpoint()
        {
            // Three written forms, three quoted remedies. A message that rendered one shape for all three would
            // hand a caller who wrote Use= a constant they never asked for.
            foreach (var (written, expected) in new[]
                     {
                         ("[MapValue(\"Name\")]", "[MapValue<Dst>(\"Name\")]"), ("[MapValue(\"Name\", Use = \"Now\")]", "[MapValue<Dst>(\"Name\", Use = \"Now\")]"), ("[MapValue(\"Name\", \"probe\")]", "[MapValue<Dst>(\"Name\", \"probe\")]")
                     })
            {
                var diagnostic = GeneratorAssert.Reports(Flat +
                                                         $$"""

                                                           [DwarfMapper]
                                                           public partial class M
                                                           {
                                                               {{written}}
                                                               public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                                           }
                                                           """,
                    "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

                Assert.Contains(written + " on this mapping method", diagnostic, StringComparison.Ordinal);
                Assert.Contains(expected, diagnostic, StringComparison.Ordinal);

                // Projection now REACHES this directive (D9 closed): a constant becomes a literal in the SELECT,
                // and only the Use= form is refused there. The one thing this tail must never do is misstate an
                // endpoint, which it has done in both directions on this branch — so the pin runs both ways, and
                // the stale "silent at projection" sentence is asserted absent rather than merely not asserted.
                Assert.Contains("create-map, update-into and projection endpoints",
                    diagnostic,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("silent at projection", diagnostic, StringComparison.Ordinal);
            }
        }

        // ── Malformed input reaches neither the model nor a message ──────────────

        [Fact]
        public void A_null_directive_argument_produces_no_element_wise_report_and_no_crash()
        {
            // ReadFlattenRoots and ReadMapValues each drop an argument that is absent or not a string, and the
            // gate calls those readers rather than re-parsing — so a directive resolution never saw cannot be
            // reported as one resolution dropped. The alternative is a message with the word "null" in it.
            const string source = """

                                  [DwarfMapper]
                                  public partial class M
                                  {
                                      [Flatten(null)]
                                      [MapValue(null)]
                                      public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                  }
                                  """;

            GeneratorAssert.DoesNotReport(Flat + source, "DWARF090");
            GeneratorAssert.CompilesClean(Flat + source);
        }

        [Theory]
        [InlineData("new[] { 1 }")]
        [InlineData("typeof(Src)")]
        public void A_constant_this_library_cannot_render_falls_back_to_the_bare_form_and_never_to_null(string arg)
        {
            // TypedConstant.Value THROWS for an array kind and answers an ITypeSymbol for a typeof — so the
            // element-wise arm's own rule ("echo what was written") had two ways to break it: crash the
            // generator, or silently print `null` beside a target the caller never wrote null for. Neither is a
            // rendering; both are the arm quoting something other than the source. The bare form is honest —
            // the target is still named, and the constant is left out rather than invented.
            var diagnostic = GeneratorAssert.Reports(Flat +
                                                     $$"""

                                                       [DwarfMapper]
                                                       public partial class M
                                                       {
                                                           [MapValue("Name", {{arg}})]
                                                           public partial void MapSpan(System.ReadOnlySpan<Src> s, System.Span<Dst> d);
                                                       }
                                                       """,
                "DWARF090")[0].GetMessage(CultureInfo.InvariantCulture);

            Assert.Contains("[MapValue(\"Name\")] on this mapping method", diagnostic, StringComparison.Ordinal);
            Assert.Contains("[MapValue<Dst>(\"Name\")]", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("null", diagnostic, StringComparison.Ordinal);
        }

        [Fact]
        public void Both_prescribed_remedies_are_pinned_at_AsyncStream_and_not_only_at_SpanMap()
        {
            // The remedies are named at BOTH element-wise endpoints, so measuring one and asserting the other is
            // the assumption D6/D7 exist to forbid: [MapNullSkip]'s two forms reached exactly complementary
            // halves of this surface, and nothing about "it works over a span" carries to an async stream on its
            // own. The surface matrix cannot pin the [MapValue] one here either — its generic form's probe
            // arguments are the nonsense shape recorded as B25 — so a unit test is the only instrument there is.
            var withValue = GeneratorAssert.EmitsCompilableCode(Flat +
                                                                """

                                                                [DwarfMapper]
                                                                [MapValue<Dst>("Name", "probe")]
                                                                public partial class M
                                                                {
                                                                    public partial System.Collections.Generic.IAsyncEnumerable<Dst> MapStream(
                                                                        System.Collections.Generic.IAsyncEnumerable<Src> s);
                                                                }
                                                                """);
            Assert.Contains("Name = \"probe\"", withValue, StringComparison.Ordinal);

            var withFlatten = GeneratorAssert.EmitsCompilableCode(Types +
                                                                  """

                                                                  [DwarfMapper]
                                                                  [MapProperty<Src, Dst>("Child.X", "X")]
                                                                  public partial class M
                                                                  {
                                                                      public partial System.Collections.Generic.IAsyncEnumerable<Dst> MapStream(
                                                                          System.Collections.Generic.IAsyncEnumerable<Src> s);
                                                                  }
                                                                  """);
            Assert.Contains("Child.X", withFlatten, StringComparison.Ordinal);
        }

        [Fact]
        public void A_MapValue_whose_constant_does_not_fit_the_destination_is_still_refused_at_the_create_map()
        {
            // The type check is DWARF040 and it lives on the create-map path, which is where the [MapValue] that
            // reaches an endpoint is resolved. Pinned here because A8 added a second reader of these attributes
            // (the element-wise gate) and a guard that moved rather than being shared is this round's recurring
            // defect.
            GeneratorAssert.Reports(Flat +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [MapValue("Id", "not-an-int")]
                                        public partial Dst Map(Src s);
                                    }
                                    """,
                "DWARF040");
        }

        // ── [MapValue] at the projection endpoint (D9) ──────────────────────────

        [Fact]
        public void A_MapValue_constant_is_assigned_by_the_projection_as_well_as_by_the_create_map()
        {
            // The whole of D9: the same mapper assigned the constant through .Map and did not through .Project,
            // with nothing in the build saying so. BOTH halves are asserted, and the create-map half is not
            // ceremony — a "fix" that moved the resolution rather than sharing it would pass an assertion that
            // only read the projection.
            var generated = GeneratorAssert.EmitsCompilableCode(Flat +
                                                                """

                                                                [DwarfMapper]
                                                                public partial class M
                                                                {
                                                                    [MapValue("Name", "probe")]
                                                                    public partial Dst Map(Src s);

                                                                    [MapValue("Name", "probe")]
                                                                    public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                                                }
                                                                """);

            Assert.Contains("Name = \"probe\"", generated, StringComparison.Ordinal);
            Assert.Contains("__s", generated, StringComparison.Ordinal);
            // The projected member must carry the CONSTANT, not the source member it shadows.
            Assert.DoesNotContain("Name = __s.Name", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_MapValue_value_provider_is_refused_at_the_projection_and_honoured_at_the_create_map()
        {
            // Use= is the one part of the directive a query provider cannot take: it would have to call back into
            // managed code from inside an expression tree. Refused as DWARF028, which is what [MapProperty(Use=)]
            // already gets at this endpoint — one story for the caller, not two.
            var reported = GeneratorAssert.Reports(Flat +
                                                   """

                                                   [DwarfMapper]
                                                   public partial class M
                                                   {
                                                       [MapValue("Name", Use = nameof(Probe))]
                                                       public partial IQueryable<Dst> Project(IQueryable<Src> q);

                                                       private static string Probe() => "probe";
                                                   }
                                                   """,
                "DWARF028");

            Assert.Contains(reported,
                d => d.GetMessage(CultureInfo.InvariantCulture)
                    .Contains("[MapValue(Use = ...)]", StringComparison.Ordinal));

            // And the create map, which CAN call it, still does. The refusal above must be the endpoint's answer,
            // not the directive being broken for everyone.
            var generated = GeneratorAssert.EmitsCompilableCode(Flat +
                                                                """

                                                                [DwarfMapper]
                                                                public partial class M
                                                                {
                                                                    [MapValue("Name", Use = nameof(Probe))]
                                                                    public partial Dst Map(Src s);

                                                                    private static string Probe() => "probe";
                                                                }
                                                                """);
            Assert.Contains("Name = Probe()", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void A_malformed_MapValue_is_refused_at_the_projection_by_the_create_maps_own_guard()
        {
            // DWARF042 ("neither a constant value nor Use=") and DWARF040 (the constant does not fit) are the
            // create map's guards, and the projection reaches them through TryValidateMapValueTarget rather than
            // through a copy. Both endpoints asserted in one source so a guard that stopped being shared shows up
            // as a missing id rather than as a passing test somewhere else.
            GeneratorAssert.Reports(Flat +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [MapValue("Name")]
                                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                    }
                                    """,
                "DWARF042");

            GeneratorAssert.Reports(Flat +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [MapValue("Id", "not-an-int")]
                                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                    }
                                    """,
                "DWARF040");

            // The shadow report is the create map's too, and it is a REPORT rather than a refusal — the constant
            // is still assigned. Both facts, because the hoisted validation returns true on this path.
            GeneratorAssert.Reports(Flat +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [MapValue("Name", "probe")]
                                        public partial IQueryable<Dst> Project(IQueryable<Src> q);
                                    }
                                    """,
                "DWARF064");
        }

        [Fact]
        public void A_flatten_root_naming_a_member_that_does_not_exist_is_refused_at_the_create_map_too()
        {
            GeneratorAssert.Reports(Types +
                                    """

                                    [DwarfMapper]
                                    public partial class M
                                    {
                                        [Flatten("NoSuchMember")]
                                        public partial Dst Map(Src s);
                                    }
                                    """,
                "DWARF016");
        }
    }
}
