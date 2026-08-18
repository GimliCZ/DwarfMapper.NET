// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static partial class MapperExtractor
{
    /// <summary>
    ///     Projection enumerates PUBLIC members only, and this constant is the single place that says so.
    ///     <para>
    ///         <c>AllowNonPublic</c> is honoured by the runtime path and deliberately refused here: a
    ///         projection becomes an expression tree that a query provider translates, and it cannot read a
    ///         non-public member. Accepting the option and quietly resolving the member anyway would produce
    ///         silently wrong data — so an unmatched non-public member is reported as DWARF028 naming the real
    ///         reason (see <c>ResolveProjectionMembers</c>), not resolved.
    ///     </para>
    ///     <para>
    ///         ISSUE-044 made <c>compilation</c>/<c>allowNonPublic</c> required on the member-lookup wrappers
    ///         precisely so a site like this states its choice instead of inheriting a default. Threading the
    ///         mapper's real <c>allowNonPublic</c> in here compiles fine and breaks the contract —
    ///         <c>OptionContractTests</c> and <c>ProjectionRuntimeParityTests</c> catch it.
    ///     </para>
    /// </summary>
    private const bool ProjectionPublicOnly = false;

    private static bool IsQueryable(ITypeSymbol type, out ITypeSymbol element)
    {
        element = type;
        if (type is INamedTypeSymbol n && n.Name == "IQueryable" && n.TypeArguments.Length == 1
            && n.ContainingNamespace?.ToDisplayString() == "System.Linq")
        {
            element = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Emits DWARF028 (ProjectionNotTranslatable) with a fully-formatted single-arg message.
    ///     The descriptor uses "{0}" so both member name and reason are concatenated here.
    /// </summary>
    private static void EmitDWARF028(
        List<DiagnosticInfo> diagnostics,
        LocationInfo? location,
        string memberName,
        string reason)
    {
        var msg = $"Projection member '{memberName}' cannot be translated to SQL: {reason}.";
        diagnostics.Add(new DiagnosticInfo(
            DiagnosticDescriptors.ProjectionNotTranslatable, location, msg));
    }

    /// <summary>
    ///     Source-member lookup shared by all three projection resolvers.
    ///     <para>
    ///     Was <c>GroupBy(name, comparer).ToDictionary(g =&gt; g.Key, g =&gt; g.First())</c>, which under
    ///     <c>CaseInsensitive = true</c> SILENTLY first-picked one of two members differing only in case
    ///     (<c>Foo</c> / <c>foo</c> are distinct symbols and <c>ReadableMembers</c> de-duplicates by Ordinal, so
    ///     both reach the group). Three defects in one: it was silent (the library's "never silent" tenet), it
    ///     disagreed with the runtime map path — which reports <see cref="DiagnosticDescriptors.AmbiguousMatch" />
    ///     for the very same input, so the answer depended on whether you called <c>.Map</c> or the projection —
    ///     and the winner was whichever member <c>GetMembers()</c> yielded first, which for a partial source type
    ///     split across files is not stable, so two builds could emit different expression trees (H1).
    ///     </para>
    ///     Reports DWARF010 and binds NOTHING for an ambiguous group, exactly like the runtime path.
    /// </summary>
    /// <summary>
    ///     Name comparer for <see cref="NameConvention.Flexible" />: two names are equal when their normalized
    ///     forms are (<c>NormalizeName</c> strips <c>_</c> and lowercases), so <c>user_id</c> and <c>UserId</c>
    ///     match.
    ///     <para>
    ///         Projection resolution already threads a <see cref="StringComparer" /> through every nested and
    ///         constructor resolver (the C4 case-insensitivity fix), so expressing Flexible AS a comparer makes
    ///         it propagate everywhere that case-insensitivity already does — including the DWARF010 ambiguity
    ///         grouping, which then reports two source members that collide only after normalization, exactly
    ///         as the runtime path does.
    ///     </para>
    /// </summary>
    private sealed class FlexibleNameComparer : StringComparer
    {
        public static readonly FlexibleNameComparer Instance = new();

        public override int Compare(string? x, string? y)
        {
            return string.CompareOrdinal(x is null ? null : NormalizeName(x), y is null ? null : NormalizeName(y));
        }

        public override bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(NormalizeName(x), NormalizeName(y), StringComparison.Ordinal);
        }

        public override int GetHashCode(string obj)
        {
            return obj is null ? 0 : NormalizeName(obj).GetHashCode();
        }
    }

    private static Dictionary<string, (string Name, ITypeSymbol Type)> BuildProjectionSourceLookup(
        ITypeSymbol sourceType, StringComparer comparer, Compilation compilation,
        LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        var sources = new Dictionary<string, (string Name, ITypeSymbol Type)>(comparer);
        // PUBLIC ONLY, deliberately — see ProjectionPublicOnly. Naming the choice at the site rather than
        // inheriting a default is the point of ISSUE-044.
        foreach (var group in ReadableMembers(sourceType, compilation, ProjectionPublicOnly)
                     .GroupBy(m => m.Name, comparer))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, group.Key));
                continue;
            }

            sources[group.Key] = members[0];
        }

        return sources;
    }

    /// <summary>
    ///     New (Plan 19D) recursive projection resolver. Produces a list of
    ///     <see cref="ProjectionMemberMap" /> with inline expression fragments (no helper calls).
    ///     Projection translatability: every non-translatable projection member is reported as
    ///     DWARF028 (ProjectionNotTranslatable) with a specific reason — including the
    ///     [MapProperty(Use=)] attribute-conflict case and all type-conversion unsafety
    ///     (narrowing numeric, parsable string↔T, enum by-name, non-translatable collections,
    ///     reference handling, and the "no translatable conversion found" fallback).
    ///     (DWARF019/NotProjectable was retired in favour of DWARF028's reason-carrying messages.)
    /// </summary>
    /// <param name="flattenRoots">
    ///     The <c>[Flatten("Root")]</c> directives on the projection method. Threaded here because this
    ///     resolver is a SECOND translator, not a caller of the first: it used to receive no flatten at all,
    ///     so <c>[Flatten]</c> resolved through <c>.Map</c> and was discarded without a word through
    ///     <c>.Project</c> — the same mapper filling the flattened members on one overload and leaving them
    ///     unmapped on the other (recorded as <c>D10</c>, now closed). A pulled-up leaf is
    ///     <c>__s.Root.Leaf</c>, which is the canonical navigation access every query provider translates, so
    ///     the honest answer here is to honour it rather than to refuse it.
    /// </param>
    private static List<ProjectionMemberMap> ResolveProjectionMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        EnumPolicy enumPolicy, int referenceHandling, string paramExpr, int nameConvention = 0,
        IReadOnlyList<(string Target, bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>?
            mapPropertyExtras = null,
        bool skipNullSourceMembers = false, bool allowNonPublic = false,
        bool explicitOnly = false, bool ignoreObsolete = false, bool autoNest = true,
        HashSet<string>? consumedSources = null, IReadOnlyList<string>? flattenRoots = null)
    {
        // IgnoreObsoleteMembers, target side: fold obsolete destination members into the ignore set,
        // exactly as ResolveMembers does, so every downstream check honours it through one addition. An
        // obsolete member that IS explicitly targeted stays out of the set — opting a retired member back
        // in deliberately must keep working at both endpoints.
        if (ignoreObsolete)
        {
            var explicitTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var em in explicitMaps) explicitTargets.Add(em.Target);
            ignores = new HashSet<string>(ignores, IgnoreNameComparer);
            foreach (var name in ObsoleteMemberNames(targetType))
                if (!explicitTargets.Contains(name))
                    ignores.Add(name);
        }

        // Targets the runtime COULD have deferred under SkipNullSourceMembers (settable, not init-only, not
        // required). Mirrors ResolveMembers' rule so the projection diagnostic fires on exactly the members the
        // option would have changed — no more, no less.
        var deferrableTargets = new HashSet<string>(StringComparer.Ordinal);
        if (skipNullSourceMembers)
            for (var t = targetType; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            foreach (var tm in t.GetMembers())
                if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: false } && !p.IsRequired)
                    deferrableTargets.Add(p.Name);
                else if (tm is IFieldSymbol f && !f.IsReadOnly && !f.IsConst && !f.IsRequired)
                    deferrableTargets.Add(f.Name);

        // Per-member [MapProperty] modifiers, keyed by target — checked against the translatable set below.
        var extrasByTarget =
            new Dictionary<string, (bool HasNullSub, string? When)>(StringComparer.Ordinal);
        if (mapPropertyExtras is not null)
            foreach (var e in mapPropertyExtras)
                extrasByTarget[e.Target] = (e.HasNullSub, e.When);

        // NameConvention.Flexible (1) must reach the projection path too, or the SAME mapper resolves members
        // one way through .Map and another through .Project — the divergence the ambiguity fix below exists to
        // prevent. Expressed as a comparer so it rides the existing propagation into nested/ctor resolvers.
        var comparer = nameConvention == 1
            ? FlexibleNameComparer.Instance
            : caseInsensitive
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        var sources = BuildProjectionSourceLookup(sourceType, comparer, compilation, location, diagnostics);

        // [Flatten] roots, through the SAME walk the runtime resolver uses — one refusal of an invalid root
        // rather than two that can disagree. PUBLIC ONLY (ProjectionPublicOnly) because a leaf the provider
        // cannot read is not a leaf this endpoint can pull up, and warnNullableHop: false because DWARF044
        // describes a null dereference in emitted C#, which a translated path does not perform (the dotted
        // [MapProperty] source below already makes that call, in the same words).
        var flattenInfos = ResolveFlattenInfos(flattenRoots ?? Array.Empty<string>(), sourceType, comparer,
            compilation, ProjectionPublicOnly, warnNullableHop: false, location, diagnostics);

        // C4: pass comparer to nested resolvers so CaseInsensitive propagates into nested objects.
        var writableByName = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType, compilation, ProjectionPublicOnly))
            writableByName[m.Name] = m.Type;

        var writableMembers = WritableMembers(targetType, compilation, ProjectionPublicOnly)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        // The constructor this projection will actually call, decided BEFORE the explicit maps are read
        // because a [MapProperty] may target one of its parameters. R18-32: the parameters used to bind by
        // NAME only, so an explicit map aimed at one was ignored — and then DWARF024 recommended
        // `[MapProperty(src, "<paramName>")]`, which is what the author had just written.
        var (ctorDecided, usesCtorProjection, projectionCtor) = ChooseProjectionConstructor(
            targetType, sourceType, writableMembers.Count, compilation, location, diagnostics, explicitMaps);
        // Selection reported a BLOCKING DWARF025/DWARF026 and has no constructor to offer. Resolving members
        // on top of that would append a second, contradictory complaint to a build that already fails; the
        // create-map path does the same thing (`if (ctor is null) continue;`).
        if (!ctorDecided) return new List<ProjectionMemberMap>();

        // Parameter name → type, for resolving an explicit map whose target is a parameter rather than a
        // member. Ordinal, matching ResolveConstructorArguments' explicit-map index. Empty when the target
        // will be built by member-init, where a map naming a parameter stays DWARF008 (unknown destination)
        // — the constructor is not used there, so there is nothing for it to bind to.
        var ctorParamTypes = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        if (projectionCtor is not null)
            foreach (var p in projectionCtor.Parameters)
                ctorParamTypes[p.Name] = p.Type;

        var result = new List<ProjectionMemberMap>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var explicitSeen = new HashSet<string>(StringComparer.Ordinal);

        // Explicit maps aimed at a constructor parameter: the resolved argument expression, and (separately)
        // every such target that was SEEN, so a parameter whose map failed to resolve fails quietly instead
        // of collecting a second, contradictory DWARF024 on top of the diagnostic that already explained it.
        var ctorArgExprs = new Dictionary<string, string>(StringComparer.Ordinal);
        var ctorArgTargets = new HashSet<string>(StringComparer.Ordinal);

        // ── Explicit maps ([MapProperty]) ────────────────────────────────────
        foreach (var (srcName, tgtName, use) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            handled.Add(tgtName);
            if (consumedSources is not null)
            {
                // A dotted source path (a flattened leaf) marks its ROOT consumed, matching AddConsumed.
                var dot = srcName.IndexOf('.');
                consumedSources.Add(dot < 0 ? srcName : srcName.Substring(0, dot));
            }

            if (ignores.Contains(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }

            // A constructor parameter is a legitimate target: a positional record's parameter also surfaces
            // as an init property (so both lookups hit, same type), but a ctor-only type's parameter surfaces
            // as nothing writable at all — and used to be refused as an unknown target for that reason.
            var isCtorArg = ctorParamTypes.ContainsKey(tgtName);
            if (isCtorArg) ctorArgTargets.Add(tgtName);

            if (!writableByName.TryGetValue(tgtName, out var tgtType)
                && !ctorParamTypes.TryGetValue(tgtName, out tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }

            // A custom converter (Use=) cannot run inside a provider-translated projection.
            if (use is not null)
            {
                EmitDWARF028(diagnostics, location, tgtName,
                    "custom converter (Use=) is not translatable in projection; remove Use= or map at runtime");
                continue;
            }

            // The OTHER [MapProperty] per-member modifiers are equally untranslatable, and were previously
            // dropped in silence: the rename still bound, so completeness was satisfied and nothing was
            // reported, while .Map applied the modifier and .Project did not — the same mapper yielding
            // different data depending on which method you called. Same rule as Use= above.
            if (extrasByTarget.TryGetValue(tgtName, out var extra))
            {
                if (extra.HasNullSub)
                {
                    EmitDWARF028(diagnostics, location, tgtName,
                        "NullSubstitute is not translatable in projection (the substitution would be silently "
                        + "dropped and a null stored instead); remove it or map this member at runtime");
                    continue;
                }

                if (extra.When is not null)
                {
                    EmitDWARF028(diagnostics, location, tgtName,
                        "When= is not translatable in projection (the predicate cannot run inside an expression "
                        + "tree, so the member would always be assigned); remove it or map this member at runtime");
                    continue;
                }
            }

            // Resolve the source, supporting a dotted path (e.g. "Colour.Code") for value-object /
            // nested-scalar flattening — matching the class-model [MapProperty] dotted-path feature.
            // The projection accessor "__s.Colour.Code" is built verbatim below; the walk is only here to
            // find the leaf type and validate each hop is a readable member.
            //
            // The nullable-hop answer is deliberately NOT taken: DWARF044 warns that dereferencing a null
            // interior throws, which is true in emitted C# and false here — the provider translates the path
            // to a join that yields null. Same walk, different consequence.
            MemberFacts.TryResolvePath(sourceType, srcName, compilation, ProjectionPublicOnly,
                out var sm, out _, out _);

            if (sm is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            var srcExprForExplicit = paramExpr + "." + Identifiers.EscapePath(srcName);
            var inlineExpr = ResolveProjectionExpr(
                sm, tgtType, srcExprForExplicit, 0, compilation, location,
                diagnostics, tgtName, enumPolicy, comparer, autoNest);
            if (inlineExpr is null)
                continue;

            // A parameter's expression goes into the constructor CALL, never into an initializer beside it:
            // an init-only member cannot be assigned after the constructor that already set it, and emitting
            // both produced `new T(x) { Same = x }`.
            if (isCtorArg)
                ctorArgExprs[tgtName] = inlineExpr;
            else
                result.Add(new ProjectionMemberMap(tgtName, inlineExpr));
        }

        // ── Constructor projection ───────────────────────────────────────────

        if (usesCtorProjection)
        {
            // ConstructorSelector ran ONCE, at the decision above, and its answer IS `projectionCtor`. It
            // used to be called a second time here with its return value DISCARDED, beside a local
            // widest-wins pick — so [DwarfMapperConstructor] was consulted, answered, and thrown away, and
            // the only thing the call still did was raise DWARF025/DWARF026 for a shape the emitter then
            // ignored. Two decisions that had to agree became one that cannot disagree.
            if (projectionCtor is null)
                return result;

            // C4: pass comparer (carries CaseInsensitive setting) to ctor projection resolver.
            var ctorExpr = ResolveProjectionCtorExpr(
                projectionCtor, sourceType, paramExpr, 0,
                compilation, location, diagnostics, targetType, enumPolicy, comparer, autoNest,
                ctorArgExprs, ctorArgTargets);
            if (ctorExpr is null)
                return result;

            // The constructor call leads; anything the constructor did not take follows it as an object
            // initializer, which the emitter appends. R18-32: this used to RETURN here, so an init-only
            // member outside the parameter list was dropped without a word — assigned through .Map, absent
            // through .Project, no diagnostic either way. Every parameter is marked handled so the member
            // loop below cannot assign it a second time.
            result.Insert(0, new ProjectionMemberMap("", ctorExpr));
            foreach (var p in projectionCtor.Parameters)
                handled.Add(p.Name);

            foreach (var m in writableMembers)
                if (ConstructorFeedsMember(projectionCtor, m.Name, comparer))
                    handled.Add(m.Name);
        }

        foreach (var target in writableMembers)
        {
            if (handled.Contains(target.Name) || ignores.Contains(target.Name)) continue;
            if (!sources.TryGetValue(target.Name, out var src))
            {
                // A source member MAY exist and simply be non-public. The runtime path would bind it under
                // [DwarfMapper(AllowNonPublic = true)]; projection enumerates public members only, so the
                // generic "no matching source member" would send the reader hunting for a member that is
                // plainly there. Name the real reason instead.
                if (allowNonPublic
                    && ReadableMembers(sourceType, compilation, true).Any(m => comparer.Equals(m.Name, target.Name)))
                {
                    EmitDWARF028(diagnostics, location, target.Name,
                        "the matching source member is non-public and AllowNonPublic is not honoured by "
                        + "projection (an expression tree is built from the public surface); map this member "
                        + "at runtime, or make the source member public");
                    continue;
                }

                // A [Flatten]ed leaf, in exactly the position ResolveMembers consults one: AFTER a direct
                // source member of the same name and BEFORE the unmapped-member report. The precedence is
                // not a choice made here — a flatten that outranked a direct member would make .Project
                // disagree with .Map about which source wins, which is the class of defect this whole
                // resolver's comments are about.
                var flatMatches = new List<(string Root, string Leaf, ITypeSymbol LeafType)>();
                foreach (var fi in flattenInfos)
                foreach (var leaf in fi.Leaves)
                    if (comparer.Equals(leaf.Name, target.Name))
                        flatMatches.Add((fi.Root, leaf.Name, leaf.Type));

                if (flatMatches.Count > 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten, location,
                        target.Name));
                    continue;
                }

                if (flatMatches.Count == 1)
                {
                    var fm = flatMatches[0];
                    // The root counts as READ, matching the dotted-[MapProperty] rule above: a flattened
                    // member is the root's data arriving under another name, so source-coverage must not
                    // then report the root as consumed by nothing.
                    consumedSources?.Add(fm.Root);
                    var flatExpr = ResolveProjectionExpr(
                        fm.LeafType, target.Type,
                        paramExpr + "." + Identifiers.EscapePath(fm.Root + "." + fm.Leaf), 0, compilation,
                        location, diagnostics, target.Name, enumPolicy, comparer, autoNest);
                    if (flatExpr is not null) result.Add(new ProjectionMemberMap(target.Name, flatExpr));
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name,
                    MemberName: target.Name));
                continue;
            }

            // [DwarfMapper(AutoMatchMembers = false)] is a TRUST BOUNDARY, not a convenience toggle: it is
            // the anti-over-posting guard (OWASP API6). It was honoured by the runtime resolver and ignored
            // here, so the same mapper refused the implicit wire through .Map and performed it through
            // .Project — a security control that silently did not apply at one endpoint. Explicit
            // [MapProperty]/[MapValue]/[MapIgnore] have already been handled above; only the implicit
            // by-name wire is blocked, matching ResolveMembers exactly.
            if (explicitOnly)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.AutoMatchDisabled, location, target.Name, MemberName: target.Name));
                continue;
            }

            // [DwarfMapper(SkipNullSourceMembers = true)] cannot be honoured inside an expression tree: the
            // runtime emitter guards the assignment with `if (src.X is not null) …` so the target keeps its own
            // default, which a projection's object initializer has no way to express — it always assigns, so a
            // null source would overwrite that default with null. Reported PER AFFECTED MEMBER, using the same
            // eligibility the runtime uses (nullable source + a target it could actually have deferred), so
            // members the option never touched stay quiet.
            if (skipNullSourceMembers
                && (src.Type.IsReferenceType || IsNullableValue(src.Type, out _))
                && deferrableTargets.Contains(target.Name))
            {
                EmitDWARF028(diagnostics, location, target.Name,
                    "SkipNullSourceMembers is not translatable in projection (an object initializer always "
                    + "assigns, so a null source would overwrite the target's default instead of keeping it); "
                    + "map this member at runtime, or drop the option for this mapper");
                continue;
            }

            consumedSources?.Add(src.Name);
            var srcAccessExpr = paramExpr + "." + Identifiers.Escape(src.Name);
            // C4: pass comparer so nested objects respect CaseInsensitive setting.
            var inlineExpr = ResolveProjectionExpr(
                src.Type, target.Type, srcAccessExpr, 0,
                compilation, location, diagnostics, target.Name, enumPolicy, comparer, autoNest);
            if (inlineExpr is not null)
                result.Add(new ProjectionMemberMap(target.Name, inlineExpr));
        }

        return result;
    }

    /// <summary>
    ///     Resolve a single inline projection expression for a source→target type pair.
    ///     Returns the inline C# expression string (pure, no helper calls), or null when
    ///     DWARF028 has been emitted (unsafe construct).
    ///     SAFE:
    ///     1. Direct-assignable (implicit conversion incl. widening numeric).
    ///     2. Enum by-value cast: (TgtEnum)srcExpr.
    ///     3. Nested named object: new TgtType { M1 = ..., M2 = ... } (recursive).
    ///     4. Collection (projection-translatable): .Select(...).ToList()/.ToArray()/lazy.
    ///     UNSAFE → DWARF028:
    ///     - Narrowing numeric (CreateChecked path).
    ///     - String↔T parsable (IParsable/IFormattable path).
    ///     - Enum by-name (switch path).
    ///     - Non-translatable collection target (HashSet/ISet/immutable/dict).
    ///     - Depth > ProjectionMaxDepth.
    ///     - No translatable conversion found.
    /// </summary>
    /// <param name="comparer">
    ///     C4: the case-sensitivity comparer for member name matching; passed recursively into
    ///     nested object and ctor resolvers so CaseInsensitive propagates to all depths.
    /// </param>
    private static string? ResolveProjectionExpr(
        ITypeSymbol srcType, ITypeSymbol tgtType,
        string srcExpr,
        int depth,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        string targetMemberName,
        EnumPolicy enumPolicy,
        StringComparer? comparer,
        bool autoNest)
    {
        comparer ??= StringComparer.Ordinal;

        // ── Depth guard ───────────────────────────────────────────────────────
        if (depth > ProjectionMaxDepth)
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                $"projection nesting depth exceeded {ProjectionMaxDepth}; split into a runtime mapper");
            return null;
        }

        // ── Pre-check: collection/dictionary targets BEFORE implicit-conversion ──
        // EF Core cannot translate HashSet/Dictionary/immutable collection projections even
        // when source==target (same type is directly assignable but NOT SQL-translatable).
        // We must check collection-shaped types BEFORE the HasImplicitConversion fast-path.
        if (CollectionConverter.TryResolve(srcType, tgtType,
                out var srcElem, out var tgtElem, out var shape))
        {
            if (!CollectionConverter.IsTargetKindTranslatable(shape.Target))
            {
                var tgtTypeName = tgtType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                EmitDWARF028(diagnostics, location, targetMemberName,
                    $"collection type '{tgtTypeName}' is not translatable in projection (HashSet/ISet/immutable/Dictionary targets are not supported by EF Core)");
                return null;
            }

            // Translatable collection: emit .Select(...).ToList()/.ToArray()/lazy
            var elemParam = $"__i{depth}";
            // C4: propagate comparer into element expression resolver.
            var elemExpr = ResolveProjectionExpr(
                srcElem, tgtElem, elemParam, depth + 1,
                compilation, location, diagnostics, targetMemberName, enumPolicy, comparer, autoNest);
            if (elemExpr is null) return null; // DWARF028 already emitted

            // Use fully-qualified Enumerable.Select to avoid needing 'using System.Linq' in generated code.
            var srcElemFqn = srcElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var tgtElemFqn = tgtElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var selectCall =
                $"global::System.Linq.Enumerable.Select<{srcElemFqn}, {tgtElemFqn}>({srcExpr}, {elemParam} => {elemExpr})";
            var collectionExpr = shape.Target switch
            {
                CollectionConverter.TargetKind.Array =>
                    $"global::System.Linq.Enumerable.ToArray<{tgtElemFqn}>({selectCall})",
                CollectionConverter.TargetKind.IEnumerable =>
                    selectCall, // lazy, no terminal
                _ =>
                    $"global::System.Linq.Enumerable.ToList<{tgtElemFqn}>({selectCall})"
            };
            // Guard the source collection with a null-conditional ternary ONLY when it may actually be
            // null (nullable-annotated or nullable-oblivious). A non-nullable source needs no guard —
            // guarding it would assign null to a non-nullable target (CS8601). EF translates the ternary.
            if (ProjectionSourceMayBeNull(srcType)) return $"{srcExpr} == null ? null : {collectionExpr}";
            return collectionExpr;
        }

        // ── Pre-check: Dictionary targets (always non-translatable in projection) ──
        // Check before HasImplicitConversion to catch same-type dictionary members.
        if (DictionaryConverter.TryResolve(srcType, tgtType,
                out _, out _, out _, out _, out _, out _))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "Dictionary targets are not translatable in projection; map at runtime");
            return null;
        }

        // ── 1. Direct-assignable (implicit — covers widening numeric, same-type, etc.) ──
        if (HasImplicitConversion(compilation, srcType, tgtType)) return srcExpr;

        // ── 2. Enum by-value cast (enum→enum) ─────────────────────────────────
        if (srcType.TypeKind == TypeKind.Enum && tgtType.TypeKind == TypeKind.Enum
                                              && enumPolicy.Strategy == EnumStrategy.ByValue)
        {
            var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({tgtFqn}){srcExpr}";
        }

        // ── C6: enum↔integral inline cast (SQL-translatable as a direct cast) ──
        // enum→integral (e.g. Status→int): cast to the integral type.
        // integral→enum (e.g. int→Status): cast to the enum type.
        // Only emit when the conversion is widening or same-width (safe). Narrowing (enum:long→int)
        // would need CreateChecked — fall through to DWARF028 for that case.
        if (srcType.TypeKind == TypeKind.Enum && TypeInterfaces.IsIntegral(tgtType))
        {
            // Get the enum's underlying integral type for a width-safety check.
            var enumUnderlying = ((INamedTypeSymbol)srcType).EnumUnderlyingType;
            if (enumUnderlying is not null && IsWideningOrSameWidth(enumUnderlying, tgtType))
            {
                var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"({tgtFqn}){srcExpr}";
            }

            // Narrowing / lossy (e.g. enum:long→int, or unsigned-underlying enum:uint→int) — the source
            // underlying does not fit the target range and a projection can't do a checked cast.
            EmitDWARF028(diagnostics, location, targetMemberName,
                "enum→integral conversion is narrowing (the enum's underlying type does not fit the target integral type) and cannot be range-checked in a projection; map it at runtime");
            return null;
        }

        if (TypeInterfaces.IsIntegral(srcType) && tgtType.TypeKind == TypeKind.Enum)
        {
            // integral→enum: safe when source integral width ≤ enum underlying width.
            var enumUnderlying = ((INamedTypeSymbol)tgtType).EnumUnderlyingType;
            if (enumUnderlying is not null && IsWideningOrSameWidth(srcType, enumUnderlying))
            {
                var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"({tgtFqn}){srcExpr}";
            }

            // Narrowing / lossy (e.g. long→enum:int, or int→enum:uint sign change) — the source does not
            // fit the enum's underlying range and a projection can't do a checked cast.
            EmitDWARF028(diagnostics, location, targetMemberName,
                "integral→enum conversion is narrowing (the source does not fit the enum's underlying type) and cannot be range-checked in a projection; map it at runtime");
            return null;
        }

        // ── UNSAFE: enum by-name (enumPolicy == ByName, different enum types) ──
        if ((srcType.TypeKind == TypeKind.Enum || tgtType.TypeKind == TypeKind.Enum)
            && enumPolicy.Strategy == EnumStrategy.ByName)
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "enum by-name mapping is not translatable in projection; use EnumStrategy.ByValue or map at runtime");
            return null;
        }

        // ── UNSAFE: numeric narrowing (NumericConverter would fire: both integral, no implicit) ──
        if (TypeInterfaces.IsIntegral(srcType) && TypeInterfaces.IsIntegral(tgtType))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "narrowing numeric conversion is not SQL-translatable (would need CreateChecked); map at runtime or use a widening target type");
            return null;
        }

        // ── UNSAFE: string↔T parsable (ParsableConverter would fire) ─────────
        if ((srcType.SpecialType == SpecialType.System_String
             && tgtType.TypeKind != TypeKind.Enum
             && TypeInterfaces.ImplementsIParsable(compilation, tgtType))
            || (tgtType.SpecialType == SpecialType.System_String
                && srcType.SpecialType != SpecialType.System_String
                && srcType.TypeKind != TypeKind.Enum
                && (TypeInterfaces.ImplementsIFormattable(srcType)
                    || srcType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char)))
        {
            EmitDWARF028(diagnostics, location, targetMemberName,
                "string parse/format is not translatable in projection (IParsable/IFormattable); map at runtime");
            return null;
        }

        // ── 3. Nested named object (recursive) ───────────────────────────────
        if (srcType is INamedTypeSymbol namedSrc && tgtType is INamedTypeSymbol namedTgt
                                                 && IsMappableObjectPair(compilation, srcType, namedTgt))
        {
            // [DwarfMapper(AutoNest = false)] means "do not synthesize nested pairs I did not ask for". The
            // runtime resolver refuses with DWARF005; projection auto-nested regardless, so the two endpoints
            // disagreed about whether a nested member was mapped at all. Report the same diagnostic the
            // runtime reports, so turning auto-nesting off means the same thing everywhere.
            if (!autoNest)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NoImplicitConversion, location, targetMemberName));
                return null;
            }

            // C4: pass comparer into nested object resolver.
            return ResolveProjectionNestedObjectExpr(
                namedSrc, namedTgt, srcExpr, depth, compilation, location, diagnostics,
                targetMemberName, enumPolicy, comparer, autoNest);
        }

        // ── Nullable T? → nullable U? or non-nullable U ───────────────────────
        // C5: when source is Nullable<T> and target is also Nullable<U>, emit a null-preserving
        // HasValue ternary (SQL-translatable) instead of .Value (throws on null).
        if (IsNullableValue(srcType, out var srcUnderlying))
        {
            if (IsNullableValue(tgtType, out var tgtUnderlying))
            {
                // int?→long?: null-preserving ternary: __s.X.HasValue ? (long?)__s.X.Value : null
                var innerExpr = ResolveProjectionExpr(
                    srcUnderlying, tgtUnderlying, srcExpr + ".Value", depth,
                    compilation, location, diagnostics, targetMemberName, enumPolicy, comparer, autoNest);
                if (innerExpr is null) return null;
                var tgtNullableFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"{srcExpr}.HasValue ? ({tgtNullableFqn}){innerExpr} : null";
            }
            // int?→long (non-nullable target): REFUSED. This link needs a null decision, and NullStrategy —
            // the option that makes it — never reaches the projection engine, so the emitted `.Value` ignored
            // it: a mapper configured NullStrategy.SetDefault returned 0 from .Map and threw
            // InvalidOperationException from .Project for the same input. Emitting `.Value` also pushes the
            // failure to runtime inside a provider-translated query, where NULL semantics are the provider's,
            // not ours. Refusing at build time keeps the null decision explicit and the two paths honest.
            EmitDWARF028(diagnostics, location, targetMemberName,
                "a nullable source mapped to a non-nullable target needs a null decision, and NullStrategy is "
                + "not translatable in projection; make the target nullable, or map this member at runtime");
            return null;
        }

        // ── Fallback: no translatable conversion found ────────────────────────
        EmitDWARF028(diagnostics, location, targetMemberName,
            "no translatable conversion found; map at runtime instead");
        return null;
    }

    /// <summary>
    ///     C6 helper: returns true when a cast from <paramref name="src" /> to <paramref name="tgt" /> is
    ///     widening or same-width (thus safe as a direct inline cast in SQL projection).
    ///     Both must be integral types.
    /// </summary>
    private static bool IsWideningOrSameWidth(ITypeSymbol src, ITypeSymbol tgt)
    {
        // (bit width, isSigned) per integral type. Honours the enum's ACTUAL underlying type
        // (byte/short/uint/long/…), not a fixed int assumption.
        static bool IntegralInfo(ITypeSymbol t, out int width, out bool signed)
        {
            switch (t.SpecialType)
            {
                case SpecialType.System_Byte:
                    width = 8;
                    signed = false;
                    return true;
                case SpecialType.System_SByte:
                    width = 8;
                    signed = true;
                    return true;
                case SpecialType.System_UInt16:
                    width = 16;
                    signed = false;
                    return true;
                case SpecialType.System_Int16:
                    width = 16;
                    signed = true;
                    return true;
                case SpecialType.System_UInt32:
                    width = 32;
                    signed = false;
                    return true;
                case SpecialType.System_Int32:
                    width = 32;
                    signed = true;
                    return true;
                case SpecialType.System_UInt64:
                    width = 64;
                    signed = false;
                    return true;
                case SpecialType.System_Int64:
                    width = 64;
                    signed = true;
                    return true;
                default:
                    width = 0;
                    signed = false;
                    return false;
            }
        }

        if (!IntegralInfo(src, out var sw, out var ss)) return false;
        if (!IntegralInfo(tgt, out var tw, out var ts)) return false;

        // A plain (unchecked) cast src→tgt is lossless — safe to inline in a projection that can't do a
        // checked conversion — ONLY when the target's representable range fully contains the source's:
        //   • same signedness    → target width must be ≥ source width  (short→int, uint→ulong)
        //   • unsigned → signed  → target needs a strictly wider type for the sign bit  (byte→short, uint→long)
        //   • signed → unsigned  → never lossless (source may be negative)
        // Anything else (e.g. uint→int, ushort→short, long→int) is narrowing and falls through to DWARF028.
        if (ss == ts) return tw >= sw;
        if (!ss && ts) return tw > sw;
        return false;
    }

    /// <summary>
    ///     Whether <paramref name="ctor" /> already feeds the target member <paramref name="memberName" />,
    ///     so the projection must not assign it a second time beside the constructor call.
    ///     <para>
    ///         A record's positional parameter also surfaces as an init PROPERTY, so the property has to be
    ///         recognised as already-fed or <c>Start</c> gets assigned beside <c>start</c> the argument.
    ///         Matched under the configured comparer OR case-insensitively, which is precisely the pair of
    ///         rules the parameter itself binds under — the class model does the same thing with a
    ///         case-insensitive <c>consumedParams</c>, and the two sets have to agree or one of them assigns
    ///         twice.
    ///     </para>
    ///     <para>
    ///         One predicate rather than two, because there were two and they disagreed: the NESTED site
    ///         matched under the configured comparer alone, so an ordinal-comparer mapping onto
    ///         <c>new LeafDto(x, y)</c> emitted <c>{ X = …, Y = … }</c> after it and assigned both members
    ///         twice. That went unseen only while the nested constructor path was unreachable for a target
    ///         with writable members; honouring <c>[DwarfMapperConstructor]</c> there reaches it.
    ///     </para>
    /// </summary>
    private static bool ConstructorFeedsMember(IMethodSymbol ctor, string memberName, StringComparer comparer)
    {
        return ctor.Parameters.Any(p => comparer.Equals(p.Name, memberName)
                                        || StringComparer.OrdinalIgnoreCase.Equals(p.Name, memberName));
    }

    /// <summary>
    ///     How a projected target is BUILT: by member initializer, or by a constructor call — and if so
    ///     which constructor. The ONE answer, for the top-level projection and for every nested one.
    ///     <para>
    ///         It runs <see cref="ConstructorSelector" />, the same policy the create map, the span map, the
    ///         async stream and the co-located host all run, which is what makes
    ///         <c>[DwarfMapperConstructor]</c> mean the same thing at this endpoint as at those. Before this
    ///         existed the projection had TWO hand-written constructor picks — one at the top level, one for
    ///         nested objects, each a widest-arity <c>FirstOrDefault</c> — and neither asked the selector,
    ///         so the directive was accepted, ignored and unreported at both. The top-level site did call
    ///         <c>Select</c>, and DISCARDED its return value, which is the same defect wearing the answer.
    ///     </para>
    ///     <para>
    ///         Member-init is preferred exactly where it is expressible: the target must have members to set
    ///         (<paramref name="writableMemberCount" />) AND a public parameterless constructor for EF Core
    ///         to materialise through. Positional records and other constructor-only types have neither, and
    ///         must project through a constructor whatever the selector's default preference is — which is
    ///         why a parameterless answer from the selector is not the end of the question here. That
    ///         fallback is the widest public constructor, i.e. exactly what this endpoint did before, kept so
    ///         that a target the selector answers "parameterless" for still projects the way it always has.
    ///     </para>
    ///     <para>
    ///         Public-only (<see cref="ProjectionPublicOnly" />), matching this file's member enumeration: a
    ///         constructor the query provider cannot call is not one this endpoint can project through.
    ///     </para>
    /// </summary>
    /// <param name="writableMemberCount">
    ///     How many members of the target member-init could set. Zero means member-init cannot express the
    ///     mapping at all, however the target is constructed.
    /// </param>
    /// <param name="explicitMaps">
    ///     The <c>[MapProperty]</c> renames, so selection scores a constructor parameter fed only by a rename
    ///     as satisfiable — <c>null</c> for a nested target, which takes none.
    /// </param>
    /// <returns>
    ///     <c>Ok</c> is <see langword="false" /> when selection reported a blocking <c>DWARF025</c> /
    ///     <c>DWARF026</c> and has no constructor to offer; the caller must stop rather than resolve members
    ///     on top of it. <c>Ctor</c> is <see langword="null" /> when <c>UsesCtorProjection</c> is
    ///     <see langword="false" />, and may also be null WITH it, for a constructor-only target that has no
    ///     usable constructor — the caller reports that in its own words.
    /// </returns>
    private static (bool Ok, bool UsesCtorProjection, IMethodSymbol? Ctor) ChooseProjectionConstructor(
        INamedTypeSymbol targetType, ITypeSymbol sourceType, int writableMemberCount,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        IReadOnlyList<(string Source, string Target, string? Use)>? explicitMaps)
    {
        var hasParameterlessCtor = targetType.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && !c.IsStatic
            && c.Parameters.Length == 0);
        var memberInitIsExpressible = writableMemberCount > 0 && hasParameterlessCtor;

        var selected = ConstructorSelector.Select(compilation, targetType, diagnostics, location,
            out _, ProjectionPublicOnly, sourceType, explicitMaps);
        if (selected is null) return (false, false, null);

        // The selector's answer is a parameterless constructor and this target can be built by one, so
        // member-init it is. Asked of the constructor's ARITY rather than of the selector's
        // useObjectInitializerOnly flag, which is false for an ANNOTATED parameterless constructor: reading
        // the flag there sent the projection down the constructor path and it then fell through to the
        // widest overload, so [DwarfMapperConstructor] on `Dst()` projected as `new Dst(id, name)` while the
        // create map over the same pair mapped by initializer. The annotation cannot name one constructor
        // and get another.
        if (selected.Parameters.Length == 0 && memberInitIsExpressible) return (true, false, null);

        // The selector named a constructor — because the target has no parameterless one, or because
        // [DwarfMapperConstructor] overrode the preference. Either way it is the answer.
        if (selected.Parameters.Length > 0) return (true, true, selected);

        // The selector's answer is a parameterless constructor, and member-init cannot carry this target.
        // Its answer is therefore not one this endpoint can use, so fall back to the widest public
        // constructor — what this endpoint chose here before, unchanged.
        return (true, true, targetType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic &&
                        c.Parameters.Length > 0)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault());
    }

    /// <summary>
    ///     Whether a projection source expression needs a null-navigation guard. A reference type needs one
    ///     only when it is nullable-annotated (<c>T?</c>) or nullable-oblivious (compiled with
    ///     <c>#nullable disable</c>). A NON-nullable-annotated reference is guaranteed non-null, so guarding it
    ///     would assign <c>null</c> to a (possibly non-nullable) target — a false CS8601/CS8603 in strict-
    ///     nullable hosts. This honours the consumer's own nullable annotations instead of guarding blindly.
    /// </summary>
    private static bool ProjectionSourceMayBeNull(ITypeSymbol type)
    {
        return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated;
    }

    /// <summary>
    ///     Build an inline member-init expression for a nested object target.
    ///     For nullable reference source: emits null-navigation ternary.
    ///     For non-null / value-type source: emits plain member-init.
    /// </summary>
    /// <param name="comparer">
    ///     C4: the case-sensitivity comparer for member name matching, propagated from the top-level
    ///     call site so CaseInsensitive works at all nesting depths.
    /// </param>
    private static string? ResolveProjectionNestedObjectExpr(
        INamedTypeSymbol srcType, INamedTypeSymbol tgtType,
        string srcExpr, int depth,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        string targetMemberName, EnumPolicy enumPolicy,
        StringComparer? comparer,
        bool autoNest)
    {
        comparer ??= StringComparer.Ordinal;
        var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // C4: use the configured comparer for member lookup so CaseInsensitive applies here.
        var srcReadable = BuildProjectionSourceLookup(srcType, comparer, compilation, location, diagnostics);

        // Build member-init or ctor expression for the nested object, through the SAME decision the
        // top-level projection makes. This used to be a hand-written mirror of it — a second
        // `hasParameterlessCtor` test beside a second widest-wins pick, with a comment instructing the
        // reader to keep the two in step — and it did not mirror the one thing that mattered: neither copy
        // asked ConstructorSelector, so [DwarfMapperConstructor] on a NESTED projection target was as silent
        // as on the top-level one. One function, two call sites; a third nesting level inherits it.
        var writableTargetMembers = WritableMembers(tgtType, compilation, ProjectionPublicOnly)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        // A nested target takes no explicit maps: [MapProperty] names members of the OUTER pair, and the
        // nested resolver binds by name alone (see MemberParts below). Passing none is the honest input,
        // not a shortcut — a satisfiability score computed against the outer method's renames would be
        // scoring this constructor against maps that can never reach it.
        var (ctorDecided, usesNestedCtorProjection, bestCtor) = ChooseProjectionConstructor(
            tgtType, srcType, writableTargetMembers.Count, compilation, location, diagnostics,
            explicitMaps: null);
        if (!ctorDecided) return null;

        // Member-init parts for a set of target members — `P1 = expr1`, one per member. Shared by both
        // branches below, because the constructor branch has to assign whatever the constructor did not take
        // and there is no second way to resolve a member.
        List<string>? MemberParts(IEnumerable<(string Name, ITypeSymbol Type)> members)
        {
            var parts = new List<string>();
            var failed = false;

            foreach (var tgtMember in members)
            {
                if (!srcReadable.TryGetValue(tgtMember.Name, out var srcMember))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.UnmappedMember, location, targetMemberName + "." + tgtMember.Name));
                    failed = true;
                    continue;
                }

                var memberSrcExpr = srcExpr + "." + Identifiers.Escape(srcMember.Name);
                // C4: propagate comparer into recursive member resolution.
                var memberInlineExpr = ResolveProjectionExpr(
                    srcMember.Type, tgtMember.Type, memberSrcExpr, depth + 1,
                    compilation, location, diagnostics,
                    targetMemberName + "." + tgtMember.Name, enumPolicy, comparer, autoNest);

                if (memberInlineExpr is null)
                {
                    failed = true;
                    continue;
                }

                parts.Add($"{tgtMember.Name} = {memberInlineExpr}");
            }

            return failed ? null : parts;
        }

        string innerBodyExpr;

        if (usesNestedCtorProjection)
        {
            // Expression trees require POSITIONAL args (CS0853: named args not allowed), which
            // ResolveProjectionCtorExpr emits; which constructor to call was decided above.
            if (bestCtor is null)
            {
                EmitDWARF028(diagnostics, location, targetMemberName,
                    $"nested type '{tgtFqn}' has no writable members and no usable constructor");
                return null;
            }

            // C4: pass the configured comparer (not hardcoded Ordinal) so CaseInsensitive propagates.
            // ISSUE-043: autoNest was omitted here and defaulted back to `true`, so a mapper with
            // AutoNest = false still auto-nested through THIS path (nested object → ctor projection) while
            // every sibling path honoured the setting. The parameter is required now, so the omission
            // cannot come back.
            var ctorExpr = ResolveProjectionCtorExpr(
                bestCtor, srcType, srcExpr, depth,
                compilation, location, diagnostics, tgtType, enumPolicy,
                comparer, autoNest);
            if (ctorExpr is null) return null;

            // R18-32, nested half: a member the constructor did not take used to be dropped here in silence,
            // exactly as at the top level — `new InnerDto(__s.Inner.Start)` with InnerDto.Extra never
            // assigned, while .Map assigned it. It becomes an object initializer on the constructor call.
            var leftover = MemberParts(writableTargetMembers
                .Where(m => !ConstructorFeedsMember(bestCtor, m.Name, comparer)));
            if (leftover is null) return null;

            innerBodyExpr = leftover.Count == 0
                ? ctorExpr
                : $"{ctorExpr} {{ {string.Join(", ", leftover)} }}";
        }
        else
        {
            // Member-init expression: new T { P1 = expr1, P2 = expr2 }
            var memberParts = MemberParts(writableTargetMembers);
            if (memberParts is null) return null;
            innerBodyExpr = $"new {tgtFqn} {{ {string.Join(", ", memberParts)} }}";
        }

        // Wrap with a null-navigation ternary ONLY when the source may actually be null (nullable-
        // annotated or nullable-oblivious). A non-nullable source needs no guard (guarding it would
        // assign null to a non-nullable target — CS8603).
        if (ProjectionSourceMayBeNull(srcType)) return $"{srcExpr} == null ? null : {innerBodyExpr}";
        return innerBodyExpr;
    }

    /// <summary>
    ///     Build an inline constructor-call expression for targets with only ctor params (records etc.).
    ///     e.g. "new global::D.DstRec(x: __s.X, y: __s.Y)"
    /// </summary>
    /// <param name="explicitArgExprs">
    ///     Argument expressions already resolved from a <c>[MapProperty]</c> whose target names a parameter,
    ///     keyed by parameter name (ordinal, matching <c>ResolveConstructorArguments</c>). Empty for the
    ///     nested-object caller, where <c>[MapProperty]</c> targets belong to the top-level pair.
    /// </param>
    /// <param name="explicitArgTargets">
    ///     Every parameter an explicit map NAMED, including ones whose resolution failed. A parameter in here
    ///     but not in <paramref name="explicitArgExprs" /> already has a diagnostic explaining why; DWARF024
    ///     on top of it would recommend the attribute the author has just written.
    /// </param>
    private static string? ResolveProjectionCtorExpr(
        IMethodSymbol ctor,
        ITypeSymbol srcType,
        string srcExpr,
        int depth,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        INamedTypeSymbol tgtType,
        EnumPolicy enumPolicy,
        StringComparer comparer,
        bool autoNest,
        Dictionary<string, string>? explicitArgExprs = null,
        HashSet<string>? explicitArgTargets = null)
    {
        var tgtFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var srcReadable = BuildProjectionSourceLookup(srcType, comparer, compilation, location, diagnostics);

        var argParts = new List<string>();
        var anyFailed = false;

        foreach (var param in ctor.Parameters)
        {
            // An explicit [MapProperty] naming this parameter wins over the by-name match, exactly as it does
            // in ResolveConstructorArguments. Its expression was resolved by the caller against the
            // parameter's type, so it is used verbatim here.
            if (explicitArgExprs is not null && explicitArgExprs.TryGetValue(param.Name, out var explicitArg))
            {
                argParts.Add(explicitArg);
                continue;
            }

            if (explicitArgTargets is not null && explicitArgTargets.Contains(param.Name))
            {
                anyFailed = true;
                continue;
            }

            if (!TryBindProjectionCtorParam(param.Name, srcReadable, comparer, location, diagnostics,
                    out var srcMember))
            {
                anyFailed = true;
                continue;
            }

            var paramSrcExpr = srcExpr + "." + Identifiers.Escape(srcMember.Name);
            // C4: propagate comparer into ctor param expression resolver.
            var paramInlineExpr = ResolveProjectionExpr(
                srcMember.Type, param.Type, paramSrcExpr, depth + 1,
                compilation, location, diagnostics, param.Name, enumPolicy, comparer, autoNest);

            if (paramInlineExpr is null)
            {
                anyFailed = true;
                continue;
            }

            // Expression trees do not allow named arguments (CS0853): emit positional args.
            argParts.Add(paramInlineExpr);
        }

        if (anyFailed) return null;
        return $"new {tgtFqn}({string.Join(", ", argParts)})";
    }

    /// <summary>
    ///     Binds one constructor parameter to a source member, first under the mapper's configured comparer
    ///     and then — failing that — case-insensitively, reporting DWARF024 when neither finds anything.
    /// </summary>
    /// <remarks>
    ///     The insensitive second pass is not a convenience: <c>ResolveConstructorArguments</c> matches ctor
    ///     parameters case-insensitively ALWAYS, deliberately, because C# convention is a camelCase parameter
    ///     (<c>id</c>) binding a PascalCase member (<c>Id</c>) — the dominant record / primary-constructor
    ///     shape. Projection matched them under the class comparer, which is Ordinal by default, so
    ///     <c>Dst(int id, int code)</c> bound its parameters through <c>.Map</c> and reported DWARF024 for the
    ///     same pair through <c>.Project</c>. One more member of the divergence family
    ///     <c>ProjectionRuntimeParityTests</c> exists to catch.
    ///     <para>
    ///         A genuine case-only collision is DWARF010 rather than an arbitrary pick, which is the same
    ///         promise <see cref="BuildProjectionSourceLookup" /> makes for member binding.
    ///     </para>
    /// </remarks>
    private static bool TryBindProjectionCtorParam(
        string paramName,
        Dictionary<string, (string Name, ITypeSymbol Type)> srcReadable,
        StringComparer comparer,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        out (string Name, ITypeSymbol Type) srcMember)
    {
        if (srcReadable.TryGetValue(paramName, out srcMember))
            return true;

        var insensitive = srcReadable
            .Where(kv => StringComparer.OrdinalIgnoreCase.Equals(kv.Key, paramName))
            .Select(kv => kv.Value)
            .ToList();

        if (insensitive.Count == 1)
        {
            srcMember = insensitive[0];
            return true;
        }

        diagnostics.Add(insensitive.Count > 1
            ? new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, paramName)
            : new DiagnosticInfo(DiagnosticDescriptors.ConstructorParameterUnmapped, location, paramName));
        srcMember = default;
        return false;
    }

    // The comparer already collapsed case when it is OrdinalIgnoreCase or Flexible, so the second pass can
    // only ever add matches the first pass could not see — never override one it did.

    /// <summary>
    ///     DFS reachability: can we reach <paramref name="target" /> starting from <paramref name="start" />
    ///     by following edges in the call graph? Used to detect recursive method cycles.
    /// </summary>
    private static bool CanReach(
        Dictionary<string, HashSet<string>> graph,
        string start,
        string target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        if (!graph.TryGetValue(start, out var startDeps)) return false;
        foreach (var dep in startDeps)
            stack.Push(dep);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (string.Equals(current, target, StringComparison.Ordinal)) return true;
            if (!visited.Add(current)) continue;
            if (graph.TryGetValue(current, out var deps))
                foreach (var dep in deps)
                    stack.Push(dep);
        }

        return false;
    }

    /// <summary>
    ///     Recognises <c>System.Span&lt;T&gt;</c> / <c>System.ReadOnlySpan&lt;T&gt;</c>, returning the element
    ///     type and whether it is the read-only form. Used to detect zero-alloc span map methods.
    /// </summary>
    private static bool TryGetSpanElement(ITypeSymbol t, out ITypeSymbol element, out bool isReadOnly)
    {
        element = null!;
        isReadOnly = false;
        if (t is INamedTypeSymbol n
            && n.TypeArguments.Length == 1
            && n.ContainingNamespace is { Name: "System" } ns
            && ns.ContainingNamespace?.IsGlobalNamespace == true
            && (string.Equals(n.Name, "Span", StringComparison.Ordinal)
                || string.Equals(n.Name, "ReadOnlySpan", StringComparison.Ordinal)))
        {
            element = n.TypeArguments[0];
            isReadOnly = string.Equals(n.Name, "ReadOnlySpan", StringComparison.Ordinal);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Recognises <c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c>, returning the element type.
    ///     Used to detect async streaming map methods.
    /// </summary>
    private static bool TryGetAsyncEnumerableElement(ITypeSymbol t, out ITypeSymbol element)
    {
        element = null!;
        if (t is INamedTypeSymbol n
            && n.TypeArguments.Length == 1
            && string.Equals(n.Name, "IAsyncEnumerable", StringComparison.Ordinal)
            && n.ContainingNamespace is { Name: "Generic" } g
            && g.ContainingNamespace is { Name: "Collections" } c
            && c.ContainingNamespace is { Name: "System" } s
            && s.ContainingNamespace.IsGlobalNamespace)
        {
            element = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Emits DWARF038 for a non-lossless implicit basic-type conversion: an Info-level suggestion when
    ///     <paramref name="implicitConversions" /> is true (permissive — the conversion is still applied), or a
    ///     build Error when false (strict — the user must opt in via <c>[MapProperty(Use = …)]</c>).
    /// </summary>
    /// <summary>
    ///     True when one type is integer-kind and the other is floating/decimal-kind (e.g. int↔double,
    ///     long↔float, int↔decimal) — a cross-category numeric conversion. Same-category pairs (int↔long,
    ///     float↔double) return false.
    /// </summary>
    /// <summary>True for <c>System.Threading.CancellationToken</c>.</summary>
    private static bool IsCancellationToken(ITypeSymbol t)
    {
        return t is INamedTypeSymbol { Name: "CancellationToken" } n
               && n.ContainingNamespace?.ToDisplayString() == "System.Threading";
    }
}
