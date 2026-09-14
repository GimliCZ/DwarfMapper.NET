// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using DwarfMapper.CodeFixes;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DwarfMapper.Generator.Tests.CodeFixes
{
    /// <summary>
    ///     The DWARF052 fix's refusals, its user-visible strings, and the NAME it derives for the inverse.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This provider scored 40.0 % — the worst of the four — when the code-fix project was first
    ///         mutation-tested in round 27. One end-to-end test covered the shape where everything works, so
    ///         both guards and the whole of the name-derivation could be changed freely.
    ///     </para>
    ///     <para>
    ///         The name derivation is where a silent defect would hurt most: it strips a namespace qualifier and
    ///         a generic argument list off the DTO type to build an identifier. Get either wrong and the fix
    ///         inserts a method called <c>FromDemo.PersonDto</c> or <c>FromWrapper&lt;int&gt;</c> — not a
    ///         compile error in the fix, a compile error in the CONSUMER'S file, produced by the tool that was
    ///         supposed to repair it.
    ///     </para>
    /// </remarks>
    public class AddReverseMapInverseRefusalTests
    {
        private static Diagnostic Dwarf052(string source, TextSpan span)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            return Diagnostic.Create(DiagnosticDescriptors.ReverseMapTargetMissing,
                Location.Create(tree, span),
                ImmutableDictionary<string, string?>.Empty,
                "ToDto");
        }

        private static TextSpan SpanOf(string source, string text)
        {
            var i = source.IndexOf(text, StringComparison.Ordinal);
            Assert.True(i >= 0, $"test source does not contain '{text}'");
            return new TextSpan(i, text.Length);
        }

        private static async Task<(List<CodeAction> Actions, Document Document)> OfferedFor(string source, string at)
        {
            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", source);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document,
                Dwarf052(source, SpanOf(source, at)),
                (a, _) => actions.Add(a),
                CancellationToken.None);
            await new AddReverseMapInverseCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);
            return (actions, document);
        }

        private static async Task<string> ApplyAsync(List<CodeAction> actions, Document document)
        {
            var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)
                .ConfigureAwait(false);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;
            return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
        }

        // ── The derived name ────────────────────────────────────────────────────

        /// <summary>
        ///     A NAMESPACE-QUALIFIED DTO type must contribute only its last segment. Without the strip the fix
        ///     emits <c>FromDemo.PersonDto</c>, which is not a legal method name.
        /// </summary>
        [Fact]
        public async Task A_qualified_dto_type_contributes_only_its_last_segment()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Person { public int Id { get; set; } }
                               public class PersonDto { public int Id { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [ReverseMap]
                                   public partial Demo.PersonDto ToDto(Person p);
                               }
                               """;

            var (actions, document) = await OfferedFor(src, "ToDto(Person p)").ConfigureAwait(true);
            var fixedText = await ApplyAsync(actions, document).ConfigureAwait(true);

            Assert.Contains("FromPersonDto", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("FromDemo.PersonDto", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A GENERIC DTO type must lose its argument list. Without the strip the fix emits
        ///     <c>FromWrapper&lt;int&gt;</c> as a method NAME.
        /// </summary>
        [Fact]
        public async Task A_generic_dto_type_loses_its_argument_list()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Person { public int Id { get; set; } }
                               public class Wrapper<T> { public T Value { get; set; } = default!; }
                               [DwarfMapper] public partial class M
                               {
                                   [ReverseMap]
                                   public partial Wrapper<int> ToDto(Person p);
                               }
                               """;

            var (actions, document) = await OfferedFor(src, "ToDto(Person p)").ConfigureAwait(true);
            var fixedText = await ApplyAsync(actions, document).ConfigureAwait(true);

            Assert.Contains("FromWrapper", fixedText, StringComparison.Ordinal);
            Assert.DoesNotContain("FromWrapper<", fixedText, StringComparison.Ordinal);
        }

        // ── What the user sees ──────────────────────────────────────────────────

        [Fact]
        public async Task The_action_title_is_the_one_shown_in_the_lightbulb()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Person { public int Id { get; set; } }
                               public class PersonDto { public int Id { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [ReverseMap]
                                   public partial PersonDto ToDto(Person p);
                               }
                               """;

            var (actions, _) = await OfferedFor(src, "ToDto(Person p)").ConfigureAwait(true);

            var action = Assert.Single(actions);
            Assert.Equal("Add [ReverseMap] inverse method", action.Title);
            Assert.Equal("DWARF052_AddInverseMethod", action.EquivalenceKey);
        }

        [Fact]
        public void Fix_all_is_provided_by_the_batch_fixer()
        {
            var provider = new AddReverseMapInverseCodeFixProvider().GetFixAllProvider();

            Assert.NotNull(provider);
            Assert.Same(WellKnownFixAllProviders.BatchFixer, provider);
        }

        // ── The refusals ────────────────────────────────────────────────────────

        /// <summary>
        ///     The forward method must live in a CLASS. The fix rebuilds its parent as a
        ///     <c>ClassDeclarationSyntax</c>, so offering it anywhere else would produce an action that throws
        ///     when the user clicks it.
        /// </summary>
        [Fact]
        public async Task A_forward_method_that_is_not_in_a_class_offers_no_fix()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Person { public int Id { get; set; } }
                               public class PersonDto { public int Id { get; set; } }
                               public partial struct M
                               {
                                   public partial PersonDto ToDto(Person p);
                               }
                               """;

            var (actions, _) = await OfferedFor(src, "ToDto(Person p)").ConfigureAwait(true);

            Assert.Empty(actions);
        }

        /// <summary>
        ///     A PARAMETERLESS forward method has no entity type to invert to. The guard must short-circuit on
        ///     the count before indexing, or reading the first parameter throws.
        /// </summary>
        [Fact]
        public async Task A_forward_method_with_no_parameters_offers_no_fix()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class PersonDto { public int Id { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [ReverseMap]
                                   public partial PersonDto ToDto();
                               }
                               """;

            var (actions, _) = await OfferedFor(src, "ToDto()").ConfigureAwait(true);

            Assert.Empty(actions);
        }

        /// <summary>
        ///     A forward method whose first parameter has NO type offers nothing. <c>__arglist</c> is the one parameter a
        ///     method declaration can carry without a type, and the inverse is built from that type, so there is nothing
        ///     to build it from — a fix offered anyway would emit an inverse method taking a missing type.
        /// </summary>
        [Fact]
        public async Task A_forward_method_whose_first_parameter_has_no_type_offers_no_fix()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class PersonDto { public int Id { get; set; } }
                               [DwarfMapper] public partial class M
                               {
                                   [ReverseMap]
                                   public PersonDto ToDto(__arglist) => new();
                               }
                               """;

            var (actions, _) = await OfferedFor(src, "ToDto(__arglist)").ConfigureAwait(true);

            Assert.Empty(actions);
        }
    }
}
