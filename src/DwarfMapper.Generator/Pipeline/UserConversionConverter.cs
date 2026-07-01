// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Honors <b>user-defined conversion operators</b> (a type's <c>implicit</c>/<c>explicit operator</c>)
/// between a source and destination member type — e.g. a strong-type <c>UserId</c> that declares
/// <c>implicit operator int</c> mapped to an <c>int</c> DTO field.
/// <para>
/// The built-in conversion classifier used elsewhere deliberately excludes user-defined conversions
/// (see <c>HasImplicitConversion</c>), so this converter is the dedicated path for them. It is wired LAST
/// in <c>TryResolveConversion</c> — just before <c>DWARF005</c> — so identity, built-in implicit, numeric,
/// parse/format, enum, and auto-nesting all keep precedence; this only rescues pairs that would otherwise
/// fail. A direct cast <c>(TTarget)v</c> invokes the operator (implicit or explicit) and is a concrete
/// static call — reflection-free and AOT/trim-safe.
/// </para>
/// </summary>
internal static class UserConversionConverter
{
    /// <summary>
    /// Returns a synthesized converter-method name when a user-defined conversion operator exists from
    /// <paramref name="src"/> to <paramref name="tgt"/>; <c>null</c> otherwise. <paramref name="isExplicit"/>
    /// is <c>true</c> when the operator is <c>explicit</c> (a potentially-lossy conversion the caller should
    /// surface as <c>DWARF038</c>).
    /// </summary>
    public static string? TryCreate(
        Compilation compilation,
        ITypeSymbol src,
        ITypeSymbol tgt,
        Dictionary<string, SynthesizedMethod> synthesized,
        out bool isExplicit)
    {
        isExplicit = false;

        var conv = ((CSharpCompilation)compilation).ClassifyConversion(src, tgt);
        if (!conv.Exists || !conv.IsUserDefined)
        {
            return null;
        }

        isExplicit = conv.IsExplicit;

        var name = MethodName(src, tgt);
        if (!synthesized.ContainsKey(name))
        {
            var fqSrc = Fq(src);
            var fqTgt = Fq(tgt);
            // An explicit cast invokes a user-defined implicit OR explicit operator.
            var code = $"    private static {fqTgt} {name}({fqSrc} v) => ({fqTgt})v;\n";
            synthesized[name] = new SynthesizedMethod(name, code);
        }

        return name;
    }

    private static string Fq(ITypeSymbol t) =>
        t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string MethodName(ITypeSymbol src, ITypeSymbol tgt) =>
        GeneratedNames.UserConv + Short(src) + "_To_" + Short(tgt) + "_" + Hash(Fq(src) + "|" + Fq(tgt));

    private static string Short(ITypeSymbol t)
    {
        var sb = new StringBuilder(t.Name.Length);
        foreach (var ch in t.Name)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        return sb.Length == 0 ? "_" : sb.ToString();
    }

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
}
