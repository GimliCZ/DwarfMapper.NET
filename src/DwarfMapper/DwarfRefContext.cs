// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
/// Lightweight context object threaded through recursion-capable auto-synthesized mappers.
/// Carries the <see cref="MaxDepth"/> limit used by depth-guarded private methods.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design (C1 — depth-safety only):</b> depth is tracked as an immutable <c>int depth</c>
/// parameter incremented on each recursive call. No shared mutable state is needed, so there
/// is no finally-pairing bug: siblings at the same level all see the same <c>depth</c> argument
/// and do not accumulate each other's depth into a shared counter.
/// </para>
/// <para>
/// <b>C2 extension point:</b> When <c>ReferenceHandling = Preserve</c> is added (Plan 19 C2),
/// an identity dictionary (<c>Dictionary&lt;object,object&gt;</c> with
/// <c>ReferenceEqualityComparer.Instance</c>) will be added to this class. That field is
/// intentionally left absent here so that C1 mappers allocate no dictionary.
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

    /// <summary>
    /// Initializes a new <see cref="DwarfRefContext"/> with the specified depth limit.
    /// </summary>
    /// <param name="maxDepth">The maximum allowed depth. Clamped to [1, 1000].</param>
    public DwarfRefContext(int maxDepth)
    {
        // Clamp: min 1 (a mapper that immediately throws is not useful), max AbsoluteMaxDepth
        MaxDepth = maxDepth < 1 ? 1
                 : maxDepth > AbsoluteMaxDepth ? AbsoluteMaxDepth
                 : maxDepth;
    }

    // ── C2 extension point ───────────────────────────────────────────────────────
    // When ReferenceHandling = Preserve is implemented (Plan 19 C2), add:
    //   private System.Collections.Generic.Dictionary<object, object>? _identity;
    //   public bool TryGet(object src, out object existing) { ... }
    //   public void Set(object src, object tgt) { ... }
    // The dictionary is lazily initialized only under Preserve mode to keep C1 zero-overhead.
    // ─────────────────────────────────────────────────────────────────────────────
}
