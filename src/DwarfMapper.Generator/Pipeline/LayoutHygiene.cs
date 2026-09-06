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
    ///     <para>
    ///         Three of the helpers below — <see cref="MeasureMember(ITypeSymbol)" />, <see cref="AsOptional" />
    ///         and <see cref="LayOut" /> — are visible to the assembly rather than private, on the same footing
    ///         and for the same reason <see cref="BlittableProof" /> opened five of its own to this file (round
    ///         29, <c>T0.3</c>): <see cref="TransferModelShape" /> sizes the WOULD-BE struct of a class for the
    ///         <c>≤32</c> / <c>≤64</c> byte thresholds, and <see cref="Measure" /> cannot answer that — it
    ///         refuses everything that is not already an unmanaged struct in source, which every class is. So it
    ///         supplies the members and this file does the arithmetic; a second copy of the alignment rules over
    ///         there would be free to drift from these without a single test noticing. What lives in
    ///         <see cref="TransferModelShape" /> is only the POLICY question this file has no answer to — what a
    ///         reference field is worth — and that policy is why a verdict says whether its size is an estimate.
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

                var optional = AsOptional(inner);
                return new Layout(
                    optional.Size,
                    optional.Align,
                    optional.Size - (1 + inner.Size),
                    optional.Size,
                    NoOrder);
            }

            // The known-layout BCL types, asked for directly rather than as someone's field. Same contract as
            // Nullable<T> above: a size and an alignment, and NO field order — their fields are the runtime's,
            // so there is no remedy to print and WastesAQuarter refuses them on the empty order alone.
            if (FixedLayoutBclSize(named) is { } bcl)
            {
                return new Layout(bcl.Size, bcl.Align, 0, bcl.Size, NoOrder);
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

            return LayOut(members);
        }

        /// <summary>
        ///     Lays out <paramref name="members" /> — name, size and alignment, in DECLARATION order — as a
        ///     Sequential struct and reports what it costs. The arithmetic every caller shares: a struct's
        ///     alignment is its largest member's, each member sits at the next multiple of its own alignment,
        ///     and the total rounds up to the struct's.
        ///     <para>
        ///         <see cref="Layout.PackedOrder" /> sorts by ALIGNMENT descending, not by size — the trap
        ///         round 29 <c>T0.3b</c> wrote down, since a 16-byte 4-aligned <c>Guid</c> packs AFTER an
        ///         8-byte <c>long</c>. The sort is stable, so ties keep the consumer's declaration order and the
        ///         remedy reads as an edit of their file rather than a reshuffle they cannot account for.
        ///     </para>
        /// </summary>
        public static Layout LayOut(IReadOnlyList<(string Name, int Size, int Align)> members)
        {
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

            var packed = members.OrderByDescending(m => m.Align).ToList();
            var names = new List<string>(packed.Count);
            foreach (var member in packed)
            {
                names.Add(member.Name);
            }

            return new Layout(declaredSize, alignment, declaredSize - payload, SizeOf(packed, alignment), names);
        }

        /// <summary>
        ///     One member's (size, alignment) — a primitive at its own width, an enum at its underlying
        ///     primitive's, one of the known-layout BCL structs at the size and alignment
        ///     <see cref="FixedLayoutBclSize" /> states, a nested struct at its measured size.
        ///     <see langword="null" /> for anything whose width this generator may not claim, which the caller
        ///     must treat as a refusal and never as zero.
        /// </summary>
        public static (int Size, int Align)? MeasureMember(ITypeSymbol type)
        {
            return MeasureMember(type, 0);
        }

        /// <summary>
        ///     What <paramref name="inner" /> costs once it is optional: <c>Nullable&lt;T&gt;</c> is
        ///     <c>{bool hasValue; T value}</c>, so the flag is padded up to T's alignment and the whole rounds
        ///     up to it again. Shared with the <c>Nullable&lt;T&gt;</c> arm of <see cref="MeasureStruct" /> so
        ///     an optional NESTED transfer model and an optional field are costed by one rule.
        /// </summary>
        public static (int Size, int Align) AsOptional((int Size, int Align) inner)
        {
            return (RoundUp(RoundUp(1, inner.Align) + inner.Size, inner.Align), inner.Align);
        }

        /// <summary>
        ///     One field's (size, alignment): a primitive at its own width, an enum at its underlying
        ///     primitive's, a <see cref="FixedLayoutBclSize" /> entry at its stated layout, a nested struct at
        ///     its measured size and its own alignment.
        ///     <see langword="null" /> for anything whose width this generator may not claim — a native-sized
        ///     integer, a pointer, a metadata struct outside that table, a type parameter, or a nested struct
        ///     that refused.
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

            if (FixedLayoutBclSize(type) is { } bcl)
            {
                return bcl;
            }

            return MeasureStruct(type, depth) is { } nested ? (nested.Size, nested.Alignment) : null;
        }

        /// <summary>
        ///     The BCL value types whose SIZE AND ALIGNMENT this generator is allowed to know, or
        ///     <see langword="null" /> for everything else.
        ///     <para>
        ///         Every other metadata struct is refused, because its bytes are not knowable from symbols alone —
        ///         and that refusal is the right default. These are the exception on the same footing
        ///         <c>Nullable&lt;T&gt;</c> already stood on. Without them the refusal CASCADES, and the cascade
        ///         is the damage: an unmeasurable member refuses the whole ENCLOSING type, so one
        ///         <c>DateTimeOffset CreatedAt</c> makes an otherwise ordinary DTO invisible to both
        ///         <c>DWARF101</c> and <c>DWARF103</c> — the diagnostics go quiet on precisely the types they
        ///         exist for.
        ///     </para>
        ///     <para>
        ///         <b>The bar for adding an entry</b>, so the next person extending this knows what qualifies.
        ///         All three, never two:
        ///     </para>
        ///     <list type="number">
        ///         <item>
        ///             The type is COMMON in transfer models — a shape consumers actually write, not one that
        ///             merely exists. The list is a maintenance promise per entry, and an entry nothing writes is
        ///             a promise bought for nothing. (<c>System.Half</c> was measured at 2/2 and REJECTED on this
        ///             clause alone in round 29 <c>T0.3c</c>: IEEE binary16 is a hard format contract, but no
        ///             DTO in the corpus, or in this repository, carries one.)
        ///         </item>
        ///         <item>
        ///             Its size and alignment follow from a field set that is part of the type's PUBLIC contract,
        ///             not from an internal arrangement that happens to hold — <c>TimeSpan</c> and
        ///             <c>TimeOnly</c> are their <c>Ticks</c>, <c>DateOnly</c> is its <c>DayNumber</c>,
        ///             <c>Guid</c> is its documented 16 bytes.
        ///         </item>
        ///         <item>
        ///             Both numbers are asserted against the running runtime by
        ///             <c>DwarfMapper.IntegrationTests.BclLayoutFactsTests</c>. A hard-coded table inside a
        ///             generator is only honest if something executes it; that file fails the build the day any
        ///             row here moves, and a row with no runtime assertion is not allowed to exist.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         <b>What is NOT promised is field ORDER</b>, and that is why <c>LayoutKind.Auto</c> costs
        ///         nothing here. Each entry returns a size, an alignment and <see cref="NoOrder" /> — never a
        ///         remedy — so <see cref="WastesAQuarter" /> refuses them on the empty order alone. Both
        ///         <c>DateTime</c> and <c>DateTimeOffset</c> are declared <c>Auto</c>, so the runtime may arrange
        ///         their fields as it likes; the two numbers this table states survive any arrangement of them,
        ///         because alignment is the largest member's and the size is the payload rounded up to it.
        ///         Measured in <c>T0.3c</c>: a <c>Sequential</c> consumer struct holding a <c>DateTimeOffset</c>
        ///         is still laid out in declaration order, so <see cref="LayOut" />'s arithmetic — and the
        ///         reordering remedy built on it — stays correct for the enclosing type.
        ///     </para>
        ///     <para>
        ///         Note <c>Guid</c> is 16 bytes but 4-ALIGNED — it is {int, short, short, 8 bytes}, not a 16-byte
        ///         block — so a caller that assumed size and alignment agree would place every following field
        ///         wrongly.
        ///     </para>
        ///     <para>
        ///         <b>History.</b> <c>T0.3b</c> stopped at four (<c>Guid</c>, <c>DateTime</c>, <c>TimeSpan</c>,
        ///         <c>Decimal</c>) and recorded that <c>DateTimeOffset</c> "would be a defensible fifth". That
        ///         reasoning was right and is not abandoned — <c>T0.3c</c> extended the table by it rather than
        ///         around it, after the representative corpus showed the cascade silencing five of nine transfer
        ///         targets. <c>DateTimeOffset</c>, <c>DateOnly</c> and <c>TimeOnly</c> cleared all three clauses;
        ///         <c>Half</c>, <c>Int128</c> and <c>UInt128</c> were measured and left out on clause 1.
        ///     </para>
        /// </summary>
        private static (int Size, int Align)? FixedLayoutBclSize(ITypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Struct || type.ContainingNamespace is not { Name: "System" } ns ||
                !ns.ContainingNamespace.IsGlobalNamespace)
            {
                return null;
            }

            return type.MetadataName switch
            {
                "Guid" => (16, 4),
                "DateTime" => (8, 8),
                "DateTimeOffset" => (16, 8),
                "TimeSpan" => (8, 8),
                "TimeOnly" => (8, 8),
                "DateOnly" => (4, 4),
                "Decimal" => (16, 8),
                _ => null
            };
        }

        /// <summary>Lays the members out in the order given and returns the struct size that results.</summary>
        private static int SizeOf(IReadOnlyList<(string Name, int Size, int Align)> members, int alignment)
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
