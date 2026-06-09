// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>An after-map hook invocation: <c>Name(target)</c> or <c>Name(source, target)</c>.</summary>
/// <param name="TargetByRef">When <see langword="true"/> the target argument is emitted with <c>ref</c> — required for value-type targets.</param>
public sealed record HookCall(string Name, bool TakesSource, bool TargetByRef) : System.IEquatable<HookCall>;
