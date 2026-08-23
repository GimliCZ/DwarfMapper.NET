// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     Which text an enum member maps to and from when the other side of the pair is a <see cref="string" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The default, <see cref="Attribute" />, is the useful behaviour: it lets an enum expose a
    ///         serialization name that differs from its identifier — <c>InProgress</c> ↔ <c>"in_progress"</c> —
    ///         with no custom converter anywhere.
    ///     </para>
    ///     <para>
    ///         It is also a hazard when migrating, because <c>[Description]</c> is overwhelmingly a <b>display</b>
    ///         annotation. A codebase that put <c>[Description("Next-Day")]</c> on <c>DispatchChannel.NextDay</c> for a
    ///         combo-box label, and persisted the enum with <c>.ToString()</c>, has a store full of <c>"NextDay"</c>
    ///         — and the first mapping under the default would start writing <c>"Next-Day"</c> into it, breaking
    ///         reads of every existing document. <c>DWARF083</c> reports the divergence; this option is the
    ///         one-line answer to it, instead of a converter per enum.
    ///     </para>
    /// </remarks>
    public enum EnumStringSource
    {
        /// <summary>
        ///     <c>[EnumMember(Value = "…")]</c> wins, then <c>[Description("…")]</c>, then the identifier
        ///     (default).
        /// </summary>
        Attribute = 0,

        /// <summary>
        ///     Always the C# member identifier, exactly as <c>Enum.ToString()</c> and <c>Enum.Parse</c> use it.
        ///     Attributes on the members are ignored for mapping — which is the point: it says the annotations on
        ///     this enum are for display, and the persisted form is the identifier.
        /// </summary>
        Identifier = 1
    }
}
