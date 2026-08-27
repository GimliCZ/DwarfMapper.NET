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
    ///     The DWARF072 fix's refusals and the two titles it offers.
    /// </summary>
    /// <remarks>
    ///     This provider registers TWO actions for one diagnostic — map the member, or ignore it — and it was
    ///     the strings that went untested: four of its undetected mutants blanked a title or an equivalence key.
    ///     A blank title is not a crash; it is an empty entry in the user's lightbulb menu, and one of two
    ///     otherwise identical-looking entries at that. Only an assertion on the strings can hold them.
    /// </remarks>
    public class ExplicitOnlyRefusalTests
    {
        private const string Src = """
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                   public class Dst { public int Id { get; set; } public string Name { get; set; } = ""; }
                                   [DwarfMapper(ExplicitOnly = true)] public partial class M { public partial Dst Map(Src s); }
                                   """;

        private static Diagnostic Dwarf072(TextSpan span, params KeyValuePair<string, string?>[] properties)
        {
            var tree = CSharpSyntaxTree.ParseText(Src);
            return Diagnostic.Create(DiagnosticDescriptors.AutoMatchDisabled,
                Location.Create(tree, span),
                ImmutableDictionary.CreateRange(properties),
                "Name");
        }

        private static KeyValuePair<string, string?> Member(string? value)
        {
            return new KeyValuePair<string, string?>("Member", value);
        }

        private static TextSpan SpanOf(string text)
        {
            var i = Src.IndexOf(text, StringComparison.Ordinal);
            Assert.True(i >= 0, $"test source does not contain '{text}'");
            return new TextSpan(i, text.Length);
        }

        private static async Task<List<CodeAction>> OfferedFor(Diagnostic diagnostic)
        {
            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", Src);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
            await new ResolveExplicitOnlyMemberCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(false);
            return actions;
        }

        // ── What the user sees ──────────────────────────────────────────────────

        /// <summary>
        ///     BOTH actions, in order, with their titles and equivalence keys. The two are offered together and
        ///     differ only by their text, so a blanked title leaves the user choosing between two menu entries
        ///     that do different things and look the same.
        /// </summary>
        [Fact]
        public async Task Both_actions_are_offered_and_say_which_is_which()
        {
            var actions = await OfferedFor(Dwarf072(SpanOf("Map(Src s)"), Member("Name")));

            Assert.Equal(2, actions.Count);
            Assert.Equal("Map 'Name' with [MapProperty]", actions[0].Title);
            Assert.Equal("DWARF072_AddMapProperty", actions[0].EquivalenceKey);
            Assert.Equal("Ignore 'Name' with [MapIgnore]", actions[1].Title);
            Assert.Equal("DWARF072_AddMapIgnore", actions[1].EquivalenceKey);
        }

        [Fact]
        public void Fix_all_is_provided_by_the_batch_fixer()
        {
            var provider = new ResolveExplicitOnlyMemberCodeFixProvider().GetFixAllProvider();

            Assert.NotNull(provider);
            Assert.Same(WellKnownFixAllProviders.BatchFixer, provider);
        }

        // ── The refusals ────────────────────────────────────────────────────────

        /// <summary>A dotted member is a nested target; neither attribute addresses one.</summary>
        [Fact]
        public async Task A_dotted_member_offers_no_fix()
        {
            Assert.Empty(await OfferedFor(Dwarf072(SpanOf("Map(Src s)"), Member("Address.City"))));
        }

        /// <summary>The boundary: a dot at index zero is still a dot.</summary>
        [Fact]
        public async Task A_member_whose_dot_is_the_first_character_offers_no_fix()
        {
            Assert.Empty(await OfferedFor(Dwarf072(SpanOf("Map(Src s)"), Member(".City"))));
        }

        [Fact]
        public async Task An_empty_member_property_offers_no_fix()
        {
            Assert.Empty(await OfferedFor(Dwarf072(SpanOf("Map(Src s)"), Member(""))));
        }

        [Fact]
        public async Task A_diagnostic_with_no_member_property_offers_no_fix()
        {
            Assert.Empty(await OfferedFor(Dwarf072(SpanOf("Map(Src s)"))));
        }

        /// <summary>Nothing to attach an attribute to, so nothing is offered.</summary>
        [Fact]
        public async Task A_diagnostic_outside_any_method_offers_no_fix()
        {
            Assert.Empty(await OfferedFor(Dwarf072(SpanOf("using DwarfMapper;"), Member("Name"))));
        }
    }
}
