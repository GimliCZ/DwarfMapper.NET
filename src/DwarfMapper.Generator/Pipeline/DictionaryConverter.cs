// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static class DictionaryConverter
{
    // Shared preserve-mode signature suffix.
    private const string CtxDepthParams = ", global::DwarfMapper.DwarfRefContext ctx, int depth";

    // Nullable-aware format — includes ? on nullable reference type arguments.
    private static readonly SymbolDisplayFormat NullableFullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Detect a dictionary source/target pair and its key/value element types.</summary>
    public static bool TryResolve(
        ITypeSymbol src, ITypeSymbol tgt,
        out ITypeSymbol srcKey, out ITypeSymbol srcVal,
        out ITypeSymbol tgtKey, out ITypeSymbol tgtVal,
        out bool srcHasCount,
        out DictTargetKind targetKind)
    {
        srcKey = src;
        srcVal = src;
        tgtKey = tgt;
        tgtVal = tgt;
        srcHasCount = false;
        targetKind = DictTargetKind.Dictionary;

        if (!TryGetDictTarget(tgt, out tgtKey, out tgtVal, out targetKind))
            return false;

        return TryGetKeyValue(src, out srcKey, out srcVal, out srcHasCount);
    }

    // Keep old overload for callers that don't need targetKind.
    public static bool TryResolve(
        ITypeSymbol src, ITypeSymbol tgt,
        out ITypeSymbol srcKey, out ITypeSymbol srcVal,
        out ITypeSymbol tgtKey, out ITypeSymbol tgtVal,
        out bool srcHasCount)
    {
        return TryResolve(src, tgt, out srcKey, out srcVal, out tgtKey, out tgtVal,
            out srcHasCount, out _);
    }

    /// <summary>
    ///     Synthesize (or reuse) the dictionary-mapping method and return its name.
    /// </summary>
    /// <param name="isPreserve">
    ///     When true, mutable dictionaries (Dictionary, IDictionary, IReadOnlyDictionary) register-before-fill
    ///     in the DwarfRefContext identity map; immutable dictionaries still thread ctx/depth to recursion-capable
    ///     value converters.
    /// </param>
    /// <param name="keyNeedsCtx">When true, the key converter requires (ctx, depth+1) extra args.</param>
    /// <param name="valNeedsCtx">When true, the value converter requires (ctx, depth+1) extra args.</param>
    public static string Synthesize(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcType,
        ITypeSymbol tgtKey, ITypeSymbol tgtVal, bool srcHasCount, DictTargetKind targetKind,
        string? keyConverter, NullHandling keyNull, string? valConverter, NullHandling valNull,
        bool nullAsNull = false,
        bool isPreserve = false, bool keyNeedsCtx = false, bool valNeedsCtx = false)
    {
        // Use nullable-aware format for key/value types so the generated dict type args match
        // the actual target type (e.g. Dictionary<string, List<int>?> not Dictionary<string, List<int>>).
        var keyFq = FqTypeArg(tgtKey);
        var valFq = FqTypeArg(tgtVal);
        var nullTag = nullAsNull ? "_nn" : "";

        var isMutableDict = targetKind != DictTargetKind.ImmutableDictionary
                            && targetKind != DictTargetKind.IImmutableDictionary;
        var effectivePreserve = isPreserve && isMutableDict;
        // Thread the (ctx, depth) signature whenever we register (Preserve mutable) OR a key/value
        // converter is recursion-capable — the latter now also fires in None/SetNull mode so a cycle
        // routed through a dictionary value breaks via the element's own guard. register-before-fill
        // stays Preserve-only (effectivePreserve), so None/SetNull never registers.
        var threadCtx = effectivePreserve || keyNeedsCtx || valNeedsCtx;
        // Tag preserves Preserve names byte-for-byte ("_p"); "_c" is the new ctx-only (no register)
        // variant used by None/SetNull recursive key/value.
        var preserveTag = isPreserve && (effectivePreserve || keyNeedsCtx || valNeedsCtx) ? "_p"
            : keyNeedsCtx || valNeedsCtx ? "_c" : "";

        string retTypeFq;
        switch (targetKind)
        {
            case DictTargetKind.ImmutableDictionary:
            case DictTargetKind.IImmutableDictionary:
                retTypeFq = "global::System.Collections.Immutable.ImmutableDictionary<" + keyFq + ", " + valFq + ">";
                break;
            default:
                retTypeFq = "global::System.Collections.Generic.Dictionary<" + keyFq + ", " + valFq + ">";
                break;
        }

        var name = GeneratedNames.Dictionary + StableHash.Fnv1a(Fq(srcType) + "=>" + retTypeFq + nullTag + preserveTag);
        if (synth.ContainsKey(name))
            return name;

        var srcFq = Fq(srcType);
        // Nullable-aware param type: strips outer nullable, preserves inner nullable type arguments,
        // then adds ? for the outer — avoids CS8620 when source has nullable value/element types.
        var srcParam = FqNullableParam(srcType);
        var retAnnot = nullAsNull ? retTypeFq + "?" : retTypeFq;
        var keyExpr = Expr("__kv.Key", keyConverter, keyNull, keyNeedsCtx);
        var valExpr = Expr("__kv.Value", valConverter, valNull, valNeedsCtx);
        var emptyDict = nullAsNull ? "null" : "new " + retTypeFq + "()";
        var ctxParams = threadCtx ? CtxDepthParams : "";

        var w = new CodeWriter(1);
        using (w.Block("private " + retAnnot + " " + name + "(" + srcParam + " src" + ctxParams + ")"))
        {
            if (targetKind == DictTargetKind.ImmutableDictionary
                || targetKind == DictTargetKind.IImmutableDictionary)
            {
                var emptyImm = nullAsNull
                    ? "null"
                    : "global::System.Collections.Immutable.ImmutableDictionary<" + keyFq + ", " + valFq + ">.Empty";

                w.Line("if (src is null) return " + emptyImm + ";");
                // Build through ImmutableDictionary.CreateBuilder and assign by INDEXER — deliberately NOT
                // CreateRange (audit ISSUE-005), matching the mutable path exactly. CreateRange THROWS on a
                // duplicate key, so the two dictionary paths disagreed on the same input: when the key CONVERSION
                // collapses two source keys onto one target key (enum->string by name, a case change, a narrowing),
                // a mutable target silently kept the last value while an immutable target threw ArgumentException.
                // Same mapping, same data, opposite outcome decided only by the destination type. Last-write-wins
                // matches Dictionary's own indexer and the majority path. (Also drops the intermediate List<KVP>.)
                w.Line("var __b = global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<"
                       + keyFq + ", " + valFq + ">();");
                w.Line("foreach (var __kv in src) { __b[" + keyExpr + "] = " + valExpr + "; }");
                w.Line("return __b.ToImmutable();");
            }
            else
            {
                var dictFq = "global::System.Collections.Generic.Dictionary<" + keyFq + ", " + valFq + ">";
                w.Line("if (src is null) return " + emptyDict + ";");
                if (effectivePreserve)
                    w.Line("if (ctx.TryGetReference(src, out var __cc)) return (" + dictFq + ")__cc;");
                w.Line("var __r = new " + dictFq + "(" + (srcHasCount ? "src.Count" : "") + ");");
                if (effectivePreserve) w.Line("ctx.SetReference(src, __r);");
                w.Line("foreach (var __kv in src) { __r[" + keyExpr + "] = " + valExpr + "; }");
                w.Line("return __r;");
            }
        }

        synth[name] = new SynthesizedMethod(name, w.ToString());
        return name;
    }

    // Old overload (no targetKind, no nullAsNull) for existing call-sites.
    public static string Synthesize(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcType,
        ITypeSymbol tgtKey, ITypeSymbol tgtVal, bool srcHasCount,
        string? keyConverter, NullHandling keyNull, string? valConverter, NullHandling valNull)
    {
        return Synthesize(synth, srcType, tgtKey, tgtVal, srcHasCount,
            DictTargetKind.Dictionary, keyConverter, keyNull, valConverter, valNull);
    }

    /// <summary>
    ///     Re-emits an EXISTING dictionary helper (keyed by <paramref name="existingName" />) so it threads
    ///     <c>(ctx, depth)</c> and routes the recursion-capable key/value through a ctx-accepting converter
    ///     (typically a <c>__DwarfMap_Depth_*</c> companion). Used by the post-pass when a None-mode
    ///     dictionary's key/value method is discovered self-recursive — overwrites the original body
    ///     (which StackOverflowed on a fresh context) with a depth-guarded one. Never register-before-fills.
    /// </summary>
    public static void SynthesizeInPlace(
        Dictionary<string, SynthesizedMethod> synth, string existingName, ITypeSymbol srcType,
        ITypeSymbol tgtKey, ITypeSymbol tgtVal, bool srcHasCount, DictTargetKind targetKind,
        string? keyConverter, NullHandling keyNull, bool keyNeedsCtx,
        string? valConverter, NullHandling valNull, bool valNeedsCtx, bool nullAsNull)
    {
        var keyFq = FqTypeArg(tgtKey);
        var valFq = FqTypeArg(tgtVal);
        var isImmutable = targetKind == DictTargetKind.ImmutableDictionary
                          || targetKind == DictTargetKind.IImmutableDictionary;
        var retTypeFq = isImmutable
            ? "global::System.Collections.Immutable.ImmutableDictionary<" + keyFq + ", " + valFq + ">"
            : "global::System.Collections.Generic.Dictionary<" + keyFq + ", " + valFq + ">";

        var srcParam = FqNullableParam(srcType);
        var retAnnot = nullAsNull ? retTypeFq + "?" : retTypeFq;
        var keyExpr = Expr("__kv.Key", keyConverter, keyNull, keyNeedsCtx);
        var valExpr = Expr("__kv.Value", valConverter, valNull, valNeedsCtx);

        var w = new CodeWriter(1);
        using (w.Block("private " + retAnnot + " " + existingName + "(" + srcParam + " src" + CtxDepthParams + ")"))
        {
            if (isImmutable)
            {
                var emptyImm = nullAsNull
                    ? "null"
                    : "global::System.Collections.Immutable.ImmutableDictionary<" + keyFq + ", " + valFq + ">.Empty";
                w.Line("if (src is null) return " + emptyImm + ";");
                // Builder with INDEXER semantics, matching the mutable path exactly. CreateRange THROWS on a
                // duplicate key, so the two dictionary paths disagreed on the same input: when the key CONVERSION
                // collapses two source keys onto one target key (enum->string by name, a case change, a narrowing),
                // a mutable target silently kept the last value while an immutable target threw ArgumentException.
                // Same mapping, same data, opposite outcome decided only by the destination type. Last-write-wins
                // matches Dictionary's own indexer and the majority path. (Also drops the intermediate List<KVP>.)
                w.Line("var __b = global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<"
                       + keyFq + ", " + valFq + ">();");
                w.Line("foreach (var __kv in src) { __b[" + keyExpr + "] = " + valExpr + "; }");
                w.Line("return __b.ToImmutable();");
            }
            else
            {
                var dictFq = "global::System.Collections.Generic.Dictionary<" + keyFq + ", " + valFq + ">";
                var emptyDict = nullAsNull ? "null" : "new " + retTypeFq + "()";
                w.Line("if (src is null) return " + emptyDict + ";");
                w.Line("var __r = new " + dictFq + "(" + (srcHasCount ? "src.Count" : "") + ");");
                w.Line("foreach (var __kv in src) { __r[" + keyExpr + "] = " + valExpr + "; }");
                w.Line("return __r;");
            }
        }

        synth[existingName] = new SynthesizedMethod(existingName, w.ToString());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static string Expr(string access, string? conv, NullHandling nh, bool needsCtx = false)
    {
        if (conv is not null)
        {
            var args = needsCtx ? access + ", ctx, depth + 1" : access;
            return conv + "(" + args + ")";
        }

        return nh switch
        {
            NullHandling.ThrowIfNull => access +
                                        " ?? throw new global::System.InvalidOperationException(\"Dictionary entry was null\")",
            NullHandling.ValueOrDefault => access + ".GetValueOrDefault()",
            _ => access
        };
    }

    private static bool TryGetDictTarget(ITypeSymbol t,
        out ITypeSymbol key, out ITypeSymbol val, out DictTargetKind kind)
    {
        key = t;
        val = t;
        kind = DictTargetKind.Dictionary;

        if (t is not INamedTypeSymbol n || n.TypeArguments.Length != 2)
            return false;

        var ns = n.ContainingNamespace?.ToDisplayString();
        var name = n.Name;

        if (ns == "System.Collections.Generic")
            switch (name)
            {
                case "Dictionary":
                    key = n.TypeArguments[0];
                    val = n.TypeArguments[1];
                    kind = DictTargetKind.Dictionary;
                    return true;
                case "IDictionary":
                    key = n.TypeArguments[0];
                    val = n.TypeArguments[1];
                    kind = DictTargetKind.IDictionary;
                    return true;
                case "IReadOnlyDictionary":
                    key = n.TypeArguments[0];
                    val = n.TypeArguments[1];
                    kind = DictTargetKind.IReadOnlyDictionary;
                    return true;
            }
        else if (ns == "System.Collections.Immutable")
            switch (name)
            {
                case "ImmutableDictionary":
                    key = n.TypeArguments[0];
                    val = n.TypeArguments[1];
                    kind = DictTargetKind.ImmutableDictionary;
                    return true;
                case "IImmutableDictionary":
                    key = n.TypeArguments[0];
                    val = n.TypeArguments[1];
                    kind = DictTargetKind.IImmutableDictionary;
                    return true;
            }

        return false;
    }

    private static bool TryGetKeyValue(ITypeSymbol src, out ITypeSymbol key, out ITypeSymbol val, out bool hasCount)
    {
        key = src;
        val = src;
        hasCount = false;

        INamedTypeSymbol? kvp = null;
        foreach (var c in Self(src))
            if (c is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                && named.TypeArguments[0] is INamedTypeSymbol elem
                && elem.Name == "KeyValuePair"
                && elem.TypeArguments.Length == 2
                && elem.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
            {
                kvp = elem;
                break;
            }

        if (kvp is null)
            return false;

        key = kvp.TypeArguments[0];
        val = kvp.TypeArguments[1];

        foreach (var c in Self(src))
            if (c is INamedTypeSymbol named
                && (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T
                    || named.OriginalDefinition.SpecialType ==
                    SpecialType.System_Collections_Generic_IReadOnlyCollection_T))
            {
                hasCount = true;
                break;
            }

        return true;
    }

    /// <summary>
    ///     Returns <c>true</c> and sets <paramref name="valueType" /> if <paramref name="t" /> is a
    ///     dictionary-like type (IDictionary&lt;K,V&gt;, IReadOnlyDictionary&lt;K,V&gt;, or Dictionary&lt;K,V&gt;).
    ///     Used by the FlattenGraph edge-detection logic to identify dict-value graph edges (SF-F3).
    /// </summary>
    public static bool TryGetDictionaryValueType(ITypeSymbol t, out ITypeSymbol valueType)
    {
        valueType = t;
        if (TryGetKeyValue(t, out _, out var val, out _))
        {
            valueType = val;
            return true;
        }

        return false;
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
    ///     Used for key/value type arguments in the generated dict type name.
    /// </summary>
    private static string FqTypeArg(ITypeSymbol t)
    {
        return t.ToDisplayString(NullableFullyQualifiedFormat);
    }

    /// <summary>
    ///     Computes the nullable-outer parameter type for a dict helper: strips outer nullable annotation,
    ///     preserves inner nullable type arguments, then adds <c>?</c> for the outer nullable.
    /// </summary>
    private static string FqNullableParam(ITypeSymbol t)
    {
        var stripped = t.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return stripped.ToDisplayString(NullableFullyQualifiedFormat) + "?";
    }

    internal enum DictTargetKind
    {
        Dictionary, // Dictionary<K,V>         — concrete (today)
        IDictionary, // IDictionary<K,V>        → Dictionary<K,V>
        IReadOnlyDictionary, // IReadOnlyDictionary<K,V> → Dictionary<K,V>
        ImmutableDictionary, // ImmutableDictionary<K,V>
        IImmutableDictionary // IImmutableDictionary<K,V> → ImmutableDictionary<K,V>
    }
}
