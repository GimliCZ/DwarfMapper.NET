// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DwarfMapper.CodeFixes
{
    /// <summary>
    ///     Code fix for <c>DWARF085</c> (a restated base pair has drifted). Two actions, which are the two honest
    ///     answers: <b>restate the base configuration here</b>, or <b>say the difference is deliberate</b>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The restatement half is the mechanical, one-time cost that decision R18-D4 chose to keep in exchange
    ///         for every pair's configuration staying literally visible at its own declaration. A code fix is the
    ///         right way to pay a mechanical cost. Round 18 paid it by hand at 26 sites across three projects, and
    ///         invented a <c>MIRRORS BASE</c> comment convention while doing so.
    ///     </para>
    ///     <para>
    ///         The base pair is read from the diagnostic's <c>SourcePair</c> property rather than re-derived here.
    ///         The generator has already decided which pair is the base — nearest declared pair up the class chain,
    ///         ties refused — and working that rule out a second time in a code fix would be a second
    ///         implementation free to disagree with the first, silently.
    ///     </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RestateBaseConfigurationCodeFixProvider))]
    [Shared]
    public sealed class RestateBaseConfigurationCodeFixProvider : CodeFixProvider
    {
        private const string DiagnosticId = "DWARF085";

        /// <summary>The pair-scoped attributes a restatement is made of.</summary>
        private static readonly string[] PairScoped = ["MapProperty", "MapIgnore", "MapValue"];

        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return;
            }

            foreach (var diagnostic in context.Diagnostics)
            {
                var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

                var attribute = node.FirstAncestorOrSelf<AttributeSyntax>();
                var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
                if (attribute is null || classDecl is null)
                {
                    continue;
                }

                if (TryReadPair(diagnostic, out var baseSource, out var baseTarget) && TryReadDerivedPair(attribute, out var derivedSource, out var derivedTarget))
                {
                    var capturedClass = classDecl;
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            $"Restate the {Short(baseSource)} -> {Short(baseTarget)} configuration here",
                            _ => Task.FromResult(WithRestatement(
                                context.Document,
                                root,
                                capturedClass,
                                baseSource,
                                baseTarget,
                                derivedSource,
                                derivedTarget)),
                            "DWARF085_Restate"),
                        diagnostic);
                }

                if (!diagnostic.Properties.TryGetValue("Member", out var member) || string.IsNullOrEmpty(member))
                {
                    continue;
                }

                var capturedAttribute = attribute;
                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Mark '{member}' as a deliberate override",
                        _ => Task.FromResult(WithOverride(context.Document, root, capturedAttribute, member!)),
                        "DWARF085_Override"),
                    diagnostic);
            }
        }

        /// <summary>
        ///     Copies every pair-scoped attribute of the base pair onto the derived pair, rewriting the type
        ///     arguments, and skipping any the derived pair already carries for the same target member.
        /// </summary>
        /// <remarks>
        ///     Deliberately additive and deliberately literal. It writes the same attributes a person would have
        ///     typed — no marker comments, no generated brackets — because what makes the restatement checkable is
        ///     <c>[RestatesBase]</c> comparing the resolved mappings, not a convention that has to be preserved by
        ///     hand. An attribute already present for a member is left exactly as it is: if it is the drift, the
        ///     author is the one who has to decide whether it was meant.
        /// </remarks>
        private static Document WithRestatement(
            Document document,
            SyntaxNode root,
            ClassDeclarationSyntax classDecl,
            string baseSource,
            string baseTarget,
            string derivedSource,
            string derivedTarget)
        {
            var existingByTarget = new Dictionary<string, AttributeSyntax>(StringComparer.Ordinal);
            var toCopy = new List<(AttributeSyntax Attribute, GenericNameSyntax Name)>();

            foreach (var list in classDecl.AttributeLists)
            foreach (var attribute in list.Attributes)
            {
                // A plain local and a null test, not `is not { } generic`: negating that pattern leaves `generic`
                // unassigned, and the compile error makes Stryker drop every mutant in this method (Safe Mode).
                var generic = PairScopedName(attribute);
                if (generic is null)
                {
                    continue;
                }

                var typeArgs = generic.TypeArgumentList.Arguments;
                if (MatchesPair(typeArgs, derivedSource, derivedTarget))
                {
                    var target = TargetMemberOf(attribute, generic);
                    if (target is not null)
                    {
                        existingByTarget[target] = attribute;
                    }
                }
                else if (MatchesPair(typeArgs, baseSource, baseTarget))
                {
                    toCopy.Add((attribute, generic));
                }
            }

            // Two kinds of drift, two edits. A member the derived pair does not configure at all gets a new
            // attribute; one it configures DIFFERENTLY gets the base's version in place of what is there. The
            // second is a replacement of the author's own text, which is only defensible because they chose an
            // action that says "restate the base configuration here" — that is what restating means.
            var replacements = new Dictionary<AttributeSyntax, AttributeSyntax>();
            var additions = new List<AttributeListSyntax>();

            foreach (var (attribute, generic) in toCopy)
            {
                var rewritten = Retarget(attribute, generic, derivedSource, derivedTarget);
                var target = TargetMemberOf(attribute, generic);

                if (target is not null && existingByTarget.TryGetValue(target, out var current))
                {
                    if (!string.Equals(Normalise(current), Normalise(rewritten), StringComparison.Ordinal))
                    {
                        replacements[current] = rewritten.WithTriviaFrom(current);
                    }

                    continue;
                }

                additions.Add(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(rewritten))
                    .WithTriviaFrom(classDecl.AttributeLists.LastOrDefault() ?? (SyntaxNode)classDecl));
            }

            if (replacements.Count == 0 && additions.Count == 0)
            {
                return document;
            }

            var updated = classDecl;
            if (replacements.Count > 0)
            {
                updated = updated.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
            }

            if (additions.Count > 0)
            {
                updated = updated.WithAttributeLists(updated.AttributeLists.AddRange(additions));
            }

            return document.WithSyntaxRoot(
                root.ReplaceNode(classDecl, updated.WithAdditionalAnnotations(Formatter.Annotation)));
        }

        /// <summary>Adds the member to the attribute's <c>Overrides</c> array, creating it if absent.</summary>
        private static Document WithOverride(
            Document document,
            SyntaxNode root,
            AttributeSyntax attribute,
            string member)
        {
            var literal = SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(member));

            var existing = attribute.ArgumentList?.Arguments.FirstOrDefault(a =>
                string.Equals(a.NameEquals?.Name.Identifier.ValueText, "Overrides", StringComparison.Ordinal));

            AttributeSyntax updated;

            if (existing?.Expression is CollectionExpressionSyntax collection)
            {
                updated = attribute.ReplaceNode(collection,
                    collection.AddElements(SyntaxFactory.ExpressionElement(literal)));
            }
            else if (existing?.Expression is ImplicitArrayCreationExpressionSyntax array)
            {
                updated = attribute.ReplaceNode(array.Initializer,
                    array.Initializer.AddExpressions(literal));
            }
            else
            {
                // No Overrides yet — add one. A collection expression is the shortest form the target frameworks
                // all accept, and it is what the runtime attribute's own default uses.
                var argument = SyntaxFactory.AttributeArgument(
                        SyntaxFactory.CollectionExpression(
                            SyntaxFactory.SingletonSeparatedList<CollectionElementSyntax>(
                                SyntaxFactory.ExpressionElement(literal))))
                    .WithNameEquals(SyntaxFactory.NameEquals(SyntaxFactory.IdentifierName("Overrides")));

                updated = attribute.WithArgumentList(
                    (attribute.ArgumentList ?? SyntaxFactory.AttributeArgumentList()).AddArguments(argument));
            }

            return document.WithSyntaxRoot(
                root.ReplaceNode(attribute, updated.WithAdditionalAnnotations(Formatter.Annotation)));
        }

        /// <summary>Whitespace-insensitive text of an attribute, for "is this already what the base says?".</summary>
        private static string Normalise(AttributeSyntax attribute)
        {
            return string.Concat(attribute.ToString().Where(c => !char.IsWhiteSpace(c)));
        }

        /// <summary>
        ///     The attribute's generic name when it is a pair-scoped attribute with one or two type arguments, or
        ///     null when it is not.
        /// </summary>
        /// <remarks>
        ///     Returned rather than tested, so the helpers that read it afterwards take the generic name as given
        ///     and carry no "not generic after all" arm that no caller could reach.
        /// </remarks>
        private static GenericNameSyntax? PairScopedName(AttributeSyntax attribute)
        {
            var name = attribute.Name is QualifiedNameSyntax q ? q.Right : attribute.Name;
            if (name is not GenericNameSyntax generic)
            {
                return null;
            }

            var simple = generic.Identifier.ValueText;
            if (!PairScoped.Contains(simple, StringComparer.Ordinal) && !PairScoped.Contains(simple.Replace("Attribute", string.Empty), StringComparer.Ordinal))
            {
                return null;
            }

            return generic.TypeArgumentList.Arguments.Count is 1 or 2 ? generic : null;
        }

        /// <summary>
        ///     Whether the attribute's type arguments name this pair.
        /// </summary>
        /// <remarks>
        ///     Compared on the trailing name segment, because the diagnostic carries fully-qualified names while
        ///     the source almost never does. A one-argument form (<c>[MapIgnore&lt;T&gt;]</c>) names the TARGET
        ///     only, which is why it is matched against the target alone.
        /// </remarks>
        private static bool MatchesPair(SeparatedSyntaxList<TypeSyntax> typeArgs, string source, string target)
        {
            return typeArgs.Count == 2
                ? SameType(typeArgs[0], source) && SameType(typeArgs[1], target)
                : SameType(typeArgs[0], target);
        }

        private static bool SameType(TypeSyntax syntax, string fullyQualified)
        {
            return string.Equals(Short(syntax.ToString()), Short(fullyQualified), StringComparison.Ordinal);
        }

        /// <summary>The last name segment — <c>global::Demo.CommandDto</c> and <c>CommandDto</c> both give <c>CommandDto</c>.</summary>
        private static string Short(string name)
        {
            var cut = name.LastIndexOf('.');
            return cut < 0 ? name : name.Substring(cut + 1);
        }

        /// <summary>The destination member an attribute configures, or null when it names none.</summary>
        private static string? TargetMemberOf(AttributeSyntax attribute, GenericNameSyntax name)
        {
            var args = attribute.ArgumentList?.Arguments;
            if (args is null || args.Value.Count == 0)
            {
                return null;
            }

            var positional = args.Value.Where(a => a.NameEquals is null).ToList();
            if (positional.Count == 0)
            {
                return null;
            }

            // [MapProperty<S,T>(source, target)] names the target second; [MapIgnore<T>(target)] and
            // [MapValue<T>(target, …)] name it first.
            var expression = positional.Count >= 2 && IsMapProperty(name) ? positional[1] : positional[0];

            return expression.Expression switch
            {
                LiteralExpressionSyntax literal => literal.Token.ValueText,
                InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } n
                    when n.ArgumentList.Arguments.Count == 1 =>
                    Short(n.ArgumentList.Arguments[0].Expression.ToString()),
                _ => null
            };
        }

        private static bool IsMapProperty(GenericNameSyntax name)
        {
            return name.Identifier.ValueText.StartsWith("MapProperty", StringComparison.Ordinal);
        }

        private static AttributeSyntax Retarget(
            AttributeSyntax attribute,
            GenericNameSyntax generic,
            string derivedSource,
            string derivedTarget)
        {
            var replacements = generic.TypeArgumentList.Arguments.Count == 2
                ? new[]
                {
                    SyntaxFactory.ParseTypeName(Short(derivedSource)), SyntaxFactory.ParseTypeName(Short(derivedTarget))
                }
                : [SyntaxFactory.ParseTypeName(Short(derivedTarget))];

            var retargeted = generic.WithTypeArgumentList(
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(replacements)));

            return attribute.WithName(retargeted);
        }

        private static bool TryReadPair(Diagnostic diagnostic, out string source, out string target)
        {
            source = target = string.Empty;

            if (!diagnostic.Properties.TryGetValue("SourcePair", out var pair) || string.IsNullOrEmpty(pair))
            {
                return false;
            }

            var parts = pair!.Split('|');
            if (parts.Length != 2)
            {
                return false;
            }

            source = parts[0];
            target = parts[1];
            return true;
        }

        private static bool TryReadDerivedPair(AttributeSyntax attribute, out string source, out string target)
        {
            source = target = string.Empty;

            var name = attribute.Name is QualifiedNameSyntax q ? q.Right : attribute.Name;
            if (name is not GenericNameSyntax { TypeArgumentList.Arguments.Count: 2 } generic)
            {
                return false;
            }

            source = generic.TypeArgumentList.Arguments[0].ToString();
            target = generic.TypeArgumentList.Arguments[1].ToString();
            return true;
        }
    }
}
