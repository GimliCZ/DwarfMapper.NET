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
        => "__DwarfMap_" + prefix + "_" + Sanitize(a) + "__" + Sanitize(b);

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
}
