// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Linq;
using DwarfMapper.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DwarfMapper.Generator.Tests.CodeFixes;

/// <summary>
/// End-to-end test of the DWARF072 code fix (explicit-only trust-boundary member). The generator reports the
/// diagnostic, the fix offers "Map with [MapProperty]" and "Ignore with [MapIgnore]", and each fixed source
/// then generates with no DWARF072 and no compiler errors.
/// </summary>
public class ExplicitOnlyCodeFixTests
{
    private const string Src = """
        using DwarfMapper;
        namespace Demo;
        public class Input { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
        public class Entity { public string Name { get; set; } = ""; public bool IsAdmin { get; set; } }
        [DwarfMapper(AutoMatchMembers = false)]
        public partial class M
        {
            [MapProperty(nameof(Input.Name), nameof(Entity.Name))]
            public partial Entity Map(Input input);
        }
        """;

    private static async Task<string> ApplyAsync(int actionIndex)
    {
        var (diags, _) = GeneratorTestHarness.Run(Src);
        // The IsAdmin member is the still-unresolved one (Name is explicitly mapped).
        var d = diags.First(x => x.Id == "DWARF072"
                                 && x.GetMessage(CultureInfo.InvariantCulture).Contains("IsAdmin", System.StringComparison.Ordinal));

        using var workspace = new AdhocWorkspace();
        var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", Src);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, d, (a, _) => actions.Add(a), CancellationToken.None);
        await new ResolveExplicitOnlyMemberCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);

        Assert.Equal(2, actions.Count); // Map it / Ignore it
        var operations = await actions[actionIndex].GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        return (await changed.GetTextAsync().ConfigureAwait(false)).ToString();
    }

    [Fact]
    public async Task Map_action_inserts_MapProperty_and_resolves_the_diagnostic()
    {
        var fixedText = await ApplyAsync(0);

        Assert.Contains("MapProperty", fixedText, System.StringComparison.Ordinal);
        Assert.Contains("IsAdmin", fixedText, System.StringComparison.Ordinal);

        var (afterDiags, _) = GeneratorTestHarness.Run(fixedText);
        Assert.DoesNotContain(afterDiags, x => x.Id == "DWARF072" &&
            x.GetMessage(CultureInfo.InvariantCulture).Contains("IsAdmin", System.StringComparison.Ordinal));
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(fixedText));
    }

    [Fact]
    public async Task Ignore_action_inserts_MapIgnore_and_resolves_the_diagnostic()
    {
        var fixedText = await ApplyAsync(1);

        Assert.Contains("MapIgnore", fixedText, System.StringComparison.Ordinal);

        var (afterDiags, _) = GeneratorTestHarness.Run(fixedText);
        Assert.DoesNotContain(afterDiags, x => x.Id == "DWARF072" &&
            x.GetMessage(CultureInfo.InvariantCulture).Contains("IsAdmin", System.StringComparison.Ordinal));
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(fixedText));
    }

    [Fact]
    public void Provider_advertises_only_DWARF072()
    {
        var ids = new ResolveExplicitOnlyMemberCodeFixProvider().FixableDiagnosticIds;
        Assert.Equal("DWARF072", Assert.Single(ids));
    }
}
