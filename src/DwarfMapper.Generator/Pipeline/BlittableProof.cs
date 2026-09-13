// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Proves whether two distinct unmanaged structs are byte-identical in layout AND field-name-aligned,
    ///     so a positional reinterpret equals DwarfMapper's name-based mapping. Recurses through nested structs.
    ///     <para>
    ///         Five of its helpers — <see cref="PrimitiveSize" />, <see cref="InstanceFields" />,
    ///         <see cref="IsSourceSequential" />, <see cref="FieldsSpanPartialDeclarations" /> and
    ///         <see cref="InlineArrayLength" /> — are visible to the assembly rather than private because
    ///         <see cref="LayoutHygiene" /> measures the same layouts for <c>DWARF101</c> (round 29,
    ///         <c>T0.3</c>). They encode the CLR's rules for a Sequential struct, and a second copy of those
    ///         rules would be free to drift from this proof without a single test noticing.
    ///     </para>
    /// </summary>
    internal static class BlittableProof
    {
        public static bool CanReinterpret(ITypeSymbol src, ITypeSymbol dst)
        {
            // Identity is the existing Clone() memmove, not a reinterpret.
            if (SymbolEqualityComparer.Default.Equals(src, dst))
            {
                return false;
            }

            // A TOP-LEVEL Nullable<T> element/pair can never be the type argument this proof's callers cast
            // over: MemoryMarshal.Cast<TFrom, TTo> is constrained `where : struct`, and C# refuses a
            // Nullable<T> as that argument (CS0453) even though Nullable<T> itself satisfies `unmanaged`. The
            // recursive NESTED unwrap inside LayoutIdentical (an optional FIELD keeping its enclosing struct's
            // blit) is unaffected — only the pair CanReinterpret itself is asked to bless is refused here.
            if (IsNullableValueType(src) || IsNullableValueType(dst))
            {
                return false;
            }

            return LayoutIdentical(src, dst);
        }

        /// <summary>
        ///     True when two element types occupy the SAME BYTES — identical layout, but field names ignored.
        ///     This is the proof obligation for the explicit <c>[Reinterpret]</c> opt-in.
        ///     <para>
        ///         <c>[Reinterpret]</c> exists so a caller can assert the field CORRESPONDENCE the by-name proof
        ///         cannot verify — differing member names across an assembly boundary, say. It does not, and
        ///         must not, let them assert that the bytes line up. A caller cannot know that either: a
        ///         mismatched pair does not fail loudly, it truncates. <c>MemoryMarshal.Cast&lt;int, long&gt;</c>
        ///         HALVES the span length, so the copy fills half the destination and leaves the rest zeroed —
        ///         silent data loss, which is the one failure class this project refuses outright.
        ///     </para>
        ///     <para>
        ///         The size requirement was always the documented contract (see <c>DWARF022</c>'s help text:
        ///         "only sound when both element types are unmanaged AND THE SAME SIZE"). It was enforced only
        ///         by a runtime guard inside the emitted copy, which round 26 correctly deleted — type safety
        ///         belongs to the analyzer. This is that check, moved to where it belongs.
        ///     </para>
        /// </summary>
        public static bool SameBytesIgnoringNames(ITypeSymbol src, ITypeSymbol dst)
        {
            // Same top-level refusal as CanReinterpret, and for the same reason: a Nullable<T> pair cannot be
            // the type argument its caller casts over (CS0453), whatever [Reinterpret] asserts about the bytes.
            if (IsNullableValueType(src) || IsNullableValueType(dst))
            {
                return false;
            }

            return LayoutIdentical(src, dst, byBytesOnly: true);
        }

        /// <summary>
        ///     Width in bytes of a primitive, or 0 when it has no fixed width the generator can rely on.
        ///     <para>
        ///         <c>IntPtr</c> and <c>UIntPtr</c> return 0 deliberately: their width is the platform's, so a
        ///         pair that matches on the build machine need not match where the consumer runs. Refusing them
        ///         costs an exotic opt-in; allowing them would make the proof depend on the wrong machine.
        ///     </para>
        /// </summary>
        public static int PrimitiveSize(ITypeSymbol t)
        {
            return t.SpecialType switch
            {
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => 1,
                SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => 2,
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
                SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
                _ => 0,
            };
        }

        /// <summary>
        ///     True when an enum-bearing element pair is a pure REINTERPRET, so the array can be block-copied.
        ///     <para>
        ///         The scalar path is the oracle, and reading it decides this — not the layout. Enums are the one
        ///         case where two element types can be byte-identical and still convert differently, because the
        ///         conversion is by NAME by default:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <b><c>ByName</c> (the default) can never blit.</b> Its emitted switch ends in
        ///             <c>_ =&gt; throw new ArgumentOutOfRangeException(… "Unmapped enum value")</c>, and an enum
        ///             variable may legally hold ANY value of its underlying type. A blit would pass an undefined
        ///             value through where the scalar path throws — a behaviour change, not an optimisation.
        ///             Per-name value identity does not rescue it: the throw is about values that match no member
        ///             at all.
        ///         </item>
        ///         <item>
        ///             <b><c>ByValue</c> with the SAME underlying type can.</b> It emits
        ///             <c>(Tgt)TgtU.CreateChecked((SrcU)v)</c>, and <c>CreateChecked</c> from a type to itself is
        ///             the identity — it cannot throw, and it preserves undefined values exactly as a blit does.
        ///             Differing underlying types are a real conversion (and differ in size anyway).
        ///         </item>
        ///         <item>
        ///             <b>An enum against its OWN underlying primitive can, in either direction</b>, and
        ///             regardless of strategy: those pairs go through <c>AddEnumToNum</c> / <c>AddNumToEnum</c>,
        ///             which are the same identity <c>CreateChecked</c>.
        ///         </item>
        ///     </list>
        /// </summary>
        public static bool CanReinterpretEnums(ITypeSymbol src, ITypeSymbol dst, EnumStrategy strategy)
        {
            // Identity is the existing Clone() memmove, not a reinterpret.
            if (SymbolEqualityComparer.Default.Equals(src, dst))
            {
                return false;
            }

            var srcIsEnum = src.TypeKind == TypeKind.Enum;
            var dstIsEnum = dst.TypeKind == TypeKind.Enum;

            if (srcIsEnum && dstIsEnum)
            {
                // ByName's switch throws on a value matching no member; a blit would pass it through.
                if (strategy != EnumStrategy.ByValue)
                {
                    return false;
                }

                return SameSpecialType(EnumUnderlying(src), EnumUnderlying(dst));
            }

            if (srcIsEnum)
            {
                return SameSpecialType(EnumUnderlying(src), dst.SpecialType);
            }

            if (dstIsEnum)
            {
                return SameSpecialType(EnumUnderlying(dst), src.SpecialType);
            }

            return false;
        }

        /// <summary>
        ///     The special type an enum is backed by, or <see cref="SpecialType.None" /> for a type that is not an enum.
        /// </summary>
        /// <remarks>
        ///     <see cref="CanReinterpretEnums" /> asks it only of a type whose <c>TypeKind</c> is <c>Enum</c>, and every enum
        ///     has an integral underlying type, so the "none" answer was an outcome that method never reached. Asked here,
        ///     a unit test passes a type that is not an enum.
        /// </remarks>
        internal static SpecialType EnumUnderlying(ITypeSymbol type)
        {
            return (type as INamedTypeSymbol)?.EnumUnderlyingType?.SpecialType ?? SpecialType.None;
        }

        /// <summary>Whether <paramref name="a" /> and <paramref name="b" /> are the same special type, and a real one.</summary>
        /// <remarks>
        ///     Every caller passes at least one type known to be special, an enum's underlying type or a primitive, so
        ///     "both none" was an outcome no caller reached. Asked here, a unit test passes it.
        /// </remarks>
        internal static bool SameSpecialType(SpecialType a, SpecialType b)
        {
            return a != SpecialType.None && a == b;
        }

        /// <summary>
        ///     Explains why a pair that a caller could reasonably have expected to blit did not, but ONLY for a
        ///     genuine near-miss: a pair blocked by exactly one identifiable thing.
        ///     <para>
        ///         Deliberately narrow. The broad reading — "report whenever something looked blittable" — would
        ///         fire on every ordinary struct-array mapping whose members happen to differ, which is not a
        ///         failed fast path but simply a mapping. An informational diagnostic that common gets suppressed
        ///         wholesale, and would then hide the cases worth reading. So a pair whose field counts or field
        ///         TYPES differ is not reported at all: nothing about it suggests the caller expected a blit.
        ///     </para>
        ///     <para>
        ///         The name-mismatch case is the one this exists for. Such a pair is byte-identical and one rename
        ///         away from the fast path, and nothing in the build would otherwise say so.
        ///     </para>
        ///     <para>
        ///         A one-sided <c>Nullable&lt;T&gt;</c> is the one field-TYPE difference that still gets reported:
        ///         once the optional wrapper is stripped the two sides are byte-identical, so the pair is one
        ///         <c>?</c> away from the fast path in exactly the sense the name-mismatch case is one rename away.
        ///     </para>
        /// </summary>
        public static bool TryExplainNearMiss(ITypeSymbol src, ITypeSymbol dst, out string reason)
        {
            reason = string.Empty;

            // Identity already takes the Clone() memmove; there is no fast path being missed.
            if (SymbolEqualityComparer.Default.Equals(src, dst))
            {
                return false;
            }

            // A top-level Nullable<T> pair is a categorical refusal, not a near-miss: even where the bytes
            // agree, CanReinterpret still refuses it (MemoryMarshal.Cast's `struct` constraint refuses
            // Nullable<T> — CS0453), so there is no fast path this pair is "close to" for the near-miss
            // message to explain. The NESTED case (a field that is Nullable<T> on one side only) is a
            // genuine near-miss and is handled below, inside the per-field loop.
            if (IsNullableValueType(src) || IsNullableValueType(dst))
            {
                return false;
            }

            // Two different primitives are a CONVERSION, not a near-miss blit. Same-primitive is identity, above.
            if (IsPrimitive(src) || IsPrimitive(dst))
            {
                return false;
            }

            if (src is not INamedTypeSymbol a || dst is not INamedTypeSymbol b)
            {
                return false;
            }

            if (a.TypeKind != TypeKind.Struct)
            {
                return false; // enums and classes are not almost-blittable; they are something else
            }
#pragma warning disable CA1508 // flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct
            if (b.TypeKind != TypeKind.Struct)
            {
                return false;
            }
#pragma warning restore CA1508

            if (!a.IsUnmanagedType || !b.IsUnmanagedType)
            {
                return false; // a managed member is a categorical refusal, not a near-miss
            }

            // SHAPE FIRST, blockers second — the order matters and the obvious order is wrong.
            //
            // Checking the layout blockers first looks natural (it is the order the proof itself uses) and
            // silently breaks the scoping rule: EVERY pair of distinct metadata structs would report, however
            // unrelated, because the metadata branch would answer before anything examined their fields. Note
            // that `decimal` is not in IsPrimitive, so `decimal` against `Guid` is a real reachable pair — two
            // structs with nothing in common, which would have been announced as "nearly layout-identical".
            //
            // GetMembers() works perfectly well on metadata symbols; the in-source restriction exists because
            // an absent [StructLayout] is only reliable for a type we can see the source of, not because the
            // fields are unavailable. So the shape check runs first for every pair, and a pair that is not
            // shaped alike stays silent whatever else is true of it.
            var fa = InstanceFields(a);
            var fb = InstanceFields(b);
            if (fa.Count == 0 || fa.Count != fb.Count)
            {
                return false; // a different shape entirely — an ordinary mapping, not a missed fast path
            }

            // Types must line up positionally for this to be a near-miss at all; if they do not, the pair is
            // simply two different structs and the element loop is the right answer.
            //
            // One exception, checked ahead of the general refusal: a member that is Nullable<T> on exactly one
            // side, where the OTHER side's type is itself layout-identical to T. That pair genuinely is one `?`
            // away from the fast path — unwrap the optional and the bytes agree — so it is reported here, before
            // the ordinary "field types differ" gate would silence it. A one-sided Nullable<T> whose T does NOT
            // match (e.g. `long?` against `int`) is still a real conversion, not a near-miss, and stays silent:
            // the LayoutIdentical check on the unwrapped types is what tells the two apart.
            for (var i = 0; i < fa.Count; i++)
            {
                var aIsNullable = fa[i].Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
                var bIsNullable = fb[i].Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
                if (aIsNullable != bIsNullable && LayoutIdentical(Unwrap(fa[i].Type), Unwrap(fb[i].Type)))
                {
                    reason = $"member '{fa[i].Name}' is Nullable<T> on one side only — an optional nested member has to be optional on both sides to keep the same bytes";
                    return true;
                }

                if (!LayoutIdentical(fa[i].Type, fb[i].Type))
                {
                    return false;
                }
            }

            // Only now, with the shapes known to align, is there a fast path worth explaining the absence of.
            if (!a.Locations.Any(l => l.IsInSource))
            {
                reason = $"'{a.Name}' is declared in metadata, so an absent [StructLayout] cannot be read as Sequential";
                return true;
            }

            if (!b.Locations.Any(l => l.IsInSource))
            {
                reason = $"'{b.Name}' is declared in metadata, so an absent [StructLayout] cannot be read as Sequential";
                return true;
            }

            if (!IsSourceSequential(a, out var packA, out var sizeA))
            {
                reason = $"'{a.Name}' declares a [StructLayout] that is not Sequential, so its field order is not guaranteed";
                return true;
            }

            if (!IsSourceSequential(b, out var packB, out var sizeB))
            {
                reason = $"'{b.Name}' declares a [StructLayout] that is not Sequential, so its field order is not guaranteed";
                return true;
            }

            if (FieldsSpanPartialDeclarations(a, fa))
            {
                reason = $"'{a.Name}' declares instance fields in more than one partial declaration, so the compiler defines no field order for it (CS0282); keep every instance field in one declaration";
                return true;
            }

            if (FieldsSpanPartialDeclarations(b, fb))
            {
                reason = $"'{b.Name}' declares instance fields in more than one partial declaration, so the compiler defines no field order for it (CS0282); keep every instance field in one declaration";
                return true;
            }

            if (packA != packB)
            {
                reason = $"'{a.Name}' packs to {packA} and '{b.Name}' packs to {packB}, so the two layouts differ";
                return true;
            }

            if (sizeA != sizeB)
            {
                reason = $"'{a.Name}' occupies {SizeWord(sizeA)} and '{b.Name}' {SizeWord(sizeB)}: an explicit [StructLayout] Size changes the bytes without changing the fields";
                return true;
            }

            var inlineA = InlineArrayLength(a);
            var inlineB = InlineArrayLength(b);
            if (inlineA != inlineB)
            {
                reason = $"'{a.Name}' is {InlineArrayWord(inlineA)} and '{b.Name}' is {InlineArrayWord(inlineB)}: an [InlineArray] repeats its one field, so the two counts must agree";
                return true;
            }

            for (var i = 0; i < fa.Count; i++)
                if (!SameFixedBuffer(fa[i], fb[i]))
                {
                    reason = $"field {i} is {FixedBufferWord(fa[i])} on '{a.Name}' but {FixedBufferWord(fb[i])} on '{b.Name}', and a fixed buffer's length is part of the layout";
                    return true;
                }

            for (var i = 0; i < fa.Count; i++)
                if (!string.Equals(fa[i].Name, fb[i].Name, StringComparison.Ordinal))
                {
                    reason =
                        $"field {i} is named '{fa[i].Name}' on '{a.Name}' but '{fb[i].Name}' on '{b.Name}', and a " +
                        "positional reinterpret would only agree with DwarfMapper's by-name mapping if the names line up";
                    return true;
                }

            return false; // nothing left to block it — it would have been proven, so there is nothing to explain
        }

        private static string SizeWord(int size)
        {
            return size == 0 ? "its natural size" : $"an explicit Size of {size.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string InlineArrayWord(int length)
        {
            return length == 0 ? "not an inline array" : $"an [InlineArray({length.ToString(CultureInfo.InvariantCulture)})]";
        }

        private static string FixedBufferWord(IFieldSymbol f)
        {
            return f.IsFixedSizeBuffer ? $"a fixed buffer of {f.FixedSize.ToString(CultureInfo.InvariantCulture)}" : "a pointer";
        }

        /// <summary>
        ///     True when two types are byte-identical in layout AND field-name-aligned, so a positional
        ///     reinterpret equals DwarfMapper's name-based mapping. Recurses through nested structs.
        /// </summary>
        private static bool LayoutIdentical(ITypeSymbol a, ITypeSymbol b, bool byBytesOnly = false)
        {
            if (SymbolEqualityComparer.Default
                .Equals(a, b))
            {
                return true; // same type -> trivially same layout and name-aligned
            }

            if (!a.IsUnmanagedType || !b.IsUnmanagedType)
            {
                return false;
            }

            // Nullable<T> is {bool hasValue; T value}, sequential, unmanaged when T is (C# 8 rule); two Nullable<T>
            // instantiations have the same layout exactly when their T's do. The metadata-struct rule below would refuse
            // it (no source to read [StructLayout] from), so it is decided here, by the proof over T — measured in
            // Issues/round29 §10: an optional nested member keeps the root blit at 0.15x / 0.06x.
            if (a is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nna &&
                b is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nnb)
            {
                return LayoutIdentical(nna.TypeArguments[0], nnb.TypeArguments[0], byBytesOnly);
            }

            if (IsPrimitive(a) || IsPrimitive(b))
            {
                // The automatic blit demands the SAME TYPE: int -> uint is a conversion the mapper should
                // perform properly, not a reinterpret nobody asked for. The explicit [Reinterpret] opt-in
                // demands only the same WIDTH, because asserting "treat these bits as unsigned" is precisely
                // what the caller is there to say — and it is a thing they can actually know.
                if (byBytesOnly)
                {
                    var wa = PrimitiveSize(a);
                    return wa > 0 && wa == PrimitiveSize(b);
                }

                return SameSpecialType(a.SpecialType, b.SpecialType);
            }

            if (a is not INamedTypeSymbol na || b is not INamedTypeSymbol nb)
            {
                return false;
            }

            if (na.TypeKind != TypeKind.Struct)
            {
                return false;
            }
#pragma warning disable CA1508 // flow analysis false positive: INamedTypeSymbol can be Class/Enum/Interface/Delegate, not only Struct
            if (nb.TypeKind !=
                TypeKind.Struct)
            {
                return false; // excludes enums (TypeKind.Enum): by-name enum mapping != byte copy
            }
#pragma warning restore CA1508
            if (!IsSourceSequential(na, out var packA, out var sizeA) || !IsSourceSequential(nb, out var packB, out var sizeB))
            {
                return false;
            }

            if (packA != packB || sizeA != sizeB || InlineArrayLength(na) != InlineArrayLength(nb))
            {
                return false;
            }

            var fa = InstanceFields(na);
            var fb = InstanceFields(nb);
            if (fa.Count == 0 || fa.Count != fb.Count)
            {
                return false;
            }

            // Applies to [Reinterpret] as much as to the automatic blit: the opt-in asserts that the BYTES may be
            // read positionally, and a struct with no defined field order has no defined bytes to assert about.
            if (FieldsSpanPartialDeclarations(na, fa) || FieldsSpanPartialDeclarations(nb, fb))
            {
                return false;
            }

            for (var i = 0; i < fa.Count; i++)
            {
                if (!byBytesOnly &&
                    !string.Equals(fa[i].Name,
                        fb[i].Name,
                        StringComparison.Ordinal))
                {
                    return false; // positional == name-based requires same names
                }

                if (!SameFixedBuffer(fa[i], fb[i]))
                {
                    return false; // the type comparison below sees only the element pointer type
                }

                if (!LayoutIdentical(fa[i].Type,
                        fb[i].Type,
                        byBytesOnly))
                {
                    return false; // recurse: primitive same-SpecialType, identical type, or nested layout-identical struct
                }
            }

            return true;
        }

        /// <summary>
        ///     True when <paramref name="t" /> is itself a <c>Nullable&lt;T&gt;</c> instantiation. Used to refuse
        ///     a pair at the TOP LEVEL — see <see cref="CanReinterpret" />, <see cref="SameBytesIgnoringNames" />
        ///     and <see cref="TryExplainNearMiss" /> — where blessing it would hand a caller a type argument
        ///     <c>MemoryMarshal.Cast</c>'s <c>struct</c> constraint refuses (CS0453). A NESTED field of this type
        ///     is a different question, answered by unwrapping inside <see cref="LayoutIdentical" />'s recursion.
        /// </summary>
        private static bool IsNullableValueType(ITypeSymbol t)
        {
            return t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        }

        /// <summary>The type argument of <c>Nullable&lt;T&gt;</c>, or <paramref name="t" /> itself when it is not one.</summary>
        private static ITypeSymbol Unwrap(ITypeSymbol t)
        {
            return t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
                ? n.TypeArguments[0]
                : t;
        }

        private static bool IsPrimitive(ITypeSymbol t)
        {
            return t.SpecialType is
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Char;
        }

        public static List<IFieldSymbol> InstanceFields(INamedTypeSymbol t)
        {
            var fields = new List<IFieldSymbol>();
            foreach (var m in t.GetMembers())
                if (m is IFieldSymbol f && !f.IsStatic && !f.IsConst)
                {
                    fields.Add(f);
                }

            // GetMembers() order IS the layout. Roslyn emits a type's fields in member order and the runtime lays
            // a Sequential struct out in emitted order, so this list, compared positionally, is a comparison of
            // the two layouts — and it is deliberately NOT re-sorted. It once was: a sort by (file path, position)
            // was added so that a struct whose fields are split across partial files would get the same verdict
            // whatever order the build fed the files in, on the reasoning that for a Sequential struct
            // "declaration order is the emitted layout". That reasoning is exactly right about the UNSORTED
            // list and exactly why sorting it was unsound: the compiler orders split fields by the order it
            // received the files, which is MSBuild's — case-insensitive on Windows, where "Point.cs" precedes
            // "Point.Extra.cs" — while the sort was ordinal, where it follows it. The sorted list then lined up
            // by name with a twin whose real layout was the reverse, the proof accepted, and the emitted
            // MemoryMarshal.Cast handed every element back with its fields' bytes swapped. The determinism the
            // sort was after is provided by refusing that shape instead: see FieldsSpanPartialDeclarations.
            return fields;
        }

        /// <summary>
        ///     True when the struct's instance fields are declared in more than one partial declaration — the
        ///     shape the compiler itself warns about (CS0282: "there is no defined ordering between fields in
        ///     multiple declarations of partial struct"). Its layout is whichever file order the build happened
        ///     to use, so nothing about it is provable at generation time; the scalar path maps it by name.
        ///     <para>
        ///         Decided per declaration rather than per file: two parts in ONE file are two declarations to the
        ///         compiler as well, and the rule that fits the warning is the rule that stays sound. A field whose
        ///         declaration cannot be placed at all — a synthesized field with no syntax and no owning member —
        ///         counts as spanning, because "cannot tell" is a refusal here, never a guess. Backing fields
        ///         (auto-properties, C# 14 <c>field</c>, field-like events) are placed through the member they
        ///         back. A struct with a single declaration is exempt: within one declaration source order is
        ///         member order is layout, and there is nothing left to be uncertain about.
        ///     </para>
        /// </summary>
        public static bool FieldsSpanPartialDeclarations(INamedTypeSymbol t, List<IFieldSymbol> fields)
        {
            if (t.DeclaringSyntaxReferences.Length <= 1)
            {
                return false;
            }

            (SyntaxTree Tree, TextSpan Span)? home = null;
            foreach (var f in fields)
            {
                var part = DeclaringPart(f);
                if (part is null)
                {
                    return true;
                }

                if (home is null)
                {
                    home = part;
                }
                else if (!ReferenceEquals(home.Value.Tree, part.Value.Tree) || home.Value.Span != part.Value.Span)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The type declaration that lexically contains the field (or the member it backs), as a (tree, span) key.</summary>
        /// <remarks>
        ///     Internal so a unit test can pass a field whose declaration is not a type declaration, such as an enum
        ///     member: the proof asks it only about instance fields, which always sit inside one, so the "no type
        ///     declaration" answer was never reached through it.
        /// </remarks>
        internal static (SyntaxTree Tree, TextSpan Span)? DeclaringPart(IFieldSymbol f)
        {
            var owner = f.DeclaringSyntaxReferences.Length > 0 ? f : f.AssociatedSymbol;
            if (owner is null || owner.DeclaringSyntaxReferences.Length == 0)
            {
                return null;
            }

            var reference = owner.DeclaringSyntaxReferences[0];
            var declaration = reference.GetSyntax().FirstAncestorOrSelf<TypeDeclarationSyntax>();
            return declaration is null ? null : (reference.SyntaxTree, declaration.Span);
        }

        public static bool IsSourceSequential(INamedTypeSymbol t, out int pack, out int size)
        {
            pack = 0;
            size = 0;
            // Auto-blit requires a source struct so that an absent [StructLayout] reliably means the C# default (Sequential).
            if (!t.Locations.Any(l => l.IsInSource))
            {
                return false;
            }

            foreach (var attr in t.GetAttributes())
                if (KnownNames.IsAttributeClass(attr.AttributeClass, "System.Runtime.InteropServices.StructLayoutAttribute"))
                {
                    if (attr.ConstructorArguments.Length >= 1 &&
                        attr.ConstructorArguments[0].Value is int kind &&
                        kind != 0)
                    {
                        return false; // 0 = Sequential; 2 = Explicit; 3 = Auto
                    }

                    foreach (var na in attr.NamedArguments)
                        if (na.Key == "Pack" && na.Value.Value is int p)
                        {
                            pack = p;
                        }
                        else if (na.Key == "Size" && na.Value.Value is int sz)
                        {
                            // Size is a floor on the struct's size: the runtime pads a smaller natural layout up
                            // to it. It changes the bytes without changing the field list, so two structs whose
                            // fields agree still lay out differently when their Sizes do not. Reported, and
                            // compared, like Pack; 0 is the attribute's own default and means "natural size".
                            size = sz;
                        }

                    return true;
                }

            return true; // no [StructLayout] -> C# struct default is Sequential, Pack 0, natural Size
        }

        /// <summary>
        ///     The element count of an <c>[InlineArray(n)]</c> struct, or 0 when it is not one. An inline array
        ///     repeats its single field <c>n</c> times in the runtime layout, so — like an explicit
        ///     <see cref="System.Runtime.InteropServices.StructLayoutAttribute.Size" /> — it changes the bytes
        ///     without changing the field list, and two such structs share a layout only when their counts agree.
        /// </summary>
        public static int InlineArrayLength(INamedTypeSymbol t)
        {
            foreach (var attr in t.GetAttributes())
                if (KnownNames.IsAttributeClass(attr.AttributeClass, "System.Runtime.CompilerServices.InlineArrayAttribute") &&
                    attr.ConstructorArguments.Length == 1 &&
                    attr.ConstructorArguments[0].Value is int length)
                {
                    return length;
                }

            return 0;
        }

        /// <summary>
        ///     True when two fields agree on being (or not being) a fixed-size buffer, and on its length. A fixed
        ///     buffer's symbol type is the element POINTER type — <c>fixed int Buf[4]</c> and <c>fixed int Buf[8]</c>
        ///     are both <c>int*</c> to the type comparison, as is a genuine <c>int*</c> field — while its length,
        ///     which is what the runtime reserves bytes for, lives on the field.
        /// </summary>
        private static bool SameFixedBuffer(IFieldSymbol a, IFieldSymbol b)
        {
            return a.IsFixedSizeBuffer == b.IsFixedSizeBuffer && a.FixedSize == b.FixedSize;
        }
    }
}
