// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Registry;

/// <summary>
/// EXPERIMENTAL (v23). Second front door: scans the assembly for <c>[MapTo]</c> on plain types and emits
/// static extension methods (<c>src.MapTo&lt;TTarget&gt;()</c> / <c>src.To{Target}()</c>) — no user
/// <c>partial</c>. A multi-target map is N independent single-target resolutions, each running the
/// completeness gate. Conversions reuse the core engine (numeric / parse / enum), plus self-contained
/// nested-object mapping and List/array collections. Cyclic graphs are rejected (DWARFR06) — use the
/// <c>[DwarfMapper]</c> class model for those.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MapToGenerator : IIncrementalGenerator
{
    private const string MapToAttr = "DwarfMapper.Registry.MapToAttribute";
    private const string MapPropAttr = "DwarfMapper.Registry.MapPropertyAttribute";
    private const string MapIgnoreAttr = "DwarfMapper.Registry.MapIgnoreAttribute";

    // ── equatable, symbol-free model (cache-safe) ───────────────────────────────
    internal sealed record Assignment(string DestMember, string Expr) : System.IEquatable<Assignment>;
    internal sealed record TargetPlan(string TargetFqn, string MethodName, EquatableArray<Assignment> Assignments)
        : System.IEquatable<TargetPlan>;
    internal sealed record Model(
        string SourceFqn,
        string? Namespace,
        string ExtClassName,
        EquatableArray<TargetPlan> Targets,
        EquatableArray<SynthesizedMethod> Helpers,
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
        var resolver = new Resolver(compilation, diags, location);
        var hasError = false;

        // Declared targets across all [MapTo] attributes, in declaration order.
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

        // Per-member directives in source order; each aligns positionally to a [MapTo] target.
        var members = new List<(ISymbol Sym, ITypeSymbol Type, List<(bool Ignore, string? Name)> Directives)>();
        foreach (var (srcSym, srcType) in ReadableMembers(source))
        {
            var directives = ParseDirectives(srcSym);
            if (directives.Count > 1 && targetCount > 0 && directives.Count != targetCount)
            {
                diags.Add(new DiagnosticInfo(RegistryDiagnostics.MapPropertyArity, location, $"'{srcSym.Name}'"));
                hasError = true;
                directives = new List<(bool, string?)>();
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
            var chosen = new Dictionary<string, (ISymbol Sym, ITypeSymbol Type)>();
            foreach (var m in members)
            {
                string destName;
                if (m.Directives.Count == 0)
                {
                    destName = m.Sym.Name;
                }
                else
                {
                    var d = m.Directives.Count == 1 ? m.Directives[0] : m.Directives[ti];
                    if (d.Ignore) continue;
                    destName = !string.IsNullOrEmpty(d.Name) ? d.Name! : m.Sym.Name;
                }

                var w = writables.FirstOrDefault(x => x.Name == destName);
                if (w.Symbol is null) continue;

                if (chosen.ContainsKey(destName))
                {
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.ConflictingSource, location,
                        $"'{destName}' on '{target.Name}'"));
                    hasError = true;
                }
                else
                {
                    chosen[destName] = (m.Sym, m.Type);
                }
            }

            // Completeness + conversion: every writable destination must be satisfied by a convertible source.
            var assignments = new List<Assignment>();
            foreach (var w in writables)
            {
                if (!chosen.TryGetValue(w.Name, out var src))
                {
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.UnmappedDestination, location,
                        $"'{w.Name}' on '{target.Name}'"));
                    hasError = true;
                    continue;
                }

                var expr = resolver.Resolve(src.Type, w.Type, "source." + src.Sym.Name, w.Name);
                if (expr is null)
                {
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.NoConversion, location,
                        $"'{src.Sym.Name}' → '{w.Name}' on '{target.Name}'"));
                    hasError = true;
                }
                else
                {
                    assignments.Add(new Assignment(w.Name, expr));
                }
            }

            plans.Add(new TargetPlan(targetFqn, "To" + target.Name, new EquatableArray<Assignment>(assignments.ToArray())));
        }

        var ns = source.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : null;
        var helpers = resolver.Synth.Values.OrderBy(h => h.Name, System.StringComparer.Ordinal).ToArray();
        return new Model(
            source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ns,
            "__DwarfRegistry_" + source.Name,
            new EquatableArray<TargetPlan>(plans.ToArray()),
            new EquatableArray<SynthesizedMethod>(helpers),
            new EquatableArray<DiagnosticInfo>(diags.ToArray()),
            hasError);
    }

    /// <summary>Chains the core conversion engine, then collections, then self-contained nested objects.</summary>
    private sealed class Resolver
    {
        public readonly Dictionary<string, SynthesizedMethod> Synth = new();
        private readonly Compilation _comp;
        private readonly List<DiagnosticInfo> _diags;
        private readonly LocationInfo? _loc;
        private readonly HashSet<string> _inProgress = new();

        public Resolver(Compilation comp, List<DiagnosticInfo> diags, LocationInfo? loc)
        {
            _comp = comp;
            _diags = diags;
            _loc = loc;
        }

        /// <summary>RHS expression assigning <paramref name="srcExpr"/> (of <paramref name="srcType"/>) into a member of <paramref name="tgtType"/>, or null.</summary>
        public string? Resolve(ITypeSymbol srcType, ITypeSymbol tgtType, string srcExpr, string targetName)
        {
            var conv = _comp.ClassifyCommonConversion(srcType, tgtType);
            if (conv.IsIdentity || conv.IsImplicit) return srcExpr;

            var n = NumericConverter.TryCreate(srcType, tgtType, Synth);
            if (n is not null) return $"{n}({srcExpr})";

            var p = ParsableConverter.TryCreate(_comp, srcType, tgtType, Synth);
            if (p is not null) return $"{p}({srcExpr})";

            var e = EnumConverter.TryCreate(srcType, tgtType, EnumStrategy.ByName, Synth, _loc, targetName, _diags);
            if (e is not null) return $"{e}({srcExpr})";

            var coll = TryCollection(srcType, tgtType, srcExpr, targetName);
            if (coll is not null) return coll;

            if (srcType is INamedTypeSymbol ns && tgtType is INamedTypeSymbol nt && IsObjectType(ns) && IsObjectType(nt))
            {
                var m = SynthNested(ns, nt, targetName);
                return m is null ? null : $"{m}({srcExpr})";
            }

            return null;
        }

        private string? SynthNested(INamedTypeSymbol src, INamedTypeSymbol tgt, string targetName)
        {
            var key = Key(src, tgt);
            var name = "__DwarfMapObj_" + Hash(key);
            if (Synth.ContainsKey(name)) return name;
            if (!_inProgress.Add(key))
            {
                _diags.Add(new DiagnosticInfo(RegistryDiagnostics.RecursiveNesting, _loc, $"'{src.Name}' → '{tgt.Name}'"));
                return null;
            }

            var lines = new List<string>();
            var ok = true;
            var readable = ReadableMembers(src).ToList();
            foreach (var w in WritableMembers(tgt))
            {
                var sm = readable.FirstOrDefault(r => r.Symbol.Name == w.Name);
                if (sm.Symbol is null)
                {
                    _diags.Add(new DiagnosticInfo(RegistryDiagnostics.UnmappedDestination, _loc, $"'{w.Name}' on '{tgt.Name}'"));
                    ok = false;
                    continue;
                }
                var expr = Resolve(sm.Type, w.Type, "s." + sm.Symbol.Name, w.Name);
                if (expr is null)
                {
                    _diags.Add(new DiagnosticInfo(RegistryDiagnostics.NoConversion, _loc, $"'{sm.Symbol.Name}' → '{w.Name}' on '{tgt.Name}'"));
                    ok = false;
                }
                else
                {
                    lines.Add($"            {w.Name} = {expr},");
                }
            }
            _inProgress.Remove(key);
            if (!ok) return null;

            var fqTgt = Fq(tgt);
            var fqSrc = Fq(src);
            var ctor = $"new {fqTgt}\n        {{\n{string.Join("\n", lines)}\n        }}";
            // Reference-type source: null-propagate; value-type source: can't be null.
            var body = src.IsReferenceType
                ? $"    private static {fqTgt} {name}({fqSrc} s) => s is null ? default! : {ctor};\n"
                : $"    private static {fqTgt} {name}({fqSrc} s) => {ctor};\n";
            Synth[name] = new SynthesizedMethod(name, body);
            return name;
        }

        private string? TryCollection(ITypeSymbol srcType, ITypeSymbol tgtType, string srcExpr, string targetName)
        {
            // Destination must be U[] or List<U>.
            ITypeSymbol? dElem = null;
            var dstArray = false;
            if (tgtType is IArrayTypeSymbol da)
            {
                dElem = da.ElementType;
                dstArray = true;
            }
            else if (tgtType is INamedTypeSymbol dn && dn.TypeArguments.Length == 1
                     && dn.Name == "List" && dn.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
            {
                dElem = dn.TypeArguments[0];
            }
            if (dElem is null) return null;

            if (!CollectionConverter.TryGetEnumerableElement(srcType, out var sElem, out _)) return null;

            var elemConv = Resolve(sElem, dElem, "x", targetName);
            if (elemConv is null) return null;

            var key = "Coll|" + Fq(srcType) + "|" + Fq(tgtType);
            var name = "__DwarfMapColl_" + Hash(key);
            if (!Synth.ContainsKey(name))
            {
                var fqSrc = Fq(srcType);
                var fqDElem = Fq(dElem);
                var sb = new StringBuilder();
                sb.Append("    private static ").Append(Fq(tgtType)).Append(' ').Append(name)
                  .Append('(').Append(fqSrc).Append(" s)\n    {\n");
                sb.Append("        if (s is null) return ")
                  .Append(dstArray ? $"global::System.Array.Empty<{fqDElem}>()" : $"new global::System.Collections.Generic.List<{fqDElem}>()")
                  .Append(";\n");
                sb.Append("        var __r = new global::System.Collections.Generic.List<").Append(fqDElem).Append(">();\n");
                sb.Append("        foreach (var x in s) __r.Add(").Append(elemConv).Append(");\n");
                sb.Append("        return __r").Append(dstArray ? ".ToArray()" : "").Append(";\n");
                sb.Append("    }\n");
                Synth[name] = new SynthesizedMethod(name, sb.ToString());
            }
            return $"{name}({srcExpr})";
        }

        private static string Key(ITypeSymbol a, ITypeSymbol b) => "Obj|" + Fq(a) + "|" + Fq(b);
    }

    // ── member enumeration / helpers ─────────────────────────────────────────────
    private static bool IsMappableTarget(INamedTypeSymbol target, INamedTypeSymbol source) =>
        !SymbolEqualityComparer.Default.Equals(target, source)
        && (target.TypeKind == TypeKind.Class || target.TypeKind == TypeKind.Struct)
        && !target.IsAbstract;

    private static bool IsObjectType(INamedTypeSymbol t) =>
        (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Struct)
        && t.SpecialType == SpecialType.None
        && !t.IsAbstract
        && !t.AllInterfaces.Any(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

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

    /// <summary>A member's [MapProperty]/[MapIgnore] directives in source order; i-th → i-th [MapTo] target.</summary>
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

    private static string Fq(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in s) { h ^= c; h *= 16777619u; }
            return h.ToString("x8", CultureInfo.InvariantCulture);
        }
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
                sb.Append(i3).Append("    ").Append(a.DestMember).Append(" = ").Append(a.Expr).AppendLine(",");
            }
            sb.Append(i3).AppendLine("};");
            sb.Append(i2).AppendLine("}");
        }

        foreach (var h in model.Helpers)
        {
            sb.AppendLine();
            sb.Append(h.Code);
        }

        sb.Append(indent).AppendLine("}");
        if (model.Namespace is not null) sb.AppendLine("}");
        return sb.ToString();
    }
}
