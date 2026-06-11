// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
/// Lightweight context object threaded through recursion-capable auto-synthesized mappers.
/// Carries the <see cref="MaxDepth"/> limit used by depth-guarded private methods and,
/// when preserve mode is active, a reference-identity dictionary for full object-graph
/// reconstruction (Plan 19 C2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Design (depth-safety, C1):</b> depth is tracked as an immutable <c>int depth</c>
/// parameter incremented on each recursive call. No shared mutable state is needed, so there
/// is no finally-pairing bug: siblings at the same level all see the same <c>depth</c> argument
/// and do not accumulate each other's depth into a shared counter.
/// </para>
/// <para>
/// <b>Reference-identity map (C2 — Preserve mode):</b> when <c>preserve: true</c> is passed
/// to the constructor, a <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> keyed
/// by <c>System.Collections.Generic.ReferenceEqualityComparer.Instance</c> (NOT the default
/// comparer — CRITICAL to prevent value-equal records from collapsing into one target instance)
/// is allocated. Only reference-type source objects are tracked.
/// </para>
/// <para>
/// When <c>preserve: false</c> (None mode, the default), the dictionary is never allocated —
/// zero overhead.
/// </para>
/// </remarks>
public sealed class DwarfRefContext
{
    /// <summary>
    /// The absolute hard cap. No <c>MaxDepth</c> value can exceed this, regardless of
    /// the <c>[DwarfMapper(MaxDepth = N)]</c> attribute.
    /// </summary>
    public const int AbsoluteMaxDepth = 1000;

    /// <summary>
    /// The configured maximum recursion depth for this mapper invocation.
    /// Clamped to [1, <see cref="AbsoluteMaxDepth"/>].
    /// </summary>
    public int MaxDepth { get; }

    // ── Identity map for Preserve mode ───────────────────────────────────────────
    // CRITICAL: Must use ReferenceEqualityComparer.Instance, never the default comparer.
    // Using the default comparer would silently merge two distinct-but-value-equal records
    // (e.g. two distinct `record Point(int X, int Y)` instances with the same values)
    // into a single target instance — topology-corrupting, silent, and very hard to debug.
    private System.Collections.Generic.Dictionary<object, object>? _identity;
    private readonly bool _preserve;

    /// <summary>
    /// Initializes a new <see cref="DwarfRefContext"/> with the specified depth limit.
    /// </summary>
    /// <param name="maxDepth">The maximum allowed depth. Clamped to [1, 1000].</param>
    /// <param name="preserve">
    /// When <c>true</c> (Preserve mode), allocates a reference-identity dictionary
    /// for full topology reconstruction. When <c>false</c> (None mode, default),
    /// no dictionary is allocated.
    /// </param>
    public DwarfRefContext(int maxDepth, bool preserve = false)
    {
        // Clamp: min 1 (a mapper that immediately throws is not useful), max AbsoluteMaxDepth
        MaxDepth = maxDepth < 1 ? 1
                 : maxDepth > AbsoluteMaxDepth ? AbsoluteMaxDepth
                 : maxDepth;
        _preserve = preserve;
        // In Preserve mode, allocate the identity map upfront so it is ready on first use.
        // Using a ternary avoids a branch in TryGetReference/SetReference.
        if (preserve)
        {
            _identity = new System.Collections.Generic.Dictionary<object, object>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
        }
    }

    // ── Reference-identity API ────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to retrieve an already-mapped target object for the given source reference.
    /// Returns <c>false</c> in None mode (identity map not active) or if the source has
    /// not been mapped yet.
    /// </summary>
    /// <param name="src">The source object reference. Must not be <c>null</c>.</param>
    /// <param name="existing">When found, the previously registered target object; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if the source was already mapped; <c>false</c> otherwise.</returns>
    public bool TryGetReference(object src, out object existing)
    {
        if (_identity is null)
        {
            existing = null!;
            return false;
        }
        return _identity.TryGetValue(src, out existing!);
    }

    /// <summary>
    /// Registers a (source → target) mapping in the identity map.
    /// Must be called BEFORE populating the target's members to break cycles
    /// (the register-before-populate algorithm).
    /// A no-op in None mode. Null <paramref name="src"/> is never inserted.
    /// </summary>
    /// <param name="src">The source object reference. Must not be <c>null</c>.</param>
    /// <param name="tgt">The newly constructed target object (not yet fully populated).</param>
    public void SetReference(object src, object tgt)
    {
        if (_identity is null || src is null) return;
        _identity[src] = tgt;
    }
}
