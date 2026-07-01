// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    /// <summary>
    /// Output of <see cref="ReadMapConfig"/>: the same internal pair-scoped IR the attribute readers
    /// (<c>ReadPairMapProperties</c> etc.) produce, so downstream resolution/emission is unchanged regardless
    /// of whether a pair was configured via <c>[MapProperty&lt;,&gt;]</c> or via a <c>MapConfig&lt;S,T&gt;</c>
    /// convention method.
    /// </summary>
    // `private` (not `internal`, despite the brief's draft) — PairProp/PairIgnore/PairValue/PairConstructor
    // are themselves PRIVATE nested types of MapperExtractor (CS0052 forbids an internal/public type exposing
    // a less-accessible one via a public member). Mirrors the existing PairProp/PairIgnore/PairValue/
    // PairConstructor convention just below: a private nested class with public fields, reachable from
    // anywhere in MapperExtractor's own program text (ReadMapConfig here, and the ExtractCore merge point
    // in MapperExtractor.cs) because that's exactly the domain a private nested type's members occupy.
    private sealed class MapConfigResult
    {
        public readonly List<PairProp> Props = new();
        public readonly List<PairIgnore> Ignores = new();
        public readonly List<PairValue> Values = new();
        public readonly List<PairConstructor> Constructors = new();
        public readonly List<string> IgnoreSources = new();
    }

    /// <summary>Dotted member path from a selector lambda `p => p.A.B`, or null when it is not a pure
    /// member-access chain rooted at the lambda's single parameter.</summary>
    private static string? TryReadMemberPath(ExpressionSyntax arg)
    {
        string param;
        ExpressionSyntax body;
        switch (arg)
        {
            case SimpleLambdaExpressionSyntax { Body: ExpressionSyntax b } sl:
                param = sl.Parameter.Identifier.ValueText; body = b; break;
            case ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1 } ps, Body: ExpressionSyntax b } pl:
                param = ps[0].Identifier.ValueText; body = b; break;
            default:
                return null;
        }
        var parts = new List<string>();
        var cur = body;
        while (cur is MemberAccessExpressionSyntax ma)
        {
            parts.Add(ma.Name.Identifier.ValueText);
            cur = ma.Expression;
        }
        if (cur is IdentifierNameSyntax id && id.Identifier.ValueText == param)
        {
            parts.Reverse();
            return string.Join(".", parts);
        }
        return null;
    }

    /// <summary>Simple method name from a method-group argument (`Convert` or `Type.Convert`/`this.Convert`),
    /// or null when the argument is not a method group (e.g. an inline lambda).</summary>
    private static string? TryReadMethodGroup(ExpressionSyntax arg) => arg switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// Finds every <c>MapConfig&lt;S,T&gt;</c> convention method on <paramref name="classSymbol"/> (a private
    /// or public method whose single parameter's type is <c>MapConfig&lt;S,T&gt;</c>) and walks its body's
    /// fluent <c>.Map(...)</c> calls SYNTACTICALLY — the config method is never executed, only its syntax tree
    /// is read — to produce the same <see cref="PairProp"/> IR the pair-scoped <c>[MapProperty&lt;S,T&gt;]</c>
    /// attribute reader produces. Returns an empty result (not an error) when the runtime MapConfig type isn't
    /// referenced by the compilation, or when the class declares no such method.
    /// </summary>
    private static MapConfigResult ReadMapConfig(
        INamedTypeSymbol classSymbol, Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        var result = new MapConfigResult();
        var mapConfigDef = compilation.GetTypeByMetadataName(KnownNames.MapConfigMetadata);
        if (mapConfigDef is null)
            return result; // runtime library not referenced; nothing to do

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1) continue;
            if (method.Parameters[0].Type is not INamedTypeSymbol pt) continue;
            if (!SymbolEqualityComparer.Default.Equals(pt.OriginalDefinition, mapConfigDef)) continue;
            if (pt.TypeArguments.Length != 2) continue;

            var src = pt.TypeArguments[0];
            var tgt = pt.TypeArguments[1];
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is not MethodDeclarationSyntax decl) continue;
            var model = compilation.GetSemanticModel(decl.SyntaxTree);

            foreach (var inv in decl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inv.Expression is not MemberAccessExpressionSyntax ma) continue;
                // Only fluent calls whose receiver IS the MapConfig<S,T> value — never an unrelated `.Map(...)`.
                if (model.GetTypeInfo(ma.Expression).Type?.OriginalDefinition is not INamedTypeSymbol rt
                    || !SymbolEqualityComparer.Default.Equals(rt, mapConfigDef)) continue;
                var op = ma.Name.Identifier.ValueText;
                var args = inv.ArgumentList.Arguments;
                var loc = LocationInfo.From(inv.GetLocation());
                if (op == "Map" && args.Count >= 2)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    var srcPath = TryReadMemberPath(args[1].Expression);
                    if (tgtPath is null || srcPath is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig Map: expected a member-access selector (t => t.A.B) or a method group, but found '{inv}'"));
                        continue;
                    }
                    string? use = null;
                    if (args.Count == 3)
                    {
                        use = TryReadMethodGroup(args[2].Expression);
                        if (use is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                                $"MapConfig Map converter: expected a member-access selector (t => t.A.B) or a method group, but found '{args[2].Expression}'"));
                            continue;
                        }
                    }
                    result.Props.Add(new PairProp
                    {
                        Source = src, Target = tgt, SrcMember = srcPath, TgtMember = tgtPath, Use = use, Loc = loc,
                    });
                }
                else if (op == "MapWhen" && args.Count == 3)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    var srcPath = TryReadMemberPath(args[1].Expression);
                    var when = TryReadMethodGroup(args[2].Expression);
                    if (tgtPath is null || srcPath is null || when is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig MapWhen: expected member-access selectors and a predicate method group, but found '{inv}'"));
                        continue;
                    }
                    result.Props.Add(new PairProp { Source = src, Target = tgt, SrcMember = srcPath, TgtMember = tgtPath, When = when, Loc = loc });
                }
                else if (op == "Ignore" && args.Count == 1)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    if (tgtPath is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig Ignore: expected a member-access selector (t => t.X), but found '{args[0].Expression}'"));
                        continue;
                    }
                    result.Ignores.Add(new PairIgnore { Target = tgt, Member = tgtPath, Loc = loc });
                }
            }
        }
        return result;
    }
}
