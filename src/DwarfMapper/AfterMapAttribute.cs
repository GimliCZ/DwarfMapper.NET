// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Marks a method to run after mapping. Signature: <c>void Hook(TTarget target)</c> or
/// <c>void Hook(TSource source, TTarget target)</c>. Applies to every mapping method whose
/// source/target types are assignable to the parameters.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AfterMapAttribute : Attribute
{
}
