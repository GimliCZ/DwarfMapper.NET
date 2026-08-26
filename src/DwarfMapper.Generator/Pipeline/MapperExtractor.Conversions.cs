// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     Returns true when <paramref name="src" /> is a named type that would be a mappable-object-pair
        ///     source except that it is abstract or an interface — i.e. it would silently drop derived members
        ///     (C2: DWARF033 guard).
        /// </summary>
        /// <summary>
        ///     True when <paramref name="src" /> is a concrete, NON-SEALED class that at least one other type in
        ///     this compilation derives from — i.e. a member declared as this type can hold a subclass instance at
        ///     run time, whose extra members an auto-nested map would silently drop (DWARF071).
        ///     <para>
        ///         Deliberately narrow, to keep the Info actionable rather than ambient noise:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <description>sealed source → the declared type IS the runtime type; nothing can be dropped;</description>
        ///         </item>
        ///         <item>
        ///             <description>abstract source → already a hard error, DWARF033; not this diagnostic's business;</description>
        ///         </item>
        ///         <item>
        ///             <description>
        ///                 no derived type anywhere in the compilation → the risk is theoretical. A type
        ///                 derived in a DOWNSTREAM assembly is not visible here, and warning on every non-sealed class on
        ///                 that basis would fire on essentially every DTO in existence.
        ///             </description>
        ///         </item>
        ///     </list>
        /// </summary>
        /// <summary>
        ///     Every type that is a base of some other type declared in this assembly, computed in ONE pass and
        ///     cached per <see cref="Compilation" />.
        ///     <para>
        ///         <see cref="HasDerivedTypesInCompilation" /> used to walk every type in the assembly on each call, and
        ///         its call site sits in per-MEMBER auto-nest resolution — before the nested-registry dedupes the pair —
        ///         so the full walk repeated for every member that reached that branch, not merely once per distinct
        ///         type pair. On a large assembly that is O(members x types) of pure re-derivation at compile time.
        ///     </para>
        ///     Keyed by a <see cref="ConditionalWeakTable{TKey,TValue}" /> so the entry (and the symbols it holds)
        ///     dies with the compilation — a plain static dictionary in a long-lived generator host would pin
        ///     symbols from every compilation it ever saw.
        /// </summary>
        private static readonly ConditionalWeakTable<Compilation, HashSet<ISymbol>> BaseTypesByCompilation = new();

        private static bool TryResolveConversion(
            Compilation compilation,
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            string? useMethod,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            EnumPolicy enumPolicy,
            Dictionary<string, SynthesizedMethod> synthesized,
            NullStrategy nullStrategy,
            LocationInfo? location,
            string targetName,
            List<DiagnosticInfo> diagnostics,
            out string? converterMethod,
            out NullHandling nullHandling,
            out bool converterNeedsCtx,
            bool autoNest = false,
            NestedMappingRegistry? nestedRegistry = null,
            bool nullAsNull = false,
            bool isPreserve = false,
            bool allowInterfaceSrc = false,
            bool isSetNull = false,
            bool implicitConversions = true,
            IReadOnlyCollection<string>? reservedConverters = null)
        {
            converterMethod = null;
            nullHandling = NullHandling.None;
            converterNeedsCtx = false;

            if (useMethod is not null)
            {
                foreach (var m in allMethods)
                    if (string.Equals(m.Name, useMethod, StringComparison.Ordinal) && HasImplicitConversion(compilation, srcType, m.ParamType) && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                    {
                        // B4 / DWARF032: Under Preserve mode, a Use= converter pointing to an
                        // arbitrary user function cannot participate in reference-identity tracking —
                        // the generator does not own its body and cannot thread DwarfRefContext into it.
                        // A shared/cyclic reference-type object routed through this method will be
                        // duplicated rather than de-duplicated, silently producing wrong topology.
                        // Only fire for REFERENCE-TYPE targets: scalars (int, Guid, enum, string, etc.)
                        // are never tracked by the identity map and Use= on a scalar is fine.
                        if (isPreserve && tgtType.IsReferenceType)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                DiagnosticDescriptors.ReferenceHandlingUseConverter,
                                location,
                                targetName));
                            // Report the diagnostic AND return false so no silent wrong code is emitted.
                            return false;
                        }

                        converterMethod = m.Name;
                        return true;
                    }

                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UseMethodInvalid, location, useMethod));
                return false;
            }

            if (DictionaryConverter.TryResolve(srcType,
                    tgtType,
                    out var srcKey,
                    out var srcVal,
                    out var tgtKey,
                    out var tgtVal,
                    out var dictHasCount,
                    out var dictTargetKind))
            {
                // A3: determine effective null-as-null for the OUTER dict helper based on target nullability.
                // If nullAsNull=true but the target dict type is non-nullable, fall back to AsEmpty
                // to prevent CS8601 (nullable helper assigned to non-nullable field).
                var dictEffectiveNullAsNull = nullAsNull && IsNullableReferenceType(tgtType);

                // A1: propagate nullAsNull to nested key/value converters so nullable elements
                // (e.g. the value type List<int>? in Dictionary<string, List<int>?>) generate
                // helpers that preserve null instead of silently mapping to empty.
                if (!TryResolveConversion(compilation,
                        srcKey,
                        tgtKey,
                        null,
                        allMethods,
                        autoCandidates,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        location,
                        targetName,
                        diagnostics,
                        out var keyConv,
                        out var keyNull,
                        out var keyNeedsCtx,
                        autoNest,
                        nestedRegistry,
                        nullAsNull,
                        isPreserve,
                        isSetNull: isSetNull,
                        implicitConversions: implicitConversions,
                        reservedConverters: reservedConverters))
                {
                    return false;
                }

                if (!TryResolveConversion(compilation,
                        srcVal,
                        tgtVal,
                        null,
                        allMethods,
                        autoCandidates,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        location,
                        targetName,
                        diagnostics,
                        out var valConv,
                        out var valNull,
                        out var valNeedsCtx,
                        autoNest,
                        nestedRegistry,
                        nullAsNull,
                        isPreserve,
                        isSetNull: isSetNull,
                        implicitConversions: implicitConversions,
                        reservedConverters: reservedConverters))
                {
                    return false;
                }

                // Preserve OR SetNull: if the key/value converter is an auto-nested object mapper, force it RC
                // so it carries (ctx, depth) and the dict helper threads the shared context into it — this is
                // what lets a cycle routed through a dictionary value break (SetNull) or depth-cap.
                if ((isPreserve || isSetNull) && nestedRegistry is not null)
                {
                    if (keyConv is not null && GeneratedNames.IsObjectMap(keyConv))
                    {
                        nestedRegistry.ForceRecursionCapable(keyConv);
                        keyNeedsCtx = true;
                    }

                    if (valConv is not null && GeneratedNames.IsObjectMap(valConv))
                    {
                        nestedRegistry.ForceRecursionCapable(valConv);
                        valNeedsCtx = true;
                    }
                }

                converterMethod = DictionaryConverter.Synthesize(synthesized,
                    srcType,
                    tgtKey,
                    tgtVal,
                    dictHasCount,
                    dictTargetKind,
                    keyConv,
                    keyNull,
                    valConv,
                    valNull,
                    dictEffectiveNullAsNull,
                    isPreserve,
                    keyNeedsCtx,
                    valNeedsCtx);
                // The dict helper threads (ctx, depth) when it register-before-fills (Preserve mutable) OR a
                // key/value converter is recursion-capable (Preserve, or None/SetNull self-referential value).
                var isMutableDict = dictTargetKind != DictionaryConverter.DictTargetKind.ImmutableDictionary && dictTargetKind != DictionaryConverter.DictTargetKind.IImmutableDictionary;
                converterNeedsCtx = (isPreserve && isMutableDict) || keyNeedsCtx || valNeedsCtx;

                // None+Throw: a key/value resolved to a PUBLIC declared method. Record a re-synthesis
                // closure so the post-pass can upgrade this dict helper if that method is self-recursive.
                if (!isPreserve && !isSetNull && nestedRegistry is not null)
                {
                    var KeyIsPublicObj = keyConv is not null &&
                                         !keyNeedsCtx &&
                                         !GeneratedNames.IsAnySynthesized(keyConv) &&
                                         tgtKey is INamedTypeSymbol tk &&
                                         IsMappableObjectPair(compilation, srcKey, tk);
                    var ValIsPublicObj = valConv is not null &&
                                         !valNeedsCtx &&
                                         !GeneratedNames.IsAnySynthesized(valConv) &&
                                         tgtVal is INamedTypeSymbol tv &&
                                         IsMappableObjectPair(compilation, srcVal, tv);
                    if (KeyIsPublicObj || ValIsPublicObj)
                    {
                        var hName = converterMethod!;
                        var elems = new List<string>();
                        if (KeyIsPublicObj)
                        {
                            elems.Add(keyConv!);
                        }

                        if (ValIsPublicObj)
                        {
                            elems.Add(valConv!);
                        }

                        var cSrc = srcType;
                        var cTk = tgtKey;
                        var cTv = tgtVal;
                        var cHas = dictHasCount;
                        var cKind = dictTargetKind;
                        var cKeyConv = keyConv;
                        var cKeyNull = keyNull;
                        var cValConv = valConv;
                        var cValNull = valNull;
                        var cNullAsNull = dictEffectiveNullAsNull;
                        nestedRegistry.RecordCtxUpgradeCandidate(hName,
                            elems.ToArray(),
                            resolve =>
                            {
                                var nk = cKeyConv;
                                var nkCtx = false;
                                if (KeyIsPublicObj)
                                {
                                    var r = resolve(cKeyConv!);
                                    if (!string.Equals(r, cKeyConv, StringComparison.Ordinal))
                                    {
                                        nk = r;
                                        nkCtx = true;
                                    }
                                }

                                var nv = cValConv;
                                var nvCtx = false;
                                if (ValIsPublicObj)
                                {
                                    var r = resolve(cValConv!);
                                    if (!string.Equals(r, cValConv, StringComparison.Ordinal))
                                    {
                                        nv = r;
                                        nvCtx = true;
                                    }
                                }

                                DictionaryConverter.SynthesizeInPlace(synthesized,
                                    hName,
                                    cSrc,
                                    cTk,
                                    cTv,
                                    cHas,
                                    cKind,
                                    nk,
                                    cKeyNull,
                                    nkCtx,
                                    nv,
                                    cValNull,
                                    nvCtx,
                                    cNullAsNull);
                            });
                    }
                }

                return true;
            }

            if (CollectionConverter.TryResolve(srcType,
                    tgtType,
                    out var srcElem,
                    out var tgtElem,
                    out var collShape,
                    nullAsNull))
            {
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    BlittableProof.CanReinterpret(srcElem,
                        tgtElem))
                {
                    converterMethod = CollectionConverter.SynthesizeBlit(synthesized, srcType, srcElem, tgtElem);
                    return true;
                }

                // R25-03: enum arrays as underlying-primitive blit. Kept separate from the struct proof above
                // because the question is not layout — the layouts are trivially identical — but whether the
                // SCALAR path is a reinterpret. Under ByName it is not: its switch throws on a value matching
                // no member, and an enum may legally hold any value of its underlying type.
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    BlittableProof.CanReinterpretEnums(srcElem, tgtElem, enumPolicy.Strategy))
                {
                    converterMethod = CollectionConverter.SynthesizeBlit(synthesized, srcType, srcElem, tgtElem);
                    return true;
                }

                // R25-02 (T2): the LIST-involved shapes — array→List, List→array, List→List. The element proof
                // is the one above, reused verbatim and never relaxed; all that changes is which storage the
                // bytes are read from and written to. Interfaces are excluded on the source side because
                // CollectionsMarshal.AsSpan is declared on the concrete List<T>.
                //
                // NOT reached for array→array: that returns above. NOT gated on a minimum length either —
                // measured locally at 2026-08-23, the crossover is between n=4 and n=8 and the sub-crossover
                // penalty is tens of nanoseconds, so a runtime branch would cost more clarity than it buys
                // time. The RFC's `Count >= 32` guard came from a container that this hardware does not
                // reproduce. See benchmarks/results/2026-08-23-round25-kernels.md.
                if (!collShape.NullAsNull)
                {
                    var tgtIsArray = collShape.Target == CollectionConverter.TargetKind.Array;
                    var tgtIsListFamily = collShape.Target is CollectionConverter.TargetKind.List
                        or CollectionConverter.TargetKind.ICollection
                        or CollectionConverter.TargetKind.IList
                        or CollectionConverter.TargetKind.IReadOnlyList
                        or CollectionConverter.TargetKind.IReadOnlyCollection;
                    var tgtIsImmutableArray = collShape.Target == CollectionConverter.TargetKind.ImmutableArray;
                    var srcIsList = CollectionConverter.IsConcreteList(srcType);
                    var srcIsImmutableArray = CollectionConverter.IsImmutableArray(srcType);
                    var elementBlits = BlittableProof.CanReinterpret(srcElem, tgtElem) ||
                                       BlittableProof.CanReinterpretEnums(srcElem, tgtElem, enumPolicy.Strategy);

                    var srcStorage = collShape.SourceIsArray ? CollectionConverter.BlitStorage.Array
                        : srcIsList ? CollectionConverter.BlitStorage.List
                        : srcIsImmutableArray ? CollectionConverter.BlitStorage.ImmutableArray
                        : (CollectionConverter.BlitStorage?)null;

                    var tgtStorage = tgtIsArray ? CollectionConverter.BlitStorage.Array
                        : tgtIsListFamily ? CollectionConverter.BlitStorage.List
                        : tgtIsImmutableArray ? CollectionConverter.BlitStorage.ImmutableArray
                        : (CollectionConverter.BlitStorage?)null;

                    if (elementBlits &&
                        srcStorage is { } ss &&
                        tgtStorage is { } ts &&
                        !(ss == CollectionConverter.BlitStorage.Array && ts == CollectionConverter.BlitStorage.Array))
                    {
                        converterMethod = CollectionConverter.SynthesizeBlitListShape(synthesized,
                            srcType,
                            srcElem,
                            tgtElem,
                            ss,
                            ts);
                        return true;
                    }
                }

                // The blit was not provable. If the pair MISSED it narrowly, say so — the element loop is correct
                // but the caller is one rename away from a block copy, and nothing else in the build reports that.
                // Never reached for [Reinterpret] members: that branch forces the blit and returns before this
                // method is called, so DWARF022 stays the only voice on the explicit form.
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    BlittableProof.TryExplainNearMiss(srcElem, tgtElem, out var nearMissReason))
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.BlitNearMiss,
                        location,
                        $"'{targetName}' maps an array whose element types are nearly layout-identical, so it " +
                        $"takes the element-by-element copy: {nearMissReason}",
                        MemberName: targetName));
                }

                // SIMD widening fast-path: array→array of a lossless primitive widen pair (e.g. int[]→long[],
                // float[]→double[]) → Vector.Widen. Identical result to the scalar implicit widen; reflection-free.
                // Comes AFTER blit (same-size pairs blit; widen pairs differ in size so CanReinterpret is false).
                if (collShape.Target == CollectionConverter.TargetKind.Array &&
                    collShape.SourceIsArray &&
                    CollectionConverter.IsWidenPair(srcElem,
                        tgtElem))
                {
                    converterMethod = CollectionConverter.SynthesizeSimdWiden(synthesized, srcType, srcElem, tgtElem);
                    return true;
                }

                // A3: determine effective null-as-null for the OUTER collection helper based on target nullability.
                // Reference-type collections: fall back to AsEmpty when target is non-nullable to prevent CS8601.
                // ImmutableArray<T>?: CollectionConverter.TryResolve already handles Nullable<ImmutableArray<T>>
                // by unwrapping it and setting nullAsNull=true in the shape, so collShape.NullAsNull is already
                // correct and we just need to preserve it.
                var collEffectiveNullAsNull = collShape.Target == CollectionConverter.TargetKind.ImmutableArray
                    // ImmutableArray: shape.NullAsNull is authoritative (set by TryResolve for Nullable<> unwrapping).
                    ? collShape.NullAsNull
                    // Reference-type collections: only AsNull when target field is nullable ref type.
                    : nullAsNull && IsNullableReferenceType(tgtType);

                // A1: propagate nullAsNull to the element converter so nullable elements
                // (e.g. element type List<int>? inside List<List<int>?>) generate helpers
                // that preserve null instead of silently mapping to empty.
                if (!TryResolveConversion(compilation,
                        srcElem,
                        tgtElem,
                        null,
                        allMethods,
                        autoCandidates,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        location,
                        targetName,
                        diagnostics,
                        out var elemConv,
                        out var elemNull,
                        out var elemNeedsCtx,
                        autoNest,
                        nestedRegistry,
                        nullAsNull,
                        isPreserve,
                        isSetNull: isSetNull,
                        implicitConversions: implicitConversions,
                        reservedConverters: reservedConverters))
                {
                    return false; // element diagnostic already reported by the recursive call
                }

                // #100: a nullable-annotated REFERENCE element whose target element is non-nullable needs the
                // same per-element null handling as a nullable VALUE element (int?→int). SymbolEqualityComparer
                // ignores nullable annotations, so without this the identity fast-path emits a direct collection
                // copy the compiler rejects (List<string?>→List<string> = CS8620) or an array clone that smuggles
                // nulls past the annotation. A non-null target element cannot hold null, so throw on a null
                // element — loud and never-silent (there is no valid non-null reference "default" to substitute).
                if (elemConv is null &&
                    elemNull == NullHandling.None &&
                    srcElem.IsReferenceType &&
                    srcElem.NullableAnnotation == NullableAnnotation.Annotated &&
                    tgtElem.NullableAnnotation != NullableAnnotation.Annotated &&
                    SymbolEqualityComparer.Default.Equals(
                        srcElem.WithNullableAnnotation(NullableAnnotation.None),
                        tgtElem.WithNullableAnnotation(NullableAnnotation.None)))
                {
                    elemNull = NullHandling.ThrowIfNull;
                }

                // Preserve OR SetNull: if the element converter is an auto-nested object mapper, force it
                // recursion-capable so it gets the (ctx, depth) signature — the collection helper will call it
                // with (elem, ctx, depth + 1), threading ONE shared context across the collection edge. This
                // is what lets a cycle routed through a collection break (SetNull → back-edge null) or
                // depth-cap, instead of the element re-entering the public entry (fresh context → StackOverflow).
                if ((isPreserve || isSetNull) && elemConv is not null && GeneratedNames.IsObjectMap(elemConv) && nestedRegistry is not null)
                {
                    nestedRegistry.ForceRecursionCapable(elemConv);
                    elemNeedsCtx = true;
                }

                // Apply effective nullAsNull (A3: may be false even when nullAsNull=true if target is non-nullable).
                if (collEffectiveNullAsNull != nullAsNull)
                {
                    collShape = new CollectionConverter.Shape(collShape.Target,
                        collShape.SourceIsArray,
                        collShape.Count,
                        collEffectiveNullAsNull);
                }

                converterMethod = CollectionConverter.Synthesize(synthesized,
                    srcType,
                    srcElem,
                    tgtElem,
                    collShape,
                    elemConv,
                    elemNull,
                    isPreserve,
                    elemNeedsCtx);
                // Thread (ctx, depth) when the collection register-before-fills (Preserve mutable) OR its
                // element is recursion-capable (Preserve, or None/SetNull self-referential element).
                converterNeedsCtx = (isPreserve && CollectionConverter.IsMutableReferenceCollection(collShape.Target)) ||
                                    elemNeedsCtx;

                // None+Throw: the element resolved either to a PUBLIC declared method (e.g. a self-map `Map`) or
                // to a SYNTHESIZED object-map helper (`__DwarfMap_Obj_…`, which is what a [GenerateMap<S,T>] pair
                // produces — there is no declared method to resolve to). Record a re-synthesis closure for BOTH:
                // if the element turns out self-recursive, the post-pass re-emits this collection helper so it
                // threads (ctx, depth) into the element call.
                //
                // Only covering the public-method case was a real bug: a [GenerateMap] pair whose type recurses
                // THROUGH a collection edge (e.g. `class Node { List<Node> Kids; }`) had its object helper marked
                // recursion-capable by ComputeRecursionCapability() — gaining (ctx, depth) IN PLACE — while the
                // collection helper calling it was never re-synthesized, so it still called it with one argument.
                // That emitted code which did not compile (CS7036). The equivalent partial-method mapper worked,
                // because its element resolved to a declared method and so WAS recorded here.
                if (!isPreserve && !isSetNull && !elemNeedsCtx && nestedRegistry is not null && elemConv is not null && (!GeneratedNames.IsAnySynthesized(elemConv) || GeneratedNames.IsObjectMap(elemConv)) && tgtElem is INamedTypeSymbol tgtElemNamed && IsMappableObjectPair(compilation, srcElem, tgtElemNamed))
                {
                    var hName = converterMethod!;
                    var capSrc = srcType;
                    var capElem = srcElem;
                    var capTgt = tgtElem;
                    var capShape = collShape;
                    var capNull = elemNull;
                    nestedRegistry.RecordCtxUpgradeCandidate(hName,
                        new[]
                        {
                            elemConv
                        },
                        resolve =>
                            CollectionConverter.SynthesizeInPlace(synthesized,
                                hName,
                                capSrc,
                                capElem,
                                capTgt,
                                capShape,
                                resolve(elemConv),
                                capNull));
                }

                return true;
            }

            // ── DWARF027: target is collection/dict-shaped but not in the supported taxonomy ──
            // Check this BEFORE the implicit-conversion / object-field-mapping fallbacks so the user
            // gets a loud diagnostic instead of a wrong (or silent) mapping.
            if (IsUnsupportedCollectionTarget(tgtType))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.UnsupportedCollectionTarget,
                    location,
                    targetName));
                return false;
            }

            if (HasImplicitConversion(compilation, srcType, tgtType))
            {
                // Cross-category numeric (integer ↔ floating/decimal, e.g. int→double, int→float) is implicit
                // in C# but crosses kinds (and int→float / long→double silently lose precision). Same-category
                // widening (int→long, float→double) is NOT flagged. DWARF038: suggestion / strict-mode error.
                if (NumericConverter.IsCrossCategoryLossy(srcType, tgtType))
                {
                    EmitImplicitConversionDiag(diagnostics,
                        location,
                        targetName,
                        srcType,
                        tgtType,
                        "cross-category numeric",
                        implicitConversions,
                        true);
                }

                return true; // direct assignment
            }

            // Nullable-value source into a target that CAN HOLD NULL: T? → U? (both Nullable<>) or T? → U?
            // (a nullable-annotated reference target) with a non-implicit inner T→U. Null-preserving
            // (null → null). Must come before the source-nullable branch so that a nullable source with a
            // synthesized inner conversion resolves to NullableProject rather than ThrowIfNull/ValueOrDefault.
            //
            // The gate used to demand BOTH sides be Nullable<T>, which made the lift depend on the DESTINATION'S
            // KIND rather than on whether it can hold the null: `S1? → D1?` lifted when D1 was a struct and threw
            // ("Source member 'X' was null") when D1 was a class or a record — an inconsistency no user can predict
            // from the types, and undocumented (the NullStrategy contract governs nullable-value source →
            // NON-nullable target). TASKS.md I7 / round 23 N2. Nullable-capable is deliberately annotation-strict
            // for references: an unannotated or oblivious reference target keeps the documented throw.
            if (IsNullableValue(srcType, out var bothSrcU) && TryGetNullableCapableTarget(tgtType, out var bothTgtU))
                // implicitConversions is threaded, and it used to be dropped here. Every recursion that crosses
                // a Nullable<> wrapper defaulted the option back to `true` (permissive), so under
                // [DwarfMapper(ImplicitConversions = false)] a lossy conversion between two nullable members
                // reported DWARF038 as a WARNING and the mapper was still generated — the strict setting was
                // silently off for the whole nullable half of the type space. The collection-element and
                // dictionary key/value recursions above always passed it; these three did not. (TASKS.md I20.)
            {
                if (TryResolveConversion(compilation,
                        bothSrcU,
                        bothTgtU,
                        useMethod,
                        allMethods,
                        autoCandidates,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        location,
                        targetName,
                        diagnostics,
                        out var innerNN,
                        out _,
                        out _,
                        autoNest,
                        nestedRegistry,
                        nullAsNull,
                        implicitConversions: implicitConversions,
                        reservedConverters: reservedConverters) &&
                    innerNN is not null)
                {
                    converterMethod = innerNN;
                    nullHandling = NullHandling.NullableProject;
                    return true;
                }
            }

            // Inner unresolved or has no converter (implicit, already caught above) — fall through.
            if (IsNullableValue(srcType, out var underlying))
            {
                // First check the simple implicit-conversion path (int? → int, int? → long, etc.)
                if (HasImplicitConversion(compilation, underlying, tgtType))
                {
                    // Same question the direct-assign path at the top of this method asks, and this arm never
                    // asked it: `long? → double` unwraps to `long → double`, which IS implicit in C# and IS
                    // lossy, so it was assigned in silence at every severity. The DWARF038 names the UNWRAPPED
                    // pair, because that is the conversion being applied. (TASKS.md I20.)
                    if (NumericConverter.IsCrossCategoryLossy(underlying, tgtType))
                    {
                        EmitImplicitConversionDiag(diagnostics,
                            location,
                            targetName,
                            underlying,
                            tgtType,
                            "cross-category numeric",
                            implicitConversions,
                            true);
                    }

                    nullHandling = nullStrategy == NullStrategy.SetDefault
                        ? NullHandling.ValueOrDefault
                        : NullHandling.ThrowIfNull;
                    return true;
                }

                // Recurse: try to resolve a conversion from the underlying (non-nullable) type to tgtType.
                // This handles cases like E1? → E2 where E1 → E2 requires a synthesized conversion.
                // Guard: 'underlying' is not itself nullable (Nullable<Nullable<T>> is illegal in C#).
                if (TryResolveConversion(compilation,
                        underlying,
                        tgtType,
                        useMethod,
                        allMethods,
                        autoCandidates,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        location,
                        targetName,
                        diagnostics,
                        out var innerConv,
                        out _,
                        out _,
                        autoNest,
                        nestedRegistry,
                        nullAsNull,
                        implicitConversions: implicitConversions,
                        reservedConverters: reservedConverters))
                {
                    nullHandling = nullStrategy == NullStrategy.SetDefault
                        ? NullHandling.ValueOrDefault
                        : NullHandling.ThrowIfNull;
                    converterMethod = innerConv; // may be null (direct assign after unwrap) or a synthesized method
                    return true;
                }

                // Fall through — let the rest of TryResolveConversion attempt further resolutions.
            }

            // Target-nullable composition: non-Nullable<> src → T? (Nullable<> target).
            // The source is not a Nullable<T>, so resolve src→underlying and let the implicit T→T? lift do the
            // rest (valid C# assignment). A nullable-VALUE source is handled by the nullable-capable-target
            // branch above.
            //
            // The source may still be a possibly-null REFERENCE, and then the lift has to be explicit: the
            // converter is a synthesized nested mapper with a value-type return, which cannot answer null, so
            // it threw ("Cannot map a null 'S' to value-type 'D'.") on a null the Nullable<U> destination could
            // have held — I7's reverse genre, and the mirror of the kind-instead-of-capability confusion the
            // gate above had. NullableProjectRef makes the CALL SITE test for null first, which is the only
            // place that can: the helper's return type leaves it no way to express the answer.
            // TASKS.md I7 / round 23 N2.
            if (!IsNullableValue(srcType, out _) && IsNullableValue(tgtType, out var tgtUnderlying))
            {
                if (TryResolveConversion(compilation,
                        srcType,
                        tgtUnderlying,
                        useMethod,
                        allMethods,
                        autoCandidates,
                        enumPolicy,
                        synthesized,
                        nullStrategy,
                        location,
                        targetName,
                        diagnostics,
                        out var innerConvT,
                        out _,
                        out _,
                        autoNest,
                        nestedRegistry,
                        nullAsNull,
                        implicitConversions: implicitConversions,
                        reservedConverters: reservedConverters))
                {
                    converterMethod = innerConvT; // returns U; assigned to U? field via implicit U→U?
                    // A possibly-null reference source needs the explicit null test. A value-type source
                    // (non-Nullable<>) always yields a value, and a direct assignment (no converter) already
                    // lifts through the implicit U→U?, so both keep NullHandling.None.
                    if (innerConvT is not null && SourceMayBeNullRef(srcType))
                    {
                        nullHandling = NullHandling.NullableProjectRef;
                    }

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
            // A method named by some [MapProperty(Use = …)] is RESERVED: the author dedicated it to that one
            // member, which is a statement of intent, not an offer of a general-purpose converter. Without
            // this guard it is also auto-adopted for every other member whose types happen to line up —
            // silently, with no diagnostic and a green build.
            //
            // Found migrating a real codebase: a `string BuildDocumentId(Guid)` written for Document.Id (it
            // prefixes a date) was also applied to Document.DispatchId, a plain auto-matched Guid→string
            // member, so every record would have stored the decorated id in the plain field. An explicit
            // Use= for THIS member still resolves above and is unaffected — only auto-adoption is blocked.
            static bool IsReserved(IReadOnlyCollection<string>? reserved, string name)
            {
                return reserved is not null && reserved.Contains(name, StringComparer.Ordinal);
            }

            string? found = null;
            foreach (var c in autoCandidates)
                if (!IsReserved(reservedConverters, c.Name) && HasImplicitConversion(compilation, srcType, c.ParamType) && HasImplicitConversion(compilation, c.ReturnType, tgtType))
                {
                    if (found is not null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion,
                            location,
                            targetName));
                        return false;
                    }

                    found = c.Name;
                }

            // Also search all non-partial user methods (scalar converters not declared as partial mappers).
            foreach (var m in allMethods)
            {
                if (IsReserved(reservedConverters, m.Name))
                {
                    continue;
                }

                // Skip methods that are already in autoCandidates (partial mapper methods).
                if (autoCandidates.Any(ac => string.Equals(ac.Name, m.Name, StringComparison.Ordinal) && SymbolEqualityComparer.Default.Equals(ac.ParamType, m.ParamType) && SymbolEqualityComparer.Default.Equals(ac.ReturnType, m.ReturnType)))
                {
                    continue;
                }

                if (HasImplicitConversion(compilation, srcType, m.ParamType) && HasImplicitConversion(compilation, m.ReturnType, tgtType))
                {
                    if (found is not null)
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AmbiguousConversion,
                            location,
                            targetName));
                        return false;
                    }

                    found = m.Name;
                }
            }

            if (found is not null)
            {
                // Plan 19 C2b: Under Preserve OR SetNull mode, if the found auto-candidate is a PUBLIC
                // partial mapper method (from autoCandidates) and autoNest is enabled, prefer the
                // synthesized private __DwarfMap_Obj_* form instead. Public methods don't accept the
                // shared DwarfRefContext — calling them from a collection/dict helper would create a fresh
                // context, losing identity/depth/on-stack state and causing infinite loops on cycles.
                // We fall through to the auto-nest path below only when these conditions hold;
                // user-provided converter helpers (allMethods, not autoCandidates) are always respected.
                var foundIsAutoCandidate = (isPreserve || isSetNull) &&
                                           autoNest &&
                                           nestedRegistry is not null &&
                                           autoCandidates.Any(ac =>
                                               string.Equals(ac.Name, found, StringComparison.Ordinal)) &&
                                           tgtType is INamedTypeSymbol &&
                                           IsMappableObjectPair(compilation, srcType, (INamedTypeSymbol)tgtType);
                if (!foundIsAutoCandidate)
                {
                    converterMethod = found;
                    return true;
                }
                // Fall through to synthesize a private __DwarfMap_Obj_* form.
            }

            // Integral↔integral narrowing / sign-change: emit CreateChecked (throws on overflow).
            // Must come after the implicit-conversion check (widening uses direct assign, not this)
            // and after user auto-candidates (user methods take precedence over built-in synthesis).
            // Enums have SpecialType.None — IsIntegral is false for them, so this never intercepts enums.
            var numericMethod = NumericConverter.TryCreate(srcType, tgtType, synthesized);
            if (numericMethod is not null)
            {
                converterMethod = numericMethod;
                // DWARF038: a non-implicit numeric conversion (narrowing / sign-change, via CreateChecked) is
                // a non-lossless basic-type conversion. Surface it as a suggestion (permissive) or a build
                // error (ImplicitConversions = false). Lossless widening uses the implicit/direct path above
                // and is never flagged.
                EmitImplicitConversionDiag(diagnostics,
                    location,
                    targetName,
                    srcType,
                    tgtType,
                    "numeric (narrowing/sign-change)",
                    implicitConversions,
                    true);
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
                // DWARF038: string↔T parse/format is a non-lossless basic-type conversion → suggestion / error.
                EmitImplicitConversionDiag(diagnostics,
                    location,
                    targetName,
                    srcType,
                    tgtType,
                    "parse/format (string↔T)",
                    implicitConversions,
                    true);
                return true;
            }

            var enumMethod = EnumConverter.TryCreate(srcType,
                tgtType,
                enumPolicy,
                synthesized,
                location,
                targetName,
                diagnostics);
            if (enumMethod is not null)
            {
                converterMethod = enumMethod;
                return true;
            }

            // ── Auto-synthesized nested object mapper ─────────────────────────────
            // Placed LAST before DWARF005: only fires when nothing else resolved the pair.
            // Gate: autoNest=true AND both types are mappable named object types.
            if (autoNest && nestedRegistry is not null && tgtType is INamedTypeSymbol namedTgt)
            {
                if (IsMappableObjectPair(compilation, srcType, namedTgt, allowInterfaceSrc))
                {
                    // DWARF071: the source is a CONCRETE class that other types derive from. It maps fine, but only
                    // the declared members are mapped — a derived instance at run time loses everything declared
                    // below the base. DWARF033 catches the abstract/interface form of this; the concrete form is
                    // instantiable and slips past it. Reported (not refused) because base-only mapping is often
                    // exactly what was intended. Suppressed under allowInterfaceSrc — a [MapDerivedType] arm has
                    // already told us how the runtime type is dispatched.
                    if (!allowInterfaceSrc && HasDerivedTypesInCompilation(compilation, srcType))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            DiagnosticDescriptors.PolymorphicSourceMayDropMembers,
                            location,
                            srcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                    }

                    // C1: pass the effective autoNest value so the drain loop uses it for the pair's body.
                    var synthName = nestedRegistry.GetOrReserve(srcType, namedTgt, location, autoNest);
                    if (synthName is not null)
                    {
                        converterMethod = synthName;
                        return true;
                    }
                    // GetOrReserve returned null → cap exceeded; DWARF031 will be reported after drain.
                    // Fall through to DWARF005.
                }
                else if (!allowInterfaceSrc && IsAbstractOrInterfaceAutoNestSource(compilation, srcType, namedTgt))
                {
                    // C2: abstract/interface source — emit DWARF033 (loud, never silent).
                    // Suppressed when allowInterfaceSrc=true (e.g. [MapDerivedType] arms where the caller
                    // explicitly opted in to mapping an interface source to a concrete DTO).
                    var srcName = srcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.AbstractSourceAutoNest, location, srcName));
                    return false;
                }
            }

            // ── User-defined conversion operators (e.g. a strong-type's `implicit operator int`) ──────────
            // The built-in classifier above excludes user-defined conversions; honor them here, LAST, so nothing
            // else is overridden. Implicit operators convert silently (the author declared them safe); explicit
            // operators are potentially lossy → DWARF038 (or a build error under ImplicitConversions = false).
            var userConv =
                UserConversionConverter.TryCreate(compilation, srcType, tgtType, synthesized, out var userConvExplicit);
            if (userConv is not null)
            {
                converterMethod = userConv;
                if (userConvExplicit)
                {
                    EmitImplicitConversionDiag(diagnostics,
                        location,
                        targetName,
                        srcType,
                        tgtType,
                        "user-defined explicit conversion operator",
                        implicitConversions);
                }

                return true;
            }

            diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.NoImplicitConversion, location, targetName));
            return false;
        }

        /// <summary>
        ///     Returns true when both <paramref name="src" /> and <paramref name="tgt" /> are named types
        ///     suitable for auto-nested-mapper synthesis. Excludes: scalars, enums, string, collection/
        ///     IEnumerable types, Nullable&lt;T&gt;, interfaces, abstract target types, and
        ///     abstract/interface source types (C2: those would silently drop derived-only members).
        ///     When <paramref name="allowInterfaceSrc" /> is <see langword="true" />, interface source types
        ///     are accepted (used by [MapDerivedType] arm resolution where the caller explicitly opts in).
        /// </summary>
        private static bool IsMappableObjectPair(
            Compilation compilation,
            ITypeSymbol src,
            INamedTypeSymbol tgt,
            bool allowInterfaceSrc = false)
        {
            // Source must be a named type (class or struct/record, not array/pointer/etc.)
            if (src is not INamedTypeSymbol namedSrc)
            {
                return false;
            }

            // Source must be Class, Struct, or (when allowed) Interface.
            // Enums (TypeKind.Enum), delegates, arrays, etc. are always excluded.
            if (allowInterfaceSrc)
            {
                if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct && namedSrc.TypeKind != TypeKind.Interface)
                {
                    return false;
                }
            }
            else
            {
                // Default: both must be Class or Struct (records are Class or Struct).
                // This also implicitly excludes enums (TypeKind.Enum), interfaces (TypeKind.Interface),
                // delegates, arrays, etc. — no separate enum guard needed.
                if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct)
                {
                    return false;
                }
            }

            if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            // Not scalar / special types (string, int, Guid, etc.)
            if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None)
            {
                return false;
            }

            // Not Nullable<T>
            if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            // C2: abstract SOURCE type — auto-nest would silently drop derived members.
            // We return false here so the caller can emit DWARF033 when appropriate.
            // Exception: interface sources are allowed when allowInterfaceSrc=true (caller explicitly
            // opted in via [MapDerivedType] dispatch, which is safe because the user controls the arms).
            if (!allowInterfaceSrc && namedSrc.IsAbstract)
            {
                return false;
            }

            // Not an interface target (can't construct an interface)
            if (tgt.IsAbstract)
            {
                return false;
            }

            // Not an IEnumerable / collection / dictionary — REGARDLESS of class vs struct.
            // (Struct collections like ImmutableArray<T> must NOT be object-field-mapped; they belong to
            //  CollectionConverter/DictionaryConverter, or fall to DWARF005 until supported. string is
            //  already excluded above by the SpecialType.None check.)
            if (ImplementsIEnumerable(compilation, namedSrc))
            {
                return false;
            }

            if (ImplementsIEnumerable(compilation, tgt))
            {
                return false;
            }

            // Target must have a constructible constructor (ConstructorSelector-compatible).
            // We do a lightweight check: at least one accessible non-static constructor.
            if (!tgt.InstanceConstructors.Any(c =>
                    c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic))
            {
                return false;
            }

            return true;
        }

        private static HashSet<ISymbol> BaseTypesInAssembly(Compilation compilation)
        {
            return BaseTypesByCompilation.GetValue(compilation,
                static c =>
                {
                    var set = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    foreach (var type in AllTypesIn(c.Assembly.GlobalNamespace))
                        for (var b = type.BaseType; b is not null; b = b.BaseType)
                            set.Add(b.OriginalDefinition);
                    return set;
                });
        }

        private static bool HasDerivedTypesInCompilation(Compilation compilation, ITypeSymbol src)
        {
            if (src is not INamedTypeSymbol { TypeKind: TypeKind.Class } namedSrc)
            {
                return false;
            }

            if (namedSrc.IsSealed || namedSrc.IsAbstract)
            {
                return false;
            }

            if (namedSrc.SpecialType != SpecialType.None)
            {
                return false;
            }

            // "Something derives from namedSrc" — a type is never its own BaseType, so the self-comparison the old
            // loop had to make is structurally impossible here.
            return BaseTypesInAssembly(compilation).Contains(namedSrc.OriginalDefinition);
        }

        /// <summary>Every named type declared in this assembly, walking nested types too.</summary>
        private static IEnumerable<INamedTypeSymbol> AllTypesIn(INamespaceOrTypeSymbol root)
        {
            foreach (var member in root.GetMembers())
                switch (member)
                {
                    case INamespaceSymbol ns:
                        foreach (var t in AllTypesIn(ns)) yield return t;
                        break;

                    case INamedTypeSymbol type:
                        yield return type;
                        foreach (var t in AllTypesIn(type)) yield return t;
                        break;
                }
        }

        private static bool IsAbstractOrInterfaceAutoNestSource(
            Compilation compilation,
            ITypeSymbol src,
            INamedTypeSymbol tgt)
        {
            if (src is not INamedTypeSymbol namedSrc)
            {
                return false;
            }

            // Must pass all other IsMappableObjectPair gates except the IsAbstract-source check
            if (namedSrc.TypeKind != TypeKind.Class && namedSrc.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            if (tgt.TypeKind != TypeKind.Class && tgt.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            if (namedSrc.SpecialType != SpecialType.None || tgt.SpecialType != SpecialType.None)
            {
                return false;
            }

            if (namedSrc.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            if (tgt.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                return false;
            }

            if (tgt.IsAbstract)
            {
                return false;
            }

            if (ImplementsIEnumerable(compilation, namedSrc))
            {
                return false;
            }

            if (ImplementsIEnumerable(compilation, tgt))
            {
                return false;
            }

            if (!tgt.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic))
            {
                return false;
            }

            // The source is abstract (class abstract) or interface — this is the C2 trigger
            return namedSrc.IsAbstract;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> implements <c>IEnumerable</c> (generic or non-generic),
        ///     which means it is a collection/sequence type that belongs to CollectionConverter/DictionaryConverter.
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
            {
                return true;
            }

            // Check whether the type or any of its interfaces is IEnumerable<T> or IEnumerable
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                {
                    return true;
                }

                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> is collection-shaped (implements IEnumerable,
        ///     is not string, is not already handled by CollectionConverter or DictionaryConverter)
        ///     → should emit DWARF027 rather than DWARF005.
        /// </summary>
        private static bool IsUnsupportedCollectionTarget(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String)
            {
                return false;
            }

            // Multi-dimensional array (not IArrayTypeSymbol with Rank > 1 check needed here)
            if (type is IArrayTypeSymbol arr && arr.Rank > 1)
            {
                return true;
            }

            if (type is not INamedTypeSymbol named)
            {
                return false;
            }

            // Check if it implements IEnumerable (non-string collection-shaped)
            foreach (var iface in named.AllInterfaces)
            {
                if (iface.SpecialType == SpecialType.System_Collections_IEnumerable)
                {
                    return true;
                }

                if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return true;
                }
            }

            // Also check the type itself (IEnumerable<T> as named type)
            if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Every method name dedicated to a member by a <c>Use=</c> anywhere on this mapper — class-level
        ///     pair-scoped attributes and per-method ones alike.
        /// </summary>
        /// <remarks>
        ///     These are withheld from signature-based auto-adoption. Naming a converter for a member states
        ///     that it belongs to that member; it is not an offer to convert every pair of those types. The
        ///     scan is deliberately generic over the named argument rather than per-attribute-type, so it
        ///     covers <c>[MapProperty]</c>, <c>[MapValue]</c> and their generic pair-scoped forms uniformly,
        ///     and keeps working if another attribute gains a <c>Use=</c>.
        /// </remarks>
        /// <summary>
        ///     True when the symbol carries <c>[SuppressMessage(…, "DWARFxxx…")]</c> for the given id.
        /// </summary>
        /// <remarks>
        ///     Generator-reported diagnostics do not pass through Roslyn's pragma filtering, so
        ///     <c>#pragma warning disable</c> cannot reach them — that is a platform limitation, not something
        ///     this generator can fix. <c>[SuppressMessage]</c> however is an ordinary attribute the generator
        ///     can read, so honouring it restores an in-FILE escape hatch next to the deliberate code, instead
        ///     of forcing an <c>.editorconfig</c> entry that disables the rule for the whole project.
        ///     <para>
        ///         The checkId is matched by prefix because the conventional spelling carries a description
        ///         after a colon: <c>"DWARF076:Source and target are the same type"</c>.
        ///     </para>
        /// </remarks>
        private static bool HasSuppressMessage(ISymbol symbol, string diagnosticId)
        {
            foreach (var a in symbol.GetAttributes())
            {
                if (!string.Equals(a.AttributeClass?.Name, "SuppressMessageAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                if (a.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                if (a.ConstructorArguments[1].Value is string checkId && (string.Equals(checkId, diagnosticId, StringComparison.Ordinal) || checkId.StartsWith(diagnosticId + ":", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> CollectReservedConverterNames(INamedTypeSymbol classSymbol)
        {
            var reserved = new HashSet<string>(StringComparer.Ordinal);

            static void Scan(HashSet<string> into, ImmutableArray<AttributeData> attrs)
            {
                foreach (var a in attrs)
                foreach (var na in a.NamedArguments)
                    if (string.Equals(na.Key, "Use", StringComparison.Ordinal) && na.Value.Value is string s && !string.IsNullOrEmpty(s))
                    {
                        into.Add(s);
                    }
            }

            // [MapConstructor<S,T>("Factory")] names the factory as a CONSTRUCTOR argument, not a named one,
            // so the Use= scan above cannot see it. It must be reserved for the same reason and with more
            // force: a factory only CONSTRUCTS its pair's target, after which the pair assigns members.
            // Adopted as a general element converter it produces an object with nothing filled in — the
            // collection case that surfaced this returned an Empty singleton, so a whole collection mapped
            // to blanks, silently and with a green build.
            static void ScanCtorFactories(HashSet<string> into, ImmutableArray<AttributeData> attrs)
            {
                foreach (var a in attrs)
                {
                    if (!string.Equals(a.AttributeClass?.Name, KnownNames.MapConstructor, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var ca in a.ConstructorArguments)
                        if (ca.Value is string s && !string.IsNullOrEmpty(s))
                        {
                            into.Add(s);
                        }
                }
            }

            Scan(reserved, classSymbol.GetAttributes());
            ScanCtorFactories(reserved, classSymbol.GetAttributes());
            foreach (var member in classSymbol.GetMembers())
            {
                Scan(reserved, member.GetAttributes());
                ScanCtorFactories(reserved, member.GetAttributes());
            }

            return reserved;
        }

        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMethods(
            INamedTypeSymbol classSymbol)
        {
            var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 1)
                {
                    methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
                }

            return methods;
        }

        /// <summary>
        ///     Collects parameterless, non-void methods on the mapper — the candidate value providers for
        ///     <c>[MapValue(Use = nameof(...))]</c>. Returns <c>(Name, ReturnType)</c> pairs.
        /// </summary>
        private static List<(string Name, ITypeSymbol ReturnType)> CollectValueProviders(INamedTypeSymbol classSymbol)
        {
            var providers = new List<(string Name, ITypeSymbol ReturnType)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary && !m.ReturnsVoid && m.Parameters.Length == 0)
                {
                    providers.Add((m.Name, m.ReturnType));
                }

            return providers;
        }

        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> CollectMapperMethods(
            INamedTypeSymbol classSymbol)
        {
            var methods = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
                if (m.MethodKind == MethodKind.Ordinary &&
                    m.IsPartialDefinition &&
                    !m.ReturnsVoid &&
                    m.Parameters.Length == 1 &&
                    m.ReturnType is INamedTypeSymbol)
                {
                    methods.Add((m.Name, m.Parameters[0].Type, m.ReturnType));
                }

            return methods;
        }

        /// <summary>
        ///     Turns the class's <c>[GenerateMap&lt;S,T&gt;]</c> pairs into resolution candidates, so an element or
        ///     member of those types routes through the declared pair instead of a freshly synthesized mapper that
        ///     cannot see its configuration.
        /// </summary>
        /// <remarks>
        ///     A pair that a declared partial method already covers is skipped rather than added alongside it.
        ///     Seeding both would put two matching candidates in front of the ambiguity check and turn a shape
        ///     that is legal today — declare the pair, then declare a partial method for it — into a
        ///     <c>DWARF013</c>. The declared method wins, which is the same precedence the rest of resolution uses.
        /// </remarks>
        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> GeneratedPairCandidates(
            IReadOnlyList<(ITypeSymbol Src, INamedTypeSymbol Tgt)> genPairs,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> declared)
        {
            var candidates = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>();
            foreach (var (src, tgt) in genPairs)
            {
                var alreadyDeclared = false;
                foreach (var d in declared)
                    if (SymbolEqualityComparer.Default.Equals(d.ParamType, src) && SymbolEqualityComparer.Default.Equals(d.ReturnType, tgt))
                    {
                        alreadyDeclared = true;
                        break;
                    }

                // Also skip a pair we have already added: [GenerateMap] twice over the same types is a
                // duplicate, not an ambiguity, and it must not become one here.
                if (!alreadyDeclared)
                {
                    foreach (var c in candidates)
                        if (SymbolEqualityComparer.Default.Equals(c.ParamType, src) && SymbolEqualityComparer.Default.Equals(c.ReturnType, tgt))
                        {
                            alreadyDeclared = true;
                            break;
                        }
                }

                // "Map" is the name the [GenerateMap] emission loop gives every pair it produces.
                if (!alreadyDeclared)
                {
                    candidates.Add(("Map", src, tgt));
                }
            }

            return candidates;
        }

        /// <summary>
        ///     The candidate list minus the pair being resolved.
        /// </summary>
        /// <remarks>
        ///     Only matters for a pair resolved as a whole rather than member by member — a top-level collection,
        ///     dictionary or value-like <c>[GenerateMap]</c>. Left in, the pair finds ITSELF:
        ///     <c>
        ///         [GenerateMap&lt;int,
        ///         long&gt;]
        ///     </c>
        ///     would resolve its own <c>Map</c> as its converter and emit <c>return Map(src);</c>,
        ///     which compiles, reports nothing, and recurses until the stack ends.
        /// </remarks>
        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> ExcludingPair(
            List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> candidates,
            ITypeSymbol src,
            ITypeSymbol tgt)
        {
            var result = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>(candidates.Count);
            foreach (var c in candidates)
                if (!SymbolEqualityComparer.Default.Equals(c.ParamType, src) || !SymbolEqualityComparer.Default.Equals(c.ReturnType, tgt))
                {
                    result.Add(c);
                }

            return result;
        }

        /// <summary>
        ///     The candidate list minus ONE named method — the one currently being resolved.
        /// </summary>
        /// <remarks>
        ///     Narrower than <see cref="ExcludingPair" /> and necessary where a SIBLING may legitimately share the
        ///     signature. A <c>[MapDerivedType]</c> dispatcher and its base arm are exactly that: two methods with
        ///     the same parameter and return types and different names, which C# allows and <c>DWARF060</c> does
        ///     not object to. Excluding by signature removed the arm along with the dispatcher, so the arm had
        ///     nowhere to resolve and the pair failed its completeness gate on members the sibling configures.
        ///     Found by converting a CLEAN-architecture corpus off AutoMapper, where a base and a derived source
        ///     mapping to one DTO is the ordinary shape rather than an edge case.
        /// </remarks>
        private static List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> ExcludingMethod(
            List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> candidates,
            string name,
            ITypeSymbol src,
            ITypeSymbol tgt)
        {
            var result = new List<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)>(candidates.Count);
            foreach (var c in candidates)
                if (!string.Equals(c.Name, name, StringComparison.Ordinal) || !SymbolEqualityComparer.Default.Equals(c.ParamType, src) || !SymbolEqualityComparer.Default.Equals(c.ReturnType, tgt))
                {
                    result.Add(c);
                }

            return result;
        }

        private static (List<(string Name, ITypeSymbol ParamType)> Before,
            List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)> After)
            CollectHooks(INamedTypeSymbol classSymbol, List<DiagnosticInfo> diagnostics)
        {
            var before = new List<(string Name, ITypeSymbol ParamType)>();
            var after = new List<(string Name, ITypeSymbol P0, ITypeSymbol? P1, RefKind TargetRefKind)>();
            foreach (var m in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                var isBefore = m.GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == KnownNames.BeforeMapFqn);
                var isAfter = m.GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == KnownNames.AfterMapFqn);
                if (!isBefore && !isAfter)
                {
                    continue;
                }

                var loc = LocationInfo.From(m.Locations.FirstOrDefault() ?? Location.None);

                // A partial declaration whose implementing part is absent has no body: C# erases it and every call
                // to it, and where THIS generator supplies the missing part the call is into the method being
                // generated. Either way there is nothing for a hook to run, so it is refused (DWARF091) before the
                // signature is looked at — the shape is wrong regardless of what the signature happens to be, and
                // checking void-ness first produced three different wrong answers for one mistake: DWARF018 on the
                // create map, silence on the span map, and `Update(s, d);` emitted inside `Update` on the update
                // map. See the descriptor's remarks, and surface-matrix finding D16.
                if (m.IsPartialDefinition && m.PartialImplementationPart is null)
                {
                    // Once per attribute written, not once per method: the two are separate mistakes with
                    // separate remedies, and a method can carry both.
                    if (isBefore)
                    {
                        diagnostics.Add(NoBodyHook("BeforeMap", "void Hook(TSource)"));
                    }

                    if (isAfter)
                    {
                        diagnostics.Add(NoBodyHook("AfterMap", "void Hook(TTarget) or void Hook(TSource, TTarget)"));
                    }

                    continue;

                    DiagnosticInfo NoBodyHook(string attribute, string shape)
                    {
                        return new DiagnosticInfo(
                            DiagnosticDescriptors.HookOnMethodWithNoBody,
                            loc,
                            $"[{attribute}] is written on '{m.Name}', a partial method with no implementing part, so " +
                            "it has no body to run — C# erases such a method and every call to it, and where " +
                            "DwarfMapper supplies the missing part the call would re-enter the method being " +
                            $"generated. The hook is not registered. Move [{attribute}] to an ordinary method on the " +
                            $"mapper with the hook shape ({shape}), or remove it.");
                    }
                }

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
                        // 1-param: the sole parameter is the target; capture its RefKind
                    {
                        after.Add((m.Name, m.Parameters[0].Type, null, m.Parameters[0].RefKind));
                    }
                    else if (m.Parameters.Length == 2)
                        // 2-param: P0=source, P1=target; capture P1's RefKind
                    {
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

        /// <summary>
        ///     Returns true when <paramref name="type" /> is a destination that CAN HOLD NULL — either a
        ///     <c>Nullable&lt;U&gt;</c> or a nullable-annotated reference — and yields the type a converter must
        ///     produce (<c>U</c> for <c>Nullable&lt;U&gt;</c>; the un-annotated reference type otherwise).
        ///     <para>
        ///         This is the predicate that decides whether a nullable source may be LIFTED (null → null) rather
        ///         than unwrapped with <c>?? throw</c>. It asks the only question that matters — can the destination
        ///         store the null? — instead of the destination's KIND, which is what made the same member throw or
        ///         lift depending on whether the mirrored node happened to be a struct or a class (TASKS.md I7).
        ///     </para>
        ///     <para>
        ///         Annotation-strict on the reference side on purpose: an un-annotated (or oblivious) reference target
        ///         is a promise that it holds no null, and the documented <c>NullStrategy</c> contract — nullable
        ///         source into a non-nullable target throws, or takes the default — continues to govern it.
        ///     </para>
        /// </summary>
        private static bool TryGetNullableCapableTarget(ITypeSymbol type, out ITypeSymbol underlying)
        {
            if (IsNullableValue(type, out underlying))
            {
                return true;
            }

            if (IsNullableReferenceType(type))
            {
                // Hand the converter resolver the un-annotated form: the conversion S → D is the same question
                // whether or not the destination slot is annotated, and stripping it keeps converter matching
                // (which compares symbols) from depending on an annotation the resolver does not model.
                underlying = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                return true;
            }

            underlying = type;
            return false;
        }

        /// <summary>
        ///     Returns true when <paramref name="type" /> is a nullable reference type
        ///     (i.e. a reference type with <c>NullableAnnotation.Annotated</c>), e.g. <c>List&lt;int&gt;?</c>.
        ///     Used to decide whether AsNull semantics are safe for a given target field (A3).
        /// </summary>
        private static bool IsNullableReferenceType(ITypeSymbol type)
        {
            return type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated;
        }


        /// <summary>
        ///     True when a source member of this type may be null from the compiler's point of view — a
        ///     reference type that is nullable-annotated OR oblivious (<c>#nullable disable</c> context).
        ///     Drives the null-forgiving <c>!</c> at synthesized-converter call sites: only such sources can
        ///     trip CS8604 when the converter's parameter is non-nullable. Value types (enums, numerics,
        ///     <c>Nullable&lt;T&gt;</c>) and non-nullable references never need it, so the emitter omits the
        ///     otherwise-spurious <c>!</c> for them. <see cref="MemberMap.SourceIsNullableRef" />.
        /// </summary>
        private static bool SourceMayBeNullRef(ITypeSymbol type)
        {
            return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.NotAnnotated;
        }

        /// <summary>
        ///     True when a nullable-annotated REFERENCE source is being assigned to a NON-nullable reference
        ///     target — i.e. exactly the case in which the C# compiler raises CS8601 on the generated assignment.
        ///     Drives DWARF070 and the null-forgiving <c>!</c> that keeps CS8601 out of the generated file.
        ///     <para>
        ///         Deliberately strict on BOTH sides (<c>== Annotated</c> / <c>== NotAnnotated</c>) rather than the
        ///         looser <c>!= NotAnnotated</c> used by <see cref="SourceMayBeNullRef" />. NullableAnnotation has
        ///         three states, and the third — <c>None</c>, "oblivious", a type written in a <c>#nullable disable</c>
        ///         context — means the user opted out of nullable analysis. The compiler emits no CS8601 there, so
        ///         neither do we: an oblivious codebase would otherwise be flooded with warnings about a contract it
        ///         never opted into. This predicate tracks the compiler exactly.
        ///     </para>
        /// </summary>
        private static bool NullRefIntoNonNullableRef(ITypeSymbol srcType, ITypeSymbol tgtType)
        {
            return srcType.IsReferenceType && srcType.NullableAnnotation == NullableAnnotation.Annotated && tgtType.IsReferenceType && tgtType.NullableAnnotation == NullableAnnotation.NotAnnotated;
        }

        /// <summary>
        ///     True when this member resolves to a <b>raw</b> assignment of a nullable reference into a
        ///     non-nullable one — the only shape that actually emits CS8601. A converter or a NullHandling
        ///     strategy means the emitter routes the value through something that deals with the null
        ///     (<c>Conv(src.X!)</c>, <c>src.X ?? throw</c>, …), so neither the <c>!</c> nor DWARF070 applies there.
        /// </summary>
        private static bool IsDirectNullRefAssign(
            string? converterMethod,
            NullHandling nullHandling,
            ITypeSymbol srcType,
            ITypeSymbol tgtType)
        {
            return converterMethod is null && nullHandling == NullHandling.None && NullRefIntoNonNullableRef(srcType, tgtType);
        }

        /// <summary>
        ///     True when <paramref name="converterMethod" /> resolves to a USER-declared map/converter method whose
        ///     parameter is a non-nullable reference type. This is the fact the emitter's <c>IsSynthesized</c> proxy
        ///     stood in for: a synthesized nested mapper (<c>__DwarfMap_Obj_</c>) has a non-nullable parameter and
        ///     null-guards internally, so a nullable source argument is null-forgiven; a user-declared nested map
        ///     (<c>MapFlat(FlatSrc s)</c>) has the same non-nullable parameter but is invisible to
        ///     <c>IsSynthesized</c>, so its call was left as <c>MapFlat(s.Inner)</c> — CS8604. Looking the converter
        ///     up in the user's own method lists recovers the parameter nullability the proxy discarded.
        ///     <para>
        ///         A null-TOLERANT user converter (declared with a nullable parameter) returns false here, so it is never
        ///         forgiven and keeps receiving the null it was written to accept.
        ///     </para>
        /// </summary>
        private static bool ConverterParamIsNonNullableRef(
            string? converterMethod,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods)
        {
            if (converterMethod is null)
            {
                return false;
            }

            static bool IsNonNullableRefParam(ITypeSymbol p)
            {
                return p.IsReferenceType && p.NullableAnnotation == NullableAnnotation.NotAnnotated;
            }

            foreach (var m in autoCandidates)
                if (string.Equals(m.Name, converterMethod, StringComparison.Ordinal) && IsNonNullableRefParam(m.ParamType))
                {
                    return true;
                }

            foreach (var m in allMethods)
                if (string.Equals(m.Name, converterMethod, StringComparison.Ordinal) && IsNonNullableRefParam(m.ParamType))
                {
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     The single decision for EVERY <see cref="MemberMap" /> construction site (auto-match member, explicit
        ///     [MapProperty], flatten, and both constructor-argument paths): does a nullable-reference source flowing
        ///     into a user-declared converter with a non-nullable parameter need null-forgiving, AND — coupled here so
        ///     no site can forgive without also signalling — does it warrant DWARF070? Gated on the DESTINATION being
        ///     non-nullable (<see cref="NullRefIntoNonNullableRef" />): a nullable destination legitimately propagates
        ///     the null. Emits DWARF070 exactly when it returns true, so the emitter's <c>!</c> and the diagnostic can
        ///     never diverge across sites. Returning the flag is what the emitter reads via
        ///     <c>MemberMap.ConverterParamIsNonNullableRef</c>.
        ///     See docs/superpowers/specs/2026-07-25-nested-nullable-parameter.md.
        /// </summary>
        private static bool ForgiveNestedNullableArg(
            string? converterMethod,
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> autoCandidates,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> allMethods,
            string sourceName,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics)
        {
            var forgive = NullRefIntoNonNullableRef(srcType, tgtType) && ConverterParamIsNonNullableRef(converterMethod, autoCandidates, allMethods);
            if (forgive)
            {
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.NullableRefSourceToNonNullableTarget,
                    location,
                    sourceName));
            }

            return forgive;
        }

        // Enumeration lives in Core.MemberFacts so both engines share one implementation. These wrappers keep the
        // class model's existing (Name, Type) shape so its 31 call sites are untouched by the move.
        //
        // ISSUE-044: both parameters are REQUIRED on purpose. They used to default to (null, false), and twenty
        // call sites silently took that default — so those paths answered "which members can I read?" as if the
        // mapper had never opted into AllowNonPublic, producing false DWARF043/DWARF045/DWARF001 on legal code.
        // A required parameter turns "I forgot to thread the flag" from a silent wrong answer into a compile
        // error at the call site, which is the only version of this that stays fixed.
        private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(
            ITypeSymbol type,
            Compilation? compilation,
            bool allowNonPublic)
        {
            foreach (var m in MemberFacts.Readable(type, compilation, allowNonPublic))
                yield return (m.Name, m.Type);
        }

        private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(
            ITypeSymbol type,
            Compilation? compilation,
            bool allowNonPublic)
        {
            foreach (var m in MemberFacts.Writable(type, compilation, allowNonPublic))
                yield return (m.Name, m.Type);
        }
    }
}
