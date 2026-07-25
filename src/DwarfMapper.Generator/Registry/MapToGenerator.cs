// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Registry;

/// <summary>
///     The <c>[MapTo]</c> front door: scans the assembly for <c>[MapTo]</c> on plain types and emits static
///     extension methods (<c>src.MapTo&lt;TTarget&gt;()</c> / <c>src.To{Target}()</c>) — no user
///     <c>partial</c>. A multi-target map is N independent single-target resolutions, each running the
///     completeness gate. Conversions reuse the core engine (numeric / parse / enum), plus self-contained
///     nested-object mapping and List/array collections. Cyclic graphs are rejected (DWARFR06) — use the
///     <c>[DwarfMapper]</c> class model for those.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MapToGenerator : IIncrementalGenerator
{
    /// <summary>
    ///     Tracking name for the extraction step. Without one the step is anonymous, so a test cannot address
    ///     this pipeline at all — which is why DwarfGenerator had six incremental-caching tests and this
    ///     generator had none. Its model could stop being value-equatable and nothing would notice.
    /// </summary>
    internal const string ExtractStepName = "MapToExtract";

    /// <summary>Every tracked step in this generator, for the cacheability battery.</summary>
    internal static readonly string[] AllStepNames = { ExtractStepName };

    private const string MapToAttr = "DwarfMapper.MapToAttribute";
    private const string MapPropAttr = "DwarfMapper.MapPropertyAttribute";
    private const string MapIgnoreAttr = "DwarfMapper.MapIgnoreAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
                MapToAttr,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => Extract(ctx))
            .WithTrackingName(ExtractStepName);

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            foreach (var d in model.Diagnostics) spc.ReportDiagnostic(d.ToDiagnostic());

            if (model.HasError || model.Targets.Count == 0) return;

            spc.AddNormalizedSource($"{model.ExtClassName}.g.cs", Emit(model));
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
                if (tc.Value is INamedTypeSymbol t)
                    targets.Add(t);
        }

        var targetCount = targets.Count;

        // Per-member directives in source order; each aligns positionally to a [MapTo] target.
        var members = new List<(ISymbol Sym, ITypeSymbol Type, List<(bool Ignore, string? Name)> Directives)>();
        foreach (var (srcSym, _, srcType) in MemberFacts.Readable(source))
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

            if (!HasParameterlessCtor(target))
            {
                diags.Add(new DiagnosticInfo(RegistryDiagnostics.NoParameterlessConstructor, location,
                    target.ToDisplayString()));
                hasError = true;
                continue;
            }

            var targetFqn = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var writables = MemberFacts.Writable(target).ToList();

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

            plans.Add(new TargetPlan(targetFqn, "To" + target.Name,
                new EquatableArray<Assignment>(assignments.ToArray())));
        }

        // Two targets with the same SIMPLE name (Foo.Order + Bar.Order) both yield `ToOrder(this Src)` in one
        // static class → CS0111 out of generated code. Say so instead.
        foreach (var dup in plans.GroupBy(p => p.MethodName, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            diags.Add(new DiagnosticInfo(RegistryDiagnostics.DuplicateTargetMethodName, location,
                $"[MapTo] targets {string.Join(", ", dup.Select(p => p.TargetFqn))} share a simple name, so each "
                + $"would generate '{dup.Key}(this …)' — rename one target, or use the [DwarfMapper] class model "
                + "where every method is named explicitly"));
            hasError = true;
        }

        var ns = source.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : null;
        var helpers = resolver.Synth.Values.OrderBy(h => h.Name, StringComparer.Ordinal).ToArray();
        // Public extension class when the source and every target are effectively public, so callers in
        // OTHER assemblies can use x.MapTo<T>(); otherwise internal (a public method on an internal type
        // would not compile). Mirrors the class-model convenience-extension visibility policy.
        var isPublic = IsAccessiblePublic(source) && targets.All(IsAccessiblePublic);
        return new Model(
            source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ns,
            "__DwarfRegistry_" + source.Name,
            isPublic,
            new EquatableArray<TargetPlan>(plans.ToArray()),
            new EquatableArray<SynthesizedMethod>(helpers),
            new EquatableArray<DiagnosticInfo>(diags.ToArray()),
            hasError);
    }

    // ── member enumeration / helpers ─────────────────────────────────────────────
    private static bool IsAccessiblePublic(INamedTypeSymbol t)
    {
        for (var cur = t; cur is not null; cur = cur.ContainingType)
            if (cur.DeclaredAccessibility != Accessibility.Public)
                return false;
        return true;
    }

    private static bool IsMappableTarget(INamedTypeSymbol target, INamedTypeSymbol source)
    {
        return !SymbolEqualityComparer.Default.Equals(target, source)
               && (target.TypeKind == TypeKind.Class || target.TypeKind == TypeKind.Struct)
               && !target.IsAbstract;
    }

    /// <summary>
    ///     The registry emits <c>new T { … }</c>, so the target needs an accessible parameterless constructor.
    ///     A struct always has one. Reported as DWARFR09 rather than left to CS1729, which is what a ctor-only
    ///     target produced before: it has no writable members either, so the completeness gate never fired.
    /// </summary>
    private static bool HasParameterlessCtor(INamedTypeSymbol target)
    {
        return target.TypeKind == TypeKind.Struct
               || target.InstanceConstructors.Any(c =>
                   c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
    }

    private static bool IsObjectType(INamedTypeSymbol t)
    {
        return (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Struct)
               && t.SpecialType == SpecialType.None
               && !t.IsAbstract
               && !t.AllInterfaces.Any(i =>
                   i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
    }

    /// <summary>A member's [MapProperty]/[MapIgnore] directives in source order; i-th → i-th [MapTo] target.</summary>
    private static List<(bool Ignore, string? Name)> ParseDirectives(ISymbol member)
    {
        var ordered = new List<(string File, int Pos, bool Ignore, string? Name)>();
        foreach (var a in member.GetAttributes())
        {
            var cls = a.AttributeClass?.ToDisplayString();
            if (cls != MapPropAttr && cls != MapIgnoreAttr) continue;
            var reference = a.ApplicationSyntaxReference;
            // Span.Start alone orders attributes only within ONE file. A partial property (C# 13) can carry
            // directives in two files, where the spans are independent offsets and the ordering — which decides
            // WHICH [MapTo] target each directive binds to — would depend on GetAttributes()' cross-file order.
            // Including the file path makes it total and stable across builds.
            var file = reference?.SyntaxTree.FilePath ?? string.Empty;
            var pos = reference?.Span.Start ?? 0;
            if (cls == MapIgnoreAttr)
            {
                ordered.Add((file, pos, true, null));
            }
            else
            {
                var name = a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null;
                ordered.Add((file, pos, false, name));
            }
        }

        ordered.Sort((x, y) =>
        {
            var byFile = string.CompareOrdinal(x.File, y.File);
            return byFile != 0 ? byFile : x.Pos.CompareTo(y.Pos);
        });
        return ordered.Select(x => (x.Ignore, x.Name)).ToList();
    }

    private static string Fq(ITypeSymbol t)
    {
        return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string Emit(Model model)
    {
        var w = new CodeWriter();
        w.Line("// <auto-generated/>");
        w.Line("#nullable enable");

        using (model.Namespace is not null ? w.Block("namespace " + model.Namespace) : null)
        {
            var classHeader = (model.Public ? "public static class " : "internal static class ")
                               + model.ExtClassName;
            using (w.Block(classHeader))
            {
                using (w.Block("public static TTarget MapTo<TTarget>(this " + model.SourceFqn + " source)"))
                {
                    w.Line("if (source is null) throw new global::System.ArgumentNullException(nameof(source));");
                    foreach (var t in model.Targets)
                        w.Line("if (typeof(TTarget) == typeof(" + t.TargetFqn + ")) return (TTarget)(object)"
                               + t.MethodName + "(source);");
                    w.Line("throw new global::System.NotSupportedException(\"No [MapTo] mapping from "
                           + model.SourceFqn + " to \" + typeof(TTarget).FullName + \".\");");
                }

                foreach (var t in model.Targets)
                {
                    w.Line();
                    using (w.Block("public static " + t.TargetFqn + " " + t.MethodName + "(this "
                                    + model.SourceFqn + " source)"))
                    {
                        w.Line(
                            "if (source is null) throw new global::System.ArgumentNullException(nameof(source));");
                        w.Line("return new " + t.TargetFqn);
                        w.Line("{");
                        using (w.Indent())
                        {
                            foreach (var a in t.Assignments)
                                w.Line(a.DestMember + " = " + a.Expr + ",");
                        }

                        w.Line("};");
                    }
                }

                foreach (var h in model.Helpers)
                {
                    w.Line();
                    w.Raw(h.Code);
                }
            }
        }

        return w.ToString();
    }

    // ── equatable, symbol-free model (cache-safe) ───────────────────────────────
    internal sealed record Assignment(string DestMember, string Expr) : IEquatable<Assignment>;

    internal sealed record TargetPlan(string TargetFqn, string MethodName, EquatableArray<Assignment> Assignments)
        : IEquatable<TargetPlan>;

    internal sealed record Model(
        string SourceFqn,
        string? Namespace,
        string ExtClassName,
        bool Public,
        EquatableArray<TargetPlan> Targets,
        EquatableArray<SynthesizedMethod> Helpers,
        EquatableArray<DiagnosticInfo> Diagnostics,
        bool HasError) : IEquatable<Model>;

    /// <summary>Chains the core conversion engine, then collections, then self-contained nested objects.</summary>
    private sealed class Resolver
    {
        private readonly Compilation _comp;
        private readonly List<DiagnosticInfo> _diags;
        private readonly HashSet<string> _inProgress = new();
        private readonly LocationInfo? _loc;
        public readonly Dictionary<string, SynthesizedMethod> Synth = new();

        public Resolver(Compilation comp, List<DiagnosticInfo> diags, LocationInfo? loc)
        {
            _comp = comp;
            _diags = diags;
            _loc = loc;
        }

        /// <summary>
        ///     RHS expression assigning <paramref name="srcExpr" /> (of <paramref name="srcType" />) into a member of
        ///     <paramref name="tgtType" />, or null.
        /// </summary>
        public string? Resolve(ITypeSymbol srcType, ITypeSymbol tgtType, string srcExpr, string targetName)
        {
            var conv = _comp.ClassifyCommonConversion(srcType, tgtType);
            if (conv.IsIdentity) return srcExpr;
            if (conv.IsImplicit)
            {
                // An implicit conversion is a free direct assignment — EXCEPT across numeric categories
                // (long→double, int→float, long→decimal), which the compiler accepts silently while losing
                // precision. The class model reports DWARF038 here; without this the registry stayed silent, so
                // the same mapping was loud through [DwarfMapper] and quiet through [MapTo].
                if (NumericConverter.IsCrossCategoryLossy(srcType, tgtType))
                    _diags.Add(new DiagnosticInfo(RegistryDiagnostics.LossyImplicitConversion, _loc,
                        $"'{srcType.ToDisplayString()}' → '{tgtType.ToDisplayString()}' for '{targetName}'"));
                return srcExpr;
            }

            var n = NumericConverter.TryCreate(srcType, tgtType, Synth);
            if (n is not null) return $"{n}({srcExpr})";

            var p = ParsableConverter.TryCreate(_comp, srcType, tgtType, Synth);
            if (p is not null) return $"{p}({srcExpr})";

            var e = EnumConverter.TryCreate(srcType, tgtType, EnumStrategy.ByName, Synth, _loc, targetName, _diags);
            if (e is not null) return $"{e}({srcExpr})";

            var coll = TryCollection(srcType, tgtType, srcExpr, targetName);
            if (coll is not null) return coll;

            if (srcType is INamedTypeSymbol ns && tgtType is INamedTypeSymbol nt && IsObjectType(ns) &&
                IsObjectType(nt))
            {
                var m = SynthNested(ns, nt, targetName);
                return m is null ? null : $"{m}({srcExpr})";
            }

            return null;
        }

        private string? SynthNested(INamedTypeSymbol src, INamedTypeSymbol tgt, string targetName)
        {
            var key = Key(src, tgt);
            var name = "__DwarfMapObj_" + StableHash.Fnv1a(key);
            if (Synth.ContainsKey(name)) return name;
            if (!_inProgress.Add(key))
            {
                _diags.Add(new DiagnosticInfo(RegistryDiagnostics.RecursiveNesting, _loc,
                    $"'{src.Name}' → '{tgt.Name}'"));
                return null;
            }

            var members = new List<(string Name, string Expr)>();
            var ok = true;
            var readable = MemberFacts.Readable(src).ToList();
            foreach (var w in MemberFacts.Writable(tgt))
            {
                var sm = readable.FirstOrDefault(r => r.Symbol.Name == w.Name);
                if (sm.Symbol is null)
                {
                    _diags.Add(new DiagnosticInfo(RegistryDiagnostics.UnmappedDestination, _loc,
                        $"'{w.Name}' on '{tgt.Name}'"));
                    ok = false;
                    continue;
                }

                var expr = Resolve(sm.Type, w.Type, "s." + sm.Symbol.Name, w.Name);
                if (expr is null)
                {
                    _diags.Add(new DiagnosticInfo(RegistryDiagnostics.NoConversion, _loc,
                        $"'{sm.Symbol.Name}' → '{w.Name}' on '{tgt.Name}'"));
                    ok = false;
                }
                else
                {
                    members.Add((w.Name, expr));
                }
            }

            _inProgress.Remove(key);
            if (!ok) return null;

            var fqTgt = Fq(tgt);
            var fqSrc = Fq(src);
            // Reference-type source: null-propagate; value-type source: can't be null.
            var header = src.IsReferenceType
                ? $"private static {fqTgt} {name}({fqSrc} s) => s is null ? default! : new {fqTgt}"
                : $"private static {fqTgt} {name}({fqSrc} s) => new {fqTgt}";

            var bodyWriter = new CodeWriter(1);
            bodyWriter.Line(header);
            using (bodyWriter.Indent())
            {
                bodyWriter.Line("{");
                using (bodyWriter.Indent())
                {
                    foreach (var m in members) bodyWriter.Line($"{m.Name} = {m.Expr},");
                }

                bodyWriter.Line("};");
            }

            Synth[name] = new SynthesizedMethod(name, bodyWriter.ToString());
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
                                                    && dn.Name == "List" && dn.ContainingNamespace?.ToDisplayString() ==
                                                    "System.Collections.Generic")
            {
                dElem = dn.TypeArguments[0];
            }

            if (dElem is null) return null;

            if (!CollectionConverter.TryGetEnumerableElement(srcType, out var sElem, out var sCount)) return null;

            var elemConv = Resolve(sElem, dElem, "x", targetName);
            if (elemConv is null) return null;

            var key = "Coll|" + Fq(srcType) + "|" + Fq(tgtType);
            var name = "__DwarfMapColl_" + StableHash.Fnv1a(key);
            if (!Synth.ContainsKey(name))
            {
                var fqSrc = Fq(srcType);
                var fqDElem = Fq(dElem);
                var fqTgt = Fq(tgtType);
                var emptyExpr = dstArray
                    ? $"global::System.Array.Empty<{fqDElem}>()"
                    : $"new global::System.Collections.Generic.List<{fqDElem}>()";

                var bodyWriter = new CodeWriter(1);
                using (bodyWriter.Block($"private static {fqTgt} {name}({fqSrc} s)"))
                {
                    bodyWriter.Line($"if (s is null) return {emptyExpr};");
                    // ISSUE-020: the element count was available from TryGetEnumerableElement and thrown away, so
                    // this buffer grew by repeated reallocation even when the source's size was known up front.
                    // The class engine pre-sizes from that very helper; the registry simply never used the value
                    // it asked for.
                    bodyWriter.Line(
                        $"var __r = new global::System.Collections.Generic.List<{fqDElem}>({CountExpr(sCount)});");
                    bodyWriter.Line($"foreach (var x in s) __r.Add({elemConv});");
                    bodyWriter.Line($"return __r{(dstArray ? ".ToArray()" : "")};");
                }

                Synth[name] = new SynthesizedMethod(name, bodyWriter.ToString());
            }

            return $"{name}({srcExpr})";
        }

        /// <summary>Capacity argument for a pre-sized buffer, or empty when the source size is unknown.</summary>
        private static string CountExpr(CollectionConverter.CountKind count)
        {
            return count switch
            {
                CollectionConverter.CountKind.Length => "s.Length",
                CollectionConverter.CountKind.Count => "s.Count",
                _ => string.Empty,
            };
        }

        private static string Key(ITypeSymbol a, ITypeSymbol b)
        {
            return "Obj|" + Fq(a) + "|" + Fq(b);
        }
    }
}
