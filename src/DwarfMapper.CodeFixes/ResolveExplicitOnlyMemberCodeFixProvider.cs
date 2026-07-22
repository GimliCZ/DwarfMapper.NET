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
///     Code fix for <c>DWARF072</c> (explicit-only mapper: a destination member has a same-named source but
///     auto-matching is disabled). The trust-boundary guard deliberately makes each member a conscious
///     decision; this offers the two decisions directly on the mapping method:
///     <list type="bullet">
///       <item><description><b>Map it</b> — insert <c>[MapProperty("Member", "Member")]</c>. A same-named
///       source is known to exist (that is what raised DWARF072), so the scaffold compiles as-is.</description></item>
///       <item><description><b>Ignore it</b> — insert <c>[MapIgnore("Member")]</c>.</description></item>
///     </list>
///     Both resolve the diagnostic, so the developer picks the intent rather than hand-typing the attribute —
///     the adoption-easing half of the never-silent trust-boundary story. Nested (dotted) targets are skipped.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ResolveExplicitOnlyMemberCodeFixProvider))]
[Shared]
public sealed class ResolveExplicitOnlyMemberCodeFixProvider : CodeFixProvider
{
    private const string DiagnosticId = "DWARF072";

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
            if (member is null) continue;

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method is null) continue;

            var captured = method;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Map '{member}' with [MapProperty]",
                    _ => Task.FromResult(WithAttribute(context.Document, root, captured,
                        "global::DwarfMapper.MapProperty", member, member)),
                    "DWARF072_AddMapProperty"),
                diagnostic);

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Ignore '{member}' with [MapIgnore]",
                    _ => Task.FromResult(WithAttribute(context.Document, root, captured,
                        "global::DwarfMapper.MapIgnore", member)),
                    "DWARF072_AddMapIgnore"),
                diagnostic);
        }
    }

    private static Document WithAttribute(Document document, SyntaxNode root, MethodDeclarationSyntax method,
        string attributeType, params string[] literalArgs)
    {
        var generator = SyntaxGenerator.GetGenerator(document);
        var args = new SyntaxNode[literalArgs.Length];
        for (var i = 0; i < literalArgs.Length; i++)
            args[i] = generator.LiteralExpression(literalArgs[i]);

        var attribute = generator.Attribute(attributeType, args);
        var newMethod = generator.AddAttributes(method, attribute);
        return document.WithSyntaxRoot(root.ReplaceNode(method, newMethod));
    }

    // "Destination member 'Member' has a same-named source member …" → "Member" (null if dotted/unexpected).
    private static string? ExtractMemberName(Diagnostic diagnostic)
    {
        // ISSUE-028: read the member off the diagnostic's PROPERTY bag. This used to parse the text between
        // the first pair of quotes in the human-readable message, so rewording (or localising) that message
        // silently broke the fix — no compile error, no failing test, the lightbulb just stops appearing.
        if (!diagnostic.Properties.TryGetValue("Member", out var name) || string.IsNullOrEmpty(name))
            return null;

        // A dotted path is a nested member; this fix only applies to a member on the mapped type itself.
        return name!.IndexOf('.') >= 0 ? null : name;
    }
}
