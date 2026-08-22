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

    /// <summary>
    ///     The registry front door has no <c>AllowNonPublic</c> equivalent: <c>[MapTo]</c> takes no options, and
    ///     the extension methods it generates are ordinary public code. So its member enumeration is
    ///     public-only, and it has no compilation context to reason about <c>[InternalsVisibleTo]</c> with.
    ///     ISSUE-044 removed the defaults from <see cref="MemberFacts" /> so this stays a stated decision
    ///     rather than an inherited one — if <c>[MapTo]</c> ever grows the option, these two are what change.
    /// </summary>
    private const Compilation? RegistryCompilation = null;

    /// <inheritdoc cref="RegistryCompilation" />
    private const bool RegistryAllowNonPublic = false;

    private const string MapToAttr = "DwarfMapper.MapToAttribute";

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

        // Assembly-level configuration, through the SAME reader the [DwarfMapper] class model resolves its
        // defaults with. [MapTo] takes no options of its own, so the assembly defaults are the whole option
        // list here — there is no per-mapper layer above them. Reading the shared resolution rather than
        // re-parsing the attribute is what brings the mapper path's guards along for free: a non-bool value
        // falls through to the built-in default in one place, and an option DwarfMapperDefaults does not
        // declare at all (MaxDepth, GenerateExtensions) is refused by the compiler before this runs.
        var assemblyOptions = AssemblyConfiguration.OptionsFor(compilation);
        var explicitOnly = !MapperExtractor.ReadAutoMatchMembers(assemblyOptions);

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
        var members = new List<(ISymbol Sym, ITypeSymbol Type, List<MemberDirective> Directives)>();
        foreach (var (srcSym, _, srcType) in MemberFacts.Readable(source, RegistryCompilation, RegistryAllowNonPublic))
        {
            var directives = MemberDirectives.Read(srcSym);

            // TWO arities can be wrong here and only one of them was checked. The second — how many values
            // ONE [MapProperty] carries — reached this loop as a directive with no name at all, because
            // ParseDirectives reads a name only off a one-argument application and yields null for anything
            // else; the member then fell back to binding its OWN name a few lines below. So the caller who
            // wrote the class model's two-name METHOD form on a registry member got a binding they did not
            // ask for, or (where the fallback happened to satisfy the destination) exactly the mapping they
            // would have had with no attribute at all — and in neither case a word from the build.
            //
            // Same descriptor, deliberately: DWARFR04's TITLE already says "the value count does not match
            // the targets", which is precisely this statement made about one attribute's values rather than
            // about how many attributes were stacked. (The MessageFormat itself is a report-time pass-through
            // now — each branch below composes its own remedy — but the one descriptor covers both mistakes.)
            // Checked BEFORE the stacked-count rule so a member that gets both wrong reports the arity that is
            // actually the mistake, and reported once either way.
            if (HasNonMemberFormMapProperty(directives))
            {
                diags.Add(new DiagnosticInfo(RegistryDiagnostics.MapPropertyArity, location,
                    $"[MapProperty] on '{srcSym.Name}' uses the [DwarfMapper] class model's two-argument "
                    + "method form; the registry's member form takes only the single destination name this "
                    + "member supplies — drop the first argument."));
                hasError = true;
                directives = new List<MemberDirective>();
            }
            else if (directives.Count > 1 && targetCount > 0 && directives.Count != targetCount)
            {
                diags.Add(new DiagnosticInfo(RegistryDiagnostics.MapPropertyArity, location,
                    $"[MapProperty] on '{srcSym.Name}' must have either one value (all targets) or exactly "
                    + "one value per [MapTo] target, in order."));
                hasError = true;
                directives = new List<MemberDirective>();
            }

            // The [MapIgnore] half of the arity question, decided with B15 (round 22 W1): an argument on the
            // registry's member-form [MapIgnore] was accepted and DISCARDED — the annotated member was
            // ignored, the argument named nothing, and nobody said so, while the identical text on a
            // co-located host member is refused as DWARF089. A Warning that keeps the behaviour: the ignore
            // still acts (so `continue` below is untouched), the discard is just no longer silent.
            foreach (var d in directives)
                if (d.Ignore && d.ArgumentCount != 0)
                {
                    var written = d.Name is null ? "[MapIgnore(…)]" : $"[MapIgnore(\"{d.Name}\")]";
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.MapIgnoreArgumentNotRead, d.Loc ?? location,
                        $"{written} on '{srcSym.Name}' carries an argument the [MapTo] registry does not read: "
                        + "the registry's member form ignores the ANNOTATED source member itself, so the "
                        + "argument names nothing here (the member is still ignored). Write the bare "
                        + "[MapIgnore], or map the pair with the [DwarfMapper] class model, where "
                        + "[MapIgnore(\"Member\")] names a destination member to exclude."));
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
                    $"[MapTo] target '{target.ToDisplayString()}' has no public parameterless constructor; the "
                    + "registry constructs the types it builds with an object initializer — add one, or map "
                    + "the pair with the [DwarfMapper] class model, which supports constructor mapping"));
                hasError = true;
                continue;
            }

            ReportUnreadConstructorDirective(target, diags, location);

            var targetFqn = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var writables = MemberFacts.Writable(target, RegistryCompilation, RegistryAllowNonPublic).ToList();

            // destName -> chosen source member (resolved per target, independently).
            var chosen = new Dictionary<string, (ISymbol Sym, ITypeSymbol Type)>(StringComparer.Ordinal);
            // Destinations the trust boundary refused below. Kept so the completeness gate does not go on to
            // report DWARFR02 about them: "has no source member" would be false — it has one, and refusing to
            // wire it is the entire point. Two diagnostics about one member, one of them a lie, is how a caller
            // ends up reading the wrong one.
            var autoMatchRefused = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in members)
            {
                string destName;
                // Whether the caller NAMED this destination, as opposed to the names happening to line up.
                // That distinction is the trust boundary: an explicit binding is a decision, a by-name match is
                // the mass-assignment surface.
                var namedByCaller = false;
                if (m.Directives.Count == 0)
                {
                    destName = m.Sym.Name;
                }
                else
                {
                    var d = m.Directives.Count == 1 ? m.Directives[0] : m.Directives[ti];
                    if (d.Ignore) continue;
                    // A [MapProperty] whose one argument is not a usable constant string names nothing (see
                    // MemberDirectives.Name), so the binding below is still the member's own name — an
                    // implicit match, and treated as one here rather than credited to the attribute's presence.
                    if (!string.IsNullOrEmpty(d.Name))
                    {
                        destName = d.Name!;
                        namedByCaller = true;
                    }
                    else
                    {
                        destName = m.Sym.Name;
                    }
                }

                var w = writables.FirstOrDefault(x => x.Name == destName);
                if (w.Symbol is null) continue;

                // Explicit-only (trust boundary): the by-name match must NOT silently auto-wire. Exactly the
                // rule MapperExtractor applies for [DwarfMapper(AutoMatchMembers = false)] — the completeness
                // gate would never notice, because the member IS mapped. Refuse it and make the caller decide.
                // Deliberately NOT propagated into SynthNested below, for the same reason the class model does
                // not propagate it into an auto-synthesized nested mapper: the boundary guards the pair the
                // caller declared, and a synthesized helper has no member-level directives to satisfy it with.
                if (explicitOnly && !namedByCaller)
                {
                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.AutoMatchDisabled, location,
                        $"'{destName}' on '{target.Name}'"));
                    hasError = true;
                    autoMatchRefused.Add(destName);
                    continue;
                }

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
                    // Already refused by the trust boundary; DWARFR02 would contradict DWARFR10 about the
                    // same member.
                    if (autoMatchRefused.Contains(w.Name)) continue;

                    diags.Add(new DiagnosticInfo(RegistryDiagnostics.UnmappedDestination, location,
                        $"'{w.Name}' on '{target.Name}'"));
                    hasError = true;
                    continue;
                }

                resolver.ClearRefusalReported();
                var expr = resolver.Resolve(src.Type, w.Type, "source." + src.Sym.Name, w.Name);
                if (expr is null)
                {
                    // Silent when the refusal already said why (see Resolver.RefusalReported): the types are
                    // compatible, so "incompatible member types" would send the caller to fix the wrong thing.
                    if (!resolver.RefusalReported)
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
        // Public extension class only when the assembly OPTED IN with
        // [assembly: DwarfMapperOptions(PublicExtensions = true)] and the source and every target are
        // effectively public — a public method on an internal type would not compile, so the type check
        // narrows the opt-in rather than substituting for it.
        //
        // The opt-in used to be missing here: this front door read no assembly configuration and chose public
        // whenever the types allowed it. That contradicted the option's own documented contract ("Defaults to
        // false — all generated extensions are assembly-internal") and meant a caller got the assembly default
        // honoured for their [DwarfMapper] classes and quietly overridden for their [MapTo] types. Making the
        // registry read the SAME resolution the facade reads is what closes that; the cost is that a library
        // shipping [MapTo] types for another assembly to consume must now say so, exactly as one shipping
        // [DwarfMapper] classes always had to.
        var isPublic = AssemblyConfiguration.PublicExtensions(compilation)
                       && IsAccessiblePublic(source) && targets.All(IsAccessiblePublic);
        return new Model(
            source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            // Carried as a bool rather than re-derived in Emit, which sees only the cache-safe model and no
            // symbols at all. [MapTo] is legal on a struct, and a struct source cannot be null — see
            // TypeFacts.CanBeNull for why "can be null" is not the same question as "is a reference type".
            TypeFacts.CanBeNull(source),
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

    /// <summary>
    ///     Says so when a type this front door is about to build with <c>new T { … }</c> carries
    ///     <c>[DwarfMapperConstructor]</c> — a directive the registry has never read, because it selects no
    ///     constructor anywhere (<c>DWARFR11</c>).
    ///     <para>
    ///         One function with a call site at EVERY type the registry constructs, which is two: the
    ///         <c>[MapTo]</c> target itself, and the nested-object helper <c>SynthNested</c> synthesizes —
    ///         which is also the path a collection ELEMENT reaches, so a <c>List&lt;LeafDto&gt;</c> member
    ///         whose element type is annotated is covered by the same call. Reporting only at the target would
    ///         have left the identical silence one level down, which is the shape this branch has had to
    ///         unpick repeatedly.
    ///     </para>
    ///     <para>
    ///         The predicate is <see cref="ConstructorSelector.HasAnnotatedConstructor" />, i.e. the very
    ///         selector this front door does not call, so "an annotated constructor exists" cannot come to
    ///         mean one thing here and another there.
    ///     </para>
    /// </summary>
    private static void ReportUnreadConstructorDirective(
        INamedTypeSymbol target, List<DiagnosticInfo> diags, LocationInfo? location)
    {
        if (ConstructorSelector.HasAnnotatedConstructor(target))
            diags.Add(new DiagnosticInfo(RegistryDiagnostics.ConstructorDirectiveNotRead, location,
                target.ToDisplayString()));
    }

    private static bool IsObjectType(INamedTypeSymbol t)
    {
        return (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Struct)
               && t.SpecialType == SpecialType.None
               && !t.IsAbstract
               && !t.AllInterfaces.Any(i =>
                   i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
    }

    /// <summary>
    ///     Whether some <c>[MapProperty]</c> on this member was written with an overload OTHER than the
    ///     one-name member form — in practice the class model's <c>[MapProperty(source, target)]</c>, the only
    ///     other constructor the attribute has.
    ///     <para>
    ///         Keyed on "not one argument" rather than on "exactly two", so a constructor added later is
    ///         refused here by default instead of being silently accepted and discarded, which is the failure
    ///         this check exists to end.
    ///     </para>
    ///     <para>
    ///         Asked of the PARSED directives rather than of the attribute list a second time, so the arity
    ///         this check judges is the one <see cref="MemberDirectives.Read" /> actually recorded. The
    ///         <c>[MapIgnore]</c> half is judged separately (<c>DWARFR12</c>, decided with B15): the registry
    ///         still accepts <c>[MapIgnore("x")]</c> as a plain ignore of the annotated member, but the
    ///         discarded argument is now reported instead of dropped in silence.
    ///     </para>
    /// </summary>
    private static bool HasNonMemberFormMapProperty(List<MemberDirective> directives) =>
        directives.Exists(d => !d.Ignore && d.ArgumentCount != 1);

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
                    // Only a source that CAN be null gets the guard. Emitted unconditionally, it made every
                    // [MapTo] on a struct produce CS0037 — `is null` against a non-nullable value type is not
                    // a check the compiler will even parse. See Model.SourceCanBeNull.
                    if (model.SourceCanBeNull)
                        w.Line(
                            "if (source is null) throw new global::System.ArgumentNullException(nameof(source));");
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
                        // Same rule as the generic dispatcher above, and the reason it is one flag on the
                        // model rather than a test repeated here: the two sites disagreeing is exactly how
                        // this path came to differ from the nested-helper one.
                        if (model.SourceCanBeNull)
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
        bool SourceCanBeNull,
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
        private readonly HashSet<string> _inProgress = new(StringComparer.Ordinal);
        private readonly LocationInfo? _loc;
        public readonly Dictionary<string, SynthesizedMethod> Synth = new(StringComparer.Ordinal);

        /// <summary>
        ///     Set by a LOUD refusal inside <see cref="Resolve" /> (<c>DWARFR06</c> recursive nesting,
        ///     <c>DWARFR09</c> unconstructible nested type) to tell the caller that the null it is about to
        ///     receive is already explained.
        ///     <para>
        ///         Without it the caller adds <c>DWARFR05</c> — <i>"the source and destination member types are
        ///         incompatible"</i> — on top, which is false: the types are perfectly compatible and the
        ///         registry's own construction strategy is what cannot express the map. Two diagnostics about
        ///         one member, one of them a lie, is how a caller ends up reading the wrong one — the same rule
        ///         that already keeps <c>DWARFR02</c> off a destination the trust boundary refused.
        ///     </para>
        ///     <para>
        ///         A field rather than an out parameter because the refusal propagates up through however many
        ///         levels <see cref="Resolve" /> recursed; each caller clears it immediately before its own
        ///         call, so a genuine no-conversion after an earlier member's refusal still reports.
        ///     </para>
        /// </summary>
        public bool RefusalReported { get; private set; }

        /// <summary>Clears <see cref="RefusalReported" />; call immediately before each <see cref="Resolve" />.</summary>
        public void ClearRefusalReported()
        {
            RefusalReported = false;
        }

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

            var e = EnumConverter.TryCreate(srcType, tgtType, EnumPolicy.Default, Synth, _loc, targetName, _diags);
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
                RefusalReported = true;
                return null;
            }

            // The second — and last — type this front door constructs. Same object initializer, so the SAME
            // two questions the target is asked have to be asked here, or the answer only holds one level up.
            ReportUnreadConstructorDirective(tgt, _diags, _loc);

            // B30: `new {fqTgt} { … }` below needs an accessible parameterless constructor exactly as the
            // target's does. Without this check a nested member — or a collection ELEMENT, which reaches here
            // through TryCollection → Resolve → SynthNested — whose type is a positional record or otherwise
            // ctor-only emitted CS1729 out of a generated file with no diagnostic at all: DWARFR09's own
            // reason for existing, one level down. Refused before the member walk, so the caller is told the
            // one true thing rather than a list of members that cannot be assigned into a type that cannot be
            // built.
            if (!HasParameterlessCtor(tgt))
            {
                _diags.Add(new DiagnosticInfo(RegistryDiagnostics.NoParameterlessConstructor, _loc,
                    // The annotation is dropped: `LeafDto?` names the MEMBER's nullability, not the type whose
                    // constructors are at fault, and reads as though the '?' were the problem.
                    $"nested type '{tgt.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString()}' "
                    + $"(reached through member '{targetName}') has no "
                    + "public parameterless constructor; the [MapTo] registry constructs every nested object "
                    + "— and every collection element — with an object initializer, so it cannot build this "
                    + "one. Add a public parameterless constructor, or map the pair with the [DwarfMapper] "
                    + "class model, which supports constructor mapping"));
                RefusalReported = true;
                _inProgress.Remove(key);
                return null;
            }

            var members = new List<(string Name, string Expr)>();
            var ok = true;
            var readable = MemberFacts.Readable(src, RegistryCompilation, RegistryAllowNonPublic).ToList();
            foreach (var w in MemberFacts.Writable(tgt, RegistryCompilation, RegistryAllowNonPublic))
            {
                var sm = readable.FirstOrDefault(r => r.Symbol.Name == w.Name);
                if (sm.Symbol is null)
                {
                    _diags.Add(new DiagnosticInfo(RegistryDiagnostics.UnmappedDestination, _loc,
                        $"'{w.Name}' on '{tgt.Name}'"));
                    ok = false;
                    continue;
                }

                ClearRefusalReported();
                var expr = Resolve(sm.Type, w.Type, "s." + sm.Symbol.Name, w.Name);
                if (expr is null)
                {
                    // Same rule one level down — and the level that made the cascade visible: a recursive
                    // nesting used to draw one DWARFR05 per level on the way back up.
                    if (!RefusalReported)
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
            // A source that can be null null-propagates; one that cannot has no null to propagate — and
            // `s is null` against it would not compile. The SAME predicate the extension methods use, which
            // is the point: this path had the discrimination and the other did not.
            var header = TypeFacts.CanBeNull(src)
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
                    // The THIRD site of the same question in this file, and the one reachable from a plain
                    // CLASS source: TryGetEnumerableElement admits any IEnumerable<T>, value types included,
                    // so an ImmutableArray<T> member reaches here and `s is null` against it is CS0037 —
                    // exactly the defect the extension methods had, with no struct in the caller's
                    // declaration at all. Omitted rather than wrapped, matching the other three sites: a
                    // value-type collection has no null case to return an empty destination for.
                    if (TypeFacts.CanBeNull(srcType))
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
