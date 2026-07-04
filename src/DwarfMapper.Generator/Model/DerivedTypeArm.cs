// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>One arm of a derived-type dispatch switch.</summary>
public sealed record DerivedTypeArm(
    /// <summary>Fully-qualified source type name (e.g. <c>global::Demo.Dog</c>).</summary>
    string SrcFqn,
    /// <summary>Name of the method to call for this arm.</summary>
    string ConverterMethod,
    /// <summary>
    /// When <c>true</c>, <see cref="ConverterMethod"/> is a recursion-capable synthesized method
    /// that requires <c>(ctx, depth + 1)</c> extra arguments at call sites (e.g. under
    /// <c>ReferenceHandling = Preserve</c>).  Mirrors <see cref="MemberMap.ConverterNeedsDepthCtx"/>.
    /// </summary>
    bool ConverterNeedsDepthCtx = false)
    : System.IEquatable<DerivedTypeArm>;
