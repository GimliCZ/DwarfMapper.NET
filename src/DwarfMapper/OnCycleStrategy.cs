// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
/// Controls how a reference cycle in the source graph is handled when
/// <see cref="ReferenceHandlingStrategy.None"/> is active (the default reference mode).
/// </summary>
/// <remarks>
/// This knob is the <em>degraded</em> companion to <see cref="ReferenceHandlingStrategy"/>:
/// it decides what happens when the data (not the type graph) contains a cycle while no
/// identity map is being kept. It is <b>ignored</b> under
/// <see cref="ReferenceHandlingStrategy.Preserve"/>, where cycles are reconstructed faithfully
/// (a <c>DWARF037</c> diagnostic is reported if both are configured together, since the
/// <c>OnCycle</c> setting then has no effect).
/// </remarks>
public enum OnCycleStrategy
{
    /// <summary>
    /// Default. Recursion-capable pairs carry a depth counter; a cycle (or an over-deep
    /// acyclic chain) throws a loud, catchable <see cref="DwarfMappingDepthException"/> at
    /// <c>MaxDepth</c> rather than a silent <see cref="System.StackOverflowException"/>.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// Breaks cycles by nulling the re-entrant back-edge (equivalent to
    /// <c>System.Text.Json</c> <c>ReferenceHandler.IgnoreCycles</c>). A node already on the
    /// active mapping stack is not mapped again; the member that points back to it is set to
    /// <c>null</c>, yielding a finite, acyclic projection of a cyclic source. Useful for
    /// display/DTO shapes where the back-reference is not needed. Only reference-type nodes are
    /// tracked (value types are copied by value and cannot form a reference cycle). Shared but
    /// acyclic nodes (diamonds) are still mapped each time they are reached — only true
    /// back-edges to an ancestor on the current stack are nulled.
    /// </summary>
    SetNull = 1,
}
