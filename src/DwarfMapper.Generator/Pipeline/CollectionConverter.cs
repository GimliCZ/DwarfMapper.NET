// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal static class CollectionConverter
{
    internal enum TargetKind { Array, List }
    internal enum CountKind { None, Length, Count }

    internal readonly struct Shape
    {
        public Shape(TargetKind target, bool sourceIsArray, CountKind count)
        {
            Target = target;
            SourceIsArray = sourceIsArray;
            Count = count;
        }
        public TargetKind Target { get; }
        public bool SourceIsArray { get; }
        public CountKind Count { get; }
    }

    /// <summary>Detect a supported collection mapping pair and its element types.</summary>
    public static bool TryResolve(ITypeSymbol src, ITypeSymbol tgt,
        out ITypeSymbol srcElem, out ITypeSymbol tgtElem, out Shape shape)
    {
        srcElem = src;
        tgtElem = tgt;
        shape = default;

        // Strings are IEnumerable<char> but must be treated as scalars.
        if (src.SpecialType == SpecialType.System_String || tgt.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        TargetKind targetKind;
        if (tgt is IArrayTypeSymbol tArr)
        {
            tgtElem = tArr.ElementType;
            targetKind = TargetKind.Array;
        }
        else if (IsListT(tgt, out var le))
        {
            tgtElem = le;
            targetKind = TargetKind.List;
        }
        else
        {
            return false;
        }

        if (!TryGetEnumerableElement(src, out srcElem, out var count))
        {
            return false;
        }

        shape = new Shape(targetKind, src is IArrayTypeSymbol, count);
        return true;
    }

    /// <summary>Synthesize (or reuse) the collection-mapping method and return its name.</summary>
    public static string Synthesize(
        Dictionary<string, SynthesizedMethod> synth,
        ITypeSymbol srcType, ITypeSymbol srcElem, ITypeSymbol tgtElem, Shape shape,
        string? elemConverter, NullHandling elemNull)
    {
        var name = "__DwarfMapColl_" + Hash(Fq(srcType) + "=>" + (shape.Target == TargetKind.Array ? Fq(tgtElem) + "[]" : "List<" + Fq(tgtElem) + ">"));
        if (synth.ContainsKey(name))
        {
            return name;
        }

        var elem = Fq(tgtElem);
        var srcFq = Fq(srcType);
        var identity = SymbolEqualityComparer.Default.Equals(srcElem, tgtElem) && elemConverter is null && elemNull == NullHandling.None;
        var item = ElementExpr("__item", elemConverter, elemNull);
        var sb = new StringBuilder();

        if (shape.Target == TargetKind.Array)
        {
            sb.Append("    private ").Append(elem).Append("[] ").Append(name).Append('(').Append(srcFq).Append(" src)\n    {\n");
            if (identity && shape.SourceIsArray)
            {
                sb.Append("        return src is null ? global::System.Array.Empty<").Append(elem).Append(">() : (").Append(elem).Append("[])src.Clone();\n");
            }
            else if (shape.Count != CountKind.None)
            {
                var countExpr = shape.Count == CountKind.Length ? "src.Length" : "src.Count";
                sb.Append("        if (src is null) return global::System.Array.Empty<").Append(elem).Append(">();\n");
                sb.Append("        var __r = new ").Append(elem).Append('[').Append(countExpr).Append("];\n");
                sb.Append("        var __i = 0;\n");
                sb.Append("        foreach (var __item in src) { __r[__i++] = ").Append(item).Append("; }\n");
                sb.Append("        return __r;\n");
            }
            else
            {
                sb.Append("        if (src is null) return global::System.Array.Empty<").Append(elem).Append(">();\n");
                sb.Append("        var __buf = new global::System.Collections.Generic.List<").Append(elem).Append(">();\n");
                sb.Append("        foreach (var __item in src) { __buf.Add(").Append(item).Append("); }\n");
                sb.Append("        return __buf.ToArray();\n");
            }
            sb.Append("    }\n");
        }
        else // List
        {
            var listFq = "global::System.Collections.Generic.List<" + elem + ">";
            sb.Append("    private ").Append(listFq).Append(' ').Append(name).Append('(').Append(srcFq).Append(" src)\n    {\n");
            if (identity)
            {
                sb.Append("        return src is null ? new ").Append(listFq).Append("() : new ").Append(listFq).Append("(src);\n");
            }
            else
            {
                sb.Append("        var __r = new ").Append(listFq).Append("();\n");
                sb.Append("        if (src is null) return __r;\n");
                sb.Append("        foreach (var __item in src) { __r.Add(").Append(item).Append("); }\n");
                sb.Append("        return __r;\n");
            }
            sb.Append("    }\n");
        }

        synth[name] = new SynthesizedMethod(name, sb.ToString());
        return name;
    }

    /// <summary>Synthesize a reinterpret-blit array mapper (vectorized memmove with a runtime size guard).</summary>
    public static string SynthesizeBlit(
        Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcArrayType, ITypeSymbol srcElem, ITypeSymbol tgtElem)
    {
        var elem = Fq(tgtElem);
        var srcE = Fq(srcElem);
        var srcFq = Fq(srcArrayType);
        var name = "__DwarfBlit_" + Hash(srcFq + "=>" + elem + "[]");
        if (synth.ContainsKey(name))
        {
            return name;
        }
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

    private static string ElementExpr(string item, string? conv, NullHandling nh)
    {
        if (conv is not null)
        {
            return conv + "(" + item + ")";
        }
        return nh switch
        {
            NullHandling.ThrowIfNull => item + " ?? throw new global::System.InvalidOperationException(\"Collection element was null\")",
            NullHandling.ValueOrDefault => item + ".GetValueOrDefault()",
            _ => item,
        };
    }

    private static bool IsListT(ITypeSymbol t, out ITypeSymbol element)
    {
        element = t;
        if (t is INamedTypeSymbol n && n.TypeArguments.Length == 1
            && n.Name == "List"
            && n.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
        {
            element = n.TypeArguments[0];
            return true;
        }
        return false;
    }

    private static bool TryGetEnumerableElement(ITypeSymbol src, out ITypeSymbol element, out CountKind count)
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
        {
            if (candidate is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                enumerable = named;
                break;
            }
        }
        if (enumerable is null)
        {
            return false;
        }
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
