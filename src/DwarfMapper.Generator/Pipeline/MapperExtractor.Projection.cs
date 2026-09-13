// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
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

        /// <summary>
        ///     Decide whether the projection method that has just been resolved must be DROPPED, and confine its
        ///     refusal to itself when it can be confined.
        /// </summary>
        /// <param name="diagnostics">
        ///     The class's diagnostic list; entries from <paramref name="start" /> on
        ///     belong to this one method.
        /// </param>
        /// <param name="start">The list's length when this method's resolution began.</param>
        /// <param name="methodName">The projection method's name, carried in the per-method signpost's message.</param>
        /// <param name="location">The method's own declaration site, where that signpost is reported.</param>
        /// <returns>True when the caller must NOT add this method's model.</returns>
        /// <remarks>
        ///     <para>
        ///         DWARF028 is an Error and an error suppressed the entire class, so a mapper declaring a
        ///         <c>Map</c> and a <c>Project</c> over the same pair generated <b>nothing at all</b> the moment
        ///         one projected member was untranslatable — and DWARF078 said so, accurately and unhelpfully.
        ///         The <c>Map</c> methods were collateral: nothing about them is translated, so nothing about
        ///         them can fail to translate (TASKS.md I14). This scopes the refusal to what was actually
        ///         refused.
        ///     </para>
        ///     <para>
        ///         Two errors are scopable here, and only when EVERY error this method raised is one of them:
        ///         DWARF028 (untranslatable — a property of the endpoint, not of the mapping) and, since I17,
        ///         DWARF001 (an unmapped destination member — a property of THIS method's pair and THIS
        ///         method's <c>[MapIgnore]</c> set). Every other error a projection method can collect — an
        ///         ambiguous member name (DWARF010), an unknown destination (DWARF008), a duplicate
        ///         <c>[MapProperty]</c> — describes the SOURCE MODEL, is equally true of the <c>Map</c> methods
        ///         over the same pair, and keeps the whole-class suppression it has always had.
        ///     </para>
        ///     <para>
        ///         The method is dropped either way. A projection whose members did not all resolve has no
        ///         honest body: emitting it with the members that DID resolve would silently drop the rest,
        ///         which is the failure mode this whole endpoint is written to refuse.
        ///     </para>
        /// </remarks>
        private static bool TryScopeProjectionRefusalToItsMethod(
            List<DiagnosticInfo> diagnostics,
            int start,
            string methodName,
            LocationInfo? location)
        {
            return TryScopeMethodRefusal(
                diagnostics,
                start,
                methodName,
                location,
                DiagnosticDescriptors.ProjectionMethodNotGenerated,
                DiagnosticDescriptors.ProjectionNotTranslatable,
                // I17: completeness is per METHOD at every endpoint, this one included. A projection resolves
                // its own members (MapperExtractor.Projection emits UnmappedMember at two sites, both inside
                // this method's diagnostic range), and its method-level [MapIgnore] set is its own — so a
                // destination member this projection does not map says nothing about the Map methods beside it.
                // Reading it as class-level here and per-method everywhere else would make the SAME error
                // proportional at four endpoints and not at the fifth.
                DiagnosticDescriptors.UnmappedMember);
        }

        private static bool IsQueryable(ITypeSymbol type, out ITypeSymbol element)
        {
            element = type;
            if (type is INamedTypeSymbol n && n.Name == "IQueryable" && n.TypeArguments.Length == 1 && KnownNames.IsNamespace(n.ContainingNamespace, "System.Linq"))
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
        private static void EmitDwarf028(
            List<DiagnosticInfo> diagnostics,
            LocationInfo? location,
            string memberName,
            string reason)
        {
            var msg = $"Projection member '{memberName}' cannot be translated to SQL: {reason}.";
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.ProjectionNotTranslatable,
                location,
                msg));
        }

        private static Dictionary<string, (string Name, ITypeSymbol Type)> BuildProjectionSourceLookup(
            ITypeSymbol sourceType,
            StringComparer comparer,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
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
        /// <param name="sourceType">
        ///     The type being projected FROM. Enumerated PUBLIC-ONLY (see <c>ProjectionPublicOnly</c>): a member a
        ///     query provider cannot read is not one this endpoint can project.
        /// </param>
        /// <param name="targetType">
        ///     The type being projected INTO — the destination whose members must all be accounted for before the
        ///     method is emitted at all.
        /// </param>
        /// <param name="ignores">
        ///     The effective <c>[MapIgnore]</c> set. Under <c>IgnoreObsoleteMembers</c> the target's retired
        ///     members are folded into a COPY of it, never into the caller's.
        /// </param>
        /// <param name="compilation">The compilation every member enumeration and type question runs against.</param>
        /// <param name="location">The declaration site every diagnostic raised here is reported at.</param>
        /// <param name="diagnostics">
        ///     Diagnostic sink. An untranslatable member appends <c>DWARF028</c> naming the member and the reason;
        ///     the method is then dropped rather than emitted half-filled.
        /// </param>
        /// <param name="options">
        ///     The mapper's option bundle, read here exactly as the runtime resolver reads it: <c>CaseInsensitive</c>
        ///     and <c>NameConvention</c> fold into the one comparer that rides down into the nested and constructor
        ///     resolvers; <c>AutoNest</c> is threaded to the expression resolver, where a false value refuses an
        ///     unrequested nested pair instead of quietly synthesizing one; <c>NullAsNull</c> is the
        ///     <c>NullCollections</c> setting; <c>ImplicitConversions</c> decides whether a lossy-but-C#-implicit
        ///     conversion is an Info suggestion or a build Error; <c>SkipNullSourceMembers</c> is refused per
        ///     AFFECTED member (an object initializer always assigns); <c>AllowNonPublic</c> sharpens the refusal
        ///     rather than widening what is read; <c>ExplicitOnly</c> (<c>AutoMatchMembers = false</c>) blocks the
        ///     implicit by-name wire exactly as at the runtime resolver; <c>IgnoreObsolete</c> folds the target's
        ///     <c>[Obsolete]</c> members into the ignore set except any a directive deliberately targets.
        ///     <c>ReferenceHandling</c> never reaches this resolver: the call site refuses every mode but
        ///     <c>None</c> with <c>DWARF028</c> and drops the method first, because a stateful identity map
        ///     cannot live inside an expression tree.
        /// </param>
        /// <param name="explicitMaps">
        ///     The <c>[MapProperty]</c> renames as (source, target, <c>Use</c>) triples. Resolved before
        ///     auto-matching, and their targets count as claimed.
        /// </param>
        /// <param name="enumPolicy">
        ///     The mapper's <c>EnumStrategy</c>, threaded to the expression resolver, where <c>ByName</c> is refused
        ///     as untranslatable.
        /// </param>
        /// <param name="paramExpr">
        ///     The lambda parameter every source read is rooted at (<c>"__s"</c> from the one call site), so an
        ///     emitted access reads <c>__s.Member</c>.
        /// </param>
        /// <param name="mapPropertyExtras">
        ///     Per-target <c>[MapProperty]</c> modifiers — <c>NullSubstitute</c> and <c>When</c> — checked against
        ///     each explicit map, so a modifier an expression tree cannot express is refused rather than dropped.
        /// </param>
        /// <param name="consumedSources">
        ///     Optional set the source names actually read are added to, so the caller can run source-side
        ///     completeness over the projection under <c>RequiredMappingStrategy.Both</c>. A dotted path marks its
        ///     ROOT consumed, matching the runtime resolver.
        /// </param>
        /// <param name="flattenRoots">
        ///     The <c>[Flatten("Root")]</c> directives on the projection method. Threaded here because this
        ///     resolver is a SECOND translator, not a caller of the first: it used to receive no flatten at all,
        ///     so <c>[Flatten]</c> resolved through <c>.Map</c> and was discarded without a word through
        ///     <c>.Project</c> — the same mapper filling the flattened members on one overload and leaving them
        ///     unmapped on the other (recorded as <c>D10</c>, now closed). A pulled-up leaf is
        ///     <c>__s.Root.Leaf</c>, which is the canonical navigation access every query provider translates, so
        ///     the honest answer here is to honour it rather than to refuse it.
        /// </param>
        /// <param name="mapValues">
        ///     The <c>[MapValue]</c> directives — a rendered constant, or a <c>Use=</c> provider call, per target.
        ///     Validated through the same <c>TryValidateMapValueTarget</c> the create map runs, so the rule is one
        ///     statement rather than two that can drift.
        /// </param>
        /// <param name="ignoredSourceMembers">
        ///     Source members disowned by <c>[MapIgnoreSource]</c>, by real name, for the DWARF064 shadow rule this
        ///     endpoint shares with the create map. Empty when none were declared.
        /// </param>
        private static List<ProjectionMemberMap> ResolveProjectionMembers(
            ITypeSymbol sourceType,
            INamedTypeSymbol targetType,
            HashSet<string> ignores,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            in MapperOptions options,
            IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
            EnumPolicy enumPolicy,
            string paramExpr,
            IReadOnlyList<(string Target, bool HasNullSub, TypedConstant NullSub, string? When, string? NullSubLiteral)>?
                mapPropertyExtras,
            // I19: the projection endpoint reads NullCollections like every other endpoint. It used to read it
            // nowhere at all, so a null source collection came back EMPTY through .Map (the documented AsEmpty
            // default) and NULL through .Project — the same member answering differently depending on which
            // method the caller reached for. The one call site passes the mapper's real setting.
            // I20: the projection endpoint reads ImplicitConversions, and it used to read it NOWHERE. The option
            // reached ResolveMembers at five call sites and this resolver at none, so under
            // [DwarfMapper(ImplicitConversions = false)] a lossy-but-C#-implicit conversion (long -> double) was
            // an Error at .Map and produced no diagnostic at all at .Project — the strict TRUST setting silently
            // off at one endpoint. The one call site passes the mapper's real setting.
            HashSet<string>? consumedSources,
            IReadOnlyList<string> flattenRoots,
            IReadOnlyList<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>
                mapValues,
            // Source members disowned by [MapIgnoreSource], by real name. Read by the DWARF064 shadow rule, which
            // this endpoint shares with the create map through TryValidateMapValueTarget — and which reached
            // the create map's [MapIgnoreSource] set first and this one not at all, the "fixed at 1 of N sites"
            // shape. Empty means "none declared".
            HashSet<string> ignoredSourceMembers)
        {
            // IgnoreObsoleteMembers, target side: fold obsolete destination members into the ignore set,
            // exactly as ResolveMembers does, so every downstream check honours it through one addition. An
            // obsolete member that IS explicitly targeted stays out of the set — opting a retired member back
            // in deliberately must keep working at both endpoints.
            if (options.IgnoreObsolete)
            {
                var explicitTargets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var em in explicitMaps) explicitTargets.Add(em.Target);
                // A [MapValue]'d target is explicitly targeted too, exactly as ResolveMembers reads it: opting a
                // retired member back in deliberately must keep working at both endpoints, and reading only
                // [MapProperty] here would have made "at both endpoints" false the moment [MapValue] arrived.
                foreach (var mv in mapValues)
                    explicitTargets.Add(mv.Target);

                ignores = new HashSet<string>(ignores, IgnoreNameComparer);
                foreach (var name in ObsoleteMemberNames(targetType))
                    if (!explicitTargets.Contains(name))
                    {
                        ignores.Add(name);
                    }
            }

            // Targets the runtime COULD have deferred under SkipNullSourceMembers (settable, not init-only, not
            // required). Mirrors ResolveMembers' rule so the projection diagnostic fires on exactly the members the
            // option would have changed — no more, no less.
            var deferrableTargets = new HashSet<string>(StringComparer.Ordinal);
            if (options.SkipNullSourceMembers)
            {
                foreach (var t in TypeAndBasesBelowObject(targetType))
                    foreach (var tm in t.GetMembers())
                        if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: false } && !p.IsRequired)
                        {
                            deferrableTargets.Add(p.Name);
                        }
                        else if (tm is IFieldSymbol f && !f.IsReadOnly && !f.IsConst && !f.IsRequired)
                        {
                            deferrableTargets.Add(f.Name);
                        }
            }

            // Per-member [MapProperty] modifiers, keyed by target — checked against the translatable set below.
            var extrasByTarget =
                new Dictionary<string, (bool HasNullSub, string? When)>(StringComparer.Ordinal);
            if (mapPropertyExtras is not null)
            {
                foreach (var e in mapPropertyExtras)
                    extrasByTarget[e.Target] = (e.HasNullSub, e.When);
            }

            // NameConvention.Flexible (1) must reach the projection path too, or the SAME mapper resolves members
            // one way through .Map and another through .Project — the divergence the ambiguity fix below exists to
            // prevent. Expressed as a comparer so it rides the existing propagation into nested/ctor resolvers.
            var comparer = options.NameConvention == 1
                ? FlexibleNameComparer.Instance
                : options.CaseInsensitive
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;
            var sources = BuildProjectionSourceLookup(sourceType, comparer, compilation, location, diagnostics);

            // [Flatten] roots, through the SAME walk the runtime resolver uses — one refusal of an invalid root
            // rather than two that can disagree. PUBLIC ONLY (ProjectionPublicOnly) because a leaf the provider
            // cannot read is not a leaf this endpoint can pull up, and warnNullableHop: false because DWARF044
            // describes a null dereference in emitted C#, which a translated path does not perform (the dotted
            // [MapProperty] source below already makes that call, in the same words).
            var flattenInfos = ResolveFlattenInfos(flattenRoots,
                sourceType,
                comparer,
                compilation,
                ProjectionPublicOnly,
                false,
                location,
                diagnostics);

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
                targetType,
                sourceType,
                writableMembers.Count,
                compilation,
                location,
                diagnostics,
                explicitMaps);
            // Selection reported a BLOCKING DWARF025/DWARF026 and has no constructor to offer. Resolving members
            // on top of that would append a second, contradictory complaint to a build that already fails; the
            // create-map path does the same thing (`if (ctor is null) continue;`).
            if (!ctorDecided)
            {
                return new List<ProjectionMemberMap>();
            }

            // Parameter name → type, for resolving an explicit map whose target is a parameter rather than a
            // member. Ordinal, matching ResolveConstructorArguments' explicit-map index. Empty when the target
            // will be built by member-init, where a map naming a parameter stays DWARF008 (unknown destination)
            // — the constructor is not used there, so there is nothing for it to bind to.
            var ctorParamTypes = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
            if (projectionCtor is not null)
            {
                foreach (var p in projectionCtor.Parameters)
                    ctorParamTypes[p.Name] = p.Type;
            }

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
                if (isCtorArg)
                {
                    ctorArgTargets.Add(tgtName);
                }

                if (!writableByName.TryGetValue(tgtName, out var tgtType) && !ctorParamTypes.TryGetValue(tgtName, out tgtType))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                    continue;
                }

                // A custom converter (Use=) cannot run inside a provider-translated projection.
                if (use is not null)
                {
                    EmitDwarf028(diagnostics,
                        location,
                        tgtName,
                        "custom converter (Use=) is not translatable in projection; remove Use= or map at runtime");
                    continue;
                }

                // The OTHER [MapProperty] per-member modifiers are equally untranslatable, and were previously
                // dropped in silence: the rename still bound, so completeness was satisfied and nothing was
                // reported, while .Map applied the modifier and .Project did not — the same mapper yielding
                // different data depending on which method you called. Same rule as Use= above.
                if (UntranslatableModifierReason(extrasByTarget, tgtName) is { } modifierReason)
                {
                    EmitDwarf028(diagnostics, location, tgtName, modifierReason);
                    continue;
                }

                // Resolve the source, supporting a dotted path (e.g. "Colour.Code") for value-object /
                // nested-scalar flattening — matching the class-model [MapProperty] dotted-path feature.
                // The projection accessor "__s.Colour.Code" is built verbatim below; the walk is only here to
                // find the leaf type and validate each hop is a readable member.
                //
                // The nullable-hop answer is deliberately NOT taken: DWARF044 warns that dereferencing a null
                // interior throws, which is true in emitted C# and false here — the provider translates the path
                // to a join that yields null. Same walk, different consequence.
                MemberFacts.TryResolvePath(sourceType,
                    srcName,
                    compilation,
                    ProjectionPublicOnly,
                    out var sm,
                    out _,
                    out _);

                if (sm is null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                    continue;
                }

                var srcExprForExplicit = paramExpr + "." + Identifiers.EscapePath(srcName);
                var inlineExpr = ResolveProjectionExpr(
                    sm,
                    tgtType,
                    srcExprForExplicit,
                    0,
                    compilation,
                    location,
                    diagnostics,
                    tgtName,
                    enumPolicy,
                    comparer,
                    options.AutoNest,
                    options.NullAsNull,
                    options.ImplicitConversions);
                if (inlineExpr is null)
                {
                    continue;
                }

                // A parameter's expression goes into the constructor CALL, never into an initializer beside it:
                // an init-only member cannot be assigned after the constructor that already set it, and emitting
                // both produced `new T(x) { Same = x }`.
                if (isCtorArg)
                {
                    ctorArgExprs[tgtName] = inlineExpr;
                }
                else
                {
                    result.Add(new ProjectionMemberMap(tgtName, inlineExpr));
                }
            }

            // ── [MapValue] ───────────────────────────────────────────────────────
            // After the explicit maps and before AUTO matching, which is where ResolveMembers reads it, and the
            // position is part of the contract rather than a convenience: a [MapValue]'d target counts as mapped
            // and suppresses DWARF001 at both endpoints, and a [MapValue] that outranked a [MapProperty] at one
            // endpoint and not the other would make .Project disagree with .Map about which directive wins.
            //
            // The validation is the create map's own, hoisted rather than copied — see TryValidateMapValueTarget.
            // What differs here is only what this endpoint can SEE: the public-only writable set, the projection
            // source lookup, and the constructor the projection actually calls.
            foreach (var mv in mapValues)
            {
                if (!TryValidateMapValueTarget(mv.Target,
                        handled,
                        ignores,
                        ctorParamTypes.ContainsKey,
                        writableByName,
                        // The projection lookup is already keyed the way this pair matches and carries the real
                        // source name — the spelling [MapIgnoreSource] is read under, here and for source coverage.
                        name => sources.TryGetValue(name, out var shadowed)
                                && !ignoredSourceMembers.Contains(shadowed.Name)
                            ? shadowed.Name
                            : null,
                        location,
                        diagnostics,
                        out var mvTgtType))
                {
                    continue;
                }

                if (mv.IsConstant)
                {
                    var literal = mv.ConstLiteral;
                    if (literal is null && !TryFormatConstant(mv.Value, mvTgtType, compilation, out literal, out var why))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueTypeMismatch,
                            location,
                            why));
                        continue;
                    }

                    // A constant is the one thing every query provider translates without help: it becomes a
                    // literal in the SELECT. Nothing is read from the destination, so the object-initializer
                    // argument that makes SkipNullSourceMembers untranslatable here does not reach it.
                    result.Add(new ProjectionMemberMap(mv.Target, literal));
                }
                else if (mv.Use is not null)
                {
                    // Refused, not dropped, and for the reason every other Use= at this endpoint is refused: a
                    // provider translates an expression tree into a query and cannot call back into managed code
                    // to ask what the value should be. Same wording shape as the [MapProperty(Use=)] refusal
                    // twenty lines up, so a caller who hits both is not told two different stories.
                    EmitDwarf028(diagnostics,
                        location,
                        mv.Target,
                        "[MapValue(Use = ...)] is not translatable in projection (a query provider cannot call a " + "method inside an expression tree); assign a constant instead, or map this member at " + "runtime");
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid,
                        location,
                        $"[MapValue] for '{mv.Target}' provides neither a constant value nor Use="));
                }
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
                {
                    return result;
                }

                // C4: pass comparer (carries CaseInsensitive setting) to ctor projection resolver.
                var ctorExpr = ResolveProjectionCtorExpr(
                    projectionCtor,
                    sourceType,
                    paramExpr,
                    0,
                    compilation,
                    location,
                    diagnostics,
                    targetType,
                    enumPolicy,
                    comparer,
                    options.AutoNest,
                    options.NullAsNull,
                    options.ImplicitConversions,
                    ctorArgExprs,
                    ctorArgTargets);
                if (ctorExpr is null)
                {
                    return result;
                }

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
                    {
                        handled.Add(m.Name);
                    }
            }

            foreach (var target in writableMembers)
            {
                if (handled.Contains(target.Name) || ignores.Contains(target.Name))
                {
                    continue;
                }

                if (!sources.TryGetValue(target.Name, out var src))
                {
                    // A source member MAY exist and simply be non-public. The runtime path would bind it under
                    // [DwarfMapper(AllowNonPublic = true)]; projection enumerates public members only, so the
                    // generic "no matching source member" would send the reader hunting for a member that is
                    // plainly there. Name the real reason instead.
                    if (options.AllowNonPublic && ReadableMembers(sourceType, compilation, true).Any(m => comparer.Equals(m.Name, target.Name)))
                    {
                        EmitDwarf028(diagnostics,
                            location,
                            target.Name,
                            "the matching source member is non-public and AllowNonPublic is not honoured by " + "projection (an expression tree is built from the public surface); map this member " + "at runtime, or make the source member public");
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
                        {
                            flatMatches.Add((fi.Root, leaf.Name, leaf.Type));
                        }

                    if (flatMatches.Count > 1)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten,
                            location,
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
                            fm.LeafType,
                            target.Type,
                            paramExpr + "." + Identifiers.EscapePath(fm.Root + "." + fm.Leaf),
                            0,
                            compilation,
                            location,
                            diagnostics,
                            target.Name,
                            enumPolicy,
                            comparer,
                            options.AutoNest,
                            options.NullAsNull,
                            options.ImplicitConversions);
                        if (flatExpr is not null)
                        {
                            result.Add(new ProjectionMemberMap(target.Name, flatExpr));
                        }

                        continue;
                    }

                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember,
                        location,
                        target.Name,
                        MemberName: target.Name));
                    continue;
                }

                // [DwarfMapper(AutoMatchMembers = false)] is a TRUST BOUNDARY, not a convenience toggle: it is
                // the anti-over-posting guard (OWASP API6). It was honoured by the runtime resolver and ignored
                // here, so the same mapper refused the implicit wire through .Map and performed it through
                // .Project — a security control that silently did not apply at one endpoint. Explicit
                // [MapProperty]/[MapValue]/[MapIgnore] have already been handled above; only the implicit
                // by-name wire is blocked, matching ResolveMembers exactly.
                if (options.ExplicitOnly)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AutoMatchDisabled,
                        location,
                        target.Name,
                        MemberName: target.Name));
                    continue;
                }

                // [DwarfMapper(SkipNullSourceMembers = true)] cannot be honoured inside an expression tree: the
                // runtime emitter guards the assignment with `if (src.X is not null) …` so the target keeps its own
                // default, which a projection's object initializer has no way to express — it always assigns, so a
                // null source would overwrite that default with null. Reported PER AFFECTED MEMBER, using the same
                // eligibility the runtime uses (nullable source + a target it could actually have deferred), so
                // members the option never touched stay quiet.
                if (options.SkipNullSourceMembers && (src.Type.IsReferenceType || IsNullableValue(src.Type, out _)) && deferrableTargets.Contains(target.Name))
                {
                    EmitDwarf028(diagnostics,
                        location,
                        target.Name,
                        "SkipNullSourceMembers is not translatable in projection (an object initializer always " + "assigns, so a null source would overwrite the target's default instead of keeping it); " + "map this member at runtime, or drop the option for this mapper");
                    continue;
                }

                consumedSources?.Add(src.Name);
                var srcAccessExpr = paramExpr + "." + Identifiers.Escape(src.Name);
                // C4: pass comparer so nested objects respect CaseInsensitive setting.
                var inlineExpr = ResolveProjectionExpr(
                    src.Type,
                    target.Type,
                    srcAccessExpr,
                    0,
                    compilation,
                    location,
                    diagnostics,
                    target.Name,
                    enumPolicy,
                    comparer,
                    options.AutoNest,
                    options.NullAsNull,
                    options.ImplicitConversions);
                if (inlineExpr is not null)
                {
                    result.Add(new ProjectionMemberMap(target.Name, inlineExpr));
                }
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
        /// <param name="srcType">The source type of this leg of the projection.</param>
        /// <param name="tgtType">The destination type this expression must produce.</param>
        /// <param name="srcExpr">
        ///     The C# expression that reads the source at this point — <c>__s.Order</c> at the top level, or a
        ///     <c>.Select</c> lambda parameter such as <c>__i0</c> one level in. Every deeper access is appended
        ///     to it, so the whole projection is one expression rooted here.
        /// </param>
        /// <param name="depth">
        ///     How many nesting levels down this call is. It bounds the recursion (<c>ProjectionMaxDepth</c>) and
        ///     names the element lambda parameter, so nested <c>.Select</c>s do not shadow one another.
        /// </param>
        /// <param name="compilation">
        ///     The compilation every type question is asked against — implicit conversions,
        ///     <c>IParsable</c>/<c>IFormattable</c>, member accessibility.
        /// </param>
        /// <param name="location">The declaration site every diagnostic raised here is reported at.</param>
        /// <param name="diagnostics">
        ///     Diagnostic sink. A construct no query provider can translate appends <c>DWARF028</c> with the
        ///     specific reason and returns <see langword="null" />; the caller then drops the whole method rather
        ///     than emit a projection missing a member.
        /// </param>
        /// <param name="targetMemberName">
        ///     Name of the TOP-LEVEL destination member this whole expression fills. Quoted by every
        ///     <c>DWARF028</c> raised beneath it, so a refusal deep inside a nested object still names the member
        ///     the reader actually wrote.
        /// </param>
        /// <param name="enumPolicy">
        ///     The mapper's <c>EnumStrategy</c>. <c>ByName</c> is refused here: it compiles to a switch, and a
        ///     switch is not something a query provider can translate.
        /// </param>
        /// <param name="comparer">
        ///     C4: the case-sensitivity comparer for member name matching; passed recursively into
        ///     nested object and ctor resolvers so CaseInsensitive propagates to all depths.
        /// </param>
        /// <param name="autoNest">
        ///     <c>[DwarfMapper(AutoNest = …)]</c>, per-method override included. When false an unrequested nested
        ///     pair is refused with the same <c>DWARF005</c> the runtime resolver raises, so turning auto-nesting
        ///     off means one thing at both endpoints.
        /// </param>
        /// <param name="nullAsNull">
        ///     The mapper's <c>NullCollections</c> setting. When true — and only where the target can actually
        ///     hold a null — a null source collection projects to <see langword="null" /> rather than to an empty
        ///     one; it degrades to the documented <c>AsEmpty</c> default where the target cannot.
        /// </param>
        /// <param name="implicitConversions">
        ///     <c>[DwarfMapper(ImplicitConversions = …)]</c>, forwarded to the <c>DWARF038</c> report for a
        ///     cross-category numeric pair: an Info suggestion when true, a build Error when false.
        /// </param>
        private static string? ResolveProjectionExpr(
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            string srcExpr,
            int depth,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            string targetMemberName,
            EnumPolicy enumPolicy,
            StringComparer comparer,
            bool autoNest,
            bool nullAsNull,
            bool implicitConversions)
        {
            // ── Depth guard ───────────────────────────────────────────────────────
            if (depth > ProjectionMaxDepth)
            {
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    $"projection nesting depth exceeded {ProjectionMaxDepth}; split into a runtime mapper");
                return null;
            }

            // ── Pre-check: collection/dictionary targets BEFORE implicit-conversion ──
            // EF Core cannot translate HashSet/Dictionary/immutable collection projections even
            // when source==target (same type is directly assignable but NOT SQL-translatable).
            // We must check collection-shaped types BEFORE the HasImplicitConversion fast-path.
            if (CollectionConverter.TryResolve(srcType,
                    tgtType,
                    out var srcElem,
                    out var tgtElem,
                    out var shape))
            {
                if (!CollectionConverter.IsTargetKindTranslatable(shape.Target))
                {
                    var tgtTypeName = tgtType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    EmitDwarf028(diagnostics,
                        location,
                        targetMemberName,
                        $"collection type '{tgtTypeName}' is not translatable in projection (HashSet/ISet/immutable/Dictionary targets are not supported by EF Core)");
                    return null;
                }

                // Translatable collection: emit .Select(...).ToList()/.ToArray()/lazy
                var elemParam = $"__i{depth}";
                // C4: propagate comparer into element expression resolver.
                var elemExpr = ResolveProjectionExpr(
                    srcElem,
                    tgtElem,
                    elemParam,
                    depth + 1,
                    compilation,
                    location,
                    diagnostics,
                    targetMemberName,
                    enumPolicy,
                    comparer,
                    autoNest,
                    nullAsNull,
                    implicitConversions);
                if (elemExpr is null)
                {
                    return null; // DWARF028 already emitted
                }

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
                if (!ProjectionSourceMayBeNull(srcType))
                {
                    return collectionExpr;
                }

                // I19: WHICH VALUE the guard's null arm yields is NullCollections, and this endpoint used to
                // answer it without asking — always `null`, whatever the mapper had configured. The effective
                // rule is the SAME LINE the runtime endpoint computes (MapperExtractor.Conversions, the
                // `nullAsNull && IsNullableReferenceType(tgtType)` gate): AsNull propagates the null only when
                // the target member can HOLD it, and degrades to AsEmpty when it cannot. Reading the option
                // here is what makes the two endpoints agree; computing it the same way is what keeps them
                // agreeing in an oblivious (`#nullable disable`) context, where BOTH degrade.
                if (nullAsNull && IsNullableReferenceType(tgtType))
                {
                    return $"{srcExpr} == null ? null : {collectionExpr}";
                }

                // AsEmpty — the documented default (docs/options.md, `NullCollections`: "Null source collection
                // → AsEmpty (never throws)"). The empty arm is chosen per target kind so the two arms have the
                // SAME static type and the conditional needs no cast: `List<T>` both sides, `T[]` both sides,
                // `IEnumerable<T>` both sides. No new construct class enters the tree beyond an empty
                // materialisation — the ternary itself is the one this endpoint already emitted here.
                var emptyExpr = shape.Target switch
                {
                    CollectionConverter.TargetKind.Array =>
                        $"global::System.Array.Empty<{tgtElemFqn}>()",
                    CollectionConverter.TargetKind.IEnumerable =>
                        $"global::System.Linq.Enumerable.Empty<{tgtElemFqn}>()",
                    _ =>
                        $"new global::System.Collections.Generic.List<{tgtElemFqn}>()"
                };
                return $"{srcExpr} == null ? {emptyExpr} : {collectionExpr}";
            }

            // ── Pre-check: Dictionary targets (always non-translatable in projection) ──
            // Check before HasImplicitConversion to catch same-type dictionary members.
            if (DictionaryConverter.TryResolve(srcType,
                    tgtType,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    "Dictionary targets are not translatable in projection; map at runtime");
                return null;
            }

            // ── 1. Direct-assignable (implicit — covers widening numeric, same-type, etc.) ──
            if (HasImplicitConversion(compilation, srcType, tgtType))
            {
                // ...but "C# will assign it" is not "the product has no opinion about it". Cross-category numeric
                // (long → double, int → float) is implicit in C# and LOSSY, and it is the one lossy kind that
                // reaches this line: narrowing and parse/format have no implicit conversion, so they fall through
                // to the DWARF028 refusals below. The .Map endpoint has always reported it here — a Warning by
                // default, an Error under ImplicitConversions = false — and this endpoint reported nothing at
                // either severity (TASKS.md I20).
                //
                // The remedy is the SAME emitter the runtime endpoint calls, not a projection-flavoured one:
                // the endpoints have to agree on WHETHER THE BUILD BREAKS, which is the whole content of the
                // option, and two emitters are two things that can disagree. Deliberately NOT a DWARF028: that
                // id means "a query provider cannot translate this", which is FALSE here — a widening cast is
                // the most translatable thing there is — and refusing under the permissive default would be a
                // capability regression at one endpoint, which is the branch I19 rejected for NullCollections.
                //
                // Deliberately NOT scoped to the method either (no DiagnosticInfo.ScopedToMethod, no DWARF096).
                // I14's rule: a DWARF028 describes THIS endpoint's translatability and is confined to the
                // projection, while every other error describes the SOURCE MODEL and is equally true of the .Map
                // methods over the same pair. A lossy type pair is the second kind — the class dies, exactly as
                // it already does when a .Map method sits beside the projection over that pair.
                if (NumericConverter.IsCrossCategoryLossy(srcType, tgtType))
                {
                    EmitImplicitConversionDiag(diagnostics,
                        location,
                        targetMemberName,
                        srcType,
                        tgtType,
                        "cross-category numeric",
                        implicitConversions,
                        true);
                }

                if (NullRefIntoNonNullableRef(srcType, tgtType))
                {
                    // Same rule as the class endpoint (MemberMap.NullRefIntoNonNullable): a bare nullable access
                    // into a non-nullable member or constructor parameter is CS8601/CS8604 from inside the
                    // generated file, in an expression tree the consumer can edit even less than a method body.
                    // Null-forgiven here, and DWARF070 carries the signal against the DTO — once per source
                    // member per method, whichever binding (initializer or constructor) reached it first.
                    ReportProjectionNullRefIntoNonNullable(diagnostics, location, srcExpr);
                    return srcExpr + "!";
                }

                return srcExpr;
            }

            /// <summary>
            ///     DWARF070 for the projection endpoint, keyed on the source member the access names so the
            ///     initializer and constructor bindings of one member report once.
            /// </summary>
            static void ReportProjectionNullRefIntoNonNullable(
                List<DiagnosticInfo> diagnostics,
                LocationInfo? location,
                string srcExpr)
            {
                // The LABEL, not the bare name, on both the dedup probe and the report — they must be the same
                // string or the once-per-member guarantee this method exists for silently stops holding.
                var name = NullSourceLabel(srcExpr.Substring(srcExpr.LastIndexOf('.') + 1).TrimStart('@'));
                foreach (var d in diagnostics)
                    if (ReferenceEquals(d.Descriptor, DiagnosticDescriptors.NullableRefSourceToNonNullableTarget) &&
                        d.MessageArg == name &&
                        Equals(d.Location, location))
                    {
                        return;
                    }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NullableRefSourceToNonNullableTarget, location, name));
            }

            // ── 2. Enum by-value cast (enum→enum) ─────────────────────────────────
            if (srcType.TypeKind == TypeKind.Enum && tgtType.TypeKind == TypeKind.Enum && enumPolicy.Strategy == EnumStrategy.ByValue)
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
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
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
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    "integral→enum conversion is narrowing (the source does not fit the enum's underlying type) and cannot be range-checked in a projection; map it at runtime");
                return null;
            }

            // ── UNSAFE: enum by-name (enumPolicy == ByName, different enum types) ──
            if ((srcType.TypeKind == TypeKind.Enum || tgtType.TypeKind == TypeKind.Enum) && enumPolicy.Strategy == EnumStrategy.ByName)
            {
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    "enum by-name mapping is not translatable in projection; use EnumStrategy.ByValue or map at runtime");
                return null;
            }

            // ── UNSAFE: numeric narrowing (NumericConverter would fire: both integral, no implicit) ──
            if (TypeInterfaces.IsIntegral(srcType) && TypeInterfaces.IsIntegral(tgtType))
            {
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    "narrowing numeric conversion is not SQL-translatable (would need CreateChecked); map at runtime or use a widening target type");
                return null;
            }

            // ── UNSAFE: string↔T parsable (ParsableConverter would fire) ─────────
            if ((srcType.SpecialType == SpecialType.System_String && tgtType.TypeKind != TypeKind.Enum && TypeInterfaces.ImplementsIParsable(compilation, tgtType)) ||
                (tgtType.SpecialType == SpecialType.System_String && srcType.SpecialType != SpecialType.System_String && srcType.TypeKind != TypeKind.Enum && (TypeInterfaces.ImplementsIFormattable(srcType) || srcType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char)))
            {
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    "string parse/format is not translatable in projection (IParsable/IFormattable); map at runtime");
                return null;
            }

            // ── 3. Nested named object (recursive) ───────────────────────────────
            if (srcType is INamedTypeSymbol namedSrc && tgtType is INamedTypeSymbol namedTgt && IsMappableObjectPair(compilation, srcType, namedTgt))
            {
                // [DwarfMapper(AutoNest = false)] means "do not synthesize nested pairs I did not ask for". The
                // runtime resolver refuses with DWARF005; projection auto-nested regardless, so the two endpoints
                // disagreed about whether a nested member was mapped at all. Report the same diagnostic the
                // runtime reports, so turning auto-nesting off means the same thing everywhere.
                if (!autoNest)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.NoImplicitConversion,
                        location,
                        targetMemberName));
                    return null;
                }

                // C4: pass comparer into nested object resolver.
                return ResolveProjectionNestedObjectExpr(
                    namedSrc,
                    namedTgt,
                    srcExpr,
                    depth,
                    compilation,
                    location,
                    diagnostics,
                    targetMemberName,
                    enumPolicy,
                    comparer,
                    autoNest,
                    nullAsNull,
                    implicitConversions);
            }

            // ── Nullable-value source T? → a target that CAN hold null, or one that cannot ───────────────────────
            // C5: emit a null-preserving HasValue ternary (SQL-translatable) instead of .Value, which
            // throws on null.
            //
            // The gate asks TryGetNullableCapableTarget — CAN the destination store the null? — not
            // IsNullableValue, which asks the destination's KIND. Asking the kind is what made `S1? M` →
            // `D1? M` project when D1 happened to be a struct and be REFUSED with DWARF028 when D1 was a class
            // or a record, while .Map on the same mapper lifted all of them (TASKS.md I14; the runtime half of
            // the same confusion is I7). One question, one answer, at both endpoints.
            if (IsNullableValue(srcType, out var srcUnderlying))
            {
                if (TryGetNullableCapableTarget(tgtType, out var tgtUnderlying))
                {
                    // int?→long?: null-preserving ternary: __s.X.HasValue ? (long?)__s.X.Value : null
                    var innerExpr = ResolveProjectionExpr(
                        srcUnderlying,
                        tgtUnderlying,
                        srcExpr + ".Value",
                        depth,
                        compilation,
                        location,
                        diagnostics,
                        targetMemberName,
                        enumPolicy,
                        comparer,
                        autoNest,
                        nullAsNull,
                        implicitConversions);
                    if (innerExpr is null)
                    {
                        return null;
                    }

                    // The cast is carried ONLY for a Nullable<U> target, where the two arms (U and the null
                    // literal) have no best common type and CS0173 would follow. For a nullable-ANNOTATED
                    // REFERENCE target the inner expression already has the target's own type and the null
                    // literal converts to it, so the conditional's natural type IS the target — the same shape
                    // ResolveProjectionNestedObjectExpr has always emitted for a nullable reference source.
                    if (IsNullableValue(tgtType, out _))
                    {
                        var tgtNullableFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        return $"{srcExpr}.HasValue ? ({tgtNullableFqn}){innerExpr} : null";
                    }

                    return $"{srcExpr}.HasValue ? {innerExpr} : null";
                }

                // int?→long (non-nullable target): REFUSED. This link needs a null decision, and NullStrategy —
                // the option that makes it — never reaches the projection engine, so the emitted `.Value` ignored
                // it: a mapper configured NullStrategy.SetDefault returned 0 from .Map and threw
                // InvalidOperationException from .Project for the same input. Emitting `.Value` also pushes the
                // failure to runtime inside a provider-translated query, where NULL semantics are the provider's,
                // not ours. Refusing at build time keeps the null decision explicit and the two paths honest.
                // Deliberately annotation-strict on the reference side, exactly as TryGetNullableCapableTarget
                // is for .Map: an un-annotated (or oblivious) reference target is a promise that it holds no
                // null, so the documented NullStrategy contract keeps governing it — and NullStrategy is the
                // one thing this endpoint cannot express.
                EmitDwarf028(diagnostics,
                    location,
                    targetMemberName,
                    "a nullable source mapped to a target that cannot hold null needs a null decision, and " + "NullStrategy is not translatable in projection; make the target nullable, or map this " + "member at runtime");
                return null;
            }

            // Reference source into a Nullable<U> target — the re-kinded pair, the other way round.
            // `S1? M` → `D1? M` where S1 is a class and D1 a struct. Nothing above catches it: Nullable<D1> is
            // excluded from IsMappableObjectPair by name, so the nested-object branch declines and the pair fell
            // through to "no translatable conversion found" — while .Map lifts it (I7's reverse genre, the
            // NullableProjectRef handling). The lift is a CALL-SITE question at both endpoints, because a
            // value-typed inner expression has no way to answer null; here the call site is this ternary.
            if (srcType.IsReferenceType && IsNullableValue(tgtType, out var refTgtUnderlying))
            {
                // The annotation is STRIPPED for the recursion on purpose. The inner question is only "how does
                // an S1 become a D1", and the nested-object resolver adds its OWN null guard for a nullable
                // reference source — which, with a VALUE-type target below it, would produce
                // `x == null ? null : new D1 { … }`: two arms with no best common type (CS0173), nested inside
                // the guard this branch is about to add anyway. One guard, and it is this one.
                var refInnerExpr = ResolveProjectionExpr(
                    srcType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                    refTgtUnderlying,
                    srcExpr,
                    depth,
                    compilation,
                    location,
                    diagnostics,
                    targetMemberName,
                    enumPolicy,
                    comparer,
                    autoNest,
                    nullAsNull,
                    implicitConversions);
                if (refInnerExpr is null)
                {
                    return null;
                }

                // A non-nullable-annotated source cannot be null, so it needs no guard — only the widening to
                // Nullable<U>, which is implicit. Guarding it would be the false-CS8601 shape
                // ProjectionSourceMayBeNull exists to avoid.
                if (!ProjectionSourceMayBeNull(srcType))
                {
                    return refInnerExpr;
                }

                // The cast is required here: null and U have no best common type (CS0173).
                var refTgtNullableFqn = tgtType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"{srcExpr} == null ? null : ({refTgtNullableFqn})({refInnerExpr})";
            }

            // ── Fallback: no translatable conversion found ────────────────────────
            EmitDwarf028(diagnostics,
                location,
                targetMemberName,
                "no translatable conversion found; map at runtime instead");
            return null;
        }

        /// <summary>
        ///     C6 helper: returns true when a cast from <paramref name="src" /> to <paramref name="tgt" /> is
        ///     widening or same-width (thus safe as a direct inline cast in SQL projection).
        ///     Both must be integral types.
        /// </summary>
        internal static bool IsWideningOrSameWidth(ITypeSymbol src, ITypeSymbol tgt)
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

            if (!IntegralInfo(src, out var sw, out var ss))
            {
                return false;
            }

            if (!IntegralInfo(tgt, out var tw, out var ts))
            {
                return false;
            }

            // A plain (unchecked) cast src→tgt is lossless — safe to inline in a projection that can't do a
            // checked conversion — ONLY when the target's representable range fully contains the source's:
            //   • same signedness    → target width must be ≥ source width  (short→int, uint→ulong)
            //   • unsigned → signed  → target needs a strictly wider type for the sign bit  (byte→short, uint→long)
            //   • signed → unsigned  → never lossless (source may be negative)
            // Anything else (e.g. uint→int, ushort→short, long→int) is narrowing and falls through to DWARF028.
            if (ss == ts)
            {
                return tw >= sw;
            }

            if (!ss && ts)
            {
                return tw > sw;
            }

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
            return ctor.Parameters.Any(p => comparer.Equals(p.Name, memberName) || StringComparer.OrdinalIgnoreCase.Equals(p.Name, memberName));
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
        /// <param name="targetType">The type being projected INTO — the one whose construction is decided here.</param>
        /// <param name="sourceType">
        ///     The projection's source type, handed to the selector so its satisfiability narrowing can ask whether
        ///     a candidate's parameters have anywhere to come from.
        /// </param>
        /// <param name="writableMemberCount">
        ///     How many members of the target member-init could set. Zero means member-init cannot express the
        ///     mapping at all, however the target is constructed.
        /// </param>
        /// <param name="compilation">The compilation the selector's accessibility checks run against.</param>
        /// <param name="location">
        ///     The declaration site the selector's <c>DWARF025</c> / <c>DWARF026</c> are reported at.
        /// </param>
        /// <param name="diagnostics">
        ///     Diagnostic sink, passed straight through to the selector — this function raises nothing of its own.
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
            INamedTypeSymbol targetType,
            ITypeSymbol sourceType,
            int writableMemberCount,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            IReadOnlyList<(string Source, string Target, string? Use)>? explicitMaps)
        {
            var hasParameterlessCtor = targetType.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic && c.Parameters.Length == 0);
            var memberInitIsExpressible = writableMemberCount > 0 && hasParameterlessCtor;

            var selected = ConstructorSelector.Select(compilation,
                targetType,
                diagnostics,
                location,
                out _,
                ProjectionPublicOnly,
                sourceType,
                explicitMaps);
            if (selected is null)
            {
                return (false, false, null);
            }

            // The selector's answer is a parameterless constructor and this target can be built by one, so
            // member-init it is. Asked of the constructor's ARITY rather than of the selector's
            // useObjectInitializerOnly flag, which is false for an ANNOTATED parameterless constructor: reading
            // the flag there sent the projection down the constructor path and it then fell through to the
            // widest overload, so [DwarfMapperConstructor] on `Dst()` projected as `new Dst(id, name)` while the
            // create map over the same pair mapped by initializer. The annotation cannot name one constructor
            // and get another.
            if (selected.Parameters.Length == 0 && memberInitIsExpressible)
            {
                return (true, false, null);
            }

            // The selector named a constructor — because the target has no parameterless one, or because
            // [DwarfMapperConstructor] overrode the preference. Either way it is the answer.
            if (selected.Parameters.Length > 0)
            {
                return (true, true, selected);
            }

            // The selector's answer is a parameterless constructor, and member-init cannot carry this target.
            // Its answer is therefore not one this endpoint can use, so fall back to the widest public
            // constructor — what this endpoint chose here before, unchanged.
            return (true, true, targetType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public &&
                            !c.IsStatic &&
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
        /// <param name="srcType">The nested source object being read.</param>
        /// <param name="tgtType">
        ///     The nested destination object being built — by member initializer, or through a constructor when
        ///     <c>ChooseProjectionConstructor</c> says the target cannot be member-initialized.
        /// </param>
        /// <param name="srcExpr">
        ///     The C# expression that reads the source at this point — <c>__s.Order</c> at the top level, or a
        ///     <c>.Select</c> lambda parameter such as <c>__i0</c> one level in. Every deeper access is appended
        ///     to it, so the whole projection is one expression rooted here.
        /// </param>
        /// <param name="depth">
        ///     How many nesting levels down this call is. It bounds the recursion (<c>ProjectionMaxDepth</c>) and
        ///     names the element lambda parameter, so nested <c>.Select</c>s do not shadow one another.
        /// </param>
        /// <param name="compilation">
        ///     The compilation every type question is asked against — implicit conversions,
        ///     <c>IParsable</c>/<c>IFormattable</c>, member accessibility.
        /// </param>
        /// <param name="location">The declaration site every diagnostic raised here is reported at.</param>
        /// <param name="diagnostics">
        ///     Diagnostic sink. A construct no query provider can translate appends <c>DWARF028</c> with the
        ///     specific reason and returns <see langword="null" />; the caller then drops the whole method rather
        ///     than emit a projection missing a member.
        /// </param>
        /// <param name="targetMemberName">
        ///     Name of the TOP-LEVEL destination member this whole expression fills. Quoted by every
        ///     <c>DWARF028</c> raised beneath it, so a refusal deep inside a nested object still names the member
        ///     the reader actually wrote.
        /// </param>
        /// <param name="enumPolicy">
        ///     The mapper's <c>EnumStrategy</c>. <c>ByName</c> is refused here: it compiles to a switch, and a
        ///     switch is not something a query provider can translate.
        /// </param>
        /// <param name="comparer">
        ///     C4: the case-sensitivity comparer for member name matching, propagated from the top-level
        ///     call site so CaseInsensitive works at all nesting depths.
        /// </param>
        /// <param name="autoNest">
        ///     <c>[DwarfMapper(AutoNest = …)]</c>, per-method override included. When false an unrequested nested
        ///     pair is refused with the same <c>DWARF005</c> the runtime resolver raises, so turning auto-nesting
        ///     off means one thing at both endpoints.
        /// </param>
        /// <param name="nullAsNull">
        ///     The mapper's <c>NullCollections</c> setting. When true — and only where the target can actually
        ///     hold a null — a null source collection projects to <see langword="null" /> rather than to an empty
        ///     one; it degrades to the documented <c>AsEmpty</c> default where the target cannot.
        /// </param>
        /// <param name="implicitConversions">
        ///     <c>[DwarfMapper(ImplicitConversions = …)]</c>, forwarded to the <c>DWARF038</c> report for a
        ///     cross-category numeric pair: an Info suggestion when true, a build Error when false.
        /// </param>
        private static string? ResolveProjectionNestedObjectExpr(
            INamedTypeSymbol srcType,
            INamedTypeSymbol tgtType,
            string srcExpr,
            int depth,
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            string targetMemberName,
            EnumPolicy enumPolicy,
            StringComparer comparer,
            bool autoNest,
            bool nullAsNull,
            bool implicitConversions)
        {
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
                tgtType,
                srcType,
                writableTargetMembers.Count,
                compilation,
                location,
                diagnostics,
                null);
            if (!ctorDecided)
            {
                return null;
            }

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
                            DiagnosticDescriptors.UnmappedMember,
                            location,
                            targetMemberName + "." + tgtMember.Name));
                        failed = true;
                        continue;
                    }

                    var memberSrcExpr = srcExpr + "." + Identifiers.Escape(srcMember.Name);
                    // C4: propagate comparer into recursive member resolution.
                    var memberInlineExpr = ResolveProjectionExpr(
                        srcMember.Type,
                        tgtMember.Type,
                        memberSrcExpr,
                        depth + 1,
                        compilation,
                        location,
                        diagnostics,
                        targetMemberName + "." + tgtMember.Name,
                        enumPolicy,
                        comparer,
                        autoNest,
                        nullAsNull,
                        implicitConversions);

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
                    EmitDwarf028(diagnostics,
                        location,
                        targetMemberName,
                        $"nested type '{tgtFqn}' has no writable members and no usable constructor");
                    return null;
                }

                // C4: pass the configured comparer (not hardcoded Ordinal) so CaseInsensitive propagates.
                // ISSUE-043: autoNest was omitted here and defaulted back to `true`, so a mapper with
                // AutoNest = false still auto-nested through THIS path (nested object → ctor projection) while
                // every sibling path honoured the setting. The parameter is required now, so the omission
                // cannot come back.
                var ctorExpr = ResolveProjectionCtorExpr(
                    bestCtor,
                    srcType,
                    srcExpr,
                    depth,
                    compilation,
                    location,
                    diagnostics,
                    tgtType,
                    enumPolicy,
                    comparer,
                    autoNest,
                    nullAsNull,
                    implicitConversions);
                if (ctorExpr is null)
                {
                    return null;
                }

                // R18-32, nested half: a member the constructor did not take used to be dropped here in silence,
                // exactly as at the top level — `new InnerDto(__s.Inner.Start)` with InnerDto.Extra never
                // assigned, while .Map assigned it. It becomes an object initializer on the constructor call.
                var leftover = MemberParts(writableTargetMembers
                    .Where(m => !ConstructorFeedsMember(bestCtor, m.Name, comparer)));
                if (leftover is null)
                {
                    return null;
                }

                innerBodyExpr = leftover.Count == 0
                    ? ctorExpr
                    : $"{ctorExpr} {{ {string.Join(", ", leftover)} }}";
            }
            else
            {
                // Member-init expression: new T { P1 = expr1, P2 = expr2 }
                var memberParts = MemberParts(writableTargetMembers);
                if (memberParts is null)
                {
                    return null;
                }

                innerBodyExpr = $"new {tgtFqn} {{ {string.Join(", ", memberParts)} }}";
            }

            // Wrap with a null-navigation ternary ONLY when the source may actually be null (nullable-
            // annotated or nullable-oblivious). A non-nullable source needs no guard (guarding it would
            // assign null to a non-nullable target — CS8603).
            if (ProjectionSourceMayBeNull(srcType))
            {
                // ... and only when the TARGET slot can hold the null. A VALUE-type target cannot: the two arms
                // would be `null` and a struct, which has no best common type, and the generated file failed to
                // compile with CS0037 — silently, because the resolver reported nothing. The same
                // kind-instead-of-capability confusion I14/I7 name, one function further down. Found by the I14
                // sibling hunt (round 23), not by sampling: this cell is `class Src { Nested? N }` →
                // `class Dst { NestedStruct N }`, which the type-graph generators mirror kinds across and so
                // never build. The honest answer is the refusal a nullable-VALUE source into a null-incapable
                // target already gets: .Map answers it by throwing per NullStrategy from inside the synthesized
                // helper, and NullStrategy is precisely what a provider-translated expression cannot express.
                // A nullable-ANNOTATED REFERENCE target keeps the long-standing ternary — it can hold the null.
                if (tgtType.IsValueType)
                {
                    EmitDwarf028(diagnostics,
                        location,
                        targetMemberName,
                        $"a nullable source mapped to the value-type target '{tgtFqn}' needs a null decision, and " + "NullStrategy is not translatable in projection; make the target nullable, or map this " + "member at runtime");
                    return null;
                }

                return $"{srcExpr} == null ? null : {innerBodyExpr}";
            }

            return innerBodyExpr;
        }

        /// <summary>
        ///     Build an inline constructor-call expression for targets with only ctor params (records etc.).
        ///     e.g. "new global::D.DstRec(x: __s.X, y: __s.Y)"
        /// </summary>
        /// <param name="ctor">
        ///     The constructor to call. Already chosen by <c>ChooseProjectionConstructor</c>, so this function only
        ///     binds its parameters and renders the <c>new</c> expression.
        /// </param>
        /// <param name="srcType">The source object each constructor argument is read from.</param>
        /// <param name="srcExpr">
        ///     The C# expression that reads the source at this point — <c>__s.Order</c> at the top level, or a
        ///     <c>.Select</c> lambda parameter such as <c>__i0</c> one level in. Every deeper access is appended
        ///     to it, so the whole projection is one expression rooted here.
        /// </param>
        /// <param name="depth">
        ///     How many nesting levels down this call is. It bounds the recursion (<c>ProjectionMaxDepth</c>) and
        ///     names the element lambda parameter, so nested <c>.Select</c>s do not shadow one another.
        /// </param>
        /// <param name="compilation">
        ///     The compilation every type question is asked against — implicit conversions,
        ///     <c>IParsable</c>/<c>IFormattable</c>, member accessibility.
        /// </param>
        /// <param name="location">The declaration site every diagnostic raised here is reported at.</param>
        /// <param name="diagnostics">
        ///     Diagnostic sink. A construct no query provider can translate appends <c>DWARF028</c> with the
        ///     specific reason and returns <see langword="null" />; the caller then drops the whole method rather
        ///     than emit a projection missing a member.
        /// </param>
        /// <param name="tgtType">The type being constructed; its fully-qualified name is what <c>new</c> names.</param>
        /// <param name="enumPolicy">
        ///     The mapper's <c>EnumStrategy</c>. <c>ByName</c> is refused here: it compiles to a switch, and a
        ///     switch is not something a query provider can translate.
        /// </param>
        /// <param name="comparer">
        ///     The member-name comparer in force (<c>CaseInsensitive</c> / <c>NameConvention</c>), used to match a
        ///     constructor parameter to the source member that feeds it.
        /// </param>
        /// <param name="autoNest">
        ///     <c>[DwarfMapper(AutoNest = …)]</c>, per-method override included. When false an unrequested nested
        ///     pair is refused with the same <c>DWARF005</c> the runtime resolver raises, so turning auto-nesting
        ///     off means one thing at both endpoints.
        /// </param>
        /// <param name="nullAsNull">
        ///     The mapper's <c>NullCollections</c> setting. When true — and only where the target can actually
        ///     hold a null — a null source collection projects to <see langword="null" /> rather than to an empty
        ///     one; it degrades to the documented <c>AsEmpty</c> default where the target cannot.
        /// </param>
        /// <param name="implicitConversions">
        ///     <c>[DwarfMapper(ImplicitConversions = …)]</c>, forwarded to the <c>DWARF038</c> report for a
        ///     cross-category numeric pair: an Info suggestion when true, a build Error when false.
        /// </param>
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
            bool nullAsNull,
            bool implicitConversions,
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

                if (!TryBindProjectionCtorParam(param.Name,
                        srcReadable,
                        location,
                        diagnostics,
                        out var srcMember))
                {
                    anyFailed = true;
                    continue;
                }

                var paramSrcExpr = srcExpr + "." + Identifiers.Escape(srcMember.Name);
                // C4: propagate comparer into ctor param expression resolver.
                var paramInlineExpr = ResolveProjectionExpr(
                    srcMember.Type,
                    param.Type,
                    paramSrcExpr,
                    depth + 1,
                    compilation,
                    location,
                    diagnostics,
                    param.Name,
                    enumPolicy,
                    comparer,
                    autoNest,
                    nullAsNull,
                    implicitConversions);

                if (paramInlineExpr is null)
                {
                    anyFailed = true;
                    continue;
                }

                // Expression trees do not allow named arguments (CS0853): emit positional args.
                argParts.Add(paramInlineExpr);
            }

            if (anyFailed)
            {
                return null;
            }

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
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            out (string Name, ITypeSymbol Type) srcMember)
        {
            if (srcReadable.TryGetValue(paramName, out srcMember))
            {
                return true;
            }

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

        /// <summary>
        ///     Why the <c>[MapProperty]</c> modifier recorded for <paramref name="target" /> cannot be carried into a
        ///     projection, or <see langword="null" /> when the target has no modifier to refuse.
        /// </summary>
        /// <remarks>
        ///     <c>ReadMapPropertyExtras</c> records only a target that carries <c>NullSubstitute</c> or <c>When</c>, so an
        ///     entry with neither never reaches the projection through a mapper. Answering it here states that case once,
        ///     where a test can ask it, instead of leaving an exit no input takes at the call site. NullSubstitute is
        ///     named first when both are present, as the inline checks this replaced did.
        /// </remarks>
        internal static string? UntranslatableModifierReason(IReadOnlyDictionary<string, (bool HasNullSub, string? When)> extrasByTarget, string target)
        {
            if (!extrasByTarget.TryGetValue(target, out var extra))
            {
                return null;
            }

            if (extra.HasNullSub)
            {
                return "NullSubstitute is not translatable in projection (the substitution would be silently " + "dropped and a null stored instead); remove it or map this member at runtime";
            }

            return extra.When is not null
                ? "When= is not translatable in projection (the predicate cannot run inside an expression " + "tree, so the member would always be assigned); remove it or map this member at runtime"
                : null;
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
            if (!graph.TryGetValue(start, out var startDeps))
            {
                return false;
            }

            foreach (var dep in startDeps)
                stack.Push(dep);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (string.Equals(current, target, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!visited.Add(current))
                {
                    continue;
                }

                if (graph.TryGetValue(current, out var deps))
                {
                    foreach (var dep in deps)
                        stack.Push(dep);
                }
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
            if (t is INamedTypeSymbol n && n.TypeArguments.Length == 1 && KnownNames.IsNamespace(n.ContainingNamespace, "System") && (string.Equals(n.Name, "Span", StringComparison.Ordinal) || string.Equals(n.Name, "ReadOnlySpan", StringComparison.Ordinal)))
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
            if (t is INamedTypeSymbol n && n.TypeArguments.Length == 1 && string.Equals(n.Name, "IAsyncEnumerable", StringComparison.Ordinal) && n.ContainingNamespace is { Name: "Generic" } g && g.ContainingNamespace is { Name: "Collections" } c && c.ContainingNamespace is { Name: "System" } s && s.ContainingNamespace.IsGlobalNamespace)
            {
                element = n.TypeArguments[0];
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Emits DWARF038 for a non-lossless implicit basic-type conversion: an Info-level suggestion when
        ///     <c>ImplicitConversions</c> is true (permissive — the conversion is still applied), or a
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
            return t is INamedTypeSymbol { Name: "CancellationToken" } n && KnownNames.IsNamespace(n.ContainingNamespace, "System.Threading");
        }

        /// <summary>
        ///     Source-member lookup shared by all three projection resolvers.
        ///     <para>
        ///         Was <c>GroupBy(name, comparer).ToDictionary(g =&gt; g.Key, g =&gt; g.First())</c>, which under
        ///         <c>CaseInsensitive = true</c> SILENTLY first-picked one of two members differing only in case
        ///         (<c>Foo</c> / <c>foo</c> are distinct symbols and <c>ReadableMembers</c> de-duplicates by Ordinal, so
        ///         both reach the group). Three defects in one: it was silent (the library's "never silent" tenet), it
        ///         disagreed with the runtime map path — which reports <see cref="DiagnosticDescriptors.AmbiguousMatch" />
        ///         for the very same input, so the answer depended on whether you called <c>.Map</c> or the projection —
        ///         and the winner was whichever member <c>GetMembers()</c> yielded first, which for a partial source type
        ///         split across files is not stable, so two builds could emit different expression trees (H1).
        ///     </para>
        ///     Reports DWARF010 and binds NOTHING for an ambiguous group, exactly like the runtime path.
        /// </summary>
        /// <summary>
        ///     Name comparer for <c>NameConvention.Flexible</c>: two names are equal when their normalized
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
        internal sealed class FlexibleNameComparer : StringComparer
        {
            public static readonly FlexibleNameComparer Instance = new();

            public override int Compare(string? x, string? y)
            {
                return string.CompareOrdinal(x is null ? null : NormalizeName(x), y is null ? null : NormalizeName(y));
            }

            public override bool Equals(string? x, string? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x is null || y is null)
                {
                    return false;
                }

                return string.Equals(NormalizeName(x), NormalizeName(y), StringComparison.Ordinal);
            }

            public override int GetHashCode(string obj)
            {
                return Ordinal.GetHashCode(NormalizeName(obj));
            }
        }
    }
}
