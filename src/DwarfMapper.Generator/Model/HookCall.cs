// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Model
{
    /// <summary>An after-map hook invocation: <c>Name(target)</c> or <c>Name(source, target)</c>.</summary>
    /// <param name="Name">Name of the hook method to invoke.</param>
    /// <param name="TakesSource">When <see langword="true"/> the source is passed as the first argument: <c>Name(source, target)</c>.</param>
    /// <param name="TargetByRef">When <see langword="true"/> the target argument is emitted with <c>ref</c> — required for value-type targets.</param>
    public sealed record HookCall(
        string Name,
        bool TakesSource,
        bool TargetByRef) : IEquatable<HookCall>
    {
        /// <summary><see cref="Name" /> as it must be written into emitted C#.</summary>
        /// <remarks>
        ///     An <c>[AfterMap]</c> hook is a method the CONSUMER declared and named, so it may legally be
        ///     <c>@class</c>; <c>ISymbol.Name</c> hands back <c>class</c> and the generated body calls it.
        /// </remarks>
        public string EmitName => Identifiers.Escape(Name);
    }
}
