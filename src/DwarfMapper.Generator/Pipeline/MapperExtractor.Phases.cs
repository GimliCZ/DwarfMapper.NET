// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Phases of <c>ExtractCore</c>, extracted one per commit under the byte-identity lock (round 27,
    ///     R27-03).
    ///     <para>
    ///         <c>ExtractCore</c> reached 3,574 lines and cyclomatic complexity 408 — it overflows any review
    ///         window, and every new option lands inside it. The seam comments it already carried are the cut
    ///         lines: a phase becomes a method and its seam comment becomes that method's documentation, so the
    ///         structure it always had in prose becomes structure the compiler can see.
    ///     </para>
    ///     <para>
    ///         Each extraction is proven byte-identical against the 973-case golden manifest, and the corpus was
    ///         first proven to REACH every phase (<c>scripts/seam-reach.ps1</c>, 26/26) — otherwise "the manifest
    ///         is unchanged" would only mean "nothing I tested noticed".
    ///     </para>
    /// </summary>
    internal static partial class MapperExtractor
    {
        // ── DWARF109: an [AfterMap] hook's "applies to this pair?" test is a BY-VALUE implicit-
        // conversion check (HasImplicitConversion(actualTarget, h.P0)) at every one of the five sites
        // that construct a HookCall. That is correct for an ordinary by-value hook parameter — but
        // once the hook takes its target BY REF, the true requirement is an IDENTITY match: C# has no
        // ref covariance, so `ref DerivedDto` does not bind to a `ref BaseDto` parameter even though
        // DerivedDto converts to BaseDto by value. Found via a [MapDerivedType] dispatch method whose
        // [AfterMap] hook declared the dispatch's own BASE return type: the hook matched the dispatch
        // method itself (its local IS exactly that base type) and ALSO matched a concrete arm's
        // declared pair (whose local is the derived type) — compiling clean for the former and CS1503
        // for the latter, in a .g.cs no consumer can edit. Shared here rather than reimplemented at
        // each site, so a sixth site added later inherits the check instead of being free to omit it.
        private static bool RefHookTargetMismatches(
            ITypeSymbol actualTarget,
            (string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind) h,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            if (h.TargetRefKind != RefKind.Ref)
            {
                return false;
            }

            // The TARGET parameter is h.P0 for the one-parameter form (Name(target)) and h.P1 for the
            // two-parameter form (Name(source, target)) — h.P0 is the SOURCE parameter there. Getting
            // this backwards (comparing against the source type) was the actual bug the first version
            // of this check shipped with: BlitSoundnessTests' two-parameter ref hook, whose target type
            // matched exactly, was rejected because its SOURCE type didn't.
            var hookTargetType = h.P1 ?? h.P0;
            if (SymbolEqualityComparer.Default.Equals(actualTarget, hookTargetType))
            {
                return false;
            }

            var hookName = h.Name;
            var hookParamType = hookTargetType;

            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.AfterMapRefTargetTypeMismatch,
                location,
                hookName,
                // ScopedToMethod: true — this describes a fact about the ONE method/pair being built at
                // this call site, not the mapper class. Skipping the incompatible hook still leaves that
                // method (and the class) emittable; MapperClassModel.HasBlockingError would otherwise
                // take the WHOLE class down for an unrelated hook mismatch on one pair (the exact I14
                // shape ProjectionNotTranslatable's own ScopedToMethod comment warns against).
                ScopedToMethod: true,
                MessageArg2: $"the hook declares 'ref {hookParamType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}', this pair's destination is '{actualTarget.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'"));
            return true;
        }

        // ── OnCycle = SetNull post-processing (None mode) ────────────────────────
        // After recursion-capability is finalised, flag every recursion-capable method so the
        // emitter wraps its body in the on-stack guard (TryEnterNode/ExitNode) and the public
        // entry allocates DwarfRefContext(maxDepth, setNull: true). Only reference-type pairs
        // can form a reference cycle, so value-type sources are left untouched (they keep the
        // plain depth-guarded None body — a struct cannot be its own ancestor on the stack).
        // This is the None-mode analogue of the Preserve post-pass above, but far simpler:
        // construction is unchanged (no register-before-populate, no DWARF030, no dispatch
        // wrapper) — the guard only nulls a re-entrant back-edge.
        private static void ApplySetNullPostPass(List<MapMethodModel> methods, bool isSetNullMode, List<DiagnosticInfo> diagnostics, LocationInfo? classLocation, INamedTypeSymbol classSymbol)
        {
            if (!isSetNullMode)
            {
                return;
            }

            // Same escape hatch DWARF076 already offers (MapperExtractor.Conversions.cs' HasSuppressMessage,
            // reused rather than reimplemented): #pragma cannot reach a generator-reported diagnostic — Roslyn
            // does not run these through pragma filtering — but [SuppressMessage] is an ordinary attribute the
            // generator itself can read off the class symbol, independent of that limitation. Computed once,
            // outside the loop: it does not vary per method.
            var setNullSuppressed = HasSuppressMessage(classSymbol, "DWARF108");

            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsRecursionCapable)
                {
                    continue; // only pairs that can re-enter
                }

                // Value types never form ref cycles — but the span / async-stream models record their
                // PARAMETER (a span struct / IAsyncEnumerable) here, not their ELEMENT, and it is the
                // element pair whose converter carries the on-stack guard. Without this exemption the
                // element-wise emitters allocated their shared DwarfRefContext without `setNull: true`,
                // so the guard the converter runs had no stack set behind it.
                if (!m.ParameterIsReferenceType && !m.IsSpanMap && !m.IsAsyncStreamMap)
                {
                    continue;
                }

                // ── DWARF108: the on-stack guard's back-edge is `return null!;`
                // (MapEmitter.EmitSetNullGuardedBody), which does not compile against a
                // non-nullable value-type destination (CS0037). Span / async-stream / update-into /
                // projection / derived-dispatch / top-level-collection models never reach that
                // emitter at all — EmitMethod returns out of each of them before the SetNull
                // section — so only the ordinary object-pair shape (a synthesized nested mapper or
                // a public entry) is actually at risk here.
                var reachesSetNullGuard = !m.IsSpanMap && !m.IsAsyncStreamMap && !m.IsUpdateInto &&
                                          !m.IsProjection && !m.IsTopLevelCollectionConversion &&
                                          m.DerivedTypeArms.Count == 0;
                if (reachesSetNullGuard && !m.ReturnIsReferenceType)
                {
                    // classLocation (the [DwarfMapper] class identifier), not null: this diagnostic is about
                    // the `OnCycle = SetNull` the user wrote, not about a synthesized helper method with no
                    // source position of its own — the right target on its own merits, independent of
                    // suppression mechanics.
                    if (!setNullSuppressed)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.OnCycleSetNullRequiresReferenceTarget,
                            classLocation,
                            m.MethodName,
                            MessageArg2: m.ReturnTypeFullName));
                    }

                    // Falls back to the plain depth-guarded body EVEN WHEN the diagnostic is suppressed: unlike
                    // DWARF076 (where suppression accepts the shallow copy as-is), the fallback here is a
                    // correctness requirement, not a stylistic one — the on-stack guard's `return null!;`
                    // does not compile against this destination (CS0037) regardless of whether the user
                    // wants to hear about it.
                    continue; // leave IsSetNullMode=false.
                }

                methods[i] = m with
                {
                    IsSetNullMode = true
                };
            }
        }

        // ── Plan 19 C2: Preserve mode post-processing ───────────────────────────
        // After recursion-capability is finalised, propagate IsPreserveMode and detect DWARF030.
        private static void ReportCyclicConstructorParameters(List<MapMethodModel> methods, Dictionary<string, HashSet<string>> allCallGraph, List<DiagnosticInfo> diagnostics, bool isPreserveMode, Dictionary<string, int> declaredNameCount)
        {
            if (isPreserveMode)
            {
                for (var i = 0; i < methods.Count; i++)
                {
                    var m = methods[i];

                    // ── DWARF030: detect cyclic constructor parameters ─────────────────
                    // Two patterns:
                    // (A) Explicit cycle: ctor arg has ConverterNeedsDepthCtx = true (calls recursion-capable
                    //     synthesized method). The back-edge is injected via ctor → can't register-before-populate.
                    // (B) Identity self-map cycle: S == T AND the method has ctor args that copy S? members
                    //     by identity (no converter). Example: record ImmutableNode(int V, ImmutableNode? Next)
                    //     mapped to itself — Next is copied as s.Next (source ref), not the target. The record
                    //     is immutable so we can't fix it up. Even though this method may not be "recursion-capable"
                    //     in the type-graph sense (S=T → implicit conversion → no synthesized method), the
                    //     DATA can still be cyclic and the ctor arg prevents register-before-populate.
                    if (m.ConstructorArguments.Count > 0)
                    {
                        // Pattern A: explicit recursion-capable ctor arg whose converter is on a
                        // call-graph cycle that includes the OUTER method. Under Preserve, ALL auto-nested
                        // object mappers are forced recursion-capable for uniform topology tracking, so
                        // ConverterNeedsDepthCtx=true alone is not sufficient — we must also verify that
                        // the converter can reach back to the outer method (i.e. they are on the SAME cycle),
                        // otherwise an acyclic nested mapper (e.g. Address→AddressDto) would be falsely
                        // flagged as cyclic just because it got forced-RC for Preserve threading.
                        // A scalar ctor param (int, string, Guid, enum) will have ConverterMethod=null and
                        // never reaches this branch.
                        var outerMethodKey = DeclKey(m, declaredNameCount);
                        foreach (var ctorArg in m.ConstructorArguments)
                            if (ctorArg.ConverterMethod is not null &&
                                ctorArg.ConverterNeedsDepthCtx &&
                                CanReach(allCallGraph,
                                    ctorArg.ConverterMethod,
                                    outerMethodKey))
                            {
                                var loc = (LocationInfo?)null;
                                diagnostics.Add(new DiagnosticInfo(
                                    DiagnosticDescriptors.CyclicConstructorParameter,
                                    loc,
                                    ctorArg.TargetName));
                            }

                        // Pattern B: self-map (S == T) with any ctor arg that has no converter.
                        // For S==T, direct-assignment ctor args copy the source reference into the target.
                        // If the source has a cycle (n.Next = n), the target's ctor arg will hold the source,
                        // not the target. Since the type is immutable (has ctor args), we can't fix this up.
                        // We only flag ctor args that are of reference type (not int/string/etc.) — but since
                        // we don't have type info here, we flag ALL ctor args when the method is S→S and
                        // recursion-capable (proven by members using ConverterNeedsDepthCtx).
                        // More precisely: the method must be recursion-capable to be affected.
                        if (m.IsRecursionCapable && string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
                        {
                            foreach (var ctorArg in m.ConstructorArguments)
                                // Only flag args with no converter (identity copy of potentially cyclic member).
                                // Args with a converter have already been checked above (Pattern A) or map scalars.
                                if (ctorArg.ConverterMethod is null && !ctorArg.ConverterNeedsDepthCtx)
                                {
                                    var loc = (LocationInfo?)null;
                                    diagnostics.Add(new DiagnosticInfo(
                                        DiagnosticDescriptors.CyclicConstructorParameter,
                                        loc,
                                        ctorArg.TargetName));
                                }
                        }
                    }

                    // Only recursion-capable methods need the Preserve-mode register-before-populate emission.
                    if (!m.IsRecursionCapable)
                    {
                        continue;
                    }

                    // Mark the method as Preserve mode.
                    methods[i] = m with
                    {
                        IsPreserveMode = true
                    };
                }

                // Pattern B (public declared methods): detect S==T self-recursive declared methods
                // where the target has ctor args. These are recursion-capable by definition.
                // The check above already covers it since we iterate ALL methods.
                // Additional check: for public partial methods that are Preserve+RecursionCapable,
                // check if the SOURCE type == RETURN type with ctor args — this covers user-declared
                // self-mappers like Map(ImmutableNode n) → ImmutableNode.
                // (This is already covered by the loop above for cases where m.IsRecursionCapable.)
                //
                // Special case: S==T where the method is NOT recursion-capable (pure identity copy,
                // no auto-nest synthesized method). This happens for record self-maps. We detect it
                // separately here because the isRecursionCapable gate filters them out above.
                for (var i = 0; i < methods.Count; i++)
                {
                    var m = methods[i];
                    if (m.ConstructorArguments.Count == 0)
                    {
                        continue;
                    }

                    if (m.IsRecursionCapable)
                    {
                        continue; // already handled above
                    }

                    if (!m.ParameterIsReferenceType)
                    {
                        continue; // value types excluded
                    }

                    // S == T (same type) with ctor args and NOT recursion-capable:
                    // This is the "record ImmutableNode(ImmutableNode? Next)" self-map case.
                    // The method isn't recursion-capable because S=T uses implicit conversion (no auto-nest),
                    // but at RUNTIME a cyclic ImmutableNode CAN exist. Under Preserve mode, this is
                    // an unsupported pattern → DWARF030 for the cyclic ctor args.
                    if (string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
                        // Flag ctor args that have the same type as the source (cyclic back-edge).
                        // Since we don't have type info here, flag ALL non-scalar ctor args where
                        // the source name suggests it's a complex member (has a converter or is the cycle).
                        // Conservative approach: flag all ctor args with no converter when S==T.
                        // The scalar ctor args (int, string, etc.) would also get flagged — this is
                        // acceptable since the real issue is that ANY ctor arg in this scenario is suspect
                        // (the ENTIRE pattern of immutable S=T mapping with cycles is broken).
                        // In practice, DWARF030 is a COMPILE ERROR — the user MUST fix the type design.
                    {
                        foreach (var ctorArg in m.ConstructorArguments)
                        {
                            var loc = (LocationInfo?)null;
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.CyclicConstructorParameter,
                                loc,
                                ctorArg.TargetName));
                        }
                    }
                }
            }

        }

        // ── MF-B fix: Preserve + [MapDerivedType] dispatch wrapper synthesis ─────────
        // Problem: a container mapper under Preserve has a member like First=Map(animal) where
        // Map(PsvAnimal) is a [MapDerivedType] dispatch method. Each call to the PUBLIC dispatch
        // creates a FRESH DwarfRefContext — so two members sharing the same source object land in
        // different identity maps and never deduplicate.
        //
        // Fix: synthesize a private ctx-accepting dispatch wrapper __DwarfMap_Disp_*(s, ctx, depth)
        // for every public dispatch method that is recursion-capable (arms use ctx) in Preserve mode.
        // The wrapper:
        //   1. Null-guards the source.
        //   2. TryGetReference — returns the cached target if the source was already mapped.
        //   3. Depth-guards against infinite dispatch chains.
        //   4. Dispatches via the same switch expression (forwarding ctx+depth to arm converters).
        //   5. SetReference — caches the result keyed by the BASE source reference.
        //
        // Then patch every member/ctor-arg (in both synthesized and public methods) that calls the
        // PUBLIC dispatch by name to instead call the wrapper (with ConverterNeedsDepthCtx=true).
        // Any public method with patched members is promoted to IsRecursionCapable+IsPreserveMode
        // so the emitter creates a shared DwarfRefContext and threads it through all members.
        private static void SynthesizePreserveDispatchWrappers(List<MapMethodModel> methods, HashSet<string> recursionCapableNames, Dictionary<string, SynthesizedMethod> synthesized, int maxDepth, bool isPreserveMode)
        {
            var dispatchWrapperByPublicName = new Dictionary<string, string>(
                StringComparer.Ordinal);
            if (isPreserveMode)
            {
                // Candidates first; a wrapper is synthesized only once a caller has actually been redirected to it.
                // A dispatch method on a recursion cycle already had every caller redirected to its depth companion
                // by MarkRecursionCapableCallers, so its wrapper would be a private method nothing calls — emitted
                // into the consumer's .g.cs all the same.
                var wrapperModels = new Dictionary<string, MapMethodModel>(StringComparer.Ordinal);
                for (var i = 0; i < methods.Count; i++)
                {
                    var m = methods[i];
                    if (m.DerivedTypeArms.Count == 0)
                    {
                        continue; // not a dispatch method
                    }

                    if (!m.IsRecursionCapable)
                    {
                        continue; // arm converters don't need ctx
                    }

                    if (!m.IsPartial)
                    {
                        continue; // only public declared dispatch methods
                    }

                    var wrapperName = NestedMappingRegistry.BuildDispatchWrapperName(
                        m.ParameterTypeFullName,
                        m.ReturnTypeFullName);

                    wrapperModels[wrapperName] = m;
                    dispatchWrapperByPublicName[m.MethodName] = wrapperName;
                }

                // Patch: redirect members/ctor-args that call a public dispatch method to the wrapper.
                if (dispatchWrapperByPublicName.Count > 0)
                {
                    var usedWrappers = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < methods.Count; i++)
                    {
                        var m = methods[i];
                        if (m.DerivedTypeArms.Count > 0)
                        {
                            continue; // skip the dispatch methods themselves
                        }

                        var patched = false;
                        var newMembers = m.Members.ToArray();
                        for (var mi = 0; mi < newMembers.Length; mi++)
                        {
                            var mem = newMembers[mi];
                            if (mem.ConverterMethod is null || mem.ConverterNeedsDepthCtx)
                            {
                                continue;
                            }

                            if (dispatchWrapperByPublicName.TryGetValue(mem.ConverterMethod, out var wn))
                            {
                                newMembers[mi] = mem with
                                {
                                    ConverterMethod = wn,
                                    ConverterNeedsDepthCtx = true
                                };
                                usedWrappers.Add(wn);
                                patched = true;
                            }
                        }

                        var newCtorArgs = m.ConstructorArguments.ToArray();
                        for (var ci = 0; ci < newCtorArgs.Length; ci++)
                        {
                            var arg = newCtorArgs[ci];
                            if (arg.ConverterMethod is null || arg.ConverterNeedsDepthCtx)
                            {
                                continue;
                            }

                            if (dispatchWrapperByPublicName.TryGetValue(arg.ConverterMethod, out var wn))
                            {
                                newCtorArgs[ci] = arg with
                                {
                                    ConverterMethod = wn,
                                    ConverterNeedsDepthCtx = true
                                };
                                usedWrappers.Add(wn);
                                patched = true;
                            }
                        }

                        if (patched)
                        {
                            // Public methods patched to use ctx-accepting wrappers must create a shared
                            // DwarfRefContext. Promote to IsRecursionCapable+IsPreserveMode so the emitter
                            // generates: var __dwarf_ctx = new DwarfRefContext(maxDepth, true); and threads
                            // ctx into all wrapper calls via the register-before-populate path.
                            var newRc = m.IsPartial ? true : m.IsRecursionCapable;
                            methods[i] = m with
                            {
                                Members = EquatableArray.From(newMembers),
                                ConstructorArguments = EquatableArray.From(newCtorArgs),
                                IsRecursionCapable = newRc,
                                MaxDepth = m.IsPartial && !m.IsRecursionCapable ? maxDepth : m.MaxDepth,
                                IsPreserveMode = m.IsPartial ? true : m.IsPreserveMode
                            };
                        }
                    }

                    // In candidate order, so the synthesized table fills in the order it always did.
                    foreach (var candidate in wrapperModels)
                    {
                        if (!usedWrappers.Contains(candidate.Key))
                        {
                            continue;
                        }

                        if (!synthesized.ContainsKey(candidate.Key))
                        {
                            synthesized[candidate.Key] = new SynthesizedMethod(candidate.Key, BuildDispatchWrapperCode(candidate.Value, candidate.Key));
                        }

                        recursionCapableNames.Add(candidate.Key);
                    }
                }
            }
            // ── End MF-B fix ─────────────────────────────────────────────────────────

        }

        // ── MF-A fix: [MapDerivedType] dispatch method arm ctx threading ────────────
        // Now that recursion-capability is fully resolved, patch any dispatch method
        // (DerivedTypeArms.Count > 0) whose arm converters are recursion-capable (i.e.
        // need ctx+depth forwarding).  This includes Preserve-mode auto-nested pairs
        // (__DwarfMap_Obj_*) which were force-marked recursion-capable in the block above.
        private static void ThreadContextThroughDispatchArms(List<MapMethodModel> methods, HashSet<string> recursionCapableNames, HashSet<string> selfRecursivePublicMethods, int maxDepth)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.DerivedTypeArms.Count == 0)
                {
                    continue; // not a dispatch method
                }

                var patchedArms = m.DerivedTypeArms.ToArray();
                var anyArmNeedsCtx = false;
                for (var ai = 0; ai < patchedArms.Length; ai++)
                {
                    var arm = patchedArms[ai];
                    if (arm.ConverterNeedsDepthCtx)
                    {
                        // Already marked (e.g. captured from TryResolveConversion or previously patched).
                        anyArmNeedsCtx = true;
                    }
                    else if (recursionCapableNames.Contains(arm.ConverterMethod))
                    {
                        patchedArms[ai] = arm with
                        {
                            ConverterNeedsDepthCtx = true
                        };
                        anyArmNeedsCtx = true;
                    }
                    else if (selfRecursivePublicMethods.Contains(arm.ConverterMethod))
                    {
                        // Declared public method on a recursion cycle — redirect to its companion.
                        var companionName = GeneratedNames.Depth + arm.ConverterMethod;
                        patchedArms[ai] = arm with
                        {
                            ConverterMethod = companionName,
                            ConverterNeedsDepthCtx = true
                        };
                        anyArmNeedsCtx = true;
                    }
                }

                if (anyArmNeedsCtx)
                {
                    methods[i] = m with
                    {
                        IsRecursionCapable = true,
                        MaxDepth = m.IsPartial ? maxDepth : m.MaxDepth,
                        DerivedTypeArms = EquatableArray.From(patchedArms)
                    };
                }
            }
            // ── End MF-A fix ─────────────────────────────────────────────────────────

        }

        // ── Preserve-mode second pass: propagate ctx to public methods whose synthesized callees
        // became recursion-capable during the loop above but were visited BEFORE their callee.
        // This fixes the ordering problem: public Map(SharingRoot) is added to methods[] before
        // the synthesized __DwarfMap_Obj_...Holder... pair, so the first loop processes the
        // public method before knowing the Holder mapper needs ctx. We now re-check all public
        // declared methods under Preserve mode and patch any member/ctor-arg that calls a
        // newly-added recursionCapableNames entry without ConverterNeedsDepthCtx=true.
        internal static void PropagateContextToPublicMethodsSecondPass(List<MapMethodModel> methods, HashSet<string> recursionCapableNames, HashSet<string> selfRecursivePublicMethods, int maxDepth, bool isPreserveMode)
        {
            if (isPreserveMode)
            {
                for (var i = 0; i < methods.Count; i++)
                {
                    var m = methods[i];
                    if (!m.IsPartial)
                    {
                        continue; // only public declared methods
                    }

                    if (selfRecursivePublicMethods.Contains(m.MethodName))
                    {
                        continue; // already handled above
                    }

                    var patched = false;
                    var newMembers2 = m.Members.ToArray();
                    for (var mi = 0; mi < newMembers2.Length; mi++)
                    {
                        var mem = newMembers2[mi];
                        if (mem.ConverterMethod is null || mem.ConverterNeedsDepthCtx)
                        {
                            continue;
                        }

                        if (recursionCapableNames.Contains(mem.ConverterMethod))
                        {
                            newMembers2[mi] = mem with
                            {
                                ConverterNeedsDepthCtx = true
                            };
                            patched = true;
                        }
                    }

                    var newCtorArgs2 = m.ConstructorArguments.ToArray();
                    for (var ci = 0; ci < newCtorArgs2.Length; ci++)
                    {
                        var arg = newCtorArgs2[ci];
                        if (arg.ConverterMethod is null || arg.ConverterNeedsDepthCtx)
                        {
                            continue;
                        }

                        if (recursionCapableNames.Contains(arg.ConverterMethod))
                        {
                            newCtorArgs2[ci] = arg with
                            {
                                ConverterNeedsDepthCtx = true
                            };
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        methods[i] = m with
                        {
                            IsRecursionCapable = true,
                            MaxDepth = maxDepth,
                            Members = EquatableArray.From(newMembers2),
                            ConstructorArguments = EquatableArray.From(newCtorArgs2)
                        };
                    }
                }
            }

        }

        // ── Mark public methods and synthesized methods that call recursion-capable pairs ─
        // The public Map(S s) method needs to create a DwarfRefContext if it calls (directly
        // or indirectly through its members) a recursion-capable synthesized pair.
        // We patch the already-added method models here.
        private static void MarkRecursionCapableCallers(List<MapMethodModel> methods, HashSet<string> recursionCapableNames, HashSet<string> selfRecursivePublicMethods, int maxDepth)
        {
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];

                // For self-recursive declared methods: generate a companion and redirect.
                if (m.IsPartial && selfRecursivePublicMethods.Contains(m.MethodName))
                {
                    var companionName = GeneratedNames.Depth + m.MethodName;

                    // Patch the members/ctor-args of the declared method:
                    // (a) self-calls → redirect to companion with depth ctx
                    // (b) calls to other self-recursive declared methods → redirect to their companions
                    // (c) calls to recursion-capable synthesized methods → add depth ctx
                    // (d) already-set ConverterNeedsDepthCtx (e.g. Preserve-mode collection helpers) → keep as-is
                    var newMembers2 = m.Members.ToArray();
                    for (var mi = 0; mi < newMembers2.Length; mi++)
                    {
                        var mem = newMembers2[mi];
                        if (mem.ConverterMethod is null)
                        {
                            continue;
                        }

                        if (mem.ConverterNeedsDepthCtx)
                        {
                            // Already marked (Preserve collection helper or previously patched).
                        }
                        else if (string.Equals(mem.ConverterMethod, m.MethodName, StringComparison.Ordinal))
                        {
                            // Self-call: redirect to companion.
                            newMembers2[mi] = mem with
                            {
                                ConverterMethod = companionName,
                                ConverterNeedsDepthCtx = true
                            };
                        }
                        else if (selfRecursivePublicMethods.Contains(mem.ConverterMethod))
                        {
                            // Indirect recursive declared method: redirect to its companion.
                            newMembers2[mi] = mem with
                            {
                                ConverterMethod = GeneratedNames.Depth + mem.ConverterMethod,
                                ConverterNeedsDepthCtx = true
                            };
                        }
                        else if (recursionCapableNames.Contains(mem.ConverterMethod))
                        {
                            // Recursion-capable synthesized method: pass depth ctx.
                            newMembers2[mi] = mem with
                            {
                                ConverterNeedsDepthCtx = true
                            };
                        }
                    }

                    var newCtorArgs2 = m.ConstructorArguments.ToArray();
                    for (var ci = 0; ci < newCtorArgs2.Length; ci++)
                    {
                        var arg = newCtorArgs2[ci];
                        if (arg.ConverterMethod is null)
                        {
                            continue;
                        }

                        if (arg.ConverterNeedsDepthCtx)
                        {
                            // Already marked (Preserve collection helper or previously patched).
                        }
                        else if (string.Equals(arg.ConverterMethod, m.MethodName, StringComparison.Ordinal))
                        {
                            newCtorArgs2[ci] = arg with
                            {
                                ConverterMethod = companionName,
                                ConverterNeedsDepthCtx = true
                            };
                        }
                        else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                        {
                            newCtorArgs2[ci] = arg with
                            {
                                ConverterMethod = GeneratedNames.Depth + arg.ConverterMethod,
                                ConverterNeedsDepthCtx = true
                            };
                        }
                        else if (recursionCapableNames.Contains(arg.ConverterMethod))
                        {
                            newCtorArgs2[ci] = arg with
                            {
                                ConverterNeedsDepthCtx = true
                            };
                        }
                    }

                    // Mark public method as needing ctx creation.
                    methods[i] = m with
                    {
                        IsRecursionCapable = true,
                        MaxDepth = maxDepth,
                        Members = EquatableArray.From(newMembers2),
                        ConstructorArguments = EquatableArray.From(newCtorArgs2)
                    };

                    // Synthesize the companion: same body, but IsRecursionCapable=true, IsPartial=false.
                    var companion = m with
                    {
                        MethodName = companionName,
                        Accessibility = "private",
                        IsPartial = false,
                        IsRecursionCapable = true,
                        MaxDepth = maxDepth,
                        Members = EquatableArray.From(newMembers2),
                        ConstructorArguments = EquatableArray.From(newCtorArgs2)
                        // Companion's self-calls also use the companion (already patched above).
                    };
                    methods.Add(companion);
                    continue;
                }

                // Check if any of this method's members/ctor-args uses a recursion-capable synthesized method
                // OR a self-recursive declared method (which must be redirected to the companion)
                // OR a Preserve-mode collection helper that already has ConverterNeedsDepthCtx=true.
                var needsCtx = false;
                var newMembers = m.Members.ToArray();
                for (var mi = 0; mi < newMembers.Length; mi++)
                {
                    var member = newMembers[mi];
                    if (member.ConverterMethod is null)
                    {
                        continue;
                    }

                    if (member.ConverterNeedsDepthCtx)
                    {
                        // Already marked (e.g. Preserve-mode collection helper set by ResolveMembers).
                        needsCtx = true;
                    }
                    else if (recursionCapableNames.Contains(member.ConverterMethod))
                    {
                        newMembers[mi] = member with
                        {
                            ConverterNeedsDepthCtx = true
                        };
                        needsCtx = true;
                    }
                    else if (selfRecursivePublicMethods.Contains(member.ConverterMethod))
                    {
                        // Redirect call from declared public method to its depth-guarded companion.
                        var companionName = GeneratedNames.Depth + member.ConverterMethod;
                        newMembers[mi] = member with
                        {
                            ConverterMethod = companionName,
                            ConverterNeedsDepthCtx = true
                        };
                        needsCtx = true;
                    }
                }

                var newCtorArgs = m.ConstructorArguments.ToArray();
                for (var ci = 0; ci < newCtorArgs.Length; ci++)
                {
                    var arg = newCtorArgs[ci];
                    if (arg.ConverterMethod is null)
                    {
                        continue;
                    }

                    if (arg.ConverterNeedsDepthCtx)
                    {
                        // Already marked (e.g. Preserve-mode collection helper set by ResolveConstructorArguments).
                        needsCtx = true;
                    }
                    else if (recursionCapableNames.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs[ci] = arg with
                        {
                            ConverterNeedsDepthCtx = true
                        };
                        needsCtx = true;
                    }
                    else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                    {
                        var companionName = GeneratedNames.Depth + arg.ConverterMethod;
                        newCtorArgs[ci] = arg with
                        {
                            ConverterMethod = companionName,
                            ConverterNeedsDepthCtx = true
                        };
                        needsCtx = true;
                    }
                }

                if (needsCtx || (m.IsRecursionCapable && !m.IsPartial))
                {
                    var newRc = m.IsRecursionCapable || needsCtx;
                    methods[i] = m with
                    {
                        IsRecursionCapable = newRc,
                        MaxDepth = m.IsPartial ? maxDepth : m.MaxDepth, // only public methods carry MaxDepth
                        Members = EquatableArray.From(newMembers),
                        ConstructorArguments = EquatableArray.From(newCtorArgs)
                    };
                    // ── Preserve-mode propagation fix ──────────────────────────────────
                    // When a synthesized (non-partial) method is newly marked recursion-capable
                    // (because its own members need ctx, e.g. a Preserve-mode List<T> helper),
                    // record it in recursionCapableNames immediately so that public declared
                    // methods processed LATER in this same loop can see it.
                    // This handles the case where public methods come BEFORE their synthesized
                    // callees in the methods list (declaration order: public first, synth second).
                    // A second pass below handles the reverse order (synth processed first but
                    // public was already visited).
                    if (!m.IsPartial && newRc)
                    {
                        recursionCapableNames.Add(m.MethodName);
                    }
                }
            }

        }

        // ── None+Throw: upgrade collection/dict helpers whose element method is self-recursive ──
        // Now that selfRecursivePublicMethods is known, re-synthesize any recorded None-mode helper
        // whose element/key/value resolved to a now-self-recursive public method: route it through the
        // depth-guarded `__DwarfMap_Depth_<method>` companion and thread (ctx, depth). Adding the helper
        // name to recursionCapableNames makes every referencing member/method pick up ctx threading via
        // the existing patching loops below — so a deep/cyclic graph through a collection edge throws
        // DwarfMappingDepthException instead of a silent StackOverflow. Non-recursive collections are
        // never recorded, so they stay zero-overhead.
        private static void UpgradeElementHelpersOnRecursionCycle(List<MapMethodModel> methods, NestedMappingRegistry nestedRegistry, HashSet<string> recursionCapableNames, HashSet<string> selfRecursivePublicMethods, HashSet<string> nodesOnCycle, Dictionary<string, int> declaredNameCount)
        {
            foreach (var cand in nestedRegistry.CtxUpgradeCandidates)
            {
                // Two kinds of element can turn out recursion-capable, and they upgrade DIFFERENTLY:
                //
                //  * a synthesized object-map helper (`__DwarfMap_Obj_…`, what a [GenerateMap<S,T>] pair yields)
                //    gains the (ctx, depth) parameters IN PLACE — it keeps its own name. The collection helper
                //    just has to be re-emitted so it calls it with (elem, ctx, depth + 1).
                //  * a PUBLIC declared method cannot change signature (it is the user's API), so it is routed
                //    through its depth-guarded `__DwarfMap_Depth_<method>` companion instead. Must also be
                //    non-overloaded: an overloaded name can't be safely disambiguated to a single companion.
                bool UpgradeableSynth(string? em)
                {
                    return em is not null && GeneratedNames.IsObjectMap(em) && nestedRegistry.IsRecursionCapable(em);
                }

                bool UpgradeablePublic(string? em)
                {
                    return em is not null && selfRecursivePublicMethods.Contains(em) && !(declaredNameCount.TryGetValue(em, out var oc) && oc > 1);
                }

                bool Upgradeable(string? em)
                {
                    return UpgradeableSynth(em) || UpgradeablePublic(em);
                }

                if (!cand.ElemMethods.Any(Upgradeable))
                {
                    continue;
                }

                // Synthesized helper → same name (now 3-param). Public method → its Depth companion.
                cand.ReSynth(name => UpgradeableSynth(name)
                    ? name
                    : UpgradeablePublic(name)
                        ? GeneratedNames.Depth + name
                        : name);
                recursionCapableNames.Add(cand.HelperName);
            }

            // Re-check synthesized methods using the full allCallGraph (which includes declared methods).
            // The registry's edge graph only tracks synthesized→synthesized edges; it misses cycles that
            // go through declared methods (e.g. __DwarfMap_Obj_B → Map → __DwarfMap_Obj_B).
            // Any synthesized method on such a mixed cycle must also be recursion-capable.
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (m.IsPartial)
                {
                    continue; // only synthesized methods
                }

                if (!recursionCapableNames.Contains(m.MethodName) && nodesOnCycle.Contains(m.MethodName))
                {
                    recursionCapableNames.Add(m.MethodName);
                    // Re-mark the method model as recursion-capable.
                    methods[i] = m with
                    {
                        IsRecursionCapable = true
                    };
                }
            }

        }

        // ── Also detect declared public methods on a recursion cycle ────────────
        // Two cases:
        //   Direct: Map(Node n) has member.ConverterMethod == "Map" (self-call).
        //   Indirect: Map(A) calls __DwarfMap_Obj_B which calls Map (mutual cycle).
        //
        // We extend the call graph to include declared methods and run reachability.
        // All_methods_graph: maps methodKey → set of methods it calls.
        //
        // KEY DISAMBIGUATION: Overloaded declared methods (e.g. ToDto(Person) and ToDto(Addr))
        // share the same base name but must be tracked separately. We use:
        //   Single overload  → key = MethodName  (e.g. "Map")
        //   Multiple overloads → key = MethodName + "§" + ParameterTypeFullName
        //     (e.g. "ToDto§global::Demo.Person", "ToDto§global::Demo.Addr")
        //
        // Synthesized methods always use their unique auto-generated name as key.
        // When a synthesized method has a converter referencing a SINGLE-overload declared method,
        // the edge target is just the method name. Callers targeting an overloaded name may
        // traverse all overloads (conservative: at least one variant is reachable).
        /// <remarks>
        ///     <paramref name="declaredNameCount" />, <paramref name="nodesOnCycle" /> and
        ///     <paramref name="selfRecursivePublicMethods" /> are OWNED BY THE CALLER even though this phase is
        ///     what fills them: later phases read all three. They were locals here until the extraction made
        ///     the dependency visible — which is the point of cutting the method up, and the reason the
        ///     compiler caught it rather than a reviewer having to.
        /// </remarks>
        private static void DetectDeclaredMethodsOnRecursionCycle(List<MapMethodModel> methods, NestedMappingRegistry nestedRegistry, List<(MapMethodModel Model, string MethodName)> pendingNestedModels, HashSet<string> recursionCapableNames, Dictionary<string, int> declaredNameCount, HashSet<string> nodesOnCycle, HashSet<string> selfRecursivePublicMethods, Dictionary<string, HashSet<string>> allCallGraph)
        {

            // Count declared methods per name to detect overloads.
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsPartial)
                {
                    continue;
                }

                declaredNameCount.TryGetValue(m.MethodName, out var prev);
                declaredNameCount[m.MethodName] = prev + 1;
            }

            // Helper: get the graph key for a declared method.
            // Seed with synthesized method edges (already computed in registry, but not accessible here).
            // Re-derive from pending models (synthesized methods always have unique names).
            foreach (var (model, _) in pendingNestedModels)
            {
                var callerName = model.MethodName;
                if (!allCallGraph.ContainsKey(callerName))
                {
                    allCallGraph[callerName] = new HashSet<string>(StringComparer.Ordinal);
                }

                foreach (var mem in model.Members)
                    if (mem.ConverterMethod is not null)
                    {
                        allCallGraph[callerName].Add(ExactOverloadKey(mem.ConverterMethod, mem.ConverterParamTypeFqn, declaredNameCount) ?? mem.ConverterMethod);
                    }

                foreach (var arg in model.ConstructorArguments)
                    if (arg.ConverterMethod is not null)
                    {
                        allCallGraph[callerName].Add(ExactOverloadKey(arg.ConverterMethod, arg.ConverterParamTypeFqn, declaredNameCount) ?? arg.ConverterMethod);
                    }
            }

            // Add declared methods to the graph using disambiguation keys.
            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsPartial)
                {
                    continue;
                }

                var callerKey = DeclKey(m, declaredNameCount);
                if (!allCallGraph.TryGetValue(callerKey, out var callerEdges))
                {
                    callerEdges = new HashSet<string>(StringComparer.Ordinal);
                    allCallGraph[callerKey] = callerEdges;
                }

                foreach (var mem in m.Members)
                {
                    if (mem.ConverterMethod is null)
                    {
                        continue;
                    }

                    // Resolve the edge target: if the converter is an overloaded declared method,
                    // we can't determine which overload without param-type info, so we add edges
                    // to ALL overloads of that name.  For non-overloaded names and synthesized
                    // names, add the name directly.
                    if (ExactOverloadKey(mem.ConverterMethod, mem.ConverterParamTypeFqn, declaredNameCount) is { } memKey)
                    {
                        allCallGraph[callerKey].Add(memKey);
                    }
                    else if (declaredNameCount.TryGetValue(mem.ConverterMethod, out var oc) && oc > 1)
                        // Add edges to all OTHER overloads (not the method itself — a converter can't be
                        // a self-call when it was auto-matched to a DIFFERENT overload by parameter type).
                    {
                        for (var j = 0; j < methods.Count; j++)
                        {
                            var ov = methods[j];
                            if (!ov.IsPartial)
                            {
                                continue;
                            }

                            if (!string.Equals(ov.MethodName, mem.ConverterMethod, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var ovKey = DeclKey(ov, declaredNameCount);
                            if (!string.Equals(ovKey, callerKey, StringComparison.Ordinal))
                            {
                                allCallGraph[callerKey].Add(ovKey);
                            }
                        }
                    }
                    else
                    {
                        allCallGraph[callerKey].Add(mem.ConverterMethod);
                    }
                }

                foreach (var arg in m.ConstructorArguments)
                {
                    if (arg.ConverterMethod is null)
                    {
                        continue;
                    }

                    if (ExactOverloadKey(arg.ConverterMethod, arg.ConverterParamTypeFqn, declaredNameCount) is { } argKey)
                    {
                        allCallGraph[callerKey].Add(argKey);
                    }
                    else if (declaredNameCount.TryGetValue(arg.ConverterMethod, out var oc) && oc > 1)
                    {
                        for (var j = 0; j < methods.Count; j++)
                        {
                            var ov = methods[j];
                            if (!ov.IsPartial)
                            {
                                continue;
                            }

                            if (!string.Equals(ov.MethodName, arg.ConverterMethod, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var ovKey = DeclKey(ov, declaredNameCount);
                            if (!string.Equals(ovKey, callerKey, StringComparison.Ordinal))
                            {
                                allCallGraph[callerKey].Add(ovKey);
                            }
                        }
                    }
                    else
                    {
                        allCallGraph[callerKey].Add(arg.ConverterMethod);
                    }
                }

                // A [MapDerivedType] dispatch method has no members: its calls are its ARMS. Without these edges
                // `Map(Animal)` → arm → `Friend` → `Map(Animal)` was not a cycle in this graph at all, and was only
                // ever flagged when an overloaded bare name happened to fan out into one elsewhere. A bare overloaded
                // arm name is expanded below like any other edge.
                foreach (var armEdge in m.DerivedTypeArms)
                {
                    allCallGraph[callerKey].Add(ExactOverloadKey(armEdge.ConverterMethod, armEdge.ConverterParamTypeFqn, declaredNameCount) ?? armEdge.ConverterMethod);
                }
            }

            // Inject helper → element-method edges for None-mode ctx-upgrade candidates. A collection/dict
            // helper's internal call to its element method is invisible to this graph (helpers aren't method
            // models), so a cycle routed ONLY through a collection/dict edge (Map → helper → Map) would go
            // undetected and the element method would never get a depth companion. Adding the edge makes the
            // cycle visible → the element method is flagged self-recursive → companion synthesized → the
            // re-synthesis pass below upgrades the helper to depth-guarded ctx threading (no silent SO).
            foreach (var cand in nestedRegistry.CtxUpgradeCandidates)
            {
                if (!allCallGraph.ContainsKey(cand.HelperName))
                {
                    allCallGraph[cand.HelperName] = new HashSet<string>(StringComparer.Ordinal);
                }

                foreach (var em in cand.ElemMethods)
                    // Only inject NON-overloaded element methods: a raw overloaded name would be expanded
                    // to edges for ALL overloads, manufacturing a false self-cycle (e.g. Map(Person) →
                    // List<Addr> helper → Map(Addr) wrongly resolving to Map(Person)). Overloaded
                    // self-map-through-collection falls back to the documented None-mode behaviour.
                    if (!(declaredNameCount.TryGetValue(em, out var oc) && oc > 1))
                    {
                        allCallGraph[cand.HelperName].Add(em);
                    }
            }

            // For synthesized methods calling overloaded declared methods, also expand edges
            // so DFS can follow the full cycle. If synth-method calls "Map" and there are
            // two overloads "Map§A" and "Map§B", add edges to all variants except self.
            foreach (var callerKey in allCallGraph.Keys.ToList())
            {
                var edges = allCallGraph[callerKey];
                var expandedEdges = new List<string>();
                foreach (var edge in edges)
                    if (declaredNameCount.TryGetValue(edge, out var oc) && oc > 1)
                        // Replace simple name with qualified variants (excluding self to avoid false cycles).
                    {
                        for (var j = 0; j < methods.Count; j++)
                        {
                            var ov = methods[j];
                            if (!ov.IsPartial)
                            {
                                continue;
                            }

                            if (!string.Equals(ov.MethodName, edge, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var ovKey = DeclKey(ov, declaredNameCount);
                            if (!string.Equals(ovKey, callerKey, StringComparison.Ordinal))
                            {
                                expandedEdges.Add(ovKey);
                            }
                        }
                    }
                    else
                    {
                        expandedEdges.Add(edge);
                    }

                edges.Clear();
                foreach (var e in expandedEdges) edges.Add(e);
            }

            // Find which declared methods are on a cycle (can reach themselves in allCallGraph).
            // ISSUE-023: one Tarjan SCC pass answers "is this node on a cycle?" for EVERY node, replacing a
            // per-node DFS here and again in the synthesized-method re-check below. allCallGraph is complete at
            // this point — the edge-expansion loop above is its last mutation — so a single pass stays valid for
            // both. The general reachability query further down (converter → outer method, a DIFFERENT question)
            // keeps its DFS: SCC membership cannot answer reachability between distinct nodes.
            //
            // Fills the caller-owned set rather than declaring a local, because three later phases read it.
            nodesOnCycle.UnionWith(StronglyConnected.NodesOnACycle(allCallGraph));

            for (var i = 0; i < methods.Count; i++)
            {
                var m = methods[i];
                if (!m.IsPartial)
                {
                    continue;
                }

                var key = DeclKey(m, declaredNameCount);
                if (nodesOnCycle.Contains(key))
                {
                    selfRecursivePublicMethods.Add(m.MethodName);
                    // Add the companion name so call-sites in synthesized methods can reference it.
                    var companionName = GeneratedNames.Depth + m.MethodName;
                    recursionCapableNames.Add(companionName);
                }
            }

        }

        // ── DWARF060: same-source / multiple-target signature collision ──────────
        // Two public create-style maps that would emit an identical (name, parameter-type) signature but
        // with different return (target) types overload only by return type — illegal C# (CS0111). The
        // consumer would otherwise see a raw CS0111 inside generated code. Detect, report loudly, and drop
        // the duplicate emission so DWARF060 is the single actionable diagnostic (the build still fails).
        private static void ReportSameSourceSignatureCollisions(List<MapMethodModel> methods, Dictionary<int, LocationInfo?> publicMethodLocs, List<DiagnosticInfo> diagnostics)
        {
            {
                var sigOwner = new Dictionary<string, int>(StringComparer.Ordinal);
                var collisionDrop = new List<int>();
                for (var i = 0; i < methods.Count; i++)
                {
                    var m = methods[i];
                    // Only public, create-style methods share the `T Map(S)` shape. Update-into / span /
                    // async-stream have distinct parameter lists, so a shared source never collides there.
                    if (!(m.IsPartial || m.EmitAsNonPartial))
                    {
                        continue;
                    }

                    if (m.IsUpdateInto || m.IsSpanMap || m.IsAsyncStreamMap)
                    {
                        continue;
                    }

                    var sig = m.MethodName + "(" + m.ParameterTypeFullName + "|" + string.Join(",", m.ExtraParameters) + ")";
                    if (!sigOwner.TryGetValue(sig, out var ownerIdx))
                    {
                        sigOwner[sig] = i;
                        continue;
                    }

                    var owner = methods[ownerIdx];
                    // Identical (name, params, return): both would be EMITTED, so this is CS0111 in the
                    // generated file plus a CS0121 ambiguity cascade in the caller's — the B27 shape. Only
                    // possible when at least one side is [GenerateMap]-synthesized (wrapper-expanded pairs
                    // included, since ExpandWrapperMaps appends to the same pair list): two identical
                    // user-declared partial DEFINITIONS are the compiler's own error in the caller's file,
                    // and not the generator's collision to announce.
                    if (string.Equals(owner.ReturnTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
                    {
                        if (!owner.EmitAsNonPartial && !m.EmitAsNonPartial)
                        {
                            continue;
                        }

                        var dupLoc = publicMethodLocs.TryGetValue(i, out var dl) ? dl
                            : publicMethodLocs.TryGetValue(ownerIdx, out var dl2) ? dl2 : null;
                        // Which of the two shapes it met decides the remedy sentence. Refused rather than
                        // deduplicated (DWARF087's reasoning): keeping one silently would hide the mistake,
                        // and a co-located host's member directives bind to declared pairs POSITIONALLY, so
                        // a duplicated pair shifts what a directive configures.
                        var message = owner.EmitAsNonPartial && m.EmitAsNonPartial
                            ? $"Duplicate [GenerateMap]: the pair '{m.ParameterTypeFullName}' -> " +
                              $"'{m.ReturnTypeFullName}' is declared more than once on this class, so two " +
                              $"identical '{m.ReturnTypeFullName} {m.MethodName}({m.ParameterTypeFullName})' " +
                              "methods would be emitted (CS0111 in the generated file). Declare each pair " +
                              "exactly once — remove the duplicate [GenerateMap] attribute."
                            : $"[GenerateMap] would emit '{m.ReturnTypeFullName} {m.MethodName}" +
                              $"({m.ParameterTypeFullName})', but this class already declares a partial " +
                              "method with that exact signature over the same pair (CS0111 in the generated " +
                              "file). The pair is mapped either way, so declare it once: remove the " +
                              "[GenerateMap] and keep the partial method, or delete the partial method and " +
                              "let [GenerateMap] emit it.";
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.DuplicateGenerateMapSignature,
                            dupLoc,
                            message));

                        // Drop the synthesized side so a declared partial (when one exists) keeps its
                        // implementation slot; by construction the declared method was extracted first, so
                        // the LATER entry is the [GenerateMap]-synthesized one — asserted rather than
                        // assumed, in case extraction order ever changes.
                        if (m.EmitAsNonPartial)
                        {
                            collisionDrop.Add(i);
                        }
                        else
                        {
                            collisionDrop.Add(ownerIdx);
                            sigOwner[sig] = i;
                        }

                        continue;
                    }

                    var loc = publicMethodLocs.TryGetValue(i, out var l) ? l
                        : publicMethodLocs.TryGetValue(ownerIdx, out var l2) ? l2 : null;
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.ConflictingMapSignature,
                        loc,
                        $"Cannot generate two maps named '{m.MethodName}' from source '{m.ParameterTypeFullName}' " +
                        $"to different targets ('{owner.ReturnTypeFullName}' and '{m.ReturnTypeFullName}'): C# cannot " +
                        $"overload by return type. Give one a distinct name with a partial method, e.g. " +
                        $"'public partial {m.ReturnTypeFullName} MapToOther({m.ParameterTypeFullName} source);'."));
                    collisionDrop.Add(i);
                }

                // Remove dropped methods (highest index first to keep indices valid). Sorted first: the
                // DWARF094 declared-partial arm can add an OWNER index, which is smaller than every index
                // appended before it, so append order alone is no longer ascending.
                collisionDrop.Sort();
                for (var k = collisionDrop.Count - 1; k >= 0; k--)
                    methods.RemoveAt(collisionDrop[k]);
            }

        }

        // ── DWARF076: declared create-map whose source and target are the same type ──
        // A type trivially satisfies itself, so every destination member resolves and the completeness gate
        // stays silent while the generator emits a shallow copy. Almost always a copy-paste slip. Only
        // DECLARED create-style maps are considered: update-into (T src, T dest) is a legitimate
        // refresh-an-existing-instance pattern, and the span/async-stream/projection shapes carry their own
        // parameter lists. Auto-synthesized nested pairs never reach `methods`, so a same-type nested member
        // (the graph's shape, not a typo) is exempt by construction.
        // A deliberate clone says so with [SuppressMessage] on the mapper. #pragma cannot reach a
        // generator-reported diagnostic (Roslyn does not run these through pragma filtering), so without
        // honouring the attribute the only escape hatch is an .editorconfig entry that disables the rule
        // for an entire project — far broader than the one deliberate clone being acknowledged.
        private static void ReportSelfMapWithoutConfiguration(List<MapMethodModel> methods, Dictionary<int, LocationInfo?> publicMethodLocs, List<DiagnosticInfo> diagnostics, INamedTypeSymbol classSymbol)
        {
            var selfMapSuppressed = HasSuppressMessage(classSymbol, "DWARF076");

            for (var i = 0; i < methods.Count && !selfMapSuppressed; i++)
            {
                var m = methods[i];
                if (!(m.IsPartial || m.EmitAsNonPartial))
                {
                    continue;
                }

                if (m.IsUpdateInto || m.IsSpanMap || m.IsAsyncStreamMap || m.IsProjection)
                {
                    continue;
                }

                if (!string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, StringComparison.Ordinal))
                {
                    continue;
                }

                publicMethodLocs.TryGetValue(i, out var selfLoc);
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.SelfMap,
                    selfLoc,
                    // The remedy names <NoWarn> and NOT a #pragma on purpose, and the distinction is not
                    // pedantry: Roslyn filters generator-reported diagnostics at the COMPILATION level only, so
                    // neither `#pragma warning disable DWARF076` nor an editorconfig
                    // `dotnet_diagnostic.DWARF076.severity` reaches them. Both were tried against a real consumer
                    // solution and neither suppressed anything; only NoWarn did. The previous wording said
                    // "suppress DWARF076 here", which sends the reader to a per-site pragma that silently does
                    // nothing — a remedy that cannot be followed is worse than no remedy, because the reader
                    // concludes the diagnostic is broken rather than that the advice was.
                    $"Source and target are the same type '{m.ParameterTypeFullName}', so '{m.MethodName}' just " + "emits a shallow copy of every member. This is usually a mistyped type argument — did you mean " + "a different target? If a shallow copy IS what you want, add DWARF076 to <NoWarn> in the project " + "to say so — a per-site #pragma does NOT suppress generator diagnostics."));
            }

        }

        /// <summary>
        ///     The call-graph key for a declared method: its name, disambiguated by parameter type only when
        ///     the name is overloaded.
        /// </summary>
        /// <remarks>
        ///     Was a local function inside the recursion-cycle phase, closing over
        ///     <paramref name="declaredNameCount" />. Extracting the phases showed a SECOND phase computing the
        ///     same key, so it became a shared helper — two phases keying the same graph two ways would be a
        ///     silent mismatch, and the split is what made the sharing visible.
        /// </remarks>
        private static string DeclKey(MapMethodModel mm, Dictionary<string, int> declaredNameCount)
        {
            return declaredNameCount.TryGetValue(mm.MethodName, out var cnt) && cnt > 1
                ? mm.MethodName + "\u00a7" + mm.ParameterTypeFullName
                : mm.MethodName;
        }

        /// <summary>
        ///     The <see cref="DeclKey" /> of the one overload <paramref name="edge" />'s converter was adopted from, when
        ///     resolution recorded it and the name is overloaded; otherwise <see langword="null" />, and the caller
        ///     falls back to the bare-name treatment.
        /// </summary>
        /// <remarks>
        ///     The exact key INCLUDES the caller itself. The bare-name fallback excludes it, because a bare name
        ///     cannot say which overload it meant \u2014 and that exclusion is what hid an overloaded self-map's own cycle.
        /// </remarks>
        private static string? ExactOverloadKey(string? converterMethod, string? converterParamTypeFqn, Dictionary<string, int> declaredNameCount)
        {
            return converterParamTypeFqn is not null && converterMethod is not null && declaredNameCount.TryGetValue(converterMethod, out var cnt) && cnt > 1
                ? converterMethod + "\u00a7" + converterParamTypeFqn
                : null;
        }

        /// <summary>
        ///     The source members this mapper disowns for <paramref name="method" /> — class-level
        ///     <c>[MapIgnoreSource]</c> plus the method's own, by their REAL source spelling. Read by the DWARF064
        ///     shadow rule, whose message names <c>[MapIgnoreSource]</c> as the way to declare a shadow
        ///     intentional; until it was threaded through, that remedy was inert. Same construction as the
        ///     source-coverage set in <c>EmitSourceCoverage</c>, so the two rules agree on what "disowned" means.
        /// </summary>
        private static HashSet<string> IgnoredSourcesFor(MapperDeclarations decls, IMethodSymbol method)
        {
            var set = new HashSet<string>(decls.ClassIgnoreSources, StringComparer.Ordinal);
            foreach (var s in ReadIgnoreSources(method))
                set.Add(s);
            return set;
        }

        /// <summary>
        ///     Class-level <c>[MapIgnoreSource]</c> only — for synthesized pairs, which have no declared method
        ///     to read a method-level set from.
        /// </summary>
        private static HashSet<string> ClassIgnoredSources(MapperDeclarations decls)
        {
            return new HashSet<string>(decls.ClassIgnoreSources, StringComparer.Ordinal);
        }

        /// <summary>
        ///     Everything <c>ExtractCore</c> does for ONE declared method of the mapper — the body of its
        ///     per-method loop, which was 1,386 lines inline.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Eighteen <c>continue</c> statements targeted this loop and became <c>return</c>; eleven more
        ///         target inner loops and were left alone. That distinction is the whole safety of the move, and
        ///         getting it wrong is not subtle: converting all twenty-nine by text made the generator stop
        ///         emitting entirely, CS8795 across the suite. Each jump is resolved against its enclosing
        ///         constructs. No <c>break</c> targets this loop, which is what makes the extraction expressible
        ///         at all — a <c>break</c> would need a signal back to the caller.
        ///     </para>
        ///     <para>
        ///         <c>methodDiagStart</c> and <c>withheld</c> were declared outside the loop and are per
        ///         iteration: the first is dead after it, and the next phase reassigns the second before reading
        ///         it. Both are locals here now, which is what they always were in effect.
        ///     </para>
        ///     <para>
        ///         <paramref name="classIgnoreLivenessBlinded" /> is <c>ref</c> because it genuinely escapes:
        ///         the class-site DWARF095 verdict, hundreds of lines later, stands down when any endpoint this
        ///         walk cannot see is present.
        ///     </para>
        /// </remarks>
        private static void ProcessDeclaredMethod(
            IMethodSymbol method,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            ref bool classIgnoreLivenessBlinded,
            CancellationToken ct)
        {
            int methodDiagStart;
            bool withheld;

            ct.ThrowIfCancellationRequested();

            if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition)
            {
                return;
            }

            // A generic mapping method (arity > 0) cannot be implemented: the generator emits a
            // type-parameter-less body that fails to satisfy the generic partial declaration, producing
            // a confusing downstream C# error with no DwarfMapper signal. Refuse loudly (DWARF053) and
            // skip the method so no broken implementation is emitted.
            if (method.Arity > 0 || method.TypeParameters.Length > 0)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.GenericMapperMethodUnsupported,
                    LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None),
                    method.Name));
                return;
            }

            var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None);
            methodDiagStart = acc.Diagnostics.Count;

            // Before any endpoint-specific handling, because the mistake is the same one at all of them: this
            // loop is the single point every partial mapping method passes through, and a check placed inside
            // one endpoint's branch would have refused the directive on a create map and gone on discarding it
            // on the span and stream overloads of the very same mapper.
            ReportMemberFormDirectives(method, "mapping method", methodLocation, acc.Diagnostics);

            // ── Zero-alloc span map: void Map(ReadOnlySpan<S>/Span<S> src, Span<D> dst) ──
            if (TryHandleSpanMap(method, ctx, decls, policy, acc, methodLocation, methodDiagStart))
            {
                return;
            }
            // ── Update-into-existing: void/T Map(S src, T dest) ─────────────────────
            if (TryHandleUpdateIntoMap(method, ctx, decls, policy, acc, methodLocation, methodDiagStart))
            {
                return;
            }
            // ── Async streaming map: IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src) ──
            if (TryHandleAsyncStreamMap(method, ctx, decls, policy, acc, methodLocation, methodDiagStart))
            {
                return;
            }

            if (method.ReturnType is not (INamedTypeSymbol or IArrayTypeSymbol))
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                    methodLocation,
                    method.Name));
                return;
            }

            var targetType = method.ReturnType;

            var sourceType = method.Parameters[0].Type;

            // The FIFTH call site of the one gate, and the reason it is not named after the create map any
            // more. Every branch above this one is some other endpoint, so this is the create map — and a
            // directive whose home is the UPDATE-INTO ([MapCollectionKey], finding D14) is discarded HERE
            // exactly as the create-map-only trio is discarded there. Before resolution, so the refusal does
            // not depend on what else this method turns out to be wrong about.
            ReportDirectivesNotReadHere(method,
                ctx.SemanticModel.Compilation,
                sourceType,
                targetType,
                MapEndpointKind.CreateMap,
                methodLocation,
                acc.Diagnostics);

            // Phase 5: parameters after the source are extra named value sources, matched to destination
            // members by name (precedence: explicit > extra parameter > by-name). Pre-format their
            // signature fragments ("global::Type name") for emission.
            //
            // NullableFullyQualifiedFormat, not FullyQualifiedFormat: the fragment is re-emitted VERBATIM as
            // the implementing half of the user's partial declaration, so dropping the '?' off a nullable
            // reference parameter makes the two halves disagree — CS8611, inside the consumer's .g.cs, where
            // no pragma or NoWarn of theirs can reach it. Same hole the collection helper's declared
            // parameter/return types had, hence the same format rather than a third copy of it. Under
            // `#nullable disable` the annotation is Oblivious and the format adds nothing, so this is a no-op
            // for every oblivious consumer.
            var extraParams = new List<(string Name, ITypeSymbol Type)>();
            var extraParamSig = new List<string>();
            for (var pi = 1; pi < method.Parameters.Length; pi++)
            {
                var ep = method.Parameters[pi];
                extraParams.Add((ep.Name, ep.Type));
                // Identifiers.Escape, not the raw ISymbol.Name: Roslyn hands over `class` for a parameter the
                // user wrote as `@class`, and the fragment is written straight into the emitted signature —
                // `int class`, which is not C#. The consumer's .g.cs then fails to PARSE (a 27-diagnostic
                // CS1001/CS1026/CS1519 cascade, ending in CS0111 and CS0756 against the partial), which is the
                // same unfixable-file failure class as the CS8611 above, only louder.
                extraParamSig.Add(ep.Type.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat) + " " + Identifiers.Escape(ep.Name));
            }

            // Read methodAutoNest early — needed by both Plan 21 (derived dispatch) and the normal path.
            var methodAutoNest = ReadMethodAutoNest(method, policy.ClassAutoNest);

            // ── Plan 22: early-detect heterogeneous [FlattenGraph] ───────────
            // If [FlattenGraph] is present on the same method, [MapDerivedType] attrs apply
            // to the GRAPH NODE types (not the root method type), so Plan 21's validation
            // (derived-src assignable to method source type = root type) would falsely reject them.
            // Read FlattenGraph attrs now so we can skip Plan 21 for hetero-FlattenGraph acc.Methods.
            var flattenGraphRawEarly = ReadFlattenGraphAttributes(method);
            var isHeteroFlattenGraph = flattenGraphRawEarly.Count > 0;

            // ── Plan 21: [MapDerivedType] dispatch ───────────────────────────
            var rawDerivedPairs = ReadDerivedTypeAttributes(method);
            if (rawDerivedPairs.Count > 0 && !isHeteroFlattenGraph)
            {
                var resolvedArms =
                    new List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)>();
                // Keyed by the arm's source type, which is unique per method (seenSrcTypes below): the overload each
                // arm adopted, for the recursion-cycle phase's edge (see MemberMap.ConverterParamTypeFqn).
                var armParamTypes = new Dictionary<string, string?>(StringComparer.Ordinal);
                var seenSrcTypes = new HashSet<string>(StringComparer.Ordinal);

                foreach (var (derivedSrc, derivedTgt, _) in rawDerivedPairs)
                {
                    var srcFqn = derivedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var tgtFqn = derivedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                    // 1. srcDerived must be assignable to method source type.
                    if (!HasImplicitConversion(ctx.SemanticModel.Compilation, derivedSrc, sourceType))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] source type '{srcFqn}' is not assignable to method source type '{sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; derived source must inherit from or implement the method's source type."));
                        continue;
                    }

                    // 2. tgtDerived must be assignable to method return type.
                    if (!HasImplicitConversion(ctx.SemanticModel.Compilation, derivedTgt, targetType))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] target type '{tgtFqn}' is not assignable to method return type '{targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'; derived target must inherit from or implement the method's return type."));
                        continue;
                    }

                    // 3. No duplicate source types.
                    if (!seenSrcTypes.Add(srcFqn))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] duplicate source type '{srcFqn}'; each derived source type may only be registered once per dispatch method."));
                        continue;
                    }

                    // 4. Resolve converter via TryResolveConversion.
                    // SF-ORDER fix: pass allowInterfaceSrc=true so that [MapDerivedType] arms
                    // with an interface source (e.g. [MapDerivedType(typeof(IFoo), typeof(FooDto))])
                    // are acc.Synthesized correctly. The DWARF033 guard is suppressed here because the
                    // user explicitly opted in via the attribute.
                    var resolved = TryResolveConversion(
                        ctx.SemanticModel.Compilation,
                        derivedSrc,
                        derivedTgt,
                        null,
                        // Both lists, not just the mapper-method one: a partial mapping method is an ordinary
                        // one-parameter method too, so it appears in decls.AllMethods as well and the auto-adoption
                        // scan would pick it up again from there.
                        ExcludingMethod(decls.AllMethods, method.Name, sourceType, targetType),
                        // The dispatching method is not a candidate for its own arms. It matches every arm
                        // by signature — a derived source converts to the declared source type — so
                        // [MapDerivedType<AliasCommand, CommandOverviewDto>] on
                        // `partial CommandOverviewDto ToDto(Command)` resolved to ToDto itself and emitted
                        // `AliasCommand __s => ToDto(__s)`. That is a switch arm calling its own switch:
                        // it compiles, reports nothing, and overflows the stack for every AliasCommand.
                        // Excluded, the arm synthesizes a real AliasCommand -> CommandOverviewDto mapper,
                        // which is what "map this derived type differently" asked for.
                        ExcludingMethod(decls.MapperMethods, method.Name, sourceType, targetType),
                        policy.EnumPolicy,
                        acc.Synthesized,
                        policy.NullStrategy,
                        methodLocation,
                        srcFqn,
                        acc.Diagnostics,
                        out var armConverter,
                        out _,
                        out var armNeedsCtx,
                        out var armParamType,
                        methodAutoNest,
                        acc.NestedRegistry,
                        policy.NullCollections == NullCollectionsBehavior.AsNull,
                        policy.IsPreserveMode,
                        true,
                        policy.IsSetNullMode,
                        policy.ImplicitConversions,
                        // An arm is not an invitation to reuse a converter dedicated to some other pair.
                        decls.MapperReservedConverters);

                    if (!resolved || armConverter is null)
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.InvalidMapDerivedType,
                            methodLocation,
                            $"[MapDerivedType] pair ('{srcFqn}', '{tgtFqn}') is not mappable: no declared partial overload and not auto-nestable."));
                        continue;
                    }

                    resolvedArms.Add((derivedSrc, derivedTgt, armConverter, armNeedsCtx));
                    armParamTypes[srcFqn] = armParamType;
                }

                // Sort arms most-derived-first.
                var sortedArms = SortArmsMostDerivedFirst(resolvedArms, ctx.SemanticModel.Compilation);

                // DWARF036: detect mutually-unorderable interface/abstract source arms.
                // If two arm source types are neither assignable to each other AND at least one
                // is an interface or abstract class, a concrete type could implement/inherit both
                // and would dispatch non-deterministically (whichever arm is first wins).
                // Concrete-to-concrete pairs are NOT ambiguous: a concrete instance has exactly
                // one runtime type, so at most one arm can match at runtime.
                DetectAmbiguousInterfaceArms(sortedArms, ctx.SemanticModel.Compilation, methodLocation, acc.Diagnostics);

                var armModels = sortedArms
                    .Select(a => new DerivedTypeArm(
                        a.Src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        a.ConverterMethod,
                        a.NeedsCtx,
                        armParamTypes[a.Src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]))
                    .ToArray();

                // Collect applicable hooks.
                var derivedBefore = new List<string>();
                foreach (var h in decls.BeforeHookDefs)
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                    {
                        derivedBefore.Add(h.Name);
                    }

                var derivedAfter = new List<HookCall>();
                foreach (var h in decls.AfterHookDefs)
                {
                    bool applies;
                    bool takesSource;
                    if (h.P1 is null)
                    {
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P0);
                        takesSource = false;
                    }
                    else
                    {
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
                        takesSource = true;
                    }

                    if (!applies)
                    {
                        continue;
                    }

                    var targetIsRef = h.TargetRefKind == RefKind.Ref;
                    if (targetType.IsValueType && !targetIsRef)
                    {
                        continue;
                    }

                    if (RefHookTargetMismatches(targetType, h, methodLocation, acc.Diagnostics))
                    {
                        continue;
                    }

                    derivedAfter.Add(new HookCall(h.Name, takesSource, targetIsRef));
                }

                // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                // model is still recorded, because the class-level analyses downstream ask what the mapper
                // DECLARES (see MapMethodModel.Withheld).
                withheld = TryScopeCompletenessRefusalToItsMethod(
                    acc.Diagnostics,
                    methodDiagStart,
                    method.Name,
                    methodLocation);

                acc.Methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    sourceType.IsReferenceType,
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    EquatableArray.From(derivedBefore),
                    EquatableArray.From(derivedAfter),
                    false,
                    "",
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    true,
                    targetType.IsReferenceType,
                    DerivedTypeArms: EquatableArray.From(armModels),
                    Withheld: withheld,
                    ParameterTypeSignature: sourceType.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    ReturnTypeSignature: DeclaredReturnSignature(method),
                    ReturnIsNullableRef: DeclaresNullableRefReturn(method)));
                // A dispatch method's arm resolution is not an unscoped-ignore consumer this walk can see,
                // so the class-site DWARF095 verdict stands down for this class (see the flag's declaration).
                classIgnoreLivenessBlinded = true;
                return;
            }
            // ── End Plan 21 ──────────────────────────────────────────────────

            // ── Fix 1: Top-level collection/dictionary-returning method ──────────────────
            // If the return type is a recognized collection or dictionary TARGET shape,
            // route through TryResolveConversion to get a acc.Synthesized helper, then emit
            // "return helper(param);" instead of running ConstructorSelector → DWARF007.
            // Detection: call CollectionConverter.TryResolve / DictionaryConverter.TryResolve
            // with targetType as both src and target — they check the TARGET shape first.
            // Scope: only fires when the return type IS a collection/dict; object/record/scalar
            // return types fail both TryResolve calls and fall through unchanged.
            // An ARRAY return always takes this route, including the shapes CollectionConverter declines (a
            // multi-dimensional array): an array is never constructed member by member, so the conversion owns
            // both the success and the refusal, exactly as it does for an array MEMBER. Below this block the
            // return is therefore a named type.
            var isCollReturn = targetType is IArrayTypeSymbol || CollectionConverter.TryResolve(targetType,
                targetType,
                out _,
                out _,
                out _);
            var isDictReturn = !isCollReturn &&
                               DictionaryConverter.TryResolve(targetType,
                                   targetType,
                                   out _,
                                   out _,
                                   out _,
                                   out _,
                                   out _);

            if (isCollReturn || isDictReturn)
            {
                // The route emits `return helper(param);` — there is nowhere for an extra parameter to go, so a
                // second parameter is refused here rather than dropped from the implementing half (which emitted
                // CS0759 + CS8795 into the consumer's .g.cs for `partial List<D> Map(List<S> s, int x)`).
                if (method.Parameters.Length > 1)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                        methodLocation,
                        method.Name));
                    return;
                }

                var tlResolved = TryResolveConversion(
                    ctx.SemanticModel.Compilation,
                    sourceType,
                    targetType,
                    null,
                    // The method itself is in AllMethods, and the user-declared-conversion arm scans that list
                    // too: an array return from a source no collection shape accepts (a ReadOnlySpan<S>) found
                    // ITSELF there and emitted a depth wrapper calling itself (CS0457). A List return never got
                    // that far only because DWARF027 answers first for a named collection target.
                    ExcludingMethod(decls.AllMethods, method.Name, sourceType, targetType),
                    // This pair is resolved as a WHOLE, so the method must not be a candidate for its own
                    // conversion — the same self-exclusion the [GenerateMap] collection path needs.
                    ExcludingPair(decls.MapperMethods, sourceType, targetType),
                    policy.EnumPolicy,
                    acc.Synthesized,
                    policy.NullStrategy,
                    methodLocation,
                    method.Name,
                    acc.Diagnostics,
                    out var tlConverter,
                    out _,
                    out var tlNeedsCtx,
                    out _,
                    methodAutoNest,
                    acc.NestedRegistry,
                    policy.NullCollections == NullCollectionsBehavior.AsNull,
                    policy.IsPreserveMode,
                    isSetNull: policy.IsSetNullMode,
                    implicitConversions: policy.ImplicitConversions,
                    // WITHOUT this the ELEMENT conversion adopts a method dedicated to one pair. Found in a
                    // real consumer: `[MapConstructor<DbCommand, UserCommand>(nameof(CreateUserCommand))]`
                    // plus `partial ICollection<UserCommand> ToUserCommands(List<DbCommand>)` emitted
                    // `result.Add(CreateUserCommand(i))` — the bare factory, without the member assignments
                    // the real element map performs, and that factory ignores its argument. A list of blank
                    // objects, silently. The [GenerateMap] path was fixed for this; the DECLARED-METHOD path
                    // was the same bug at the other door.
                    reservedConverters: decls.MapperReservedConverters);

                if (!tlResolved || tlConverter is null)
                    // Element conversion failed (diagnostic already reported). Skip this method.
                {
                    return;
                }

                var tlMember = new MemberMap(
                    "",
                    "", // sentinel: emit helper(param) not helper(param.Member)
                    tlConverter,
                    ConverterNeedsDepthCtx: tlNeedsCtx);

                // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                // model is still recorded, because the class-level analyses downstream ask what the mapper
                // DECLARES (see MapMethodModel.Withheld).
                withheld = TryScopeCompletenessRefusalToItsMethod(
                    acc.Diagnostics,
                    methodDiagStart,
                    method.Name,
                    methodLocation);

                acc.Methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    sourceType.IsReferenceType,
                    EquatableArray.From(new[]
                    {
                        tlMember
                    }),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    false,
                    "",
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    true,
                    targetType.IsReferenceType,
                    IsTopLevelCollectionConversion: true,
                    ParameterIsPublicType: IsEffectivelyPublic(sourceType),
                    ReturnIsPublicType: IsEffectivelyPublic(targetType),
                    Withheld: withheld,
                    ParameterTypeSignature: sourceType.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    ReturnTypeSignature: DeclaredReturnSignature(method),
                    ReturnIsNullableRef: DeclaresNullableRefReturn(method)));
                // A top-level collection map's element pair is acc.Synthesized (pair-scoped config only), so
                // this method consumes no unscoped ignore this walk can see — class-site DWARF095 stands
                // down for the class rather than guess (see the flag's declaration).
                classIgnoreLivenessBlinded = true;
                return;
            }
            // ── End Fix 1 ────────────────────────────────────────────────────────────────

            // Every array return was claimed by Fix 1 (see isCollReturn), so construction sees a named type.
            var namedTargetType = (INamedTypeSymbol)targetType;

            // Selection must see the FULL explicit-rename set — method-level [MapProperty] PLUS the
            // [ReverseMap]-inherited renames and pair-scoped [MapProperty<S,T>] renames — so a ctor parameter
            // bound ONLY via a rename still counts as satisfiable. Previously reverse/pair renames were merged
            // AFTER Select, so Select saw method-level maps alone and could reject a satisfiable wide ctor,
            // picking a narrower one — or emitting DWARF008 — for a mapping resolution would have completed
            // (ISSUE-016 audit regression). The [GenerateMap] path already passes its full genExplicit to
            // Select; this makes the declared-method path consistent. The golden fingerprint sorts acc.Diagnostics
            // by Id, so moving CollectReverseRenames' emission earlier cannot move the manifest.
            var explicitMaps = ReadExplicitMaps(method);
            // [ReverseMap]: inherit the inverted simple renames (A→B becomes B→A). Non-invertible → DWARF051.
            var reverseAdds = CollectReverseRenames(decls.ClassSymbol,
                method,
                sourceType,
                targetType,
                explicitMaps,
                acc.Diagnostics,
                methodLocation);
            if (reverseAdds.Count > 0)
            {
                explicitMaps.AddRange(reverseAdds);
            }

            // Pair-scoped class-level config ([MapProperty<S,T>]) also applies to a DECLARED partial method for
            // the same pair; method-level config wins, pair-scoped fills the gaps, and MatchPairProps marks them
            // consumed so DWARF056 does not fire for a pair this method already maps.
            var (pairExplicit, pairExtras) = MatchPairProps(decls.PairProps, sourceType, targetType);
            var methodExplicitTargets = new HashSet<string>(explicitMaps.Select(m => m.Target), StringComparer.Ordinal);
            foreach (var pe in pairExplicit)
                if (methodExplicitTargets.Add(pe.Target))
                {
                    explicitMaps.Add(pe);
                }

            // Before constructor selection, so the liveness marking (and a dead method-site name's report)
            // happens even when selection refuses the target.
            JudgeUnscopedIgnores(decls.ClassIgnores,
                acc.LiveClassIgnores,
                method,
                targetType,
                ctx.SemanticModel.Compilation,
                policy.AllowNonPublic,
                methodLocation,
                acc.Diagnostics,
                acc.IgnorableNamesMemo);

            // Choose construction strategy for the target type, now with the full rename set visible.
            var ctor = ConstructorSelector.Select(ctx.SemanticModel.Compilation,
                namedTargetType,
                acc.Diagnostics,
                methodLocation,
                out var objInitOnly,
                policy.AllowNonPublic,
                sourceType,
                explicitMaps);
            if (ctor is null)
            {
                return;
            }

            var ignores = new HashSet<string>(decls.ClassIgnores, IgnoreNameComparer);
            foreach (var i in ReadIgnores(method)) ignores.Add(i);
            // A forward [ReverseMap] method with no inverse declared → DWARF052.
            if (HasReverseMap(method))
            {
                // The inverse may declare additional (Phase 5) parameters after the source, so match on
                // parameter[0] + return type, not an exact arity of 1.
                var hasInverse = decls.ClassSymbol.GetMembers().OfType<IMethodSymbol>().Any(m =>
                    !SymbolEqualityComparer.Default.Equals(m, method) &&
                    m.Parameters.Length >= 1 &&
                    SymbolEqualityComparer.Default.Equals(
                        m.Parameters[0].Type,
                        targetType) &&
                    SymbolEqualityComparer.Default.Equals(
                        m.ReturnType,
                        sourceType));
                if (!hasInverse)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReverseMapTargetMissing,
                        methodLocation,
                        $"[ReverseMap] on '{method.Name}' has no inverse mapping method '{sourceType.ToDisplayString()} X({targetType.ToDisplayString()})'"));
                }
            }

            var mapPropExtras = ReadMapPropertyExtras(method);
            var stringFormats = ReadStringFormats(method);
            var mapValues = ReadMapValues(method);

            // pairExplicit / pairExtras were computed above (before Select) so the constructor selector could see
            // the pair-scoped renames; pairExtras is merged into the extras set here.
            var methodExtraTargets = new HashSet<string>(mapPropExtras.Select(e => e.Target), StringComparer.Ordinal);
            foreach (var pe in pairExtras)
                if (methodExtraTargets.Add(pe.Target))
                {
                    mapPropExtras.Add(pe);
                }

            foreach (var pim in MatchPairIgnores(decls.PairIgnores, targetType)) ignores.Add(pim);
            var methodValueTargets = new HashSet<string>(mapValues.Select(v => v.Target), StringComparer.Ordinal);
            foreach (var pv in MatchPairValues(decls.PairValues, targetType))
                if (methodValueTargets.Add(pv.Target))
                {
                    mapValues.Add(pv);
                }

            var flattenRoots = ReadFlattenRoots(method);
            var reinterpretMembers = ReadReinterpretMembers(method);
            var shareMembers = ReadShareMembers(method);
            var denseEnumMembers = ReadDenseEnumKeys(method);

            // ── Plan 20 / 22: [FlattenGraph] ─────────────────────────────────
            // Read and resolve [FlattenGraph] directives BEFORE ResolveMembers so that
            // target collection members can be added to ignores and skipped from normal mapping.
            // flattenGraphRawEarly was already read above (for hetero-detection); reuse it.
            var flattenGraphRaw = flattenGraphRawEarly;
            var flattenGraphConsumed = new HashSet<string>(StringComparer.Ordinal);
            List<FlattenGraphDirective> resolvedFgDirectives;
            List<MemberMap> fgInjectedMembers;

            if (flattenGraphRaw.Count > 0)
            {
                (resolvedFgDirectives, fgInjectedMembers) = ResolveFlattenGraphDirectives(
                    sourceType,
                    namedTargetType,
                    flattenGraphRaw,
                    ctx.SemanticModel.Compilation,
                    methodLocation,
                    acc.Diagnostics,
                    decls.AllMethods,
                    decls.MapperMethods,
                    policy.EnumPolicy,
                    acc.Synthesized,
                    policy.NullStrategy,
                    methodAutoNest,
                    acc.NestedRegistry,
                    policy.IsPreserveMode,
                    policy.AllowNonPublic,
                    flattenGraphConsumed,
                    rawDerivedPairs);

                // Add consumed targets to ignores so ResolveMembers skips them and
                // does not emit DWARF001 (unmapped) for them.
                foreach (var consumed in flattenGraphConsumed)
                    ignores.Add(consumed);
            }
            else
            {
                resolvedFgDirectives = new List<FlattenGraphDirective>();
                fgInjectedMembers = new List<MemberMap>();
            }

            // Resolve constructor arguments (empty set when objInitOnly).
            MemberMap[] ctorArgs;
            HashSet<string> consumedParams;
            // Members that are `required` AND satisfied via a ctor param but whose ctor is NOT annotated
            // [SetsRequiredMembers]: C# requires them to ALSO be set in the object initializer (CS9035).
            // These must NOT be excluded from the initializer even though they are in consumedParams.
            HashSet<string> requiredMustInitialize;
            if (objInitOnly)
            {
                ctorArgs = Array.Empty<MemberMap>();
                consumedParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                requiredMustInitialize = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(ctor,
                        sourceType,
                        ctx.SemanticModel.Compilation,
                        methodLocation,
                        acc.Diagnostics,
                        policy.CaseInsensitive,
                        policy.AllowNonPublic,
                        explicitMaps,
                        decls.AllMethods,
                        decls.MapperMethods,
                        policy.EnumPolicy,
                        acc.Synthesized,
                        policy.NullStrategy,
                        methodAutoNest,
                        acc.NestedRegistry,
                        out ctorArgs,
                        out consumedParams,
                        policy.NullCollections == NullCollectionsBehavior.AsNull,
                        policy.IsPreserveMode,
                        policy.IsSetNullMode,
                        policy.ImplicitConversions))
                    // At least one parameter was unmappable → DWARF024 already reported; skip emit.
                {
                    return;
                }

                // Compute which consumed-param members are `required` and whose ctor lacks [SetsRequiredMembers].
                // Those must still be emitted in the object initializer to satisfy the C# `required` rule.
                requiredMustInitialize = ComputeRequiredMustInitialize(ctor, namedTargetType, consumedParams);
            }

            var members = ResolveMembers(
                sourceType,
                namedTargetType,
                ignores,
                ctx.SemanticModel.Compilation,
                methodLocation,
                acc.Diagnostics,
                new MapperOptions(
                    CaseInsensitive: policy.CaseInsensitive,
                    AutoNest: methodAutoNest,
                    NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                    IsPreserve: policy.IsPreserveMode,
                    IsSetNull: policy.IsSetNullMode,
                    ImplicitConversions: policy.ImplicitConversions,
                    NameConvention: policy.NameConvention,
                    SkipNullSourceMembers: ResolveNullSkip(decls.PairNullSkips, method, sourceType, targetType, policy.SkipNullSrc),
                    AllowNonPublic: policy.AllowNonPublic,
                    ExplicitOnly: policy.ExplicitOnly,
                    IgnoreObsolete: policy.IgnoreObsolete),
                explicitMaps,
                decls.AllMethods,
                decls.MapperMethods,
                policy.EnumPolicy,
                acc.Synthesized,
                policy.NullStrategy,
                flattenRoots,
                reinterpretMembers,
                consumedParams,
                requiredMustInitialize,
                acc.NestedRegistry,
                mapValues,
                decls.ValueProviders,
                extraParams,
                mapPropExtras,
                stringFormats,
                decls.MapperReservedConverters,
                // NOT gated on objInitOnly: a parameterless constructor can still carry
                // [SetsRequiredMembers], and it satisfies the required members exactly as a parameterized one
                // would. Gating here produced a false DWARF079 on that shape.
                CtorSetsRequiredMembers(ctor),
                ignoredSourceMembers: IgnoredSourcesFor(decls, method),
                shareMembers: shareMembers,
                denseEnumMembers: denseEnumMembers);

            // Append FlattenGraph-injected member maps (traversal helper calls).
            // These come AFTER normal members so the object initializer order is:
            //   normal scalars/nested first, then flat-graph collections.
            members.AddRange(fgInjectedMembers);

            // ── Source-member coverage (RequiredMapping = Both) ───────────────────────────
            ReportSourceMemberCoverage(method, ctx, decls, policy, acc, sourceType, namedTargetType, members,
                ctorArgs, resolvedFgDirectives, extraParamSig, methodLocation, methodDiagStart);
        }

        // ── Async streaming map: IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src) ──
        // Emitted as an async iterator (await foreach … yield return conv(item)) that lazily
        // transforms the source sequence — preserves streaming/back-pressure, no buffering.
        // A trailing CancellationToken is accepted (and required to be honoured): without it, nothing the
        // consumer passes to `WithCancellation` can ever reach this iterator, so the stream is uncancellable.
        // The generated half must match the user's partial signature exactly, so the token only exists when
        // the user declared it.
        /// <returns>
        ///     <see langword="true" /> when this endpoint claimed the method and no further endpoint should
        ///     look at it. Falling through to <see langword="false" /> is the "not mine" answer, which is what
        ///     the original code expressed by simply not returning.
        /// </returns>
        private static bool TryHandleAsyncStreamMap(
            IMethodSymbol method,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            LocationInfo? methodLocation,
            int methodDiagStart)
        {
            bool withheld;

            var asCtParam = method.Parameters.Length == 2 && IsCancellationToken(method.Parameters[1].Type)
                ? method.Parameters[1].Name
                : null;
            if ((method.Parameters.Length == 1 || asCtParam is not null) &&
                !method.ReturnsVoid &&
                TryGetAsyncEnumerableElement(method.Parameters[0].Type,
                    out var asSrcElem) &&
                TryGetAsyncEnumerableElement(method.ReturnType, out var asDstElem))
            {
                var asComp = ctx.SemanticModel.Compilation;
                var asAutoNest = ReadMethodAutoNest(method, policy.ClassAutoNest);
                // Class-site liveness only, exactly as at the span map (DWARF090 owns the method site).
                JudgeUnscopedIgnores(decls.ClassIgnores,
                    acc.LiveClassIgnores,
                    null,
                    asDstElem,
                    asComp,
                    policy.AllowNonPublic,
                    methodLocation,
                    acc.Diagnostics,
                    acc.IgnorableNamesMemo);
                ReportDirectivesNotReadHere(method,
                    asComp,
                    asSrcElem,
                    asDstElem,
                    MapEndpointKind.AsyncStream,
                    methodLocation,
                    acc.Diagnostics);
                if (ReportElementWiseDirectiveGaps(method,
                        decls.ClassSymbol,
                        asSrcElem,
                        asDstElem,
                        policy.ExplicitOnly,
                        asComp,
                        policy.AllowNonPublic,
                        methodLocation,
                        acc.Diagnostics))
                {
                    return true;
                }

                if (!TryResolveConversion(asComp,
                        asSrcElem,
                        asDstElem,
                        null,
                        decls.AllMethods,
                        decls.MapperMethods,
                        policy.EnumPolicy,
                        acc.Synthesized,
                        policy.NullStrategy,
                        methodLocation,
                        method.Name,
                        acc.Diagnostics,
                        out var asConv,
                        out var asNull,
                        out var asNeedsCtx,
                        out _,
                        asAutoNest,
                        acc.NestedRegistry,
                        reservedConverters: decls.MapperReservedConverters))
                {
                    return true; // element pair not mappable → diagnostic already added
                }

                if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
                {
                    acc.ElementPairsOwedCoverage.Add(
                        (asSrcElem, asDstElem, methodLocation, ReadIgnoreSources(method).ToList()));
                }

                // Round 29 T2.8, mirroring the span map's own computation (T0.2b) one endpoint over: a
                // nullable-annotated REFERENCE element (IAsyncEnumerable<C?>) reaching a synthesized object
                // helper needs the null-forgiving '!' that helper's null-guard makes safe (null in, null out).
                // Read by MapEmitter's async-stream loop through the shared CollectionConverter.ElementExpr.
                var asSrcElemIsNullableRef = asSrcElem.IsReferenceType &&
                                             asSrcElem.NullableAnnotation == NullableAnnotation.Annotated;

                var asElemMember = new MemberMap(
                    "",
                    "",
                    asConv,
                    asNull,
                    asNeedsCtx,
                    SourceIsNullableRef: asSrcElemIsNullableRef,
                    // Round 29 T2.9: the '!' above is only reached for a SYNTHESIZED element helper. A map
                    // method the USER declared has an equally non-nullable parameter and was left un-forgiven —
                    // CS8604 per element in the consumer's .g.cs. Same coupled call the member and collection
                    // paths make, so the forgiveness and DWARF070 cannot diverge between the two.
                    ConverterParamIsNonNullableRef: ForgiveNestedNullableArg(asConv,
                        asSrcElem,
                        asDstElem,
                        decls.MapperMethods,
                        decls.AllMethods,
                        method.Name,
                        methodLocation,
                        acc.Diagnostics,
                        NullSourceKind.CollectionElement),
                    ConverterReturnIsNullableRef: ForgiveConverterNullableReturn(asConv,
                        asDstElem,
                        decls.MapperMethods,
                        decls.AllMethods,
                        method.Name,
                        methodLocation,
                        acc.Diagnostics,
                        NullSourceKind.CollectionElement));

                // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                // model is still recorded, because the class-level analyses downstream ask what the mapper
                // DECLARES (see MapMethodModel.Withheld).
                withheld = TryScopeCompletenessRefusalToItsMethod(
                    acc.Diagnostics,
                    methodDiagStart,
                    method.Name,
                    methodLocation);

                acc.Methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    false,
                    EquatableArray.From(new[]
                    {
                        asElemMember
                    }),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    false,
                    "",
                    IsAsyncStreamMap: true,
                    AsyncCancellationParam: asCtParam,
                    ParameterIsPublicType: IsEffectivelyPublic(method.Parameters[0].Type),
                    ReturnIsPublicType: IsEffectivelyPublic(method.ReturnType),
                    MaxDepth: policy.MaxDepth,
                    Withheld: withheld,
                    ParameterTypeSignature: method.Parameters[0].Type.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    ReturnTypeSignature: DeclaredReturnSignature(method),
                    ReturnIsNullableRef: DeclaresNullableRefReturn(method),
                    AsyncStreamTargetElementFullName: asDstElem.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat)));
                return true;
            }

            // A construction mapper has the source as parameter 0 and may declare ADDITIONAL parameters
            // (Phase 5) used as extra named value sources — so allow >= 1, not exactly 1.
            if (method.ReturnsVoid || method.Parameters.Length < 1)
            {
                acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                    methodLocation,
                    method.Name));
                return true;
            }

            if (method.Parameters.Length == 1 && IsQueryable(method.ReturnType, out var projTarget) && IsQueryable(method.Parameters[0].Type, out var projSource) && projTarget is INamedTypeSymbol projTargetNamed)
            {
                // Everything this projection method reports lands at or after this index, and that is what
                // makes a refusal ATTRIBUTABLE to one method rather than to the class (TASKS.md I14 — see
                // TryScopeProjectionRefusalToItsMethod).
                var projDiagStart = acc.Diagnostics.Count;

                // Before the DWARF028 early exit below, which used to add the method with empty projection
                // members and continue: a directive dropped here is dropped whether or not reference
                // handling also refuses the endpoint.
                ReportDirectivesNotReadHere(method,
                    ctx.SemanticModel.Compilation,
                    projSource,
                    projTargetNamed,
                    MapEndpointKind.Projection,
                    methodLocation,
                    acc.Diagnostics);

                var projIgnores = new HashSet<string>(decls.ClassIgnores, IgnoreNameComparer);
                foreach (var i in ReadIgnores(method)) projIgnores.Add(i);
                JudgeUnscopedIgnores(decls.ClassIgnores,
                    acc.LiveClassIgnores,
                    method,
                    projTargetNamed,
                    ctx.SemanticModel.Compilation,
                    policy.AllowNonPublic,
                    methodLocation,
                    acc.Diagnostics,
                    acc.IgnorableNamesMemo);

                // Plan 19D: DWARF028 — ReferenceHandling != None is incompatible with projection
                // (a stateful identity map cannot live inside an expression tree).
                if (policy.ReferenceHandling != 0)
                {
                    EmitDwarf028(acc.Diagnostics,
                        methodLocation,
                        method.Name,
                        "reference handling is not supported in projection (stateful identity map cannot live in an expression tree); use ReferenceHandling=None or map at runtime");
                    // The method used to be added with EMPTY projection members "so no further cascades" —
                    // which only ever worked because the DWARF028 above suppressed the whole class, so the
                    // empty model was never emitted. Now that a projection refusal is scoped to its own
                    // method, emitting it would produce `Select(q, __s => new D { })`: a projection that
                    // silently drops every member. Dropped, like every other refused projection.
                    TryScopeProjectionRefusalToItsMethod(acc.Diagnostics,
                        projDiagStart,
                        method.Name,
                        methodLocation);
                    return true;
                }

                // VF5: Hooks (before/after) cannot live inside an expression tree.
                // Detect any applicable hook for this projection's source/target and emit DWARF028
                // instead of silently dropping it. Per thesis: loud, never silent.
                var hasApplicableHook = false;
                foreach (var h in decls.BeforeHookDefs)
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.ParamType))
                    {
                        hasApplicableHook = true;
                        break;
                    }

                if (!hasApplicableHook)
                {
                    foreach (var h in decls.AfterHookDefs)
                    {
                        var applies = h.P1 is null
                            ? HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P0)
                            : HasImplicitConversion(ctx.SemanticModel.Compilation, projSource, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, projTargetNamed, h.P1);
                        if (applies)
                        {
                            hasApplicableHook = true;
                            break;
                        }
                    }
                }

                if (hasApplicableHook)
                {
                    EmitDwarf028(acc.Diagnostics,
                        methodLocation,
                        method.Name,
                        "hooks (BeforeMap/AfterMap) are not supported in IQueryable projection (expression trees cannot contain hook calls); move hooks to a runtime mapper or remove them");
                }

                // Per-method [AutoNest] override, matching every other endpoint (C1). Projection ignored
                // the option entirely before this.
                var projAutoNest = ReadMethodAutoNest(method, policy.ClassAutoNest);
                var projConsumedSources = new HashSet<string>(StringComparer.Ordinal);
                var projExplicitMaps = ReadExplicitMaps(method);
                var projMembers = ResolveProjectionMembers(
                    projSource,
                    projTargetNamed,
                    projIgnores,
                    ctx.SemanticModel.Compilation,
                    methodLocation,
                    acc.Diagnostics,
                    new MapperOptions(
                        CaseInsensitive: policy.CaseInsensitive,
                        AutoNest: projAutoNest,
                        // I19: the FIFTH reader of NullCollections, and the endpoint that never read it.
                        // Same value, same shape as the four .Map call sites — a null source collection
                        // materialises the documented AsEmpty default through .Project too.
                        NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                        // Projection has no reference-tracking context: it builds an expression tree, and
                        // Preserve/SetNull are runtime graph behaviours with nothing to thread them onto.
                        // False rather than omitted, because the bundle admits no defaults.
                        IsPreserve: false,
                        IsSetNull: false,
                        // I20: the SIXTH reader of ImplicitConversions, and the endpoint that never read
                        // it. Same value the five .Map call sites get — a lossy cross-category conversion
                        // reports at .Project too, and breaks the build at both endpoints or at neither.
                        ImplicitConversions: policy.ImplicitConversions,
                        NameConvention: policy.NameConvention,
                        // The FOURTH call site of the one reader, and the last: projection used to pass the
                        // bare class value, which made it the third partial reader of an option written at
                        // four scopes (D6/D7). It now sees the method form and the pair-scoped form like
                        // every other endpoint.
                        //
                        // The refusal it feeds is not new: ResolveProjectionMembers already refuses an
                        // untranslatable null-skip per affected member with DWARF028, which is what
                        // [DwarfMapper(SkipNullSourceMembers = true)] and its assembly-level twin have
                        // always got here. Threading the scoped forms simply lets them reach it.
                        //
                        // This one line was built and reverted once (A6): DWARF028 is an Error, a blocking
                        // error suppresses the class's emission, the partial projection method is left
                        // unimplemented, and SurfaceProbe read the resulting CS8795 as "the compiler
                        // rejected the placement" — so landing it would have raised
                        // NotCompilableCellCeiling rather than closing anything. That was the R4 ordering
                        // defect in the instrument, not a fact about this option, and it is fixed: a
                        // CS8795 behind a blocking DWARF error now reads Refused.
                        SkipNullSourceMembers: ResolveNullSkip(decls.PairNullSkips, method, projSource, projTargetNamed, policy.SkipNullSrc),
                        AllowNonPublic: policy.AllowNonPublic,
                        ExplicitOnly: policy.ExplicitOnly,
                        IgnoreObsolete: policy.IgnoreObsolete),
                    projExplicitMaps,
                    policy.EnumPolicy,
                    "__s",
                    ReadMapPropertyExtras(method),
                    projConsumedSources,
                    // Both [Flatten] and [MapValue] are threaded now, and they arrive from opposite
                    // directions worth keeping distinct. A flattened leaf is `__s.Root.Leaf`, the navigation
                    // access every query provider translates (D10). A [MapValue] constant becomes a literal
                    // in the SELECT, and the object-initializer argument that makes SkipNullSourceMembers
                    // untranslatable one parameter up does NOT reach it: a constant assignment reads nothing
                    // from the destination, so there is no "current value" it could need. Its Use= form is
                    // the one part a provider cannot take, and that is refused rather than dropped.
                    //
                    // [MapValue] was built, measured and reverted once (A8) for the reason [MapNullSkip] was:
                    // DWARF042 and DWARF041 are Errors, a blocking error suppresses emission, and before R4
                    // the resulting CS8795 read as NotCompilable — so three of the four cells closed by
                    // moving into the population the parity theory judges by nothing. R4 is fixed.
                    ReadFlattenRoots(method),
                    ReadMapValues(method),
                    IgnoredSourcesFor(decls, method));

                // Source-side completeness for projection. The resolver already knows which source members it
                // read, so this needed tracking rather than new analysis — it was simply never asked.
                if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
                {
                    EmitSourceCoverageFromConsumed(
                        projSource,
                        projConsumedSources,
                        decls.ClassIgnoreSources,
                        ReadIgnoreSources(method),
                        policy.IgnoreObsolete,
                        ctx.SemanticModel.Compilation,
                        policy.AllowNonPublic,
                        methodLocation,
                        acc.Diagnostics);
                }

                // A refused projection member means this method has no honest body — half a projection is
                // silently wrong data, which is worse than none. Drop the METHOD; the mapper's Map acc.Methods
                // are untouched by anything a query provider cannot translate and are still emitted.
                if (TryScopeProjectionRefusalToItsMethod(acc.Diagnostics,
                        projDiagStart,
                        method.Name,
                        methodLocation))
                {
                    return true;
                }

                acc.Methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    true,
                    EquatableArray.From(Array.Empty<MemberMap>()),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    true,
                    projTargetNamed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ProjectionMembers: EquatableArray.From(projMembers.ToArray()),
                    ParameterTypeSignature: method.Parameters[0].Type.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    ReturnTypeSignature: DeclaredReturnSignature(method),
                    ReturnIsNullableRef: DeclaresNullableRefReturn(method)));
                return true;
            }

            return false;
        }

        // ── Update-into-existing: void/T Map(S src, T dest) ─────────────────────
        // Maps onto an EXISTING reference-type target instance (no construction; identity kept).
        // Return is void OR the destination type. v1 is None-semantics (no Preserve/SetNull on the
        // update method itself) and reference-type targets only (a struct dest by value can't
        // observe mutations). Resolution reuses ResolveMembers; only the emission differs.
        /// <returns>
        ///     <see langword="true" /> when this endpoint claimed the method and no further endpoint should
        ///     look at it. Falling through to <see langword="false" /> is the "not mine" answer, which is what
        ///     the original code expressed by simply not returning.
        /// </returns>
        private static bool TryHandleUpdateIntoMap(
            IMethodSymbol method,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            LocationInfo? methodLocation,
            int methodDiagStart)
        {
            bool withheld;

            if (method.Parameters.Length == 2 && method.Parameters[1].Type is INamedTypeSymbol updTgt && method.Parameters[1].Type.IsReferenceType && (method.ReturnsVoid || SymbolEqualityComparer.Default.Equals(method.ReturnType, method.Parameters[1].Type)))
            {
                var updSrc = method.Parameters[0].Type;
                var comp = ctx.SemanticModel.Compilation;
                // Before resolution, so a directive read only at another endpoint is reported whatever else
                // this method turns out to be wrong about. An update-into does NOT reach a sibling create map.
                ReportDirectivesNotReadHere(method,
                    comp,
                    updSrc,
                    updTgt,
                    MapEndpointKind.UpdateInto,
                    methodLocation,
                    acc.Diagnostics);
                var updIgnores = new HashSet<string>(decls.ClassIgnores, IgnoreNameComparer);
                foreach (var ig in ReadIgnores(method)) updIgnores.Add(ig);
                JudgeUnscopedIgnores(decls.ClassIgnores,
                    acc.LiveClassIgnores,
                    method,
                    updTgt,
                    comp,
                    policy.AllowNonPublic,
                    methodLocation,
                    acc.Diagnostics,
                    acc.IgnorableNamesMemo);
                var updExplicit = ReadExplicitMaps(method);
                var updMapValues = ReadMapValues(method);
                var updMapPropExtras = ReadMapPropertyExtras(method);
                var updFlatten = ReadFlattenRoots(method);
                var updReinterpret = ReadReinterpretMembers(method);
                var updShare = ReadShareMembers(method);
                var updDense = ReadDenseEnumKeys(method);
                var updAutoNest = ReadMethodAutoNest(method, policy.ClassAutoNest);

                var updMembers = ResolveMembers(
                    updSrc,
                    updTgt,
                    updIgnores,
                    comp,
                    methodLocation,
                    acc.Diagnostics,
                    new MapperOptions(
                        CaseInsensitive: policy.CaseInsensitive,
                        AutoNest: updAutoNest,
                        // These were hardcoded `false` while the other ResolveMembers call sites passed the
                        // real values. Passing them is consistency, not a demonstrated fix: preserve
                        // threading actually comes from the class-level nested synthesis path, so setting
                        // them back to `false` changes no generated output and no test can catch it. Kept
                        // because a lone hardcoded literal here is a landmine the next person would have to
                        // re-derive.
                        NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                        IsPreserve: policy.IsPreserveMode,
                        IsSetNull: policy.IsSetNullMode,
                        ImplicitConversions: policy.ImplicitConversions,
                        NameConvention: policy.NameConvention,
                        // Update-into is where patch-merge actually lives, so a method-level [MapNullSkip]
                        // matters most here.
                        SkipNullSourceMembers: ResolveNullSkip(decls.PairNullSkips, method, updSrc, updTgt, policy.SkipNullSrc),
                        AllowNonPublic: policy.AllowNonPublic,
                        ExplicitOnly: policy.ExplicitOnly,
                        IgnoreObsolete: policy.IgnoreObsolete),
                    updExplicit,
                    decls.AllMethods,
                    decls.MapperMethods,
                    policy.EnumPolicy,
                    acc.Synthesized,
                    policy.NullStrategy,
                    updFlatten,
                    updReinterpret,
                    null,
                    null,
                    acc.NestedRegistry,
                    updMapValues,
                    decls.ValueProviders,
                    mapPropertyExtras: updMapPropExtras,
                    stringFormats: ReadStringFormats(method),
                    mapperReservedConverters: decls.MapperReservedConverters,
                    // Update-into writes into an instance the CALLER already constructed, so there is no
                    // object initializer to omit a member from and `required` cannot be violated here.
                    // Without this, ignoring a required member on an update-into method reported a false
                    // DWARF079 — caught by NonTrivialShapeRuntimeTests, which does exactly that legitimately.
                    requiredMembersAlreadySatisfied: true,
                    ignoredSourceMembers: IgnoredSourcesFor(decls, method),
                    shareMembers: updShare,
                    denseEnumMembers: updDense);

                // Source-side completeness applies here too. It lived inline in the create-map branch, so
                // RequiredMapping = Both reported unconsumed source members through .Map and said nothing
                // through .Update on the SAME mapper. There are no constructor arguments to consider: an
                // update writes onto an instance that already exists.
                if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
                {
                    EmitSourceCoverage(
                        updSrc,
                        updMembers,
                        null,
                        decls.ClassIgnoreSources,
                        ReadIgnoreSources(method),
                        policy.IgnoreObsolete,
                        comp,
                        policy.AllowNonPublic,
                        methodLocation,
                        acc.Diagnostics);
                }

                // Update-into assigns members post-construction, so init-only targets cannot be written
                // (they would emit CS8852). Treat them as read-only here: drop them and surface DWARF007
                // so the user adds [MapIgnore], consistent with get-only members. In a CREATE map init-only
                // is writable via the object initializer, so this is update-into-specific.
                var updInitOnly = new HashSet<string>(StringComparer.Ordinal);
                for (var t = updTgt; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                    foreach (var tm in t.GetMembers())
                        if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: true })
                        {
                            updInitOnly.Add(p.Name);
                        }

                if (updInitOnly.Count > 0)
                {
                    var keptUpd = new List<MemberMap>(updMembers.Count);
                    foreach (var mm in updMembers)
                    {
                        if (updInitOnly.Contains(mm.TargetName) && !updIgnores.Contains(mm.TargetName))
                        {
                            // A matching source value would be lost — loud, actionable (suggests [MapIgnore]).
                            acc.Diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.ReadOnlyDestinationMember,
                                methodLocation,
                                mm.TargetName));
                            continue; // cannot assign an init-only property post-construction
                        }

                        keptUpd.Add(mm);
                    }

                    updMembers = keptUpd;
                }

                // [MapCollectionKey]: turn a List<T> member's whole-collection replacement into a key-based
                // upsert (merge in place). Applied before DWARF065 so an upserted collection is not also flagged
                // as "replaced".
                ApplyCollectionKeyUpserts(method,
                    updSrc,
                    updTgt,
                    comp,
                    policy.AllowNonPublic,
                    methodLocation,
                    acc.Diagnostics,
                    updMembers);

                // Item 13 (DWARF065): update-into maps a nested object member by REPLACING dest's existing
                // instance with a freshly-mapped one (the auto-nested __DwarfMap_Obj_* converter constructs a
                // new object), NOT by recursively merging into it. Callers expecting a deep merge / preserved
                // identity are warned. Info; only for acc.Synthesized object sub-maps (collections/dicts are
                // expected to be rebuilt, and a direct scalar copy preserves nothing to merge).
                foreach (var mm in updMembers)
                    if (mm.ConverterMethod is { } cmName && GeneratedNames.IsObjectMap(cmName))
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.UpdateIntoNestedReplaced,
                            methodLocation,
                            mm.TargetName));
                    }

                var updBefore = new List<string>();
                foreach (var h in decls.BeforeHookDefs)
                    if (HasImplicitConversion(comp, updSrc, h.ParamType))
                    {
                        updBefore.Add(h.Name);
                    }

                var updAfter = new List<HookCall>();
                foreach (var h in decls.AfterHookDefs)
                {
                    bool applies;
                    bool takesSource;
                    if (h.P1 is null)
                    {
                        applies = HasImplicitConversion(comp, updTgt, h.P0);
                        takesSource = false;
                    }
                    else
                    {
                        applies = HasImplicitConversion(comp, updSrc, h.P0) &&
                                  HasImplicitConversion(comp, updTgt, h.P1);
                        takesSource = true;
                    }

                    if (!applies)
                    {
                        continue;
                    }

                    // Target is a reference type → by-value is fine (mutations propagate); ref optional.
                    var updTargetIsRef = h.TargetRefKind == RefKind.Ref;
                    if (RefHookTargetMismatches(updTgt, h, methodLocation, acc.Diagnostics))
                    {
                        continue;
                    }

                    updAfter.Add(new HookCall(h.Name, takesSource, updTargetIsRef));
                }

                // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                // model is still recorded, because the class-level analyses downstream ask what the mapper
                // DECLARES (see MapMethodModel.Withheld).
                withheld = TryScopeCompletenessRefusalToItsMethod(
                    acc.Diagnostics,
                    methodDiagStart,
                    method.Name,
                    methodLocation);

                acc.Methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    updTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    updSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    updSrc.IsReferenceType,
                    EquatableArray.From(updMembers),
                    EquatableArray.From(updBefore),
                    EquatableArray.From(updAfter),
                    false,
                    "",
                    IsUpdateInto: true,
                    UpdateTargetParameterName: method.Parameters[1].Name,
                    UpdateReturnsVoid: method.ReturnsVoid,
                    MaxDepth: policy.MaxDepth,
                    // Both flags were left at their defaults here while every other endpoint set them. That
                    // was invisible until update-into became ambient-registerable, at which point the
                    // registration gate rejected every merge method for types that are plainly public.
                    ParameterIsPublicType: IsEffectivelyPublic(updSrc),
                    ReturnIsPublicType: IsEffectivelyPublic(updTgt),
                    Withheld: withheld,
                    ParameterTypeSignature: updSrc.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    // Two independently-annotated slots from two different symbols: the returning form
                    // `partial Dst Update(Src s, Dst? d)` annotates the parameter and not the return, so one
                    // string written into both would trade the parameter's CS8611 for a CS8819 on the return.
                    ReturnTypeSignature: DeclaredReturnSignature(method),
                    UpdateTargetTypeSignature: updTgt.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    ReturnIsNullableRef: DeclaresNullableRefReturn(method)));
                return true;
            }


            return false;
        }

        /// <summary>
        ///     True when a pair-scoped directive or hook targets EXACTLY the given (src, tgt) element
        ///     pair (round 29, T0.2 fix-round-1, Important #1). <see cref="NestedMappingRegistry.GetOrReserve" />
        ///     is keyed purely by the type pair, so the ONE synthesized <c>__DwarfMap_Obj_*</c> helper it hands
        ///     back for this pair is shared by every route that reaches it — a class-level
        ///     <c>[MapIgnore&lt;T&gt;]</c>, <c>[MapProperty&lt;S,T&gt;]</c>, or <c>[MapValue&lt;T&gt;]</c>, or a
        ///     <c>[BeforeMap]</c>/<c>[AfterMap]</c> hook whose parameter types match this pair, is baked into
        ///     THAT NAME's body (see <c>DrainNestedMappingQueue</c>'s nested-pair hook wiring, which applies the
        ///     identical <c>[BeforeMap]</c>/<c>[AfterMap]</c> match rule used below). A block copy bypasses the
        ///     helper entirely, so it would silently skip whatever any of these customizes.
        /// </summary>
        /// <remarks>
        ///     Round 29 T0.2 fix-round-2 (Important #2): a QUERY, not a decision — <see cref="AnyPairIgnore" />/
        ///     <see cref="AnyPairProp" />/<see cref="AnyPairValue" /> are the non-mutating siblings of
        ///     <see cref="MatchPairIgnores" />/<see cref="MatchPairProps" />/<see cref="MatchPairValues" />
        ///     (same match rule, factored into <see cref="IsPairTargetMatch" />/<see cref="IsPairPropMatch" /> so
        ///     there is exactly one definition of "matches" for both the mutating builder and this query). Calling
        ///     the MUTATING form here marked a pair-scoped attribute <c>Consumed</c> merely because THIS gate
        ///     asked about it — even on a pair where nothing else ever applies it (e.g. a plain
        ///     <c>int → long</c> span map beside a stray <c>[MapIgnore&lt;long&gt;("X")]</c>, or a customized
        ///     pair whose blit this gate refuses because a user converter already owns construction and never
        ///     reads the ignore at all) — which silenced the DWARF056 "matched no pair" sweep for a directive
        ///     that, in truth, matched nothing. Asking must not have that side effect.
        ///     <para>
        ///         Round 29 T0.2c: TWO callers now, which is why the name lost its <c>Span</c>. The span endpoint
        ///         calls it directly, just below; the array/list arm reaches it through
        ///         <see cref="NestedMappingRegistry.PairIsCustomized" />, which <c>ExtractCore</c> wires to this
        ///         method — the arm sits inside <c>TryResolveConversion</c>, which never sees
        ///         <see cref="MapperDeclarations" />. One rule, asked from two places, so neither can drift.
        ///     </para>
        /// </remarks>
        private static bool ElementPairHasCustomization(
            MapperDeclarations decls,
            Compilation compilation,
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem)
        {
            return DescribeElementPairCustomization(decls, compilation, srcElem, tgtElem) is not null;
        }

        /// <summary>
        ///     The rule itself: how the pair-scoped directive or hook that customizes this element pair should be
        ///     NAMED to a user, or <see langword="null" /> when nothing customizes it.
        ///     <see cref="ElementPairHasCustomization" /> is the boolean derived from it, so the gates that only
        ///     need yes/no and the diagnostic that has to name the thing cannot disagree about what counts as
        ///     customization.
        ///     <para>
        ///         <c>What</c> is a complete noun phrase, built here because this is the only place that knows
        ///         WHICH kind matched and therefore how it relates to the pair — a pair-scoped attribute is
        ///         <em>declared for</em> the pair, whereas a <c>[BeforeMap]</c>/<c>[AfterMap]</c> hook is merely
        ///         <em>matched to</em> it by implicit conversion of its parameter types (see the loops below, and
        ///         <c>DrainNestedMappingQueue</c>'s identical rule). Saying a hook was "declared for" the pair
        ///         would overstate the scoping to a reader trying to find it. <c>Verb</c> is the gerund that
        ///         reads correctly for that kind: an attribute is APPLIED, a hook is RUN.
        ///     </para>
        ///     <para>
        ///         Round 29 T0.2d: DWARF106 was reported only when the element pair resolved to a user
        ///         CONVERSION, so <c>[Reinterpret]</c> overriding a pair-scoped directive or a hook — the same
        ///         intentional bypass, the same invisible consequence — was silent. Widening it needed the
        ///         directive named in the message, which is the only reason this returns a phrase rather than a
        ///         bool. Whichever check matches FIRST names the message; the order below is the gate's own, and
        ///         a pair carrying two directives is customized either way.
        ///     </para>
        /// </summary>
        private static (string What, string Verb)? DescribeElementPairCustomization(
            MapperDeclarations decls,
            Compilation compilation,
            ITypeSymbol srcElem,
            ITypeSymbol tgtElem)
        {
            // The element pair, spelled the way the user wrote it. Built once: every phrase below names it.
            var pair = "'" + srcElem.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "' → '" +
                       tgtElem.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "'";

            if (AnyPairIgnore(decls.PairIgnores, tgtElem))
            {
                return ("the pair-scoped [MapIgnore<T>] declared for " + pair, "applying");
            }

            if (AnyPairProp(decls.PairProps, srcElem, tgtElem))
            {
                return ("the pair-scoped [MapProperty<S,T>] declared for " + pair, "applying");
            }

            if (AnyPairValue(decls.PairValues, tgtElem))
            {
                return ("the pair-scoped [MapValue<T>] declared for " + pair, "applying");
            }

            // Round 29 T0.2c review fix 1. A pair-scoped [MapConstructor<S,T>] delegates CONSTRUCTION of the
            // element to a user factory, which a block copy plainly does not call. It is asked here, with the
            // other pair-scoped directives, rather than through the "would the resolver adopt a user
            // conversion?" question, because under Preserve/SetNull that question correctly answers NO: the
            // declared pair method cannot accept the shared DwarfRefContext, so PrefersSynthesizedObjectMap
            // routes the element through the synthesized __DwarfMap_Obj_* helper — and it is THAT helper which
            // DrainNestedMappingQueue wires the factory into. The blit therefore skipped the factory in exactly
            // the two modes where the declared-method rule looked like it had the case covered.
            //
            // Scoped to DECLARED pairs, the identical scoping the factory wiring itself applies: a
            // [MapConstructor] naming a pair no [GenerateMap<S,T>] declares is honoured by nobody and reported
            // by DWARF056, and refusing a fast path for a directive that changes nothing would make that
            // diagnostic untrue.
            if (decls.GenPairs.Exists(gp => SymbolEqualityComparer.Default.Equals(gp.Src, srcElem) && SymbolEqualityComparer.Default.Equals(gp.Tgt, tgtElem)) &&
                AnyPairConstructor(decls.PairConstructors, srcElem, tgtElem))
            {
                return ("the pair-scoped [MapConstructor<S,T>] declared for " + pair, "applying");
            }

            // Same match rule as DrainNestedMappingQueue's nested-pair hook wiring: a [BeforeMap] whose parameter
            // the source implicitly converts to, or a [AfterMap] whose parameter(s) the source/target implicitly
            // convert to, applies to this pair and must run — which a block copy would silently skip.
            foreach (var h in decls.BeforeHookDefs)
                if (HasImplicitConversion(compilation, srcElem, h.ParamType))
                {
                    return ("the [BeforeMap] hook matching " + pair, "running");
                }

            foreach (var h in decls.AfterHookDefs)
            {
                var applies = h.P1 is null
                    ? HasImplicitConversion(compilation, tgtElem, h.P0)
                    : HasImplicitConversion(compilation, srcElem, h.P0) && HasImplicitConversion(compilation, tgtElem, h.P1);
                if (applies)
                {
                    return ("the [AfterMap] hook matching " + pair, "running");
                }
            }

            return null;
        }

        // ── Zero-alloc span map: void Map(ReadOnlySpan<S>/Span<S> src, Span<D> dst) ──
        // Maps element-wise into a caller-provided destination buffer (no allocation). The
        // destination must be a writable Span<D>; a too-small destination throws (never silent
        // truncation). The element conversion reuses the full resolution pipeline.
        /// <returns>
        ///     <see langword="true" /> when this endpoint claimed the method and no further endpoint should
        ///     look at it. Falling through to <see langword="false" /> is the "not mine" answer, which is what
        ///     the original code expressed by simply not returning.
        /// </returns>
        private static bool TryHandleSpanMap(
            IMethodSymbol method,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            LocationInfo? methodLocation,
            int methodDiagStart)
        {
            bool withheld;

            if (method.ReturnsVoid &&
                method.Parameters.Length == 2 &&
                TryGetSpanElement(method.Parameters[0].Type, out var spanSrcElem, out _) &&
                TryGetSpanElement(method.Parameters[1].Type,
                    out var spanDstElem,
                    out var dstIsReadOnly) &&
                !dstIsReadOnly)
            {
                var spanComp = ctx.SemanticModel.Compilation;
                var spanAutoNest = ReadMethodAutoNest(method, policy.ClassAutoNest);
                // Before the element-wise gate and before resolution, so a directive read only at another
                // endpoint is reported whatever else this method turns out to be wrong about.
                ReportDirectivesNotReadHere(method,
                    spanComp,
                    spanSrcElem,
                    spanDstElem,
                    MapEndpointKind.SpanMap,
                    methodLocation,
                    acc.Diagnostics);
                // Class-site liveness only (method: null): a class-wide ignore naming a member of the ELEMENT
                // pair is DWARF090's to report, so it must not also read as dead at the class site.
                JudgeUnscopedIgnores(decls.ClassIgnores,
                    acc.LiveClassIgnores,
                    null,
                    spanDstElem,
                    spanComp,
                    policy.AllowNonPublic,
                    methodLocation,
                    acc.Diagnostics,
                    acc.IgnorableNamesMemo);
                if (ReportElementWiseDirectiveGaps(method,
                        decls.ClassSymbol,
                        spanSrcElem,
                        spanDstElem,
                        policy.ExplicitOnly,
                        spanComp,
                        policy.AllowNonPublic,
                        methodLocation,
                        acc.Diagnostics))
                {
                    return true;
                }

                if (!TryResolveConversion(spanComp,
                        spanSrcElem,
                        spanDstElem,
                        null,
                        decls.AllMethods,
                        decls.MapperMethods,
                        policy.EnumPolicy,
                        acc.Synthesized,
                        policy.NullStrategy,
                        methodLocation,
                        method.Name,
                        acc.Diagnostics,
                        out var spanConv,
                        out var spanNull,
                        out var spanNeedsCtx,
                        out _,
                        spanAutoNest,
                        acc.NestedRegistry,
                        // Reservation is mapper-wide: a converter dedicated by Use=, or a [MapConstructor]
                        // factory, must not be adopted as this element's converter either.
                        reservedConverters: decls.MapperReservedConverters))
                    // Element pair not mappable → diagnostic (e.g. DWARF005) already added.
                {
                    return true;
                }

                if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
                {
                    acc.ElementPairsOwedCoverage.Add(
                        (spanSrcElem, spanDstElem, methodLocation, ReadIgnoreSources(method).ToList()));
                }

                // Zero-alloc span map, blit fast path (round 29, T0.2): the proof is the decision, independent
                // of what the resolver above picked for spanConv (typically a synthesized __DwarfMap_Obj_*
                // element mapper) — mirrors the array/list arm at MapperExtractor.Conversions.Arms.cs:382-383.
                // The synthesized element converter stays in the accumulator either way: resolution still ran
                // for its completeness (DWARF001), directive-gap and coverage side effects, it is simply unused
                // by the emitted body when the blit fires.
                //
                // Round 29 T0.2 fix-round-1 (Important #1): the proof alone is NOT the whole decision. The
                // registry that names spanConv is keyed purely by (srcElem, tgtElem) — GetOrReserve returns the
                // SAME __DwarfMap_Obj_* name no matter which route reached the pair — so a user-declared
                // converter, or a pair-scoped [MapIgnore<T>]/[MapProperty<S,T>]/[MapValue<T>], or a
                // [BeforeMap]/[AfterMap] hook that matches this element pair, is baked into what THAT NAME
                // does, not into a different name the blit could safely bypass. Blitting past any of those
                // is a silent behaviour change, not a speed-up.
                //
                // Round 29 T0.2 fix-round-2 (Important #1, continued): IsSynthesized alone was too wide — it
                // also admits __DwarfMap_UserConv_*, the shim UserConversionConverter wraps a user's OWN
                // implicit/explicit operator in (reachable here whenever auto-nest is off, e.g. [AutoNest(false)],
                // so the auto-nest arm never claims the pair and the user-operator arm does instead). That is
                // exactly as customizable as a hand-written element converter and must not be blitted past.
                // Excluded explicitly via GeneratedNames.IsUserConv rather than narrowed to IsObjectMap alone:
                // CanReinterpretEnums resolves through EnumConverter's OWN synthesized helpers (__DwarfMap_EnumVal_*
                // / EnumNum_* / NumEnum_*, verified empirically — an enum-by-value span map blits today), which
                // are byte-identical CreateChecked identity conversions with NO customization surface (no
                // MapIgnore/MapProperty/MapValue/hook mechanism reaches an enum arm at all); narrowing to
                // IsObjectMap-only would have silently made the CanReinterpretEnums half of the OR below
                // permanently unreachable for span maps — the same class of dead-condition defect this whole
                // gate exists to avoid. `spanConv is null` is kept for parity with the array arm's unconditional
                // check and is believed UNREACHABLE in combination with a true CanReinterpret/CanReinterpretEnums
                // verdict: identity is refused up front (BlittableProof.CanReinterpret's own early-out), two
                // DISTINCT types are never implicitly convertible in C# without a user-defined operator, and
                // HasImplicitConversion (the earlier resolver arm that would produce a null spanConv) explicitly
                // excludes user-defined conversions (`!conversion.IsUserDefined`) — so a user operator always
                // resolves via the UserConv arm above, never via a null spanConv. Kept anyway, defensively, on
                // the same "prove it, don't assume it" footing as every other gate in this file.
                var spanIsDefaultConverter = spanConv is null ||
                                             (GeneratedNames.IsSynthesized(spanConv) && !GeneratedNames.IsUserConv(spanConv));
                var spanPairIsCustomized = ElementPairHasCustomization(decls, spanComp, spanSrcElem, spanDstElem);
                var spanBlits = spanIsDefaultConverter && !spanPairIsCustomized &&
                                (BlittableProof.CanReinterpret(spanSrcElem, spanDstElem) ||
                                 BlittableProof.CanReinterpretEnums(spanSrcElem, spanDstElem, policy.EnumPolicy.Strategy));
                // Round 29 T0.2c: the near-miss follows the PROOF, and is silenced only by a user conversion
                // owning the pair — not by spanPairIsCustomized. A pair-scoped [MapProperty<S,T>] rename is
                // exactly the caller this diagnostic is for: they reconciled the names by hand, and renaming the
                // field really is what would hand them the block copy. Suppressing it there deleted a hint the
                // array arm has emitted (and pinned) since DWARF100 was introduced; the two arms now say the
                // same thing. A non-rename directive cannot produce a false hint: TryExplainNearMiss answers
                // false for a pair that already lines up by name.
                if (!spanBlits &&
                    spanIsDefaultConverter &&
                    BlittableProof.TryExplainNearMiss(spanSrcElem, spanDstElem, out var spanNearMissReason))
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.BlitNearMiss,
                        methodLocation,
                        $"'{method.Name}' maps a span whose element types are nearly layout-identical, so it " +
                        $"takes the element-by-element copy: {spanNearMissReason}",
                        MemberName: method.Name));
                }

                // Round 29 T0.2b: mirrors CollectionConverter.Synthesize's own srcElemIsNullableRef computation
                // — a nullable-annotated REFERENCE element (ReadOnlySpan<C?>) reaching a synthesized object
                // helper needs the same null-forgiving '!' the collection converter's element loop applies
                // (the helper null-guards internally: null in, null out). Read by MapEmitter.SpanMap's call
                // into the shared CollectionConverter.ElementExpr.
                var spanSrcElemIsNullableRef = spanSrcElem.IsReferenceType &&
                                                spanSrcElem.NullableAnnotation == NullableAnnotation.Annotated;

                var spanElemMember = new MemberMap(
                    "",
                    "",
                    spanConv,
                    spanNull,
                    spanNeedsCtx,
                    SourceIsNullableRef: spanSrcElemIsNullableRef,
                    // Round 29 T2.9, exactly as the async-stream endpoint one screen up: IsSynthesized is blind
                    // to a user-declared element map, so ReadOnlySpan<Child?> -> Span<ChildDto> through a
                    // declared ToDto emitted a bare, un-forgiven call. Forgiven and reported through the one
                    // coupled decision every other edge uses.
                    ConverterParamIsNonNullableRef: ForgiveNestedNullableArg(spanConv,
                        spanSrcElem,
                        spanDstElem,
                        decls.MapperMethods,
                        decls.AllMethods,
                        method.Name,
                        methodLocation,
                        acc.Diagnostics,
                        NullSourceKind.CollectionElement),
                    ConverterReturnIsNullableRef: ForgiveConverterNullableReturn(spanConv,
                        spanDstElem,
                        decls.MapperMethods,
                        decls.AllMethods,
                        method.Name,
                        methodLocation,
                        acc.Diagnostics,
                        NullSourceKind.CollectionElement));

                // I17: an unmapped destination member is a statement about THIS method's pair and THIS
                // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
                // model is still recorded, because the class-level analyses downstream ask what the mapper
                // DECLARES (see MapMethodModel.Withheld).
                withheld = TryScopeCompletenessRefusalToItsMethod(
                    acc.Diagnostics,
                    methodDiagStart,
                    method.Name,
                    methodLocation);

                acc.Methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    // Nullable-aware format (round 29 T0.2b): a plain FullyQualifiedFormat silently drops the
                    // '?' on a nullable-annotated REFERENCE type argument (Span<D?> → "global::T.D"), so the
                    // generated partial's declared parameter type stopped matching the user's own partial
                    // declaration — CS8611 in the consumer's build, on the signature itself, before element
                    // resolution is even reached. Same format CollectionConverter uses for its own helper
                    // parameter types, for the same reason.
                    method.Parameters[1].Type.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    method.Parameters[0].Name,
                    false,
                    EquatableArray.From(new[]
                    {
                        spanElemMember
                    }),
                    EquatableArray.From(Array.Empty<string>()),
                    EquatableArray.From(Array.Empty<HookCall>()),
                    false,
                    "",
                    IsSpanMap: true,
                    SpanTargetParameterName: method.Parameters[1].Name,
                    SpanMapBlits: spanBlits,
                    SpanSourceElementFullName: spanSrcElem.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    SpanTargetElementFullName: spanDstElem.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                    // The create and update models carry MaxDepth and these did not, which was an omission
                    // relative to its siblings. Since B33 it is READ: when the element converter carries the
                    // (ctx, depth) tail, EmitElementContext sizes the shared DwarfRefContext from this value,
                    // and a cyclic element under None throws DwarfMappingDepthException at exactly this depth
                    // (pinned in ElementWiseReferenceHandlingRuntimeTests). The MaxDepth OPTION divergence in
                    // DeclaredDivergences.Reasons["MaxDepth"] is still open for the non-ctx-tailed case — a
                    // non-recursive element pair creates no context, so the option changes nothing there.
                    MaxDepth: policy.MaxDepth,
                    Withheld: withheld));
                return true;
            }


            return false;
        }

        // ── Source-member coverage (RequiredMapping = Both) ───────────────────────────
        // The source-side mirror of the DWARF001 completeness gate: under `Both`, every readable
        // source member must be read by some destination (member OR constructor argument). A source
        // consumed by nothing surfaces DWARF039 (Info suggestion), unless suppressed by
        // [MapIgnoreSource]. Dotted source names (flattened leaves) mark their root consumed.
        /// <remarks>
        ///     The only one of the four middle phases the compiler certified as separable. Brace-scoping each
        ///     in place and building is what asked the question: the other three declare locals the rest of the
        ///     method still needs — nine of them in the collection/dictionary stage — so those seams mark
        ///     stages of a pipeline that accumulates a shared working set, not independent phases.
        /// </remarks>
        private static void ReportSourceMemberCoverage(
            IMethodSymbol method,
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            ITypeSymbol sourceType,
            INamedTypeSymbol targetType,
            List<MemberMap> members,
            IReadOnlyList<MemberMap> ctorArgs,
            IReadOnlyList<FlattenGraphDirective> resolvedFgDirectives,
            List<string> extraParamSig,
            LocationInfo? methodLocation,
            int methodDiagStart)
        {
            bool withheld;
            if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
            {
                EmitSourceCoverage(
                    sourceType,
                    members,
                    ctorArgs,
                    decls.ClassIgnoreSources,
                    ReadIgnoreSources(method),
                    policy.IgnoreObsolete,
                    ctx.SemanticModel.Compilation,
                    policy.AllowNonPublic,
                    methodLocation,
                    acc.Diagnostics);
            }

            var applicableBefore = new List<string>();
            foreach (var h in decls.BeforeHookDefs)
                if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                {
                    applicableBefore.Add(h.Name);
                }

            var applicableAfter = new List<HookCall>();
            foreach (var h in decls.AfterHookDefs)
            {
                bool applies;
                bool takesSource;
                if (h.P1 is null)
                {
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P0);
                    takesSource = false;
                }
                else
                {
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
                    takesSource = true;
                }

                if (!applies)
                {
                    continue;
                }

                var targetIsValue = targetType.IsValueType;
                var targetIsRef = h.TargetRefKind == RefKind.Ref;

                if (targetIsValue && !targetIsRef)
                {
                    // Silent correctness bug: struct target passed by value — mutations would be lost.
                    acc.Diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AfterMapValueTargetByValue,
                        methodLocation,
                        targetType.Name));
                    // Skip: do not emit this hook.
                    continue;
                }

                if (RefHookTargetMismatches(targetType, h, methodLocation, acc.Diagnostics))
                {
                    continue;
                }

                applicableAfter.Add(new HookCall(h.Name, takesSource, targetIsRef));
            }

            // I17: an unmapped destination member is a statement about THIS method's pair and THIS
            // method's [MapIgnore] set, not about the mapper. The method is WITHHELD from emission; the
            // model is still recorded, because the class-level analyses downstream ask what the mapper
            // DECLARES (see MapMethodModel.Withheld).
            withheld = TryScopeCompletenessRefusalToItsMethod(
                acc.Diagnostics,
                methodDiagStart,
                method.Name,
                methodLocation);

            acc.Methods.Add(new MapMethodModel(
                method.Name,
                AccessibilityText(method.DeclaredAccessibility),
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.Parameters[0].Name,
                sourceType.IsReferenceType,
                EquatableArray.From(members),
                EquatableArray.From(applicableBefore),
                EquatableArray.From(applicableAfter),
                false,
                "",
                EquatableArray.From(ctorArgs),
                FlattenGraphDirectives: EquatableArray.From(resolvedFgDirectives.ToArray()),
                ExtraParameters: EquatableArray.From(extraParamSig.ToArray()),
                ParameterIsPublicType: IsEffectivelyPublic(sourceType),
                ReturnIsPublicType: IsEffectivelyPublic(targetType),
                // Left at its default (true) here for years — invisible until DWARF108's post-pass
                // started reading it for EVERY recursion-capable pair, public entries included, and a
                // struct-destination declared mapper under OnCycle=SetNull kept its on-stack guard
                // (CS0037: `return null!;` against a non-nullable value type). Same failure shape as
                // ParameterIsPublicType/ReturnIsPublicType two lines up: a flag nobody read until a new
                // feature did.
                ReturnIsReferenceType: targetType.IsReferenceType,
                Withheld: withheld,
                ParameterTypeSignature: sourceType.ToDisplayString(CollectionConverter.NullableFullyQualifiedFormat),
                ReturnTypeSignature: DeclaredReturnSignature(method),
                ReturnIsNullableRef: DeclaresNullableRefReturn(method)));
            acc.PublicMethodLocs[acc.Methods.Count - 1] = methodLocation;
        }

        // ── [GenerateMap<TSrc, TTgt>] — low-ceremony attribute-declared mappers ──────
        // For each [GenerateMap<S,T>] on the mapper class, synthesize a public `T Map(S)` overload
        // (EmitAsNonPartial → emitted as a full method, not a partial impl) with the SAME completeness
        // gate, conversions, nested/collection handling, constructor mapping, and hooks as a declared
        // partial mapper. Source/target types stay plain POCOs (no attributes on them) — migrating from
        // e.g. AutoMapper's CreateMap<A,B>() is a near-mechanical 1:1 replace with [GenerateMap<A,B>].
        // genPairs / genComp / genLoc are computed near the top of Extract, because element and member
        // resolution has to be able to SEE these pairs long before this loop emits them.
        // Indexed rather than a foreach because a co-located host's MEMBER directives bind to the pairs
        // POSITIONALLY, exactly as a [MapTo] source member's do to its targets — so which pair this iteration
        // is emitting is part of the config, not just a loop variable.
        /// <remarks>
        ///     Certified separable by bracing it in place and building: it declares nothing the rest of
        ///     <c>ExtractCore</c> still needs. The pair-collection stage above it shares twelve locals and the
        ///     registry drain below shares one, so this is the only clean cut of the three.
        /// </remarks>
        private static void ExtractGenerateMapPairs(
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            List<(ITypeSymbol Src, ITypeSymbol Tgt)> genPairs,
            Compilation genComp,
            LocationInfo? genLoc,
            Dictionary<int, HostPairDirectives> hostDirectives)
        {
            bool withheld;

            for (var genIndex = 0; genIndex < genPairs.Count; genIndex++)
            {
                var (genSrc, genTgt) = genPairs[genIndex];

                // Pair-scoped [MapProperty<S,T>] / [MapIgnore<T>] config for this declared pair.
                var (genExplicit, genExtras) = MatchPairProps(decls.PairProps, genSrc, genTgt);
                var genIgnores = new HashSet<string>(decls.ClassIgnores, IgnoreNameComparer);
                foreach (var im in MatchPairIgnores(decls.PairIgnores, genTgt)) genIgnores.Add(im);
                // Class-site liveness against this pair's target ([GenerateMap] pairs have no method site).
                JudgeUnscopedIgnores(decls.ClassIgnores,
                    acc.LiveClassIgnores,
                    null,
                    genTgt,
                    ctx.SemanticModel.Compilation,
                    policy.AllowNonPublic,
                    genLoc,
                    acc.Diagnostics,
                    acc.IgnorableNamesMemo);

                // Member-level [MapProperty("SourceMember")] / [MapIgnore] written on the co-located host itself.
                // Layered ON TOP of the pair-scoped config rather than instead of it: the two are different
                // placements of the same intent and a host may reasonably carry both. Empty for every other
                // shape, so nothing below this line behaves differently for a [DwarfMapper] class.
                Dictionary<string, string>? genFormats = null;
                if (hostDirectives.TryGetValue(genIndex, out var hostConfig))
                {
                    genExplicit.AddRange(hostConfig.Explicit);
                    genExtras.AddRange(hostConfig.Extras);
                    foreach (var hm in hostConfig.Ignores) genIgnores.Add(hm);
                    genFormats = hostConfig.StringFormats;
                }

                // Top-level collection/dictionary [GenerateMap<Coll, Coll>]: route through the collection/dict
                // converter (as a declared partial method does, see "Fix 1" above) instead of object-mapping the
                // target's members — which would e.g. flag List<T>.Capacity via DWARF001. The source may be ANY
                // IEnumerable<T> (custom user collections like a ConcurrentList<T> included), matching the
                // member-level collection handling.
                // An array target always takes this route, for the reason the declared path's isCollReturn gives.
                var genIsColl = genTgt is IArrayTypeSymbol || CollectionConverter.TryResolve(genTgt, genTgt, out _, out _, out _);
                var genIsDict = !genIsColl && DictionaryConverter.TryResolve(genTgt, genTgt, out _, out _, out _, out _, out _);

                // An ENUM target needs the same treatment, and for the same reason: it is a VALUE to convert,
                // not an object to construct. Without this, `[GenerateMap<SrcKind, DstKind>]` emitted
                // `return new DstKind { };` — an empty object initializer over an enum, which compiles, has no
                // members to flag, and silently returns the zero value while discarding the source entirely.
                // Green build, no diagnostic, every mapped value wrong.
                //
                // The conversion machinery was never the problem: the identical pair used as a MEMBER already
                // resolves correctly through the enum converter. Only this declared-pair path constructed
                // instead of converting.
                // Every VALUE-like target, not just enums. The enum case was found first, but the bug class is
                // "a declared top-level pair whose target is a value gets object-mapped instead of converted" —
                // and a follow-up audit caught [GenerateMap<int, long>] emitting `return new long { };` for
                // exactly the same reason. SpecialType covers the primitives, string, decimal, char and bool;
                // TypeKind.Enum covers the rest of the family.
                var genIsValueLike = genTgt.TypeKind == TypeKind.Enum || genTgt.SpecialType != SpecialType.None;

                if (genIsColl || genIsDict || genIsValueLike)
                {
                    var gResolved = TryResolveConversion(
                        genComp,
                        genSrc,
                        genTgt,
                        null,
                        decls.AllMethods,
                        // This pair is resolved as a WHOLE, so it must not be a candidate for its own conversion.
                        ExcludingPair(decls.MapperMethods, genSrc, genTgt),
                        policy.EnumPolicy,
                        acc.Synthesized,
                        policy.NullStrategy,
                        genLoc,
                        "Map",
                        acc.Diagnostics,
                        out var gConv,
                        out _,
                        out var gNeedsCtx,
                        out _,
                        policy.ClassAutoNest,
                        acc.NestedRegistry,
                        policy.NullCollections == NullCollectionsBehavior.AsNull,
                        policy.IsPreserveMode,
                        isSetNull: policy.IsSetNullMode,
                        implicitConversions: policy.ImplicitConversions,
                        // Without this the ELEMENT conversion for a collection pair can adopt a method
                        // dedicated to one pair — a [MapConstructor] factory over the same types matches by
                        // signature and wins, so the loop constructs each element and assigns nothing.
                        reservedConverters: decls.MapperReservedConverters);

                    if (!gResolved || gConv is null)
                    {
                        continue; // element/shape diagnostic already reported by the recursive call
                    }

                    var gMember = new MemberMap(
                        "",
                        "", // sentinel: emit helper(param), not helper(param.Member)
                        gConv,
                        ConverterNeedsDepthCtx: gNeedsCtx);

                    // I17 STOPS HERE, and the boundary is a measurement rather than a preference. Withholding a
                    // method is only safe while its DECLARATION survives: a partial method is declared by the
                    // CONSUMER, so a sibling that maps a nested member through it still BINDS and the single
                    // CS8795 is the whole cost. A [GenerateMap] pair has no declaration — the generator is the
                    // only source of the symbol — so withholding it made a sibling's `N = Map(o.N)` emit
                    // **CS0103, 'the name Map does not exist'**, in a file the consumer cannot edit: the
                    // EmittedInvalidCode genre, whose ceiling is exactly zero. Measured on a two-pair probe
                    // before this line was written. So the pair keeps the whole-class kill, and the CS8795 it
                    // costs a sibling stays: loud collateral beats generated code that does not compile.
                    withheld = false;

                    acc.Methods.Add(new MapMethodModel(
                        "Map",
                        "public",
                        genTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        genSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        "src",
                        genSrc.IsReferenceType,
                        EquatableArray.From(new[]
                        {
                            gMember
                        }),
                        EquatableArray.From(Array.Empty<string>()),
                        EquatableArray.From(Array.Empty<HookCall>()),
                        false,
                        "",
                        EquatableArray.From(Array.Empty<MemberMap>()),
                        true,
                        genTgt.IsReferenceType,
                        IsTopLevelCollectionConversion: true,
                        EmitAsNonPartial: true,
                        ParameterIsPublicType: IsEffectivelyPublic(genSrc),
                        ReturnIsPublicType: IsEffectivelyPublic(genTgt),
                        Withheld: withheld));
                    acc.PublicMethodLocs[acc.Methods.Count - 1] = genLoc;
                    continue;
                }

                // Every array target was claimed by the conversion route above.
                var genTgtNamed = (INamedTypeSymbol)genTgt;

                // Pair-scoped [MapConstructor<S,T>(factory)] override: delegate construction to a user factory
                // method and only populate settable members afterward (AutoMapper ConstructUsing semantics).
                string? genFactory = null;
                foreach (var pc in decls.PairConstructors)
                {
                    if (!IsPairCtorMatch(pc, genSrc, genTgt))
                    {
                        continue;
                    }

                    pc.Consumed = true;
                    var factory = decls.AllMethods.FirstOrDefault(m =>
                        string.Equals(m.Name, pc.Method, StringComparison.Ordinal) && HasImplicitConversion(genComp, genSrc, m.ParamType) && HasImplicitConversion(genComp, m.ReturnType, genTgt));
                    if (factory.Name is null)
                    {
                        acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapConstructorInvalid,
                            pc.Loc,
                            $"[MapConstructor<{genSrc.ToDisplayString()}, {genTgt.ToDisplayString()}>(\"{pc.Method}\")] factory was not found or has an incompatible signature (it must take the source type '{genSrc.ToDisplayString()}' and return the destination type '{genTgt.ToDisplayString()}')"));
                    }
                    else
                    {
                        genFactory = factory.Name;
                    }

                    break;
                }

                MemberMap[] genCtorArgs;
                HashSet<string> genConsumed;
                HashSet<string> genRequiredInit;

                // Whether the chosen constructor already satisfies every `required` member. Tracked separately
                // from genRequiredInit because the selected ctor is scoped to the pattern below and DWARF079 asks
                // a different question of it — see CtorSetsRequiredMembers.
                var genCtorSetsRequired = false;
                HashSet<string>? genFactoryExcluded = null;

                if (genFactory is not null)
                {
                    // Factory builds the object; only settable members are assigned afterward, so init-only /
                    // required members are excluded (the factory owns them) and there are no ctor args.
                    genCtorArgs = Array.Empty<MemberMap>();
                    genConsumed = CollectFactoryExcludedMembers(genTgtNamed);
                    genRequiredInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // Kept separately from genConsumed so DWARF080 can tell "the ctor assigns it" (no loss) from
                    // "the factory owns it and the source value is dropped" (silent loss).
                    genFactoryExcluded = genConsumed;
                }
                else if (ConstructorSelector.Select(ctx.SemanticModel.Compilation,
                             genTgtNamed,
                             acc.Diagnostics,
                             genLoc,
                             out var genObjInitOnly,
                             policy.AllowNonPublic,
                             genSrc,
                             genExplicit) is not { } genCtor)
                {
                    continue;
                }
                else if (genObjInitOnly)
                {
                    genCtorSetsRequired = CtorSetsRequiredMembers(genCtor);
                    genCtorArgs = Array.Empty<MemberMap>();
                    genConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    genRequiredInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    if (!ResolveConstructorArguments(genCtor,
                            genSrc,
                            genComp,
                            genLoc,
                            acc.Diagnostics,
                            policy.CaseInsensitive,
                            policy.AllowNonPublic,
                            genExplicit,
                            decls.AllMethods,
                            decls.MapperMethods,
                            policy.EnumPolicy,
                            acc.Synthesized,
                            policy.NullStrategy,
                            policy.ClassAutoNest,
                            acc.NestedRegistry,
                            out genCtorArgs,
                            out genConsumed,
                            policy.NullCollections == NullCollectionsBehavior.AsNull,
                            policy.IsPreserveMode,
                            policy.IsSetNullMode,
                            policy.ImplicitConversions))
                    {
                        continue;
                    }

                    genRequiredInit = ComputeRequiredMustInitialize(genCtor, genTgtNamed, genConsumed);
                    genCtorSetsRequired = CtorSetsRequiredMembers(genCtor);
                }

                var genMembers = ResolveMembers(
                    genSrc,
                    genTgtNamed,
                    genIgnores,
                    genComp,
                    genLoc,
                    acc.Diagnostics,
                    new MapperOptions(
                        CaseInsensitive: policy.CaseInsensitive,
                        AutoNest: policy.ClassAutoNest,
                        NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                        IsPreserve: policy.IsPreserveMode,
                        IsSetNull: policy.IsSetNullMode,
                        ImplicitConversions: policy.ImplicitConversions,
                        NameConvention: 0,
                        // No method: a [GenerateMap] pair is declared by the class, so there is no
                        // method-scoped annotation that could speak for it.
                        SkipNullSourceMembers: ResolveNullSkip(decls.PairNullSkips, null, genSrc, genTgt, policy.SkipNullSrc),
                        AllowNonPublic: policy.AllowNonPublic,
                        ExplicitOnly: policy.ExplicitOnly,
                        IgnoreObsolete: policy.IgnoreObsolete),
                    genExplicit,
                    decls.AllMethods,
                    decls.MapperMethods,
                    policy.EnumPolicy,
                    acc.Synthesized,
                    policy.NullStrategy,
                    Array.Empty<string>(),
                    new List<string>(),
                    genConsumed,
                    genRequiredInit,
                    acc.NestedRegistry,
                    MatchPairValues(decls.PairValues, genTgt),
                    decls.ValueProviders,
                    mapPropertyExtras: genExtras,
                    // StringFormat rides on the SAME [MapProperty] the rename does, so a path that reads the
                    // directive and does not thread this drops the format in silence — D20 in miniature.
                    stringFormats: genFormats,
                    mapperReservedConverters: decls.MapperReservedConverters,
                    requiredMembersAlreadySatisfied: genCtorSetsRequired,
                    factoryExcludedMembers: genFactoryExcluded,
                    ignoredSourceMembers: ClassIgnoredSources(decls));

                var genBefore = new List<string>();
                foreach (var h in decls.BeforeHookDefs)
                    if (HasImplicitConversion(genComp, genSrc, h.ParamType))
                    {
                        genBefore.Add(h.Name);
                    }

                var genAfter = new List<HookCall>();
                foreach (var h in decls.AfterHookDefs)
                {
                    bool applies;
                    bool takesSource;
                    if (h.P1 is null)
                    {
                        applies = HasImplicitConversion(genComp, genTgt, h.P0);
                        takesSource = false;
                    }
                    else
                    {
                        applies = HasImplicitConversion(genComp, genSrc, h.P0) &&
                                  HasImplicitConversion(genComp, genTgt, h.P1);
                        takesSource = true;
                    }

                    if (!applies)
                    {
                        continue;
                    }

                    var tIsRef = h.TargetRefKind == RefKind.Ref;
                    if (genTgt.IsValueType && !tIsRef)
                    {
                        continue;
                    }

                    if (RefHookTargetMismatches(genTgt, h, genLoc, acc.Diagnostics))
                    {
                        continue;
                    }

                    genAfter.Add(new HookCall(h.Name, takesSource, tIsRef));
                }

                // I17 STOPS HERE, and the boundary is a measurement rather than a preference. Withholding a
                // method is only safe while its DECLARATION survives: a partial method is declared by the
                // CONSUMER, so a sibling that maps a nested member through it still BINDS and the single
                // CS8795 is the whole cost. A [GenerateMap] pair has no declaration — the generator is the
                // only source of the symbol — so withholding it made a sibling's `N = Map(o.N)` emit
                // **CS0103, 'the name Map does not exist'**, in a file the consumer cannot edit: the
                // EmittedInvalidCode genre, whose ceiling is exactly zero. Measured on a two-pair probe
                // before this line was written. So the pair keeps the whole-class kill, and the CS8795 it
                // costs a sibling stays: loud collateral beats generated code that does not compile.
                withheld = false;

                acc.Methods.Add(new MapMethodModel(
                    "Map",
                    "public",
                    genTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    genSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "src",
                    genSrc.IsReferenceType,
                    EquatableArray.From(genMembers),
                    EquatableArray.From(genBefore),
                    EquatableArray.From(genAfter),
                    false,
                    "",
                    EquatableArray.From(genCtorArgs),
                    true,
                    genTgt.IsReferenceType,
                    EmitAsNonPartial: true,
                    ParameterIsPublicType: IsEffectivelyPublic(genSrc),
                    ReturnIsPublicType: IsEffectivelyPublic(genTgt),
                    FactoryMethod: genFactory,
                    Withheld: withheld));
                acc.PublicMethodLocs[acc.Methods.Count - 1] = genLoc;
            }

        }


        // ── Drain the NestedMappingRegistry queue ────────────────────────────────
        // User-declared partial methods are already registered in mapperMethods (autoCandidates).
        // We process synthesized pairs AFTER declared methods so user methods always win.
        // Each dequeued pair may enqueue further pairs → loop until empty (terminates because
        // each pair is registered-before-built, so revisits hit the memoization branch).
        // We also track dependency edges (nestedRegistry.SetCurrentPair) so that after the
        // drain we can compute which pairs are recursion-capable (Plan 19 C1).
        // Temporary list: collect models before we know their IsRecursionCapable flag.
        /// <remarks>
        ///     <paramref name="pendingNestedModels" /> is owned by the caller: this phase FILLS it and the
        ///     recursion-cycle phase reads it. Bracing the span in place named that one escaping local before
        ///     anything moved, which is the check this round added after three boundary bugs found the other
        ///     way round.
        /// </remarks>
        private static void DrainNestedMappingQueue(
            GeneratorAttributeSyntaxContext ctx,
            MapperDeclarations decls,
            MapperPolicy policy,
            MapperAccumulators acc,
            List<(ITypeSymbol Src, ITypeSymbol Tgt)> genPairs,
            Compilation genComp,
            List<(MapMethodModel Model, string MethodName)> pendingNestedModels,
            CancellationToken ct)
        {

            while (acc.NestedRegistry.HasPending)
            {
                ct.ThrowIfCancellationRequested();

                var (nestedSrc, nestedTgt, nestedName, pairAutoNest, nestedOrigin) = acc.NestedRegistry.Dequeue();

                // Pair-scoped member config for this acc.Synthesized pair (empty when none declared on the class), so a
                // [MapProperty<S,T>] rename applies even when S -> T is mapped as a nested/collection element.
                var (nestedExplicit, nestedExtras) = MatchPairProps(decls.PairProps, nestedSrc, nestedTgt);
                var nestedIgnores = MatchPairIgnores(decls.PairIgnores, nestedTgt);

                // Inform the registry that we are now building this pair's body,
                // so subsequent GetOrReserve calls record edges in the dependency graph.
                acc.NestedRegistry.SetCurrentPair(nestedName);

                // The diagnostic anchor for everything this pair's resolution reports (DWARF001/005/025/038/070,
                // …): the site that first requested the pair — the declared method whose member reached it, or
                // for a deeper pair the anchor its requester was built under, since the requester resolves its
                // members at this very location and threads it into GetOrReserve. C3 intended this ("use the first
                // declared method's location"), ISSUE-012 found the code that was supposed to compute it dead and
                // recorded null as the contract — which put every nested-pair error on the project node instead
                // of a line, with a remedy ("annotate the method") that names nothing the reader can find.
                LocationInfo? nestedLocation = nestedOrigin;

                // A helper acc.Synthesized for a pair the class ALSO declares must construct it the way the declared
                // pair does. Under Preserve/SetNull the element route is REQUIRED to be a acc.Synthesized helper —
                // calling the public method from a collection helper would allocate a fresh DwarfRefContext per
                // element and lose the identity map — so this is the one place the two routes can still diverge
                // after element resolution learned to reuse declared pairs. Left alone, `Map(node)` ran the
                // factory and `Map(node).Kids[0]` did not.
                //
                // Scoped to DECLARED pairs deliberately: a [MapConstructor] naming a pair with no [GenerateMap]
                // is already refused by DWARF056 ("matches no pair"), and quietly honouring it here would make
                // that diagnostic untrue.
                string? nestedFactory = null;
                if (genPairs.Exists(gp => SymbolEqualityComparer.Default.Equals(gp.Src, nestedSrc) && SymbolEqualityComparer.Default.Equals(gp.Tgt, nestedTgt)))
                {
                    foreach (var pc in decls.PairConstructors)
                    {
                        if (!IsPairCtorMatch(pc, nestedSrc, nestedTgt))
                        {
                            continue;
                        }

                        // A factory that does not resolve is already reported against the declared pair; saying
                        // it twice, once without a usable location, would only add noise.
                        var nestedFactorySym = decls.AllMethods.FirstOrDefault(m =>
                            string.Equals(m.Name, pc.Method, StringComparison.Ordinal) && HasImplicitConversion(genComp, nestedSrc, m.ParamType) && HasImplicitConversion(genComp, m.ReturnType, nestedTgt));
                        if (nestedFactorySym.Name is not null)
                        {
                            nestedFactory = nestedFactorySym.Name;
                        }

                        break;
                    }
                }

                // Choose construction strategy for the nested target type.
                IMethodSymbol? nestedCtor = null;
                var nestedObjInitOnly = false;
                if (nestedFactory is null)
                {
                    nestedCtor = ConstructorSelector.Select(ctx.SemanticModel.Compilation,
                        nestedTgt,
                        acc.Diagnostics,
                        nestedLocation,
                        out nestedObjInitOnly,
                        policy.AllowNonPublic,
                        nestedSrc,
                        nestedExplicit);
                    if (nestedCtor is null)
                    {
                        // DWARF025/026 already reported; skip body emission for this pair.
                        acc.NestedRegistry.ClearCurrentPair();
                        continue;
                    }
                }

                MemberMap[] nestedCtorArgs;
                HashSet<string> nestedConsumed;
                HashSet<string> nestedRequiredMustInit;
                HashSet<string>? nestedFactoryExcluded = null;

                if (nestedFactory is not null)
                {
                    // The factory builds the object; only settable members are assigned afterwards, so init-only
                    // and required members are excluded — the factory owns them. Same shape as the declared path.
                    nestedCtorArgs = Array.Empty<MemberMap>();
                    nestedConsumed = CollectFactoryExcludedMembers(nestedTgt);
                    nestedRequiredMustInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    nestedFactoryExcluded = nestedConsumed;
                }
                else if (nestedObjInitOnly)
                {
                    nestedCtorArgs = Array.Empty<MemberMap>();
                    nestedConsumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    nestedRequiredMustInit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    // C1: use the per-pair autoNest value (pairAutoNest), NOT policy.ClassAutoNest.
                    if (!ResolveConstructorArguments(nestedCtor!,
                            nestedSrc,
                            ctx.SemanticModel.Compilation,
                            nestedLocation,
                            acc.Diagnostics,
                            policy.CaseInsensitive,
                            policy.AllowNonPublic,
                            nestedExplicit,
                            decls.AllMethods,
                            decls.MapperMethods,
                            policy.EnumPolicy,
                            acc.Synthesized,
                            policy.NullStrategy,
                            pairAutoNest,
                            acc.NestedRegistry,
                            out nestedCtorArgs,
                            out nestedConsumed,
                            policy.NullCollections == NullCollectionsBehavior.AsNull,
                            policy.IsPreserveMode,
                            policy.IsSetNullMode,
                            policy.ImplicitConversions))
                    {
                        acc.NestedRegistry.ClearCurrentPair();
                        continue;
                    }

                    nestedRequiredMustInit = ComputeRequiredMustInitialize(nestedCtor!, nestedTgt, nestedConsumed);
                }

                // C1: use the per-pair autoNest value (pairAutoNest), NOT policy.ClassAutoNest.
                var nestedMembers = ResolveMembers(
                    nestedSrc,
                    nestedTgt,
                    nestedIgnores, // pair-scoped [MapIgnore<T>] (empty when none declared)
                    ctx.SemanticModel.Compilation,
                    nestedLocation,
                    acc.Diagnostics,
                    new MapperOptions(
                        CaseInsensitive: policy.CaseInsensitive,
                        AutoNest: pairAutoNest,
                        NullAsNull: policy.NullCollections == NullCollectionsBehavior.AsNull,
                        IsPreserve: policy.IsPreserveMode,
                        IsSetNull: policy.IsSetNullMode,
                        ImplicitConversions: policy.ImplicitConversions,
                        NameConvention: 0,
                        // A acc.Synthesized nested pair honours its own [MapNullSkip<S,T>] if the author declared
                        // one, otherwise the enclosing class's policy. Without the pair-scoped lookup the
                        // enclosing class's value is the ONLY input, which is how one logical nested pair
                        // reached from two classes ended up with opposite null semantics.
                        SkipNullSourceMembers: ResolveNullSkip(decls.PairNullSkips, null, nestedSrc, nestedTgt, policy.SkipNullSrc),
                        AllowNonPublic: policy.AllowNonPublic,
                        // FALSE on purpose: this is the auto-acc.Synthesized NESTED mapper. Explicit-only guards
                        // the TOP-LEVEL trust boundary; reaching a nested pair already required the developer
                        // to map that edge explicitly (top-level auto-nest is blocked by DWARF072), so the
                        // nested contents map normally. Propagating it would give every nested member DWARF072
                        // — a acc.Synthesized mapper has no [MapProperty] to satisfy it — making nested objects
                        // unmappable. For a nested trust boundary, declare that pair's own
                        // [DwarfMapper(AutoMatchMembers = false)] mapper.
                        ExplicitOnly: false,
                        // IgnoreObsolete DOES propagate, unlike ExplicitOnly: skipping an obsolete nested
                        // member just leaves it at its default — safe and consistent, with no "unmappable"
                        // hazard.
                        IgnoreObsolete: policy.IgnoreObsolete),
                    nestedExplicit, // pair-scoped [MapProperty<S,T>] (empty when none declared)
                    decls.AllMethods,
                    decls.MapperMethods,
                    policy.EnumPolicy,
                    acc.Synthesized,
                    policy.NullStrategy,
                    new List<string>(),
                    new List<string>(), // no flatten/reinterpret
                    nestedConsumed,
                    nestedRequiredMustInit,
                    acc.NestedRegistry,
                    MatchPairValues(decls.PairValues, nestedTgt),
                    decls.ValueProviders,
                    mapPropertyExtras: nestedExtras,
                    // A acc.Synthesized nested mapper must not adopt a dedicated converter either — the author
                    // never wrote this pair, so they certainly did not offer it one.
                    mapperReservedConverters: decls.MapperReservedConverters,
                    requiredMembersAlreadySatisfied: nestedCtor is not null && CtorSetsRequiredMembers(nestedCtor),
                    factoryExcludedMembers: nestedFactoryExcluded,
                    ignoredSourceMembers: ClassIgnoredSources(decls));

                // Only the pairs registered above — a genuinely NESTED member pair is deliberately left alone,
                // because source coverage has never applied at depth and turning it on for every acc.Synthesized pair
                // would be a broad behavioural change rather than closing this gap.
                foreach (var owed in acc.ElementPairsOwedCoverage)
                    if (SymbolEqualityComparer.Default.Equals(owed.Src, nestedSrc) && SymbolEqualityComparer.Default.Equals(owed.Tgt, nestedTgt))
                    {
                        EmitSourceCoverage(
                            nestedSrc,
                            nestedMembers,
                            null,
                            decls.ClassIgnoreSources,
                            owed.IgnoreSources,
                            policy.IgnoreObsolete,
                            ctx.SemanticModel.Compilation,
                            policy.AllowNonPublic,
                            owed.Loc,
                            acc.Diagnostics);
                        break;
                    }

                acc.NestedRegistry.ClearCurrentPair();

                // Hooks ([BeforeMap]/[AfterMap]) bound to THIS pair must also run when the pair is mapped as a
                // nested member or collection element — otherwise a target produced via the private helper silently
                // skips its post-processing (e.g. an AfterMap that rebuilds a dictionary), a data-loss bug.
                // Match by the same implicit-conversion rule the public pairs use (see ~line 835).
                var nestedBefore = new List<string>();
                foreach (var h in decls.BeforeHookDefs)
                    if (HasImplicitConversion(ctx.SemanticModel.Compilation, nestedSrc, h.ParamType))
                    {
                        nestedBefore.Add(h.Name);
                    }

                var nestedAfter = new List<HookCall>();
                foreach (var h in decls.AfterHookDefs)
                {
                    bool applies;
                    bool takesSource;
                    if (h.P1 is null)
                    {
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, nestedTgt, h.P0);
                        takesSource = false;
                    }
                    else
                    {
                        applies = HasImplicitConversion(ctx.SemanticModel.Compilation, nestedSrc, h.P0) && HasImplicitConversion(ctx.SemanticModel.Compilation, nestedTgt, h.P1);
                        takesSource = true;
                    }

                    if (!applies)
                    {
                        continue;
                    }

                    var nestedTargetIsRef = h.TargetRefKind == RefKind.Ref;
                    // Struct target passed by value would lose the hook's mutations; skip it here (the public /
                    // update-into path for the same pair surfaces the AfterMapValueTargetByValue diagnostic).
                    if (nestedTgt.IsValueType && !nestedTargetIsRef)
                    {
                        continue;
                    }

                    if (RefHookTargetMismatches(nestedTgt, h, nestedLocation, acc.Diagnostics))
                    {
                        continue;
                    }

                    nestedAfter.Add(new HookCall(h.Name, takesSource, nestedTargetIsRef));
                }

                // Build a private (non-partial) MapMethodModel for this acc.Synthesized pair.
                // IsRecursionCapable is set to false here and patched below after ComputeRecursionCapability().
                var nestedModel = new MapMethodModel(
                    nestedName,
                    "private",
                    nestedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    nestedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "s",
                    nestedSrc.IsReferenceType,
                    EquatableArray.From(nestedMembers),
                    EquatableArray.From(nestedBefore),
                    EquatableArray.From(nestedAfter),
                    false,
                    "",
                    EquatableArray.From(nestedCtorArgs),
                    false,
                    nestedTgt.IsReferenceType, // patched below
                    FactoryMethod: nestedFactory);

                pendingNestedModels.Add((nestedModel, nestedName));
            }

        }
    }
}
