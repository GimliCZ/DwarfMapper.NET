// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     Maps an enum-keyed dictionary member into a fixed-size inline array, indexing it by the key's numeric
    ///     value instead of hashing it. Apply to a mapping method.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <strong>The shape.</strong> The source member is a <c>Dictionary&lt;TEnum,TValue&gt;</c> (or
    ///         anything implementing <c>IEnumerable&lt;KeyValuePair&lt;TEnum,TValue&gt;&gt;</c> — an
    ///         <c>IReadOnlyDictionary</c>, an <c>ImmutableDictionary</c>); the destination member is an
    ///         <c>[InlineArray(n)]</c> struct whose element type is <c>TValue</c>. The generated code is a single
    ///         pass — <c>dst[(int)kv.Key - Offset] = kv.Value</c> — and the destination costs no allocation at
    ///         all, because an inline array lives inside the object that declares it.
    ///     </para>
    ///     <para>
    ///         <strong>What is PROVEN at compile time.</strong> Every member the enum declares lands inside
    ///         <c>[Offset, Offset + n)</c>. The check is arithmetic on the declared constants, in a width that
    ///         cannot wrap, and it covers the LOWER bound as well as the upper — a negative member indexes before
    ///         the array. A member outside the range is <c>DWARF105</c>, an ERROR naming that member and its
    ///         value: nothing is emitted, and there is no fallback that would hide an unprovable shape behind a
    ///         runtime check. Extending the enum later re-runs the proof, so a new member outside the range fails
    ///         the build rather than reaching the index.
    ///     </para>
    ///     <para>
    ///         <strong>What CANNOT be proven, and what is done about it.</strong> A dictionary keyed by an enum
    ///         can hold a key that is not any declared member — <c>(TEnum)999</c> is legal C#. No compile-time
    ///         proof reaches that value, so the emitted loop carries a range check and throws
    ///         <see cref="ArgumentOutOfRangeException" /> naming the key. That check is not politeness: for an
    ///         enum whose underlying type is wider than <c>int</c>, a bare <c>(int)</c> cast of such a key WRAPS
    ///         — <c>(int)(SomeEnum)0x1_0000_0001</c> is <c>1</c> — and the write would land in a slot that
    ///         belongs to a different key with no exception at all. The index is therefore computed in a
    ///         non-wrapping width and range-checked before it is used.
    ///     </para>
    ///     <para>
    ///         <strong><c>[Flags]</c> enums are refused</strong> (<c>DWARF105</c>). A flags enum's key space is
    ///         the power set of its members: <c>Read | Write</c> is a legitimate dictionary key that no member
    ///         declares, and it would land in whatever slot its numeric value happens to name. Dense indexing has
    ///         no honest answer for that shape, so it is refused at compile time rather than accepted with a
    ///         footnote.
    ///     </para>
    ///     <para>
    ///         <strong>Aliases are a non-case.</strong> Two names for one value (<c>None = 0, Default = 0</c>) are
    ///         one dictionary key and one slot; the proof judges values, not names, so an alias neither
    ///         duplicates a slot nor widens the range.
    ///     </para>
    /// </remarks>
    // AppliesTo stays at its default (All), and that is a claim rather than an omission: the directive is
    // HONOURED at the create map and the update-into — the two endpoints that resolve destination members one
    // at a time — and REFUSED LOUDLY at the other three with DWARF092. A projection is translated into an
    // expression tree, which has no statement for a loop to live in; a span map and an async stream map each
    // element through a mapper for the element pair and read no per-member directive at all. A narrowed claim
    // would tell the surface matrix to skip three cells that are live, which is what [MapCollectionKey] and
    // [FlattenGraph] — the other two directives DWARF092 speaks for — already leave at All.
    [DwarfSurface(SurfaceCategory.ConsumerDirective,
        ProbeKey = "dense-enum-keyed-member",
        Security = SecuritySurface.MemorySafety)]
// The default flat DTO pair has no dictionary member and no inline array, so the sampled argument would name
// nothing and the cell would measure the instrument rather than the generator -- the same reason [Reinterpret]
// and [MapShare] carry probe keys. Why that fixture declares exactly ONE dense-able member, and what the
// multiplicity axis therefore renders, is stated where the fixture is.
    [DwarfSurfaceProbe(1, Arguments = "{Counts}")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class MapDenseEnumKeysAttribute : Attribute
    {
        /// <summary>Creates a dense-enum-key directive for the named destination member.</summary>
        /// <param name="member">
        ///     The destination inline-array member the source dictionary is written into.
        /// </param>
        public MapDenseEnumKeysAttribute(string member)
        {
            Member = member;
        }

        /// <summary>Name of the destination inline-array member.</summary>
        public string Member { get; }

        /// <summary>
        ///     The enum value that maps to slot 0. Defaults to <c>0</c>.
        ///     <para>
        ///         An enum whose members start at 1 — the common shape, where 0 is reserved for "unset" — wastes
        ///         a slot at offset 0 and needs one more than it has members. <c>Offset = 1</c> makes the first
        ///         declared member slot 0. The proof is run against the offset actually in force, so an offset
        ///         that pushes a member out of either end of the array is <c>DWARF105</c>.
        ///     </para>
        /// </summary>
        public int Offset { get; set; }
    }
}
