// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DwarfMapper.Generator.Tests.CodeFixes
{
    /// <summary>
    ///     End-to-end test of the <c>DWARF085</c> code fix. The generator reports drift, the fix offers the two
    ///     honest answers — restate the base configuration, or say the difference is deliberate — and each fixed
    ///     source then generates with no <c>DWARF085</c>.
    /// </summary>
    /// <remarks>
    ///     Restatement is the mechanical, one-time cost decision R18-D4 chose to keep in exchange for every pair's
    ///     configuration staying visible at its own declaration. A code fix is the right way to pay a mechanical
    ///     cost; Round 18 paid it by hand at 26 sites across three projects.
    /// </remarks>
    public class RestateBaseCodeFixTests
    {
        /// <summary>
        ///     The drift a real codebase produces: the restatement is THERE, it just no longer does the same thing
        ///     — the base gained a converter and this pair kept mapping the raw value. Invisible to any convention
        ///     based on comments, which is why the check compares resolved mappings instead.
        /// </summary>
        private const string Src = """
                                   using DwarfMapper;
                                   namespace Demo;
                                   public class Command { public string Raw { get; set; } = ""; }
                                   public class AliasCommand : Command { public string Alias { get; set; } = ""; }
                                   public class CommandDto { public string Text { get; set; } = ""; }
                                   public class AliasCommandDto : CommandDto { public string Alias { get; set; } = ""; }

                                   [DwarfMapper]
                                   [GenerateMap<Command, CommandDto>]
                                   [MapProperty<Command, CommandDto>(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
                                   [GenerateMap<AliasCommand, AliasCommandDto>]
                                   [RestatesBase<AliasCommand, AliasCommandDto>]
                                   [MapProperty<AliasCommand, AliasCommandDto>(nameof(Command.Raw), nameof(CommandDto.Text))]
                                   public partial class M
                                   {
                                       private static string Clean(string raw) => raw.Trim();
                                   }
                                   """;

        private static async Task<(List<CodeAction> Actions, Document Document, string Text)> OfferAsync()
        {
            var (diags, _) = GeneratorTestHarness.Run(Src);
            var d = diags.First(x => x.Id == "DWARF085");

            using var workspace = new AdhocWorkspace();
            var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", Src);

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, d, (a, _) => actions.Add(a), CancellationToken.None);
            await new RestateBaseConfigurationCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);

            return (actions, document, Src);
        }

        private static async Task<string> ApplyAsync(int index)
        {
            var (actions, document, _) = await OfferAsync().ConfigureAwait(false);
            var operations = await actions[index].GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
            var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
        }

        [Fact]
        public async Task Both_honest_answers_are_offered()
        {
            var (actions, _, _) = await OfferAsync().ConfigureAwait(true);

            Assert.Equal(2, actions.Count);
            Assert.Contains(actions, a => a.Title.Contains("Restate", StringComparison.Ordinal));
            Assert.Contains(actions, a => a.Title.Contains("deliberate override", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Restating_copies_the_base_configuration_and_clears_the_diagnostic()
        {
            var fixedText = await ApplyAsync(0).ConfigureAwait(true);

            // The attribute a person would have typed — retargeted to the derived pair, converter and all.
            Assert.Contains("MapProperty<AliasCommand, AliasCommandDto>", fixedText, StringComparison.Ordinal);
            Assert.Contains("Use = nameof(Clean)", fixedText, StringComparison.Ordinal);

            // No marker comments: what makes the restatement checkable is [RestatesBase] comparing the resolved
            // mappings, not a convention that then has to be preserved by hand.
            Assert.DoesNotContain("MIRRORS BASE", fixedText, StringComparison.Ordinal);

            GeneratorAssert.DoesNotReport(fixedText, "DWARF085");
            GeneratorAssert.CompilesClean(fixedText);
        }

        [Fact]
        public async Task Marking_an_override_clears_the_diagnostic_without_changing_the_mapping()
        {
            var fixedText = await ApplyAsync(1).ConfigureAwait(true);

            Assert.Contains("Overrides", fixedText, StringComparison.Ordinal);
            Assert.Contains("\"Text\"", fixedText, StringComparison.Ordinal);

            GeneratorAssert.DoesNotReport(fixedText, "DWARF085");

            // And it really is an override rather than a restatement — the derived pair still maps Raw straight
            // through, which is the difference the author has just declared they meant.
            var generated = GeneratorAssert.CompilesClean(fixedText);
            Assert.Equal(1, generated.Split("Clean(").Length - 1);
        }
    }
}
