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
    public sealed record DerivedTypeArm(
        string SrcFqn,
        string ConverterMethod,
        bool ConverterNeedsDepthCtx = false,
        string? ConverterParamTypeFqn = null)
        : IEquatable<DerivedTypeArm>
    {
        /// <summary><see cref="ConverterMethod" /> as it must be written into emitted C#.</summary>
        /// <remarks>
        ///     The arm's converter may be a user-declared map method, so the consumer chose the name. The raw
        ///     field stays the call-graph edge label, exactly as <see cref="MemberMap.ConverterMethod" /> does.
        /// </remarks>
        public string EmitConverterMethod => Identifiers.Escape(ConverterMethod);
    }
}
