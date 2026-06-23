// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Registry;

/// <summary>
/// EXPERIMENTAL (v23 prototype). Second front door: scans the assembly for <c>[MapTo]</c> on plain types
/// and emits static extension methods (<c>src.MapTo&lt;TTarget&gt;()</c> / <c>src.To{Target}()</c>) — no
/// user <c>partial</c>. A multi-target map is resolved as N independent single-target resolutions, each
/// running the completeness gate, so cross-target member "swaps" are compile errors. Prototype scope:
/// reference-type targets, identity/implicit member assignment only (no converters/nested/collections).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MapToGenerator : IIncrementalGenerator
{
    private const string MapToAttr = "DwarfMapper.Registry.MapToAttribute";
    private const string MapPropAttr = "DwarfMapper.Registry.MapPropertyAttribute";
    private const string MapIgnoreAttr = "DwarfMapper.Registry.MapIgnoreAttribute";

    // ── equatable, symbol-free model (cache-safe) ───────────────────────────────
    internal sealed record Assignment(string DestMember, string SourceMember) : System.IEquatable<Assignment>;
    internal sealed record TargetPlan(string TargetFqn, string MethodName, EquatableArray<Assignment> Assignments)
        : System.IEquatable<TargetPlan>;
    internal sealed record Model(
        string SourceFqn,
        string? Namespace,
        string ExtClassName,
        EquatableArray<TargetPlan> Targets,
        EquatableArray<DiagnosticInfo> Diagnostics,
        bool HasError) : System.IEquatable<Model>;

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
            MapToAttr,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => Extract(ctx));

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            foreach (var d in model.Diagnostics)
            {
                spc.ReportDiagnostic(d.ToDiagnostic());
            }

            if (model.HasError || model.Targets.Count == 0)
            {
                return;
            }

            spc.AddSource($"{model.ExtClassName}.g.cs", Emit(model));
        });
    }

    private static Model Extract(GeneratorAttributeSyntaxContext ctx)
    {
        var source = (INamedTypeSymbol)ctx.TargetSymbol;
        var compilation = ctx.SemanticModel.Compilation;
        var location = LocationInfo.From(source.Locations.FirstOrDefault() ?? Location.None);
        var diags = new List<DiagnosticInfo>();
        var hasError = false;

        // Collect declared targets across all [MapTo] attributes (AllowMultiple), in declaration order.
        var targets = new List<INamedTypeSymbol>();
        foreach (var attr in ctx.Attributes)
        {
            if (attr.ConstructorArguments.Length != 1) continue;
            foreach (var tc in attr.ConstructorArguments[0].Values)
            {
                if (tc.Value is INamedTypeSymbol t) targets.Add(t);
            }
        }
        var targetCount = targets.Count;

        // Parse each source member's directives ONCE, in source order. Each [MapProperty]/[MapIgnore]
        // aligns positionally to a [MapTo] target.
        var members = new List<(ISymbol Sym, ITypeSymbol Type, List<(bool Ignore, string? Name)> Directives)>();
        foreach (var (srcSym, srcType) in ReadableMembers(source))
        {
            var directives = ParseDirectives(srcSym);
            if (directives.Count > 1 && targetCount > 0 && directives.Count != targetCount)
            {
                diags.Add(new DiagnosticInfo(RegistryDiagnostics.MapPropertyArity, location, $"'{srcSym.Name}'"));
                hasError = true;
                directives = new List<(bool, string?)>(); // fall back to by-name; avoid cascading errors
            }
            members.Add((srcSym, srcType, directives));
        }

        var plans = new List<TargetPlan>();

        for (var ti = 0; ti < targetCount; ti++)
        {
            var target = targets[ti];
            if (!IsMappableTarget(target, source))
            {
                diags.Add(new DiagnosticInfo(RegistryDiagnostics.InvalidTarget, location, target.ToDisplayString()));
                hasError = true;
                continue;
            }

            var targetFqn = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var writables = WritableMembers(target).ToList();

            // destName -> chosen source member (resolved per target, independently).
            var chosen = new Dictionary<string, ISymbol>();
            foreach (var m in members)
            {
                // Directive for THIS target: none → by-name; one → broadcast to all; else → positional.
                string destName;
                if (m.Directives.Count == 0)
                {
                    destName = m.Sym.Name;
                }
                else
                {
                    var d = m.Directives.Count == 1 ? m.Directives[0] : m.Directives[ti];
                    if (d.Ignore) continue; // ignored for this target
                    destName = !string.IsNullOrEmpty(d.Name) ? d.Name! : m.Sym.Name;
                }

                var w = writables.FirstOrDefault(x => x.Name == destName);
                if (w.Symbol is null) continue; // not a member of THIS target — fine

                var conv = compilation.ClassifyCommonConversion(m.Type, w.Type);
                if (!(conv.IsIdentity || conv.IsImplicit)) continue; // out of prototype scope

                if (chosen.ContainsKey(destName))
                {
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.ConflictingSource, location,
                        $"'{destName}' on '{target.Name}'"));
                    hasError = true;
                }
                else
                {
                    chosen[destName] = m.Sym;
                }
            }

            // Completeness: every writable destination member must be satisfied.
            var assignments = new List<Assignment>();
            foreach (var w in writables)
            {
                if (chosen.TryGetValue(w.Name, out var src))
                {
                    assignments.Add(new Assignment(w.Name, src.Name));
                }
                else
                {
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.UnmappedDestination, location,
                        $"'{w.Name}' on '{target.Name}'"));
                    hasError = true;
                }
            }

            plans.Add(new TargetPlan(targetFqn, "To" + target.Name, new EquatableArray<Assignment>(assignments.ToArray())));
        }

        var ns = source.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : null;
        return new Model(
            source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ns,
            "__DwarfRegistry_" + source.Name,
            new EquatableArray<TargetPlan>(plans.ToArray()),
            new EquatableArray<DiagnosticInfo>(diags.ToArray()),
            hasError);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────
    private static bool IsMappableTarget(INamedTypeSymbol target, INamedTypeSymbol source) =>
        !SymbolEqualityComparer.Default.Equals(target, source)
        && (target.TypeKind == TypeKind.Class || target.TypeKind == TypeKind.Struct)
        && !target.IsAbstract;

    private static IEnumerable<(ISymbol Symbol, ITypeSymbol Type)> ReadableMembers(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic || m.DeclaredAccessibility != Accessibility.Public) continue;
            if (m is IPropertySymbol { GetMethod: not null, IsIndexer: false } p) yield return (p, p.Type);
            else if (m is IFieldSymbol { IsConst: false, IsImplicitlyDeclared: false } f) yield return (f, f.Type);
        }
    }

    private static IEnumerable<(ISymbol Symbol, string Name, ITypeSymbol Type)> WritableMembers(INamedTypeSymbol type)
    {
        foreach (var m in type.GetMembers())
        {
            if (m.IsStatic || m.DeclaredAccessibility != Accessibility.Public) continue;
            if (m is IPropertySymbol { SetMethod: not null, IsIndexer: false } p) yield return (p, p.Name, p.Type);
            else if (m is IFieldSymbol { IsConst: false, IsReadOnly: false, IsImplicitlyDeclared: false } f)
                yield return (f, f.Name, f.Type);
        }
    }

    /// <summary>
    /// A member's [MapProperty]/[MapIgnore] directives in SOURCE ORDER (sorted by syntax position so the
    /// order is robust). Each directive aligns positionally to a [MapTo] target: <c>Ignore</c> or a
    /// destination <c>Name</c>.
    /// </summary>
    private static List<(bool Ignore, string? Name)> ParseDirectives(ISymbol member)
    {
        var ordered = new List<(int Pos, bool Ignore, string? Name)>();
        foreach (var a in member.GetAttributes())
        {
            var cls = a.AttributeClass?.ToDisplayString();
            if (cls != MapPropAttr && cls != MapIgnoreAttr) continue;
            var pos = a.ApplicationSyntaxReference?.Span.Start ?? 0;
            if (cls == MapIgnoreAttr)
            {
                ordered.Add((pos, true, null));
            }
            else
            {
                var name = a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null;
                ordered.Add((pos, false, name));
            }
        }

        ordered.Sort((x, y) => x.Pos.CompareTo(y.Pos));
        return ordered.Select(x => (x.Ignore, x.Name)).ToList();
    }

    private static string Emit(Model model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        var indent = "";
        if (model.Namespace is not null)
        {
            sb.Append("namespace ").AppendLine(model.Namespace);
            sb.AppendLine("{");
            indent = "    ";
        }

        sb.Append(indent).Append("internal static class ").AppendLine(model.ExtClassName);
        sb.Append(indent).AppendLine("{");
        var i2 = indent + "    ";
        var i3 = i2 + "    ";

        // Generic dispatcher: src.MapTo<TTarget>()
        sb.Append(i2).Append("public static TTarget MapTo<TTarget>(this ").Append(model.SourceFqn).AppendLine(" source)");
        sb.Append(i2).AppendLine("{");
        sb.Append(i3).AppendLine("if (source is null) throw new global::System.ArgumentNullException(nameof(source));");
        foreach (var t in model.Targets)
        {
            sb.Append(i3).Append("if (typeof(TTarget) == typeof(").Append(t.TargetFqn).Append(")) return (TTarget)(object)")
              .Append(t.MethodName).AppendLine("(source);");
        }
        sb.Append(i3).Append("throw new global::System.NotSupportedException(\"No [MapTo] mapping from ")
          .Append(model.SourceFqn).AppendLine(" to \" + typeof(TTarget).FullName + \".\");");
        sb.Append(i2).AppendLine("}");

        // Named per-target workers: src.ToTarget()
        foreach (var t in model.Targets)
        {
            sb.AppendLine();
            sb.Append(i2).Append("public static ").Append(t.TargetFqn).Append(' ').Append(t.MethodName)
              .Append("(this ").Append(model.SourceFqn).AppendLine(" source)");
            sb.Append(i2).AppendLine("{");
            sb.Append(i3).AppendLine("if (source is null) throw new global::System.ArgumentNullException(nameof(source));");
            sb.Append(i3).Append("return new ").Append(t.TargetFqn).AppendLine();
            sb.Append(i3).AppendLine("{");
            foreach (var a in t.Assignments)
            {
                sb.Append(i3).Append("    ").Append(a.DestMember).Append(" = source.").Append(a.SourceMember).AppendLine(",");
            }
            sb.Append(i3).AppendLine("};");
            sb.Append(i2).AppendLine("}");
        }

        sb.Append(indent).AppendLine("}");
        if (model.Namespace is not null) sb.AppendLine("}");
        return sb.ToString();
    }
}
