// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DwarfMapper.CodeFixes;

/// <summary>
///     Code fix for <c>DWARF052</c> (<c>[ReverseMap]</c> has no inverse mapping method). The diagnostic asks the
///     user to declare the inverse; this inserts that declaration for them. From the forward method
///     <c>{Dto} ToX({Entity} e)</c> it scaffolds <c>public partial {Entity} From{Dto}({Dto} source);</c> into the
///     same mapper class — which the generator then implements (inheriting the inverted simple renames). Pure
///     syntax transformation (no semantic model): the forward method's own return/parameter type syntax is
///     reused verbatim, so the inserted declaration always type-checks. The user is free to rename it.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddReverseMapInverseCodeFixProvider))]
[Shared]
public sealed class AddReverseMapInverseCodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "DWARF052";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var forward = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (forward is null
                || forward.Parent is not ClassDeclarationSyntax
                || forward.ParameterList.Parameters.Count == 0
                || forward.ParameterList.Parameters[0].Type is null)
                continue;

            var captured = forward;
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add [ReverseMap] inverse method",
                    _ => Task.FromResult(WithInverseMethod(context.Document, root, captured)),
                    "DWARF052_AddInverseMethod"),
                diagnostic);
        }
    }

    private static Document WithInverseMethod(Document document, SyntaxNode root, MethodDeclarationSyntax forward)
    {
        var classDecl = (ClassDeclarationSyntax)forward.Parent!;
        var dtoType = forward.ReturnType; // forward returns the DTO
        var entityType = forward.ParameterList.Parameters[0].Type!; // forward takes the entity

        var inverse = SyntaxFactory
            .MethodDeclaration(entityType.WithoutTrivia(), InverseName(dtoType))
            .AddModifiers(
                SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.PartialKeyword))
            .AddParameterListParameters(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("source")).WithType(dtoType.WithoutTrivia()))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            // Raw SyntaxFactory nodes carry no whitespace — NormalizeWhitespace makes the declaration
            // well-formed C# (correct inter-token spacing) so it compiles regardless of host formatting;
            // the leading/trailing newlines put it on its own line; Formatter.Annotation lets the IDE
            // re-indent it to the surrounding style when the fix is applied.
            .NormalizeWhitespace()
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newClass = classDecl.InsertNodesAfter(forward, new SyntaxNode[] { inverse });
        return document.WithSyntaxRoot(root.ReplaceNode(classDecl, newClass));
    }

    // "Demo.PersonDto" / "global::Demo.PersonDto" / "PersonDto<T>" → "FromPersonDto".
    private static string InverseName(TypeSyntax dtoType)
    {
        var name = dtoType.ToString();
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name.Substring(dot + 1);

        var generic = name.IndexOf('<');
        if (generic >= 0) name = name.Substring(0, generic);

        return "From" + name;
    }
}
