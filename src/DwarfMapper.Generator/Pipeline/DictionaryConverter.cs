// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static class DictionaryConverter
{
    internal enum DictTargetKind
    {
        Dictionary,          // Dictionary<K,V>         — concrete (today)
        IDictionary,         // IDictionary<K,V>        → Dictionary<K,V>
        IReadOnlyDictionary, // IReadOnlyDictionary<K,V> → Dictionary<K,V>
        ImmutableDictionary, // ImmutableDictionary<K,V>
        IImmutableDictionary,// IImmutableDictionary<K,V> → ImmutableDictionary<K,V>
    }

    /// <summary>Detect a dictionary source/target pair and its key/value element types.</summary>
    public static bool TryResolve(
        ITypeSymbol src, ITypeSymbol tgt,
        out ITypeSymbol srcKey, out ITypeSymbol srcVal,
        out ITypeSymbol tgtKey, out ITypeSymbol tgtVal,
        out bool srcHasCount,
        out DictTargetKind targetKind)
    {
        srcKey = src; srcVal = src; tgtKey = tgt; tgtVal = tgt;
        srcHasCount = false;
        targetKind  = DictTargetKind.Dictionary;

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

    // Shared preserve-mode signature suffix.
    private const string CtxDepthParams = ", global::DwarfMapper.DwarfRefContext ctx, int depth";

    /// <summary>
    /// Synthesize (or reuse) the dictionary-mapping method and return its name.
    /// </summary>
    /// <param name="isPreserve">
    /// When true, mutable dictionaries (Dictionary, IDictionary, IReadOnlyDictionary) register-before-fill
    /// in the DwarfRefContext identity map; immutable dictionaries still thread ctx/depth to recursion-capable
    /// value converters.
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
        var keyFq  = FqTypeArg(tgtKey);
        var valFq  = FqTypeArg(tgtVal);
        var nullTag = nullAsNull ? "_nn" : "";

        bool isMutableDict = targetKind != DictTargetKind.ImmutableDictionary
                          && targetKind != DictTargetKind.IImmutableDictionary;
        var effectivePreserve = isPreserve && isMutableDict;
        var needsCtxSig = effectivePreserve || (isPreserve && (keyNeedsCtx || valNeedsCtx));
        var preserveTag = needsCtxSig ? "_p" : "";

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

        var name = "__DwarfMapDict_" + Hash(Fq(srcType) + "=>" + retTypeFq + nullTag + preserveTag);
        if (synth.ContainsKey(name))
            return name;

        var srcFq     = Fq(srcType);
        // Nullable-aware param type: strips outer nullable, preserves inner nullable type arguments,
        // then adds ? for the outer — avoids CS8620 when source has nullable value/element types.
        var srcParam  = FqNullableParam(srcType);
        var retAnnot  = nullAsNull ? retTypeFq + "?" : retTypeFq;
        var keyExpr   = Expr("__kv.Key",   keyConverter, keyNull, keyNeedsCtx);
        var valExpr   = Expr("__kv.Value", valConverter, valNull, valNeedsCtx);
        var emptyDict = nullAsNull ? "null" : "new " + retTypeFq + "()";
        var ctxParams = needsCtxSig ? CtxDepthParams : "";

        var sb = new StringBuilder();
        sb.Append("    private ").Append(retAnnot).Append(' ').Append(name)
          .Append('(').Append(srcParam).Append(" src").Append(ctxParams).Append(")\n    {\n");

        if (targetKind == DictTargetKind.ImmutableDictionary
            || targetKind == DictTargetKind.IImmutableDictionary)
        {
            var emptyImm = nullAsNull
                ? "null"
                : "global::System.Collections.Immutable.ImmutableDictionary<" + keyFq + ", " + valFq + ">.Empty";

            sb.Append("        if (src is null) return ").Append(emptyImm).Append(";\n");
            // Build a List<KeyValuePair<K,V>>, then CreateRange.
            var kvpFq = "global::System.Collections.Generic.KeyValuePair<" + keyFq + ", " + valFq + ">";
            sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(kvpFq).Append(">();\n");
            sb.Append("        foreach (var __kv in src)\n");
            sb.Append("        {\n");
            sb.Append("            __buf.Add(new ").Append(kvpFq).Append('(').Append(keyExpr).Append(", ").Append(valExpr).Append("));\n");
            sb.Append("        }\n");
            sb.Append("        return global::System.Collections.Immutable.ImmutableDictionary.CreateRange(__buf);\n");
        }
        else
        {
            var dictFq = "global::System.Collections.Generic.Dictionary<" + keyFq + ", " + valFq + ">";
            sb.Append("        if (src is null) return ").Append(emptyDict).Append(";\n");
            if (effectivePreserve)
            {
                sb.Append("        if (ctx.TryGetReference(src, out var __cc)) return (").Append(dictFq).Append(")__cc;\n");
            }
            sb.Append("        var __r = new ").Append(dictFq).Append('(').Append(srcHasCount ? "src.Count" : "").Append(");\n");
            if (effectivePreserve)
            {
                sb.Append("        ctx.SetReference(src, __r);\n");
            }
            sb.Append("        foreach (var __kv in src) { __r[").Append(keyExpr).Append("] = ").Append(valExpr).Append("; }\n");
            sb.Append("        return __r;\n");
        }

        sb.Append("    }\n");
        synth[name] = new SynthesizedMethod(name, sb.ToString());
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
            NullHandling.ThrowIfNull   => access + " ?? throw new global::System.InvalidOperationException(\"Dictionary entry was null\")",
            NullHandling.ValueOrDefault => access + ".GetValueOrDefault()",
            _ => access,
        };
    }

    private static bool TryGetDictTarget(ITypeSymbol t,
        out ITypeSymbol key, out ITypeSymbol val, out DictTargetKind kind)
    {
        key = t; val = t; kind = DictTargetKind.Dictionary;

        if (t is not INamedTypeSymbol n || n.TypeArguments.Length != 2)
            return false;

        var ns   = n.ContainingNamespace?.ToDisplayString();
        var name = n.Name;

        if (ns == "System.Collections.Generic")
        {
            switch (name)
            {
                case "Dictionary":
                    key = n.TypeArguments[0]; val = n.TypeArguments[1];
                    kind = DictTargetKind.Dictionary; return true;
                case "IDictionary":
                    key = n.TypeArguments[0]; val = n.TypeArguments[1];
                    kind = DictTargetKind.IDictionary; return true;
                case "IReadOnlyDictionary":
                    key = n.TypeArguments[0]; val = n.TypeArguments[1];
                    kind = DictTargetKind.IReadOnlyDictionary; return true;
            }
        }
        else if (ns == "System.Collections.Immutable")
        {
            switch (name)
            {
                case "ImmutableDictionary":
                    key = n.TypeArguments[0]; val = n.TypeArguments[1];
                    kind = DictTargetKind.ImmutableDictionary; return true;
                case "IImmutableDictionary":
                    key = n.TypeArguments[0]; val = n.TypeArguments[1];
                    kind = DictTargetKind.IImmutableDictionary; return true;
            }
        }
        return false;
    }

    private static bool TryGetKeyValue(ITypeSymbol src, out ITypeSymbol key, out ITypeSymbol val, out bool hasCount)
    {
        key = src; val = src; hasCount = false;

        INamedTypeSymbol? kvp = null;
        foreach (var c in Self(src))
        {
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
        }
        if (kvp is null)
            return false;

        key = kvp.TypeArguments[0];
        val = kvp.TypeArguments[1];

        foreach (var c in Self(src))
        {
            if (c is INamedTypeSymbol named
                && (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T
                    || named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IReadOnlyCollection_T))
            {
                hasCount = true;
                break;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> and sets <paramref name="valueType"/> if <paramref name="t"/> is a
    /// dictionary-like type (IDictionary&lt;K,V&gt;, IReadOnlyDictionary&lt;K,V&gt;, or Dictionary&lt;K,V&gt;).
    /// Used by the FlattenGraph edge-detection logic to identify dict-value graph edges (SF-F3).
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

    private static string Fq(ITypeSymbol t) =>
        t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    // Nullable-aware format — includes ? on nullable reference type arguments.
    private static readonly Microsoft.CodeAnalysis.SymbolDisplayFormat NullableFullyQualifiedFormat =
        Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | Microsoft.CodeAnalysis.SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Formats a type argument with nullable annotation preserved (e.g. <c>List&lt;int&gt;?</c>).
    /// Used for key/value type arguments in the generated dict type name.
    /// </summary>
    private static string FqTypeArg(ITypeSymbol t) =>
        t.ToDisplayString(NullableFullyQualifiedFormat);

    /// <summary>
    /// Computes the nullable-outer parameter type for a dict helper: strips outer nullable annotation,
    /// preserves inner nullable type arguments, then adds <c>?</c> for the outer nullable.
    /// </summary>
    private static string FqNullableParam(ITypeSymbol t)
    {
        var stripped = t.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return stripped.ToDisplayString(NullableFullyQualifiedFormat) + "?";
    }

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
