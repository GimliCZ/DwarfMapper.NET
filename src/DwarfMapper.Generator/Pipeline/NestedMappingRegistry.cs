// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
///     Memoized find-or-build registry for auto-synthesized nested object mapper methods.
///     Keyed by (srcFqn, tgtFqn) string pairs — no ISymbol stored (value-equatable safe).
///     The register-before-build contract guarantees the generator never infinite-loops on
///     recursive types: the key is inserted BEFORE the body is built, so a re-entrant pair
///     hits the "already registered" path and returns the reserved method name immediately.
///     <para>
///         <b>Recursion-capability analysis (Plan 19 C1):</b> while draining the build queue,
///         the registry tracks a directed graph of which synthesized method calls which others.
///         After the drain, <see cref="IsRecursionCapable" /> identifies methods on a cycle in
///         that graph — only those get the depth-guarded signature.
///     </para>
/// </summary>
internal sealed class NestedMappingRegistry
{
    private const int MaxPairs = 512;

    // C1: per-pair autoNest value that triggered the enqueue.
    private readonly Dictionary<string, bool> _autoNestByName = new(StringComparer.Ordinal);

    private readonly Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, bool AutoNest)> _buildQueue = new();

    // ── None-mode collection/dict ctx-upgrade candidates ────────────────────────
    // A None-mode collection/dict helper whose element/key/value resolves to a PUBLIC declared
    // method (e.g. a self-map `Map`) is synthesized to call that public entry directly — which
    // allocates a fresh DwarfRefContext per element, resetting the depth guard and StackOverflowing
    // on a deep/cyclic graph routed through the collection edge. We record a re-synthesis closure;
    // after recursion-capability is finalised, the post-pass upgrades only those helpers whose
    // element method turned out self-recursive (companion exists), keeping non-recursive collections
    // zero-overhead. Closures capture ITypeSymbols — safe because the registry is Extract-scoped.
    private readonly List<(string HelperName, string[] ElemMethods, Action<Func<string, string>> ReSynth)>
        _ctxUpgradeCandidates =
            new();

    // ── Recursion-capability analysis ────────────────────────────────────────────
    // Directed graph: _edges[method] = set of methods that 'method' calls.
    // Built during the drain loop (via SetCurrentPair + GetOrReserve).
    private readonly Dictionary<string, HashSet<string>> _edges = new(StringComparer.Ordinal);

    // ── Force-mark as recursion-capable ─────────────────────────────────────────
    // Used by Preserve-mode collection helpers: when an object mapper is used as an element
    // converter inside a Preserve collection, it MUST get the (ctx, depth) signature so the
    // collection helper can thread ctx to it. We force-mark it before ComputeRecursionCapability
    // so that the post-analysis patching includes it.
    private readonly HashSet<string> _forcedRecursionCapable = new(StringComparer.Ordinal);

    private readonly Dictionary<(string, string), string> _reserved = new();

    // The method name currently being body-resolved (set by SetCurrentPair).
    private string? _currentPair;

    // Cached result; null until ComputeRecursionCapability() is called.
    private HashSet<string>? _recursionCapable;

    /// <summary>
    ///     Whether the depth cap was exceeded. When true a DWARF031 was already scheduled.
    /// </summary>
    public bool CapExceeded { get; private set; }

    /// <summary>
    ///     The fully-qualified name of the first type that triggered the cap, for diagnostics.
    /// </summary>
    public string CapTriggerType { get; private set; } = "";

    /// <summary>
    ///     Returns true when there are pending (src, tgt) pairs whose bodies need to be built.
    /// </summary>
    public bool HasPending => _buildQueue.Count > 0;

    /// <summary>The recorded None-mode ctx-upgrade candidates (see <see cref="RecordCtxUpgradeCandidate" />).</summary>
    public IReadOnlyList<(string HelperName, string[] ElemMethods, Action<Func<string, string>> ReSynth)>
        CtxUpgradeCandidates
        => _ctxUpgradeCandidates;

    /// <summary>
    ///     Dequeues the next pending (src, tgt, methodName, autoNest) to build.
    ///     autoNest is the per-method value that triggered the enqueue (C1 fix).
    /// </summary>
    public (ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, bool AutoNest) Dequeue()
    {
        return _buildQueue.Dequeue();
    }

    /// <summary>
    ///     Informs the registry that the body of <paramref name="methodName" /> is now being resolved.
    ///     Any subsequent <see cref="GetOrReserve" /> call (from member resolution of this pair)
    ///     will be recorded as a dependency edge.
    /// </summary>
    public void SetCurrentPair(string methodName)
    {
        _currentPair = methodName;
        // Ensure a node exists for this method even if it calls nothing.
        if (!_edges.ContainsKey(methodName))
            _edges[methodName] = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Clears the current-pair context (call after body resolution is complete).
    /// </summary>
    public void ClearCurrentPair()
    {
        _currentPair = null;
    }

    /// <summary>
    ///     If a method for <paramref name="src" />→<paramref name="tgt" /> is already registered,
    ///     returns its method name without enqueuing a new build. Otherwise reserves a unique
    ///     FNV-1a-hashed name, enqueues the pair for body-building, and returns the name.
    ///     <para>
    ///         CRITICAL: the key is recorded BEFORE the body is built, so recursive types
    ///         (e.g. <c>Tree { List&lt;Tree&gt; Children }</c>) hit the "already registered" branch
    ///         on the second encounter and return the reserved name — terminating the generator-time
    ///         recursion without needing a separate depth counter.
    ///     </para>
    /// </summary>
    /// <param name="autoNest">
    ///     C1 fix: the per-method autoNest value that triggered this enqueue.
    ///     Stored alongside the pair so the drain loop uses THIS value (not the class-level default)
    ///     when resolving the pair's body, propagating the override to depth-2+ nested members.
    /// </param>
    public string? GetOrReserve(ITypeSymbol src, INamedTypeSymbol tgt, LocationInfo? location, bool autoNest = true)
    {
        var srcFqn = src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var tgtFqn = tgt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
            if (string.IsNullOrEmpty(CapTriggerType)) CapTriggerType = $"{srcFqn} → {tgtFqn}";
            return null;
        }

        methodName = BuildMethodName(srcFqn, tgtFqn);
        _reserved[key] = methodName;
        _autoNestByName[methodName] = autoNest;
        _buildQueue.Enqueue((src, tgt, methodName, autoNest));

        // Record the dependency edge: current pair → new pair.
        RecordEdge(methodName);

        return methodName;
    }

    /// <summary>
    ///     Returns the autoNest value that was stored when this pair was enqueued (C1 fix).
    ///     Defaults to true when not found (should not happen during a well-formed drain).
    /// </summary>
    public bool GetAutoNest(string methodName)
    {
        return !_autoNestByName.TryGetValue(methodName, out var v) || v;
    }

    private void RecordEdge(string calleeName)
    {
        if (_currentPair is null) return;
        if (!_edges.TryGetValue(_currentPair, out var deps))
        {
            deps = new HashSet<string>(StringComparer.Ordinal);
            _edges[_currentPair] = deps;
        }

        deps.Add(calleeName);
    }

    // ── Recursion-capability analysis ────────────────────────────────────────────

    /// <summary>
    ///     Computes which synthesized methods are "recursion-capable" — i.e. the method can
    ///     transitively call itself (directly or indirectly). Must be called AFTER the build
    ///     queue is fully drained.
    /// </summary>
    /// <remarks>
    ///     Uses a simple DFS reachability check: method M is recursion-capable if M can
    ///     reach M in the directed call graph. This is equivalent to M being on a cycle.
    /// </remarks>
    public void ComputeRecursionCapability()
    {
        _recursionCapable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var start in _edges.Keys)
            if (CanReachSelf(start))
                _recursionCapable.Add(start);

        // Also include any method that was force-marked as recursion-capable
        // (e.g. object mappers that serve as element converters in Preserve collections).
        foreach (var forced in _forcedRecursionCapable)
            _recursionCapable.Add(forced);
    }

    /// <summary>
    ///     Returns true when the given synthesized method name is recursion-capable.
    ///     <see cref="ComputeRecursionCapability" /> must be called first.
    /// </summary>
    public bool IsRecursionCapable(string methodName)
    {
        return _recursionCapable?.Contains(methodName) == true;
    }

    /// <summary>
    ///     Marks <paramref name="methodName" /> as recursion-capable unconditionally.
    ///     Call before <see cref="ComputeRecursionCapability" /> for the flag to take effect.
    ///     Used by Preserve-mode collection helpers that thread ctx to object-mapper elements.
    /// </summary>
    public void ForceRecursionCapable(string methodName)
    {
        _forcedRecursionCapable.Add(methodName);
    }

    /// <summary>
    ///     Records a collection/dict helper that may need ctx-threading if one of
    ///     <paramref name="elemMethods" /> turns out self-recursive. <paramref name="reSynth" /> receives a
    ///     resolver (method → ctx-companion-name if self-recursive, else the method itself) and rewrites
    ///     the helper body in place.
    /// </summary>
    public void RecordCtxUpgradeCandidate(string helperName, string[] elemMethods, Action<Func<string, string>> reSynth)
    {
        _ctxUpgradeCandidates.Add((helperName, elemMethods, reSynth));
    }

    private bool CanReachSelf(string start)
    {
        // DFS: can we return to 'start' following outgoing edges?
        var visited = new HashSet<string>(StringComparer.Ordinal);
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
                foreach (var dep in deps)
                    stack.Push(dep);
        }

        return false;
    }

    // ── Name synthesis ────────────────────────────────────────────────────────

    /// <summary>
    ///     Produces a deterministic, collision-resistant private method name for the given pair.
    ///     Format: __DwarfMap_Obj_{SanitizedSrc}_{SanitizedTgt}_{Fnv1aHash}
    /// </summary>
    private static string BuildMethodName(string srcFqn, string tgtFqn)
    {
        var srcSan = Sanitize(srcFqn);
        var tgtSan = Sanitize(tgtFqn);
        var hash = Fnv1a32(srcFqn + "\x00" + tgtFqn);
        return $"__DwarfMap_Obj_{srcSan}_{tgtSan}_{hash:X8}";
    }

    /// <summary>
    ///     Produces a deterministic private name for a Preserve-mode dispatch wrapper for the given
    ///     (source, target) pair. Uses prefix <c>__DwarfMap_Disp_</c> to distinguish it from the
    ///     regular auto-nest <c>__DwarfMap_Obj_</c> helpers.
    ///     Called by MapperExtractor after the MF-A fix to synthesize ctx-threading wrappers for
    ///     [MapDerivedType] dispatch methods under ReferenceHandling=Preserve.
    /// </summary>
    internal static string BuildDispatchWrapperName(string srcFqn, string tgtFqn)
    {
        var srcSan = Sanitize(srcFqn);
        var tgtSan = Sanitize(tgtFqn);
        var hash = Fnv1a32(srcFqn + "\x00" + tgtFqn);
        return $"__DwarfMap_Disp_{srcSan}_{tgtSan}_{hash:X8}";
    }

    /// <summary>Strips non-identifier characters, keeping only letters, digits, underscores.</summary>
    private static string Sanitize(string fqn)
    {
        var sb = new StringBuilder(fqn.Length);
        foreach (var c in fqn)
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c == '.' || c == ':' || c == '<' || c == '>' || c == ',')
                sb.Append('_');
        // Cap length so generated identifiers stay manageable
        const int max = 48;
        if (sb.Length > max) sb.Length = max;
        return sb.ToString();
    }

    /// <summary>
    ///     FNV-1a 32-bit hash — fast, deterministic, good dispersion for type names.
    ///     <para>
    ///         <b>Collision analysis (Item 3 audit finding)</b>: two distinct (srcFqn, tgtFqn) pairs
    ///         could theoretically produce the same 32-bit hash, yielding the same method name. A collision
    ///         guard is intentionally omitted because:
    ///         <list type="bullet">
    ///             <item>
    ///                 The primary deduplication key in <see cref="_reserved" /> is the <c>(srcFqn, tgtFqn)</c>
    ///                 string TUPLE — not the hash. So two distinct pairs never overwrite each other's registry entry.
    ///             </item>
    ///             <item>
    ///                 A hash collision would produce two distinct pairs registered under different keys but with
    ///                 the same method name. This would cause a C# compiler error (duplicate method) in the generated
    ///                 output — making the collision loudly visible rather than silent.
    ///             </item>
    ///             <item>
    ///                 32-bit FNV over fully-qualified C# type names: collision probability is ~1 in 4 billion
    ///                 per distinct pair. Typical mappers have fewer than 50 type pairs — astronomically unlikely.
    ///             </item>
    ///         </list>
    ///         If a collision ever surfaces, the generated code will fail to compile (CS0111 duplicate member),
    ///         which is a loud, actionable signal. At that point, widening to 64-bit or adding a suffix counter
    ///         would be the appropriate fix.
    ///     </para>
    /// </summary>
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
