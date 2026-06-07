// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a method to run before mapping. Signature: <c>void Hook(TSource source)</c>.
/// Applies to every mapping method whose source type is assignable to the parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BeforeMapAttribute : Attribute
{
}
