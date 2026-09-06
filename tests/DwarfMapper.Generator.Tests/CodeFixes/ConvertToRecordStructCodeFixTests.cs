// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.CodeFixes;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests.CodeFixes
{
    /// <summary>
    ///     End-to-end test of the <c>DWARF103</c> code fix: the generator reports that a mapped collection's
    ///     element type could be a <c>readonly record struct</c>, and the fix performs that rewrite — on the
    ///     element type AND on every transfer model it inlines, in one solution change.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The transitivity is what makes a shipped number true.</b> The classifier costs a nested shaped
    ///         model at the nested struct's own size rather than at pointer width, so the size <c>DWARF103</c>
    ///         printed is the size the consumer gets ONLY if the nested types are converted too. A fix that
    ///         converted the root alone would retroactively falsify a message already in front of them, which is
    ///         why <see cref="The_rewritten_outer_type_is_the_size_the_diagnostic_printed" /> compiles the fixed
    ///         source and measures the result rather than asserting on text.
    ///     </para>
    ///     <para>
    ///         <b>Every rewrite is compiled.</b> Five commits earlier in this round fixed a family of defects
    ///         where generated code carried a compiler diagnostic the consumer cannot suppress; a type rewrite
    ///         can do the same thing in the consumer's OWN file, and CS8341 (auto-property in a readonly
    ///         struct), CS8340 (instance field), CS8983 (initialisers with no constructor) and CS0261 (partial
    ///         halves disagreeing) are all one careless line away.
    ///     </para>
    /// </remarks>
    public sealed class ConvertToRecordStructCodeFixTests : IDisposable
    {
        private readonly ConvertToRecordStructFixture _fixture = new();

        /// <summary>The headline shape: a flat sealed DTO reached through a mapped collection.</summary>
        private const string Flat = """
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
        ///     Two levels of nesting, all three types the consumer's own. Written under
        ///     <c>#nullable enable</c> with explicit non-null annotations, so the nested members inline BARE and
        ///     the arithmetic is the simple one; the oblivious variant is a separate test, because it is the
        ///     case a reviewer should expect to be got wrong.
        /// </summary>
        private const string TwoLevels = """
                                         #nullable enable
                                         using DwarfMapper;
                                         using System.Collections.Generic;
                                         namespace Demo;
                                         public sealed class Money { public long Units { get; set; } public int Scale { get; set; } }
                                         public sealed class Line { public Money Amount { get; set; } = new Money(); public int Count { get; set; } }
                                         public sealed class Order { public long Id { get; set; } public Line Line { get; set; } = new Line(); }
                                         public sealed class OrderDto { public long Id { get; set; } public Line Line { get; set; } = new Line(); }
                                         public class C { public List<Order> Rows { get; set; } = new(); }
                                         public class D { public List<OrderDto> Rows { get; set; } = new(); }
                                         [DwarfMapper] public partial class M { public partial D Map(C c); }
                                         """;

        // ─── The rewrite ─────────────────────────────────────────────────────────

        /// <summary>
        ///     A flat class becomes a <c>readonly record struct</c>: <c>sealed</c> goes, the members stay, and
        ///     <c>set</c> becomes <c>init</c> — that last one because CS8341 makes a settable auto-property in a
        ///     readonly struct a compile error, so "keeps members" cannot mean "keeps them verbatim".
        /// </summary>
        [Fact]
        public async Task A_flat_class_becomes_a_readonly_record_struct()
        {
            var fixedText = await ApplyAsync(Flat).ConfigureAwait(true);

            Assert.Contains("public readonly record struct OrderDto", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("class OrderDto", fixedText, StringComparison.Ordinal);
            Assert.Contains("public long Id { get; init; }", fixedText, StringComparison.Ordinal);

            // The SOURCE type is untouched. Only the element the diagnostic named is the fix's business — the
            // block-copy clause is advice, not a second rewrite this action is entitled to make.
            Assert.Contains("public sealed class Order", fixedText, StringComparison.Ordinal);

            GeneratorAssert.EmitsCompilableCode(fixedText);
        }

        /// <summary>
        ///     Two levels of nesting, all rewritten by ONE action. The list is not a convenience: every type
        ///     here is costed inline in the byte count the consumer was shown.
        /// </summary>
        [Fact]
        public async Task Two_levels_of_nesting_are_rewritten_by_one_action()
        {
            var fixedText = await ApplyAsync(TwoLevels).ConfigureAwait(true);

            Assert.Contains("public readonly record struct OrderDto", fixedText, StringComparison.Ordinal);
            Assert.Contains("public readonly record struct Line", fixedText, StringComparison.Ordinal);
            Assert.Contains("public readonly record struct Money", fixedText, StringComparison.Ordinal);

            GeneratorAssert.EmitsCompilableCode(fixedText);
        }

        /// <summary>
        ///     <b>The assertion the whole transitive design exists for.</b> The diagnostic printed a size; after
        ///     the fix, the type the consumer actually has must BE that size. Measured off the rewritten source
        ///     with <c>LayoutHygiene</c> — the same arithmetic the classifier used, so a disagreement between
        ///     the two would show up here rather than in a consumer's profiler.
        /// </summary>
        [Fact]
        public async Task The_rewritten_outer_type_is_the_size_the_diagnostic_printed()
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(TwoLevels);
            var reported = diagnostics.Single(d => d.Id == "DWARF103");
            var printed = int.Parse(reported.Properties["TransferModelSize"]!, CultureInfo.InvariantCulture);

            var fixedText = await ApplyAsync(TwoLevels).ConfigureAwait(true);
            var compilation = GeneratorTestHarness.BuildCompilation("SizeAsm", fixedText);
            var rewritten = compilation.GetTypeByMetadataName("Demo.OrderDto");

            Assert.NotNull(rewritten);
            Assert.True(rewritten!.IsValueType, "the fix did not produce a value type");

            var layout = LayoutHygiene.Measure(rewritten);
            Assert.NotNull(layout);
            Assert.Equal(printed, layout!.Value.Size);
        }

        // ─── The nullability hazard ──────────────────────────────────────────────

        /// <summary>
        ///     <b>An OBLIVIOUS nested member must gain a <c>?</c>.</b> Where the nullable context is off,
        ///     <c>Money Total</c> may legally hold null, and the classifier costs it as optional for exactly
        ///     that reason — a flag plus padding. Rewritten naively it becomes a non-nullable struct field that
        ///     cannot hold null: the meaning changes silently, and the type comes out SMALLER than the size the
        ///     diagnostic promised. Both halves are asserted, because either alone would pass a fix that got the
        ///     other wrong.
        /// </summary>
        [Fact]
        public async Task An_oblivious_nested_member_keeps_its_nullability_and_its_size()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Money { public long Units { get; set; } }
                                  public sealed class Order { public long Id { get; set; } }
                                  public sealed class OrderDto { public long Id { get; set; } public Money Total { get; set; } }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var (diagnostics, _) = GeneratorTestHarness.Run(source);
            var printed = int.Parse(
                diagnostics.Single(d => d.Id == "DWARF103").Properties["TransferModelSize"]!,
                CultureInfo.InvariantCulture);

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("public Money? Total { get; init; }", fixedText, StringComparison.Ordinal);

            var compilation = GeneratorTestHarness.BuildCompilation("ObliviousAsm", fixedText);
            var layout = LayoutHygiene.Measure(compilation.GetTypeByMetadataName("Demo.OrderDto")!);

            Assert.NotNull(layout);
            Assert.Equal(printed, layout!.Value.Size);
        }

        /// <summary>
        ///     The other case, and the one that needs nothing done: <c>Money? Total</c> under
        ///     <c>#nullable enable</c> is a nullable REFERENCE today and a <c>Nullable&lt;Money&gt;</c> after the
        ///     rewrite. The syntax is identical and still compiles — asserted rather than assumed, because "no
        ///     change needed" is a claim as capable of being wrong as any other.
        /// </summary>
        [Fact]
        public async Task An_annotated_nested_member_needs_no_change_to_its_syntax()
        {
            const string source = """
                                  #nullable enable
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Money { public long Units { get; set; } }
                                  public sealed class Order { public long Id { get; set; } }
                                  public sealed class OrderDto { public long Id { get; set; } public Money? Total { get; set; } }
                                  public class C { public List<Order> Rows { get; set; } = new(); }
                                  public class D { public List<OrderDto> Rows { get; set; } = new(); }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source, NullableContextOptions.Enable).ConfigureAwait(true);

            Assert.Contains("public Money? Total { get; init; }", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("Money?? Total", fixedText, StringComparison.Ordinal);
            Assert.Contains("public readonly record struct Money", fixedText, StringComparison.Ordinal);
        }

        // ─── The bands and the shapes that would not compile ─────────────────────

        /// <summary>
        ///     Over 64 bytes the rewritten type carries a note about passing it by <c>in</c> — the same band
        ///     <c>DWARF103</c> gives that advice in, and the number comes from the diagnostic rather than from
        ///     re-deriving a size in a project that cannot measure one.
        /// </summary>
        [Fact]
        public async Task An_oversized_type_is_rewritten_with_a_note_about_passing_it_by_in()
        {
            var longs = string.Join(
                " ",
                Enumerable.Range(0, 9).Select(i => $"public long L{i} {{ get; set; }}"));

            var source = $$"""
                           using DwarfMapper;
                           using System.Collections.Generic;
                           namespace Demo;
                           public sealed class Order { public long Id { get; set; } }
                           public sealed class OrderDto { {{longs}} }
                           public class C { public List<Order> Rows { get; set; } }
                           public class D { public List<OrderDto> Rows { get; set; } }
                           [DwarfMapper] public partial class M { public partial D Map(C c); }
                           """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("72 bytes as a struct, over the 64-byte line", fixedText, StringComparison.Ordinal);
            Assert.Contains("by 'in'", fixedText, StringComparison.Ordinal);
            Assert.Contains("public readonly record struct OrderDto", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A member initialiser survives the rewrite, and the type gains the parameterless constructor
        ///     CS8983 demands for it. Dropping the initialiser would have compiled too, and would have been the
        ///     fix quietly changing the consumer's data.
        /// </summary>
        [Fact]
        public async Task A_member_initialiser_survives_and_earns_a_constructor()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public string Note { get; set; } }
                                  public sealed class OrderDto { public string Note { get; set; } = ""; }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("= \"\"", fixedText, StringComparison.Ordinal);
            Assert.Contains("public OrderDto()", fixedText, StringComparison.Ordinal);

            GeneratorAssert.EmitsCompilableCode(fixedText);
        }

        /// <summary>
        ///     A public instance FIELD gains <c>readonly</c>. CS8340 — the mirror of the auto-property rule, and
        ///     a shape the classifier accepts, so a fix that only handled properties would emit a broken type
        ///     for it.
        /// </summary>
        [Fact]
        public async Task An_instance_field_gains_readonly()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public long Id; }
                                  public sealed class OrderDto { public long Id; }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("public readonly record struct OrderDto", fixedText, StringComparison.Ordinal);
            Assert.Contains("public readonly long Id;", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     BOTH halves of a partial declaration are rewritten. One half a struct and the other a class is
        ///     CS0261 — a compile error in the consumer's own file produced by a fix that ran without
        ///     complaining, which is the failure class five commits of this round were spent on.
        /// </summary>
        [Fact]
        public async Task Both_halves_of_a_partial_declaration_are_rewritten()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public long Id { get; set; } }
                                  public partial class OrderDto { public long Id { get; set; } }
                                  public partial class OrderDto { public override string ToString() => "dto"; }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            // `readonly` goes BEFORE `partial`, because `partial` must sit immediately before the type
            // keyword — `public partial readonly record struct` does not parse.
            Assert.Equal(2, CountOccurrences(fixedText, "public readonly partial record struct OrderDto"));
            Assert.DoesNotContain("partial class OrderDto", fixedText, StringComparison.Ordinal);

            GeneratorAssert.EmitsCompilableCode(fixedText);
        }

        /// <summary>
        ///     A constructor that only assigns its parameters — the one shape <c>TransferModelShape</c> lets
        ///     through — survives, and the fix does NOT add a second parameterless one beside it. CS8983 asks
        ///     for "an explicitly declared constructor", any constructor, and a fix that added one regardless
        ///     would be writing a member the consumer did not ask for into every DTO that has one.
        /// </summary>
        [Fact]
        public async Task An_assigning_constructor_survives_and_is_not_duplicated()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public long Id { get; set; } }
                                  public sealed class OrderDto
                                  {
                                      public long Id { get; set; } = 1;
                                      public OrderDto(long id) { Id = id; }
                                  }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("public readonly record struct OrderDto", fixedText, StringComparison.Ordinal);
            Assert.Contains("public OrderDto(long id)", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("public OrderDto()", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An attribute list and the XML doc comment above it both survive, and the doc comment survives
        ///     ONCE. The declaration's leading trivia hangs off the attribute list's first token, and the record
        ///     node is given that same trivia — so a fix that did not take it off the attribute list would print
        ///     every converted type's documentation twice.
        /// </summary>
        [Fact]
        public async Task An_attribute_and_the_doc_comment_above_it_survive_exactly_once()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public long Id { get; set; } }
                                  /// <summary>An order, flattened.</summary>
                                  [System.Serializable]
                                  public sealed class OrderDto { public long Id { get; set; } }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("[System.Serializable]", fixedText, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(fixedText, "An order, flattened."));
            Assert.Contains("public readonly record struct OrderDto", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A field that is ALREADY <c>readonly</c> is left alone rather than given the modifier twice —
        ///     CS1004, which a fix that only ever appended would produce on the commonest hand-written
        ///     immutable DTO there is.
        /// </summary>
        [Fact]
        public async Task An_already_readonly_field_does_not_gain_a_second_modifier()
        {
            const string source = """
                                  using DwarfMapper;
                                  using System.Collections.Generic;
                                  namespace Demo;
                                  public sealed class Order { public long Id; }
                                  public sealed class OrderDto { public readonly long Id; }
                                  public class C { public List<Order> Rows { get; set; } }
                                  public class D { public List<OrderDto> Rows { get; set; } }
                                  [DwarfMapper] public partial class M { public partial D Map(C c); }
                                  """;

            var fixedText = await ApplyAsync(source).ConfigureAwait(true);

            Assert.Contains("public readonly long Id;", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("readonly readonly", fixedText, StringComparison.Ordinal);
        }

        // ─── Harness ─────────────────────────────────────────────────────────────

        private static int CountOccurrences(string text, string needle)
        {
            return text.Split([needle], StringSplitOptions.None).Length - 1;
        }

        private Task<string> ApplyAsync(string source, NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            return _fixture.ApplyAsync(source, nullable);
        }

        public void Dispose()
        {
            _fixture.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    ///     The workspace both <c>DWARF103</c> code-fix suites offer the fix in.
    ///     <para>
    ///         Unlike <c>RestateBaseCodeFixTests</c>' throwaway <c>AdhocWorkspace</c> this one carries real
    ///         metadata references and a real nullable context, and is kept alive for the whole test class:
    ///         this fix resolves <c>DocumentationCommentId</c>s against the project's COMPILATION and reads
    ///         members' <c>NullableAnnotation</c>, so a reference-less workspace would make the oblivious test
    ///         pass for the wrong reason and a disposed one would have no compilation to resolve against.
    ///     </para>
    /// </summary>
    internal sealed class ConvertToRecordStructFixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();

        public void Dispose()
        {
            _workspace.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>Runs the generator, then offers the fix for the diagnostic it reported.</summary>
        public async Task<List<CodeAction>> OfferAsync(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable,
            string diagnosticId = "DWARF103")
        {
            var (_, actions) = await OfferWithDocumentAsync(source, nullable, diagnosticId).ConfigureAwait(false);
            return actions;
        }

        public async Task<string> ApplyAsync(
            string source,
            NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            var (document, actions) = await OfferWithDocumentAsync(source, nullable, "DWARF103")
                .ConfigureAwait(false);
            var action = Assert.Single(actions);

            var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;

            return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
        }

        private async Task<(Document Document, List<CodeAction> Actions)> OfferWithDocumentAsync(
            string source,
            NullableContextOptions nullable,
            string diagnosticId)
        {
            var (diagnostics, _) = GeneratorTestHarness.Run(source, nullable);
            var reported = diagnostics.First(d => d.Id == diagnosticId);

            var document = CreateDocument(source, nullable);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, reported, (a, _) => actions.Add(a), CancellationToken.None);
            await new ConvertToRecordStructCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);

            return (document, actions);
        }

        /// <summary>Offers the fix for a diagnostic the caller built, for the refusal cases.</summary>
        public static async Task<List<CodeAction>> OfferForAsync(Document document, Diagnostic diagnostic)
        {
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
            await new ConvertToRecordStructCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);

            return actions;
        }

        /// <summary>The document the fix runs against, for a caller that supplies its own diagnostic.</summary>
        public Document Document(string source, NullableContextOptions nullable = NullableContextOptions.Disable)
        {
            return CreateDocument(source, nullable);
        }

        private Document CreateDocument(string source, NullableContextOptions nullable)
        {
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);

            var solution = _workspace.CurrentSolution
                .AddProject(projectId, "FixAsm" + projectId.Id.ToString("N"), "FixAsm", LanguageNames.CSharp)
                .WithProjectCompilationOptions(
                    projectId,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullable))
                .AddMetadataReferences(projectId, References())
                .AddDocument(documentId, "Dtos.cs", source);

            return solution.GetDocument(documentId)!;
        }

        private static IEnumerable<MetadataReference> References()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location));
        }
    }
}
