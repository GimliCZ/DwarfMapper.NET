// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
/// Lightweight context object threaded through recursion-capable auto-synthesized mappers.
/// Carries the <see cref="MaxDepth"/> limit used by depth-guarded private methods and,
/// when preserve mode is active, a reference-identity dictionary for full object-graph
/// reconstruction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design (depth-safety):</b> depth is tracked as an immutable <c>int depth</c>
/// parameter incremented on each recursive call. No shared mutable state is needed, so there
/// is no finally-pairing bug: siblings at the same level all see the same <c>depth</c> argument
/// and do not accumulate each other's depth into a shared counter.
/// </para>
/// <para>
/// <b>Reference-identity map (Preserve mode):</b> when <c>preserve: true</c> is passed
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

    // ── On-stack guard for OnCycle = SetNull (None mode) ─────────────────────────
    // Tracks the source objects currently being populated on the active mapping stack.
    // A node is added on the way down (TryEnterNode) and removed on the way up (ExitNode,
    // in a finally). When a member points back to a node already on the stack, the mapper
    // returns null instead of recursing → the re-entrant back-edge is broken with null,
    // matching System.Text.Json ReferenceHandler.IgnoreCycles.
    //
    // CRITICAL: same as the identity map, this MUST use ReferenceEqualityComparer.Instance —
    // value-equal records are distinct nodes and must NOT collide on the stack.
    //
    // This is a stack-scoped set (not a persistent identity map): a shared but acyclic node
    // (a diamond) leaves the stack before its second parent is visited, so it is mapped again
    // (duplicated) rather than nulled — only true ancestor cycles are broken.
    private System.Collections.Generic.HashSet<object>? _onStack;

    /// <summary>
    /// Initializes a new <see cref="DwarfRefContext"/> with the specified depth limit.
    /// </summary>
    /// <param name="maxDepth">The maximum allowed depth. Clamped to [1, 1000].</param>
    /// <param name="preserve">
    /// When <c>true</c> (Preserve mode), allocates a reference-identity dictionary
    /// for full topology reconstruction. When <c>false</c> (None mode, default),
    /// no dictionary is allocated.
    /// </param>
    /// <param name="setNull">
    /// When <c>true</c> (None mode with <see cref="OnCycleStrategy.SetNull"/>), allocates the
    /// on-stack guard set used to break reference cycles by nulling the back-edge. Mutually
    /// exclusive with <paramref name="preserve"/> in practice (Preserve ignores OnCycle).
    /// </param>
    public DwarfRefContext(int maxDepth, bool preserve = false, bool setNull = false)
    {
        // Clamp: min 1 (a mapper that immediately throws is not useful), max AbsoluteMaxDepth
        MaxDepth = maxDepth < 1 ? 1
                 : maxDepth > AbsoluteMaxDepth ? AbsoluteMaxDepth
                 : maxDepth;
        _preserve = preserve;
        // In Preserve mode, allocate the identity map upfront so it is ready on first use.
        // Allocating eagerly here lets TryGetReference/SetReference do a single null check
        // instead of a lazy-init branch.
        if (preserve)
        {
            _identity = new System.Collections.Generic.Dictionary<object, object>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
        }
        // In SetNull mode, allocate the on-stack guard. Preserve takes precedence (cycles are
        // reconstructed, so OnCycle is ignored) — never allocate both.
        else if (setNull)
        {
            _onStack = new System.Collections.Generic.HashSet<object>(
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

    // ── On-stack guard API (OnCycle = SetNull) ────────────────────────────────────

    /// <summary>
    /// Marks <paramref name="src"/> as being populated on the active mapping stack.
    /// Returns <c>false</c> if it is already on the stack — that is a re-entrant back-edge
    /// (a cycle), and the caller must break it by returning <c>null</c> WITHOUT pushing or
    /// later popping. Returns <c>true</c> when the node was freshly pushed; the caller must
    /// pair every <c>true</c> with exactly one <see cref="ExitNode"/> (use a <c>finally</c>).
    /// A no-op returning <c>true</c> in non-SetNull modes (the guard set is not allocated),
    /// so it never changes behaviour outside SetNull.
    /// </summary>
    /// <param name="src">The source object about to be mapped. Must not be <c>null</c>
    /// (the caller's null-guard runs first).</param>
    /// <returns><c>true</c> if freshly entered (proceed + pair with <see cref="ExitNode"/>);
    /// <c>false</c> if already on the stack (break the cycle with <c>null</c>).</returns>
    public bool TryEnterNode(object src)
    {
        if (_onStack is null) return true;
        // HashSet.Add returns false if the element was already present → cycle detected.
        return _onStack.Add(src);
    }

    /// <summary>
    /// Removes <paramref name="src"/> from the active mapping stack. Must be called in a
    /// <c>finally</c> paired with a <see cref="TryEnterNode"/> that returned <c>true</c>, so
    /// the node leaves the stack even if mapping throws. A no-op in non-SetNull modes.
    /// </summary>
    /// <param name="src">The source object whose mapping has completed.</param>
    public void ExitNode(object src)
    {
        _onStack?.Remove(src);
    }
}
