// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;

namespace DwarfMapper.Generator.Tests.CodeFixes
{
    /// <summary>
    ///     What the <c>DWARF103</c> code fix REFUSES, which is the half that keeps it safe to offer at all.
    ///     <para>
    ///         The action rewrites a consumer's type declaration and deliberately leaves every usage alone, so
    ///         its whole safety argument rests on being offered only where <c>TransferModelShape</c> has already
    ///         said yes and on doing the entire transitive set or none of it. Each row below is one way that
    ///         argument could be lost quietly: a fix offered on a diagnostic that never classified anything, a
    ///         fix that recovers its target by guessing, or a fix that converts a root whose nested models it
    ///         could not reach — which leaves the consumer with a type SMALLER than the size they were shown.
    ///     </para>
    /// </summary>
    public sealed class ConvertToRecordStructRefusalTests : IDisposable
    {
        /// <summary>The shape the fix is legitimately offered on, reused as the control in several rows.</summary>
        private const string Reported = """
                                        using DwarfMapper;
                                        using System.Collections.Generic;
                                        namespace Demo;
                                        public sealed class Money { public long Units { get; set; } }
                                        public sealed class Order { public long Id { get; set; } public Money Total { get; set; } }
                                        public sealed class OrderDto { public long Id { get; set; } public Money Total { get; set; } }
                                        public class C { public List<Order> Rows { get; set; } }
                                        public class D { public List<OrderDto> Rows { get; set; } }
                                        [DwarfMapper] public partial class M { public partial D Map(C c); }
                                        """;

        private readonly ConvertToRecordStructFixture _fixture = new();

        public void Dispose()
        {
            _fixture.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     The fix answers to <c>DWARF103</c> and to nothing else. Asserted as the exact set rather than as
        ///     "contains DWARF103", because a provider that quietly grew a second id would be offering a
        ///     type rewrite on a diagnostic that never ran the classifier.
        /// </summary>
        [Fact]
        public void The_fix_is_registered_for_DWARF103_alone()
        {
            var ids = new ConvertToRecordStructCodeFixProvider().FixableDiagnosticIds;

            Assert.Equal("DWARF103", Assert.Single(ids));
        }

        /// <summary>
        ///     And is not offered on another diagnostic even when it is handed one directly. The IDE filters by
        ///     <c>FixableDiagnosticIds</c>, so this is belt and braces — but the belt is what a
        ///     <c>FixAllContext</c> or a future caller would bypass, and the cost of the fix firing on the wrong
        ///     diagnostic is a rewritten consumer type.
        /// </summary>
        [Fact]
        public async Task The_fix_is_not_offered_on_another_diagnostic()
        {
            const string unmapped = """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public class A { public int X { get; set; } }
                                    public class B { public int X { get; set; } public int Extra { get; set; } }
                                    [DwarfMapper] public partial class M { public partial B Map(A a); }
                                    """;

            var document = _fixture.Document(unmapped);
            var diagnostic = Assert.Single(GeneratorAssert.Reports(unmapped, "DWARF001"));

            Assert.Empty(await ConvertToRecordStructFixture.OfferForAsync(document, diagnostic).ConfigureAwait(true));
        }

        /// <summary>
        ///     A <c>DWARF103</c> carrying no <c>TransferModelId</c> offers nothing. That is the shape of an
        ///     older generator's diagnostic, and the alternative — recovering the type name from the message —
        ///     is exactly what ruling 1 forbids: the wording moved four times while T2.2 was landing, and a fix
        ///     that read it would have broken with nothing failing.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_with_no_handle_offers_nothing()
        {
            var actions = await OfferSyntheticAsync(ImmutableDictionary<string, string?>.Empty)
                .ConfigureAwait(true);

            Assert.Empty(actions);
        }

        /// <summary>
        ///     An EMPTY handle is the same refusal as an absent one, and a separate row because it is a
        ///     separate branch: the guard is <c>!TryGetValue(...) || IsNullOrEmpty(...)</c>, and a fix that
        ///     dropped the second half would offer an action whose target resolves to nothing.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_with_an_empty_handle_offers_nothing()
        {
            var actions = await OfferSyntheticAsync(
                    ImmutableDictionary<string, string?>.Empty.Add("TransferModelId", string.Empty))
                .ConfigureAwait(true);

            Assert.Empty(actions);
        }

        /// <summary>
        ///     A handle missing its <c>T:</c> prefix — not a shape the generator writes, but one a hand-built or
        ///     foreign diagnostic can carry — is still offered, titled by its last name segment, and rewrites
        ///     NOTHING: <see cref="DocumentationCommentId.GetFirstSymbolForDeclarationId" /> resolves only a
        ///     prefixed id, so the conversion takes its unresolvable-root refusal and returns the solution
        ///     unchanged. Pinned as today's behaviour: a malformed handle costs a no-op lightbulb, never a
        ///     partial rewrite.
        /// </summary>
        [Fact]
        public async Task A_handle_without_its_T_prefix_is_titled_by_its_last_segment_and_rewrites_nothing()
        {
            var properties = ImmutableDictionary<string, string?>.Empty.Add("TransferModelId", "Demo.Money");

            var action = Assert.Single(await OfferSyntheticAsync(properties).ConfigureAwait(true));
            Assert.EndsWith("'Money'", action.Title, StringComparison.Ordinal);

            var text = await ApplySyntheticAsync(_fixture.Document(Reported), properties).ConfigureAwait(true);
            Assert.Equal(Reported, text);
        }

        /// <summary>
        ///     An empty NESTED list means "no nested models", not "one model with an empty name". The
        ///     generator writes the property only when there is something in it, so this is the shape a
        ///     hand-built or older diagnostic takes — and reading it as a single blank handle would abort a
        ///     rewrite that is perfectly fine to make.
        /// </summary>
        [Fact]
        public async Task An_empty_nested_list_still_converts_the_root()
        {
            var text = await ApplySyntheticAsync(
                    _fixture.Document(Reported),
                    ImmutableDictionary<string, string?>.Empty
                        .Add("TransferModelId", "T:Demo.Money")
                        .Add("NestedTransferModelIds", string.Empty))
                .ConfigureAwait(true);

            Assert.Contains("readonly record struct Money", text, StringComparison.Ordinal);
        }

        /// <summary>
        ///     <b>An unresolvable nested handle takes the whole rewrite down.</b> Half a transitive conversion
        ///     is the one outcome worse than none: the root becomes a struct whose nested members are still
        ///     references, so the consumer ends up with a type SMALLER than the byte count the diagnostic
        ///     printed — a number already in front of them, made retroactively false by the fix that was
        ///     supposed to deliver it.
        ///     <para>
        ///         The control is the same document with the real handle, which does rewrite.
        ///     </para>
        /// </summary>
        [Fact]
        public async Task An_unresolvable_nested_handle_leaves_the_solution_untouched()
        {
            var document = _fixture.Document(Reported);

            var broken = await ApplySyntheticAsync(
                    document,
                    ImmutableDictionary<string, string?>.Empty
                        .Add("TransferModelId", "T:Demo.OrderDto")
                        .Add("NestedTransferModelIds", "T:Demo.NoSuchType"))
                .ConfigureAwait(true);

            Assert.Equal(Reported, broken);

            var control = await ApplySyntheticAsync(
                    document,
                    ImmutableDictionary<string, string?>.Empty
                        .Add("TransferModelId", "T:Demo.OrderDto")
                        .Add("NestedTransferModelIds", "T:Demo.Money"))
                .ConfigureAwait(true);

            Assert.Contains("readonly record struct OrderDto", control, StringComparison.Ordinal);
            Assert.Contains("readonly record struct Money", control, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An unresolvable ROOT handle is the same refusal by the other door — a type the diagnostic named
        ///     that this compilation no longer has, which is what a stale diagnostic in an IDE looks like.
        /// </summary>
        [Fact]
        public async Task An_unresolvable_root_handle_leaves_the_solution_untouched()
        {
            var text = await ApplySyntheticAsync(
                    _fixture.Document(Reported),
                    ImmutableDictionary<string, string?>.Empty.Add("TransferModelId", "T:Demo.NoSuchType"))
                .ConfigureAwait(true);

            Assert.Equal(Reported, text);
        }

        /// <summary>
        ///     <b>A generic handle is never offered the fix, and this is now a SECOND line of defence.</b>
        ///     The classifier refuses a generic transfer model outright since T2.3 fix round 2, so the
        ///     generator no longer produces a <c>DWARF103</c> carrying one — which is exactly why the check
        ///     stays and why the diagnostic here is built by hand. A code fix reads a diagnostic it did not
        ///     create: one left over in an IDE from before the narrowing, or from a mismatched analyzer
        ///     version, still arrives at this provider, and a rewrite of <c>Box&lt;T&gt;</c> would convert
        ///     every instantiation — including ones nothing classified and whose size is not the one printed.
        ///     <para>
        ///         The refusal is read off the id STRING, so it costs no compilation and happens before any
        ///         action is registered. <c>TransferModelShapeTests.The_generic_refusal_and_the_code_fixs_handle_test_agree</c>
        ///         is what keeps this rule and the classifier's from drifting: the two cannot share code,
        ///         because <c>DwarfMapper.CodeFixes</c> does not reference the generator.
        ///     </para>
        /// </summary>
        [Fact]
        public async Task A_generic_handle_is_never_offered_the_fix()
        {
            var actions = await OfferSyntheticAsync(
                    ImmutableDictionary<string, string?>.Empty.Add("TransferModelId", "T:Demo.Box`1"))
                .ConfigureAwait(true);

            Assert.Empty(actions);
        }

        /// <summary>
        ///     And the classifier's half of the same rule, asserted from here so a reader of the refusal
        ///     suite sees why the row above needs a hand-built diagnostic: the generator does not emit one.
        /// </summary>
        [Fact]
        public void A_generic_element_type_never_produces_a_diagnostic_to_fix()
        {
            GeneratorAssert.DoesNotReport("""
                                          using DwarfMapper;
                                          using System.Collections.Generic;
                                          namespace Demo;
                                          public sealed class Src { public int Value { get; set; } }
                                          public sealed class Box<T> { public T Value { get; set; } }
                                          public sealed class Holder { public Box<string> Text { get; set; } }
                                          public class C { public List<Src> Rows { get; set; } }
                                          public class D { public List<Box<int>> Rows { get; set; } }
                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                          """, "DWARF103");
        }

        /// <summary>
        ///     The same refusal when the generic is a NESTED model rather than the root: the root alone is
        ///     rewritable, but converting it without the model it inlines is the one outcome worse than none.
        /// </summary>
        [Fact]
        public async Task A_generic_nested_model_takes_the_whole_offer_down()
        {
            var actions = await ConvertToRecordStructFixture.OfferForAsync(
                    _fixture.Document(Reported),
                    Synthetic(ImmutableDictionary<string, string?>.Empty
                        .Add("TransferModelId", "T:Demo.OrderDto")
                        .Add("NestedTransferModelIds", "T:Demo.Box`1")))
                .ConfigureAwait(true);

            Assert.Empty(actions);
        }

        /// <summary>
        ///     No Fix All. Every action is already a whole-solution change over a transitive set, and the batch
        ///     fixer computes each one against the ORIGINAL solution — two roots sharing a nested model would
        ///     have one rewrite silently dropped. Pinned because <c>null</c> here is a decision, and a later
        ///     reader reaching for <c>WellKnownFixAllProviders.BatchFixer</c> to satisfy an analyzer would be
        ///     undoing it without noticing.
        /// </summary>
        [Fact]
        public void The_fix_offers_no_fix_all()
        {
            Assert.Null(new ConvertToRecordStructCodeFixProvider().GetFixAllProvider());
        }

        /// <summary>
        ///     A model carrying an EXPRESSION-BODIED (computed) property is never reported, so the fix never
        ///     sees one.
        ///     <para>
        ///         Found while chasing the round-29 Codecov report, which flagged the null-<c>AccessorList</c>
        ///         guard in <c>WithInitAccessor</c> as uncovered. The guard exists because an expression-bodied
        ///         property has no accessor list to rewrite — and the GENERATOR never reaches it, because the
        ///         classifier declines the type before the fix is ever offered. A diagnostic the generator did
        ///         not just produce still can: a stale one left in an IDE, or one from a mismatched analyzer
        ///         version. <see cref="A_stale_handle_to_a_model_with_a_computed_property_keeps_the_property_as_written" />
        ///         drives the guard that way; this test records why the generator path never does.
        ///     </para>
        ///     <para>
        ///         The refusal is also right on its own terms: a computed property has no backing state, so a
        ///         <c>readonly record struct</c> rewrite has nothing to carry it into.
        ///     </para>
        /// </summary>
        [Fact]
        public void A_model_with_a_computed_property_is_never_reported_so_the_accessor_guard_is_unreachable()
        {
            GeneratorAssert.DoesNotReport("""
                                          using DwarfMapper;
                                          using System.Collections.Generic;
                                          namespace Demo;
                                          public sealed class Order { public long Id { get; set; } }
                                          public sealed class OrderDto
                                          {
                                              public long Id { get; set; }
                                              private long Doubled => Id * 2;
                                          }
                                          public class C { public List<Order> Rows { get; set; } }
                                          public class D { public List<OrderDto> Rows { get; set; } }
                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                          """, "DWARF103");
        }

        /// <summary>
        ///     A STALE <c>DWARF103</c> naming a model that has since gained an expression-bodied property still
        ///     reaches the fix, because a code fix reads a diagnostic it did not produce. The rewrite keeps the
        ///     computed property exactly as written: it has no accessor list, so there is no <c>set</c> to turn
        ///     into <c>init</c>, and inventing one would give the struct state the class never had. The
        ///     auto-property beside it is still rewritten, which is the control.
        /// </summary>
        [Fact]
        public async Task A_stale_handle_to_a_model_with_a_computed_property_keeps_the_property_as_written()
        {
            const string source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public sealed class OrderDto
                                  {
                                      public long Id { get; set; }
                                      public long Doubled => Id * 2;
                                  }
                                  """;

            var text = await ApplySyntheticAsync(
                    _fixture.Document(source),
                    ImmutableDictionary<string, string?>.Empty.Add("TransferModelId", "T:Demo.OrderDto"))
                .ConfigureAwait(true);

            Assert.Contains("readonly record struct OrderDto", text, StringComparison.Ordinal);
            Assert.Contains("public long Doubled => Id * 2;", text, StringComparison.Ordinal);
            Assert.Contains("init;", text, StringComparison.Ordinal);
            Assert.DoesNotContain("set;", text, StringComparison.Ordinal);
        }

        /// <summary>
        ///     And the outermost refusal, which is not this provider's at all: where the classifier says no,
        ///     there is no <c>DWARF103</c> and so nothing to offer. An ORM entity is the case that matters most
        ///     — converting one to a value type breaks change tracking rather than the build.
        /// </summary>
        [Fact]
        public void An_entity_shaped_element_is_never_reported_so_the_fix_is_never_reached()
        {
            GeneratorAssert.DoesNotReport("""
                                          using DwarfMapper;
                                          using System.Collections.Generic;
                                          namespace Demo;
                                          public sealed class TableAttribute : System.Attribute { }
                                          public sealed class Order { public long Id { get; set; } }
                                          [Table] public sealed class OrderDto { public long Id { get; set; } }
                                          public class C { public List<Order> Rows { get; set; } }
                                          public class D { public List<OrderDto> Rows { get; set; } }
                                          [DwarfMapper] public partial class M { public partial D Map(C c); }
                                          """, "DWARF103");
        }

        // ─── Harness ─────────────────────────────────────────────────────────────

        /// <summary>
        ///     A <c>DWARF103</c> built here rather than by the generator, so a property bag the generator cannot
        ///     currently produce — an absent handle, a stale one — can still be put in front of the fix. The
        ///     descriptor's other fields are irrelevant: the provider reads the id and the properties.
        /// </summary>
        private static Diagnostic Synthetic(ImmutableDictionary<string, string?> properties)
        {
            var descriptor = new DiagnosticDescriptor(
                "DWARF103",
                "Collection element could be a struct",
                "{0}",
                "Usage",
                DiagnosticSeverity.Info,
                true);

            return Diagnostic.Create(descriptor, Location.None, properties, "element");
        }

        private Task<List<CodeAction>> OfferSyntheticAsync(ImmutableDictionary<string, string?> properties)
        {
            return ConvertToRecordStructFixture.OfferForAsync(_fixture.Document(Reported), Synthetic(properties));
        }

        private static async Task<string> ApplySyntheticAsync(
            Document document,
            ImmutableDictionary<string, string?> properties)
        {
            var actions = await ConvertToRecordStructFixture.OfferForAsync(document, Synthetic(properties)).ConfigureAwait(false);
            var action = Assert.Single(actions);

            var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;

            return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
        }
    }
}
