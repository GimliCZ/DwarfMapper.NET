// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The compile-time proof behind <c>[MapDenseEnumKeys]</c>, and the helper it authorises.
    ///     <para>
    ///         <b>The emission is one line; every risk in this feature is in the proof.</b>
    ///         <c>dst[(int)kv.Key - offset] = kv.Value</c> turns a hash lookup into a raw index, so the question
    ///         this class answers is not "is this faster" but "is every index this can produce inside the array".
    ///         When the answer is no, the directive is REFUSED — <c>DWARF105</c>, an error, no output — and never
    ///         downgraded to a slower path that would hide an unprovable shape.
    ///     </para>
    ///     <para>
    ///         <b>What the proof reaches, and what it structurally cannot.</b> It reaches every value the enum
    ///         DECLARES. It does not reach the values a dictionary can actually hold: <c>(TEnum)999</c> is legal
    ///         C# and no compile-time analysis of the enum's members can bound it. That residue is closed at run
    ///         time by a range check the emitted loop carries (see <see cref="Synthesize" />), which is a
    ///         different thing from accepting an unprovable DECLARED shape behind a runtime test — the declared
    ///         shape is proven here or the directive is refused, and what remains is a value the language permits
    ///         nobody to know statically.
    ///     </para>
    /// </summary>
    internal static class DenseEnumProof
    {
        /// <summary>The metadata name of the attribute that makes a struct an inline array.</summary>
        private const string InlineArrayFqn = "System.Runtime.CompilerServices.InlineArrayAttribute";

        /// <summary>The metadata name of <c>System.FlagsAttribute</c>.</summary>
        private const string FlagsFqn = "System.FlagsAttribute";

        /// <summary>
        ///     What a proven pair emits: the destination inline array's length and element type, and the enum the
        ///     source dictionary is keyed by.
        /// </summary>
        internal readonly struct DensePlan
        {
            public DensePlan(int length, ITypeSymbol elementType, ITypeSymbol keyType)
            {
                Length = length;
                ElementType = elementType;
                KeyType = keyType;
            }

            /// <summary>The <c>[InlineArray(n)]</c> count — the exclusive upper bound on every emitted index.</summary>
            public int Length { get; }

            /// <summary>The inline array's element type, which the source's value type must match.</summary>
            public ITypeSymbol ElementType { get; }

            /// <summary>The enum the source dictionary is keyed by.</summary>
            public ITypeSymbol KeyType { get; }
        }

        /// <summary>
        ///     Proves that every member <paramref name="srcType" />'s key enum declares indexes inside
        ///     <paramref name="tgtType" />'s inline array once <paramref name="offset" /> is subtracted.
        /// </summary>
        /// <param name="srcType">The source member's type; must yield <c>KeyValuePair&lt;TEnum,TValue&gt;</c>.</param>
        /// <param name="tgtType">The destination member's type; must be an <c>[InlineArray(n)]</c> struct.</param>
        /// <param name="offset">The enum value that maps to slot 0.</param>
        /// <param name="plan">The proven shape, when the proof holds.</param>
        /// <param name="reason">
        ///     Why the proof does not hold — a sentence, printed into <c>DWARF105</c> after the member's name.
        ///     Enum member names reach it RAW (<c>ToDisplayString</c>, never escaped): this string is read by a
        ///     human, and it must name the member as their own file does.
        /// </param>
        /// <returns>True when the fast path may be emitted.</returns>
        public static bool TryProve(
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            int offset,
            out DensePlan plan,
            out string reason)
        {
            plan = default;

            if (!TryGetKeyValue(srcType, out var keyType, out var valueType))
            {
                reason = $"maps from '{srcType.ToDisplayString()}', which is not a dictionary — the source must " +
                         "yield KeyValuePair<TEnum, TValue> (a Dictionary, an IReadOnlyDictionary, an " +
                         "ImmutableDictionary), because a dense index needs a KEY to index by";
                return false;
            }

            if (!(keyType is INamedTypeSymbol enumKey) || enumKey.EnumUnderlyingType is null)
            {
                reason = $"is keyed by '{keyType.ToDisplayString()}', which is not an enum. A dense index IS the " +
                         "enum's own value, and an enum is the only key type that declares the finite value set " +
                         "the range proof runs over — anything else would make this directive an assertion " +
                         "rather than a proof";
                return false;
            }

            foreach (var a in enumKey.GetAttributes())
                if (a.AttributeClass?.ToDisplayString() == FlagsFqn)
                {
                    reason = $"is keyed by '{enumKey.ToDisplayString()}', which is a [Flags] enum. A flags enum's " +
                             "key space is the POWER SET of its members — 'A | B' is a legitimate key that no " +
                             "member declares — so proving the declared members are in range proves nothing " +
                             "about the keys the dictionary can actually hold";
                    return false;
                }

            if (!TryGetInlineArray(tgtType, out var length, out var elementType))
            {
                reason = $"writes into '{tgtType.ToDisplayString()}', which is not an [InlineArray(n)] struct. " +
                         "The destination must DECLARE the slot count, because that count is the bound every " +
                         "enum member is proven against";
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(valueType, elementType))
            {
                reason = $"maps values of type '{valueType.ToDisplayString()}' into slots of type " +
                         $"'{elementType.ToDisplayString()}'; the dense path assigns the value straight into the " +
                         "slot and performs no conversion, so the two must be the same type";
                return false;
            }

            // A nullable-annotated reference value written into a non-nullable slot is CS8601 inside a file the
            // consumer cannot edit — the unsuppressible-warning class this repository treats as a defect. Refused
            // rather than emitted with a `!`: unlike a member assignment there is no DWARF070 pathway behind this
            // one, so forgiving it would store a null with nothing said anywhere.
            if (valueType.IsReferenceType &&
                valueType.NullableAnnotation == NullableAnnotation.Annotated &&
                elementType.NullableAnnotation == NullableAnnotation.NotAnnotated)
            {
                reason = $"maps a nullable '{valueType.ToDisplayString()}' into slots declared " +
                         $"'{elementType.ToDisplayString()}', which forbids null; make the inline array's element " +
                         "type nullable, or the dictionary's value type non-nullable";
                return false;
            }

            // The RANGE, which is the whole feature. Every declared member, BOTH bounds, in a width that cannot
            // wrap: the comparison is against `offset` and `(long)offset + length` directly rather than against a
            // subtracted index, so a member near long.MinValue/MaxValue cannot overflow its way into the range.
            var upper = (long)offset + length;
            foreach (var member in enumKey.GetMembers())
            {
                if (!(member is IFieldSymbol field) || !field.IsConst || !field.HasConstantValue)
                {
                    continue;
                }

                if (TryValueOf(field.ConstantValue, out var value, out var printed) &&
                    value >= offset &&
                    value < upper)
                {
                    continue;
                }

                // ToDisplayString, never .Name: the built-in format escapes a keyword the consumer wrote as
                // `@class`, and `.Name` would strip the `@` back off a name they must be able to find in their
                // own file. The comparison above is on VALUES, so no escaped text reaches a comparison.
                reason = $"is keyed by '{enumKey.ToDisplayString()}', whose member '{field.ToDisplayString()}' = " +
                         $"{printed} falls outside the {length.ToString(CultureInfo.InvariantCulture)} slots of " +
                         $"'{tgtType.ToDisplayString()}': the mapped range is " +
                         $"[{offset.ToString(CultureInfo.InvariantCulture)}, " +
                         $"{upper.ToString(CultureInfo.InvariantCulture)}). Widen the inline array, set Offset, " +
                         "or map this member as an ordinary dictionary — an index outside the array is exactly " +
                         "what this directive exists to refuse";
                return false;
            }

            plan = new DensePlan(length, elementType, enumKey);
            reason = "";
            return true;
        }

        /// <summary>
        ///     Synthesize (or reuse) the helper that fills the inline array, and return its name.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Why the loop carries a range check when the proof already passed.</b> The proof bounds the
        ///         values the enum DECLARES; a dictionary can hold <c>(TEnum)999</c>, which no analysis of the
        ///         declarations reaches. Two things happen without the check and one of them is silent: for an
        ///         <c>int</c>-width enum the CLR's own bounds check throws <c>IndexOutOfRangeException</c> from
        ///         inside generated code with nothing naming the key; for a WIDER underlying type the
        ///         <c>(int)</c> cast wraps FIRST — <c>(int)(E)0x1_0000_0001</c> is <c>1</c>, measured — so the
        ///         write lands in a slot belonging to a different key and nothing is thrown at all. The index is
        ///         therefore computed in <c>long</c> and tested as an unsigned quantity, which catches both ends
        ///         with one comparison, and the arithmetic is written <c>unchecked</c> so a consumer building
        ///         with <c>CheckForOverflowUnderflow</c> gets the same behaviour as everyone else.
        ///     </para>
        ///     <para>
        ///         The helper returns the inline array BY VALUE, which is what lets the assignment stay an
        ///         ordinary member initializer: an inline array is indexable only through a variable, so the fill
        ///         happens against a local and the finished struct is copied into the destination member. Nothing
        ///         is allocated on either path — the local lives on the stack and the destination's slots live
        ///         inside the destination object.
        ///     </para>
        /// </remarks>
        /// <param name="synth">The mapper's synthesized-method table, keyed by helper name.</param>
        /// <param name="srcType">The source dictionary type, which becomes the parameter type.</param>
        /// <param name="tgtType">The destination inline-array type, which is the return type.</param>
        /// <param name="plan">The proven shape.</param>
        /// <param name="offset">The enum value that maps to slot 0.</param>
        /// <returns>The helper's name, for <c>MemberMap.ConverterMethod</c>.</returns>
        public static string Synthesize(
            Dictionary<string, SynthesizedMethod> synth,
            ITypeSymbol srcType,
            ITypeSymbol tgtType,
            DensePlan plan,
            int offset)
        {
            var srcFq = Fq(srcType);
            var tgtFq = Fq(tgtType);

            // The offset is part of the KEY, not merely of the body: two methods in one mapper may map the same
            // pair at two offsets, and a helper reused across them would silently give one of them the other's
            // arithmetic.
            var name = "__DwarfDense_" +
                       StableHash.Fnv1a(srcFq + "=>" + tgtFq + "@" + offset.ToString(CultureInfo.InvariantCulture));
            if (synth.ContainsKey(name))
            {
                return name;
            }

            var len = plan.Length.ToString(CultureInfo.InvariantCulture);
            var off = offset.ToString(CultureInfo.InvariantCulture);
            var upper = ((long)offset + plan.Length).ToString(CultureInfo.InvariantCulture);
            // A NULLABLE parameter for a reference source, on the collection/dictionary helpers' precedent and
            // for their reason: this helper answers null itself, so it must be callable with a
            // nullable-annotated source member without the caller appending a `!`. `__DwarfDense_` deliberately
            // does not carry the `__DwarfMap_` prefix that drives GeneratedNames.IsSynthesized, so the emitter
            // never forgives the argument — and a non-nullable parameter would then be CS8604 inside a file the
            // consumer cannot edit.
            var param = srcFq + (srcType.IsReferenceType ? "?" : "");
            var w = new CodeWriter(1);
            using (w.Block("private static " + tgtFq + " " + name + "(" + param + " src)"))
            {
                w.Line("var __r = default(" + tgtFq + ");");

                // Only for a REFERENCE source: `is null` against a non-nullable value type is CS0037, and a
                // struct dictionary (rare, but legal) would otherwise emit code that does not compile.
                if (srcType.IsReferenceType)
                {
                    w.Line("if (src is null) return __r;");
                }

                using (w.Block("foreach (var __kv in src)"))
                {
                    w.Line("var __i = unchecked((long)__kv.Key - " + off + "L);");
                    using (w.Block("if (unchecked((ulong)__i) >= " + len + "UL)"))
                    {
                        w.Line("throw new global::System.ArgumentOutOfRangeException(nameof(src), __kv.Key,");
                        w.Line("    \"DwarfMapper: dense enum key is outside the mapped range [" + off + ", " +
                               upper + ") of '" + tgtFq.Replace("global::", "") +
                               "'. The enum declares no member with that value, so no compile-time proof could " +
                               "cover it.\");");
                    }

                    w.Line("__r[(int)__i] = __kv.Value;");
                }

                w.Line("return __r;");
            }

            synth[name] = new SynthesizedMethod(name, w.ToString());
            return name;
        }

        /// <summary>The <c>KeyValuePair&lt;K,V&gt;</c> a source type yields, if it yields one.</summary>
        private static bool TryGetKeyValue(ITypeSymbol src, out ITypeSymbol key, out ITypeSymbol value)
        {
            key = src;
            value = src;

            foreach (var candidate in Self(src))
                if (candidate is INamedTypeSymbol named &&
                    named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
                    named.TypeArguments[0] is INamedTypeSymbol elem &&
                    elem.Name == "KeyValuePair" &&
                    elem.TypeArguments.Length == 2 &&
                    elem.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic")
                {
                    key = elem.TypeArguments[0];
                    value = elem.TypeArguments[1];
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     The slot count and element type of an <c>[InlineArray(n)]</c> struct.
        ///     <para>
        ///         The element type is read off the struct's single instance field, which is what the language
        ///         requires an inline array to declare. The field itself is never NAMED in emitted code — an
        ///         inline array is indexed through the language feature, not through its backing field — so a
        ///         private field on a consumer's own struct stays private and no accessibility is bypassed.
        ///     </para>
        /// </summary>
        private static bool TryGetInlineArray(ITypeSymbol tgt, out int length, out ITypeSymbol elementType)
        {
            length = 0;
            elementType = tgt;

            if (tgt.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            var declared = -1;
            foreach (var a in tgt.GetAttributes())
                if (a.AttributeClass?.ToDisplayString() == InlineArrayFqn &&
                    a.ConstructorArguments.Length == 1 &&
                    a.ConstructorArguments[0].Value is int n)
                {
                    declared = n;
                    break;
                }

            if (declared <= 0)
            {
                return false;
            }

            IFieldSymbol? only = null;
            foreach (var m in tgt.GetMembers())
            {
                if (!(m is IFieldSymbol f) || f.IsStatic || f.IsConst)
                {
                    continue;
                }

                if (only is not null)
                {
                    return false;
                }

                only = f;
            }

            if (only is null)
            {
                return false;
            }

            length = declared;
            elementType = only.Type;
            return true;
        }

        /// <summary>
        ///     One enum member's constant value as a <see cref="long" />, plus the text the diagnostic prints.
        /// </summary>
        /// <remarks>
        ///     <c>ulong</c> is handled separately rather than through a general numeric conversion: a value above
        ///     <see cref="long.MaxValue" /> has no <c>long</c> representation at all, and converting it would
        ///     either throw or wrap into a NEGATIVE number a range test could read as in-bounds. It is reported
        ///     as out of range instead, which is a refusal — the direction this feature must always fail in.
        /// </remarks>
        private static bool TryValueOf(object? constant, out long value, out string printed)
        {
            switch (constant)
            {
                case sbyte v:
                    value = v;
                    break;
                case byte v:
                    value = v;
                    break;
                case short v:
                    value = v;
                    break;
                case ushort v:
                    value = v;
                    break;
                case int v:
                    value = v;
                    break;
                case uint v:
                    value = v;
                    break;
                case long v:
                    value = v;
                    break;
                case ulong v:
                    printed = v.ToString(CultureInfo.InvariantCulture);
                    if (v > long.MaxValue)
                    {
                        value = 0;
                        return false;
                    }

                    value = (long)v;
                    return true;
                default:
                    value = 0;
                    printed = "?";
                    return false;
            }

            printed = value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static IEnumerable<ITypeSymbol> Self(ITypeSymbol t)
        {
            yield return t;
            foreach (var i in t.AllInterfaces)
                yield return i;
        }

        private static string Fq(ITypeSymbol t)
        {
            return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
    }
}
