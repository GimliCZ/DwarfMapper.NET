// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Model
{
    /// <summary>An after-map hook invocation: <c>Name(target)</c> or <c>Name(source, target)</c>.</summary>
    /// <param name="Name">Name of the hook method to invoke.</param>
    /// <param name="TakesSource">When <see langword="true"/> the source is passed as the first argument: <c>Name(source, target)</c>.</param>
    /// <param name="TargetByRef">When <see langword="true"/> the target argument is emitted with <c>ref</c> — required for value-type targets.</param>
    public sealed record HookCall(
        string Name,
        bool TakesSource,
        bool TargetByRef) : IEquatable<HookCall>;
}
