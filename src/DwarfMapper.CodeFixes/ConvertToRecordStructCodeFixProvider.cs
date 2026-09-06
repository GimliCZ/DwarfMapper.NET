// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DwarfMapper.CodeFixes
{
    /// <summary>
    ///     Code fix for <c>DWARF103</c> (a mapped collection's element type could be a
    ///     <c>readonly record struct</c>). Rewrites the element type's declaration — and the declarations of
    ///     every transfer model it INLINES — into <c>readonly record struct</c>s, in one solution change.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The transitivity is what makes the diagnostic's number true, and is not a convenience.</b>
    ///         <c>TransferModelShape</c> costs a nested shaped model at the nested struct's OWN size rather
    ///         than at pointer width, because this fix rewrites it too. Convert the root alone and every nested
    ///         member goes back to being a reference: the type the consumer ends up with is smaller than the
    ///         size <c>DWARF103</c> printed, and a number already in front of them becomes retroactively false.
    ///         So the whole inlined set moves together or nothing does — including when one of its handles
    ///         cannot be resolved, where the fix returns the solution untouched rather than doing the part it
    ///         can.
    ///     </para>
    ///     <para>
    ///         <b>The targets come from the diagnostic's properties, never from its message.</b> That message
    ///         was reworded across four fix rounds while T2.2 was landing; a fix that recovered its target by
    ///         parsing prose would break on the next wording change with no compile error and no failing test —
    ///         the lightbulb would simply stop appearing. The properties carry
    ///         <c>DocumentationCommentId</c>s, which round-trip through
    ///         <see cref="DocumentationCommentId.GetFirstSymbolForDeclarationId" />, so the rewrite lands on
    ///         the very symbol the classifier judged rather than on whatever shares its name.
    ///     </para>
    ///     <para>
    ///         <b>Usages are deliberately untouched.</b> A <c>null</c> check, an aliasing assignment and
    ///         <c>list[i].X = v</c> all become compile errors, loud over silent — the bargain
    ///         <c>TransferModelShape</c>'s own documentation states, and the reason it refuses far more than it
    ///         accepts. What the fix must NOT do is produce a declaration that does not compile on its own,
    ///         which is a different failure and one it takes four specific steps to avoid: <c>set</c> becomes
    ///         <c>init</c> (CS8341), an instance field gains <c>readonly</c> (CS8340), a type with member
    ///         initialisers and no constructor gains a parameterless one (CS8983), and an OBLIVIOUS nested
    ///         member gains a <c>?</c> (see below).
    ///     </para>
    ///     <para>
    ///         <b>The oblivious member is the case to get right.</b> The classifier treats a nested member as
    ///         optional — a flag plus padding — whenever its annotation is anything but <c>NotAnnotated</c>,
    ///         and oblivious counts as optional deliberately, because where the nullable context is off
    ///         <c>Inner I</c> may legally hold null. Rewritten naively that member becomes a non-nullable
    ///         struct field which CANNOT, which silently changes the type's meaning AND makes it smaller than
    ///         the size the diagnostic promised. So an oblivious nested member is emitted as <c>Inner? I</c>.
    ///         A member already written <c>Inner?</c> under <c>#nullable enable</c> needs nothing: the syntax
    ///         is unchanged and means <c>Nullable&lt;Inner&gt;</c> once <c>Inner</c> is a struct.
    ///     </para>
    ///     <para>
    ///         <b>No Fix All.</b> Every action here is already a whole-solution change covering a transitive
    ///         set, and two of them for two different roots would overlap on any model both roots hold — the
    ///         batch fixer computes its changes against the ORIGINAL solution and would drop one. Offering no
    ///         Fix All is the honest answer; the actions are meant to be read one at a time anyway, because
    ///         each one changes the meaning of a type.
    ///     </para>
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConvertToRecordStructCodeFixProvider))]
    [Shared]
    public sealed class ConvertToRecordStructCodeFixProvider : CodeFixProvider
    {
        private const string DiagnosticId = "DWARF103";

        /// <summary>The diagnostic property carrying the type to rewrite, as a <c>DocumentationCommentId</c>.</summary>
        private const string TargetKey = "TransferModelId";

        /// <summary>The models that type inlines, pipe-separated <c>DocumentationCommentId</c>s.</summary>
        private const string NestedKey = "NestedTransferModelIds";

        /// <summary>The would-be struct's size in bytes, read for the <c>in</c> note and nothing else.</summary>
        private const string SizeKey = "TransferModelSize";

        /// <summary>
        ///     Bytes above which the rewritten type earns a note about passing it by <c>in</c>. The same line
        ///     <c>TransferModelShape.SuggestInSizeLimit</c> draws; it is restated rather than shared because
        ///     this assembly does not reference the generator, and it is only ever used to decide whether to
        ///     add a COMMENT — a drift here costs a note, never a rewrite.
        /// </summary>
        private const int SuggestInSizeLimit = 64;

        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

        /// <summary>
        ///     No Fix All, deliberately — see the class remarks. Each action is already a whole-solution change
        ///     over a transitive set, and the batch fixer computes every action against the ORIGINAL solution,
        ///     so two roots sharing a nested model would have one of their rewrites silently dropped.
        /// </summary>
        public override FixAllProvider? GetFixAllProvider()
        {
            return null;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!diagnostic.Properties.TryGetValue(TargetKey, out var targetId) || string.IsNullOrEmpty(targetId))
                {
                    // No handle, no fix. An older generator's diagnostic, or one from somewhere else entirely;
                    // guessing the target from the message is exactly what ruling 1 forbids.
                    continue;
                }

                var nested =
                    diagnostic.Properties.TryGetValue(NestedKey, out var joined) && !string.IsNullOrEmpty(joined)
                        ? joined!.Split('|')
                        : [];

                if (NamesAGenericType(targetId!) || Array.Exists(nested, NamesAGenericType))
                {
                    continue;
                }

                var size = diagnostic.Properties.TryGetValue(SizeKey, out var sizeText) &&
                           int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0;

                var document = context.Document;
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title(targetId!, nested.Length),
                        cancellationToken => ConvertAsync(document, targetId!, nested, size, cancellationToken),
                        "DWARF103_ConvertToRecordStruct"),
                    diagnostic);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     What a consumer is told the action will do to code it is not going to touch. Appended to every
        ///     arm of <see cref="Title" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>The title is the only place this can be said.</b> The rewrite deliberately leaves usages
        ///         alone — that is the whole "loud over silent" bargain — but the consequence lands as
        ///         CS1612/CS0037 in OTHER files, and Roslyn's preview shows the diff for one document. So a
        ///         person who clicks "Convert 'X' to readonly record struct" and reads nothing else has been
        ///         told a true thing that is not the whole thing, and finds out from the error list.
        ///     </para>
        ///     <para>
        ///         Worded as "are not updated AND may stop compiling" rather than "may need updating", because
        ///         the two facts are different and only one of them is guessable from the other: the fix
        ///         declining to edit usages is a decision, and a build that stops is the consequence. "May"
        ///         is honest — a usage that only reads members survives untouched.
        ///     </para>
        /// </remarks>
        private const string CallSiteWarning = "; call sites are not updated and may stop compiling";

        /// <summary>
        ///     The action's title. The plan's wording, with the parenthetical dropped when there is nothing to
        ///     put in it and singularised when there is one — "and 0 nested transfer models" describes the
        ///     commonest case of all and reads as a defect — and <see cref="CallSiteWarning" /> on the end of
        ///     every arm.
        /// </summary>
        private static string Title(string targetId, int nestedCount)
        {
            var name = Short(targetId);

            return nestedCount switch
            {
                0 => $"Convert '{name}' to readonly record struct{CallSiteWarning}",
                1 => $"Convert '{name}' (and 1 nested transfer model) to readonly record struct{CallSiteWarning}",
                _ => $"Convert '{name}' (and {nestedCount.ToString(CultureInfo.InvariantCulture)} nested transfer models) to readonly record struct{CallSiteWarning}"
            };
        }

        /// <summary>
        ///     True when a <c>DocumentationCommentId</c> names a type with a type parameter in scope —
        ///     <c>T:Demo.Box`1</c>, and equally <c>T:Demo.Outer`1.Inner</c>, whose <c>Inner</c> is generic in
        ///     its container's parameter. The fix declines those, and the backtick is the whole test.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Measured, not assumed.</b> <c>List&lt;Src&gt; → List&lt;Box&lt;int&gt;&gt;</c> reports
        ///         <c>DWARF103</c> for <c>Demo.Box&lt;int&gt;</c> at 4 bytes, and the handle it carries is the
        ///         DEFINITION, <c>T:Demo.Box`1</c> — because that is the only thing with a declaration to
        ///         rewrite. Converting it turns <b>every instantiation</b> into a value type, including a
        ///         <c>Box&lt;string&gt;</c> held somewhere else that the classifier never saw, no diagnostic
        ///         ever named, and for which the printed 4 bytes is simply false. That type loses reference
        ///         identity silently, which is the change this whole feature is built to refuse.
        ///     </para>
        ///     <para>
        ///         Declining costs a consumer a lightbulb on a shape the corpus has never produced; the
        ///         message's remedy is still there to apply by hand, where they can see which instantiations
        ///         they are agreeing to. It is also read off the id STRING, so the refusal costs no
        ///         compilation and happens before any action is offered — an unoffered fix is a non-event,
        ///         where an offered one that quietly does the wrong thing is not.
        ///     </para>
        /// </remarks>
        private static bool NamesAGenericType(string declarationId)
        {
            return declarationId.IndexOf('`') >= 0;
        }

        /// <summary>The type name inside a <c>DocumentationCommentId</c>: <c>T:Demo.OrderDto</c> gives <c>OrderDto</c>.</summary>
        private static string Short(string declarationId)
        {
            var name = declarationId.StartsWith("T:", StringComparison.Ordinal)
                ? declarationId.Substring(2)
                : declarationId;

            var cut = name.LastIndexOf('.');
            return cut < 0 ? name : name.Substring(cut + 1);
        }

        /// <summary>
        ///     Rewrites the root and every model it inlines, across whatever documents and partial declarations
        ///     they live in, and returns the whole thing as one solution.
        /// </summary>
        /// <remarks>
        ///     Every refusal below returns the solution UNCHANGED rather than a partial rewrite. Half a
        ///     transitive conversion is the one outcome worse than none: the root becomes a struct whose nested
        ///     members are still references, which is a type smaller than the size the consumer was shown.
        /// </remarks>
        private static async Task<Solution> ConvertAsync(
            Document document,
            string targetId,
            IReadOnlyList<string> nestedIds,
            int size,
            CancellationToken cancellationToken)
        {
            var solution = document.Project.Solution;

            var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                return solution;
            }

            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(targetId, compilation)
                is not INamedTypeSymbol root)
            {
                return solution;
            }

            var models = new List<INamedTypeSymbol>
            {
                root
            };

            foreach (var id in nestedIds)
            {
                if (DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation)
                    is not INamedTypeSymbol nested)
                {
                    return solution;
                }

                models.Add(nested);
            }

            // Members that must gain a `?`, keyed "<containing type's id>.<member>". Resolved from SYMBOLS
            // here, once, so the syntax rewrite below needs no semantic model of its own — and resolved by the
            // classifier's own rule (anything but NotAnnotated is optional), because the size was measured by
            // that rule and this is what keeps the two agreeing.
            var converted = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var model in models)
            {
                converted.Add(model);
                converted.Add(model.OriginalDefinition);
            }

            var obliviousMembers = ObliviousNestedMembers(models, converted);

            // Grouped by document, because ReplaceNodes takes one root at a time and a model may be declared
            // in another file than the one the diagnostic was reported in — or across several, as partials.
            var declarations = new Dictionary<DocumentId, List<ClassDeclarationSyntax>>();
            var oversized = new HashSet<SyntaxNode>();
            var owners = new Dictionary<SyntaxNode, string>();

            foreach (var model in models)
            {
                var modelId = model.OriginalDefinition.GetDocumentationCommentId() ?? model.Name;

                // EVERY declaration, not the first: a partial split across two files whose halves disagreed
                // about being a struct is CS0261, in the consumer's own file, from a fix that ran cleanly.
                foreach (var reference in model.DeclaringSyntaxReferences)
                {
                    var syntax = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
                    if (syntax is not ClassDeclarationSyntax declaration)
                    {
                        return solution;
                    }

                    var documentId = solution.GetDocumentId(reference.SyntaxTree);
                    if (documentId is null)
                    {
                        return solution;
                    }

                    if (!declarations.TryGetValue(documentId, out var list))
                    {
                        list = [];
                        declarations.Add(documentId, list);
                    }

                    list.Add(declaration);
                    owners[declaration] = modelId;

                    // The note goes on the ROOT only, and only in the band DWARF103 itself advises `in`. A
                    // nested model's own size is not what was measured here.
                    if (SymbolEqualityComparer.Default.Equals(model, root) && size > SuggestInSizeLimit)
                    {
                        oversized.Add(declaration);
                    }
                }
            }

            foreach (var entry in declarations)
            {
                var target = solution.GetDocument(entry.Key);
                var documentRoot = target is null
                    ? null
                    : await target.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

                if (documentRoot is null)
                {
                    return solution;
                }

                var rewritten = documentRoot.ReplaceNodes(
                    entry.Value,
                    (original, node) => Convert(
                        (ClassDeclarationSyntax)node,
                        owners[original],
                        obliviousMembers,
                        oversized.Contains(original),
                        size));

                solution = solution.WithDocumentSyntaxRoot(entry.Key, rewritten);
            }

            return solution;
        }

        /// <summary>
        ///     The members of the converted set whose type is ALSO in the converted set and whose nullable
        ///     annotation is oblivious — the ones that must be written <c>Inner?</c> so the rewrite preserves
        ///     both the nullability and the size the classifier measured.
        /// </summary>
        private static HashSet<string> ObliviousNestedMembers(
            IEnumerable<INamedTypeSymbol> models,
            HashSet<ISymbol> converted)
        {
            var oblivious = new HashSet<string>(StringComparer.Ordinal);

            foreach (var model in models)
            {
                var modelId = model.OriginalDefinition.GetDocumentationCommentId() ?? model.Name;

                foreach (var member in model.GetMembers())
                {
                    if (member.IsStatic || member.IsImplicitlyDeclared)
                    {
                        continue;
                    }

                    var memberType = member switch
                    {
                        IPropertySymbol { IsIndexer: false } property => property.Type,
                        IFieldSymbol { AssociatedSymbol: null } field => field.Type,
                        _ => null
                    };

                    if (memberType is not INamedTypeSymbol named ||
                        memberType.NullableAnnotation != NullableAnnotation.None)
                    {
                        continue;
                    }

                    if (converted.Contains(named) || converted.Contains(named.OriginalDefinition))
                    {
                        oblivious.Add(modelId + "." + member.Name);
                    }
                }
            }

            return oblivious;
        }

        /// <summary>Rewrites one <c>class</c> declaration into the equivalent <c>readonly record struct</c>.</summary>
        private static RecordDeclarationSyntax Convert(
            ClassDeclarationSyntax declaration,
            string modelId,
            HashSet<string> obliviousMembers,
            bool oversized,
            int size)
        {
            var members = new List<MemberDeclarationSyntax>();
            var hasInitialiser = false;
            var hasConstructor = false;

            foreach (var member in declaration.Members)
                switch (member)
                {
                    case PropertyDeclarationSyntax property:
                        hasInitialiser |= property.Initializer is not null;
                        members.Add(WithNullableType(
                                WithInitAccessor(property),
                                obliviousMembers.Contains(modelId + "." + property.Identifier.ValueText))
                            );

                        break;

                    case FieldDeclarationSyntax field:
                        var isStatic = field.Modifiers.Any(m =>
                            m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ConstKeyword));

                        if (!isStatic)
                        {
                            foreach (var variable in field.Declaration.Variables)
                                hasInitialiser |= variable.Initializer is not null;
                        }

                        var nullable = false;
                        foreach (var variable in field.Declaration.Variables)
                            nullable |= obliviousMembers.Contains(modelId + "." + variable.Identifier.ValueText);

                        members.Add(WithNullableType(isStatic ? field : WithReadOnly(field), nullable));

                        break;

                    case ConstructorDeclarationSyntax:
                        hasConstructor = true;
                        members.Add(member);

                        break;

                    default:
                        members.Add(member);

                        break;
                }

            // CS8983: a struct with field initialisers must declare a constructor — ANY constructor, which is
            // why a type that already has one is left alone. The class kept its initialisers through the
            // rewrite, so dropping them here to satisfy the compiler would be changing the consumer's data
            // behind a fix that says it converts a type.
            if (hasInitialiser && !hasConstructor)
            {
                members.Add(ParameterlessConstructor(declaration.Identifier));
            }

            var modifiers = new List<SyntaxToken>();
            var seenReadOnly = false;
            var partialAt = -1;

            foreach (var modifier in declaration.Modifiers)
            {
                // `sealed` is meaningless on a struct and is the modifier the plan names.
                if (modifier.IsKind(SyntaxKind.SealedKeyword))
                {
                    continue;
                }

                seenReadOnly |= modifier.IsKind(SyntaxKind.ReadOnlyKeyword);

                if (modifier.IsKind(SyntaxKind.PartialKeyword))
                {
                    partialAt = modifiers.Count;
                }

                modifiers.Add(Keyword(modifier.Kind()));
            }

            // `partial` must sit LAST, immediately before the type keyword — `public partial readonly record
            // struct` is a syntax error, and a code fix that emits one has broken the consumer's file in the
            // very act of improving it. Every other modifier's order is free; this one is not.
            if (!seenReadOnly)
            {
                modifiers.Insert(partialAt < 0 ? modifiers.Count : partialAt, Keyword(SyntaxKind.ReadOnlyKeyword));
            }

            // The attribute lists carry the declaration's leading trivia on their first token, and the record
            // node is about to be given that same trivia — so it is taken off them first, or every XML doc
            // comment above a converted type appears twice.
            var attributeLists = declaration.AttributeLists;
            if (attributeLists.Count > 0)
            {
                attributeLists = attributeLists.Replace(attributeLists[0], attributeLists[0].WithoutLeadingTrivia());
            }

            var record = SyntaxFactory.RecordDeclaration(
                SyntaxKind.RecordStructDeclaration,
                attributeLists,
                SyntaxFactory.TokenList(modifiers),
                Keyword(SyntaxKind.RecordKeyword),
                Keyword(SyntaxKind.StructKeyword),
                declaration.Identifier.WithoutTrivia(),
                declaration.TypeParameterList,
                null,
                declaration.BaseList,
                declaration.ConstraintClauses,
                SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
                SyntaxFactory.List(members),
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken),
                default);

            var leading = declaration.GetLeadingTrivia();
            if (oversized)
            {
                leading = leading.Add(SyntaxFactory.Comment(
                        "// " + size.ToString(CultureInfo.InvariantCulture) +
                        " bytes as a struct, over the 64-byte line: consider passing it by 'in'."))
                    .Add(SyntaxFactory.ElasticCarriageReturnLineFeed);
            }

            return record
                .WithLeadingTrivia(leading)
                .WithTrailingTrivia(declaration.GetTrailingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);
        }

        /// <summary>A keyword token with one trailing space, so the result reads even before formatting runs.</summary>
        private static SyntaxToken Keyword(SyntaxKind kind)
        {
            return SyntaxFactory.Token(default, kind, SyntaxFactory.TriviaList(SyntaxFactory.Space));
        }

        /// <summary>
        ///     <c>set</c> becomes <c>init</c>. CS8341: an auto-property in a <c>readonly</c> struct must itself
        ///     be readonly, and <c>init</c> is the accessor that keeps object-initialiser syntax working at the
        ///     call sites the fix is deliberately not touching.
        /// </summary>
        private static PropertyDeclarationSyntax WithInitAccessor(PropertyDeclarationSyntax property)
        {
            if (property.AccessorList is null)
            {
                return property;
            }

            var accessors = property.AccessorList.Accessors;

            foreach (var accessor in property.AccessorList.Accessors)
            {
                if (!accessor.IsKind(SyntaxKind.SetAccessorDeclaration))
                {
                    continue;
                }

                accessors = accessors.Replace(
                    accessor,
                    SyntaxFactory.AccessorDeclaration(
                            SyntaxKind.InitAccessorDeclaration,
                            accessor.AttributeLists,
                            accessor.Modifiers,
                            SyntaxFactory.Token(SyntaxKind.InitKeyword),
                            accessor.Body,
                            accessor.ExpressionBody,
                            accessor.SemicolonToken)
                        .WithTriviaFrom(accessor));
            }

            return property.WithAccessorList(property.AccessorList.WithAccessors(accessors));
        }

        /// <summary>
        ///     An instance field gains <c>readonly</c>. CS8340: the instance fields of a <c>readonly</c> struct
        ///     must be readonly, and a public data field is a shape the classifier accepts.
        /// </summary>
        private static FieldDeclarationSyntax WithReadOnly(FieldDeclarationSyntax field)
        {
            foreach (var modifier in field.Modifiers)
                if (modifier.IsKind(SyntaxKind.ReadOnlyKeyword))
                {
                    return field;
                }

            return field.WithModifiers(field.Modifiers.Add(Keyword(SyntaxKind.ReadOnlyKeyword)));
        }

        private static PropertyDeclarationSyntax WithNullableType(PropertyDeclarationSyntax property, bool nullable)
        {
            return nullable && property.Type is not NullableTypeSyntax
                ? property.WithType(Nullable(property.Type))
                : property;
        }

        private static FieldDeclarationSyntax WithNullableType(FieldDeclarationSyntax field, bool nullable)
        {
            return nullable && field.Declaration.Type is not NullableTypeSyntax
                ? field.WithDeclaration(field.Declaration.WithType(Nullable(field.Declaration.Type)))
                : field;
        }

        private static NullableTypeSyntax Nullable(TypeSyntax type)
        {
            return SyntaxFactory.NullableType(type.WithoutTrivia()).WithTriviaFrom(type);
        }

        /// <summary>The parameterless constructor CS8983 asks for when the type kept member initialisers.</summary>
        private static ConstructorDeclarationSyntax ParameterlessConstructor(SyntaxToken identifier)
        {
            return SyntaxFactory.ConstructorDeclaration(identifier.WithoutTrivia())
                .WithModifiers(SyntaxFactory.TokenList(Keyword(SyntaxKind.PublicKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList())
                .WithBody(SyntaxFactory.Block())
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        }
    }
}
