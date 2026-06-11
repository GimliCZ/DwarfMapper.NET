// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace DwarfMapper.Testing;

/// <summary>
/// Plan 19 Part E — graph-aware oracle comparer.
/// Two modes:
/// (a) Value compare: deep, MaxDepth-guarded, deterministic ordering for sets/dicts so
///     HashSet/Dictionary compare stably.
/// (b) Topology compare: asserts the target graph's sharing/cycle pattern matches the source's —
///     a shared source node produces the same target instance; a cycle is closed. Uses a
///     reference-identity visited map so the oracle itself never infinite-loops on cycles.
/// </summary>
public static class GraphOracleComparer
{
    private const int MaxValueDepth = 16;
    private const double FloatEpsilon = 1e-9;

    // ─────────────────────────────────────────────────────────────────────────────
    // (a) VALUE COMPARE
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deep value comparison between two object graphs.
    /// Sets / dictionaries are compared in deterministic (sorted) order.
    /// Returns every difference with its member path.
    /// </summary>
    public static IReadOnlyList<string> ValueDiff(object? expected, object? actual, string rootPath = "root")
    {
        var diffs = new List<string>();
        var visited = new Dictionary<(RuntimeId, RuntimeId), bool>(RuntimeIdPairComparer.Instance);
        ValueCompare(rootPath, expected, actual, diffs, 0, visited);
        return diffs;
    }

    /// <summary>Returns true when two graphs are value-equal; false otherwise.</summary>
    public static bool ValueEqual(object? expected, object? actual) =>
        ValueDiff(expected, actual).Count == 0;

    /// <summary>Render value diffs as a readable string.</summary>
    public static string RenderValueDiff(IReadOnlyList<string> diffs)
    {
        if (diffs is null) throw new ArgumentNullException(nameof(diffs));
        var sb = new StringBuilder();
        foreach (var d in diffs)
            sb.AppendLine("  " + d);
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (b) TOPOLOGY COMPARE
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Topology comparison: assert that each distinct source reference maps to exactly
    /// one target reference (sharing preserved), and that cycles are closed.
    /// <para>
    /// The oracle walks the source graph and target graph in lock-step. On revisiting a
    /// source node that was already seen, it asserts the corresponding target node is the
    /// same instance as last time (ReferenceEquals). This catches both
    /// sharing (shared source node → same target instance) and cycles (back-edge returns
    /// to the already-registered target).
    /// </para>
    /// Returns a list of topology violations; empty list = topology preserved.
    /// </summary>
    public static IReadOnlyList<string> TopologyDiff(object? srcRoot, object? tgtRoot, string rootPath = "root")
    {
        var violations = new List<string>();
        // src_id → target object (reference equality on keys)
        var srcToTgt = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        TopologyCompare(rootPath, srcRoot, tgtRoot, srcToTgt, violations, 0);
        return violations;
    }

    /// <summary>Returns true when topology is preserved; false otherwise.</summary>
    public static bool TopologyPreserved(object? srcRoot, object? tgtRoot) =>
        TopologyDiff(srcRoot, tgtRoot).Count == 0;

    /// <summary>Render topology diffs as a readable string.</summary>
    public static string RenderTopologyDiff(IReadOnlyList<string> violations)
    {
        if (violations is null) throw new ArgumentNullException(nameof(violations));
        var sb = new StringBuilder();
        foreach (var v in violations)
            sb.AppendLine("  TOPOLOGY: " + v);
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Cross-type value compare (for src/dst with different but structurally-identical types)
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Value diff between two objects of potentially-different runtime types.
    /// Compares scalar and collection members by name; handles type divergence
    /// (e.g. int→long widening) by comparing the numeric underlying value.
    /// </summary>
    public static IReadOnlyList<string> CrossTypeDiff(object? expected, object? actual,
        Type? expectedType = null, Type? actualType = null, string rootPath = "root")
    {
        var diffs = new List<string>();
        CrossTypeCompare(rootPath, expected, expectedType ?? expected?.GetType(),
            actual, actualType ?? actual?.GetType(), diffs, 0,
            new Dictionary<(RuntimeId, RuntimeId), bool>(RuntimeIdPairComparer.Instance));
        return diffs;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Implementation — value compare
    // ─────────────────────────────────────────────────────────────────────────────

    private static void ValueCompare(
        string path, object? expected, object? actual,
        List<string> diffs, int depth,
        Dictionary<(RuntimeId, RuntimeId), bool> visited)
    {
        if (depth > MaxValueDepth) return;

        if (expected is null && actual is null) return;
        if (expected is null || actual is null)
        {
            diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
            return;
        }

        var type = expected.GetType();

        if (IsScalar(type))
        {
            if (!ScalarEquals(expected, actual))
                diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
            return;
        }

        // Cycle guard for reference types
        if (!type.IsValueType)
        {
            var key = (new RuntimeId(expected), new RuntimeId(actual));
            if (visited.ContainsKey(key)) return; // already compared this pair
            visited[key] = true;
        }

        // IEnumerable (including arrays, lists, sets, dicts)
        if (expected is IEnumerable ee && actual is IEnumerable ae)
        {
            var el = ToSortedList(ee);
            var al = ToSortedList(ae);
            if (el.Count != al.Count)
            {
                diffs.Add(path + ".Count: expected " + el.Count.ToString(CultureInfo.InvariantCulture) + ", actual " + al.Count.ToString(CultureInfo.InvariantCulture));
            }
            var n = Math.Min(el.Count, al.Count);
            for (var i = 0; i < n; i++)
                ValueCompare(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    el[i], al[i], diffs, depth + 1, visited);
            return;
        }

        // Object / struct / record: compare all public properties + fields
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            ValueCompare(path + "." + p.Name, p.GetValue(expected), p.GetValue(actual),
                diffs, depth + 1, visited);
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            ValueCompare(path + "." + f.Name, f.GetValue(expected), f.GetValue(actual),
                diffs, depth + 1, visited);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Implementation — topology compare
    // ─────────────────────────────────────────────────────────────────────────────

    private static void TopologyCompare(
        string path, object? src, object? tgt,
        Dictionary<object, object> srcToTgt,
        List<string> violations, int depth)
    {
        const int MaxTopoDepth = 64;
        if (depth > MaxTopoDepth) return;

        if (src is null && tgt is null) return;
        if (src is null || tgt is null)
        {
            // null mismatch is a value issue not a topology issue; skip
            return;
        }

        var srcType = src.GetType();

        // Only reference types participate in topology tracking
        if (srcType.IsValueType || IsScalar(srcType)) return;

        // Have we seen this source object before?
        if (srcToTgt.TryGetValue(src, out var registeredTgt))
        {
            // The target must be the SAME instance we registered earlier
            if (!ReferenceEquals(registeredTgt, tgt))
            {
                violations.Add(
                    $"{path}: shared source node (#{RuntimeHelpers.GetHashCode(src)}) should map to " +
                    $"the same target instance (#{RuntimeHelpers.GetHashCode(registeredTgt)}) " +
                    $"but got a different instance (#{RuntimeHelpers.GetHashCode(tgt)})");
            }
            // Either way, stop recursing — we've already walked this pair
            return;
        }

        // First encounter: register and recurse
        srcToTgt[src] = tgt;

        // Walk IEnumerable members
        if (src is IEnumerable se && tgt is IEnumerable te)
        {
            var sl = ToOrderedList(se);
            var tl = ToOrderedList(te);
            var n = Math.Min(sl.Count, tl.Count);
            for (var i = 0; i < n; i++)
                TopologyCompare(
                    path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    sl[i], tl[i], srcToTgt, violations, depth + 1);
            return;
        }

        // Walk properties + fields (using src type to discover members)
        foreach (var p in srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            var sv = p.GetValue(src);
            // Find matching property on target (may be different type)
            var tp = tgt.GetType().GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance);
            var tv = tp is not null && tp.CanRead ? tp.GetValue(tgt) : null;
            TopologyCompare(path + "." + p.Name, sv, tv, srcToTgt, violations, depth + 1);
        }
        foreach (var f in srcType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var sv = f.GetValue(src);
            var tf = tgt.GetType().GetField(f.Name, BindingFlags.Public | BindingFlags.Instance);
            var tv = tf is not null ? tf.GetValue(tgt) : null;
            TopologyCompare(path + "." + f.Name, sv, tv, srcToTgt, violations, depth + 1);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Implementation — cross-type compare
    // ─────────────────────────────────────────────────────────────────────────────

    private static void CrossTypeCompare(
        string path, object? expected, Type? expectedType,
        object? actual, Type? actualType,
        List<string> diffs, int depth,
        Dictionary<(RuntimeId, RuntimeId), bool> visited)
    {
        if (depth > MaxValueDepth) return;

        if (expected is null && actual is null) return;
        if (expected is null || actual is null)
        {
            diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
            return;
        }

        expectedType ??= expected.GetType();
        actualType ??= actual.GetType();

        if (IsScalar(expectedType) || IsScalar(actualType))
        {
            // Numeric widening: compare by rounding to the lower-precision type.
            // E.g. float→double: compare (double)(float)actual vs (float)expected — float
            // represents the exact source value; widening to double changes the bit pattern
            // but not the logical value as seen from the float perspective.
            if (IsNumeric(expectedType) && IsNumeric(actualType))
            {
                try
                {
                    // Strategy: convert both to the higher-precision type (double) then
                    // round-trip through the SOURCE type's precision for comparison.
                    var srcIsFloat = expectedType == typeof(float);
                    var dstIsDouble = actualType == typeof(double);
                    if (srcIsFloat && dstIsDouble)
                    {
                        // float→double widening: cast actual back to float for comparison
                        var fExpected = (float)Convert.ChangeType(expected, typeof(float), CultureInfo.InvariantCulture)!;
                        var fActual = (float)Convert.ChangeType(actual, typeof(float), CultureInfo.InvariantCulture)!;
                        if (Math.Abs(fExpected - fActual) >= 1e-6f)
                            diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
                        return;
                    }
                    var ev = Convert.ToDecimal(expected, CultureInfo.InvariantCulture);
                    var av = Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
                    if (ev != av)
                        diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
                }
                catch (InvalidCastException)
                {
                    if (!ScalarEquals(expected, actual))
                        diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
                }
                catch (OverflowException)
                {
                    if (!ScalarEquals(expected, actual))
                        diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
                }
                return;
            }
            if (expectedType.IsEnum && actualType.IsEnum)
            {
                var ev = Convert.ToInt64(expected, CultureInfo.InvariantCulture);
                var av = Convert.ToInt64(actual, CultureInfo.InvariantCulture);
                if (ev != av)
                    diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
                return;
            }
            if (!ScalarEquals(expected, actual))
                diffs.Add(path + ": expected " + Fmt(expected) + ", actual " + Fmt(actual));
            return;
        }

        // Cycle guard
        if (!expectedType.IsValueType)
        {
            var key = (new RuntimeId(expected), new RuntimeId(actual));
            if (visited.ContainsKey(key)) return;
            visited[key] = true;
        }

        // Collections
        if (expected is IEnumerable ee && actual is IEnumerable ae)
        {
            var el = ToSortedList(ee);
            var al = ToSortedList(ae);
            if (el.Count != al.Count)
                diffs.Add(path + ".Count: expected " + el.Count.ToString(CultureInfo.InvariantCulture) + ", actual " + al.Count.ToString(CultureInfo.InvariantCulture));
            var n = Math.Min(el.Count, al.Count);
            for (var i = 0; i < n; i++)
                CrossTypeCompare(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    el[i], el[i]?.GetType(), al[i], al[i]?.GetType(), diffs, depth + 1, visited);
            return;
        }

        // Compare properties by name
        foreach (var sp in expectedType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!sp.CanRead || sp.GetIndexParameters().Length != 0) continue;
            var dp = actualType.GetProperty(sp.Name, BindingFlags.Public | BindingFlags.Instance);
            if (dp is null || !dp.CanRead) continue;
            var sv = sp.GetValue(expected);
            var dv = dp.GetValue(actual);
            CrossTypeCompare(path + "." + sp.Name, sv, sv?.GetType(), dv, dv?.GetType(),
                diffs, depth + 1, visited);
        }
        foreach (var sf in expectedType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var df = actualType.GetField(sf.Name, BindingFlags.Public | BindingFlags.Instance);
            if (df is null) continue;
            var sv = sf.GetValue(expected);
            var dv = df.GetValue(actual);
            CrossTypeCompare(path + "." + sf.Name, sv, sv?.GetType(), dv, dv?.GetType(),
                diffs, depth + 1, visited);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static bool IsScalar(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
        || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
        || t == typeof(TimeSpan);

    private static bool IsNumeric(Type t) =>
        t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort)
        || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong)
        || t == typeof(float) || t == typeof(double) || t == typeof(decimal);

    private static bool ScalarEquals(object a, object b)
    {
        if (a is double da && b is double db) return Math.Abs(da - db) < FloatEpsilon;
        if (a is float fa && b is float fb)   return Math.Abs(fa - fb) < 1e-6f;
        if (a.GetType().IsEnum && b.GetType().IsEnum)
            return Convert.ToInt64(a, CultureInfo.InvariantCulture)
                == Convert.ToInt64(b, CultureInfo.InvariantCulture);
        return Equals(a, b);
    }

    // Convert IEnumerable to a deterministically-ordered list.
    // For sets and dicts the order depends on iteration but for cross-type comparisons
    // within a single mapping result we compare src vs dst in the same order.
    // For stable assertions we sort scalar elements; for non-scalars we preserve order.
    private static List<object?> ToSortedList(IEnumerable e)
    {
        var list = new List<object?>();
        foreach (var item in e)
            list.Add(item);

        // Sort scalars for stable set comparison
        if (list.Count > 0 && list[0] is not null && IsScalar(list[0]!.GetType()))
        {
            list.Sort((x, y) =>
            {
                if (x is null && y is null) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                try { return StringComparer.Ordinal.Compare(
                    x.ToString(), y.ToString()); }
                catch (InvalidOperationException) { return 0; }
                catch (ArgumentException) { return 0; }
            });
        }

        return list;
    }

    // For topology, preserve insertion order (no sorting)
    private static List<object?> ToOrderedList(IEnumerable e)
    {
        var list = new List<object?>();
        foreach (var item in e)
            list.Add(item);
        return list;
    }

    private static string? Fmt(object? o) => o switch
    {
        null => "<null>",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString(),
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // RuntimeId: wraps an object for reference-identity keying in a Dictionary
    // ─────────────────────────────────────────────────────────────────────────────

    private readonly struct RuntimeId : IEquatable<RuntimeId>
    {
        private readonly int _hash;
        private readonly object _obj;

        internal RuntimeId(object obj)
        {
            _obj = obj;
            _hash = RuntimeHelpers.GetHashCode(obj);
        }

        public bool Equals(RuntimeId other) => ReferenceEquals(_obj, other._obj);
        public override bool Equals(object? obj) => obj is RuntimeId other && Equals(other);
        public override int GetHashCode() => _hash;
    }

    private sealed class RuntimeIdPairComparer : IEqualityComparer<(RuntimeId A, RuntimeId B)>
    {
        internal static readonly RuntimeIdPairComparer Instance = new();
        public bool Equals((RuntimeId A, RuntimeId B) x, (RuntimeId A, RuntimeId B) y)
            => x.A.Equals(y.A) && x.B.Equals(y.B);
        public int GetHashCode((RuntimeId A, RuntimeId B) obj)
            => HashCode.Combine(obj.A.GetHashCode(), obj.B.GetHashCode());
    }
}
