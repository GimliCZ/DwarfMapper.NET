// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>An after-map hook invocation: <c>Name(target)</c> or <c>Name(source, target)</c>.</summary>
public sealed record HookCall(
    /// <summary>Name of the hook method to invoke.</summary>
    string Name,
    /// <summary>When <see langword="true"/> the source is passed as the first argument: <c>Name(source, target)</c>.</summary>
    bool TakesSource,
    /// <summary>When <see langword="true"/> the target argument is emitted with <c>ref</c> — required for value-type targets.</summary>
    bool TargetByRef) : System.IEquatable<HookCall>;
