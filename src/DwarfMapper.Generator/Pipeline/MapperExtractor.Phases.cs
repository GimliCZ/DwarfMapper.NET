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
    }
}
