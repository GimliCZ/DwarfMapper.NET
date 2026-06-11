// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static class CollectionConverter
{
    // ─── Target kinds ─────────────────────────────────────────────────────────
    internal enum TargetKind
    {
        // ── Concrete / today ──────────────────────────────────────────
        Array,              // T[]            — projection-translatable
        List,               // List<T>        — projection-translatable
        HashSet,            // HashSet<T>     — NOT translatable
        // ── Interface → List<T> ──────────────────────────────────────
        IEnumerable,        // IEnumerable<T> — projection-translatable (LAZY)
        ICollection,        // ICollection<T> — projection-translatable
        IList,              // IList<T>       — projection-translatable
        IReadOnlyList,      // IReadOnlyList<T>       — projection-translatable
        IReadOnlyCollection,// IReadOnlyCollection<T> — projection-translatable
        // ── Interface → HashSet<T> ───────────────────────────────────
        ISet,               // ISet<T>           — NOT translatable
        IReadOnlySet,       // IReadOnlySet<T>   — NOT translatable
        // ── Immutable ────────────────────────────────────────────────
        ImmutableArray,     // ImmutableArray<T>      — NOT translatable
        ImmutableList,      // ImmutableList<T>       — NOT translatable
        IImmutableList,     // IImmutableList<T>      — NOT translatable
        ImmutableHashSet,   // ImmutableHashSet<T>    — NOT translatable
        IImmutableSet,      // IImmutableSet<T>       — NOT translatable
    }

    // ─── Projection-translatability classification ────────────────────────────
    /// <summary>
    /// Returns true when the target collection kind is safe to inline into an
    /// IQueryable projection expression (EF-Core-translatable). Used by Part D.
    /// </summary>
    public static bool IsTargetKindTranslatable(TargetKind kind) => kind switch
    {
        TargetKind.Array             => true,
        TargetKind.List              => true,
        TargetKind.IEnumerable       => true,
        TargetKind.ICollection       => true,
        TargetKind.IList             => true,
        TargetKind.IReadOnlyList     => true,
        TargetKind.IReadOnlyCollection => true,
        _                            => false,   // HashSet/ISet/IReadOnlySet/immutables
    };

    internal enum CountKind { None, Length, Count }

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
    /// Returns true for mutable reference-type collection kinds that can participate in the
    /// reference-identity graph under <c>ReferenceHandling = Preserve</c>.
    /// Immutables (struct ImmutableArray, ImmutableList, ImmutableHashSet, etc.) and
    /// lazy IEnumerable cannot be registered before filling, so they are excluded.
    /// </summary>
    public static bool IsMutableReferenceCollection(TargetKind kind) => kind switch
    {
        TargetKind.List              => true,
        TargetKind.HashSet           => true,
        TargetKind.ICollection       => true,
        TargetKind.IList             => true,
        TargetKind.IReadOnlyList     => true,
        TargetKind.IReadOnlyCollection => true,
        TargetKind.ISet              => true,
        TargetKind.IReadOnlySet      => true,
        TargetKind.Array             => true,  // reference-type; registered after allocation
        _                            => false, // immutables and lazy IEnumerable
    };

    // ─── Synthesis ────────────────────────────────────────────────────────────

    /// <summary>
    /// Synthesize (or reuse) the collection-mapping method and return its name.
    /// </summary>
    /// <param name="isPreserve">
    /// When true, mutable reference collections register-before-fill in the DwarfRefContext identity
    /// map; immutables still thread ctx/depth to recursion-capable element converters.
    /// Produces a distinct helper (name contains <c>_p</c> tag) so Preserve and None helpers
    /// for the same collection type coexist in the same mapper class.
    /// </param>
    /// <param name="elemNeedsCtx">
    /// When true, the element converter requires <c>(ctx, depth+1)</c> extra arguments.
    /// This is true when the element itself is a recursion-capable synthesized object mapper.
    /// </param>
    public static string Synthesize(
        Dictionary<string, SynthesizedMethod> synth,
        ITypeSymbol srcType, ITypeSymbol srcElem, ITypeSymbol tgtElem, Shape shape,
        string? elemConverter, NullHandling elemNull,
        bool isPreserve = false, bool elemNeedsCtx = false)
    {
        var elemFq  = Fq(tgtElem);
        var srcFq   = Fq(srcType);
        var nullTag = shape.NullAsNull ? "_nn" : "";
        var preserveTag = (isPreserve && (IsMutableReferenceCollection(shape.Target) || elemNeedsCtx)) ? "_p" : "";

        var targetTag = shape.Target switch
        {
            TargetKind.Array            => elemFq + "[]",
            TargetKind.List             => "List<"            + elemFq + ">",
            TargetKind.HashSet          => "HashSet<"         + elemFq + ">",
            TargetKind.IEnumerable      => "IEnumerable<"     + elemFq + ">",
            TargetKind.ICollection      => "ICollection<"     + elemFq + ">",
            TargetKind.IList            => "IList<"           + elemFq + ">",
            TargetKind.IReadOnlyList    => "IReadOnlyList<"   + elemFq + ">",
            TargetKind.IReadOnlyCollection => "IReadOnlyCollection<" + elemFq + ">",
            TargetKind.ISet             => "ISet<"            + elemFq + ">",
            TargetKind.IReadOnlySet     => "IReadOnlySet<"    + elemFq + ">",
            TargetKind.ImmutableArray   => "ImmutableArray<"  + elemFq + ">",
            TargetKind.ImmutableList    => "ImmutableList<"   + elemFq + ">",
            TargetKind.IImmutableList   => "IImmutableList<"  + elemFq + ">",
            TargetKind.ImmutableHashSet => "ImmutableHashSet<"+ elemFq + ">",
            _                          => "IImmutableSet<"   + elemFq + ">",
        };

        var name = "__DwarfMapColl_" + Hash(srcFq + "=>" + targetTag + nullTag + preserveTag);
        if (synth.ContainsKey(name))
            return name;

        var identity = SymbolEqualityComparer.Default.Equals(srcElem, tgtElem)
                       && elemConverter is null
                       && elemNull == NullHandling.None;
        var item  = ElementExpr("__item", elemConverter, elemNull, elemNeedsCtx);
        var sb    = new StringBuilder();

        // Effective preserve: are we emitting register-before-fill for THIS collection?
        var effectivePreserve = isPreserve && IsMutableReferenceCollection(shape.Target);

        switch (shape.Target)
        {
            case TargetKind.Array:
                EmitArray(sb, name, srcFq, elemFq, item, shape, identity, shape.NullAsNull, effectivePreserve);
                break;

            case TargetKind.List:
            case TargetKind.ICollection:
            case TargetKind.IList:
            case TargetKind.IReadOnlyList:
            case TargetKind.IReadOnlyCollection:
                EmitList(sb, name, srcFq, elemFq, item, shape, identity, shape.NullAsNull, effectivePreserve);
                break;

            case TargetKind.HashSet:
            case TargetKind.ISet:
            case TargetKind.IReadOnlySet:
                EmitHashSet(sb, name, srcFq, elemFq, item, srcType, identity, shape.NullAsNull, effectivePreserve);
                break;

            case TargetKind.IEnumerable:
                // IEnumerable is lazy — cannot register-before-fill (no concrete instance exists).
                // Still thread ctx/depth to element if needed (via closure in the Select lambda).
                EmitLazyEnumerable(sb, name, srcFq, srcElem, elemFq, item, shape, identity, shape.NullAsNull, isPreserve && elemNeedsCtx);
                break;

            case TargetKind.ImmutableArray:
                // ImmutableArray is a value-type struct — cannot be registered.
                // Thread ctx/depth to element via the fill loop.
                EmitImmutableArray(sb, name, srcFq, elemFq, item, shape, identity, shape.NullAsNull, isPreserve && elemNeedsCtx);
                break;

            case TargetKind.ImmutableList:
            case TargetKind.IImmutableList:
                EmitImmutableList(sb, name, srcFq, elemFq, item, shape, identity, shape.NullAsNull, isPreserve && elemNeedsCtx);
                break;

            case TargetKind.ImmutableHashSet:
            case TargetKind.IImmutableSet:
                EmitImmutableHashSet(sb, name, srcFq, elemFq, item, shape, identity, shape.NullAsNull, isPreserve && elemNeedsCtx);
                break;
        }

        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    // ── Emitters ─────────────────────────────────────────────────────────────

    // Shared preserve-mode signature suffix: (DwarfRefContext ctx, int depth)
    private const string CtxDepthParams = ", global::DwarfMapper.DwarfRefContext ctx, int depth";

    private static void EmitArray(
        StringBuilder sb, string name, string srcFq, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool isPreserve)
    {
        var retType   = nullAsNull ? elem + "[]?" : elem + "[]";
        var paramType = srcFq + "?";  // always nullable; null guard inside the body handles it
        var emptyExpr = nullAsNull ? "null" : "global::System.Array.Empty<" + elem + ">()";
        var ctxParams = isPreserve ? CtxDepthParams : "";

        sb.Append("    private ").Append(retType).Append(' ').Append(name)
          .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && shape.SourceIsArray && !isPreserve)
        {
            // identity array→array: Clone() fast-path (only safe without register-before-fill).
            sb.Append("        return src is null ? ")
              .Append(emptyExpr)
              .Append(" : (").Append(elem).Append("[])src.Clone();\n");
        }
        else if (shape.Count != CountKind.None)
        {
            var countExpr   = shape.Count == CountKind.Length ? "src.Length" : "src.Count";
            var arrayAlloc  = ArrayNewExpr(elem, countExpr);
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            if (isPreserve)
            {
                sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(elem).Append("[])__cc;\n");
            }
            sb.Append("        var __r = ").Append(arrayAlloc).Append(";\n");
            if (isPreserve)
            {
                sb.Append("        ctx.SetReference(src, __r);\n");
            }
            sb.Append("        var __i = 0;\n");
            sb.Append("        foreach (var __item in src) { __r[__i++] = ").Append(item).Append("; }\n");
            sb.Append("        return __r;\n");
        }
        else
        {
            // Unknown-count path: must buffer first; register after allocating final array.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in src) { __buf.Add(").Append(item).Append("); }\n");
            sb.Append("        var __r = __buf.ToArray();\n");
            if (isPreserve)
            {
                sb.Append("        ctx.SetReference(src, __r);\n");
            }
            sb.Append("        return __r;\n");
        }
        sb.Append("    }\n");
    }

    private static void EmitList(
        StringBuilder sb, string name, string srcFq, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool isPreserve)
    {
        var listFq    = "global::System.Collections.Generic.List<" + elem + ">";
        var retFq     = nullAsNull ? listFq + "?" : listFq;
        var emptyExpr = nullAsNull ? "null" : "new " + listFq + "()";
        var paramType = srcFq + "?";  // always nullable; null guard inside handles it
        var ctxParams = isPreserve ? CtxDepthParams : "";

        // Return type is always List<T> (concrete); the field type is an interface but assignable.
        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
          .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && !isPreserve)
        {
            sb.Append("        return src is null ? ").Append(emptyExpr)
              .Append(" : new ").Append(listFq).Append("(src);\n");
        }
        else if (isPreserve)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(listFq).Append(")__cc;\n");
            sb.Append("        var __r = new ").Append(listFq).Append('(').Append(shape.Count != CountKind.None ? (shape.Count == CountKind.Length ? "src.Length" : "src.Count") : "").Append(");\n");
            sb.Append("        ctx.SetReference(src, __r);\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }
        else
        {
            sb.Append("        var __r = new ").Append(listFq).Append("();\n");
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }
        sb.Append("    }\n");
    }

    private static void EmitHashSet(
        StringBuilder sb, string name, string srcFq, string elem,
        string item, ITypeSymbol srcType, bool identity, bool nullAsNull, bool isPreserve)
    {
        var setFq     = "global::System.Collections.Generic.HashSet<" + elem + ">";
        var retFq     = nullAsNull ? setFq + "?" : setFq;
        var emptyExpr = nullAsNull ? "null" : "new " + setFq + "()";
        var paramType = srcFq + "?";  // always nullable; null guard inside handles it
        var ctxParams = isPreserve ? CtxDepthParams : "";

        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
          .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && IsHashSetType(srcType) && !isPreserve)
        {
            sb.Append("        return src is null ? ").Append(emptyExpr)
              .Append(" : new ").Append(setFq).Append("(src);\n");
        }
        else if (isPreserve)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(setFq).Append(")__cc;\n");
            sb.Append("        var __r = new ").Append(setFq).Append("();\n");
            sb.Append("        ctx.SetReference(src, __r);\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }
        else
        {
            sb.Append("        var __r = new ").Append(setFq).Append("();\n");
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
            sb.Append("        return __r;\n");
        }
        sb.Append("    }\n");
    }

    private static void EmitLazyEnumerable(
        StringBuilder sb, string name, string srcFq, ITypeSymbol srcElemType, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        // Return type is IEnumerable<T> (deferred — no materialisation).
        // IEnumerable is lazy: we cannot register-before-fill (no concrete instance).
        // We CAN thread ctx/depth into the Select lambda if the element converter needs it.
        // However, since IEnumerable deferred evaluation means ctx lifetime could be tricky,
        // for simplicity and correctness we materialise (via Select) and thread ctx if needed.
        var ieFq      = "global::System.Collections.Generic.IEnumerable<" + elem + ">";
        var retFq     = nullAsNull ? ieFq + "?" : ieFq;
        var emptyExpr = nullAsNull
            ? "null"
            : "global::System.Array.Empty<" + elem + ">()";
        var paramType = srcFq + "?";  // always nullable; null guard inside handles it
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        sb.Append("    private ").Append(retFq).Append(' ').Append(name)
          .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity)
        {
            // Identity: source is already assignable to IEnumerable<T> — pass through.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return src;\n");
        }
        else
        {
            // Deferred: emit Enumerable.Select<SrcElem,TgtElem>(src, __item => map(__item)) without ToList().
            // We must specify type arguments explicitly so the C# compiler doesn't infer the wrong
            // return type when the element conversion is implicit (e.g. int→long).
            var srcElemFq = Fq(srcElemType);
            var selectFq  = "global::System.Linq.Enumerable";
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return ").Append(selectFq).Append(".Select<").Append(srcElemFq).Append(", ").Append(elem).Append(">(src, __item => ").Append(item).Append(");\n");
        }
        sb.Append("    }\n");
    }

    private static void EmitImmutableArray(
        StringBuilder sb, string name, string srcFq, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq     = "global::System.Collections.Immutable.ImmutableArray<" + elem + ">";
        var paramType = srcFq + "?";  // always nullable; null guard inside handles it
        // ImmutableArray<T> is a value type — no nullable return type annotation needed
        var emptyExpr = nullAsNull
            ? "default(global::System.Collections.Immutable.ImmutableArray<" + elem + ">)"
            : "global::System.Collections.Immutable.ImmutableArray<" + elem + ">.Empty";
        var ctxParams = elemNeedsCtx ? CtxDepthParams : "";

        // When the source type itself is ImmutableArray<T> (a value-type struct), the nullable
        // parameter is Nullable<ImmutableArray<T>> which does NOT implement IEnumerable<T>.
        // We must unwrap it with .GetValueOrDefault() before passing to CreateRange / foreach.
        bool srcIsImmutableArrayStruct = srcFq.StartsWith(
            "global::System.Collections.Immutable.ImmutableArray<",
            System.StringComparison.Ordinal);
        string srcExpr = srcIsImmutableArrayStruct ? "src.GetValueOrDefault()" : "src";

        sb.Append("    private ").Append(tgtFq).Append(' ').Append(name)
          .Append('(').Append(paramType).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (identity && shape.Count != CountKind.None && !elemNeedsCtx)
        {
            // Known-count identity without ctx threading: use CreateRange directly.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableArray.CreateRange(").Append(srcExpr).Append(");\n");
        }
        else if (identity && !elemNeedsCtx)
        {
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableArray.CreateRange(").Append(srcExpr).Append(");\n");
        }
        else
        {
            // Need element conversion or ctx threading: build a List first, then CreateRange.
            sb.Append("        if (src is null) return ").Append(emptyExpr).Append(";\n");
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
            sb.Append("        foreach (var __item in ").Append(srcExpr).Append(") { __buf.Add(").Append(item).Append("); }\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableArray.CreateRange(__buf);\n");
        }
        sb.Append("    }\n");
    }

    private static void EmitImmutableList(
        StringBuilder sb, string name, string srcFq, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq     = "global::System.Collections.Immutable.ImmutableList<" + elem + ">";
        var retFq     = nullAsNull ? tgtFq + "?" : tgtFq;
        var paramType = srcFq + "?";  // always nullable; null guard inside handles it
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
        StringBuilder sb, string name, string srcFq, string elem,
        string item, Shape shape, bool identity, bool nullAsNull, bool elemNeedsCtx)
    {
        var tgtFq     = "global::System.Collections.Immutable.ImmutableHashSet<" + elem + ">";
        var retFq     = nullAsNull ? tgtFq + "?" : tgtFq;
        var paramType = srcFq + "?";  // always nullable; null guard inside handles it
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
        var elem  = Fq(tgtElem);
        var srcE  = Fq(srcElem);
        var srcFq = Fq(srcArrayType);
        var name  = "__DwarfBlit_" + Hash(srcFq + "=>" + elem + "[]");
        if (synth.ContainsKey(name))
            return name;
        var sb = new StringBuilder();
        sb.Append("    private static ").Append(elem).Append("[] ").Append(name).Append('(').Append(srcFq).Append(" src)\n    {\n");
        sb.Append("        if (src is null) return global::System.Array.Empty<").Append(elem).Append(">();\n");
        sb.Append("        if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(srcE)
          .Append(">() != global::System.Runtime.CompilerServices.Unsafe.SizeOf<").Append(elem).Append(">())\n");
        sb.Append("            throw new global::System.InvalidOperationException(\"DwarfMapper blit: element size mismatch\");\n");
        sb.Append("        var __r = new ").Append(elem).Append("[src.Length];\n");
        sb.Append("        global::System.Runtime.InteropServices.MemoryMarshal.Cast<").Append(srcE).Append(", ").Append(elem)
          .Append(">(new global::System.ReadOnlySpan<").Append(srcE).Append(">(src)).CopyTo(__r);\n");
        sb.Append("        return __r;\n    }\n");
        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a correct C# array creation expression for element type <paramref name="elem"/>
    /// with a size expression. Handles jagged arrays like <c>int[]</c> → <c>new int[n][]</c>
    /// instead of the invalid <c>new int[][n]</c>.
    /// </summary>
    private static string ArrayNewExpr(string elem, string sizeExpr)
    {
        // If elem ends with "[]", it's a jagged array element.
        // C# syntax: new ElemBase[size][] (size goes on the FIRST bracket)
        if (elem.EndsWith("[]", System.StringComparison.Ordinal))
        {
            var baseType   = elem.Substring(0, elem.Length - 2);
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
            NullHandling.ThrowIfNull   => item + " ?? throw new global::System.InvalidOperationException(\"Collection element was null\")",
            NullHandling.ValueOrDefault => item + ".GetValueOrDefault()",
            _ => item,
        };
    }

    private static bool IsHashSetType(ITypeSymbol t) =>
        t is INamedTypeSymbol n && n.TypeArguments.Length == 1
        && n.Name == "HashSet"
        && n.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";

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
    /// Matches a named type in a specific namespace with a given number of type arguments.
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
        count   = CountKind.None;

        if (src is IArrayTypeSymbol arr)
        {
            element = arr.ElementType;
            count   = CountKind.Length;
            return true;
        }

        INamedTypeSymbol? enumerable = null;
        foreach (var candidate in Self(src))
        {
            if (candidate is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                enumerable = named;
                break;
            }
        }
        if (enumerable is null)
            return false;

        element = enumerable.TypeArguments[0];

        foreach (var candidate in Self(src))
        {
            if (candidate is INamedTypeSymbol named
                && (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T
                    || named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IReadOnlyCollection_T))
            {
                count = CountKind.Count;
                break;
            }
        }
        return true;
    }

    private static IEnumerable<ITypeSymbol> Self(ITypeSymbol t)
    {
        yield return t;
        foreach (var i in t.AllInterfaces)
            yield return i;
    }

    private static string Fq(ITypeSymbol t) =>
        t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
