// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Handles automatic string↔T conversions using .NET generic-type interfaces:
/// <list type="bullet">
///   <item><term>string → T</term><description>when T : IParsable&lt;T&gt; (and T is not an enum).
///   Emits <c>T.Parse(v, global::System.Globalization.CultureInfo.InvariantCulture)</c> when the
///   type exposes a public <c>Parse(string, IFormatProvider)</c> static method, or
///   <c>T.Parse(v)</c> for types (bool, char) whose IParsable implementation is explicit only.
///   Throws <c>FormatException</c> / <c>OverflowException</c> on bad input.</description></item>
///   <item><term>T → string</term><description>when T : IFormattable (and T is not an enum, not string itself).
///   Emits <c>v.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)</c>.
///   Uses InvariantCulture for culture-independence (e.g. decimal separator is always '.').</description></item>
/// </list>
/// Both paths are concrete static/instance calls — reflection-free and AOT/trim-safe.
/// This converter is wired AFTER <see cref="NumericConverter"/> and BEFORE
/// <see cref="EnumConverter"/> (enum↔string still routes through EnumConverter's by-name
/// switch; ParsableConverter explicitly excludes enum operands).
/// </summary>
internal static class ParsableConverter
{
    /// <summary>
    /// Returns a synthesized method name if (src, tgt) is a string↔T pair where T
    /// satisfies IParsable or IFormattable; null otherwise.
    /// Never intercepts enum↔string (enums are NOT IParsable and are guarded by TypeKind check).
    /// </summary>
    public static string? TryCreate(
        Compilation compilation,
        ITypeSymbol src,
        ITypeSymbol tgt,
        Dictionary<string, SynthesizedMethod> synthesized)
    {
        // ── string → T (T : IParsable<T>) ────────────────────────────────────────
        if (src.SpecialType == SpecialType.System_String
            && tgt.TypeKind != TypeKind.Enum
            && TypeInterfaces.ImplementsIParsable(compilation, tgt))
        {
            return AddStringToT(synthesized, tgt);
        }

        // ── T → string (T : IFormattable, or bool/char which are culture-invariant) ───
        if (tgt.SpecialType == SpecialType.System_String
            && src.SpecialType != SpecialType.System_String  // identity handled elsewhere
            && src.TypeKind != TypeKind.Enum)                 // enum→string via EnumConverter
        {
            bool isBoolOrChar = src.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char;
            if (isBoolOrChar || TypeInterfaces.ImplementsIFormattable(src))
                return AddTToString(synthesized, src);
        }

        return null;
    }

    private static string AddStringToT(Dictionary<string, SynthesizedMethod> synth, ITypeSymbol tgt)
    {
        var name = MethodName("StrParse", tgt);
        if (!synth.ContainsKey(name))
        {
            var fqTgt = Fq(tgt);
            string code;

            if (tgt.SpecialType == SpecialType.System_DateTime)
            {
                // Use ISO-8601 round-trip parsing so DateTimeKind ("Z" suffix → Utc) is preserved.
                // Plain Parse with InvariantCulture treats "Z" as Local on .NET 10; RoundtripKind fixes that.
                code = $"    private static {fqTgt} {name}(string v) => {fqTgt}.Parse(v, global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind);\n";
            }
            else if (IsDateTimeOffset(tgt))
            {
                // DateTimeOffset: RoundtripKind preserves the offset from "o" format strings.
                code = $"    private static {fqTgt} {name}(string v) => {fqTgt}.Parse(v, global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind);\n";
            }
            else if (HasPublicParseWithFormatProvider(tgt))
            {
                // Most types (int, Guid, decimal, TimeSpan, …) expose a public
                // static Parse(string, IFormatProvider) — use it with InvariantCulture.
                // e.g.: global::System.Int32.Parse(v, global::System.Globalization.CultureInfo.InvariantCulture)
                code = $"    private static {fqTgt} {name}(string v) => {fqTgt}.Parse(v, global::System.Globalization.CultureInfo.InvariantCulture);\n";
            }
            else
            {
                // bool and char implement IParsable<T> explicitly; their public Parse only
                // takes a single string argument (culture-independent by nature).
                // e.g.: global::System.Boolean.Parse(v)
                code = $"    private static {fqTgt} {name}(string v) => {fqTgt}.Parse(v);\n";
            }

            synth[name] = new SynthesizedMethod(name, code);
        }
        return name;
    }

    /// <summary>
    /// Returns true when the type exposes a public static <c>Parse(string, IFormatProvider)</c>
    /// method (not just an explicit IParsable implementation). Uses Roslyn symbol inspection
    /// to avoid reflection at generator time.
    /// </summary>
    private static bool HasPublicParseWithFormatProvider(ITypeSymbol t)
    {
        // Check by SpecialType first for the known built-ins (fast path).
        // bool (System_Boolean) and char (System_Char) implement IParsable<T> explicitly;
        // their public Parse only takes a single string argument on .NET 10.
        if (t.SpecialType == SpecialType.System_Boolean || t.SpecialType == SpecialType.System_Char)
            return false;

        // For numeric SpecialTypes we know the two-arg overload exists — skip the member walk.
        if (t.SpecialType is
            SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Decimal)
        {
            return true;
        }

        // For other types (Guid, DateTime, DateTimeOffset, TimeSpan, …), walk members.
        // IFormatProvider parameter may be nullable (IFormatProvider?) so compare by
        // OriginalDefinition / fully-qualified name without nullable annotation.
        return t.GetMembers("Parse").OfType<IMethodSymbol>().Any(m =>
            m.IsStatic
            && m.DeclaredAccessibility == Accessibility.Public
            && m.Parameters.Length == 2
            && m.Parameters[0].Type.SpecialType == SpecialType.System_String
            && (m.Parameters[1].Type.ToDisplayString() == "System.IFormatProvider"
                || m.Parameters[1].Type.ToDisplayString() == "System.IFormatProvider?"));
    }

    private static string AddTToString(Dictionary<string, SynthesizedMethod> synth, ITypeSymbol src)
    {
        var name = MethodName("FmtToStr", src);
        if (!synth.ContainsKey(name))
        {
            var fqSrc = Fq(src);
            string toStringExpr;

            if (src.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char)
            {
                // bool and char are culture-invariant by nature; their public ToString()
                // takes no arguments. The 2-arg ToString(string, IFormatProvider) is only
                // an explicit IFormattable implementation and is NOT publicly callable.
                toStringExpr = "v.ToString()";
            }
            else if (src.SpecialType == SpecialType.System_DateTime || IsDateTimeOffset(src))
            {
                // DateTime / DateTimeOffset: use ISO-8601 round-trip format "o".
                // This preserves sub-second precision and DateTimeKind / UTC offset.
                // Plain "G" format (the default when format is null) loses milliseconds/ticks.
                toStringExpr = "v.ToString(\"o\", global::System.Globalization.CultureInfo.InvariantCulture)";
            }
            else
            {
                // All other IFormattable types (int, decimal, Guid, TimeSpan, …):
                // use InvariantCulture for culture-independence (e.g. '.' as decimal separator).
                toStringExpr = "v.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)";
            }

            var code = $"    private static string {name}({fqSrc} v) => {toStringExpr};\n";
            synth[name] = new SynthesizedMethod(name, code);
        }
        return name;
    }

    /// <summary>Returns true when <paramref name="t"/> is System.DateTimeOffset.</summary>
    private static bool IsDateTimeOffset(ITypeSymbol t) =>
        t.ContainingNamespace?.ToDisplayString() == "System"
        && t.Name == "DateTimeOffset"
        && t.TypeKind == TypeKind.Struct;

    private static string Fq(ITypeSymbol t) =>
        t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string MethodName(string prefix, ITypeSymbol t) =>
        "__DwarfMap_" + prefix + "_" + Sanitize(t) + "_" + Hash(prefix + "|" + Fq(t));

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
