// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     States that this mapping method's source and destination are the SAME type on purpose: the method is a
    ///     clone, and <c>DWARF076</c> ("source and target are the same type") is not reported for it. The generated
    ///     method is exactly the one it would be without the attribute - a member-by-member copy, in which a nested
    ///     object is the SAME instance in both (it is not cloned) while a collection or array member is copied into a
    ///     new one holding the same elements.
    ///     <code>
    /// [DwarfMapper]
    /// public partial class OrderMapper
    /// {
    ///     [MapClone] public partial Order Copy(Order source);
    /// }
    /// </code>
    ///     <para>
    ///         It accepts the one method it is written on; <c>[SuppressMessage("DwarfMapper", "DWARF076")]</c> on the
    ///         mapper class accepts every same-type method of the class at once. Written on a method that is not a
    ///         same-type create map - its types differ, or it maps into an existing instance, a span, a stream or a
    ///         query, none of which <c>DWARF076</c> ever judges - it accepts nothing and is itself reported
    ///         (<c>DWARF116</c>).
    ///     </para>
    /// </summary>
    // AppliesTo is left at every endpoint because the statement is READ at every endpoint that has a method: honoured
    // at the create map, the one endpoint DWARF076 judges, and refused as DWARF116 at update-into, span, async-stream
    // and projection, where it has nothing to accept. The default flat pair maps two DIFFERENT types, where the
    // statement is dead by design, so it is measured on a self-map.
    [DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "self-map")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class MapCloneAttribute : Attribute
    {
    }
}
