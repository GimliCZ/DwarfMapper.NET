// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>One arm of a derived-type dispatch switch.</summary>
/// <param name="SrcFqn">Fully-qualified source type name (e.g. <c>global::Demo.Dog</c>).</param>
/// <param name="ConverterMethod">Name of the method to call for this arm.</param>
public sealed record DerivedTypeArm(string SrcFqn, string ConverterMethod)
    : System.IEquatable<DerivedTypeArm>;
