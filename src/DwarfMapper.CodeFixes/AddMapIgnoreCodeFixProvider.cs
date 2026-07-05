// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace DwarfMapper.CodeFixes;

/// <summary>
///     Code fix for <c>DWARF001</c> (a destination member has no source). The completeness gate requires the
///     member be either mapped or explicitly ignored; this offers the "ignore it" half — inserting
///     <c>[MapIgnore("Member")]</c> on the mapping method (the message text already names exactly this fix).
///     The member name is read from the diagnostic message (it is quoted: <c>… member 'Member' …</c>). Nested
///     (dotted) targets are skipped — <c>[MapIgnore]</c> addresses a top-level destination member only.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddMapIgnoreCodeFixProvider))]
[Shared]
public sealed class AddMapIgnoreCodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "DWARF001";

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
            var member = ExtractMemberName(diagnostic);
            if (member is null) continue; // dotted/nested target, or unexpected message shape — don't offer a wrong fix

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method is null) continue;

            var captured = method;
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add [MapIgnore(\"{member}\")]",
                    _ => Task.FromResult(WithMapIgnore(context.Document, root, captured, member)),
                    "DWARF001_AddMapIgnore"),
                diagnostic);
        }
    }

    private static Document WithMapIgnore(Document document, SyntaxNode root, MethodDeclarationSyntax method,
        string member)
    {
        var generator = SyntaxGenerator.GetGenerator(document);
        var attribute = generator.Attribute("global::DwarfMapper.MapIgnore", generator.LiteralExpression(member));
        var newMethod = generator.AddAttributes(method, attribute);
        return document.WithSyntaxRoot(root.ReplaceNode(method, newMethod));
    }

    // "Destination member 'Member' has no matching source member; …" → "Member" (null if dotted/nested).
    private static string? ExtractMemberName(Diagnostic diagnostic)
    {
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        var open = message.IndexOf('\'');
        if (open < 0) return null;

        var close = message.IndexOf('\'', open + 1);
        if (close < 0) return null;

        var name = message.Substring(open + 1, close - open - 1);
        return name.IndexOf('.') >= 0 ? null : name;
    }
}
