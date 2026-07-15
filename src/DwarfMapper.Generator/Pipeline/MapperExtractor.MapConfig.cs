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

        /// <summary>
        ///     Names of the convention methods that were read. They are never CALLED (the generator reads them
        ///     syntactically), so a consumer with IDE0051-as-error would see them flagged as unused private
        ///     members. The emitter references each one via <c>nameof</c> in the generated half so they count as
        ///     used — the mapper's own compile-time config surface must not break the mapper's build.
        /// </summary>
        public readonly List<string> ConventionMethodNames = new();
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

            // This is a genuine convention method — record it so the emitter can nameof-reference it (the
            // generator reads it but never calls it, so IDE0051-as-error would otherwise flag it as unused).
            result.ConventionMethodNames.Add(method.Name);

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
                else if (op == "IgnoreSource" && args.Count == 1)
                {
                    var srcPath = TryReadMemberPath(args[0].Expression);
                    if (srcPath is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig IgnoreSource: expected a member-access selector (s => s.X), but found '{args[0].Expression}'"));
                        continue;
                    }
                    result.IgnoreSources.Add(srcPath);
                }
                else if (op == "Construct" && args.Count == 1)
                {
                    var factory = TryReadMethodGroup(args[0].Expression);
                    if (factory is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig Construct: expected a factory method group, but found '{args[0].Expression}'"));
                        continue;
                    }
                    result.Constructors.Add(new PairConstructor { Source = src, Target = tgt, Method = factory, Loc = loc });
                }
                else if (op == "MapOr" && args.Count == 3)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    var srcPath = TryReadMemberPath(args[1].Expression);
                    if (tgtPath is null || srcPath is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig MapOr: expected member-access selectors, but found '{inv}'"));
                        continue;
                    }
                    var cv = model.GetConstantValue(args[2].Expression);
                    if (!cv.HasValue)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig MapOr fallback must be a compile-time constant, but found '{args[2].Expression}'"));
                        continue;
                    }
                    var vt = model.GetTypeInfo(args[2].Expression).Type;
                    // `vt` (the constant's OWN type) is reused as BOTH valueType and targetType here — safe because
                    // MapOr's generic TMember forces the constant to be implicitly convertible to the member type,
                    // and RenderConstantLiteral only adds a cast for a float/double/decimal TARGET, which only
                    // arises when the constant's own type already is float/double/decimal; any other divergence
                    // (e.g. an int literal into a float member) is a widening C# accepts with no cast needed.
                    var literal = RenderConstantLiteral(cv.Value, vt, vt!, compilation);
                    result.Props.Add(new PairProp { Source = src, Target = tgt, SrcMember = srcPath, TgtMember = tgtPath,
                        HasNullSub = true, NullSubLiteral = literal, Loc = loc });
                }
                else if (op == "Value" && args.Count == 2)
                {
                    var tgtPath = TryReadMemberPath(args[0].Expression);
                    if (tgtPath is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                            $"MapConfig Value: expected a member-access selector (t => t.X), but found '{args[0].Expression}'"));
                        continue;
                    }
                    var compute = TryReadMethodGroup(args[1].Expression);
                    if (compute is not null)
                    {
                        result.Values.Add(new PairValue { Target = tgt, Member = tgtPath, Use = compute, Loc = loc });
                    }
                    else
                    {
                        var cv = model.GetConstantValue(args[1].Expression);
                        if (!cv.HasValue)
                        {
                            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                                $"MapConfig Value: expected a constant or a method group, but found '{args[1].Expression}'"));
                            continue;
                        }
                        var vt = model.GetTypeInfo(args[1].Expression).Type;
                        // Same target-type reuse as the MapOr call above — safe for the same reason (the
                        // `TMember` generic constraint on `.Value` guarantees implicit convertibility; a
                        // cast is only ever emitted when the constant's own type is already float/double/
                        // decimal, so no int->float style widening is ever missing its needed cast).
                        result.Values.Add(new PairValue { Target = tgt, Member = tgtPath, IsConstant = true,
                            ConstLiteral = RenderConstantLiteral(cv.Value, vt, vt!, compilation), Loc = loc });
                    }
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigUnsupportedExpression, loc,
                        $"MapConfig: unsupported configuration call '{op}' with {args.Count} argument(s)"));
                }
            }
        }
        return result;
    }

    /// <summary>Flags a destination member configured more than once — by both an attribute and a MapConfig, or
    /// twice within a MapConfig (DWARF069). <paramref name="attrProps"/>/<paramref name="attrValues"/> are the
    /// attribute-origin lists BEFORE the config entries are merged in.</summary>
    // Scope note: detection is within-IR-type (Prop-vs-Prop keyed by source+target+member; Value-vs-Value keyed
    // by target+member). A cross-type collision (a `.Map` and a `.Value` on the same member) is not flagged in
    // v1 — acceptable and consistent with the spec's same-member examples.
    private static void ReportMapConfigConflicts(
        List<PairProp> attrProps, List<PairValue> attrValues, MapConfigResult config, List<DiagnosticInfo> diagnostics)
    {
        static string PropKey(PairProp p) => p.Source.ToDisplayString() + "|" + p.Target.ToDisplayString() + "|" + p.TgtMember;
        static string ValKey(PairValue v) => v.Target.ToDisplayString() + "|" + v.Member;

        var propKeys = new HashSet<string>(attrProps.ConvertAll(PropKey));
        foreach (var p in config.Props)
            if (!propKeys.Add(PropKey(p)))
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigConflict, p.Loc,
                    $"Destination member '{p.TgtMember}' of {p.Target.ToDisplayString()} is configured more than once (MapConfig and/or attribute); remove one"));

        var valKeys = new HashSet<string>(attrValues.ConvertAll(ValKey));
        foreach (var v in config.Values)
            if (!valKeys.Add(ValKey(v)))
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConfigConflict, v.Loc,
                    $"Destination member '{v.Member}' of {v.Target.ToDisplayString()} is configured more than once (MapConfig and/or attribute); remove one"));
    }
}
