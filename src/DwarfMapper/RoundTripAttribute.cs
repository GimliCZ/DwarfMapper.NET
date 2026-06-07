// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a forward mapping method as one half of a round-trip pair. The generator
/// finds the inverse mapping method (swapped source/destination types) and emits a
/// <c>VerifyRoundTrip_&lt;method&gt;(seed, iterations)</c> method that fuzz-verifies
/// <c>backward(forward(x)) ≡ x</c>. Requires a reference to the DwarfMapper.Testing package.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RoundTripAttribute : Attribute
{
}
