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
    ///     The DWARF001 fix's REFUSALS and its user-visible strings — the half an end-to-end happy-path test
    ///     cannot reach.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Round 27 put this project under mutation testing for the first time and it scored 50.0 % here:
    ///         twelve undetected mutants in ninety-three lines. The pattern was not subtle. One test drove the
    ///         provider, and it drove the case where everything works — so every guard that declines to offer a
    ///         fix, and every string the user actually sees, could be changed to anything at all without a test
    ///         noticing.
    ///     </para>
    ///     <para>
    ///         These drive the provider with SYNTHETIC diagnostics rather than through the generator, because
    ///         the refusals are reached through the diagnostic's property bag and its location, and the
    ///         generator by construction only ever produces the shapes that succeed. Building the diagnostic
    ///         directly is what makes "the property is missing", "the property is empty" and "the location is
    ///         not in a method" reachable at all.
    ///     </para>
    /// </remarks>
    public class AddMapIgnoreCodeFixTests
    {
        private const string Src = """
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Src { public int Id { get; set; } }
                                   public class Dst { public int Id { get; set; } public string Extra { get; set; } = ""; }
                                   [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                                   """;

        /// <summary>A DWARF001 whose property bag and location are chosen by the caller.</summary>
        private static Diagnostic Dwarf001(TextSpan span, params KeyValuePair<string, string?>[] properties)
        {
            var tree = CSharpSyntaxTree.ParseText(Src);
            return Diagnostic.Create(DiagnosticDescriptors.UnmappedMember,
                Location.Create(tree, span),
                ImmutableDictionary.CreateRange(properties),
                "Extra");
        }

        private static KeyValuePair<string, string?> Member(string? value)
        {
            return new KeyValuePair<string, string?>("Member", value);
        }

        /// <summary>The span of <paramref name="text" /> inside the test source.</summary>
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
            await new AddMapIgnoreCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);
            return actions;
        }

        // ── What the user sees ──────────────────────────────────────────────────

        /// <summary>
        ///     The action's title names the member. It is the whole of what the lightbulb shows, so nothing else
        ///     in the system would notice if it became empty.
        /// </summary>
        [Fact]
        public async Task The_action_title_names_the_member_being_ignored()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("Map(Src s)"), Member("Extra")));

            var action = Assert.Single(actions);
            Assert.Equal("Add [MapIgnore(\"Extra\")]", action.Title);
        }

        /// <summary>
        ///     The equivalence key is what lets Fix-All group these actions. An empty or changed key does not
        ///     break a single fix, so only an assertion on the key itself can hold it.
        /// </summary>
        [Fact]
        public async Task The_action_carries_its_equivalence_key()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("Map(Src s)"), Member("Extra")));

            Assert.Equal("DWARF001_AddMapIgnore", Assert.Single(actions).EquivalenceKey);
        }

        /// <summary>Fix-All is the batch fixer; without it the IDE offers no "fix all occurrences".</summary>
        [Fact]
        public void Fix_all_is_provided_by_the_batch_fixer()
        {
            var provider = new AddMapIgnoreCodeFixProvider().GetFixAllProvider();

            Assert.NotNull(provider);
            Assert.Same(WellKnownFixAllProviders.BatchFixer, provider);
        }

        // ── The refusals ────────────────────────────────────────────────────────

        /// <summary>
        ///     A DOTTED member is a nested target, and <c>[MapIgnore]</c> addresses a top-level destination
        ///     member only — so no fix is offered rather than a wrong one.
        /// </summary>
        [Fact]
        public async Task A_dotted_member_offers_no_fix()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("Map(Src s)"), Member("Address.City")));

            Assert.Empty(actions);
        }

        /// <summary>
        ///     A name whose dot is at index ZERO is still dotted. This pins the boundary: the check is
        ///     "contains a dot anywhere", not "contains a dot after the first character".
        /// </summary>
        [Fact]
        public async Task A_member_whose_dot_is_the_first_character_offers_no_fix()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("Map(Src s)"), Member(".City")));

            Assert.Empty(actions);
        }

        /// <summary>An EMPTY member property is as unusable as a missing one.</summary>
        [Fact]
        public async Task An_empty_member_property_offers_no_fix()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("Map(Src s)"), Member("")));

            Assert.Empty(actions);
        }

        /// <summary>
        ///     No <c>Member</c> property at all — the shape a diagnostic from an older generator, or a
        ///     reworded one, would arrive in. The fix declines instead of guessing from the message text, which
        ///     is the defect ISSUE-028 removed.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_with_no_member_property_offers_no_fix()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("Map(Src s)")));

            Assert.Empty(actions);
        }

        /// <summary>
        ///     A location that is not inside a method declaration: there is nothing to attach the attribute to,
        ///     so the fix declines rather than attaching it somewhere arbitrary.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_outside_any_method_offers_no_fix()
        {
            var actions = await OfferedFor(Dwarf001(SpanOf("using DwarfMapper;"), Member("Extra")));

            Assert.Empty(actions);
        }
    }
}
