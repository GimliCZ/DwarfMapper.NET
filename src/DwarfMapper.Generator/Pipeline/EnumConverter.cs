// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

internal enum EnumStrategy
{
    ByName = 0,
    ByValue = 1
}

internal static class EnumConverter
{
    /// <summary>
    ///     If (src, tgt) is an enum-related pair, synthesize (or reuse) a conversion
    ///     method, register it in <paramref name="synthesized" />, and return its name.
    ///     Returns null if the pair is not enum-related.
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
            return strategy == EnumStrategy.ByValue
                ? AddEnumByValue(synthesized, (INamedTypeSymbol)src, (INamedTypeSymbol)tgt)
                : AddEnumByName(synthesized, (INamedTypeSymbol)src, (INamedTypeSymbol)tgt, location, targetName,
                    diagnostics);

        if (srcEnum && tgtStr) return AddEnumToString(synthesized, (INamedTypeSymbol)src);
        if (srcStr && tgtEnum) return AddStringToEnum(synthesized, (INamedTypeSymbol)tgt);

        if (srcEnum && tgtNum) return AddEnumToNum(synthesized, (INamedTypeSymbol)src, tgt);
        if (srcNum && tgtEnum) return AddNumToEnum(synthesized, src, (INamedTypeSymbol)tgt);

        return null;
    }

    private static string AddEnumByValue(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol src,
        INamedTypeSymbol tgt)
    {
        var name = MethodName("EnumVal", src, tgt);
        if (!synth.ContainsKey(name))
        {
            // Cast source enum to its underlying type, then use CreateChecked into the
            // target enum's underlying type, then cast to the target enum.
            // Throws OverflowException when the source value does not fit the target underlying.
            var srcUnderlying = Fq(src.EnumUnderlyingType!);
            var tgtUnderlying = Fq(tgt.EnumUnderlyingType!);
            var code =
                $"    private static {Fq(tgt)} {name}({Fq(src)} v) => ({Fq(tgt)}){tgtUnderlying}.CreateChecked(({srcUnderlying})v);\n";
            synth[name] = new SynthesizedMethod(name, code);
        }

        return name;
    }

    /// <summary>
    ///     enum → numeric: cast enum to its underlying type, then CreateChecked into target.
    ///     Throws OverflowException when the enum value does not fit the numeric target.
    /// </summary>
    private static string AddEnumToNum(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol srcEnum,
        ITypeSymbol tgtNum)
    {
        var name = MethodName("EnumNum", srcEnum, tgtNum);
        if (!synth.ContainsKey(name))
        {
            var srcUnderlying = Fq(srcEnum.EnumUnderlyingType!);
            // e.g.: global::System.Int32.CreateChecked((global::System.Int64)v)
            var code =
                $"    private static {Fq(tgtNum)} {name}({Fq(srcEnum)} v) => {Fq(tgtNum)}.CreateChecked(({srcUnderlying})v);\n";
            synth[name] = new SynthesizedMethod(name, code);
        }

        return name;
    }

    /// <summary>
    ///     numeric → enum: CreateChecked into the enum's underlying type, then cast to enum.
    ///     Throws OverflowException when the numeric value does not fit the enum's underlying.
    /// </summary>
    private static string AddNumToEnum(Dictionary<string, SynthesizedMethod> synth, ITypeSymbol srcNum,
        INamedTypeSymbol tgtEnum)
    {
        var name = MethodName("NumEnum", srcNum, tgtEnum);
        if (!synth.ContainsKey(name))
        {
            var tgtUnderlying = Fq(tgtEnum.EnumUnderlyingType!);
            // e.g.: (global::Demo.Color)global::System.Int32.CreateChecked(v)
            var code =
                $"    private static {Fq(tgtEnum)} {name}({Fq(srcNum)} v) => ({Fq(tgtEnum)}){tgtUnderlying}.CreateChecked(v);\n";
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
        var targetNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in EnumMembers(tgt)) targetNames.Add(m.Name);
        var seenValuesForDiag = new HashSet<object>();
        foreach (var m in EnumMembers(src))
        {
            if (m.ConstantValue is null ||
                !seenValuesForDiag.Add(m.ConstantValue)) continue; // alias of an already-emitted value
            if (!targetNames.Contains(m.Name))
                diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.IncompleteEnumMapping, location, m.Name));
        }

        // The synthesized switch method body is still registered only once (dedup by name).
        if (!synth.ContainsKey(name))
            synth[name] = new SynthesizedMethod(name,
                IsFlagsEnum(src) && IsFlagsEnum(tgt)
                    ? EmitFlagsByName(name, src, tgt, targetNames)
                    : EmitSwitchByName(name, src, tgt, targetNames));

        return name;
    }

    private static string EmitSwitchByName(
        string name, INamedTypeSymbol src, INamedTypeSymbol tgt, HashSet<string> targetNames)
    {
        var sb = new StringBuilder();
        sb.Append("    private static ").Append(Fq(tgt)).Append(' ').Append(name)
            .Append('(').Append(Fq(src)).Append(" v) => v switch\n    {\n");
        var seenValues = new HashSet<object>();
        foreach (var m in EnumMembers(src))
        {
            if (m.ConstantValue is null ||
                !seenValues.Add(m.ConstantValue)) continue; // alias of an already-emitted value
            if (targetNames.Contains(m.Name))
                sb.Append("        ").Append(Fq(src)).Append('.').Append(m.Name)
                    .Append(" => ").Append(Fq(tgt)).Append('.').Append(m.Name).Append(",\n");
        }

        sb.Append(
            "        _ => throw new global::System.ArgumentOutOfRangeException(nameof(v), v, \"Unmapped enum value\"),\n    };\n");
        return sb.ToString();
    }

    /// <summary>
    ///     By-name mapping for a <c>[Flags]</c> enum pair, done BITWISE.
    ///     <para>
    ///     A one-arm-per-declared-member switch is simply wrong for a flags enum: the whole point of
    ///     <c>[Flags]</c> is that <c>Read | Write</c> (value 3) is a legal value, and it matches no declared
    ///     member, so it fell to <c>_ =&gt; throw</c>. Every combined value — the ordinary case for a flags enum
    ///     — threw <c>ArgumentOutOfRangeException</c> at run time, on valid input, with nothing said at build
    ///     time. And <c>ByName</c> is the DEFAULT strategy, so this was the default behaviour.
    ///     </para>
    ///     <para>
    ///     Instead: translate each set flag individually and re-combine. Bits that correspond to no declared
    ///     source flag are left over and still throw — an undeclared bit is genuinely unmappable, and staying
    ///     loud there is the point. (A source flag with no same-named target member is caught earlier, at
    ///     compile time, by DWARF015.)
    ///     </para>
    /// </summary>
    private static string EmitFlagsByName(
        string name, INamedTypeSymbol src, INamedTypeSymbol tgt, HashSet<string> targetNames)
    {
        var underlying = Fq(src.EnumUnderlyingType!);

        var sb = new StringBuilder();
        sb.Append("    private static ").Append(Fq(tgt)).Append(' ').Append(name)
            .Append('(').Append(Fq(src)).Append(" v)\n    {\n");
        sb.Append("        var __r = default(").Append(Fq(tgt)).Append(");\n");
        sb.Append("        var __rest = (").Append(underlying).Append(")v;\n");

        var seenValues = new HashSet<object>();
        foreach (var m in EnumMembers(src))
        {
            if (m.ConstantValue is null || !seenValues.Add(m.ConstantValue)) continue;
            if (!targetNames.Contains(m.Name)) continue;

            // The zero member (None = 0) carries no bit: `(v & None) == None` is true for every value, so
            // testing it would be meaningless. It needs no arm — default(TTarget) already IS zero.
            if (IsZero(m.ConstantValue)) continue;

            var srcMember = Fq(src) + "." + m.Name;
            sb.Append("        if ((v & ").Append(srcMember).Append(") == ").Append(srcMember).Append(")\n");
            sb.Append("        {\n");
            sb.Append("            __r |= ").Append(Fq(tgt)).Append('.').Append(m.Name).Append(";\n");
            sb.Append("            __rest &= unchecked((").Append(underlying).Append(")~(")
                .Append(underlying).Append(')').Append(srcMember).Append(");\n");
            sb.Append("        }\n");
        }

        sb.Append("        if (__rest != 0) throw new global::System.ArgumentOutOfRangeException(")
            .Append("nameof(v), v, \"Unmapped enum flag\");\n");
        sb.Append("        return __r;\n    }\n");
        return sb.ToString();
    }

    /// <summary>True when the enum carries <c>[Flags]</c>, so combined values are legal by design.</summary>
    private static bool IsFlagsEnum(INamedTypeSymbol enumType)
    {
        foreach (var attribute in enumType.GetAttributes())
            if (attribute.AttributeClass is { Name: "FlagsAttribute" } a
                && a.ContainingNamespace?.ToDisplayString() == "System")
                return true;

        return false;
    }

    /// <summary>
    ///     Whether an enum member's constant value is zero, without enumerating the eight underlying types by
    ///     hand.
    ///     <para>
    ///     The obvious tool is generic math (<c>INumberBase&lt;T&gt;.IsZero</c>), but it is unavailable HERE: the
    ///     generator targets netstandard2.0 because that is what the Roslyn host loads, and generic math is
    ///     .NET 7+. It is available in the code we EMIT (which targets net10 — that is why the numeric
    ///     converters can emit <c>CreateChecked</c>), just not in the process doing the emitting.
    ///     </para>
    ///     <para>
    ///     <c>decimal</c> is the netstandard2.0 stand-in: every enum underlying type is integral, and decimal
    ///     represents all of <c>sbyte</c>…<c>ulong</c> exactly, so a single conversion decides it for all of
    ///     them with no per-type list to drift out of date.
    ///     </para>
    /// </summary>
    private static bool IsZero(object constantValue)
    {
        return constantValue is IConvertible c && c.ToDecimal(CultureInfo.InvariantCulture) == 0m;
    }

    private static IEnumerable<IFieldSymbol> EnumMembers(INamedTypeSymbol enumType)
    {
        foreach (var m in enumType.GetMembers())
            if (m is IFieldSymbol { IsConst: true, HasConstantValue: true } f)
                yield return f;
    }

    private static bool IsIntegral(ITypeSymbol t)
    {
        return TypeInterfaces.IsIntegral(t);
    }

    private static string Fq(ITypeSymbol t)
    {
        return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string MethodName(string prefix, ITypeSymbol a, ITypeSymbol b)
    {
        return GeneratedNames.Base + prefix + "_" + Sanitize(a) + "__" + Sanitize(b) + "_" +
               Hash(prefix + "|" + Fq(a) + "|" + Fq(b));
    }

    /// <summary>
    ///     FNV-1a 32-bit hash — deterministic across processes (does NOT use string.GetHashCode).
    /// </summary>
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
        foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }

    /// <summary>
    ///     The STRING form of an enum member for enum↔string mapping: <c>[EnumMember(Value = "…")]</c> wins,
    ///     then <c>[Description("…")]</c>, else the C# member name. Lets an enum expose a serialization/display
    ///     name that differs from its identifier (<c>InProgress → "in_progress"</c>) without a custom converter.
    ///     <para>
    ///     Applies to NON-flags enums only; a <c>[Flags]</c> enum keeps member-name semantics in both directions
    ///     (its string form is a comma-joined list that <c>Enum.ToString</c> builds from identifiers).
    ///     </para>
    /// </summary>
    private static string SerializedName(IFieldSymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            var cls = attribute.AttributeClass;
            if (cls is { Name: "EnumMemberAttribute" }
                && cls.ContainingNamespace?.ToDisplayString() == "System.Runtime.Serialization")
                foreach (var na in attribute.NamedArguments)
                    if (na.Key == "Value" && na.Value.Value is string v)
                        return v;
        }

        foreach (var attribute in member.GetAttributes())
        {
            var cls = attribute.AttributeClass;
            if (cls is { Name: "DescriptionAttribute" }
                && cls.ContainingNamespace?.ToDisplayString() == "System.ComponentModel"
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string d)
                return d;
        }

        return member.Name;
    }

    /// <summary>Escapes a serialized name for emission as a C# string literal.</summary>
    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string AddEnumToString(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol src)
    {
        var name = GeneratedNames.EnumToStr + Sanitize(src) + "_" + Hash("EnumStr|" + Fq(src));
        if (!synth.ContainsKey(name))
        {
            // A [Flags] enum keeps Enum.ToString() (comma-joined identifiers) for combined values;
            // [EnumMember]/[Description] custom names apply to non-flags enums only.
            var flags = IsFlagsEnum(src);
            var sb = new StringBuilder();
            sb.Append("    private static string ").Append(name).Append('(').Append(Fq(src))
                .Append(" v) => v switch\n    {\n");
            var seenValues = new HashSet<object>();
            foreach (var m in EnumMembers(src))
            {
                if (m.ConstantValue is null || !seenValues.Add(m.ConstantValue)) continue;
                var text = flags ? m.Name : SerializedName(m);
                sb.Append("        ").Append(Fq(src)).Append('.').Append(m.Name).Append(" => \"")
                    .Append(Escape(text)).Append("\",\n");
            }

            sb.Append("        _ => v.ToString(),\n    };\n");
            synth[name] = new SynthesizedMethod(name, sb.ToString());
        }

        return name;
    }

    private static string AddStringToEnum(Dictionary<string, SynthesizedMethod> synth, INamedTypeSymbol tgt)
    {
        var name = GeneratedNames.StrToEnum + Sanitize(tgt) + "_" + Hash("StrEnum|" + Fq(tgt));
        if (!synth.ContainsKey(name))
            synth[name] = new SynthesizedMethod(name,
                IsFlagsEnum(tgt) ? EmitStringToFlags(name, tgt) : EmitStringToEnum(name, tgt));

        return name;
    }

    private static string EmitStringToEnum(string name, INamedTypeSymbol tgt)
    {
        var sb = new StringBuilder();
        sb.Append("    private static ").Append(Fq(tgt)).Append(' ').Append(name)
            .Append("(string v) => v switch\n    {\n");
        // Match on the serialized name ([EnumMember]/[Description] or the identifier). De-dup by that name so a
        // duplicated [EnumMember(Value=…)] cannot emit two identical case labels (CS0152) — first member wins.
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in EnumMembers(tgt))
        {
            var text = SerializedName(m);
            if (!seenNames.Add(text)) continue;
            sb.Append("        \"").Append(Escape(text)).Append("\" => ").Append(Fq(tgt)).Append('.').Append(m.Name)
                .Append(",\n");
        }

        sb.Append(
            "        _ => throw new global::System.ArgumentOutOfRangeException(nameof(v), v, \"Unrecognized enum name\"),\n    };\n");
        return sb.ToString();
    }

    /// <summary>
    ///     string → <c>[Flags]</c> enum. The counterpart of the bitwise by-name mapping, and broken for the
    ///     same reason: a flags enum formats a combined value as <c>"Read, Write"</c> (that is what
    ///     <c>Enum.ToString()</c> produces, and what <see cref="AddEnumToString" /> therefore emits), but the
    ///     one-arm-per-name switch only recognised single names and threw on every combination — so a value
    ///     this very library had just written out could not be read back in. Split on the separator and OR the
    ///     parts. Unknown names still throw: reflection-free, allocation-light, and never silent.
    /// </summary>
    private static string EmitStringToFlags(string name, INamedTypeSymbol tgt)
    {
        var sb = new StringBuilder();
        sb.Append("    private static ").Append(Fq(tgt)).Append(' ').Append(name).Append("(string v)\n    {\n");
        sb.Append("        var __r = default(").Append(Fq(tgt)).Append(");\n");
        sb.Append("        foreach (var __part in v.Split(','))\n");
        sb.Append("        {\n");
        sb.Append("            __r |= __part.Trim() switch\n            {\n");
        foreach (var m in EnumMembers(tgt))
            sb.Append("                \"").Append(m.Name).Append("\" => ").Append(Fq(tgt)).Append('.')
                .Append(m.Name).Append(",\n");
        sb.Append("                _ => throw new global::System.ArgumentOutOfRangeException(")
            .Append("nameof(v), v, \"Unrecognized enum name\"),\n            };\n");
        sb.Append("        }\n\n        return __r;\n    }\n");
        return sb.ToString();
    }
}
