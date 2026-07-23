// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
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
        // ── Concrete Queue<T> / Stack<T> ─────────────────────────────────────
        else if (IsExactNamedType(tgt, "Queue", "System.Collections.Generic", 1, out var qe))
        {
            tgtElem = qe!;
            targetKind = TargetKind.Queue;
        }
        else if (IsExactNamedType(tgt, "Stack", "System.Collections.Generic", 1, out var ske))
        {
            tgtElem = ske!;
            targetKind = TargetKind.Stack;
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
            TargetKind.Queue => "Queue<" + elemFq + ">",
            TargetKind.Stack => "Stack<" + elemFq + ">",
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

        var name = GeneratedNames.Collection + StableHash.Fnv1a(srcFq + "=>" + targetTag + nullTag + preserveTag);
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

        var w = new CodeWriter(1);
        EmitBody(w, name, srcFq, srcParamType, srcType, srcElem, elemFq, item, shape, identity, threadCtx,
            registerBeforeFill);

        synth[name] = new SynthesizedMethod(name, w.ToString());
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

        var w = new CodeWriter(1);
        EmitBody(w, existingName, srcFq, srcParamType, srcType, srcElem, elemFq, item, shape,
            false, true, false);
        synth[existingName] = new SynthesizedMethod(existingName, w.ToString());
    }

    /// <summary>Dispatches body emission to the per-shape emitter. Shared by Synthesize / SynthesizeInPlace.</summary>
    private static void EmitBody(
        CodeWriter w, string name, string srcFq, string srcParamType, ITypeSymbol srcType,
        ITypeSymbol srcElem, string elemFq, string item, Shape shape, bool identity,
        bool threadCtx, bool registerBeforeFill)
    {
        switch (shape.Target)
        {
            case TargetKind.Array:
                EmitArray(w, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull, threadCtx,
                    registerBeforeFill);
                break;

            case TargetKind.List:
            case TargetKind.ICollection:
            case TargetKind.IList:
            case TargetKind.IReadOnlyList:
            case TargetKind.IReadOnlyCollection:
                EmitList(w, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull, threadCtx,
                    registerBeforeFill);
                break;

            case TargetKind.HashSet:
            case TargetKind.ISet:
            case TargetKind.IReadOnlySet:
                EmitHashSet(w, name, srcFq, srcParamType, elemFq, item, shape, srcType, identity, shape.NullAsNull,
                    threadCtx, registerBeforeFill);
                break;

            case TargetKind.Queue:
            case TargetKind.Stack:
                EmitStackQueue(w, name, srcParamType, elemFq, item, shape.Target == TargetKind.Stack, identity,
                    shape.NullAsNull, threadCtx);
                break;

            case TargetKind.IEnumerable:
                // IEnumerable is lazy — cannot register-before-fill (no concrete instance exists).
                // Still thread ctx/depth to element if needed (via closure in the Select lambda).
                EmitLazyEnumerable(w, name, srcFq, srcParamType, srcElem, elemFq, item, shape, identity,
                    shape.NullAsNull, threadCtx);
                break;

            case TargetKind.ImmutableArray:
                // ImmutableArray is a value-type struct — cannot be registered.
                // Thread ctx/depth to element via the fill loop.
                EmitImmutableArray(w, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull,
                    threadCtx);
                break;

            case TargetKind.ImmutableList:
            case TargetKind.IImmutableList:
                EmitImmutableList(w, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull,
                    threadCtx);
                break;

            case TargetKind.ImmutableHashSet:
            case TargetKind.IImmutableSet:
                EmitImmutableHashSet(w, name, srcFq, srcParamType, elemFq, item, shape, identity, shape.NullAsNull,
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
        CodeWriter w, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool threadCtx, bool registerBeforeFill)
    {
        var retType = nullAsNull ? elem + "[]?" : elem + "[]";
        var paramType = srcParamType; // nullable-aware; null guard inside the body handles it
        var emptyExpr = nullAsNull ? "null" : "global::System.Array.Empty<" + elem + ">()";
        var ctxParams = threadCtx ? CtxDepthParams : "";

        using (w.Block("private " + retType + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            if (identity && shape.SourceIsArray && !registerBeforeFill && !threadCtx)
            {
                // identity array→array: Clone() fast-path (only safe without register-before-fill / ctx threading).
                w.Line("return src is null ? " + emptyExpr + " : (" + elem + "[])src.Clone();");
            }
            else if (shape.Count != CountKind.None)
            {
                var countExpr = shape.Count == CountKind.Length ? "src.Length" : "src.Count";
                var arrayAlloc = ArrayNewExpr(elem, countExpr);
                w.Line("if (src is null) return " + emptyExpr + ";");
                if (registerBeforeFill)
                    w.Line("if (ctx.TryGetReference(src, out var __cc)) return (" + elem + "[])__cc;");
                w.Line("var __r = " + arrayAlloc + ";");
                if (registerBeforeFill) w.Line("ctx.SetReference(src, __r);");
                if (shape.SourceIsArray && !registerBeforeFill && !threadCtx)
                {
                    // Non-recursive array→array (None mode): index with a single length-bounded counter over
                    // src[__i] so the JIT proves BOTH the source read and the destination store in-bounds and
                    // elides both bounds checks. The foreach + separate post-incremented write-index form (below)
                    // leaves the store index not-provably-in-bounds, so its bounds check survives — measurably
                    // slower in a hot 1000-element loop. Preserve (register-before-fill), ctx-threaded, and
                    // non-array (no indexer) sources keep the foreach form unchanged.
                    w.Line("for (int __i = 0; __i < " + countExpr + "; __i++) { __r[__i] = "
                        + item.Replace("__item", "src[__i]") + "; }");
                }
                else
                {
                    w.Line("var __i = 0;");
                    w.Line("foreach (var __item in src) { __r[__i++] = " + item + "; }");
                }

                w.Line("return __r;");
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
                w.Line("if (src is null) return " + emptyExpr + ";");
                if (registerBeforeFill)
                    w.Line("if (ctx.TryGetReference(src, out var __cc)) return (" + elem + "[])__cc;");
                w.Line("var __buf = new global::System.Collections.Generic.List<" + elem + ">();");
                w.Line("foreach (var __item in src) { __buf.Add(" + item + "); }");
                w.Line("var __r = __buf.ToArray();");
                if (registerBeforeFill) w.Line("ctx.SetReference(src, __r);");
                w.Line("return __r;");
            }
        }
    }

    private static void EmitList(
        CodeWriter w, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool threadCtx, bool registerBeforeFill)
    {
        var listFq = "global::System.Collections.Generic.List<" + elem + ">";
        var retFq = nullAsNull ? listFq + "?" : listFq;
        var emptyExpr = nullAsNull ? "null" : "new " + listFq + "()";
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var ctxParams = threadCtx ? CtxDepthParams : "";

        // Return type is always List<T> (concrete); the field type is an interface but assignable.
        using (w.Block("private " + retFq + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            if (identity && !registerBeforeFill && !threadCtx)
            {
                w.Line("return src is null ? " + emptyExpr + " : new " + listFq + "(src);");
            }
            else if (registerBeforeFill)
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("if (ctx.TryGetReference(src, out var __cc)) return (" + listFq + ")__cc;");
                w.Line("var __r = new " + listFq + "(" + CapacityArg(shape) + ");");
                w.Line("ctx.SetReference(src, __r);");
                w.Line("foreach (var __item in src) { __r.Add(" + item + "); }");
                w.Line("return __r;");
            }
            else
            {
                // Plain fill. When threadCtx is true (None/SetNull recursive element), `item` already
                // contains the (..., ctx, depth + 1) arguments and ctx is in scope from the signature.
                // Pre-size from the known source count (CapacityArg) so large lists don't repeatedly
                // double+copy their backing array — same rationale as the array/dictionary paths.
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("var __r = new " + listFq + "(" + CapacityArg(shape) + ");");
                w.Line("foreach (var __item in src) { __r.Add(" + item + "); }");
                w.Line("return __r;");
            }
        }
    }

    private static void EmitHashSet(
        CodeWriter w, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, ITypeSymbol srcType, bool identity, bool nullAsNull, bool threadCtx,
        bool registerBeforeFill)
    {
        var setFq = "global::System.Collections.Generic.HashSet<" + elem + ">";
        var retFq = nullAsNull ? setFq + "?" : setFq;
        var emptyExpr = nullAsNull ? "null" : "new " + setFq + "()";
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var ctxParams = threadCtx ? CtxDepthParams : "";

        using (w.Block("private " + retFq + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            if (identity && IsHashSetType(srcType) && !registerBeforeFill && !threadCtx)
            {
                w.Line("return src is null ? " + emptyExpr + " : new " + setFq + "(src);");
            }
            else if (registerBeforeFill)
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("if (ctx.TryGetReference(src, out var __cc)) return (" + setFq + ")__cc;");
                w.Line("var __r = new " + setFq + "(" + CapacityArg(shape) + ");");
                w.Line("ctx.SetReference(src, __r);");
                w.Line("foreach (var __item in src) { __r.Add(" + item + "); }");
                w.Line("return __r;");
            }
            else
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("var __r = new " + setFq + "(" + CapacityArg(shape) + ");");
                w.Line("foreach (var __item in src) { __r.Add(" + item + "); }");
                w.Line("return __r;");
            }
        }
    }

    private static void EmitLazyEnumerable(
        CodeWriter w, string name, string srcFq, string srcParamType, ITypeSymbol srcElemType, string elem,
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

        // One materialising path for both the identity and the converting case: `item` already carries the
        // element conversion (it is just `__item` when no transform is needed), so the fill is the same.
        _ = identity;
        _ = srcElemType;
        var cap = CapacityArg(shape);
        using (w.Block("private " + retFq + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            w.Line("if (src is null) return " + emptyExpr + ";");
            if (cap.Length > 0)
            {
                // Size is known up front (src.Length / src.Count): allocate the exact array once and fill it by
                // index. Cheapest possible independent copy — one allocation, no List growth/reallocation, no
                // per-Add bounds+version checks — and the source is enumerated exactly ONCE.
                w.Line("var __a = new " + elem + "[" + cap + "];");
                w.Line("var __i = 0;");
                w.Line("foreach (var __item in src) { __a[__i++] = " + item + "; }");
                w.Line("return __a;");
            }
            else
            {
                // Size unknown (a bare IEnumerable<T>): a single growing pass. Still exactly one enumeration —
                // we must never count-then-enumerate, which would double-enumerate a side-effecting sequence.
                w.Line("var __r = new global::System.Collections.Generic.List<" + elem + ">();");
                w.Line("foreach (var __item in src) { __r.Add(" + item + "); }");
                w.Line("return __r;");
            }
        }
    }

    private static void EmitImmutableArray(
        CodeWriter w, string name, string srcFq, string srcParamType, string elem,
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

        using (w.Block("private " + retTypeFq + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            if (identity && !elemNeedsCtx)
            {
                // Identity elements without ctx threading: copy straight into the ImmutableArray via CreateRange.
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("return global::System.Collections.Immutable.ImmutableArray.CreateRange(" + srcExpr + ");");
            }
            else
            {
                // Need element conversion or ctx threading: build a List first, then CreateRange.
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("var __buf = new global::System.Collections.Generic.List<" + elem + ">();");
                w.Line("foreach (var __item in " + srcExpr + ") { __buf.Add(" + item + "); }");
                w.Line("return global::System.Collections.Immutable.ImmutableArray.CreateRange(__buf);");
            }
        }
    }

    private static void EmitImmutableList(
        CodeWriter w, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq = "global::System.Collections.Immutable.ImmutableList<" + elem + ">";
        var retFq = nullAsNull ? tgtFq + "?" : tgtFq;
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Collections.Immutable.ImmutableList<" + elem + ">.Empty";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        using (w.Block("private " + retFq + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            if (identity && !elemNeedsCtx)
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("return global::System.Collections.Immutable.ImmutableList.CreateRange(src);");
            }
            else
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("var __buf = new global::System.Collections.Generic.List<" + elem + ">();");
                w.Line("foreach (var __item in src) { __buf.Add(" + item + "); }");
                w.Line("return global::System.Collections.Immutable.ImmutableList.CreateRange(__buf);");
            }
        }
    }

    /// <summary>
    ///     Emits a <c>Queue&lt;T&gt;</c> or <c>Stack&lt;T&gt;</c> target.
    ///     <para>
    ///     Ordering is chosen so that <b>enumerating the result yields the same sequence as the source</b> — the
    ///     only round-trip-safe reading of "map this collection", and the reason these were previously refused
    ///     (DWARF027) rather than silently reversed. <c>Queue&lt;T&gt;</c> is FIFO, so
    ///     <c>new Queue&lt;T&gt;(seq)</c> already enumerates in source order. <c>Stack&lt;T&gt;</c> is LIFO:
    ///     <c>new Stack&lt;T&gt;(seq)</c> makes the LAST element the top, which would reverse the enumeration, so
    ///     the input is reversed first — <c>List → Stack → List</c> then round-trips to the original.
    ///     </para>
    /// </summary>
    private static void EmitStackQueue(
        CodeWriter w, string name, string srcParamType, string elem,
        string item, bool isStack, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var concrete = isStack ? "Stack" : "Queue";
        var tgtFq = "global::System.Collections.Generic." + concrete + "<" + elem + ">";
        var retFq = nullAsNull ? tgtFq + "?" : tgtFq;
        var emptyExpr = nullAsNull ? "null" : "new " + tgtFq + "()";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        using (w.Block("private " + retFq + " " + name + "(" + srcParamType + " src" + ctxParams + ")"))
        {
            w.Line("if (src is null) return " + emptyExpr + ";");

            // Materialize the mapped elements in SOURCE enumeration order.
            string seq;
            if (identity && !elemNeedsCtx)
            {
                seq = "src";
            }
            else
            {
                w.Line("var __buf = new global::System.Collections.Generic.List<" + elem + ">();");
                w.Line("foreach (var __item in src) { __buf.Add(" + item + "); }");
                seq = "__buf";
            }

            // Stack pushes in order, so reverse the input to keep the result's top-first enumeration == source order.
            var ctorArg = isStack ? "global::System.Linq.Enumerable.Reverse(" + seq + ")" : seq;
            w.Line("return new " + tgtFq + "(" + ctorArg + ");");
        }
    }

    private static void EmitImmutableHashSet(
        CodeWriter w, string name, string srcFq, string srcParamType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq = "global::System.Collections.Immutable.ImmutableHashSet<" + elem + ">";
        var retFq = nullAsNull ? tgtFq + "?" : tgtFq;
        var paramType = srcParamType; // nullable-aware; null guard inside handles it
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Collections.Immutable.ImmutableHashSet<" + elem + ">.Empty";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        using (w.Block("private " + retFq + " " + name + "(" + paramType + " src" + ctxParams + ")"))
        {
            if (identity && !elemNeedsCtx)
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("return global::System.Collections.Immutable.ImmutableHashSet.CreateRange(src);");
            }
            else
            {
                w.Line("if (src is null) return " + emptyExpr + ";");
                w.Line("var __buf = new global::System.Collections.Generic.List<" + elem + ">();");
                w.Line("foreach (var __item in src) { __buf.Add(" + item + "); }");
                w.Line("return global::System.Collections.Immutable.ImmutableHashSet.CreateRange(__buf);");
            }
        }
    }

    // ─── Blit ─────────────────────────────────────────────────────────────────

    /// <summary>Synthesize a reinterpret-blit array mapper (vectorized memmove with a runtime size guard).</summary>
    public static string SynthesizeBlit(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcArrayType, ITypeSymbol srcElem, ITypeSymbol tgtElem)
    {
        var elem = Fq(tgtElem);
        var srcE = Fq(srcElem);
        var srcFq = Fq(srcArrayType);
        var name = "__DwarfBlit_" + StableHash.Fnv1a(srcFq + "=>" + elem + "[]");
        if (synth.ContainsKey(name))
            return name;
        var w = new CodeWriter(1);
        using (w.Block("private static " + elem + "[] " + name + "(" + srcFq + " src)"))
        {
            w.Line("if (src is null) return global::System.Array.Empty<" + elem + ">();");
            w.Line("if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<" + srcE
                   + ">() != global::System.Runtime.CompilerServices.Unsafe.SizeOf<" + elem + ">())");
            using (w.Indent())
            {
                w.Line(
                    "throw new global::System.InvalidOperationException(\"DwarfMapper blit: element size mismatch\");");
            }

            w.Line("var __r = new " + elem + "[src.Length];");
            w.Line("global::System.Runtime.InteropServices.MemoryMarshal.Cast<" + srcE + ", " + elem
                   + ">(new global::System.ReadOnlySpan<" + srcE + ">(src)).CopyTo(__r);");
            w.Line("return __r;");
        }

        synth[name] = new SynthesizedMethod(name, w.ToString());
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
        var name = "__DwarfWiden_" + StableHash.Fnv1a(srcFq + "=>" + dst + "[]");
        if (synth.ContainsKey(name))
            return name;

        var w = new CodeWriter(1);
        using (w.Block("private static " + dst + "[] " + name + "(" + srcFq + " src)"))
        {
            w.Line("if (src is null) return global::System.Array.Empty<" + dst + ">();");
            w.Line("var __r = new " + dst + "[src.Length];");
            w.Line("int __i = 0;");
            using (w.Block("if (global::System.Numerics.Vector.IsHardwareAccelerated)"))
            {
                w.Line("int __w = global::System.Numerics.Vector<" + srcE + ">.Count;");
                w.Line("int __half = global::System.Numerics.Vector<" + dst + ">.Count;");
                using (w.Block("for (; __i + __w <= src.Length; __i += __w)"))
                {
                    w.Line("var __v = new global::System.Numerics.Vector<" + srcE + ">(src, __i);");
                    w.Line("global::System.Numerics.Vector.Widen(__v, out var __lo, out var __hi);");
                    w.Line("__lo.CopyTo(__r, __i);");
                    w.Line("__hi.CopyTo(__r, __i + __half);");
                }
            }

            // Scalar tail (and full path when not hardware-accelerated): the implicit widening cast.
            w.Line("for (; __i < src.Length; __i++) __r[__i] = src[__i];");
            w.Line("return __r;");
        }

        synth[name] = new SynthesizedMethod(name, w.ToString());
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

    // ─── Target kinds ─────────────────────────────────────────────────────────
    internal enum TargetKind
    {
        // ── Concrete / today ──────────────────────────────────────────
        Array, // T[]            — projection-translatable
        List, // List<T>        — projection-translatable
        HashSet, // HashSet<T>     — NOT translatable
        Queue, // Queue<T>       — NOT translatable; FIFO, enumeration order = source order
        Stack, // Stack<T>       — NOT translatable; LIFO, built reversed so enumeration order = source order

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
