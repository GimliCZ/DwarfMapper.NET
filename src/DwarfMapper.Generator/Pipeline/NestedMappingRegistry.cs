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
/// </summary>
internal sealed class NestedMappingRegistry
{
    private const int MaxPairs = 512;

    private readonly Dictionary<(string, string), string> _reserved =
        new Dictionary<(string, string), string>();

    private readonly Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name)> _buildQueue =
        new Queue<(ITypeSymbol Src, INamedTypeSymbol Tgt, string Name)>();

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

        if (_reserved.TryGetValue(key, out var existing))
        {
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

        var name = BuildMethodName(srcFqn, tgtFqn);
        _reserved[key] = name;
        _buildQueue.Enqueue((src, tgt, name));
        return name;
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
