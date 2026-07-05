// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Controls which side(s) of a mapping must be fully covered. Set via
///     <see cref="DwarfMapperAttribute.RequiredMapping" />.
/// </summary>
public enum RequiredMappingStrategy
{
    /// <summary>
    ///     (Default) Every <b>destination</b> member must be mapped — an unmapped target is the build error
    ///     <c>DWARF001</c>. Source members that are read by no destination are not flagged.
    /// </summary>
    Target = 0,

    /// <summary>
    ///     In addition to the target gate, every <b>source</b> member must be read by some destination — a
    ///     source member consumed by nothing surfaces the <c>DWARF039</c> suggestion (Info). Suppress a
    ///     specific member with <c>[MapIgnoreSource("Member")]</c>. Escalate to a build error with
    ///     <c>dotnet_diagnostic.DWARF039.severity = error</c> in <c>.editorconfig</c>.
    /// </summary>
    Both = 1
}
