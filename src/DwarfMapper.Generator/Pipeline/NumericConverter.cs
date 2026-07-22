// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
///     Handles integral↔integral narrowing / sign-change conversions by emitting
///     a call to <c>INumberBase&lt;TSelf&gt;.CreateChecked</c>, which throws
///     <c>OverflowException</c> when the value does not fit the target type.
///     This converter is only reached when there is NO implicit conversion between
///     <paramref name="src" /> and <paramref name="tgt" /> (widening stays on the
///     zero-cost direct-assign path). Enum types are excluded: enums have
///     <c>SpecialType.None</c>, so <see cref="TypeInterfaces.IsIntegral" /> returns
///     false for them — they stay in <see cref="EnumConverter" />.
/// </summary>
internal static class NumericConverter
{
    /// <summary>
    ///     True when <paramref name="src" /> and <paramref name="tgt" /> are both basic numeric types but sit in
    ///     DIFFERENT categories (integer kind vs floating/decimal kind) — e.g. <c>long → double</c>,
    ///     <c>int → float</c>, <c>long → decimal</c>. Such conversions are *implicit* in C#, so the compiler is
    ///     silent, yet they lose precision once the magnitude exceeds the mantissa.
    ///     <para>
    ///     Shared by BOTH engines on purpose. It previously lived as a private helper inside
    ///     <c>MapperExtractor</c>, so the class model reported DWARF038 for these while the <c>[MapTo]</c>
    ///     registry — which could not see it — emitted a silent direct assignment for the same types. Having one
    ///     definition both engines call is what stops that drift recurring.
    ///     </para>
    /// </summary>
    public static bool IsCrossCategoryLossy(ITypeSymbol src, ITypeSymbol tgt)
    {
        static int Cat(ITypeSymbol t)
        {
            return t.SpecialType switch
            {
                SpecialType.System_SByte or SpecialType.System_Byte
                    or SpecialType.System_Int16 or SpecialType.System_UInt16
                    or SpecialType.System_Int32 or SpecialType.System_UInt32
                    or SpecialType.System_Int64 or SpecialType.System_UInt64
                    or SpecialType.System_Char => 1, // integer kind
                SpecialType.System_Single or SpecialType.System_Double
                    or SpecialType.System_Decimal => 2, // floating / decimal kind
                _ => 0 // not a numeric basic type
            };
        }

        var a = Cat(src);
        var b = Cat(tgt);
        return a != 0 && b != 0 && a != b;
    }

    /// <summary>
    ///     Returns a synthesized method name if both <paramref name="src" /> and
    ///     <paramref name="tgt" /> are integral (non-enum) types; null otherwise.
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

    private static string Fq(ITypeSymbol t)
    {
        return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string MethodName(ITypeSymbol src, ITypeSymbol tgt)
    {
        return GeneratedNames.Numeric + Sanitize(src) + "__" + Sanitize(tgt)
               + "_" + Hash("Num|" + Fq(src) + "|" + Fq(tgt));
    }

    /// <summary>FNV-1a 32-bit hash — deterministic across processes.</summary>
    private static string Hash(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619u;
            }

            return h.ToString("x8", CultureInfo.InvariantCulture);
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
