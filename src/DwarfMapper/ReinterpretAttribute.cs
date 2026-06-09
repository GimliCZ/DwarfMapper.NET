// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Forces a reinterpret blit (vectorized memmove) for an unmanaged array→array member, even when the
/// automatic layout proof is too conservative to confirm it. Apply to a mapping method.
/// </summary>
/// <remarks>
/// <para><strong>What the forced path verifies (and what it does not).</strong></para>
/// <para>
/// On the forced (<c>[Reinterpret]</c>) path the generator verifies exactly two things:
/// (1) both source and destination element types are <c>unmanaged</c> (no managed references), and
/// (2) at runtime, a size guard checks <c>sizeof(TSrc) == sizeof(TDst)</c> and throws if they differ,
/// so a size mismatch produces an exception rather than a silent memory corruption.
/// </para>
/// <para>
/// That is the full extent of the machine-checked guarantee.
/// Field-by-field correspondence — names, declaration order, packing (<c>[StructLayout(Pack=…)]</c>),
/// and offset alignment — is <strong>entirely caller-asserted</strong>. The generator performs no
/// name check, no layout check, and no <c>Pack</c>/field-order check on the forced path (unlike the
/// automatic blit path, which proves layout compatibility before emitting a bulk copy).
/// </para>
/// <para>
/// Use this attribute only when you know positionally that the two types are layout-equivalent and
/// the automatic proof is too conservative to confirm it (e.g. types from referenced assemblies, or
/// differing field names over an identical memory layout). If you are wrong, the size guard prevents
/// memory corruption, but the copied bytes will be misinterpreted.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ReinterpretAttribute : Attribute
{
    /// <summary>Creates a forced-blit directive for the named destination member.</summary>
    /// <param name="member">The array member to blit.</param>
    public ReinterpretAttribute(string member) => Member = member;

    /// <summary>Name of the destination array member to blit.</summary>
    public string Member { get; }
}
