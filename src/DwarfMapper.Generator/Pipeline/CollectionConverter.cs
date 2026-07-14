// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static class CollectionConverter
{
    // ── Emitters ─────────────────────────────────────────────────────────────

    // Shared preserve-mode signature suffix: (DwarfRefContext ctx, int depth)
    private const string CtxDepthParams = ", global::DwarfMapper.DwarfRefContext ctx, int depth";

    // ── SIMD widening for primitive array conversions ────────────────────────────
    // The seven element pairs that System.Numerics.Vector.Widen supports — each is a PROVABLY-LOSSLESS
    // same-signedness widening (smaller → larger of the same kind), so the vectorized result is bit-for-bit
    // identical to the scalar implicit widening. Anything else (sign changes, narrowing, multi-step,
    // nullable, non-primitive) is NOT in this set and falls back to the element loop / CreateChecked path.
    private static readonly (SpecialType Src, SpecialType Tgt)[] WidenPairs =
    {
        (SpecialType.System_Byte, SpecialType.System_UInt16),
        (SpecialType.System_UInt16, SpecialType.System_UInt32),
        (SpecialType.System_UInt32, SpecialType.System_UInt64),
        (SpecialType.System_SByte, SpecialType.System_Int16),
        (SpecialType.System_Int16, SpecialType.System_Int32),
        (SpecialType.System_Int32, SpecialType.System_Int64),
        (SpecialType.System_Single, SpecialType.System_Double)
    };

    // Nullable-aware fully-qualified format — includes ? on nullable reference type arguments.
    // Used for PARAMETER types so the helper signature accepts nullable-annotated source types
    // (e.g. Dictionary<string, List<int>?>) without a CS8620 mismatch.
    private static readonly SymbolDisplayFormat NullableFullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // ─── Projection-translatability classification ────────────────────────────
    /// <summary>
    ///     Returns true when the target collection kind is safe to inline into an
    ///     IQueryable projection expression (EF-Core-translatable). Used by Part D.
    /// </summary>
    public static bool IsTargetKindTranslatable(TargetKind kind)
    {
        return kind switch
        {
            TargetKind.Array => true,
            TargetKind.List => true,
            TargetKind.IEnumerable => true,
            TargetKind.ICollection => true,
            TargetKind.IList => true,
            TargetKind.IReadOnlyList => true,
            TargetKind.IReadOnlyCollection => true,
            _ => false // HashSet/ISet/IReadOnlySet/immutables
        };
    }

    // ─── Detection ────────────────────────────────────────────────────────────

    /// <summary>Detect a supported collection mapping pair and its element types.</summary>
    public static bool TryResolve(ITypeSymbol src, ITypeSymbol tgt,
        out ITypeSymbol srcElem, out ITypeSymbol tgtElem, out Shape shape,
        bool nullAsNull = false)
    {
        srcElem = src;
        tgtElem = tgt;
        shape = default;

        // Strings are IEnumerable<char> but must be treated as scalars.
        if (src.SpecialType == SpecialType.System_String || tgt.SpecialType == SpecialType.System_String)
            return false;

        // A2: Nullable<ImmutableArray<T>> (i.e. ImmutableArray<T>?) is a nullable value-type struct.
        // Unwrap it and set nullAsNull=true so the helper returns ImmutableArray<T>? (null for null input).
        if (tgt is INamedTypeSymbol tgtNullable
            && tgtNullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && tgtNullable.TypeArguments.Length == 1)
        {
            var inner = tgtNullable.TypeArguments[0];
            if (IsExactNamedType(inner, "ImmutableArray", "System.Collections.Immutable", 1, out _))
                // Recurse with the unwrapped target and nullAsNull=true.
                return TryResolve(src, inner, out srcElem, out tgtElem, out shape, true);
        }

        TargetKind targetKind;

        // ── Concrete T[] ─────────────────────────────────────────────────────
        if (tgt is IArrayTypeSymbol tArr)
        {
            if (tArr.Rank != 1) return false; // multi-dim → DWARF027
            tgtElem = tArr.ElementType;
            targetKind = TargetKind.Array;
        }
        // ── Concrete List<T> ─────────────────────────────────────────────────
        else if (IsExactNamedType(tgt, "List", "System.Collections.Generic", 1, out var le))
        {
            tgtElem = le!;
            targetKind = TargetKind.List;
        }
        // ── Concrete HashSet<T> ──────────────────────────────────────────────
        else if (IsExactNamedType(tgt, "HashSet", "System.Collections.Generic", 1, out var he))
        {
            tgtElem = he!;
            targetKind = TargetKind.HashSet;
        }
        // ── IEnumerable<T> (LAZY target) ─────────────────────────────────────
        else if (IsIEnumerableT(tgt, out var ee))
        {
            tgtElem = ee!;
            targetKind = TargetKind.IEnumerable;
        }
        // ── ICollection<T> / IList<T> → List<T> ─────────────────────────────
        else if (IsExactNamedType(tgt, "ICollection", "System.Collections.Generic", 1, out var ce))
        {
            tgtElem = ce!;
            targetKind = TargetKind.ICollection;
        }
        else if (IsExactNamedType(tgt, "IList", "System.Collections.Generic", 1, out var ile))
        {
            tgtElem = ile!;
            targetKind = TargetKind.IList;
        }
        // ── IReadOnlyList<T> / IReadOnlyCollection<T> → List<T> ─────────────
        else if (IsExactNamedType(tgt, "IReadOnlyList", "System.Collections.Generic", 1, out var rle))
        {
            tgtElem = rle!;
            targetKind = TargetKind.IReadOnlyList;
        }
        else if (IsExactNamedType(tgt, "IReadOnlyCollection", "System.Collections.Generic", 1, out var rce))
        {
            tgtElem = rce!;
            targetKind = TargetKind.IReadOnlyCollection;
        }
        // ── ISet<T> / IReadOnlySet<T> → HashSet<T> ───────────────────────────
        else if (IsExactNamedType(tgt, "ISet", "System.Collections.Generic", 1, out var ise))
        {
            tgtElem = ise!;
            targetKind = TargetKind.ISet;
        }
        else if (IsExactNamedType(tgt, "IReadOnlySet", "System.Collections.Generic", 1, out var rse))
        {
            tgtElem = rse!;
            targetKind = TargetKind.IReadOnlySet;
        }
        // ── ImmutableArray<T> ────────────────────────────────────────────────
        else if (IsExactNamedType(tgt, "ImmutableArray", "System.Collections.Immutable", 1, out var iae))
        {
            tgtElem = iae!;
            targetKind = TargetKind.ImmutableArray;
        }
        // ── ImmutableList<T> / IImmutableList<T> ─────────────────────────────
        else if (IsExactNamedType(tgt, "ImmutableList", "System.Collections.Immutable", 1, out var imle))
        {
            tgtElem = imle!;
            targetKind = TargetKind.ImmutableList;
        }
        else if (IsExactNamedType(tgt, "IImmutableList", "System.Collections.Immutable", 1, out var iimle))
        {
            tgtElem = iimle!;
            targetKind = TargetKind.IImmutableList;
        }
        // ── ImmutableHashSet<T> / IImmutableSet<T> ───────────────────────────
        else if (IsExactNamedType(tgt, "ImmutableHashSet", "System.Collections.Immutable", 1, out var imhe))
        {
            tgtElem = imhe!;
            targetKind = TargetKind.ImmutableHashSet;
        }
        else if (IsExactNamedType(tgt, "IImmutableSet", "System.Collections.Immutable", 1, out var iimhe))
        {
            tgtElem = iimhe!;
            targetKind = TargetKind.IImmutableSet;
        }
        else
        {
            return false;
        }

        if (!TryGetEnumerableElement(src, out srcElem, out var count))
            return false;

        shape = new Shape(targetKind, src is IArrayTypeSymbol, count, nullAsNull);
        return true;
    }

    // ─── Preserve-mode mutable collection gate ───────────────────────────────

    /// <summary>
    ///     Returns true for mutable reference-type collection kinds that can participate in the
    ///     reference-identity graph under <c>ReferenceHandling = Preserve</c>.
    ///     Immutables (struct ImmutableArray, ImmutableList, ImmutableHashSet, etc.) and
    ///     lazy IEnumerable cannot be registered before filling, so they are excluded.
    /// </summary>
    public static bool IsMutableReferenceCollection(TargetKind kind)
    {
        return kind switch
        {
            TargetKind.List => true,
            TargetKind.HashSet => true,
            TargetKind.ICollection => true,
            TargetKind.IList => true,
            TargetKind.IReadOnlyList => true,
            TargetKind.IReadOnlyCollection => true,
            TargetKind.ISet => true,
            TargetKind.IReadOnlySet => true,
            TargetKind.Array => true, // reference-type; registered after allocation
            _ => false // immutables and lazy IEnumerable
        };
    }

    // ─── Synthesis ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Synthesize (or reuse) the collection-mapping method and return its name.
    /// </summary>
    /// <param name="isPreserve">
    ///     When true, mutable reference collections register-before-fill in the DwarfRefContext identity
    ///     map; immutables still thread ctx/depth to recursion-capable element converters.
    ///     Produces a distinct helper (name contains <c>_p</c> tag) so Preserve and None helpers
    ///     for the same collection type coexist in the same mapper class.
    /// </param>
    /// <param name="elemNeedsCtx">
    ///     When true, the element converter requires <c>(ctx, depth+1)</c> extra arguments.
    ///     This is true when the element itself is a recursion-capable synthesized object mapper.
    /// </param>
    public static string Synthesize(
        Dictionary<string, SynthesizedMethod> synth,
        ITypeSymbol srcType, ITypeSymbol srcElem, ITypeSymbol tgtElem, Shape shape,
        string? elemConverter, NullHandling elemNull,
        bool isPreserve = false, bool elemNeedsCtx = false)
    {
        // Use nullable-aware format for element type so emitted container types and Add() calls match
        // nullable element types (e.g. List<List<int>?> not List<List<int>>).
        var elemFq = FqTypeArg(tgtElem);
        var srcFq = Fq(srcType);
        // Nullable-aware param type: strips outer nullable annotation then re-adds ?, preserving
        // inner nullable type arguments (e.g. Dictionary<string, List<int>?> → Dictionary<string, List<int>?>?).
        var srcParamType = FqNullableParam(srcType);
        var nullTag = shape.NullAsNull ? "_nn" : "";
        // Helper-name tag encodes which body variant is emitted (find-or-build memoizes by name):
        //   "_p" = Preserve (threads ctx AND register-before-fill) — UNCHANGED from before.
        //   "_c" = ctx-threaded only, NO register-before-fill — the None/SetNull recursive-element
        //          variant (a re-entrant element breaks via its own on-stack/depth guard).
        //   ""   = plain (no ctx, no register).
        // Keeping the Preserve branch byte-identical means Preserve helper names/snapshots don't move.
        var preserveTag = isPreserve && (IsMutableReferenceCollection(shape.Target) || elemNeedsCtx) ? "_p"
            : elemNeedsCtx ? "_c" : "";

        var targetTag = shape.Target switch
        {
            TargetKind.Array => elemFq + "[]",
            TargetKind.List => "List<" + elemFq + ">",
            TargetKind.HashSet => "HashSet<" + elemFq + ">",
            TargetKind.IEnumerable => "IEnumerable<" + elemFq + ">",
            TargetKind.ICollection => "ICollection<" + elemFq + ">",
            TargetKind.IList => "IList<" + elemFq + ">",
            TargetKind.IReadOnlyList => "IReadOnlyList<" + elemFq + ">",
            TargetKind.IReadOnlyCollection => "IReadOnlyCollection<" + elemFq + ">",
            TargetKind.ISet => "ISet<" + elemFq + ">",
            TargetKind.IReadOnlySet => "IReadOnlySet<" + elemFq + ">",
            TargetKind.ImmutableArray => "ImmutableArray<" + elemFq + ">",
            TargetKind.ImmutableList => "ImmutableList<" + elemFq + ">",
            TargetKind.IImmutableList => "IImmutableList<" + elemFq + ">",
            TargetKind.ImmutableHashSet => "ImmutableHashSet<" + elemFq + ">",
            _ => "IImmutableSet<" + elemFq + ">"
        };

        var name = GeneratedNames.Collection + Hash(srcFq + "=>" + targetTag + nullTag + preserveTag);
        if (synth.ContainsKey(name))
            return name;

        var identity = SymbolEqualityComparer.Default.Equals(srcElem, tgtElem)
                       && elemConverter is null
                       && elemNull == NullHandling.None;
        var item = ElementExpr("__item", elemConverter, elemNull, elemNeedsCtx);

        // Effective preserve: are we emitting register-before-fill for THIS collection? (Preserve only.)
        var registerBeforeFill = isPreserve && IsMutableReferenceCollection(shape.Target);
        // Thread the (ctx, depth) signature whenever we register OR the element itself is
        // recursion-capable (Preserve, or None/SetNull with a self-referential element). This is the
        // single fix that lets the depth guard / SetNull on-stack guard reach cycles routed through
        // a collection edge — without turning on register-before-fill outside Preserve.
        var threadCtx = registerBeforeFill || elemNeedsCtx;

        var sb = new StringBuilder();
        EmitBody(sb, name, srcFq, srcParamType, srcType, srcElem, elemFq, item, shape, identity, threadCtx,
            registerBeforeFill);

        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    /// <summary>
    ///     Re-emits an EXISTING collection helper (keyed by <paramref name="existingName" />) so it threads
    ///     <c>(ctx, depth)</c> and routes each element through <paramref name="ctxElementConverter" />
    ///     (a recursion-capable method — typically a <c>__DwarfMap_Depth_*</c> companion). Used by the
    ///     post-pass when a None-mode collection's element method is discovered to be self-recursive:
    ///     the originally-emitted body (which called the public entry on a fresh context → StackOverflow)
    ///     is overwritten in place with a depth-guarded, ctx-threaded body. Never register-before-fills
    ///     (that is Preserve-only). Overwriting in place keeps the helper name stable, so existing member
    ///     references stay valid — they just gain <c>ConverterNeedsDepthCtx = true</c>.
    /// </summary>
    public static void SynthesizeInPlace(
        Dictionary<string, SynthesizedMethod> synth, string existingName,
        ITypeSymbol srcType, ITypeSymbol srcElem, ITypeSymbol tgtElem, Shape shape,
        string ctxElementConverter, NullHandling elemNull)
    {
        var elemFq = FqTypeArg(tgtElem);
        var srcFq = Fq(srcType);
        var srcParamType = FqNullableParam(srcType);
        // Recursion-capable element → identity fast-path is never applicable; element call threads ctx.
        var item = ElementExpr("__item", ctxElementConverter, elemNull, true);

        var sb = new StringBuilder();
        EmitBody(sb, existingName, srcFq, srcParamType, srcType, srcElem, elemFq, item, shape,
            false, true, false);
        synth[existingName] = new SynthesizedMethod(existingName, sb.ToString());
    }

    /// <summary>Dispatches body emission to the per-shape emitter. Shared by Synthesize / SynthesizeInPlace.</summary>
    private static void EmitBody(
        StringBuilder sb, string name, string srcFq, string srcParamType, ITypeSymbol srcType,
        ITypeSymbol srcElem, string elemFq, string item, Shape shape, bool identity,
        bool threadCtx, bool registerBeforeFill)
    {
        switch (shape.Target)
        {
            case TargetKind.Array:
                EmitArray(sb, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull, threadCtx,
                    registerBeforeFill);
                break;

            case TargetKind.List:
            case TargetKind.ICollection:
            case TargetKind.IList:
            case TargetKind.IReadOnlyList:
            case TargetKind.IReadOnlyCollection:
                EmitList(sb, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull, threadCtx,
                    registerBeforeFill);
                break;

            case TargetKind.HashSet:
            case TargetKind.ISet:
            case TargetKind.IReadOnlySet:
                EmitHashSet(sb, name, srcFq, srcParamType, elemFq, item, shape, srcType, identity, shape.NullAsNull,
                    threadCtx, registerBeforeFill);
                break;

            case TargetKind.IEnumerable:
                // IEnumerable is lazy — cannot register-before-fill (no concrete instance exists).
                // Still thread ctx/depth to element if needed (via closure in the Select lambda).
                EmitLazyEnumerable(sb, name, srcFq, srcParamType, srcElem, elemFq, item, shape, identity,
                    shape.NullAsNull, threadCtx);
                break;

            case TargetKind.ImmutableArray:
                // ImmutableArray is a value-type struct — cannot be registered.
                // Thread ctx/depth to element via the fill loop.
                EmitImmutableArray(sb, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull,
                    threadCtx);
                break;

            case TargetKind.ImmutableList:
            case TargetKind.IImmutableList:
                EmitImmutableList(sb, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull,
                    threadCtx);
                break;

            case TargetKind.ImmutableHashSet:
            case TargetKind.IImmutableSet:
                EmitImmutableHashSet(sb, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull,
                    threadCtx);
                break;
        }
    }

    /// <summary>
    ///     The count-bearing capacity argument for a pre-sizable target (<c>List</c>/<c>HashSet</c>), or
    ///     <c>""</c> when the source count is not known cheaply (lazy/unknown-count). Pre-sizing avoids the
    ///     repeated internal-array doubling+copy that <c>new List&lt;T&gt;()</c> + <c>Add</c> incurs — matching
    ///     what the array (<c>new T[src.Length]</c>) and dictionary (<c>new Dictionary(src.Count)</c>) paths
    ///     already do. Over-reservation for sets (distinct &lt; source count) is harmless.
    /// </summary>
    private static string CapacityArg(Shape shape)
    {
        return shape.Count switch
        {
            CountKind.Length => "src.Length",
            CountKind.Count => "src.Count",
            _ => ""
        };
    }

    private static void EmitArray(
        StringBuilder sb, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool threadCtx, bool registerBeforeFill)
    {
        var retType = nullAsNull ? elem + "[]?" : elem + "[]";
        var paramType = srcParamType; // nullable-aware; null guard inside the body handles it
        var emptyExpr = nullAsNull ? "null" : "global::System.Array.Empty<" + elem + ">()";
        var ctxParams = threadCtx ? CtxDepthParams : "";

        sb.Append("    private ").Append(retType).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && shape.SourceIsArray && !registerBeforeFill && !threadCtx)
        {
            // identity array→array: Clone() fast-path (only safe without register-before-fill / ctx threading).
            sb.Append("        return src is null ? ")
                .Append(emptyExpr)
                .Append(" : (").Append(elem).Append("[])src.Clone();\n");
        }
        else if (shape.Count != CountKind.None)
        {
            var countExpr = shape.Count == CountKind.Length ? "src.Length" : "src.Count";
            var arrayAlloc = ArrayNewExpr(elem, countExpr);
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            if (registerBeforeFill)
                sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(elem)
                    .Append("[])__cc;\n");
            sb.Append("        var __r = ").Append(arrayAlloc).Append(";\n");
            if (registerBeforeFill) sb.Append("        ctx.SetReference(src, __r);\n");
            if (shape.SourceIsArray && !registerBeforeFill && !threadCtx)
            {
                // Non-recursive array→array (None mode): index with a single length-bounded counter over
                // src[__i] so the JIT proves BOTH the source read and the destination store in-bounds and
                // elides both bounds checks. The foreach + separate post-incremented write-index form (below)
                // leaves the store index not-provably-in-bounds, so its bounds check survives — measurably
                // slower in a hot 1000-element loop. Preserve (register-before-fill), ctx-threaded, and
                // non-array (no indexer) sources keep the foreach form unchanged.
                sb.Append("        for (int __i = 0; __i < ").Append(countExpr).Append("; __i++) { __r[__i] = ")
                    .Append(item.Replace("__item", "src[__i]")).Append("; }\n");
            }
            else
            {
                sb.Append("        var __i = 0;\n");
                sb.Append("        foreach (var __item in src) { __r[__i++] = ").Append(item).Append("; }\n");
            }

            sb.Append("        return __r;\n");
        }
        else
        {
            // Unknown-count path: must buffer first; register after allocating final array.
            // B3 fix: under Preserve, check TryGetReference BEFORE the fill loop (two parents sharing
            // the same unknown-count source must produce ONE target array, not two independent copies).
            // SetReference is called immediately after ToArray() so the second parent finds the array.
            // Note: a cycle THROUGH this array (element refers back to the source array during fill)
            // cannot be reconstructed correctly because the final array doesn't exist until after fill.
            // That degenerate case is documented: register-before-fill is structurally impossible for
            // unknown-count sources mapping to an array target. Two-parents-sharing is fully correct.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            if (registerBeforeFill)
                sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(elem)
                    .Append("[])__cc;\n");
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in src) { __buf.Add(").Append(item).Append("); }\n");
            sb.Append("        var __r = __buf.ToArray();\n");
            if (registerBeforeFill) sb.Append("        ctx.SetReference(src, __r);\n");
            sb.Append("        return __r;\n");
        }

        sb.Append("    }\n");
    }

    private static void EmitList(
        StringBuilder sb, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool threadCtx, bool registerBeforeFill)
    {
        var listFq = "global::System.Collections.Generic.List<" + elem + ">";
        var retFq = nullAsNull ? listFq + "?" : listFq;
        var emptyExpr = nullAsNull ? "null" : "new " + listFq + "()";
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var ctxParams = threadCtx ? CtxDepthParams : "";

        // Return type is always List<T> (concrete); the field type is an interface but assignable.
        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && !registerBeforeFill && !threadCtx)
        {
            sb.Append("        return src is null ? ").Append(emptyExpr)
                .Append(" : new ").Append(listFq).Append("(src);\n");
        }
        else if (registerBeforeFill)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(listFq).Append(")__cc;\n");
            sb.Append("        var __r = new ").Append(listFq).Append('(').Append(CapacityArg(shape)).Append(");\n");
            sb.Append("        ctx.SetReference(src, __r);\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }
        else
        {
            // Plain fill. When threadCtx is true (None/SetNull recursive element), `item` already
            // contains the (..., ctx, depth + 1) arguments and ctx is in scope from the signature.
            // Pre-size from the known source count (CapacityArg) so large lists don't repeatedly
            // double+copy their backing array — same rationale as the array/dictionary paths.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __r = new ").Append(listFq).Append('(').Append(CapacityArg(shape)).Append(");\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }

        sb.Append("    }\n");
    }

    private static void EmitHashSet(
        StringBuilder sb, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, ITypeSymbol srcType, bool identity, bool nullAsNull, bool threadCtx,
        bool registerBeforeFill)
    {
        var setFq = "global::System.Collections.Generic.HashSet<" + elem + ">";
        var retFq = nullAsNull ? setFq + "?" : setFq;
        var emptyExpr = nullAsNull ? "null" : "new " + setFq + "()";
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var ctxParams = threadCtx ? CtxDepthParams : "";

        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && IsHashSetType(srcType) && !registerBeforeFill && !threadCtx)
        {
            sb.Append("        return src is null ? ").Append(emptyExpr)
                .Append(" : new ").Append(setFq).Append("(src);\n");
        }
        else if (registerBeforeFill)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(setFq).Append(")__cc;\n");
            sb.Append("        var __r = new ").Append(setFq).Append('(').Append(CapacityArg(shape)).Append(");\n");
            sb.Append("        ctx.SetReference(src, __r);\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }
        else
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __r = new ").Append(setFq).Append('(').Append(CapacityArg(shape)).Append(");\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }

        sb.Append("    }\n");
    }

    private static void EmitLazyEnumerable(
        StringBuilder sb, string name, string srcFq, string srcParamType, ITypeSymbol srcElemType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        // Return type is IEnumerable<T>, MATERIALISED into a fresh List<T>.
        //
        // This used to hand back a lazy sequence — `src` itself when the element needed no transform, or an
        // un-materialised Enumerable.Select otherwise. Both leak the source across the map boundary and were
        // silent about it:
        //   * identity  → the destination WAS the source collection. Mutating either corrupted the other, and
        //                 a consumer can cast the IEnumerable<T> back to List<T> and write straight into the
        //                 source. (This is AutoMapper's v3.1 "assignable collection" bug.)
        //   * deferred  → the destination was a live query over the source: the element conversion re-ran on
        //                 EVERY enumeration (wrong for a converter with side effects, and O(n) each time), and
        //                 enumerating after the source was mutated threw InvalidOperationException. A mapped
        //                 DTO must not be a time-bomb tied to the lifetime of the entity it came from.
        //
        // Map() returns an independent value; that contract outranks saving one allocation. The zero-alloc
        // paths that are genuinely safe (blit/SIMD into a NEW buffer) are unaffected — they already copy.
        var ieFq = "global::System.Collections.Generic.IEnumerable<" + elem + ">";
        var retFq = nullAsNull ? ieFq + "?" : ieFq;
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Array.Empty<" + elem + ">()";
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        // One materialising path for both the identity and the converting case: `item` already carries the
        // element conversion (it is just `__item` when no transform is needed), so the fill is the same.
        _ = identity;
        _ = srcElemType;
        var cap = CapacityArg(shape);
        sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
        if (cap.Length > 0)
        {
            // Size is known up front (src.Length / src.Count): allocate the exact array once and fill it by
            // index. Cheapest possible independent copy — one allocation, no List growth/reallocation, no
            // per-Add bounds+version checks — and the source is enumerated exactly ONCE.
            sb.Append("        var __a = new ").Append(elem).Append('[').Append(cap).Append("];\n");
            sb.Append("        var __i = 0;\n");
            sb.Append("        foreach (var __item in src) { __a[__i++] = ").Append(item).Append("; }\n");
            sb.Append("        return __a;\n");
        }
        else
        {
            // Size unknown (a bare IEnumerable<T>): a single growing pass. Still exactly one enumeration —
            // we must never count-then-enumerate, which would double-enumerate a side-effecting sequence.
            sb.Append("        var __r = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }

        sb.Append("    }\n");
    }

    private static void EmitImmutableArray(
        StringBuilder sb, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq = "global::System.Collections.Immutable.ImmutableArray<" + elem + ">";
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        // ImmutableArray<T> is a value type. Under AsNull, we emit ImmutableArray<T>? (nullable struct)
        // so a null source maps to null (HasValue=false) rather than default(ImmutableArray<T>)
        // which would yield HasValue=true with an empty value — a silent loss.
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Collections.Immutable.ImmutableArray<" + elem + ">.Empty";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        // When the source type itself is ImmutableArray<T> (a value-type struct), the nullable
        // parameter is Nullable<ImmutableArray<T>> which does NOT implement IEnumerable<T>.
        // We must unwrap it with .GetValueOrDefault() before passing to CreateRange / foreach.
        var srcIsImmutableArrayStruct = srcFq.StartsWith(
            "global::System.Collections.Immutable.ImmutableArray<",
            StringComparison.Ordinal);
        var srcExpr = srcIsImmutableArrayStruct ? "src.GetValueOrDefault()" : "src";

        // Under AsNull, return ImmutableArray<T>? so null source yields null (HasValue=false).
        var retTypeFq = nullAsNull ? tgtFq + "?" : tgtFq;

        sb.Append("    private ").Append(retTypeFq).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && !elemNeedsCtx)
        {
            // Identity elements without ctx threading: copy straight into the ImmutableArray via CreateRange.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableArray.CreateRange(").Append(srcExpr)
                .Append(");\n");
        }
        else
        {
            // Need element conversion or ctx threading: build a List first, then CreateRange.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in ").Append(srcExpr).Append(") { __buf.Add(").Append(item)
                .Append("); }\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableArray.CreateRange(__buf);\n");
        }

        sb.Append("    }\n");
    }

    private static void EmitImmutableList(
        StringBuilder sb, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq = "global::System.Collections.Immutable.ImmutableList<" + elem + ">";
        var retFq = nullAsNull ? tgtFq + "?" : tgtFq;
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Collections.Immutable.ImmutableList<" + elem + ">.Empty";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && !elemNeedsCtx)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableList.CreateRange(src);\n");
        }
        else
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in src) { __buf.Add(").Append(item).Append("); }\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableList.CreateRange(__buf);\n");
        }

        sb.Append("    }\n");
    }

    private static void EmitImmutableHashSet(
        StringBuilder sb, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq = "global::System.Collections.Immutable.ImmutableHashSet<" + elem + ">";
        var retFq = nullAsNull ? tgtFq + "?" : tgtFq;
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Collections.Immutable.ImmutableHashSet<" + elem + ">.Empty";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
            .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && !elemNeedsCtx)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableHashSet.CreateRange(src);\n");
        }
        else
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in src) { __buf.Add(").Append(item).Append("); }\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableHashSet.CreateRange(__buf);\n");
        }

        sb.Append("    }\n");
    }

    // ─── Blit ─────────────────────────────────────────────────────────────────

    /// <summary>Synthesize a reinterpret-blit array mapper (vectorized memmove with a runtime size guard).</summary>
    public static string SynthesizeBlit(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcArrayType, ITypeSymbol srcElem, ITypeSymbol tgtElem)
    {
        var elem = Fq(tgtElem);
        var srcE = Fq(srcElem);
        var srcFq = Fq(srcArrayType);
        var name = "__DwarfBlit_" + Hash(srcFq + "=>" + elem + "[]");
        if (synth.ContainsKey(name))
            return name;
        var sb = new StringBuilder();
        sb.Append("    private static ").Append(elem).Append("[] ").Append(name).Append('(').Append(srcFq)
            .Append(" src)\n    {\n");
        sb.Append("        if (src is null) return global::System.Array.Empty<").Append(elem).Append(">();\n");
        sb.Append("        if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(srcE)
            .Append(">() != global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(elem).Append(">())\n");
        sb.Append(
            "            throw new global::System.InvalidOperationException(\"DwarfMapper blit: element size mismatch\");\n");
        sb.Append("        var __r = new ").Append(elem).Append("[src.Length];\n");
        sb.Append("        global::System.Runtime.InteropServices.MemoryMarshal.Cast<").Append(srcE).Append(", ")
            .Append(elem)
            .Append(">(new global::System.ReadOnlySpan<").Append(srcE).Append(">(src)).CopyTo(__r);\n");
        sb.Append("        return __r;\n    }\n");
        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    /// <summary>
    ///     True when <paramref name="srcElem" />→<paramref name="tgtElem" /> is one of the seven
    ///     <c>Vector.Widen</c>-supported lossless primitive widenings (e.g. <c>int→long</c>, <c>float→double</c>).
    /// </summary>
    public static bool IsWidenPair(ITypeSymbol srcElem, ITypeSymbol tgtElem)
    {
        foreach (var p in WidenPairs)
            if (srcElem.SpecialType == p.Src && tgtElem.SpecialType == p.Tgt)
                return true;
        return false;
    }

    /// <summary>
    ///     Synthesizes a vectorized <c>TSrc[] → TDst[]</c> widening helper using <c>Vector.Widen</c>, with a
    ///     hardware-accelerated guard and a scalar tail/fallback. The output is identical to the scalar
    ///     implicit widening — this is purely a throughput optimisation. Reflection-free / AOT-safe
    ///     (<c>Vector&lt;T&gt;</c> + <c>Vector.Widen</c> are JIT/AOT intrinsics).
    /// </summary>
    public static string SynthesizeSimdWiden(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcArrayType, ITypeSymbol srcElem, ITypeSymbol tgtElem)
    {
        var dst = Fq(tgtElem);
        var srcE = Fq(srcElem);
        var srcFq = Fq(srcArrayType);
        var name = "__DwarfWiden_" + Hash(srcFq + "=>" + dst + "[]");
        if (synth.ContainsKey(name))
            return name;

        var sb = new StringBuilder();
        sb.Append("    private static ").Append(dst).Append("[] ").Append(name).Append('(').Append(srcFq)
            .Append(" src)\n    {\n");
        sb.Append("        if (src is null) return global::System.Array.Empty<").Append(dst).Append(">();\n");
        sb.Append("        var __r = new ").Append(dst).Append("[src.Length];\n");
        sb.Append("        int __i = 0;\n");
        sb.Append("        if (global::System.Numerics.Vector.IsHardwareAccelerated)\n        {\n");
        sb.Append("            int __w = global::System.Numerics.Vector<").Append(srcE).Append(">.Count;\n");
        sb.Append("            int __half = global::System.Numerics.Vector<").Append(dst).Append(">.Count;\n");
        sb.Append("            for (; __i + __w <= src.Length; __i += __w)\n            {\n");
        sb.Append("                var __v = new global::System.Numerics.Vector<").Append(srcE)
            .Append(">(src, __i);\n");
        sb.Append("                global::System.Numerics.Vector.Widen(__v, out var __lo, out var __hi);\n");
        sb.Append("                __lo.CopyTo(__r, __i);\n");
        sb.Append("                __hi.CopyTo(__r, __i + __half);\n");
        sb.Append("            }\n        }\n");
        // Scalar tail (and full path when not hardware-accelerated): the implicit widening cast.
        sb.Append("        for (; __i < src.Length; __i++) __r[__i] = src[__i];\n");
        sb.Append("        return __r;\n    }\n");
        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    ///     Generates a correct C# array creation expression for element type <paramref name="elem" />
    ///     with a size expression. Handles jagged arrays like <c>int[]</c> → <c>new int[n][]</c>
    ///     instead of the invalid <c>new int[][n]</c>.
    /// </summary>
    private static string ArrayNewExpr(string elem, string sizeExpr)
    {
        // If elem ends with "[]", it's a jagged array element.
        // C# syntax: new ElemBase[size][] (size goes on the FIRST bracket)
        if (elem.EndsWith("[]", StringComparison.Ordinal))
        {
            var baseType = elem.Substring(0, elem.Length - 2);
            var bracketRep = elem.Substring(baseType.Length); // "[]"
            return "new " + baseType + "[" + sizeExpr + "]" + bracketRep;
        }

        return "new " + elem + "[" + sizeExpr + "]";
    }

    private static string ElementExpr(string item, string? conv, NullHandling nh, bool needsCtx = false)
    {
        if (conv is not null)
        {
            // When the element converter is recursion-capable (under Preserve mode), thread ctx and depth+1.
            var args = needsCtx ? item + ", ctx, depth + 1" : item;
            return conv + "(" + args + ")";
        }

        return nh switch
        {
            NullHandling.ThrowIfNull => item +
                                        " ?? throw new global::System.InvalidOperationException(\"Collection element was null\")",
            NullHandling.ValueOrDefault => item + ".GetValueOrDefault()",
            _ => item
        };
    }

    private static bool IsHashSetType(ITypeSymbol t)
    {
        return t is INamedTypeSymbol n && n.TypeArguments.Length == 1
                                       && n.Name == "HashSet"
                                       && n.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
    }

    private static bool IsIEnumerableT(ITypeSymbol t, out ITypeSymbol? element)
    {
        element = null;
        if (t is not INamedTypeSymbol n || n.TypeArguments.Length != 1)
            return false;

        // Primary: SpecialType check
        if (n.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            element = n.TypeArguments[0];
            return true;
        }

        // Fallback: namespace + name check (handles some multi-targeting scenarios)
        if (n.Name == "IEnumerable"
            && n.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
            && n.TypeKind == TypeKind.Interface)
        {
            element = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Matches a named type in a specific namespace with a given number of type arguments.
    /// </summary>
    private static bool IsExactNamedType(ITypeSymbol t, string name, string ns, int arity, out ITypeSymbol? firstArg)
    {
        firstArg = null;
        if (t is INamedTypeSymbol n
            && n.Name == name
            && n.TypeArguments.Length == arity
            && n.ContainingNamespace?.ToDisplayString() == ns)
        {
            firstArg = n.TypeArguments[0];
            return true;
        }

        return false;
    }

    internal static bool TryGetEnumerableElement(ITypeSymbol src, out ITypeSymbol element, out CountKind count)
    {
        element = src;
        count = CountKind.None;

        if (src is IArrayTypeSymbol arr)
        {
            element = arr.ElementType;
            count = CountKind.Length;
            return true;
        }

        INamedTypeSymbol? enumerable = null;
        foreach (var candidate in Self(src))
            if (candidate is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                enumerable = named;
                break;
            }

        if (enumerable is null)
            return false;

        element = enumerable.TypeArguments[0];

        foreach (var candidate in Self(src))
            if (candidate is INamedTypeSymbol named
                && (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T
                    || named.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IReadOnlyCollection_T))
            {
                count = CountKind.Count;
                break;
            }

        return true;
    }

    private static IEnumerable<ITypeSymbol> Self(ITypeSymbol t)
    {
        yield return t;
        foreach (var i in t.AllInterfaces)
            yield return i;
    }

    private static string Fq(ITypeSymbol t)
    {
        return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    ///     Formats a type argument with nullable annotation preserved (e.g. <c>List&lt;int&gt;?</c>).
    ///     Used for element types in the generated container type names and Add() calls so nullable
    ///     element types (e.g. element <c>List&lt;int&gt;?</c>) are correctly reflected in the helper.
    /// </summary>
    private static string FqTypeArg(ITypeSymbol t)
    {
        return t.ToDisplayString(NullableFullyQualifiedFormat);
    }

    /// <summary>
    ///     Computes the nullable-outer parameter type for a collection/dict helper method.
    ///     Strips any OUTER nullable annotation (e.g. <c>List&lt;int&gt;?</c> → <c>List&lt;int&gt;</c>),
    ///     formats with inner nullable annotations preserved, then adds <c>?</c> for the outer nullable.
    ///     This ensures the helper accepts both nullable and non-nullable outer types while correctly
    ///     matching inner-nullable type arguments (e.g. <c>Dictionary&lt;string, List&lt;int&gt;?&gt;</c>).
    /// </summary>
    private static string FqNullableParam(ITypeSymbol t)
    {
        // Strip outer nullable annotation so we don't double-annotate (List<int>? → List<int>).
        // Inner annotations on type arguments are preserved by the nullable format.
        var stripped = t.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return stripped.ToDisplayString(NullableFullyQualifiedFormat) + "?";
    }

    private static string Hash(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619u;
            }

            return h.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    // ─── Target kinds ─────────────────────────────────────────────────────────
    internal enum TargetKind
    {
        // ── Concrete / today ──────────────────────────────────────────
        Array, // T[]            — projection-translatable
        List, // List<T>        — projection-translatable
        HashSet, // HashSet<T>     — NOT translatable

        // ── Interface → List<T> ──────────────────────────────────────
        IEnumerable, // IEnumerable<T> — projection-translatable (LAZY)
        ICollection, // ICollection<T> — projection-translatable
        IList, // IList<T>       — projection-translatable
        IReadOnlyList, // IReadOnlyList<T>       — projection-translatable
        IReadOnlyCollection, // IReadOnlyCollection<T> — projection-translatable

        // ── Interface → HashSet<T> ───────────────────────────────────
        ISet, // ISet<T>           — NOT translatable
        IReadOnlySet, // IReadOnlySet<T>   — NOT translatable

        // ── Immutable ────────────────────────────────────────────────
        ImmutableArray, // ImmutableArray<T>      — NOT translatable
        ImmutableList, // ImmutableList<T>       — NOT translatable
        IImmutableList, // IImmutableList<T>      — NOT translatable
        ImmutableHashSet, // ImmutableHashSet<T>    — NOT translatable
        IImmutableSet // IImmutableSet<T>       — NOT translatable
    }

    internal enum CountKind
    {
        None,
        Length,
        Count
    }

    internal readonly struct Shape
    {
        public Shape(TargetKind target, bool sourceIsArray, CountKind count, bool nullAsNull)
        {
            Target = target;
            SourceIsArray = sourceIsArray;
            Count = count;
            NullAsNull = nullAsNull;
        }

        public TargetKind Target { get; }
        public bool SourceIsArray { get; }
        public CountKind Count { get; }

        /// <summary>When true, a null source is mapped as null (AsNull strategy).</summary>
        public bool NullAsNull { get; }
    }
}
