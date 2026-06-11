// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
/// Controls how shared object references and cycles in the source graph are handled
/// when mapping with an auto-synthesized nested mapper.
/// </summary>
public enum ReferenceHandlingStrategy
{
    /// <summary>
    /// Default: no identity tracking. Recursion-capable pairs still get a depth counter
    /// (<see cref="DwarfMappingDepthException"/> at <c>MaxDepth</c>) — cyclic data throws
    /// a loud, catchable exception rather than a silent <see cref="System.StackOverflowException"/>.
    /// Acyclic graphs within the depth limit map correctly. Zero allocation overhead.
    /// </summary>
    None = 0,

    /// <summary>
    /// Full topology reconstruction. An identity dictionary (keyed by reference identity via
    /// <c>ReferenceEqualityComparer.Instance</c>) is threaded through all recursion-capable
    /// synthesized methods. Every distinct source object is mapped exactly once; shared nodes
    /// (diamonds), back-edges (cycles), and arbitrary reference topologies are reconstructed
    /// isomorphically. One small dictionary allocated per top-level <c>Map</c> call.
    /// </summary>
    Preserve = 1,
}
