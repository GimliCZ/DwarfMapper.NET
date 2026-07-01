// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Handles integral↔integral narrowing / sign-change conversions by emitting
/// a call to <c>INumberBase&lt;TSelf&gt;.CreateChecked</c>, which throws
/// <c>OverflowException</c> when the value does not fit the target type.
///
/// This converter is only reached when there is NO implicit conversion between
/// <paramref name="src"/> and <paramref name="tgt"/> (widening stays on the
/// zero-cost direct-assign path). Enum types are excluded: enums have
/// <c>SpecialType.None</c>, so <see cref="TypeInterfaces.IsIntegral"/> returns
/// false for them — they stay in <see cref="EnumConverter"/>.
/// </summary>
internal static class NumericConverter
{
    /// <summary>
    /// Returns a synthesized method name if both <paramref name="src"/> and
    /// <paramref name="tgt"/> are integral (non-enum) types; null otherwise.
    /// </summary>
    public static string? TryCreate(
        ITypeSymbol src,
        ITypeSymbol tgt,
        Dictionary<string, SynthesizedMethod> synthesized)
    {
        if (!TypeInterfaces.IsIntegral(src) || !TypeInterfaces.IsIntegral(tgt))
            return null;

        var name = MethodName(src, tgt);
        if (!synthesized.ContainsKey(name))
        {
            // Emit: private static {Fq(tgt)} {name}({Fq(src)} v) => {Fq(tgt)}.CreateChecked(v);
            // global::System.Int32.CreateChecked(v) is a valid C# static-abstract-interface
            // invocation on .NET 10 — reflection-free and AOT-safe.
            var code = $"    private static {Fq(tgt)} {name}({Fq(src)} v) => {Fq(tgt)}.CreateChecked(v);\n";
            synthesized[name] = new SynthesizedMethod(name, code);
        }

        return name;
    }

    private static string Fq(ITypeSymbol t) =>
        t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string MethodName(ITypeSymbol src, ITypeSymbol tgt) =>
        GeneratedNames.Numeric + Sanitize(src) + "__" + Sanitize(tgt)
        + "_" + Hash("Num|" + Fq(src) + "|" + Fq(tgt));

    /// <summary>FNV-1a 32-bit hash — deterministic across processes.</summary>
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
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }
}
