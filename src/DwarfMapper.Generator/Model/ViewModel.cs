// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;

namespace DwarfMapper.Generator.Model
{
    /// <summary>
    ///     One property of a <see cref="ViewModel" />: the create map's resolution for one destination member,
    ///     re-expressed as an expression that reads the source on every access instead of being written into a
    ///     freshly constructed object.
    /// </summary>
    /// <param name="Name">
    ///     The property name as it must be written into emitted C# (already escaped — <c>class</c> becomes
    ///     <c>@class</c>).
    /// </param>
    /// <param name="TypeFullName">
    ///     The property's declared type, <c>global::</c>-rooted and carrying the destination member's own
    ///     nullable annotation. Rendered with a nullable-aware display format on purpose: dropping the <c>?</c>
    ///     puts a CS8603 inside a <c>.g.cs</c> the consumer cannot edit, which is the unsuppressible-warning
    ///     class this repository has already paid for once (F24-F26).
    /// </param>
    /// <param name="Value">
    ///     For a VALUE member, the resolved mapping whose expression the property returns. Its
    ///     <see cref="MemberMap.ConverterMethod" />, <see cref="MemberMap.ValueExpression" /> and
    ///     <see cref="MemberMap.WhenPredicate" /> are already owner-qualified (<c>_m.</c>) where they name an
    ///     INSTANCE member of the mapper: a nested type may reach its enclosing type's private members, but an
    ///     instance one still needs an instance (CS0120), and a static one must NOT be reached through one
    ///     (CS0176). Null for a nested-view member.
    /// </param>
    /// <param name="NestedViewTypeName">
    ///     For a NESTED member, the name of the view type this property returns. Null for a value member.
    /// </param>
    /// <param name="NestedSourceMember">
    ///     For a nested member, the source member (already escaped) whose value the nested view reads.
    /// </param>
    /// <param name="NestedSourceIsNullable">
    ///     For a nested member, whether the source member may be null — in which case the property yields
    ///     <c>default</c>, a view whose <c>HasValue</c> is <c>false</c>, rather than constructing over null.
    /// </param>
    /// <param name="NestedViewNeedsOwner">
    ///     For a nested member, whether the view type this property constructs carries an owner field — in
    ///     which case the mapper reference must be passed to it. Independent of the ENCLOSING view's own
    ///     <see cref="ViewModel.NeedsOwner" />: a parent over identity members alone still has to hand the
    ///     mapper to a child that converts one, so the flag is resolved by a fixed point over the whole view
    ///     set rather than read off the member being emitted. Getting it wrong is CS7036 inside a
    ///     <c>.g.cs</c> the consumer cannot edit.
    /// </param>
    public sealed record ViewMemberModel(
        string Name,
        string TypeFullName,
        MemberMap? Value = null,
        string? NestedViewTypeName = null,
        string? NestedSourceMember = null,
        bool NestedSourceIsNullable = false,
        bool NestedViewNeedsOwner = false) : IEquatable<ViewMemberModel>;

    /// <summary>
    ///     One <c>readonly ref struct</c> view to emit inside the mapper class: the create map's member
    ///     resolution for a <c>(source, target)</c> pair, evaluated lazily against a source instance instead of
    ///     being copied into a new object.
    /// </summary>
    /// <remarks>
    ///     Nested views are SIBLINGS in <c>MapperClassModel.Views</c> rather than children of the view that
    ///     refers to them: one nested pair may be reached from several views, and emitting it once per reference
    ///     would be CS0102 on the second. The flat list is deduplicated by <see cref="ViewTypeName" />, which is
    ///     also what makes a name collision between two DECLARED views detectable rather than a compiler error
    ///     inside a file the consumer cannot edit.
    /// </remarks>
    /// <param name="ViewTypeName">The nested type's name — <c>[GenerateView(Name = …)]</c>, else target + <c>View</c>.</param>
    /// <param name="SourceTypeFullName">The source type the view reads, <c>global::</c>-rooted.</param>
    /// <param name="Members">One property per resolved destination member.</param>
    /// <param name="NeedsOwner">
    ///     Whether any member expression names an INSTANCE member of the mapper, so the view must carry a
    ///     reference to it — or whether any NESTED view it constructs does, since the owner can only reach that
    ///     child through its parent. Emitted only when needed: a view over identity members alone, reaching no
    ///     nested view that needs one either, holds nothing but the source.
    /// </param>
    /// <param name="EmitFactory">
    ///     Whether the mapper gets a <c>public &lt;View&gt; View(TSource)</c> factory for this view. True for a
    ///     view a <c>[GenerateView]</c> DECLARED; false for one reached only as a nested member, which is
    ///     constructed by its parent and has no independent entry point.
    /// </param>
    public sealed record ViewModel(
        string ViewTypeName,
        string SourceTypeFullName,
        EquatableArray<ViewMemberModel> Members,
        bool NeedsOwner,
        bool EmitFactory) : IEquatable<ViewModel>;
}
