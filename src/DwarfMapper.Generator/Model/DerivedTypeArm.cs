// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Model
{
    /// <summary>One arm of a derived-type dispatch switch.</summary>
    /// <param name="SrcFqn">Fully-qualified source type name (e.g. <c>global::Demo.Dog</c>).</param>
    /// <param name="ConverterMethod">Name of the method to call for this arm.</param>
    /// <param name="ConverterNeedsDepthCtx">
    ///     When <c>true</c>, <see cref="ConverterMethod"/> is a recursion-capable synthesized method
    ///     that requires <c>(ctx, depth + 1)</c> extra arguments at call sites (e.g. under
    ///     <c>ReferenceHandling = Preserve</c>).  Mirrors <see cref="MemberMap.ConverterNeedsDepthCtx"/>.
    /// </param>
    /// <param name="ConverterParamTypeFqn">
    ///     The parameter type of the declared overload <see cref="ConverterMethod" /> was adopted from, or
    ///     <see langword="null" />. Never emitted; the recursion-cycle phase's edge disambiguator. Mirrors
    ///     <see cref="MemberMap.ConverterParamTypeFqn" />.
    /// </param>
    /// <param name="PassThroughCastFqn">
    ///     A union dispatch's PASS-THROUGH arm only (<see cref="IsPassThrough" />): the target case type the matched
    ///     value is converted to before the union conversion takes it, when it is not that type already (an <c>int</c>
    ///     case into a <c>long</c> case, an <c>int?</c> case's pattern <c>int</c> into the <c>int?</c> case). Round 35 T23.
    /// </param>
    public sealed record DerivedTypeArm(
        string SrcFqn,
        string ConverterMethod,
        bool ConverterNeedsDepthCtx = false,
        string? ConverterParamTypeFqn = null,
        string? PassThroughCastFqn = null)
        : IEquatable<DerivedTypeArm>
    {
        /// <summary>
        ///     True for an arm that calls no converter: a union case whose value is already (or converts plainly to) one of
        ///     the target union's case types, so the arm's value is the matched value itself (round 35 T23). A
        ///     <c>[MapDerivedType]</c> arm over a class hierarchy always has a converter.
        /// </summary>
        public bool IsPassThrough => ConverterMethod.Length == 0;

        /// <summary><see cref="ConverterMethod" /> as it must be written into emitted C#.</summary>
        /// <remarks>
        ///     The arm's converter may be a user-declared map method, so the consumer chose the name. The raw
        ///     field stays the call-graph edge label, exactly as <see cref="MemberMap.ConverterMethod" /> does.
        /// </remarks>
        public string EmitConverterMethod => Identifiers.Escape(ConverterMethod);
    }
}
