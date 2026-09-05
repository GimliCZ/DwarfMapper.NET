// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     Measures what a transfer-model struct actually costs in memory: its size, the bytes its declaration
    ///     order wastes on alignment padding, and the field order that would waste the fewest. Reports nothing
    ///     itself — <c>DWARF101</c> is the voice, this is the arithmetic.
    ///     <para>
    ///         The layout rules are the CLR's for a <c>Sequential</c> struct: each field sits at the next offset
    ///         that is a multiple of its own alignment, the struct's alignment is its largest member's, and the
    ///         total rounds up to that. Nested structs are measured recursively; an enum is its underlying
    ///         primitive; <c>Nullable&lt;T&gt;</c> is <c>{bool hasValue; T value}</c>, so it costs one byte
    ///         padded up to T's alignment, then T.
    ///     </para>
    ///     <para>
    ///         <b>It refuses more than it answers, on purpose.</b> Every refusal below is a shape whose bytes
    ///         this generator cannot know at generation time, and a confident wrong number here would send a
    ///         consumer to reorder fields for a saving that does not exist. The native-sized refusal is the
    ///         sharpest of them, inherited from <see cref="BlittableProof.PrimitiveSize" />: <c>IntPtr</c> and
    ///         <c>UIntPtr</c> are as wide as the platform, so any size computed from them would describe the
    ///         BUILD machine rather than the machine the consumer runs on. A struct containing one has no size
    ///         this generator may claim, and <see cref="Measure" /> answers <see langword="null" /> for it.
    ///     </para>
    ///     <para>
    ///         Padding is <c>Size − Σ(size of each declared field)</c>, with a nested struct counted at its FULL
    ///         measured size — its own internal waste belongs to its own declaration. That definition is what
    ///         makes the remedy honest: every byte this reports as padding is a byte reordering THIS type's
    ///         fields can actually recover, which is precisely what the diagnostic tells the consumer to do.
    ///     </para>
    ///     <para>
    ///         The layout rules live in <see cref="BlittableProof" /> and are read from there rather than
    ///         restated here — one implementation of "what is this struct's layout", never two that can drift.
    ///     </para>
    /// </summary>
    internal static class LayoutHygiene
    {
        /// <summary>
        ///     The floor, in bytes, under which wasted padding is not worth a consumer's attention. Paired with
        ///     the quarter rule in <see cref="WastesAQuarter" /> — BOTH must hold. A flag beside an identifier
        ///     (<c>{byte; long}</c>) wastes 7 bytes of its 16, which is well over a quarter and describes half
        ///     the transfer models in existence; an informational diagnostic that common is suppressed
        ///     wholesale by the first consumer who meets it, taking the cases worth reading with it.
        /// </summary>
        public const int MinimumWastedBytes = 8;

        /// <summary>
        ///     Recursion cap for nested structs. A struct cannot legally contain itself (CS0523), but a
        ///     generator sees code the compiler has not accepted yet, so the walk is bounded rather than
        ///     trusting the language rule. Hitting the cap is a refusal, like every other "cannot tell" here.
        /// </summary>
        private const int MaxDepth = 16;

        private static readonly IReadOnlyList<string> NoOrder = Array.Empty<string>();

        /// <summary>
        ///     The measured layout of a struct, or <see langword="null" /> from <see cref="Measure" /> when its
        ///     bytes cannot be proven. Task 2.1 reads <see cref="Size" /> for the <c>≤32</c> / <c>≤64</c> byte
        ///     thresholds; <c>DWARF101</c> reads the rest.
        /// </summary>
        /// <param name="Size">Total bytes, including trailing padding.</param>
        /// <param name="Alignment">The struct's own alignment — its largest member's.</param>
        /// <param name="Padding">
        ///     Bytes lost to alignment in the DECLARED order: <see cref="Size" /> minus the sum of the field
        ///     sizes. A nested struct counts at its full size, so for a struct this is only ever what reordering
        ///     the fields of THIS type would recover.
        ///     <para>
        ///         <b>The one exception is <c>Nullable&lt;T&gt;</c></b>, whose padding — the bytes between the
        ///         flag and the value, <c>alignof T − 1</c> of them — is REAL but not recoverable: its two fields
        ///         are the runtime's, in the runtime's order. That is why its <see cref="PackedSize" /> equals its
        ///         <see cref="Size" /> and its <see cref="PackedOrder" /> is empty. A reader comparing
        ///         <see cref="Padding" /> against <c>Size − PackedSize</c> will find them disagreeing there, and
        ///         the packed figures are the ones that say what can be saved.
        ///     </para>
        /// </param>
        /// <param name="PackedSize">
        ///     What <see cref="Size" /> would be in <see cref="PackedOrder" /> — and, for a
        ///     <c>Nullable&lt;T&gt;</c>, <see cref="Size" /> itself, because none of its padding is a consumer's
        ///     to recover.
        /// </param>
        /// <param name="PackedOrder">
        ///     The declared members, largest alignment first, ties in declaration order. Empty for a
        ///     <c>Nullable&lt;T&gt;</c>, which is measured for nesting but has no field order a consumer could
        ///     restate.
        /// </param>
        internal readonly record struct Layout(
            int Size,
            int Alignment,
            int Padding,
            int PackedSize,
            IReadOnlyList<string> PackedOrder)
        {
            /// <summary>
            ///     <see cref="PackedOrder" /> as the diagnostic prints it: "Id, Value, Code, Ok, Kind".
            /// </summary>
            public string PackedOrderText => string.Join(", ", PackedOrder);
        }

        /// <summary>
        ///     The layout of <paramref name="type" />, or <see langword="null" /> when this generator cannot
        ///     prove its bytes — see the refusals on the class doc.
        /// </summary>
        public static Layout? Measure(ITypeSymbol type)
        {
            return MeasureStruct(type, 0);
        }

        /// <summary>
        ///     True when a struct wastes a quarter or more of itself AND at least
        ///     <see cref="MinimumWastedBytes" /> bytes on padding — both, never either. Integer arithmetic on
        ///     purpose: <c>Padding * 4 >= Size</c> is the quarter, with no rounding to argue about at the edge.
        /// </summary>
        /// <remarks>
        ///     The empty-order clause is a CONTRACT guard, not the mechanism that keeps <c>Nullable&lt;T&gt;</c>
        ///     quiet: what does that is the floor, since a <c>Nullable&lt;T&gt;</c>'s padding is
        ///     <c>alignof T − 1</c> and so at most 7. The clause says the other thing — that a layout with no
        ///     field order to restate is never reported, because the diagnostic's whole payload is an order the
        ///     consumer can retype. It is asserted directly (<c>WastesAQuarter_refuses_a_layout_with_no_field_order_to_restate</c>)
        ///     rather than left to be implied by a shape that cannot reach it today.
        /// </remarks>
        public static bool WastesAQuarter(Layout layout)
        {
            return layout.PackedOrder.Count > 0 &&
                   layout.Padding >= MinimumWastedBytes &&
                   layout.Padding * 4 >= layout.Size;
        }

        private static Layout? MeasureStruct(ITypeSymbol type, int depth)
        {
            if (depth > MaxDepth)
            {
                return null;
            }

            if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Struct)
            {
                return null; // classes, enums, interfaces and delegates are not laid out by these rules
            }

            if (!named.IsUnmanagedType)
            {
                return null; // a managed field has no fixed width here, and no blit for the size to serve
            }

            // Nullable<T> is the ONE metadata struct measured rather than refused: its layout is
            // {bool hasValue; T value}, Sequential, and settled by the runtime rather than by an attribute
            // this generator would have to read. BlittableProof.LayoutIdentical decides it the same way and
            // for the same reason. It is measured so an OUTER struct with an optional member still gets a
            // number; it never carries a remedy of its own, hence the empty order.
            if (named is { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                if (MeasureMember(named.TypeArguments[0], depth + 1) is not { } inner)
                {
                    return null;
                }

                var nullableSize = RoundUp(RoundUp(1, inner.Align) + inner.Size, inner.Align);
                return new Layout(nullableSize, inner.Align, nullableSize - (1 + inner.Size), nullableSize, NoOrder);
            }

            // Everything below is read from BlittableProof so the two agree by construction:
            //  - not in source, or a [StructLayout] that is not Sequential  → the field order is not knowable;
            //  - an explicit Pack                                          → every offset below is wrong;
            //  - an explicit Size                                          → the fields no longer decide the bytes;
            //  - [InlineArray]                                             → the one field repeats n times;
            //  - fields split across partial declarations (CS0282)         → the compiler defines no order;
            //  - no instance fields at all                                 → nothing to pack, nothing to save.
            if (!BlittableProof.IsSourceSequential(named, out var pack, out var explicitSize) ||
                pack != 0 ||
                explicitSize != 0 ||
                BlittableProof.InlineArrayLength(named) != 0)
            {
                return null;
            }

            var fields = BlittableProof.InstanceFields(named);
            if (fields.Count == 0 || BlittableProof.FieldsSpanPartialDeclarations(named, fields))
            {
                return null;
            }

            var members = new List<(string Name, int Size, int Align)>(fields.Count);
            foreach (var field in fields)
            {
                // A fixed buffer's field type is the element POINTER; its length — what the runtime actually
                // reserves — lives on the field, so the type-driven walk below would measure it as nothing.
                if (field.IsFixedSizeBuffer || MeasureMember(field.Type, depth + 1) is not { } measured)
                {
                    return null;
                }

                // An auto-property's backing field is named <Prop>k__BackingField, which is not something a
                // consumer can type. The remedy names the PROPERTY they would move instead; the layout is the
                // backing field's either way, and it is emitted where the property is declared.
                members.Add((field.AssociatedSymbol?.Name ?? field.Name, measured.Size, measured.Align));
            }

            var alignment = 1;
            var payload = 0;
            foreach (var member in members)
            {
                if (member.Align > alignment)
                {
                    alignment = member.Align;
                }

                payload += member.Size;
            }

            var declaredSize = SizeOf(members, alignment);

            // OrderByDescending is a STABLE sort, which is the whole rule for ties: two one-byte fields keep
            // the order the consumer declared them in, so the remedy reads as an edit of their file rather
            // than as a reshuffle they cannot account for.
            var packed = members.OrderByDescending(m => m.Align).ToList();
            var names = new List<string>(packed.Count);
            foreach (var member in packed)
            {
                names.Add(member.Name);
            }

            return new Layout(declaredSize, alignment, declaredSize - payload, SizeOf(packed, alignment), names);
        }

        /// <summary>
        ///     One field's (size, alignment): a primitive at its own width, an enum at its underlying
        ///     primitive's, a nested struct at its measured size and its own alignment.
        ///     <see langword="null" /> for anything whose width this generator may not claim — a native-sized
        ///     integer, a pointer, <c>decimal</c>, a type parameter, or a nested struct that refused.
        /// </summary>
        private static (int Size, int Align)? MeasureMember(ITypeSymbol type, int depth)
        {
            if (depth > MaxDepth)
            {
                return null;
            }

            var primitive = BlittableProof.PrimitiveSize(type);
            if (primitive > 0)
            {
                return (primitive, primitive);
            }

            if (type.TypeKind == TypeKind.Enum &&
                type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
            {
                var width = BlittableProof.PrimitiveSize(underlying);
                return width > 0 ? (width, width) : null;
            }

            return MeasureStruct(type, depth) is { } nested ? (nested.Size, nested.Alignment) : null;
        }

        /// <summary>Lays the members out in the order given and returns the struct size that results.</summary>
        private static int SizeOf(List<(string Name, int Size, int Align)> members, int alignment)
        {
            var offset = 0;
            foreach (var member in members)
            {
                offset = RoundUp(offset, member.Align) + member.Size;
            }

            return RoundUp(offset, alignment);
        }

        private static int RoundUp(int value, int alignment)
        {
            return alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
        }
    }
}
