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
    ///     The DWARF085 fix's two independent registrations, and what happens when each one's inputs are absent.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This provider offers two actions from ONE diagnostic, and they have different preconditions: the
    ///         restatement needs a <c>SourcePair</c> property and a readable derived pair on the attribute, while
    ///         the override needs a <c>Member</c>. The existing tests drive the case where both are present and
    ///         assert that two actions appear — which cannot distinguish "the second guard works" from "the
    ///         second guard is missing", because both produce two actions on that input.
    ///     </para>
    ///     <para>
    ///         So these supply each precondition WITHOUT the other. A diagnostic carrying a pair but no member
    ///         must offer exactly one action, and the fix must still be the restatement rather than an override
    ///         of nothing.
    ///     </para>
    /// </remarks>
    public class RestateBaseRefusalTests
    {
        private const string Src = """
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Command { public string Raw { get; set; } = ""; }
                                   public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                   public class CommandDto { public string Text { get; set; } = ""; }
                                   public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                                   [DwarfMapper]
                                   [GenerateMap<Command, CommandDto>]
                                   [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                   [GenerateMap<AliasCommand, AliasCommandDto>]
                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                   [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                   public partial class M
                                   {
                                   }
                                   """;

        private static Diagnostic Dwarf085(TextSpan span, params KeyValuePair<string, string?>[] properties)
        {
            var tree = CSharpSyntaxTree.ParseText(Src);
            return Diagnostic.Create(DiagnosticDescriptors.RestatedBaseDrift,
                Location.Create(tree, span),
                ImmutableDictionary.CreateRange(properties),
                "AliasCommand",
                "AliasCommandDto");
        }

        private static KeyValuePair<string, string?> Prop(string key, string? value)
        {
            return new KeyValuePair<string, string?>(key, value);
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
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(false);
            return actions;
        }

        /// <summary>The span of the derived pair's [MapProperty], which is where the generator points DWARF085.</summary>
        private static TextSpan AtDerivedAttribute()
        {
            return SpanOf("MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))");
        }

        // ── The keys the IDE groups Fix-All by ──────────────────────────────────

        /// <summary>
        ///     Both actions carry their equivalence keys. The titles are asserted elsewhere; the keys are what
        ///     Fix-All groups by, and a blank key breaks that without breaking a single fix.
        /// </summary>
        [Fact]
        public async Task Both_actions_carry_their_equivalence_keys()
        {
            var actions = await OfferedFor(Dwarf085(AtDerivedAttribute(),
                Prop("SourcePair", "Demo.Command|Demo.CommandDto"),
                Prop("Member", "Text")));

            Assert.Equal(2, actions.Count);
            Assert.Equal("DWARF085_Restate", actions[0].EquivalenceKey);
            Assert.Equal("DWARF085_Override", actions[1].EquivalenceKey);
        }

        // ── One precondition at a time ──────────────────────────────────────────

        /// <summary>
        ///     A pair but NO member: the restatement is offered, the override is not. Two actions here would
        ///     mean the member guard had stopped working.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_with_no_member_offers_only_the_restatement()
        {
            var actions = await OfferedFor(Dwarf085(AtDerivedAttribute(),
                Prop("SourcePair", "Demo.Command|Demo.CommandDto")));

            var action = Assert.Single(actions);
            Assert.Equal("DWARF085_Restate", action.EquivalenceKey);
        }

        /// <summary>An EMPTY member is as unusable as an absent one.</summary>
        [Fact]
        public async Task A_diagnostic_with_an_empty_member_offers_only_the_restatement()
        {
            var actions = await OfferedFor(Dwarf085(AtDerivedAttribute(),
                Prop("SourcePair", "Demo.Command|Demo.CommandDto"),
                Prop("Member", "")));

            Assert.Equal("DWARF085_Restate", Assert.Single(actions).EquivalenceKey);
        }

        /// <summary>
        ///     A member but NO pair: the override is offered alone. The restatement has nothing to copy, and
        ///     inventing a base pair here would be a second implementation of the generator's own rule.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_with_no_source_pair_offers_only_the_override()
        {
            var actions = await OfferedFor(Dwarf085(AtDerivedAttribute(), Prop("Member", "Text")));

            Assert.Equal("DWARF085_Override", Assert.Single(actions).EquivalenceKey);
        }

        /// <summary>
        ///     A location with no enclosing ATTRIBUTE: both actions need one — the restatement to read the
        ///     derived pair off it, the override to add an argument to it — so neither is offered.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_outside_any_attribute_offers_no_fix()
        {
            var actions = await OfferedFor(Dwarf085(SpanOf("public class Command"),
                Prop("SourcePair", "Demo.Command|Demo.CommandDto"),
                Prop("Member", "Text")));

            Assert.Empty(actions);
        }

        /// <summary>
        ///     A <c>SourcePair</c> that is PRESENT but empty. Distinct from absent: the property lookup
        ///     succeeds, so only the emptiness check stands between this and a restatement built from no pair
        ///     at all.
        /// </summary>
        [Fact]
        public async Task An_empty_source_pair_offers_only_the_override()
        {
            var actions = await OfferedFor(Dwarf085(AtDerivedAttribute(),
                Prop("SourcePair", ""),
                Prop("Member", "Text")));

            Assert.Equal("DWARF085_Override", Assert.Single(actions).EquivalenceKey);
        }

        /// <summary>
        ///     A <c>SourcePair</c> with no separator is malformed. The fix declines rather than treating the
        ///     whole string as one half of a pair.
        /// </summary>
        [Fact]
        public async Task A_source_pair_with_no_separator_offers_only_the_override()
        {
            var actions = await OfferedFor(Dwarf085(AtDerivedAttribute(),
                Prop("SourcePair", "Demo.Command"),
                Prop("Member", "Text")));

            Assert.Equal("DWARF085_Override", Assert.Single(actions).EquivalenceKey);
        }

        /// <summary>
        ///     When the derived pair ALREADY says exactly what the base says, restating changes nothing and the
        ///     document comes back untouched — not rewritten to an equivalent-but-reformatted version.
        /// </summary>
        [Fact]
        public async Task Restating_a_pair_that_has_not_drifted_leaves_the_document_alone()
        {
            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", Src);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document,
                Dwarf085(AtDerivedAttribute(), Prop("SourcePair", "Demo.Command|Demo.CommandDto")),
                (a, _) => actions.Add(a),
                CancellationToken.None);
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(true);

            var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None)
                .ConfigureAwait(true);

            // No ApplyChangesOperation at all, or one whose document is byte-identical: either way the source
            // is unchanged. The fix returns the ORIGINAL document when there is nothing to restate.
            var apply = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
            if (apply is not null)
            {
                var changed = apply.ChangedSolution.GetDocument(document.Id)!;
                Assert.Equal(Src, (await changed.GetTextAsync().ConfigureAwait(true)).ToString());
            }
        }

        /// <summary>
        ///     A diagnostic pointing at a NON-GENERIC attribute: there is no derived pair to read off it, so no
        ///     restatement is offered. Registering one anyway would give the user an action that silently does
        ///     nothing, because the pair it copies from would match no attribute at all.
        /// </summary>
        [Fact]
        public async Task A_diagnostic_on_a_non_generic_attribute_offers_only_the_override()
        {
            // The ATTRIBUTE occurrence, not the using directive that also reads "DwarfMapper".
            var at = Src.IndexOf("[DwarfMapper]", StringComparison.Ordinal) + 1;
            var actions = await OfferedFor(Dwarf085(new TextSpan(at, "DwarfMapper".Length),
                Prop("SourcePair", "Demo.Command|Demo.CommandDto"),
                Prop("Member", "Text")));

            Assert.Equal("DWARF085_Override", Assert.Single(actions).EquivalenceKey);
        }

        // ── What the override WRITES ────────────────────────────────────────────

        private static async Task<string> OverrideAsync(Diagnostic diagnostic)
        {
            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", Src);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(false);

            var action = Assert.Single(actions, a => a.EquivalenceKey == "DWARF085_Override");
            var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;
            return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
        }

        /// <summary>
        ///     Marking an override ADDS to the attribute; it does not rebuild its argument list. The attribute
        ///     the diagnostic points at already carries the mapping, and losing those arguments would silently
        ///     delete the very configuration the author is annotating.
        /// </summary>
        [Fact]
        public async Task Marking_an_override_keeps_the_attributes_existing_arguments()
        {
            var fixedText = await OverrideAsync(Dwarf085(AtDerivedAttribute(), Prop("Member", "Text")))
                .ConfigureAwait(true);

            // Asserted against the DERIVED attribute specifically. An earlier version of this test looked
            // for the argument text anywhere in the file, which the BASE attribute already contains -- so it
            // passed whether or not the fix preserved anything. A mutation survivor is what exposed it.
            // "AliasCommandDto>(nameof(Command.Raw)" can only occur on the DERIVED attribute -- the base one
            // reads "CommandDto>(nameof(Command.Raw)". If the fix rebuilt the argument list instead of adding
            // to it, those positional arguments are gone and this fails.
            Assert.Contains("AliasCommandDto>(nameof(Command.Raw)", fixedText, StringComparison.Ordinal);
            Assert.Contains("\"Text\"", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     An attribute that ALREADY has an <c>Overrides</c> list gains an element rather than a second
        ///     <c>Overrides</c> argument — which would not compile in the consumer's file.
        /// </summary>
        [Fact]
        public async Task Marking_a_second_override_extends_the_existing_list()
        {
            const string withOverrides = """
                                         using DwarfMapper;
                                         namespace Demo;
                                         public class Command { public string Raw { get; set; } = ""; }
                                         public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                         public class CommandDto { public string Text { get; set; } = ""; }
                                         public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                                         [DwarfMapper]
                                         [GenerateMap<Command, CommandDto>]
                                         [GenerateMap<AliasCommand, AliasCommandDto>]
                                         [RestatesBase<AliasCommand, AliasCommandDto>(Overrides = ["Alias"])]
                                         public partial class M
                                         {
                                         }
                                         """;

            const string at = "RestatesBase<AliasCommand, AliasCommandDto>(Overrides = [\"Alias\"])";
            var i = withOverrides.IndexOf(at, StringComparison.Ordinal);
            Assert.True(i >= 0);

            var tree = CSharpSyntaxTree.ParseText(withOverrides);
            var diagnostic = Diagnostic.Create(DiagnosticDescriptors.RestatedBaseDrift,
                Location.Create(tree, new TextSpan(i, at.Length)),
                ImmutableDictionary.CreateRange([new KeyValuePair<string, string?>("Member", "Text")]),
                "AliasCommand",
                "AliasCommandDto");

            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", withOverrides);
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(true);

            var action = Assert.Single(actions, a => a.EquivalenceKey == "DWARF085_Override");
            var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(true);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;
            var fixedText = (await changed.GetTextAsync().ConfigureAwait(true)).ToString();

            // One Overrides argument, now naming both members.
            Assert.Equal(1, fixedText.Split("Overrides =").Length - 1);
            Assert.Contains("\"Alias\"", fixedText, StringComparison.Ordinal);
            Assert.Contains("\"Text\"", fixedText, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The same, when the existing <c>Overrides</c> is written as an IMPLICIT ARRAY —
        ///     <c>Overrides = new[] { "Alias" }</c>, the form older code and older language versions use. The fix must
        ///     add to that initializer rather than append a second <c>Overrides</c> argument, which would not compile.
        /// </summary>
        [Fact]
        public async Task Marking_a_second_override_extends_an_existing_implicit_array()
        {
            const string withOverrides = """
                                         using DwarfMapper;
                                         namespace Demo;
                                         public class Command { public string Raw { get; set; } = ""; }
                                         public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                         public class CommandDto { public string Text { get; set; } = ""; }
                                         public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                                         [DwarfMapper]
                                         [GenerateMap<Command, CommandDto>]
                                         [GenerateMap<AliasCommand, AliasCommandDto>]
                                         [RestatesBase<AliasCommand, AliasCommandDto>(Overrides = new[] { "Alias" })]
                                         public partial class M
                                         {
                                         }
                                         """;

            const string at = "RestatesBase<AliasCommand, AliasCommandDto>(Overrides = new[] { \"Alias\" })";
            var i = withOverrides.IndexOf(at, StringComparison.Ordinal);
            Assert.True(i >= 0);

            var tree = CSharpSyntaxTree.ParseText(withOverrides);
            var diagnostic = Diagnostic.Create(DiagnosticDescriptors.RestatedBaseDrift,
                Location.Create(tree, new TextSpan(i, at.Length)),
                ImmutableDictionary.CreateRange([new KeyValuePair<string, string?>("Member", "Text")]),
                "AliasCommand",
                "AliasCommandDto");

            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", withOverrides);
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context)
                .ConfigureAwait(true);

            var action = Assert.Single(actions, a => a.EquivalenceKey == "DWARF085_Override");
            var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(true);
            var changed = operations.OfType<ApplyChangesOperation>().Single()
                .ChangedSolution.GetDocument(document.Id)!;
            var fixedText = (await changed.GetTextAsync().ConfigureAwait(true)).ToString();

            // Still ONE Overrides argument, still an implicit array, now naming both members.
            Assert.Equal(1, fixedText.Split("Overrides =").Length - 1);
            Assert.Contains("new[]", fixedText, StringComparison.Ordinal);
            Assert.Contains("\"Alias\"", fixedText, StringComparison.Ordinal);
            Assert.Contains("\"Text\"", fixedText, StringComparison.Ordinal);
        }

        [Fact]
        public void Fix_all_is_provided_by_the_batch_fixer()
        {
            var provider = new RestateBaseConfigurationCodeFixProvider().GetFixAllProvider();

            Assert.NotNull(provider);
            Assert.Same(WellKnownFixAllProviders.BatchFixer, provider);
        }

        /// <summary>
        ///     The fix answers to <c>DWARF085</c> and to nothing else — the exact set: a second id would offer to rewrite a
        ///     class's pair-scoped attributes on a diagnostic that never compared a restatement with its base.
        /// </summary>
        [Fact]
        public void The_fix_is_registered_for_DWARF085_alone()
        {
            var ids = new RestateBaseConfigurationCodeFixProvider().FixableDiagnosticIds;

            Assert.Equal("DWARF085", Assert.Single(ids));
        }
    }
}
