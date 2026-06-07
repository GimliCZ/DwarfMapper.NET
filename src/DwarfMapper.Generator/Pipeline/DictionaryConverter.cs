// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static class DictionaryConverter
{
    /// <summary>Detect a dictionary source/target pair and its key/value element types.</summary>
    public static bool TryResolve(
        ITypeSymbol src, ITypeSymbol tgt,
        out ITypeSymbol srcKey, out ITypeSymbol srcVal,
        out ITypeSymbol tgtKey, out ITypeSymbol tgtVal,
        out bool srcHasCount)
    {
        srcKey = src; srcVal = src; tgtKey = tgt; tgtVal = tgt; srcHasCount = false;

        if (!IsDictionary(tgt, out tgtKey, out tgtVal))
        {
            return false;
        }
        return TryGetKeyValue(src, out srcKey, out srcVal, out srcHasCount);
    }

    /// <summary>Synthesize (or reuse) the dictionary-mapping method and return its name.</summary>
    public static string Synthesize(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcType,
        ITypeSymbol tgtKey, ITypeSymbol tgtVal, bool srcHasCount,
        string? keyConverter, NullHandling keyNull, string? valConverter, NullHandling valNull)
    {
        var keyFq = Fq(tgtKey);
        var valFq = Fq(tgtVal);
        var dictFq = "global::System.Collections.Generic.Dictionary<" + keyFq + ", " + valFq + ">";
        var name = "__DwarfMapDict_" + Hash(Fq(srcType) + "=>" + dictFq);
        if (synth.ContainsKey(name))
        {
            return name;
        }

        var srcFq = Fq(srcType);
        var keyExpr = Expr("__kv.Key", keyConverter, keyNull);
        var valExpr = Expr("__kv.Value", valConverter, valNull);

        var sb = new StringBuilder();
        sb.Append("    private ").Append(dictFq).Append(' ').Append(name).Append('(').Append(srcFq).Append(" src)\n    {\n");
        sb.Append("        var __r = new ").Append(dictFq).Append('(').Append(srcHasCount ? "src.Count" : "").Append(");\n");
        sb.Append("        if (src is null) return __r;\n");
        sb.Append("        foreach (var __kv in src) { __r[").Append(keyExpr).Append("] = ").Append(valExpr).Append("; }\n");
        sb.Append("        return __r;\n");
        sb.Append("    }\n");

        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    private static string Expr(string access, string? conv, NullHandling nh)
    {
        if (conv is not null)
        {
            return conv + "(" + access + ")";
        }
        return nh switch
        {
            NullHandling.ThrowIfNull => access + " ?? throw new global::System.InvalidOperationException(\"Dictionary entry was null\")",
            NullHandling.ValueOrDefault => access + ".GetValueOrDefault()",
            _ => access,
        };
    }

    private static bool IsDictionary(ITypeSymbol t, out ITypeSymbol key, out ITypeSymbol val)
    {
        key = t; val = t;
        if (t is INamedTypeSymbol n && n.TypeArguments.Length == 2
            && n.Name == "Dictionary"
            && n.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
        {
            key = n.TypeArguments[0];
            val = n.TypeArguments[1];
            return true;
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
        {
            return false;
        }
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

    private static IEnumerable<ITypeSymbol> Self(ITypeSymbol t)
    {
        yield return t;
        foreach (var i in t.AllInterfaces)
        {
            yield return i;
        }
    }

    private static string Fq(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
