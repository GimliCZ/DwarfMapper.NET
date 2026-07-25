// SPDX-License-Identifier: GPL-2.0-only

using System.Linq;
using DwarfMapper.Generator.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     ISSUE-040 (external audit round 6): <see cref="MemberFacts.Readable" />'s interface branch must apply the
///     SAME accessor-usability gate its class branch does. A C# 8+ interface can declare non-public default
///     interface members; the interface branch used to gate only on <c>GetMethod is not null</c> and skipped
///     <c>AccessorUsable</c>, so a non-public member was enumerated as readable and the generator emitted a read
///     of it — CS0122 in generated code. Since MemberFacts is the single enumeration shared by both engines, the
///     leak affected both — the exact divergence the extraction was meant to end, reintroduced within one method.
///     Tested at the unit level (the enumeration itself), which is precise and independent of downstream
///     emission/completeness behaviour.
/// </summary>
public class InterfaceSourceAccessibilityTests
{
    private const string Source = """
                                  namespace Demo;
                                  public interface ISrc
                                  {
                                      int Id { get; }
                                      private int Secret => 42;   // C#8 private default interface member
                                      internal int Hidden => 7;   // internal default interface member
                                  }
                                  """;

    [Fact]
    public void Interface_readable_excludes_non_public_default_interface_members()
    {
        var (_, iface) = BuildInterface();

        var names = MemberFacts.Readable(iface).Select(m => m.Name).ToList();

        Assert.Contains("Id", names); // public member still enumerated
        Assert.DoesNotContain("Secret", names); // private DIM excluded
        Assert.DoesNotContain("Hidden", names); // internal DIM excluded without AllowNonPublic
    }

    [Fact]
    public void Interface_readable_includes_internal_member_only_with_AllowNonPublic_same_assembly()
    {
        var (compilation, iface) = BuildInterface();

        // allowNonPublic + the member's own (test) assembly → internal is reachable; private never is. Proves the
        // fix is the accessor gate, not a blanket "drop everything non-public".
        var names = MemberFacts.Readable(iface, compilation, allowNonPublic: true).Select(m => m.Name).ToList();

        Assert.Contains("Id", names);
        Assert.Contains("Hidden", names); // internal, same assembly, opted in
        Assert.DoesNotContain("Secret", names); // private stays unreachable even with the flag
    }

    private static (Compilation Compilation, INamedTypeSymbol Interface) BuildInterface()
    {
        var tree = CSharpSyntaxTree.ParseText(Source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));

        var compilation = CSharpCompilation.Create(
            "MemberFactsIfaceTestAsm_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(tree);
        var decl = tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();
        var iface = (INamedTypeSymbol)model.GetDeclaredSymbol(decl)!;
        return (compilation, iface);
    }
}
