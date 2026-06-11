// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Memoized find-or-build registry for auto-synthesized nested object mapper methods.
/// Keyed by (srcFqn, tgtFqn) string pairs — no ISymbol stored (value-equatable safe).
///
/// The register-before-build contract guarantees the generator never infinite-loops on
/// recursive types: the key is inserted BEFORE the body is built, so a re-entrant pair
/// hits the "already registered" path and returns the reserved method name immediately.
///
/// <para>
/// <b>Recursion-capability analysis (Plan 19 C1):</b> while draining the build queue,
/// the registry tracks a directed graph of which synthesized method calls which others.
/// After the drain, <see cref="IsRecursionCapable"/> identifies methods on a cycle in
/// that graph — only those get the depth-guarded signature.
/// </para>
/// </summary>
internal sealed class NestedMappingRegistry
{
    private const int MaxPairs = 512;

    private readonly Dictionary<(string, string), string> _reserved =
        new Dictionary<(string, string), string>();

    private readonly Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name)> _buildQueue =
        new Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name)>();

    // ── Recursion-capability analysis ────────────────────────────────────────────
    // Directed graph: _edges[method] = set of methods that 'method' calls.
    // Built during the drain loop (via SetCurrentPair + GetOrReserve).
    private readonly Dictionary<string, HashSet<string>> _edges =
        new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);

    // The method name currently being body-resolved (set by SetCurrentPair).
    private string? _currentPair;

    // Cached result; null until ComputeRecursionCapability() is called.
    private HashSet<string>? _recursionCapable;

    /// <summary>
    /// Whether the depth cap was exceeded. When true a DWARF031 was already scheduled.
    /// </summary>
    public bool CapExceeded { get; private set; }

    /// <summary>
    /// The fully-qualified name of the first type that triggered the cap, for diagnostics.
    /// </summary>
    public string CapTriggerType { get; private set; } = "";

    /// <summary>
    /// Returns true when there are pending (src, tgt) pairs whose bodies need to be built.
    /// </summary>
    public bool HasPending => _buildQueue.Count > 0;

    /// <summary>
    /// Dequeues the next pending (src, tgt, methodName) to build.
    /// </summary>
    public (ITypeSymbol Src, INamedTypeSymbol Tgt, string Name) Dequeue()
        => _buildQueue.Dequeue();

    /// <summary>
    /// Informs the registry that the body of <paramref name="methodName"/> is now being resolved.
    /// Any subsequent <see cref="GetOrReserve"/> call (from member resolution of this pair)
    /// will be recorded as a dependency edge.
    /// </summary>
    public void SetCurrentPair(string methodName)
    {
        _currentPair = methodName;
        // Ensure a node exists for this method even if it calls nothing.
        if (!_edges.ContainsKey(methodName))
            _edges[methodName] = new HashSet<string>(System.StringComparer.Ordinal);
    }

    /// <summary>
    /// Clears the current-pair context (call after body resolution is complete).
    /// </summary>
    public void ClearCurrentPair() => _currentPair = null;

    /// <summary>
    /// If a method for <paramref name="src"/>→<paramref name="tgt"/> is already registered,
    /// returns its method name without enqueuing a new build. Otherwise reserves a unique
    /// FNV-1a-hashed name, enqueues the pair for body-building, and returns the name.
    ///
    /// <para>
    /// CRITICAL: the key is recorded BEFORE the body is built, so recursive types
    /// (e.g. <c>Tree { List&lt;Tree&gt; Children }</c>) hit the "already registered" branch
    /// on the second encounter and return the reserved name — terminating the generator-time
    /// recursion without needing a separate depth counter.
    /// </para>
    /// </summary>
    public string? GetOrReserve(ITypeSymbol src, INamedTypeSymbol tgt, LocationInfo? location)
    {
        var srcFqn = src.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
        var tgtFqn = tgt.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
        var key = (srcFqn, tgtFqn);

        string methodName;

        if (_reserved.TryGetValue(key, out var existing))
        {
            methodName = existing;
            // Record the dependency edge: current pair → this pair (even if already registered).
            RecordEdge(methodName);
            return existing;
        }

        if (_reserved.Count >= MaxPairs)
        {
            CapExceeded = true;
            if (string.IsNullOrEmpty(CapTriggerType))
            {
                CapTriggerType = $"{srcFqn} → {tgtFqn}";
            }
            return null;
        }

        methodName = BuildMethodName(srcFqn, tgtFqn);
        _reserved[key] = methodName;
        _buildQueue.Enqueue((src, tgt, methodName));

        // Record the dependency edge: current pair → new pair.
        RecordEdge(methodName);

        return methodName;
    }

    private void RecordEdge(string calleeName)
    {
        if (_currentPair is null) return;
        if (!_edges.TryGetValue(_currentPair, out var deps))
        {
            deps = new HashSet<string>(System.StringComparer.Ordinal);
            _edges[_currentPair] = deps;
        }
        deps.Add(calleeName);
    }

    // ── Recursion-capability analysis ────────────────────────────────────────────

    /// <summary>
    /// Computes which synthesized methods are "recursion-capable" — i.e. the method can
    /// transitively call itself (directly or indirectly). Must be called AFTER the build
    /// queue is fully drained.
    /// </summary>
    /// <remarks>
    /// Uses a simple DFS reachability check: method M is recursion-capable if M can
    /// reach M in the directed call graph. This is equivalent to M being on a cycle.
    /// </remarks>
    public void ComputeRecursionCapability()
    {
        _recursionCapable = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var start in _edges.Keys)
        {
            if (CanReachSelf(start))
                _recursionCapable.Add(start);
        }

        // Also include any method that was force-marked as recursion-capable
        // (e.g. object mappers that serve as element converters in Preserve collections).
        foreach (var forced in _forcedRecursionCapable)
            _recursionCapable.Add(forced);
    }

    /// <summary>
    /// Returns true when the given synthesized method name is recursion-capable.
    /// <see cref="ComputeRecursionCapability"/> must be called first.
    /// </summary>
    public bool IsRecursionCapable(string methodName)
        => _recursionCapable?.Contains(methodName) == true;

    // ── Force-mark as recursion-capable ─────────────────────────────────────────
    // Used by Preserve-mode collection helpers: when an object mapper is used as an element
    // converter inside a Preserve collection, it MUST get the (ctx, depth) signature so the
    // collection helper can thread ctx to it. We force-mark it before ComputeRecursionCapability
    // so that the post-analysis patching includes it.
    private readonly HashSet<string> _forcedRecursionCapable =
        new HashSet<string>(System.StringComparer.Ordinal);

    /// <summary>
    /// Marks <paramref name="methodName"/> as recursion-capable unconditionally.
    /// Call before <see cref="ComputeRecursionCapability"/> for the flag to take effect.
    /// Used by Preserve-mode collection helpers that thread ctx to object-mapper elements.
    /// </summary>
    public void ForceRecursionCapable(string methodName)
        => _forcedRecursionCapable.Add(methodName);

    private bool CanReachSelf(string start)
    {
        // DFS: can we return to 'start' following outgoing edges?
        var visited = new HashSet<string>(System.StringComparer.Ordinal);
        var stack = new Stack<string>();

        if (!_edges.TryGetValue(start, out var startDeps)) return false;
        foreach (var dep in startDeps)
            stack.Push(dep);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == start) return true;
            if (!visited.Add(current)) continue;
            if (_edges.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                    stack.Push(dep);
            }
        }

        return false;
    }

    // ── Name synthesis ────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a deterministic, collision-resistant private method name for the given pair.
    /// Format: __DwarfMap_Obj_{SanitizedSrc}_{SanitizedTgt}_{Fnv1aHash}
    /// </summary>
    private static string BuildMethodName(string srcFqn, string tgtFqn)
    {
        var srcSan = Sanitize(srcFqn);
        var tgtSan = Sanitize(tgtFqn);
        var hash = Fnv1a32(srcFqn + "\x00" + tgtFqn);
        return $"__DwarfMap_Obj_{srcSan}_{tgtSan}_{hash:X8}";
    }

    /// <summary>Strips non-identifier characters, keeping only letters, digits, underscores.</summary>
    private static string Sanitize(string fqn)
    {
        var sb = new System.Text.StringBuilder(fqn.Length);
        foreach (var c in fqn)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c == '.' || c == ':' || c == '<' || c == '>' || c == ',')
                sb.Append('_');
        }
        // Cap length so generated identifiers stay manageable
        const int max = 48;
        if (sb.Length > max)
        {
            sb.Length = max;
        }
        return sb.ToString();
    }

    /// <summary>FNV-1a 32-bit hash — fast, deterministic, good dispersion for type names.</summary>
    private static uint Fnv1a32(string s)
    {
        const uint Offset = 2166136261u;
        const uint Prime = 16777619u;
        var hash = Offset;
        foreach (var c in s)
        {
            hash ^= (byte)(c & 0xFF);
            hash *= Prime;
            hash ^= (byte)(c >> 8);
            hash *= Prime;
        }
        return hash;
    }
}
