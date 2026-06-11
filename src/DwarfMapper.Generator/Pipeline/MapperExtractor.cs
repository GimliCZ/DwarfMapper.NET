// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Pipeline;

internal enum NullStrategy
{
    Throw = 0,
    SetDefault = 1,
}

internal enum NullCollectionsBehavior
{
    AsEmpty = 0,
    AsNull  = 1,
}

internal static class MapperExtractor
{
    public static MapperClassModel Extract(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;
        var classSyntax = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = new List<DiagnosticInfo>();

        var isPartial = classSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MapperNotPartial,
                LocationInfo.From(classSyntax.Identifier.GetLocation()),
                classSymbol.Name));
        }

        var classIgnores = ReadIgnores(classSymbol).ToList();
        var caseInsensitive = ReadCaseInsensitive(ctx.Attributes);
        var enumStrategy = ReadEnumStrategy(ctx.Attributes);
        var nullStrategy = ReadNullStrategy(ctx.Attributes);
        var classAutoNest = ReadAutoNest(ctx.Attributes);
        var nullCollections = ReadNullCollections(ctx.Attributes);
        var maxDepth = ReadMaxDepth(ctx.Attributes);
        var referenceHandling = ReadReferenceHandling(ctx.Attributes);
        var isPreserveMode = referenceHandling == 1; // 1 = ReferenceHandlingStrategy.Preserve
        var synthesized = new Dictionary<string, SynthesizedMethod>(System.StringComparer.Ordinal);
        var allMethods = CollectMethods(classSymbol);
        var mapperMethods = CollectMapperMethods(classSymbol);
        var (beforeHookDefs, afterHookDefs) = CollectHooks(classSymbol, diagnostics);
        var methods = new List<MapMethodModel>();

        // NestedMappingRegistry: local to this Extract call (contains ISymbol — never stored in model).
        var nestedRegistry = new NestedMappingRegistry();

        foreach (var method in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (method.MethodKind != MethodKind.Ordinary || !method.IsPartialDefinition)
            {
                continue;
            }

            var methodLocation = LocationInfo.From(method.Locations.FirstOrDefault() ?? Location.None);

            if (method.ReturnsVoid || method.Parameters.Length != 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            if (IsQueryable(method.ReturnType, out var projTarget)
                && IsQueryable(method.Parameters[0].Type, out var projSource)
                && projTarget is INamedTypeSymbol projTargetNamed)
            {
                if (!HasAccessibleParameterlessCtor(projTargetNamed))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoParameterlessConstructor, methodLocation, projTargetNamed.Name));
                    continue;
                }
                var projIgnores = new HashSet<string>(classIgnores);
                foreach (var i in ReadIgnores(method)) { projIgnores.Add(i); }
                var projMembers = ResolveProjectionMembers(
                    projSource, projTargetNamed, projIgnores, ctx.SemanticModel.Compilation,
                    methodLocation, diagnostics, caseInsensitive, ReadExplicitMaps(method));

                methods.Add(new MapMethodModel(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Parameters[0].Name,
                    true,
                    EquatableArray.From(projMembers),
                    EquatableArray.From(System.Array.Empty<string>()),
                    EquatableArray.From(System.Array.Empty<HookCall>()),
                    true,
                    projTargetNamed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                continue;
            }

            if (method.ReturnType is not INamedTypeSymbol targetType)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidMapMethod, methodLocation, method.Name));
                continue;
            }

            var sourceType = method.Parameters[0].Type;

            // Choose construction strategy for the target type.
            var ctor = ConstructorSelector.Select(targetType, diagnostics, methodLocation, out var objInitOnly);
            if (ctor is null)
            {
                continue;
            }

            var ignores = new HashSet<string>(classIgnores);
            foreach (var i in ReadIgnores(method))
            {
                ignores.Add(i);
            }

            var explicitMaps = ReadExplicitMaps(method);
            var flattenRoots = ReadFlattenRoots(method);
            var reinterpretMembers = ReadReinterpretMembers(method);
            var methodAutoNest = ReadMethodAutoNest(method, classAutoNest);

            // Resolve constructor arguments (empty set when objInitOnly).
            MemberMap[] ctorArgs;
            HashSet<string> consumedParams;
            // Members that are `required` AND satisfied via a ctor param but whose ctor is NOT annotated
            // [SetsRequiredMembers]: C# requires them to ALSO be set in the object initializer (CS9035).
            // These must NOT be excluded from the initializer even though they are in consumedParams.
            HashSet<string> requiredMustInitialize;
            if (objInitOnly)
            {
                ctorArgs = System.Array.Empty<MemberMap>();
                consumedParams = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                requiredMustInitialize = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(ctor, sourceType, ctx.SemanticModel.Compilation,
                    methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                    enumStrategy, synthesized, nullStrategy, methodAutoNest, nestedRegistry, out ctorArgs, out consumedParams,
                    nullCollections == NullCollectionsBehavior.AsNull))
                {
                    // At least one parameter was unmappable → DWARF024 already reported; skip emit.
                    continue;
                }

                // Compute which consumed-param members are `required` and whose ctor lacks [SetsRequiredMembers].
                // Those must still be emitted in the object initializer to satisfy the C# `required` rule.
                requiredMustInitialize = ComputeRequiredMustInitialize(ctor, targetType, consumedParams);
            }

            var members = ResolveMembers(
                sourceType, targetType, ignores, ctx.SemanticModel.Compilation,
                methodLocation, diagnostics, caseInsensitive, explicitMaps, allMethods, mapperMethods,
                enumStrategy, synthesized, nullStrategy, flattenRoots, reinterpretMembers,
                consumedParams, requiredMustInitialize, methodAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull);

            var applicableBefore = new List<string>();
            foreach (var h in beforeHookDefs)
            {
                if (HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.ParamType))
                {
                    applicableBefore.Add(h.Name);
                }
            }
            var applicableAfter = new List<HookCall>();
            foreach (var h in afterHookDefs)
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
                    applies = HasImplicitConversion(ctx.SemanticModel.Compilation, sourceType, h.P0)
                        && HasImplicitConversion(ctx.SemanticModel.Compilation, targetType, h.P1);
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
                    diagnostics.Add(new DiagnosticInfo(
                        DiagnosticDescriptors.AfterMapValueTargetByValue,
                        methodLocation,
                        targetType.Name));
                    // Skip: do not emit this hook.
                    continue;
                }

                applicableAfter.Add(new HookCall(h.Name, takesSource, TargetByRef: targetIsRef));
            }

            methods.Add(new MapMethodModel(
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
                EquatableArray.From(ctorArgs)));
        }

        // ── Drain the NestedMappingRegistry queue ────────────────────────────────
        // User-declared partial methods are already registered in mapperMethods (autoCandidates).
        // We process synthesized pairs AFTER declared methods so user methods always win.
        // Each dequeued pair may enqueue further pairs → loop until empty (terminates because
        // each pair is registered-before-built, so revisits hit the memoization branch).
        // We also track dependency edges (nestedRegistry.SetCurrentPair) so that after the
        // drain we can compute which pairs are recursion-capable (Plan 19 C1).
        // Temporary list: collect models before we know their IsRecursionCapable flag.
        var pendingNestedModels = new List<(MapMethodModel Model, string MethodName)>();

        while (nestedRegistry.HasPending)
        {
            ct.ThrowIfCancellationRequested();

            var (nestedSrc, nestedTgt, nestedName) = nestedRegistry.Dequeue();

            // Inform the registry that we are now building this pair's body,
            // so subsequent GetOrReserve calls record edges in the dependency graph.
            nestedRegistry.SetCurrentPair(nestedName);

            // We use the location of the first declared method as the diagnostic anchor
            // for nested diagnostics (acceptable per spec).
            var nestedLocation = methods.Count > 0 ? methods[0].Members.Count > 0
                ? (LocationInfo?)null : null : null;

            // Choose construction strategy for the nested target type.
            var nestedCtor = ConstructorSelector.Select(nestedTgt, diagnostics, nestedLocation, out var nestedObjInitOnly);
            if (nestedCtor is null)
            {
                // DWARF025/026 already reported; skip body emission for this pair.
                nestedRegistry.ClearCurrentPair();
                continue;
            }

            MemberMap[] nestedCtorArgs;
            HashSet<string> nestedConsumed;
            HashSet<string> nestedRequiredMustInit;

            if (nestedObjInitOnly)
            {
                nestedCtorArgs = System.Array.Empty<MemberMap>();
                nestedConsumed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                nestedRequiredMustInit = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                if (!ResolveConstructorArguments(nestedCtor, nestedSrc, ctx.SemanticModel.Compilation,
                    nestedLocation, diagnostics, caseInsensitive, System.Array.Empty<(string, string, string?)>(),
                    allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                    classAutoNest, nestedRegistry, out nestedCtorArgs, out nestedConsumed,
                    nullCollections == NullCollectionsBehavior.AsNull))
                {
                    nestedRegistry.ClearCurrentPair();
                    continue;
                }
                nestedRequiredMustInit = ComputeRequiredMustInitialize(nestedCtor, nestedTgt, nestedConsumed);
            }

            var nestedMembers = ResolveMembers(
                nestedSrc, nestedTgt,
                new HashSet<string>(System.StringComparer.Ordinal), // no ignores for synthesized
                ctx.SemanticModel.Compilation,
                nestedLocation, diagnostics, caseInsensitive,
                System.Array.Empty<(string, string, string?)>(), // no explicit maps
                allMethods, mapperMethods, enumStrategy, synthesized, nullStrategy,
                new List<string>(), new List<string>(), // no flatten/reinterpret
                nestedConsumed, nestedRequiredMustInit,
                classAutoNest, nestedRegistry,
                nullCollections == NullCollectionsBehavior.AsNull);

            nestedRegistry.ClearCurrentPair();

            // Build a private (non-partial) MapMethodModel for this synthesized pair.
            // IsRecursionCapable is set to false here and patched below after ComputeRecursionCapability().
            var nestedModel = new MapMethodModel(
                MethodName: nestedName,
                Accessibility: "private",
                ReturnTypeFullName: nestedTgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ParameterTypeFullName: nestedSrc.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ParameterName: "s",
                ParameterIsReferenceType: nestedSrc.IsReferenceType,
                Members: EquatableArray.From(nestedMembers),
                BeforeHooks: EquatableArray.From(System.Array.Empty<string>()),
                AfterHooks: EquatableArray.From(System.Array.Empty<HookCall>()),
                IsProjection: false,
                ElementTargetTypeFullName: "",
                ConstructorArguments: EquatableArray.From(nestedCtorArgs),
                IsPartial: false,
                ReturnIsReferenceType: nestedTgt.IsReferenceType,
                IsRecursionCapable: false); // patched below

            pendingNestedModels.Add((nestedModel, nestedName));
        }

        // ── Recursion-capability analysis ────────────────────────────────────────
        // Now that the full dependency graph is known, compute which pairs are on cycles.
        nestedRegistry.ComputeRecursionCapability();

        // Build a set of method names that are recursion-capable (for the public method check).
        var recursionCapableNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (model, name) in pendingNestedModels)
        {
            var isRC = nestedRegistry.IsRecursionCapable(name);
            if (isRC) recursionCapableNames.Add(name);
            // Rebuild the model with the correct IsRecursionCapable flag.
            methods.Add(model with { IsRecursionCapable = isRC });
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

        // Count declared methods per name to detect overloads.
        var declaredNameCount = new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;
            declaredNameCount.TryGetValue(m.MethodName, out var prev);
            declaredNameCount[m.MethodName] = prev + 1;
        }

        // Helper: get the graph key for a declared method.
        string DeclKey(MapMethodModel mm)
            => declaredNameCount.TryGetValue(mm.MethodName, out var cnt) && cnt > 1
               ? mm.MethodName + "§" + mm.ParameterTypeFullName
               : mm.MethodName;

        var allCallGraph = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);

        // Seed with synthesized method edges (already computed in registry, but not accessible here).
        // Re-derive from pending models (synthesized methods always have unique names).
        foreach (var (model, _) in pendingNestedModels)
        {
            var callerName = model.MethodName;
            if (!allCallGraph.ContainsKey(callerName))
                allCallGraph[callerName] = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var mem in model.Members)
                if (mem.ConverterMethod is not null)
                    allCallGraph[callerName].Add(mem.ConverterMethod);
            foreach (var arg in model.ConstructorArguments)
                if (arg.ConverterMethod is not null)
                    allCallGraph[callerName].Add(arg.ConverterMethod);
        }

        // Add declared methods to the graph using disambiguation keys.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;

            var callerKey = DeclKey(m);
            if (!allCallGraph.ContainsKey(callerKey))
                allCallGraph[callerKey] = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var mem in m.Members)
            {
                if (mem.ConverterMethod is null) continue;
                // Resolve the edge target: if the converter is an overloaded declared method,
                // we can't determine which overload without param-type info, so we add edges
                // to ALL overloads of that name.  For non-overloaded names and synthesized
                // names, add the name directly.
                if (declaredNameCount.TryGetValue(mem.ConverterMethod, out var oc) && oc > 1)
                {
                    // Add edges to all OTHER overloads (not the method itself — a converter can't be
                    // a self-call when it was auto-matched to a DIFFERENT overload by parameter type).
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, mem.ConverterMethod, System.StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, System.StringComparison.Ordinal))
                            allCallGraph[callerKey].Add(ovKey);
                    }
                }
                else
                {
                    allCallGraph[callerKey].Add(mem.ConverterMethod);
                }
            }
            foreach (var arg in m.ConstructorArguments)
            {
                if (arg.ConverterMethod is null) continue;
                if (declaredNameCount.TryGetValue(arg.ConverterMethod, out var oc) && oc > 1)
                {
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, arg.ConverterMethod, System.StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, System.StringComparison.Ordinal))
                            allCallGraph[callerKey].Add(ovKey);
                    }
                }
                else
                {
                    allCallGraph[callerKey].Add(arg.ConverterMethod);
                }
            }
        }

        // For synthesized methods calling overloaded declared methods, also expand edges
        // so DFS can follow the full cycle. If synth-method calls "Map" and there are
        // two overloads "Map§A" and "Map§B", add edges to all variants except self.
        foreach (var callerKey in allCallGraph.Keys.ToList())
        {
            var edges = allCallGraph[callerKey];
            var expandedEdges = new System.Collections.Generic.List<string>();
            foreach (var edge in edges)
            {
                if (declaredNameCount.TryGetValue(edge, out var oc) && oc > 1)
                {
                    // Replace simple name with qualified variants (excluding self to avoid false cycles).
                    for (var j = 0; j < methods.Count; j++)
                    {
                        var ov = methods[j];
                        if (!ov.IsPartial) continue;
                        if (!string.Equals(ov.MethodName, edge, System.StringComparison.Ordinal)) continue;
                        var ovKey = DeclKey(ov);
                        if (!string.Equals(ovKey, callerKey, System.StringComparison.Ordinal))
                            expandedEdges.Add(ovKey);
                    }
                }
                else
                {
                    expandedEdges.Add(edge);
                }
            }
            edges.Clear();
            foreach (var e in expandedEdges) edges.Add(e);
        }

        // Find which declared methods are on a cycle (can reach themselves in allCallGraph).
        var selfRecursivePublicMethods = new HashSet<string>(System.StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (!m.IsPartial) continue;

            var key = DeclKey(m);
            if (CanReach(allCallGraph, key, key))
            {
                selfRecursivePublicMethods.Add(m.MethodName);
                // Add the companion name so call-sites in synthesized methods can reference it.
                var companionName = "__DwarfMap_Depth_" + m.MethodName;
                recursionCapableNames.Add(companionName);
            }
        }

        // Re-check synthesized methods using the full allCallGraph (which includes declared methods).
        // The registry's edge graph only tracks synthesized→synthesized edges; it misses cycles that
        // go through declared methods (e.g. __DwarfMap_Obj_B → Map → __DwarfMap_Obj_B).
        // Any synthesized method on such a mixed cycle must also be recursion-capable.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];
            if (m.IsPartial) continue; // only synthesized methods

            if (!recursionCapableNames.Contains(m.MethodName)
                && CanReach(allCallGraph, m.MethodName, m.MethodName))
            {
                recursionCapableNames.Add(m.MethodName);
                // Re-mark the method model as recursion-capable.
                methods[i] = m with { IsRecursionCapable = true };
            }
        }

        // ── Mark public methods and synthesized methods that call recursion-capable pairs ─
        // The public Map(S s) method needs to create a DwarfRefContext if it calls (directly
        // or indirectly through its members) a recursion-capable synthesized pair.
        // We patch the already-added method models here.
        for (var i = 0; i < methods.Count; i++)
        {
            var m = methods[i];

            // For self-recursive declared methods: generate a companion and redirect.
            if (m.IsPartial && selfRecursivePublicMethods.Contains(m.MethodName))
            {
                var companionName = "__DwarfMap_Depth_" + m.MethodName;

                // Patch the members/ctor-args of the declared method:
                // (a) self-calls → redirect to companion with depth ctx
                // (b) calls to other self-recursive declared methods → redirect to their companions
                // (c) calls to recursion-capable synthesized methods → add depth ctx
                var newMembers2 = m.Members.ToArray();
                for (var mi = 0; mi < newMembers2.Length; mi++)
                {
                    var mem = newMembers2[mi];
                    if (mem.ConverterMethod is null) continue;

                    if (string.Equals(mem.ConverterMethod, m.MethodName, System.StringComparison.Ordinal))
                    {
                        // Self-call: redirect to companion.
                        newMembers2[mi] = mem with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    }
                    else if (selfRecursivePublicMethods.Contains(mem.ConverterMethod))
                    {
                        // Indirect recursive declared method: redirect to its companion.
                        newMembers2[mi] = mem with { ConverterMethod = "__DwarfMap_Depth_" + mem.ConverterMethod, ConverterNeedsDepthCtx = true };
                    }
                    else if (recursionCapableNames.Contains(mem.ConverterMethod))
                    {
                        // Recursion-capable synthesized method: pass depth ctx.
                        newMembers2[mi] = mem with { ConverterNeedsDepthCtx = true };
                    }
                }
                var newCtorArgs2 = m.ConstructorArguments.ToArray();
                for (var ci = 0; ci < newCtorArgs2.Length; ci++)
                {
                    var arg = newCtorArgs2[ci];
                    if (arg.ConverterMethod is null) continue;

                    if (string.Equals(arg.ConverterMethod, m.MethodName, System.StringComparison.Ordinal))
                    {
                        newCtorArgs2[ci] = arg with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    }
                    else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs2[ci] = arg with { ConverterMethod = "__DwarfMap_Depth_" + arg.ConverterMethod, ConverterNeedsDepthCtx = true };
                    }
                    else if (recursionCapableNames.Contains(arg.ConverterMethod))
                    {
                        newCtorArgs2[ci] = arg with { ConverterNeedsDepthCtx = true };
                    }
                }

                // Mark public method as needing ctx creation.
                methods[i] = m with
                {
                    IsRecursionCapable = true,
                    MaxDepth = maxDepth,
                    Members = EquatableArray.From(newMembers2),
                    ConstructorArguments = EquatableArray.From(newCtorArgs2),
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
                    ConstructorArguments = EquatableArray.From(newCtorArgs2),
                    // Companion's self-calls also use the companion (already patched above).
                };
                methods.Add(companion);
                continue;
            }

            // Check if any of this method's members/ctor-args uses a recursion-capable synthesized method
            // OR a self-recursive declared method (which must be redirected to the companion).
            var needsCtx = false;
            var newMembers = m.Members.ToArray();
            for (var mi = 0; mi < newMembers.Length; mi++)
            {
                var member = newMembers[mi];
                if (member.ConverterMethod is null) continue;

                if (recursionCapableNames.Contains(member.ConverterMethod))
                {
                    newMembers[mi] = member with { ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
                else if (selfRecursivePublicMethods.Contains(member.ConverterMethod))
                {
                    // Redirect call from declared public method to its depth-guarded companion.
                    var companionName = "__DwarfMap_Depth_" + member.ConverterMethod;
                    newMembers[mi] = member with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
            }

            var newCtorArgs = m.ConstructorArguments.ToArray();
            for (var ci = 0; ci < newCtorArgs.Length; ci++)
            {
                var arg = newCtorArgs[ci];
                if (arg.ConverterMethod is null) continue;

                if (recursionCapableNames.Contains(arg.ConverterMethod))
                {
                    newCtorArgs[ci] = arg with { ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
                else if (selfRecursivePublicMethods.Contains(arg.ConverterMethod))
                {
                    var companionName = "__DwarfMap_Depth_" + arg.ConverterMethod;
                    newCtorArgs[ci] = arg with { ConverterMethod = companionName, ConverterNeedsDepthCtx = true };
                    needsCtx = true;
                }
            }

            if (needsCtx || (m.IsRecursionCapable && !m.IsPartial))
            {
                methods[i] = m with
                {
                    IsRecursionCapable = m.IsRecursionCapable || needsCtx,
                    MaxDepth = m.IsPartial ? maxDepth : m.MaxDepth, // only public methods carry MaxDepth
                    Members = EquatableArray.From(newMembers),
                    ConstructorArguments = EquatableArray.From(newCtorArgs),
                };
            }
        }

        // ── Plan 19 C2: Preserve mode post-processing ───────────────────────────
        // After recursion-capability is finalised, propagate IsPreserveMode and detect DWARF030.
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
                    // Pattern A: explicit recursion-capable ctor arg.
                    foreach (var ctorArg in m.ConstructorArguments)
                    {
                        if (ctorArg.ConverterMethod is not null && ctorArg.ConverterNeedsDepthCtx)
                        {
                            var loc = (LocationInfo?)null;
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.CyclicConstructorParameter,
                                loc,
                                ctorArg.TargetName));
                        }
                    }

                    // Pattern B: self-map (S == T) with any ctor arg that has no converter.
                    // For S==T, direct-assignment ctor args copy the source reference into the target.
                    // If the source has a cycle (n.Next = n), the target's ctor arg will hold the source,
                    // not the target. Since the type is immutable (has ctor args), we can't fix this up.
                    // We only flag ctor args that are of reference type (not int/string/etc.) — but since
                    // we don't have type info here, we flag ALL ctor args when the method is S→S and
                    // recursion-capable (proven by members using ConverterNeedsDepthCtx).
                    // More precisely: the method must be recursion-capable to be affected.
                    if (m.IsRecursionCapable
                        && string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, System.StringComparison.Ordinal))
                    {
                        foreach (var ctorArg in m.ConstructorArguments)
                        {
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
                }

                // Only recursion-capable methods need the Preserve-mode register-before-populate emission.
                if (!m.IsRecursionCapable) continue;

                // Mark the method as Preserve mode.
                methods[i] = m with { IsPreserveMode = true };
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
                if (m.ConstructorArguments.Count == 0) continue;
                if (m.IsRecursionCapable) continue; // already handled above
                if (!m.ParameterIsReferenceType) continue; // value types excluded

                // S == T (same type) with ctor args and NOT recursion-capable:
                // This is the "record ImmutableNode(ImmutableNode? Next)" self-map case.
                // The method isn't recursion-capable because S=T uses implicit conversion (no auto-nest),
                // but at RUNTIME a cyclic ImmutableNode CAN exist. Under Preserve mode, this is
                // an unsupported pattern → DWARF030 for the cyclic ctor args.
                if (string.Equals(m.ParameterTypeFullName, m.ReturnTypeFullName, System.StringComparison.Ordinal))
                {
                    // Flag ctor args that have the same type as the source (cyclic back-edge).
                    // Since we don't have type info here, flag ALL non-scalar ctor args where
                    // the source name suggests it's a complex member (has a converter or is the cycle).
                    // Conservative approach: flag all ctor args with no converter when S==T.
                    // The scalar ctor args (int, string, etc.) would also get flagged — this is
                    // acceptable since the real issue is that ANY ctor arg in this scenario is suspect
                    // (the ENTIRE pattern of immutable S=T mapping with cycles is broken).
                    // In practice, DWARF030 is a COMPILE ERROR — the user MUST fix the type design.
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

        // Report DWARF031 if the registry cap was exceeded.
        if (nestedRegistry.CapExceeded)
        {
            // Use a null location — the cap is a generator-level limit, not method-specific.
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.DeepNestingLimit,
                null,
                nestedRegistry.CapTriggerType));
        }

        // CollectRoundTrips must be called before capturing diagnostics so that DWARF020/021 are included.
        var roundTrips = CollectRoundTrips(classSymbol, ctx.SemanticModel.Compilation, diagnostics);

        return new MapperClassModel(
            classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            AccessibilityText(classSymbol.DeclaredAccessibility),
            EquatableArray.From(methods),
            EquatableArray.From(diagnostics),
            EquatableArray.From(synthesized.Values.OrderBy(m => m.Name, System.StringComparer.Ordinal)),
            EquatableArray.From(roundTrips));
    }

    private static List<MemberMap> ResolveMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy, IReadOnlyList<string> flattenRoots, List<string> reinterpretMembers,
        HashSet<string>? consumedCtorParams = null,
        HashSet<string>? requiredMustInitialize = null,
        bool autoNest = false,
        NestedMappingRegistry? nestedRegistry = null,
        bool nullAsNull = false)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        var sourceGroups = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var writableByName = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType))
        {
            writableByName[m.Name] = m.Type;
        }

        var result = new List<MemberMap>();
        var handledTargets = new HashSet<string>(System.StringComparer.Ordinal);

        var comparerForLeaves = comparer; // same comparer used for member matching
        var flattenInfos = new List<(string Root, IReadOnlyList<(string Name, ITypeSymbol Type)> Leaves)>();
        foreach (var root in flattenRoots)
        {
            var match = ReadableMembers(sourceType)
                .Where(m => comparerForLeaves.Equals(m.Name, root))
                .Select(m => ((string Name, ITypeSymbol Type)?)m)
                .FirstOrDefault();
            if (match is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }
            var rootType = match.Value.Type;
            // Scalars (string, primitives, enums) are not flattenable roots — flattening their
            // BCL members (e.g. string.Length) is never intended and must not happen silently.
            if (rootType.SpecialType != SpecialType.None || rootType.TypeKind == TypeKind.Enum)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }
            var leaves = ReadableMembers(rootType).ToList();
            if (leaves.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.FlattenRootInvalid, location, root));
                continue;
            }
            flattenInfos.Add((match.Value.Name, leaves));
        }

        // EXPLICIT: [MapProperty] pairs take precedence and are matched by exact name.
        var explicitSeen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var (srcName, tgtName, useMethod) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                // More than one [MapProperty] for the same destination.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }

            handledTargets.Add(tgtName);

            // If this explicit mapping targets a constructor parameter (already consumed), skip it here
            // UNLESS the member is `required` and the ctor lacks [SetsRequiredMembers] — in that case
            // the member must also appear in the object initializer to satisfy CS9035.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(tgtName)
                && (requiredMustInitialize is null || !requiredMustInitialize.Contains(tgtName)))
            {
                continue;
            }

            if (ignores.Contains(tgtName))
            {
                // Contradictory: [MapIgnore] and [MapProperty] target the same member.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }

            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }

            var srcMatch = ReadableMembers(sourceType)
                .Where(m => System.StringComparer.Ordinal.Equals(m.Name, srcName))
                .Select(m => (ITypeSymbol?)m.Type)
                .FirstOrDefault();
            if (srcMatch is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }

            if (TryResolveConversion(compilation, srcMatch, tgtType, useMethod, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, tgtName, diagnostics, out var conv, out var nullH, autoNest, nestedRegistry, nullAsNull))
            {
                result.Add(new MemberMap(tgtName, srcName, conv, nullH));
            }
        }

        // AUTO: remaining writable targets matched by name under the comparer.
        var targets = WritableMembers(targetType)
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();
        foreach (var target in targets)
        {
            // Skip members already consumed as constructor parameters (positional record members appear
            // as both ctor params AND init properties — must not double-assign).
            // EXCEPTION: `required` members whose ctor lacks [SetsRequiredMembers] must also be set in
            // the object initializer (CS9035), so do NOT skip them.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(target.Name)
                && (requiredMustInitialize is null || !requiredMustInitialize.Contains(target.Name)))
            {
                continue;
            }

            if (handledTargets.Contains(target.Name) || ignores.Contains(target.Name))
            {
                continue;
            }

            if (!sourceGroups.TryGetValue(target.Name, out var matches))
            {
                var flatMatches = new List<(string Root, string Leaf, ITypeSymbol LeafType)>();
                foreach (var fi in flattenInfos)
                {
                    foreach (var leaf in fi.Leaves)
                    {
                        if (comparer.Equals(leaf.Name, target.Name))
                        {
                            flatMatches.Add((fi.Root, leaf.Name, leaf.Type));
                        }
                    }
                }

                if (flatMatches.Count > 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousFlatten, location, target.Name));
                    continue;
                }
                if (flatMatches.Count == 1)
                {
                    var fm = flatMatches[0];
                    if (TryResolveConversion(compilation, fm.LeafType, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var fconv, out var fnull, autoNest, nestedRegistry, nullAsNull))
                    {
                        result.Add(new MemberMap(target.Name, fm.Root + "." + fm.Leaf, fconv, fnull));
                    }
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }

            if (matches.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, target.Name));
                continue;
            }

            var source = matches[0];
            if (reinterpretMembers.Contains(target.Name))
            {
                if (source.Type is IArrayTypeSymbol sa && target.Type is IArrayTypeSymbol ta
                    && sa.ElementType.IsUnmanagedType && ta.ElementType.IsUnmanagedType)
                {
                    var blit = CollectionConverter.SynthesizeBlit(synthesized, source.Type, sa.ElementType, ta.ElementType);
                    result.Add(new MemberMap(target.Name, source.Name, blit));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid, location, target.Name));
                }
                continue;
            }
            if (TryResolveConversion(compilation, source.Type, target.Type, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, target.Name, diagnostics, out var conv, out var nullH, autoNest, nestedRegistry, nullAsNull))
            {
                result.Add(new MemberMap(target.Name, source.Name, conv, nullH));
            }
        }

        // READ-ONLY destinations with a matching source (silent-loss guard).
        // A read-only member satisfied via a constructor parameter is already mapped — no diagnostic.
        foreach (var readOnly in ReadOnlyMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (handledTargets.Contains(readOnly.Name) || ignores.Contains(readOnly.Name))
            {
                continue;
            }
            // Satisfied via ctor param → not a silent loss.
            if (consumedCtorParams is not null && consumedCtorParams.Contains(readOnly.Name))
            {
                continue;
            }
            if (sourceGroups.ContainsKey(readOnly.Name))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReadOnlyDestinationMember, location, readOnly.Name));
            }
        }

        // A [Reinterpret] name that matches no writable destination member is a typo — never silently ignore it.
        // A [Reinterpret] member that is ALSO in [MapIgnore] is a contradiction — report DWARF012.
        if (reinterpretMembers.Count > 0)
        {
            var writableNames = new HashSet<string>(WritableMembers(targetType).Select(m => m.Name), System.StringComparer.Ordinal);
            foreach (var rm in reinterpretMembers)
            {
                if (ignores.Contains(rm))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, rm));
                }
                else if (!writableNames.Contains(rm))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.ReinterpretInvalid, location, rm));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// For each constructor parameter, find a matching source member and resolve the conversion.
    /// Every parameter is mandatory — if any fails, DWARF024 is reported and the method returns false.
    /// </summary>
    private static bool ResolveConstructorArguments(
        IMethodSymbol ctor,
        ITypeSymbol sourceType,
        Compilation compilation,
        LocationInfo? location,
        List<DiagnosticInfo> diagnostics,
        bool caseInsensitive,
        IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy,
        Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy,
        bool autoNest,
        NestedMappingRegistry? nestedRegistry,
        out MemberMap[] ctorArgs,
        out HashSet<string> consumedParams,
        bool nullAsNull = false)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        // Build explicit-maps index: target (param) name → source name (exact match).
        var explicitForParams = new Dictionary<string, (string Source, string? Use)>(System.StringComparer.Ordinal);
        foreach (var (srcName, tgtName, use) in explicitMaps)
        {
            explicitForParams[tgtName] = (srcName, use);
        }

        var readableByName = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.ToList(), comparer);

        var args = new List<MemberMap>();
        // Case-insensitive set for deduplication (positional record param names can differ in case).
        consumedParams = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var allOk = true;

        foreach (var param in ctor.Parameters)
        {
            // 1. Check for an explicit [MapProperty(src, paramName)] override.
            if (explicitForParams.TryGetValue(param.Name, out var explicitInfo))
            {
                var srcList = ReadableMembers(sourceType)
                    .Where(m => System.StringComparer.Ordinal.Equals(m.Name, explicitInfo.Source))
                    .ToList();
                if (srcList.Count == 0)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, explicitInfo.Source));
                    allOk = false;
                    continue;
                }

                var srcType = srcList[0].Type;
                if (TryResolveConversion(compilation, srcType, param.Type, explicitInfo.Use,
                    allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy,
                    location, param.Name, diagnostics, out var eConv, out var eNull,
                    autoNest, nestedRegistry, nullAsNull))
                {
                    args.Add(new MemberMap(param.Name, explicitInfo.Source, eConv, eNull));
                    consumedParams.Add(param.Name);
                }
                else
                {
                    allOk = false;
                }
                continue;
            }

            // 2. Auto-match by name under the configured comparer.
            if (!readableByName.TryGetValue(param.Name, out var matches) || matches.Count == 0)
            {
                // No matching source member. If the parameter is OPTIONAL (author-declared default)
                // or a params array, omit it from the emitted call so C# supplies the default /
                // empty array. That honors the type author's intent and is not data loss — only a
                // MANDATORY unmatched parameter breaks completeness (DWARF024).
                if (param.HasExplicitDefaultValue || param.IsParams)
                {
                    // Account for it (positional record params also surface as init/get properties;
                    // marking consumed excludes the matching property from the object-initializer AND
                    // from completeness diagnostics) but do NOT add it to args — the emitted call omits
                    // it so C# supplies the declared default / empty params array.
                    consumedParams.Add(param.Name);
                    continue;
                }
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.ConstructorParameterUnmapped,
                    location, param.Name));
                allOk = false;
                continue;
            }

            if (matches.Count > 1)
            {
                // Ambiguous under case-insensitive matching — report as AmbiguousMatch.
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousMatch, location, param.Name));
                allOk = false;
                continue;
            }

            var srcMember = matches[0];
            if (TryResolveConversion(compilation, srcMember.Type, param.Type, null,
                allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy,
                location, param.Name, diagnostics, out var conv, out var nullH,
                autoNest, nestedRegistry, nullAsNull))
            {
                args.Add(new MemberMap(param.Name, srcMember.Name, conv, nullH));
                consumedParams.Add(param.Name);
            }
            else
            {
                allOk = false;
            }
        }

        ctorArgs = args.ToArray();
        return allOk;
    }

    private static bool TryResolveConversion(
        Compilation compilation, ITypeSymbol srcType, ITypeSymbol tgtType, string? useMethod,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
        IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
        EnumStrategy enumStrategy, Dictionary<string, SynthesizedMethod> synthesized,
        NullStrategy nullStrategy,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics,
        out string? converterMethod, out Model.NullHandling nullHandling,
        bool autoNest = false,
        NestedMappingRegistry? nestedRegistry = null,
        bool nullAsNull = false)
    {
        converterMethod = null;
        nullHandling = Model.NullHandling.None;

        if (useMethod is not null)
        {
            foreach (var m in allMethods)
            {
                if (string.Equals(m.Name, useMethod, System.StringComparison.Ordinal)
                    && HasImplicitConversion(compilation, srcType, m.ParamType)
                    && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                {
                    converterMethod = m.Name;
                    return true;
                }
            }
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
            return false;
        }

        if (DictionaryConverter.TryResolve(srcType, tgtType,
                out var srcKey, out var srcVal, out var tgtKey, out var tgtVal,
                out var dictHasCount, out var dictTargetKind))
        {
            if (!TryResolveConversion(compilation, srcKey, tgtKey, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var keyConv, out var keyNull, autoNest, nestedRegistry))
                return false;
            if (!TryResolveConversion(compilation, srcVal, tgtVal, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var valConv, out var valNull, autoNest, nestedRegistry))
                return false;
            converterMethod = DictionaryConverter.Synthesize(synthesized, srcType, tgtKey, tgtVal,
                dictHasCount, dictTargetKind, keyConv, keyNull, valConv, valNull, nullAsNull);
            return true;
        }

        if (CollectionConverter.TryResolve(srcType, tgtType,
                out var srcElem, out var tgtElem, out var collShape, nullAsNull))
        {
            if (collShape.Target == CollectionConverter.TargetKind.Array && collShape.SourceIsArray
                && BlittableProof.CanReinterpret(srcElem, tgtElem))
            {
                converterMethod = CollectionConverter.SynthesizeBlit(synthesized, srcType, srcElem, tgtElem);
                return true;
            }
            if (!TryResolveConversion(compilation, srcElem, tgtElem, null, allMethods, autoCandidates, enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics, out var elemConv, out var elemNull, autoNest, nestedRegistry))
                return false; // element diagnostic already reported by the recursive call
            converterMethod = CollectionConverter.Synthesize(synthesized, srcType, srcElem, tgtElem, collShape, elemConv, elemNull);
            return true;
        }

        // ── DWARF027: target is collection/dict-shaped but not in the supported taxonomy ──
        // Check this BEFORE the implicit-conversion / object-field-mapping fallbacks so the user
        // gets a loud diagnostic instead of a wrong (or silent) mapping.
        if (IsUnsupportedCollectionTarget(tgtType))
        {
            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnsupportedCollectionTarget,
                location, targetName));
            return false;
        }

        if (HasImplicitConversion(compilation, srcType, tgtType))
        {
            return true; // direct assignment
        }

        // Both nullable: T? → U? with a non-implicit inner T→U. Null-preserving (null → null).
        // Must come before the source-nullable branch so that T?→U? with a synthesized inner
        // conversion resolves to NullableProject rather than ThrowIfNull/ValueOrDefault.
        if (IsNullableValue(srcType, out var bothSrcU) && IsNullableValue(tgtType, out var bothTgtU))
        {
            if (TryResolveConversion(compilation, bothSrcU, bothTgtU, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics,
                    out var innerNN, out _, autoNest, nestedRegistry) && innerNN is not null)
            {
                converterMethod = innerNN;
                nullHandling = Model.NullHandling.NullableProject;
                return true;
            }
            // Inner unresolved or has no converter (implicit, already caught above) — fall through.
        }

        if (IsNullableValue(srcType, out var underlying))
        {
            // First check the simple implicit-conversion path (int? → int, int? → long, etc.)
            if (HasImplicitConversion(compilation, underlying, tgtType))
            {
                nullHandling = nullStrategy == NullStrategy.SetDefault ? Model.NullHandling.ValueOrDefault : Model.NullHandling.ThrowIfNull;
                return true;
            }

            // Recurse: try to resolve a conversion from the underlying (non-nullable) type to tgtType.
            // This handles cases like E1? → E2 where E1 → E2 requires a synthesized conversion.
            // Guard: 'underlying' is not itself nullable (Nullable<Nullable<T>> is illegal in C#).
            if (TryResolveConversion(compilation, underlying, tgtType, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics,
                    out var innerConv, out _, autoNest, nestedRegistry))
            {
                nullHandling = nullStrategy == NullStrategy.SetDefault ? Model.NullHandling.ValueOrDefault : Model.NullHandling.ThrowIfNull;
                converterMethod = innerConv; // may be null (direct assign after unwrap) or a synthesized method
                return true;
            }

            // Fall through — let the rest of TryResolveConversion attempt further resolutions.
        }

        // Target-nullable composition: non-nullable src → T? (nullable target).
        // When the source is NOT nullable but the target IS nullable, resolve src→underlying
        // and let the implicit T→T? lift do the rest (valid C# assignment).
        // Scope: non-nullable source only. nullable-source + nullable-target (T?→U?) is a
        // documented follow-up (complex null-semantics; left as DWARF005 for now).
        if (!IsNullableValue(srcType, out _) && IsNullableValue(tgtType, out var tgtUnderlying))
        {
            if (TryResolveConversion(compilation, srcType, tgtUnderlying, useMethod, allMethods, autoCandidates,
                    enumStrategy, synthesized, nullStrategy, location, targetName, diagnostics,
                    out var innerConvT, out _, autoNest, nestedRegistry))
            {
                converterMethod = innerConvT; // returns U; assigned to U? field via implicit U→U?
                // nullHandling stays None — source is non-null, always yields a value
                return true;
            }
            // Did not resolve — fall through to DWARF005
            return false;
        }

        // User-provided auto-candidate methods (no Use= annotation, auto-matched by type).
        // Checked BEFORE built-in synthesized converters (NumericConverter, ParsableConverter)
        // so that a user method can intentionally shadow the built-in behavior.
        // Two sources of user candidates:
        //   1. autoCandidates  — partial mapper methods (S → D object-level mappers)
        //   2. allMethods      — non-partial scalar converter helpers (e.g. int Shrink(long v))
        //      These are already in allMethods; excluding partials avoids double-counting mappers.
        string? found = null;
        foreach (var c in autoCandidates)
        {
            if (HasImplicitConversion(compilation, srcType, c.ParamType)
                && HasImplicitConversion(compilation, c.ReturnType, tgtType))
            {
                if (found is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion, location, targetName));
                    return false;
                }
                found = c.Name;
            }
        }
        // Also search all non-partial user methods (scalar converters not declared as partial mappers).
        foreach (var m in allMethods)
        {
            // Skip methods that are already in autoCandidates (partial mapper methods).
            if (autoCandidates.Any(ac => string.Equals(ac.Name, m.Name, System.StringComparison.Ordinal)
                    && SymbolEqualityComparer.Default.Equals(ac.ParamType, m.ParamType)
                    && SymbolEqualityComparer.Default.Equals(ac.ReturnType, m.ReturnType)))
                continue;
            if (HasImplicitConversion(compilation, srcType, m.ParamType)
                && HasImplicitConversion(compilation, m.ReturnType, tgtType))
            {
                if (found is not null)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion, location, targetName));
                    return false;
                }
                found = m.Name;
            }
        }

        if (found is not null)
        {
            converterMethod = found;
            return true;
        }

        // Integral↔integral narrowing / sign-change: emit CreateChecked (throws on overflow).
        // Must come after the implicit-conversion check (widening uses direct assign, not this)
        // and after user auto-candidates (user methods take precedence over built-in synthesis).
        // Enums have SpecialType.None — IsIntegral is false for them, so this never intercepts enums.
        var numericMethod = NumericConverter.TryCreate(srcType, tgtType, synthesized);
        if (numericMethod is not null)
        {
            converterMethod = numericMethod;
            return true;
        }

        // string↔T via IParsable<T>.Parse / IFormattable.ToString (InvariantCulture, loud on bad input).
        // Wired AFTER autoCandidates (explicit Use= and auto-conversion methods still win) and BEFORE
        // EnumConverter (enum↔string routes through EnumConverter's by-name switch; ParsableConverter
        // guards against enum operands explicitly).
        var parsableMethod = ParsableConverter.TryCreate(compilation, srcType, tgtType, synthesized);
        if (parsableMethod is not null)
        {
            converterMethod = parsableMethod;
            return true;
        }

        var enumMethod = EnumConverter.TryCreate(srcType, tgtType, enumStrategy, synthesized, location, targetName, diagnostics);
        if (enumMethod is not null)
        {
            converterMethod = enumMethod;
            return true;
        }

        // ── Auto-synthesized nested object mapper ─────────────────────────────
        // Placed LAST before DWARF005: only fires when nothing else resolved the pair.
        // Gate: autoNest=true AND both types are mappable named object types.
        if (autoNest && nestedRegistry is not null
            && tgtType is INamedTypeSymbol namedTgt
            && IsMappableObjectPair(compilation, srcType, namedTgt))
        {
            var synthName = nestedRegistry.GetOrReserve(srcType, namedTgt, location);
            if (synthName is not null)
            {
                converterMethod = synthName;
                return true;
            }
            // GetOrReserve returned null → cap exceeded; DWARF031 will be reported after drain.
            // Fall through to DWARF005.
        }

        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
        return false;
    }

    /// <summary>
    /// Returns true when both <paramref name="src"/> and <paramref name="tgt"/> are named types
    /// suitable for auto-nested-mapper synthesis. Excludes: scalars, enums, string, collection/
    /// IEnumerable types, Nullable&lt;T&gt;, interfaces, and abstract target types.
    /// </summary>
    private static bool IsMappableObjectPair(Compilation compilation, ITypeSymbol src, INamedTypeSymbol tgt)
    {
        // Source must be a named type (class or struct/record, not array/pointer/etc.)
        if (src is not INamedTypeSymbol namedSrc)
            return false;

        // Both must be Class or Struct (records are Class or Struct).
        // This also implicitly excludes enums (TypeKind.Enum), interfaces (TypeKind.Interface),
        // delegates, arrays, etc. — no separate enum guard needed.
        if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct)
            return false;
        if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct)
            return false;

        // Not scalar / special types (string, int, Guid, etc.)
        if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None)
            return false;

        // Not Nullable<T>
        if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return false;
        if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return false;

        // Not an interface target (can't construct an interface)
        if (tgt.IsAbstract)
            return false;

        // Not an IEnumerable / collection / dictionary — REGARDLESS of class vs struct.
        // (Struct collections like ImmutableArray<T> must NOT be object-field-mapped; they belong to
        //  CollectionConverter/DictionaryConverter, or fall to DWARF005 until supported. string is
        //  already excluded above by the SpecialType.None check.)
        if (ImplementsIEnumerable(compilation, namedSrc))
            return false;
        if (ImplementsIEnumerable(compilation, tgt))
            return false;

        // Target must have a constructible constructor (ConstructorSelector-compatible).
        // We do a lightweight check: at least one accessible non-static constructor.
        if (!tgt.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> implements <c>IEnumerable</c> (generic or non-generic),
    /// which means it is a collection/sequence type that belongs to CollectionConverter/DictionaryConverter.
    /// </summary>
    private static bool ImplementsIEnumerable(Compilation compilation, INamedTypeSymbol type)
    {
        // Fast checks: well-known collection / dict names (all supported + well-known unsupported)
        if (type.Name is "List" or "Array" or "HashSet" or "Dictionary"
            or "IEnumerable" or "ICollection" or "IList"
            or "IReadOnlyList" or "IReadOnlyCollection"
            or "ISet" or "IReadOnlySet"
            or "ImmutableArray" or "ImmutableList" or "IImmutableList"
            or "ImmutableHashSet" or "IImmutableSet"
            or "ImmutableDictionary" or "IImmutableDictionary"
            or "IDictionary" or "IReadOnlyDictionary"
            // well-known unsupported (DWARF027)
            or "SortedSet" or "SortedDictionary" or "SortedList"
            or "Queue" or "Stack" or "LinkedList"
            or "ConcurrentDictionary" or "ConcurrentQueue" or "ConcurrentStack" or "ConcurrentBag")
            return true;

        // Check whether the type or any of its interfaces is IEnumerable<T> or IEnumerable
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
            if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is collection-shaped (implements IEnumerable,
    /// is not string, is not already handled by CollectionConverter or DictionaryConverter)
    /// → should emit DWARF027 rather than DWARF005.
    /// </summary>
    private static bool IsUnsupportedCollectionTarget(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return false;

        // Multi-dimensional array (not IArrayTypeSymbol with Rank > 1 check needed here)
        if (type is IArrayTypeSymbol arr && arr.Rank > 1)
            return true;

        if (type is not INamedTypeSymbol named)
            return false;

        // Check if it implements IEnumerable (non-string collection-shaped)
        foreach (var iface in named.AllInterfaces)
        {
            if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                return true;
            if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return true;
        }
        // Also check the type itself (IEnumerable<T> as named type)
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            return true;

        return false;
    }

    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 1)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }

    private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMapperMethods(INamedTypeSymbol classSymbol)
    {
        var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition
                && !m.ReturnsVoid && m.Parameters.Length == 1 && m.ReturnType is INamedTypeSymbol)
            {
                methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
            }
        }
        return methods;
    }

    private static (List<(string Name, ITypeSymbol ParamType)> Before, List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)> After)
        CollectHooks(INamedTypeSymbol classSymbol, List<DiagnosticInfo> diagnostics)
    {
        var before = new List<(string Name, ITypeSymbol ParamType)>();
        var after = new List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)>();
        foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            var isBefore = m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.BeforeMapAttribute");
            var isAfter = m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.AfterMapAttribute");
            if (!isBefore && !isAfter)
            {
                continue;
            }
            var loc = LocationInfo.From(m.Locations.FirstOrDefault() ?? Location.None);
            if (!m.ReturnsVoid)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                continue;
            }
            if (isBefore)
            {
                if (m.Parameters.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                }
                else
                {
                    before.Add((m.Name, m.Parameters[0].Type));
                }
            }
            if (isAfter)
            {
                if (m.Parameters.Length == 1)
                {
                    // 1-param: the sole parameter is the target; capture its RefKind
                    after.Add((m.Name, m.Parameters[0].Type, null, m.Parameters[0].RefKind));
                }
                else if (m.Parameters.Length == 2)
                {
                    // 2-param: P0=source, P1=target; capture P1's RefKind
                    after.Add((m.Name, m.Parameters[0].Type, m.Parameters[1].Type, m.Parameters[1].RefKind));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.InvalidHook, loc, m.Name));
                }
            }
        }
        return (before, after);
    }

    private static bool HasImplicitConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol target)
    {
        var conversion = ((CSharpCompilation)compilation).ClassifyConversion(source, target);
        return conversion.IsImplicit && !conversion.IsUserDefined;
    }

    private static bool IsNullableValue(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            underlying = named.TypeArguments[0];
            return true;
        }
        underlying = type;
        return false;
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.GetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var m in current.GetMembers())
            {
                if (m.IsStatic)
                {
                    continue;
                }
                switch (m)
                {
                    case IPropertySymbol p when !p.IsIndexer && p.SetMethod is { DeclaredAccessibility: Accessibility.Public }:
                        if (seen.Add(p.Name))
                        {
                            yield return (p.Name, p.Type);
                        }
                        break;
                    case IFieldSymbol f when !f.IsImplicitlyDeclared && !f.IsReadOnly && f.DeclaredAccessibility == Accessibility.Public:
                        if (seen.Add(f.Name))
                        {
                            yield return (f.Name, f.Type);
                        }
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadOnlyMembers(ITypeSymbol type)
    {
        var writable = new HashSet<string>(WritableMembers(type).Select(m => m.Name), System.StringComparer.Ordinal);
        return ReadableMembers(type).Where(m => !writable.Contains(m.Name));
    }

    private static bool HasAccessibleParameterlessCtor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

    private const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";

    /// <summary>
    /// Returns the set of member names (case-insensitive) that are <c>required</c> AND satisfied via
    /// a constructor parameter, but whose constructor does NOT carry
    /// <c>[SetsRequiredMembers]</c>. These members must also be emitted in the object initializer to
    /// avoid CS9035.
    /// </summary>
    private static HashSet<string> ComputeRequiredMustInitialize(
        IMethodSymbol ctor,
        INamedTypeSymbol targetType,
        HashSet<string> consumedParams)
    {
        // If the chosen ctor is annotated [SetsRequiredMembers], C# considers all required members
        // satisfied — no double-set needed.
        var ctorHasSetsRequired = ctor.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == SetsRequiredMembersAttribute);

        if (ctorHasSetsRequired)
        {
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        }

        // Collect required member names from the target type hierarchy.
        var required = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (var current = (ITypeSymbol)targetType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol p when p.IsRequired:
                        required.Add(p.Name);
                        break;
                    case IFieldSymbol f when f.IsRequired:
                        required.Add(f.Name);
                        break;
                }
            }
        }

        // The intersection: consumed params that are also required members.
        var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var name in consumedParams)
        {
            if (required.Contains(name))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private static IEnumerable<string> ReadIgnores(ISymbol symbol) =>
        symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.MapIgnoreAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .Where(s => s is not null)
            .Select(s => s!);

    private static List<(string Source, string Target, string? Use)> ReadExplicitMaps(ISymbol method)
    {
        var maps = new List<(string Source, string Target, string? Use)>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "DwarfMapper.MapPropertyAttribute")
            {
                continue;
            }
            if (attr.ConstructorArguments.Length == 2
                && attr.ConstructorArguments[0].Value is string s
                && attr.ConstructorArguments[1].Value is string t)
            {
                string? use = null;
                foreach (var na in attr.NamedArguments)
                {
                    if (na.Key == "Use" && na.Value.Value is string u)
                    {
                        use = u;
                    }
                }
                maps.Add((s, t, use));
            }
        }
        return maps;
    }

    private static List<string> ReadFlattenRoots(ISymbol method)
    {
        var roots = new List<string>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.FlattenAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string s)
            {
                roots.Add(s);
            }
        }
        return roots;
    }

    private static List<string> ReadReinterpretMembers(ISymbol method)
    {
        var members = new List<string>();
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.ReinterpretAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is string m)
            {
                members.Add(m);
            }
        }
        return members;
    }

    private static EnumStrategy ReadEnumStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "EnumStrategy" && named.Value.Value is int i)
                {
                    return (EnumStrategy)i;
                }
            }
        }
        return EnumStrategy.ByName;
    }

    private static NullStrategy ReadNullStrategy(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "NullStrategy" && named.Value.Value is int i)
                {
                    return (NullStrategy)i;
                }
            }
        }
        return NullStrategy.Throw;
    }

    private static bool ReadCaseInsensitive(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "CaseInsensitive" && named.Value.Value is bool b)
                {
                    return b;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Reads the class-level <see cref="DwarfMapper.DwarfMapperAttribute.AutoNest"/> value
    /// from the <c>[DwarfMapper]</c> attribute. Defaults to <c>true</c>.
    /// </summary>
    /// <summary>
    /// Reads <c>[DwarfMapper(MaxDepth = N)]</c>; defaults to 64; clamps to [1, 1000].
    /// </summary>
    private static int ReadMaxDepth(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "MaxDepth" && named.Value.Value is int i)
                {
                    // Clamp to [1, 1000] — matches DwarfRefContext.AbsoluteMaxDepth
                    if (i < 1) return 1;
                    if (i > 1000) return 1000;
                    return i;
                }
            }
        }
        return 64; // default
    }

    private static bool ReadAutoNest(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "AutoNest" && named.Value.Value is bool b)
                {
                    return b;
                }
            }
        }
        return true; // default: auto-nesting enabled
    }

    /// <summary>
    /// Reads <c>[DwarfMapper(NullCollections = ...)]</c>; defaults to <c>AsEmpty</c>.
    /// </summary>
    private static NullCollectionsBehavior ReadNullCollections(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "NullCollections" && named.Value.Value is int i)
                    return (NullCollectionsBehavior)i;
            }
        }
        return NullCollectionsBehavior.AsEmpty;
    }

    /// <summary>
    /// Reads <c>[DwarfMapper(ReferenceHandling = ...)]</c>; returns the integer value of the
    /// <see cref="DwarfMapper.ReferenceHandlingStrategy"/> enum (0 = None, 1 = Preserve).
    /// Defaults to 0 (None).
    /// </summary>
    private static int ReadReferenceHandling(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "ReferenceHandling" && named.Value.Value is int i)
                    return i;
            }
        }
        return 0; // None
    }

    /// <summary>
    /// Reads the per-method <c>[AutoNest(bool)]</c> attribute override, falling back to
    /// <paramref name="classDefault"/> when the attribute is absent.
    /// </summary>
    private static bool ReadMethodAutoNest(IMethodSymbol method, bool classDefault)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "DwarfMapper.AutoNestAttribute"
                && attr.ConstructorArguments.Length == 1
                && attr.ConstructorArguments[0].Value is bool b)
            {
                return b;
            }
        }
        return classDefault;
    }

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

    private static List<MemberMap> ResolveProjectionMembers(
        ITypeSymbol sourceType, INamedTypeSymbol targetType, HashSet<string> ignores,
        Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics,
        bool caseInsensitive, IReadOnlyList<(string Source, string Target, string? Use)> explicitMaps)
    {
        var comparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;
        var sources = ReadableMembers(sourceType)
            .GroupBy(m => m.Name, comparer)
            .ToDictionary(g => g.Key, g => g.First(), comparer);
        var writableByName = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var m in WritableMembers(targetType)) { writableByName[m.Name] = m.Type; }

        var result = new List<MemberMap>();
        var handled = new HashSet<string>(System.StringComparer.Ordinal);
        var explicitSeen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var (srcName, tgtName, use) in explicitMaps)
        {
            if (!explicitSeen.Add(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.DuplicateMapProperty, location, tgtName));
                continue;
            }
            handled.Add(tgtName);
            if (ignores.Contains(tgtName))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IgnoreExplicitConflict, location, tgtName));
                continue;
            }
            if (!writableByName.TryGetValue(tgtName, out var tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownTarget, location, tgtName));
                continue;
            }
            if (use is not null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NotProjectable, location, tgtName));
                continue;
            }
            var sm = ReadableMembers(sourceType)
                .Where(m => System.StringComparer.Ordinal.Equals(m.Name, srcName))
                .Select(m => (ITypeSymbol?)m.Type).FirstOrDefault();
            if (sm is null)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapPropertyUnknownSource, location, srcName));
                continue;
            }
            if (!HasImplicitConversion(compilation, sm, tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NotProjectable, location, tgtName));
                continue;
            }
            result.Add(new MemberMap(tgtName, srcName));
        }

        foreach (var target in WritableMembers(targetType).OrderBy(m => m.Name, System.StringComparer.Ordinal))
        {
            if (handled.Contains(target.Name) || ignores.Contains(target.Name)) { continue; }
            if (!sources.TryGetValue(target.Name, out var src))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnmappedMember, location, target.Name));
                continue;
            }
            if (!HasImplicitConversion(compilation, src.Type, target.Type))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NotProjectable, location, target.Name));
                continue;
            }
            result.Add(new MemberMap(target.Name, src.Name));
        }
        return result;
    }

    /// <summary>
    /// DFS reachability: can we reach <paramref name="target"/> starting from <paramref name="start"/>
    /// by following edges in the call graph? Used to detect recursive method cycles.
    /// </summary>
    private static bool CanReach(
        Dictionary<string, HashSet<string>> graph,
        string start,
        string target)
    {
        var visited = new HashSet<string>(System.StringComparer.Ordinal);
        var stack = new Stack<string>();
        if (!graph.TryGetValue(start, out var startDeps)) return false;
        foreach (var dep in startDeps)
            stack.Push(dep);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (string.Equals(current, target, System.StringComparison.Ordinal)) return true;
            if (!visited.Add(current)) continue;
            if (graph.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                    stack.Push(dep);
            }
        }
        return false;
    }

    private static string AccessibilityText(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.Private => "private",
        _ => "public",
    };

    private static List<RoundTripPair> CollectRoundTrips(INamedTypeSymbol classSymbol, Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        var pairs = new List<RoundTripPair>();
        // Only emit a verifier when DwarfMapper.Testing is referenced — never force the test package into production.
        if (compilation.GetTypeByMetadataName("DwarfMapper.Testing.RoundTrip") is null)
        {
            return pairs;
        }

        var partials = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && m.IsPartialDefinition && !m.ReturnsVoid && m.Parameters.Length == 1)
            .ToList();

        foreach (var fwd in partials)
        {
            if (!fwd.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "DwarfMapper.RoundTripAttribute"))
            {
                continue;
            }
            var loc = LocationInfo.From(fwd.Locations.FirstOrDefault() ?? Location.None);
            var src = fwd.Parameters[0].Type;
            var dto = fwd.ReturnType;

            var inverses = partials.Where(m =>
                !SymbolEqualityComparer.Default.Equals(m, fwd)
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, dto)
                && SymbolEqualityComparer.Default.Equals(m.ReturnType, src)).ToList();

            if (inverses.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RoundTripNoInverse, loc, fwd.Name));
                continue;
            }
            if (inverses.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.RoundTripAmbiguousInverse, loc, fwd.Name));
                continue;
            }
            pairs.Add(new RoundTripPair(
                fwd.Name,
                inverses[0].Name,
                src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }
        return pairs;
    }
}
