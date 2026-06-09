// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal enum EnumStrategy
{
    ByName = 0,
    ByValue = 1,
}

internal static class EnumConverter
{
    /// <summary>
    /// If (src, tgt) is an enum-related pair, synthesize (or reuse) a conversion
    /// method, register it in <paramref name="synthesized"/>, and return its name.
    /// Returns null if the pair is not enum-related.
    /// </summary>
    public static string? TryCreate(
        ITypeSymbol src, ITypeSymbol tgt, EnumStrategy strategy,
        Dictionary<string, SynthesizedMethod> synthesized,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics)
    {
        var srcEnum = src.TypeKind == TypeKind.Enum;
        var tgtEnum = tgt.TypeKind == TypeKind.Enum;
        var srcNum = IsIntegral(src);
        var tgtNum = IsIntegral(tgt);
        var srcStr = src.SpecialType == SpecialType.System_String;
        var tgtStr = tgt.SpecialType == SpecialType.System_String;

        if (srcEnum && tgtEnum)
        {
            return strategy == EnumStrategy.ByValue
                ? AddEnumByValue(synthesized, (INamedTypeSymbol)src, (INamedTypeSymbol)tgt)
                : AddEnumByName(synthesized, (INamedTypeSymbol)src, (INamedTypeSymbol)tgt, location, targetName, diagnostics);
        }

        if (srcEnum && tgtStr)
        {
            return AddEnumToString(synthesized, (INamedTypeSymbol)src);
        }
        if (srcStr && tgtEnum)
        {
            return AddStringToEnum(synthesized, (INamedTypeSymbol)tgt);
        }

        if (srcEnum && tgtNum)
        {
            return AddCast(synthesized, "EnumNum", src, tgt);
        }
        if (srcNum && tgtEnum)
        {
            return AddCast(synthesized, "NumEnum", src, tgt);
        }

        return null;
    }

    private static string AddEnumByValue(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol src, INamedTypeSymbol tgt)
    {
        var name = MethodName("EnumVal", src, tgt);
        if (!synth.ContainsKey(name))
        {
            var underlying = Fq(src.EnumUnderlyingType!);
            var code = $"    private static {Fq(tgt)} {name}({Fq(src)} v) => ({Fq(tgt)})({underlying})v;\n";
            synth[name] = new SynthesizedMethod(name, code);
        }
        return name;
    }

    private static string AddEnumByName(
        Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol src, INamedTypeSymbol tgt,
        LocationInfo? location, string targetName, List<DiagnosticInfo> diagnostics)
    {
        var name = MethodName("EnumName", src, tgt);

        // Completeness diagnostics are emitted on EVERY call (at the call's location),
        // so that two mapping methods using the same incomplete enum pair each report DWARF015.
        var targetNames = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m in EnumMembers(tgt))
        {
            targetNames.Add(m.Name);
        }
        var seenValuesForDiag = new HashSet<object>();
        foreach (var m in EnumMembers(src))
        {
            if (m.ConstantValue is null || !seenValuesForDiag.Add(m.ConstantValue))
            {
                continue; // alias of an already-emitted value
            }
            if (!targetNames.Contains(m.Name))
            {
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IncompleteEnumMapping, location, m.Name));
            }
        }

        // The synthesized switch method body is still registered only once (dedup by name).
        if (!synth.ContainsKey(name))
        {
            var sb = new StringBuilder();
            sb.Append("    private static ").Append(Fq(tgt)).Append(' ').Append(name)
              .Append('(').Append(Fq(src)).Append(" v) => v switch\n    {\n");
            var seenValues = new HashSet<object>();
            foreach (var m in EnumMembers(src))
            {
                if (m.ConstantValue is null || !seenValues.Add(m.ConstantValue))
                {
                    continue; // alias of an already-emitted value
                }
                if (targetNames.Contains(m.Name))
                {
                    sb.Append("        ").Append(Fq(src)).Append('.').Append(m.Name)
                      .Append(" => ").Append(Fq(tgt)).Append('.').Append(m.Name).Append(",\n");
                }
            }
            sb.Append("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(v), v, \"Unmapped enum value\"),\n    };\n");
            synth[name] = new SynthesizedMethod(name, sb.ToString());
        }
        return name;
    }

    private static IEnumerable<IFieldSymbol> EnumMembers(INamedTypeSymbol enumType)
    {
        foreach (var m in enumType.GetMembers())
        {
            if (m is IFieldSymbol { IsConst: true, HasConstantValue: true } f)
            {
                yield return f;
            }
        }
    }

    private static string AddCast(Dictionary<string, SynthesizedMethod> synth, string prefix, ITypeSymbol src, ITypeSymbol tgt)
    {
        var name = MethodName(prefix, src, tgt);
        if (!synth.ContainsKey(name))
        {
            var code = $"    private static {Fq(tgt)} {name}({Fq(src)} v) => ({Fq(tgt)})v;\n";
            synth[name] = new SynthesizedMethod(name, code);
        }
        return name;
    }

    private static bool IsIntegral(ITypeSymbol t) => t.SpecialType is
        SpecialType.System_SByte or SpecialType.System_Byte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64;

    private static string Fq(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string MethodName(string prefix, ITypeSymbol a, ITypeSymbol b)
        => "__DwarfMap_" + prefix + "_" + Sanitize(a) + "__" + Sanitize(b) + "_" + Hash(prefix + "|" + Fq(a) + "|" + Fq(b));

    /// <summary>
    /// FNV-1a 32-bit hash — deterministic across processes (does NOT use string.GetHashCode).
    /// </summary>
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

    private static string Sanitize(ITypeSymbol t)
    {
        var s = Fq(t);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        return sb.ToString();
    }

    private static string AddEnumToString(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol src)
    {
        var name = "__DwarfMap_EnumStr_" + Sanitize(src) + "_" + Hash("EnumStr|" + Fq(src));
        if (!synth.ContainsKey(name))
        {
            var sb = new StringBuilder();
            sb.Append("    private static string ").Append(name).Append('(').Append(Fq(src)).Append(" v) => v switch\n    {\n");
            var seenValues = new HashSet<object>();
            foreach (var m in EnumMembers(src))
            {
                if (m.ConstantValue is null || !seenValues.Add(m.ConstantValue))
                {
                    continue;
                }
                sb.Append("        ").Append(Fq(src)).Append('.').Append(m.Name).Append(" => \"").Append(m.Name).Append("\",\n");
            }
            sb.Append("        _ => v.ToString(),\n    };\n");
            synth[name] = new SynthesizedMethod(name, sb.ToString());
        }
        return name;
    }

    private static string AddStringToEnum(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol tgt)
    {
        var name = "__DwarfMap_StrEnum_" + Sanitize(tgt) + "_" + Hash("StrEnum|" + Fq(tgt));
        if (!synth.ContainsKey(name))
        {
            var sb = new StringBuilder();
            sb.Append("    private static ").Append(Fq(tgt)).Append(' ').Append(name).Append("(string v) => v switch\n    {\n");
            foreach (var m in EnumMembers(tgt))
            {
                sb.Append("        \"").Append(m.Name).Append("\" => ").Append(Fq(tgt)).Append('.').Append(m.Name).Append(",\n");
            }
            sb.Append("        _ => throw new global::System.ArgumentOutOfRangeException(nameof(v), v, \"Unrecognized enum name\"),\n    };\n");
            synth[name] = new SynthesizedMethod(name, sb.ToString());
        }
        return name;
    }
}
