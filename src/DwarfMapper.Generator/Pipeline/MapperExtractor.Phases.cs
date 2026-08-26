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
        // ── OnCycle = SetNull post-processing (None mode) ────────────────────────
        // After recursion-capability is finalised, flag every recursion-capable method so the
        // emitter wraps its body in the on-stack guard (TryEnterNode/ExitNode) and the public
        // entry allocates DwarfRefContext(maxDepth, setNull: true). Only reference-type pairs
        // can form a reference cycle, so value-type sources are left untouched (they keep the
        // plain depth-guarded None body — a struct cannot be its own ancestor on the stack).
        // This is the None-mode analogue of the Preserve post-pass above, but far simpler:
        // construction is unchanged (no register-before-populate, no DWARF030, no dispatch
        // wrapper) — the guard only nulls a re-entrant back-edge.
        private static void ApplySetNullPostPass(List<MapMethodModel> methods, bool isSetNullMode)
        {
            if (!isSetNullMode)
            {
                return;
            }

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

                    if (!synthesized.ContainsKey(wrapperName))
                    {
                        var wrapperCode = BuildDispatchWrapperCode(m, wrapperName);
                        synthesized[wrapperName] = new SynthesizedMethod(wrapperName, wrapperCode);
                    }

                    dispatchWrapperByPublicName[m.MethodName] = wrapperName;
                    recursionCapableNames.Add(wrapperName);
                }

                // Patch: redirect members/ctor-args that call a public dispatch method to the wrapper.
                if (dispatchWrapperByPublicName.Count > 0)
                {
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
        private static void PropagateContextToPublicMethodsSecondPass(List<MapMethodModel> methods, HashSet<string> recursionCapableNames, HashSet<string> selfRecursivePublicMethods, int maxDepth, bool isPreserveMode)
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
                        allCallGraph[callerName].Add(mem.ConverterMethod);
                    }

                foreach (var arg in model.ConstructorArguments)
                    if (arg.ConverterMethod is not null)
                    {
                        allCallGraph[callerName].Add(arg.ConverterMethod);
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
                if (!allCallGraph.ContainsKey(callerKey))
                {
                    allCallGraph[callerKey] = new HashSet<string>(StringComparer.Ordinal);
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
                    if (declaredNameCount.TryGetValue(mem.ConverterMethod, out var oc) && oc > 1)
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

                    if (declaredNameCount.TryGetValue(arg.ConverterMethod, out var oc) && oc > 1)
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
                    if (em is not null && !(declaredNameCount.TryGetValue(em, out var oc) && oc > 1))
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
                    $"Source and target are the same type '{m.ParameterTypeFullName}', so '{m.MethodName}' just " + "emits a shallow copy of every member. This is usually a mistyped type argument — did you mean " + "a different target? If a shallow copy IS what you want, suppress DWARF076 here to say so."));
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
            var methodDiagStart = 0;
            var withheld = false;

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
                // Maps element-wise into a caller-provided destination buffer (no allocation). The
                // destination must be a writable Span<D>; a too-small destination throws (never silent
                // truncation). The element conversion reuses the full resolution pipeline.
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
                        return;
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
                            spanAutoNest,
                            acc.NestedRegistry,
                            // Reservation is mapper-wide: a converter dedicated by Use=, or a [MapConstructor]
                            // factory, must not be adopted as this element's converter either.
                            reservedConverters: decls.MapperReservedConverters))
                        // Element pair not mappable → diagnostic (e.g. DWARF005) already added.
                    {
                        return;
                    }

                    if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
                    {
                        acc.ElementPairsOwedCoverage.Add(
                            (spanSrcElem, spanDstElem, methodLocation, ReadIgnoreSources(method).ToList()));
                    }

                    var spanElemMember = new MemberMap(
                        "",
                        "",
                        spanConv,
                        spanNull,
                        spanNeedsCtx);

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
                        method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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
                        // The create and update models carry MaxDepth and these did not, which was an omission
                        // relative to its siblings. Since B33 it is READ: when the element converter carries the
                        // (ctx, depth) tail, EmitElementContext sizes the shared DwarfRefContext from this value,
                        // and a cyclic element under None throws DwarfMappingDepthException at exactly this depth
                        // (pinned in ElementWiseReferenceHandlingRuntimeTests). The MaxDepth OPTION divergence in
                        // DeclaredDivergences.Reasons["MaxDepth"] is still open for the non-ctx-tailed case — a
                        // non-recursive element pair creates no context, so the option changes nothing there.
                        MaxDepth: policy.MaxDepth,
                        Withheld: withheld));
                    return;
                }

                // ── Update-into-existing: void/T Map(S src, T dest) ─────────────────────
                // Maps onto an EXISTING reference-type target instance (no construction; identity kept).
                // Return is void OR the destination type. v1 is None-semantics (no Preserve/SetNull on the
                // update method itself) and reference-type targets only (a struct dest by value can't
                // observe mutations). Resolution reuses ResolveMembers; only the emission differs.
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
                        requiredMembersAlreadySatisfied: true);

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
                        updAfter.Add(new HookCall(h.Name, takesSource, h.TargetRefKind == RefKind.Ref));
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
                        Withheld: withheld));
                    return;
                }

                // ── Async streaming map: IAsyncEnumerable<D> Map(IAsyncEnumerable<S> src) ──
                // Emitted as an async iterator (await foreach … yield return conv(item)) that lazily
                // transforms the source sequence — preserves streaming/back-pressure, no buffering.
                // A trailing CancellationToken is accepted (and required to be honoured): without it, nothing the
                // consumer passes to `WithCancellation` can ever reach this iterator, so the stream is uncancellable.
                // The generated half must match the user's partial signature exactly, so the token only exists when
                // the user declared it.
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
                        return;
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
                            asAutoNest,
                            acc.NestedRegistry,
                            reservedConverters: decls.MapperReservedConverters))
                    {
                        return; // element pair not mappable → diagnostic already added
                    }

                    if (policy.RequiredMapping == 1) // RequiredMappingStrategy.Both
                    {
                        acc.ElementPairsOwedCoverage.Add(
                            (asSrcElem, asDstElem, methodLocation, ReadIgnoreSources(method).ToList()));
                    }

                    var asElemMember = new MemberMap(
                        "",
                        "",
                        asConv,
                        asNull,
                        asNeedsCtx);

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
                        Withheld: withheld));
                    return;
                }

                // A construction mapper has the source as parameter 0 and may declare ADDITIONAL parameters
                // (Phase 5) used as extra named value sources — so allow >= 1, not exactly 1.
                if (method.ReturnsVoid || method.Parameters.Length < 1)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                        methodLocation,
                        method.Name));
                    return;
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
                        EmitDWARF028(acc.Diagnostics,
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
                        return;
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
                        EmitDWARF028(acc.Diagnostics,
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
                        policy.ReferenceHandling,
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
                        ReadMapValues(method));

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
                        return;
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
                        ProjectionMembers: EquatableArray.From(projMembers.ToArray())));
                    return;
                }

                if (method.ReturnType is not INamedTypeSymbol targetType)
                {
                    acc.Diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod,
                        methodLocation,
                        method.Name));
                    return;
                }

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
                var extraParams = new List<(string Name, ITypeSymbol Type)>();
                var extraParamSig = new List<string>();
                for (var pi = 1; pi < method.Parameters.Length; pi++)
                {
                    var ep = method.Parameters[pi];
                    extraParams.Add((ep.Name, ep.Type));
                    extraParamSig.Add(ep.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " " + ep.Name);
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
                var rawDerivedPairs = ReadDerivedTypeAttributes(method, ctx.SemanticModel.Compilation);
                if (rawDerivedPairs.Count > 0 && !isHeteroFlattenGraph)
                {
                    var resolvedArms =
                        new List<(INamedTypeSymbol Src, INamedTypeSymbol Tgt, string ConverterMethod, bool NeedsCtx)>();
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
                            a.NeedsCtx))
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
                        Withheld: withheld));
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
                var isCollReturn = CollectionConverter.TryResolve(targetType,
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
                    var tlResolved = TryResolveConversion(
                        ctx.SemanticModel.Compilation,
                        sourceType,
                        targetType,
                        null,
                        decls.AllMethods,
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
                        Withheld: withheld));
                    // A top-level collection map's element pair is acc.Synthesized (pair-scoped config only), so
                    // this method consumes no unscoped ignore this walk can see — class-site DWARF095 stands
                    // down for the class rather than guess (see the flag's declaration).
                    classIgnoreLivenessBlinded = true;
                    return;
                }
                // ── End Fix 1 ────────────────────────────────────────────────────────────────

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
                    targetType,
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
                        targetType,
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
                        policy.NullCollections == NullCollectionsBehavior.AsNull,
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
                    requiredMustInitialize = ComputeRequiredMustInitialize(ctor, targetType, consumedParams);
                }

                var members = ResolveMembers(
                    sourceType,
                    targetType,
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
                    CtorSetsRequiredMembers(ctor));

                // Append FlattenGraph-injected member maps (traversal helper calls).
                // These come AFTER normal members so the object initializer order is:
                //   normal scalars/nested first, then flat-graph collections.
                members.AddRange(fgInjectedMembers);

                // ── Source-member coverage (RequiredMapping = Both) ───────────────────────────
                // The source-side mirror of the DWARF001 completeness gate: under `Both`, every readable
                // source member must be read by some destination (member OR constructor argument). A source
                // consumed by nothing surfaces DWARF039 (Info suggestion), unless suppressed by
                // [MapIgnoreSource]. Dotted source names (flattened leaves) mark their root consumed.
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
                    Withheld: withheld));
                acc.PublicMethodLocs[acc.Methods.Count - 1] = methodLocation;
        }
    }
}
