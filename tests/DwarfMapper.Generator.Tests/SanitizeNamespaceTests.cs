// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-008 — the assembly-wide aggregate files put the assembly name in a `namespace` declaration, after
///     sanitising it. The sanitiser replaced non-identifier characters and prefixed leading digits, but did NOT
///     handle segments that are C# KEYWORDS: an assembly legitimately named "Acme.Class" produced
///     `namespace Acme.class;`, which does not parse — a raw compiler error out of generated code.
/// </summary>
public class SanitizeNamespaceTests
{
    private const string Source = """
                                  using DwarfMapper;
                                  namespace Demo;
                                  public class A { public int X { get; set; } }
                                  public class B { public int X { get; set; } }
                                  [DwarfMapper]
                                  public partial class M { public partial B Map(A a); }
                                  """;

    [Theory]
    [InlineData("Acme.class")]     // reserved keyword
    [InlineData("Acme.namespace")]
    [InlineData("Acme.int")]
    [InlineData("Acme.value")]     // contextual keyword
    [InlineData("Acme.Class")]     // NOT a keyword — the check is case-sensitive; must be left alone
    [InlineData("Acme.Data")]      // ordinary segment — control
    public void Keyword_assembly_name_segments_produce_a_parseable_namespace(string assemblyName)
    {
        var generated = GeneratorTestHarness.RunAndGetSource(
            Source, "DwarfMapper.ServiceCollectionExtensions.g.cs", assemblyName: assemblyName);

        Assert.NotEmpty(generated);

        // Whatever the sanitiser produces must parse as a namespace declaration.
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(generated);
        Assert.DoesNotContain(tree.GetDiagnostics(),
            d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
