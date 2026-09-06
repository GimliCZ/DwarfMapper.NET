// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <c>DWARF103</c> — the mapping-site voice for <c>TransferModelShape.Classify</c> (round 29, T2.2).
    ///     A collection of class elements maps to a collection of class elements, the destination element is
    ///     transfer-model shaped, and nothing in the build says that declaring it a
    ///     <c>readonly record struct</c> turns one allocation per element into one for the whole collection.
    ///     <para>
    ///         <b>More than half of these tests assert SILENCE, and they are the half that keeps the
    ///         diagnostic alive.</b> An Info that fires on ordinary code is suppressed wholesale by the first
    ///         consumer who meets it, taking the useful cases with it — the lesson
    ///         <c>BlittableProof.TryExplainNearMiss</c> records for <c>DWARF100</c> and
    ///         <c>LayoutHygiene.WastesAQuarter</c> for <c>DWARF101</c>. Every silence below is a shape where
    ///         the suggestion would be wrong rather than merely unwelcome: the elements are not built by this
    ///         generator, the pair carries a hook a struct target would silently drop, or reference identity
    ///         is the point of the mapping.
    ///     </para>
    ///     <para>
    ///         Messages are asserted WHOLE. The wording is pinned in <c>docs/diagnostics.md</c> and in the
    ///         NegativeCases rows, and a substring assertion could not have caught an estimate printed as a
    ///         fact — which is the one defect <c>Verdict.SizeIsUpperBound</c> exists to prevent.
    ///     </para>
    /// </summary>
    public class TransferModelDiagnosticTests
    {
        /// <summary>The headline shape: a member-level collection whose element pair is auto-nested.</summary>
        private const string MemberPair = """
                                          using DwarfMapper;
                                          using System.Collections.Generic;
                                          namespace Demo;
                                          public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                          public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                          public class C { public List<Order> Rows { get; set; } }
                                          public class D { public List<OrderDto> Rows { get; set; } }
                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                          """;

        /// <summary>
        ///     The message the fixtures around it produce, for a 16-byte sealed target whose SOURCE is itself
        ///     transfer-model shaped and holds no reference — the one shape in which the block-copy clause is
        ///     both earned and possible.
        /// </summary>
        private const string OrderDtoMessage =
            "'Demo.Order' → 'Demo.OrderDto' allocates one 'Demo.OrderDto' per element, and 'Demo.OrderDto' is " +
            "transfer-model shaped: declared as a readonly record struct — with any transfer model it holds a " +
            "struct too — it is 16 bytes, and the collection becomes one allocation instead of one per element. " +
            "'Demo.Order' is transfer-model shaped too, and neither type holds a reference: as structs with " +
            "identical layout and matching field names the pair could take the block copy instead of the " +
            "element loop.";

        private static List<Diagnostic> Run(string source)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source);

            return diagnostics.Where(d => d.Id == "DWARF103").ToList();
        }

        private static string Message(Diagnostic diagnostic)
        {
            return diagnostic.GetMessage(CultureInfo.InvariantCulture);
        }

        // ─── The reporting cases ─────────────────────────────────────────────────

        /// <summary>
        ///     A member-level <c>List&lt;Order&gt; → List&lt;OrderDto&gt;</c>, whose element pair the generator
        ///     answers with a synthesized object map: it allocates one <c>OrderDto</c> per element, and that is
        ///     the cost the hint is about.
        /// </summary>
        [Fact]
        public void A_collection_of_class_elements_is_told_its_element_could_be_a_struct()
        {
            var one = Assert.Single(Run(MemberPair));

            Assert.Equal(DiagnosticSeverity.Info, one.Severity);
            Assert.Equal(OrderDtoMessage, Message(one));
        }

        /// <summary>
        ///     The shape the research measured: <c>partial List&lt;OrderDto&gt; Map(List&lt;Order&gt;)</c> beside
        ///     <c>partial OrderDto Map(Order)</c>. The element pair resolves to the SIBLING PARTIAL METHOD rather
        ///     than to a synthesized helper, and the gate has to count that as "this generator builds the
        ///     element" — the generator writes that method's body. Gating on "no user-declared conversion", the
        ///     obvious rule, silences exactly this case, which is the feature's headline.
        /// </summary>
        [Fact]
        public void A_top_level_collection_method_whose_element_is_a_sibling_partial_method_reports()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                        public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                        [DwarfMapper]
                                        public partial class M
                                        {
                                            public partial OrderDto Map(Order o);
                                            public partial List<OrderDto> MapAll(List<Order> o);
                                        }
                                        """));

            Assert.Equal(OrderDtoMessage, Message(one));
        }

        /// <summary>
        ///     A <c>[GenerateMap&lt;S,T&gt;]</c> pair reached through a collection is the same case by another
        ///     door: the pair contributes a candidate named <c>Map</c> that this generator emits.
        /// </summary>
        [Fact]
        public void A_declared_pair_reached_through_a_collection_reports()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                        public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper]
                                        [GenerateMap<Order, OrderDto>]
                                        public partial class M { public partial D Map(C c); }
                                        """));

            Assert.Equal(OrderDtoMessage, Message(one));
        }

        /// <summary>
        ///     A reference member is costed at 8 bytes — its x64 width, 4 on a 32-bit runtime — so the size is
        ///     an OVER-estimate and the message must say "at most". Printing an estimate as a measurement is the
        ///     defect <c>Verdict.SizeIsUpperBound</c> exists to prevent, and it is asserted here rather than
        ///     trusted.
        /// </summary>
        [Fact]
        public void A_reference_member_makes_the_size_an_upper_bound_and_the_message_says_so()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Person { public long Id { get; set; } public string Name { get; set; } }
                                        public sealed class PersonDto { public long Id { get; set; } public string Name { get; set; } }
                                        public class C { public List<Person> Rows { get; set; } }
                                        public class D { public List<PersonDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.Equal(
                "'Demo.Person' → 'Demo.PersonDto' allocates one 'Demo.PersonDto' per element, and " +
                "'Demo.PersonDto' is transfer-model shaped: declared as a readonly record struct — with any " +
                "transfer model it holds a struct too — it is at most 16 bytes, a reference member counted at " +
                "8 bytes, its x64 width, and the collection becomes one allocation instead of one per element.",
                Message(one));

            // Round 29 T2.2 review, critical 1. The block copy is not merely unproven for this pair, it is
            // IMPOSSIBLE: BlittableProof requires both element types to be unmanaged, and a type carrying a
            // string never is — which is the same fact that set SizeIsUpperBound two clauses earlier. A message
            // that hedges its byte count and then states the block copy as fact contradicts itself.
            Assert.DoesNotContain("block copy", Message(one), StringComparison.Ordinal);
        }

        /// <summary>
        ///     Between 32 and 64 bytes the struct is still worth having and is too big to copy by value at every
        ///     call, so the message adds the <c>in</c> advice. Five <c>long</c>s is 40 bytes.
        /// </summary>
        [Fact]
        public void A_target_over_the_value_copy_threshold_is_told_to_pass_it_by_in()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Wide { public long A { get; set; } public long B { get; set; } public long C1 { get; set; } public long D1 { get; set; } public long E { get; set; } }
                                        public sealed class WideDto { public long A { get; set; } public long B { get; set; } public long C1 { get; set; } public long D1 { get; set; } public long E { get; set; } }
                                        public class C { public List<Wide> Rows { get; set; } }
                                        public class D { public List<WideDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.EndsWith(
                " At 40 bytes it is over the 32-byte threshold for copying by value, so pass it by 'in'.",
                Message(one),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     Past 64 bytes the classifier answers <c>TooLarge</c> and clears <c>SuggestIn</c> — the flag is a
        ///     BAND, not a ceiling — so a report that read <c>SuggestIn</c> alone would drop the advice for
        ///     exactly the types that need it most. Nine <c>long</c>s is 72 bytes.
        /// </summary>
        [Fact]
        public void A_target_past_the_upper_threshold_still_gets_the_in_advice()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Big { public long A { get; set; } public long B { get; set; } public long C1 { get; set; } public long D1 { get; set; } public long E { get; set; } public long F { get; set; } public long G { get; set; } public long H { get; set; } public long I { get; set; } }
                                        public sealed class BigDto { public long A { get; set; } public long B { get; set; } public long C1 { get; set; } public long D1 { get; set; } public long E { get; set; } public long F { get; set; } public long G { get; set; } public long H { get; set; } public long I { get; set; } }
                                        public class C { public List<Big> Rows { get; set; } }
                                        public class D { public List<BigDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.EndsWith(
                " At 72 bytes it is over the 32-byte threshold for copying by value, so pass it by 'in'.",
                Message(one),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A public unsealed target: the derived-type sweep walked THIS assembly, and a consuming project
        ///     can still subclass it. The message says what was actually checked rather than implying the
        ///     question was settled everywhere.
        /// </summary>
        [Fact]
        public void A_public_unsealed_target_says_what_the_derivation_check_covered()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.EndsWith(
                " The check that nothing derives from 'Demo.OrderDto' covered this assembly only, since " +
                "'Demo.OrderDto' is public and not sealed — a project referencing this one can still derive " +
                "from it.",
                Message(one),
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     Once per element PAIR, however many members reach it. An Info repeated per member is the shape
        ///     consumers suppress wholesale, and the message names the pair and nothing about the member, so the
        ///     second and third copies are the same string and drop.
        /// </summary>
        [Fact]
        public void The_same_element_pair_is_named_once_however_many_members_reach_it()
        {
            Assert.Single(Run("""
                              using DwarfMapper;
                              using System.Collections.Generic;
                              namespace Demo;
                              public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                              public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                              public class C
                              {
                                  public List<Order> Rows { get; set; }
                                  public Order[] Archive { get; set; }
                              }
                              public class D
                              {
                                  public List<OrderDto> Rows { get; set; }
                                  public OrderDto[] Archive { get; set; }
                              }
                              [DwarfMapper] public partial class M { public partial D Map(C c); }
                              """));
        }

        /// <summary>
        ///     A NULLABLE-annotated element names the type without its annotation. Found in the repository's own
        ///     integration corpus while measuring how often this fires: the message read "'ElChildDto?' is
        ///     transfer-model shaped … declared as a readonly record struct", which names no declaration that
        ///     exists — the consumer would go looking for a type called <c>ElChildDto?</c>. It is a dedupe bug
        ///     as well as a wording one: <c>List&lt;Dto&gt;</c> and <c>List&lt;Dto?&gt;</c> on one mapper are one
        ///     type and must be one report.
        /// </summary>
        [Fact]
        public void A_nullable_annotated_element_is_named_without_its_annotation()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run("""
                                                            using DwarfMapper;
                                                            using System.Collections.Generic;
                                                            namespace Demo;
                                                            public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                                            public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                                            public class C
                                                            {
                                                                public List<Order?> Maybe { get; set; } = new();
                                                                public List<Order> Rows { get; set; } = new();
                                                            }
                                                            public class D
                                                            {
                                                                public List<OrderDto?> Maybe { get; set; } = new();
                                                                public List<OrderDto> Rows { get; set; } = new();
                                                            }
                                                            [DwarfMapper] public partial class M { public partial D Map(C c); }
                                                            """,
                NullableContextOptions.Enable);

            var one = Assert.Single(diagnostics.Where(d => d.Id == "DWARF103"));
            Assert.Equal(OrderDtoMessage, Message(one));
        }

        /// <summary>
        ///     A collection reached through a NESTED object map is reported once, like any other. The nested
        ///     pair's own resolution runs later, out of the registry's drain loop, and the question is whether
        ///     that resolution reports into the same sink — if it did not, the same pair would be named twice at
        ///     two locations, and the once-per-pair rule would hold only for the shapes that happen to resolve
        ///     on the first pass.
        /// </summary>
        [Fact]
        public void A_pair_reached_through_a_nested_object_map_is_reported_once()
        {
            Assert.Single(Run("""
                              using DwarfMapper;
                              using System.Collections.Generic;
                              namespace Demo;
                              public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                              public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                              public sealed class Basket { public List<Order> Rows { get; set; } }
                              public sealed class BasketDto { public List<OrderDto> Rows { get; set; } }
                              public class C { public Basket Inner { get; set; } public List<Order> Direct { get; set; } }
                              public class D { public BasketDto Inner { get; set; } public List<OrderDto> Direct { get; set; } }
                              [DwarfMapper] public partial class M { public partial D Map(C c); }
                              """));
        }

        /// <summary>Two DIFFERENT element pairs are two reports: the dedupe is by pair, not a global cap.</summary>
        [Fact]
        public void Two_different_element_pairs_are_two_reports()
        {
            Assert.Equal(2,
                Run("""
                    using DwarfMapper;
                    using System.Collections.Generic;
                    namespace Demo;
                    public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                    public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                    public sealed class Line { public long Id { get; set; } public int Count { get; set; } }
                    public sealed class LineDto { public long Id { get; set; } public int Count { get; set; } }
                    public class C
                    {
                        public List<Order> Rows { get; set; }
                        public List<Line> Lines { get; set; }
                    }
                    public class D
                    {
                        public List<OrderDto> Rows { get; set; }
                        public List<LineDto> Lines { get; set; }
                    }
                    [DwarfMapper] public partial class M { public partial D Map(C c); }
                    """).Count);
        }

        /// <summary>
        ///     An UNSHAPED source is still reported — the advice is about the target, which is fully
        ///     rule-checked — but the message says nothing about converting the source. Round 29 T2.2 review,
        ///     critical 2: <c>Classify</c> runs on the target only, and every refusal that earns the target its
        ///     safety (derived from, abstract, IDisposable, an event, ORM-tracked, a validating constructor) was
        ///     never asked of the source. In the commonest real shape — entity → DTO — the closing clause was
        ///     therefore advising that a tracked entity become a struct, on no evidence at all.
        ///     <para>
        ///         The trigger is deliberately NOT narrowed: collapsing N object headers into one array is the
        ///         measured win and it does not depend on the source. Only the clause is gated.
        ///     </para>
        /// </summary>
        [Fact]
        public void An_unshaped_source_is_reported_but_never_told_to_become_a_struct()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order
                                        {
                                            public long Id { get; set; }
                                            public int Quantity { get; set; }
                                            public event EventHandler Changed;
                                        }
                                        public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.Equal(
                "'Demo.Order' → 'Demo.OrderDto' allocates one 'Demo.OrderDto' per element, and 'Demo.OrderDto' " +
                "is transfer-model shaped: declared as a readonly record struct — with any transfer model it " +
                "holds a struct too — it is 16 bytes, and the collection becomes one allocation instead of one " +
                "per element.",
                Message(one));

            Assert.DoesNotContain("block copy", Message(one), StringComparison.Ordinal);
            Assert.DoesNotContain("'Demo.Order' is transfer-model shaped", Message(one), StringComparison.Ordinal);
        }

        /// <summary>
        ///     A size that is a BOUND stays a bound in the <c>in</c> advice too. Round 29 T2.2 review,
        ///     important 4: the message hedged the size in one clause and reprinted it as a bare fact in the
        ///     next ("at most 40 bytes …" then "At 40 bytes pass it by 'in'"). No fixture crossed both branches,
        ///     so nothing caught it. Five strings is 40 bytes on x64 and 20 on x86 — over the threshold on the
        ///     bound, possibly under it in reality, and worth passing by <c>in</c> either way.
        /// </summary>
        [Fact]
        public void A_bounded_size_stays_a_bound_in_the_in_advice()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Wide { public string A { get; set; } public string B { get; set; } public string C1 { get; set; } public string D1 { get; set; } public string E { get; set; } }
                                        public sealed class WideDto { public string A { get; set; } public string B { get; set; } public string C1 { get; set; } public string D1 { get; set; } public string E { get; set; } }
                                        public class C { public List<Wide> Rows { get; set; } }
                                        public class D { public List<WideDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.EndsWith(
                " At most 40 bytes — over the 32-byte threshold for copying by value unless a 32-bit runtime " +
                "narrows it below, and worth passing by 'in' either way.",
                Message(one),
                StringComparison.Ordinal);

            // The same pair may not be told it could blit: five reference members, so neither side is unmanaged.
            Assert.DoesNotContain("block copy", Message(one), StringComparison.Ordinal);
        }

        /// <summary>
        ///     ASYMMETRIC, target dirty: the target holds a <c>string</c> and the source holds nothing but
        ///     primitives, so only the TARGET conjunct of the block-copy gate can suppress the clause. Round 29
        ///     T2.2 fix round 2, important 1 — both existing suppression fixtures carry a reference on BOTH
        ///     sides, so either conjunct alone kept them green and deleting the target one would have let
        ///     critical 1 back in with every assertion still passing.
        ///     <para>
        ///         <c>[MapIgnore]</c> is what makes the shape reachable: a target member with no source of its
        ///         own is a completeness refusal, and the point here is a target that is dirty for a reason the
        ///         source cannot be blamed for.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_target_holding_a_reference_is_never_told_it_could_blit_though_the_source_is_clean()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                        public sealed class OrderDto
                                        {
                                            public long Id { get; set; }
                                            public int Quantity { get; set; }
                                            public string Note { get; set; }
                                        }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper]
                                        public partial class M
                                        {
                                            [MapIgnore(nameof(OrderDto.Note))]
                                            public partial D Map(C c);
                                        }
                                        """));

            // The target's own size is a bound; the source's is exact. Only the target conjunct stands between
            // this message and a block copy the pair can never take.
            Assert.Contains("it is at most 24 bytes", Message(one), StringComparison.Ordinal);
            Assert.DoesNotContain("block copy", Message(one), StringComparison.Ordinal);
        }

        /// <summary>
        ///     ASYMMETRIC, source dirty: the mirror of the fixture above, and it pins the other conjunct. The
        ///     target is all primitives — its size is exact — while the source holds a <c>string</c>, so a
        ///     block copy is still impossible and only the SOURCE conjunct says so.
        /// </summary>
        [Fact]
        public void A_source_holding_a_reference_is_never_told_it_could_blit_though_the_target_is_clean()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order
                                        {
                                            public long Id { get; set; }
                                            public int Quantity { get; set; }
                                            public string Note { get; set; }
                                        }
                                        public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.Contains("it is 16 bytes", Message(one), StringComparison.Ordinal);
            Assert.DoesNotContain("block copy", Message(one), StringComparison.Ordinal);
        }

        /// <summary>
        ///     A source in a <c>.g.cs</c> is never named by the block-copy clause. Round 29 T2.2 fix round 2,
        ///     minor 2: the target has been refused for this since T2.2 landed — a consumer cannot rewrite a
        ///     declaration they did not write — and the clause was inviting them to rewrite a SOURCE under the
        ///     same disability. The diagnostic itself still fires: the target is theirs to change, and that is
        ///     the whole allocation win.
        /// </summary>
        [Fact]
        public void A_source_another_generator_emitted_is_never_named_by_the_block_copy_clause()
        {
            const string mapper = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;
            const string source = """
                                  namespace Demo;
                                  public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                  """;

            // Control: the identical source in a hand-written file earns the clause.
            Assert.Contains("block copy",
                Message(Assert.Single(RunAcross(mapper, source, "Order.cs"))),
                StringComparison.Ordinal);

            var generated = Assert.Single(RunAcross(mapper, source, "Order.g.cs"));
            Assert.DoesNotContain("block copy", Message(generated), StringComparison.Ordinal);
        }

        /// <summary>
        ///     A public unsealed SOURCE earns the same assembly-scope caveat the target does, and the two are
        ///     stated in ONE sentence rather than two. Round 29 T2.2 fix round 2, minor 2: the clause called the
        ///     source "transfer-model shaped too" on evidence the target's own equivalent would have qualified
        ///     — the derived-type sweep walks this assembly, and a consuming project can subclass either type.
        /// </summary>
        [Fact]
        public void Both_sides_share_one_derivation_caveat_when_both_are_public_and_unsealed()
        {
            var one = Assert.Single(Run("""
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """));

            Assert.EndsWith(
                " The check that nothing derives from 'Demo.OrderDto' or 'Demo.Order' covered this assembly " +
                "only, since both are public and not sealed — a project referencing this one can still derive " +
                "from them.",
                Message(one),
                StringComparison.Ordinal);
        }

        // ─── The silence cases ───────────────────────────────────────────────────

        /// <summary>The remedy applied: a target element that is ALREADY a struct has nothing to say.</summary>
        [Fact]
        public void A_struct_target_element_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public readonly record struct OrderDto(long Id, int Quantity);
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     An event is behaviour: subscribers hold the object, and copying a value type copies the
        ///     invocation list with it. The classifier refuses it, and the report site must be reading the
        ///     classifier's verdict rather than a shape test of its own.
        /// </summary>
        [Fact]
        public void A_target_with_an_event_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto
                             {
                                 public long Id { get; set; }
                                 public int Quantity { get; set; }
                                 public event EventHandler Changed;
                             }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A type someone derives from cannot become a struct at all, and the derived type is IN this
        ///     assembly, so the sweep sees it. Pinned at the report site as well as in the classifier: a site
        ///     that read only <c>IsShaped</c>'s size would have reported it.
        /// </summary>
        [Fact]
        public void A_target_with_a_derived_type_in_this_assembly_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class AuditedOrderDto : OrderDto { }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     <c>List&lt;Order&gt; → List&lt;Order&gt;</c> copies REFERENCES: no element is allocated, so
        ///     "one allocation instead of one per element" would be false. The claim the message makes is what
        ///     decides this, not the shape of the target.
        /// </summary>
        [Fact]
        public void An_identity_element_pair_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public List<OrderDto> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A hand-written element converter OWNS the construction of every element. The consumer's own
        ///     method body returns a class instance; suggesting the type become a struct would be suggesting we
        ///     rewrite code this generator does not emit, and the code fix in T2.3 does not touch usages.
        /// </summary>
        [Fact]
        public void A_hand_written_element_converter_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial D Map(C c);
                                 public static OrderDto Convert(Order o) => new OrderDto { Id = o.Id, Quantity = o.Quantity };
                             }
                             """));
        }

        /// <summary>
        ///     A user-defined conversion OPERATOR is only the resolver's answer when the auto-nest arm declines
        ///     the pair, and the report follows the resolver rather than the declaration. With auto-nest ON the
        ///     operator is not called for the elements at all — the synthesized object map builds them, so the
        ///     allocation is this generator's to remove and the hint stands. Under
        ///     <c>[DwarfMapper(AutoNest = false)]</c> the operator IS what runs, it owns the construction of
        ///     every element, and the hint goes quiet.
        ///     <para>
        ///         Both halves are asserted, because the first draft of this test asserted only silence and was
        ///         wrong about which arm wins: the pair resolves through <c>AutoNestWouldClaim</c>, exactly as
        ///         <c>ElementPairResolvesToUserConversion</c>'s remarks say it does.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_user_defined_conversion_operator_is_silent_only_when_it_is_what_runs()
        {
            const string withOperator = """
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                        public sealed class OrderDto
                                        {
                                            public long Id { get; set; }
                                            public int Quantity { get; set; }
                                            public static implicit operator OrderDto(Order o) => new OrderDto { Id = o.Id, Quantity = o.Quantity };
                                        }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper(AutoNest = {0})] public partial class M { public partial D Map(C c); }
                                        """;

            Assert.Single(Run(withOperator.Replace("{0}", "true", StringComparison.Ordinal)));
            Assert.Empty(Run(withOperator.Replace("{0}", "false", StringComparison.Ordinal)));
        }

        /// <summary>
        ///     A hand-written OVERLOAD that shares its name with a partial mapper method is still hand-written.
        ///     The resolver hands back a NAME, and a name alone is ambiguous here: <c>Map</c> is both the partial
        ///     method the generator implements for <c>Order</c> → <c>OrderDto</c> and the static method the
        ///     consumer wrote for <c>Line</c> → <c>LineDto</c>. Matching the name against the auto-candidates
        ///     without also matching the SIGNATURE would report a pair whose elements this generator does not
        ///     build — which is why the match is on both.
        /// </summary>
        [Fact]
        public void A_hand_written_overload_sharing_a_partial_methods_name_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class Line { public long Id { get; set; } public int Count { get; set; } }
                             public sealed class LineDto { public long Id { get; set; } public int Count { get; set; } }
                             public class C { public List<Line> Rows { get; set; } }
                             public class D { public List<LineDto> Rows { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial D MapAll(C c);
                                 public partial OrderDto Map(Order o);
                                 public static LineDto Map(Line l) => new LineDto { Id = l.Id, Count = l.Count };
                             }
                             """));
        }

        /// <summary>
        ///     A STRUCT source element is out of scope, deliberately. The plan scopes this to "a collection of
        ///     class elements maps to a collection of class elements", and the message is written for that: it
        ///     offers the block copy as what happens once the source is a struct too, which reads as nonsense
        ///     when it already is. The advice would still be worth giving — a struct on both sides is the
        ///     strongest case in the research — so this silence is a scope boundary rather than a refusal, and
        ///     it is pinned here so widening it is a deliberate edit with a message to rewrite.
        /// </summary>
        [Fact]
        public void A_struct_source_element_is_out_of_scope_and_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public struct Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A <c>[AfterMap]</c> hook matching the element pair takes the target and mutates it. On a
        ///     <c>readonly record struct</c> that mutation is either a compile error or lost on the copy — a
        ///     silent semantic change, which is the failure class this project refuses. The pair-scoped
        ///     customization rule is the same one the blit gate reads, so the two cannot disagree.
        /// </summary>
        [Fact]
        public void An_element_pair_carrying_a_hook_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper]
                             public partial class M
                             {
                                 public partial D Map(C c);
                                 [AfterMap] private static void Finish(Order s, OrderDto t) { t.Quantity = s.Quantity; }
                             }
                             """));
        }

        /// <summary>
        ///     Preserve mode reconstructs the source's TOPOLOGY: two references to one object become two
        ///     references to one mapped object. A value type has no identity to preserve, so the suggestion
        ///     would undo the very thing the mapper was configured for.
        /// </summary>
        [Fact]
        public void Preserve_mode_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                             public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     <c>OnCycle = SetNull</c> writes <c>null</c> into a back-edge. A struct field cannot hold one, so
        ///     the mapper's cycle strategy and the suggestion are incompatible.
        /// </summary>
        [Fact]
        public void SetNull_mode_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             using System.Collections.Generic;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public List<Order> Rows { get; set; } }
                             public class D { public List<OrderDto> Rows { get; set; } }
                             [DwarfMapper(OnCycle = OnCycleStrategy.SetNull)]
                             public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A transfer-model class mapped as a SCALAR member is allocated once, not once per element. The
        ///     whole claim is about collections, and a hint that fired on every nested DTO member would be the
        ///     type-level analyzer the research explicitly rejected.
        /// </summary>
        [Fact]
        public void A_scalar_member_of_the_same_shape_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             namespace Demo;
                             public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                             public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                             public class C { public Order Row { get; set; } }
                             public class D { public OrderDto Row { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A collection of PRIMITIVES says nothing: there is no element object to remove, and the pair
        ///     already blits. The site sits above nothing it needs to be below, but this pins that it does not
        ///     fire where <c>DWARF100</c>/<c>DWARF101</c> live.
        /// </summary>
        [Fact]
        public void A_collection_of_primitives_reports_nothing()
        {
            Assert.Empty(Run("""
                             using DwarfMapper;
                             namespace Demo;
                             public class C { public int[] Rows { get; set; } }
                             public class D { public int[] Rows { get; set; } }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """));
        }

        /// <summary>
        ///     A DTO ANOTHER source generator emitted is never named. It is transfer-model shaped by every rule
        ///     the classifier applies — it is in source, sealed, and holds nothing but data — and fails the only
        ///     question a REPORT has to answer: the consumer cannot rewrite a declaration they did not write,
        ///     and cannot suppress a diagnostic raised inside a <c>.g.cs</c> either. The same refusal
        ///     <c>DWARF101</c> makes, for the same reason and in the same place — at the report site, not in the
        ///     predicate, because measurability and actionability are different questions.
        ///     <para>
        ///         The control half is the point: the SAME source under a path that is not <c>.g.cs</c> reports,
        ///         so this pins the file path as the discriminator rather than passing for an unrelated reason.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_dto_another_generator_emitted_is_never_named()
        {
            const string mapper = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public long Id { get; set; } public int Quantity { get; set; } }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;
            const string dto = """
                               namespace Demo;
                               public sealed class OrderDto { public long Id { get; set; } public int Quantity { get; set; } }
                               """;

            Assert.Single(RunAcross(mapper, dto, "OrderDto.cs"));
            Assert.Empty(RunAcross(mapper, dto, "OrderDto.g.cs"));
        }

        /// <summary>
        ///     Runs the generator over two files, the second under <paramref name="secondPath" />, and returns
        ///     the DWARF103s. The path is the whole point — <c>GeneratedSourceExtensions.IsGeneratorAuthored</c>
        ///     reads it — so the trees are built here rather than through the single-source harness entry.
        /// </summary>
        private static List<Diagnostic> RunAcross(string first, string second, string secondPath)
        {
            var compilation = GeneratorTestHarness.BuildCompilation("DwarfMapperTestAsm",
                new[]
                {
                    CSharpSyntaxTree.ParseText(first, path: "Mapper.cs"),
                    CSharpSyntaxTree.ParseText(second, path: secondPath)
                });

            CSharpGeneratorDriver.Create(new DwarfGenerator())
                .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

            return diagnostics.Where(d => d.Id == "DWARF103").ToList();
        }
    }
}
