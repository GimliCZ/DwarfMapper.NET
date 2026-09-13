// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
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

        private readonly Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, bool AutoNest, LocationInfo? Origin)> _buildQueue = new();

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

        // ── Collection/dict helper → element-method edges, every mode ──────────────
        // A helper is not a method model, so its call to its element/key/value method is invisible to the
        // declared call graph. DWARF030 needs that edge to see a constructor argument whose cycle runs through a
        // collection (`TreeDto(List<TreeDto> kids)`); recorded separately from the None-mode candidates above so
        // adding it cannot change which methods the recursion phases mark.
        private readonly List<(string HelperName, string ElemMethod, string? ElemParamTypeFqn)> _helperElementEdges = new();

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

        // ── Is this pair CUSTOMIZED? (round 29, T0.2c) ──────────────────────────────
        // The rule lives in MapperExtractor (it reads the mapper's pair-scoped attributes, whose types are
        // private to it); the registry carries it because the registry is the thing that NAMES a pair's helper,
        // and "customized" is precisely what gets baked into the body behind that name. Extract-scoped, like the
        // closures in _ctxUpgradeCandidates, so capturing ITypeSymbols is safe. Null until wired, and then the
        // answer is "no" — the same verdict a mapper with no pair-scoped attributes produces.
        private Func<ITypeSymbol, ITypeSymbol, (string What, string Verb)?>? _pairIsCustomized;

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

        /// <summary>The recorded helper → element-method edges (see <see cref="RecordHelperElementEdge" />).</summary>
        public IReadOnlyList<(string HelperName, string ElemMethod, string? ElemParamTypeFqn)> HelperElementEdges => _helperElementEdges;

        /// <summary>
        ///     Dequeues the next pending (src, tgt, methodName, autoNest, origin) to build.
        ///     autoNest is the per-method value that triggered the enqueue (C1 fix); origin is the location the
        ///     first requester of the pair was resolving at — the declared method whose member reached it, or,
        ///     for a deeper pair, the anchor its requesting pair was itself built under.
        /// </summary>
        public (ITypeSymbol Src, INamedTypeSymbol Tgt, string Name, bool AutoNest, LocationInfo? Origin) Dequeue()
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
            {
                _edges[methodName] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        ///     Clears the current-pair context (call after body resolution is complete).
        /// </summary>
        public void ClearCurrentPair()
        {
            _currentPair = null;
        }

        /// <summary>
        ///     Teaches the registry which (src, tgt) pairs the mapper CUSTOMIZES — a pair-scoped
        ///     <c>[MapIgnore&lt;T&gt;]</c>/<c>[MapProperty&lt;S,T&gt;]</c>/<c>[MapValue&lt;T&gt;]</c>, or a
        ///     <c>[BeforeMap]</c>/<c>[AfterMap]</c> hook matching the pair. Wired once per extraction, from the
        ///     one site where the mapper's declarations are known.
        /// </summary>
        public void SetPairCustomizationRule(Func<ITypeSymbol, ITypeSymbol, (string What, string Verb)?> rule)
        {
            _pairIsCustomized = rule;
        }

        /// <summary>
        ///     True when the helper this registry would hand out for <paramref name="src" />→<paramref name="tgt" />
        ///     carries customization, so no fast path may bypass it. See <see cref="SetPairCustomizationRule" />.
        /// </summary>
        public bool PairIsCustomized(ITypeSymbol src, ITypeSymbol tgt)
        {
            return PairCustomization(src, tgt) is not null;
        }

        /// <summary>
        ///     How to NAME the directive or hook that customizes <paramref name="src" />→<paramref name="tgt" /> —
        ///     a complete noun phrase (<c>"the pair-scoped [MapIgnore&lt;T&gt;] declared for 'A' → 'B'"</c>,
        ///     <c>"the [AfterMap] hook matching 'A' → 'B'"</c>) and the gerund that reads correctly for it — or
        ///     <see langword="null" /> when nothing does. Round 29 T0.2d: DWARF106 has to name what
        ///     <c>[Reinterpret]</c> is overriding, and the answer must be the one this registry's own gate uses —
        ///     so the rule yields the phrase and <see cref="PairIsCustomized" /> is the boolean derived from it,
        ///     rather than a second predicate that could drift from the first.
        /// </summary>
        public (string What, string Verb)? PairCustomization(ITypeSymbol src, ITypeSymbol tgt)
        {
            return _pairIsCustomized?.Invoke(src, tgt);
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
        /// <param name="src">The nested pair's source type; its fully-qualified name is half the registry key.</param>
        /// <param name="tgt">The nested pair's destination type; its fully-qualified name is the other half.</param>
        /// <param name="location">
        ///     The site the caller is resolving at. Stored with the enqueued pair and handed back by
        ///     <see cref="Dequeue" /> as the anchor for every diagnostic the pair's own resolution reports:
        ///     a synthesized pair has no declaration of its own, and without this its DWARF001/005/025/… carried
        ///     <c>Location.None</c> — no file, no line, and in Rider the generic "failed to generate sources" title.
        ///     The one refusal here — the <c>MaxPairs</c> cap — records the triggering pair on the registry and
        ///     returns <see langword="null" />, leaving the caller to report at the location it already holds.
        /// </param>
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
                if (string.IsNullOrEmpty(CapTriggerType))
                {
                    CapTriggerType = $"{srcFqn} → {tgtFqn}";
                }

                return null;
            }

            methodName = BuildMethodName(srcFqn, tgtFqn);
            _reserved[key] = methodName;
            _buildQueue.Enqueue((src, tgt, methodName, autoNest, location));

            // Record the dependency edge: current pair → new pair.
            RecordEdge(methodName);

            return methodName;
        }

        private void RecordEdge(string calleeName)
        {
            if (_currentPair is null)
            {
                return;
            }

            // SetCurrentPair created this node before _currentPair could name it, and no entry is ever removed.
            _edges[_currentPair].Add(calleeName);
        }

        // ── Recursion-capability analysis ────────────────────────────────────────────

        /// <summary>
        ///     Computes which synthesized methods are "recursion-capable" — i.e. the method can
        ///     transitively call itself (directly or indirectly). Must be called AFTER the build
        ///     queue is fully drained.
        /// </summary>
        /// <remarks>
        ///     Method M is recursion-capable when M can reach M in the directed call graph — equivalently, when M
        ///     lies on a cycle. ISSUE-023: that used to be answered by a fresh DFS per node, O(V·(V+E)); a single
        ///     Tarjan SCC pass answers it for every node in O(V+E). Same set, one traversal.
        /// </remarks>
        public void ComputeRecursionCapability()
        {
            _recursionCapable = StronglyConnected.NodesOnACycle(_edges);

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

        /// <summary>
        ///     Records that collection/dict helper <paramref name="helperName" /> calls <paramref name="elemMethod" />
        ///     for each element; <paramref name="elemParamTypeFqn" /> is the adopted overload's parameter type when the
        ///     element method is a user-declared one (see <c>MemberMap.ConverterParamTypeFqn</c>).
        /// </summary>
        public void RecordHelperElementEdge(string helperName, string elemMethod, string? elemParamTypeFqn)
        {
            _helperElementEdges.Add((helperName, elemMethod, elemParamTypeFqn));
        }

        // ── Name synthesis ────────────────────────────────────────────────────────

        /// <summary>
        ///     Produces a deterministic, collision-resistant private method name for the given pair.
        ///     Format: __DwarfMap_Obj_{SanitizedSrc}_{SanitizedTgt}_{Fnv1aHash}
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
        private static string BuildMethodName(string srcFqn, string tgtFqn)
        {
            var srcSan = Sanitize(srcFqn);
            var tgtSan = Sanitize(tgtFqn);
            // Uppercased to match the historical "{hash:X8}" formatting of the uint this once was —
            // StableHash.Fnv1aPerByte returns lowercase, and changing case here would rename every
            // generated helper (and move the golden fingerprint) for zero behavioural benefit.
            var hash = StableHash.Fnv1aPerByte(srcFqn + "\x00" + tgtFqn).ToUpperInvariant();
            return $"{GeneratedNames.ObjectMap}{srcSan}_{tgtSan}_{hash}";
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
            // Uppercased — see BuildMethodName's remarks: this is the historical "{hash:X8}" case, preserved.
            var hash = StableHash.Fnv1aPerByte(srcFqn + "\x00" + tgtFqn).ToUpperInvariant();
            return $"{GeneratedNames.Dispatch}{srcSan}_{tgtSan}_{hash}";
        }

        /// <summary>Strips non-identifier characters, keeping only letters, digits, underscores.</summary>
        private static string Sanitize(string fqn)
        {
            var sb = new StringBuilder(fqn.Length);
            foreach (var c in fqn)
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (c == '.' || c == ':' || c == '<' || c == '>' || c == ',')
                {
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
    }
}
