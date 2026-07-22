// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DwarfMapper.Generator.Tests.CodeFixes;

/// <summary>
///     End-to-end test of the DWARF052 code fix: the generator reports a missing [ReverseMap] inverse, the fix
///     inserts the inverse method declaration, and the FIXED source then generates cleanly with no DWARF052.
///     The diagnostic comes from the source GENERATOR (not a DiagnosticAnalyzer), so the standard
///     CodeFixVerifier&lt;TAnalyzer,TCodeFix&gt; harness doesn't apply — we drive the provider directly. The
///     diagnostic's SourceSpan is text-offset based, so it maps onto an identical-text Document even though the
///     generator produced it against its own compilation.
/// </summary>
public class ReverseMapCodeFixTests
{
    private const string Src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Entity { public int Id { get; set; } public string FullName { get; set; } = ""; }
                               public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                               [DwarfMapper] public partial class M
                               {
                                   [ReverseMap]
                                   [MapProperty(nameof(Entity.FullName), nameof(Dto.Name))]
                                   public partial Dto ToDto(Entity e);
                               }
                               """;

    private const string UnmappedSrc = """
                                       using DwarfMapper;
                                       namespace Demo;
                                       public class Src { public int Id { get; set; } }
                                       public class Dst { public int Id { get; set; } public string Extra { get; set; } = ""; }
                                       [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
                                       """;

    [Fact]
    public async Task DWARF052_fix_inserts_an_inverse_that_resolves_the_diagnostic()
    {
        var (diags, _) = GeneratorTestHarness.Run(Src);
        var d052 = diags.Single(d => d.Id == "DWARF052");

        using var workspace = new AdhocWorkspace();
        var document = workspace
            .AddProject("FixAsm", LanguageNames.CSharp)
            .AddDocument("M.cs", Src);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, d052,
            (action, _) => actions.Add(action), CancellationToken.None);
        await new AddReverseMapInverseCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.NotEmpty(actions);

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single()
            .ChangedSolution.GetDocument(document.Id)!;
        var fixedText = (await changed.GetTextAsync()).ToString();

        // The fix scaffolded a partial inverse: public partial Entity FromDto(Dto source);
        Assert.Contains("partial Entity FromDto", fixedText, StringComparison.Ordinal);
        Assert.Contains("Dto source", fixedText, StringComparison.Ordinal);

        // The crucial assertion: the FIXED source now generates with no DWARF052 (and no errors).
        var (afterDiags, _) = GeneratorTestHarness.Run(fixedText);
        Assert.DoesNotContain(afterDiags, d => d.Id == "DWARF052");
        GeneratorAssert.EmitsCompilableCode(fixedText);
    }

    [Fact]
    public void Provider_advertises_only_DWARF052()
    {
        var ids = new AddReverseMapInverseCodeFixProvider().FixableDiagnosticIds;
        Assert.Equal("DWARF052", Assert.Single(ids));
    }

    [Fact]
    public async Task DWARF001_fix_inserts_MapIgnore_that_resolves_the_diagnostic()
    {
        var (diags, _) = GeneratorTestHarness.Run(UnmappedSrc);
        var d001 = diags.Single(d => d.Id == "DWARF001");

        using var workspace = new AdhocWorkspace();
        var document = workspace.AddProject("FixAsm", LanguageNames.CSharp).AddDocument("M.cs", UnmappedSrc);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, d001, (a, _) => actions.Add(a), CancellationToken.None);
        await new AddMapIgnoreCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.NotEmpty(actions);
        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var fixedText = (await changed.GetTextAsync()).ToString();

        Assert.Contains("MapIgnore", fixedText, StringComparison.Ordinal);
        Assert.Contains("Extra", fixedText, StringComparison.Ordinal);

        // The fixed source must now compile with no DWARF001 and no errors.
        var (afterDiags, _) = GeneratorTestHarness.Run(fixedText);
        Assert.DoesNotContain(afterDiags, d => d.Id == "DWARF001");
        GeneratorAssert.EmitsCompilableCode(fixedText);
    }
}
