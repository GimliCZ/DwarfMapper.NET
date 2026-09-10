// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     Assigns the named member's source reference to the destination instead of copying it, for a shape the
    ///     automatic immutability proof cannot see through. Apply to a mapping method.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <strong>You usually do not need this.</strong> When the two sides have the same type and that type
    ///         is provably immutable — an <c>ImmutableArray&lt;T&gt;</c>, an <c>ImmutableList&lt;T&gt;</c>, a
    ///         sealed type whose every instance member is get-only or init-only and whose member types are
    ///         themselves proven — the generator shares the reference on its own, with no attribute and no
    ///         option. A statically decidable optimization should not have to be asked for. This attribute exists
    ///         only for the shapes where the proof runs out.
    ///     </para>
    ///     <para>
    ///         <strong>What is proven, and what is caller-asserted.</strong> The generator refuses what it can
    ///         DISPROVE and accepts your word about what it merely cannot prove:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <em>Proven immutable</em> — shared automatically; the attribute is redundant but harmless.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <em>Unprovable</em> — an interface such as <c>IReadOnlyList&lt;T&gt;</c>, an unsealed
    ///                 class, a type from an assembly whose members the proof cannot follow, or a reference
    ///                 cycle. This attribute shares it <strong>on your assertion</strong>, exactly as
    ///                 <see cref="ReinterpretAttribute" /> forces a block copy the layout proof declines to
    ///                 confirm.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <em>Provably mutable</em> — a settable property, a writable field, an event or an array
    ///                 anywhere in the reachable graph. Refused with <c>DWARF104</c>, in this mode too: no
    ///                 assertion can make a settable member unsettable.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <strong>What you are asserting, stated plainly.</strong> That nothing will write through the
    ///         shared instance after the map. <c>IReadOnlyList&lt;T&gt;</c> is an interface, not a guarantee: a
    ///         <c>List&lt;T&gt;</c> assigned to it is still a <c>List&lt;T&gt;</c> at run time, and if the source
    ///         mutates it afterwards the destination sees the change. That is why the automatic path does not
    ///         accept the interface, and why this attribute is the place where a caller who knows better says so.
    ///         If you are wrong, two object graphs you believe are independent are not, and no diagnostic will
    ///         tell you.
    ///     </para>
    ///     <para>
    ///         The share requires the same type on both sides — it performs no conversion at all — and an
    ///         allocation-free empty value for the destination type, so that a null source still produces the
    ///         empty collection <see cref="NullCollectionStrategy.AsEmpty" /> promises. Both are checked, and a
    ///         member failing either is refused with <c>DWARF104</c> rather than quietly copied.
    ///     </para>
    /// </remarks>
    // AppliesTo is narrowed rather than left at All: the share is decided where a destination MEMBER is
    // assigned, which is the create map and the update-into. A projection is translated by a query provider that
    // reads the member access itself — there is no helper call to remove; a span map and an async stream are
    // element-wise and carry no per-member configuration surface at all.
    [DwarfSurface(SurfaceCategory.ConsumerDirective,
        AppliesTo = SurfaceEndpoints.CreateMap | SurfaceEndpoints.UpdateInto |
                             SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream,
        ProbeKey = "shareable-readonly-member")]
// The default flat DTO pair has no collection member at all, so [MapShare] on it names nothing and the cell
// would measure the instrument rather than the generator -- the same reason [Reinterpret] carries a probe key.
// `Badges` is IReadOnlyList<Badge> on both sides: identical types, an interface the proof deliberately refuses,
// and therefore precisely the shape this attribute exists to force.
    [DwarfSurfaceProbe(1, Arguments = "{Badges}")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class MapShareAttribute : Attribute
    {
        /// <summary>Creates a forced-share directive for the named destination member.</summary>
        /// <param name="member">The destination member whose reference is to be shared rather than copied.</param>
        public MapShareAttribute(string member)
        {
            Member = member;
        }

        /// <summary>Name of the destination member to share.</summary>
        public string Member { get; }
    }
}
