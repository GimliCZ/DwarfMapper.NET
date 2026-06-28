// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// Structural value comparison across two runtime types that share a shape but are not the same Type — e.g.
/// a source and its mapped destination, or two destinations produced by the same source compiled into
/// different assemblies. Compares by member name (properties + public fields), recursing into nested objects
/// and walking collections positionally. Used by the value oracles (items 5/11).
/// </summary>
internal static class CrossTypeComparer
{
    public sealed record Diff(string Path, string? Expected, string? Actual);

    public static List<Diff> Compare(object? expected, object? actual)
    {
        var diffs = new List<Diff>();
        CompareValues("root", expected, actual, diffs, 0);
        return diffs;
    }

    private static void CompareByName(string path, Type aType, object aVal, Type bType, object bVal, List<Diff> diffs, int depth)
    {
        const int MaxDepth = 8;
        if (depth > MaxDepth) return;

        foreach (var ap in aType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!ap.CanRead || ap.GetIndexParameters().Length != 0) continue;
            var bp = bType.GetProperty(ap.Name, BindingFlags.Public | BindingFlags.Instance);
            if (bp is null || !bp.CanRead) continue;
            CompareValues(path + "." + ap.Name, ap.GetValue(aVal), bp.GetValue(bVal), diffs, depth + 1);
        }
        foreach (var af in aType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var bf = bType.GetField(af.Name, BindingFlags.Public | BindingFlags.Instance);
            if (bf is null) continue;
            CompareValues(path + "." + af.Name, af.GetValue(aVal), bf.GetValue(bVal), diffs, depth + 1);
        }
    }

    private static void CompareValues(string path, object? a, object? b, List<Diff> diffs, int depth)
    {
        const int MaxDepth = 8;
        if (depth > MaxDepth) return;

        if (a is null && b is null) return;
        if (a is null || b is null) { diffs.Add(new Diff(path, Fmt(a), Fmt(b))); return; }

        var aType = a.GetType();
        if (IsScalar(aType))
        {
            if (!ScalarEquals(a, b)) diffs.Add(new Diff(path, Fmt(a), Fmt(b)));
            return;
        }

        if (a is string) { if (!Equals(a, b)) diffs.Add(new Diff(path, Fmt(a), Fmt(b))); return; }

        if (a is IEnumerable ea && b is IEnumerable eb)
        {
            var al = ToList(ea);
            var bl = ToList(eb);
            if (al.Count != bl.Count)
                diffs.Add(new Diff(path + ".Count", al.Count.ToString(CultureInfo.InvariantCulture), bl.Count.ToString(CultureInfo.InvariantCulture)));
            var n = Math.Min(al.Count, bl.Count);
            for (var i = 0; i < n; i++)
                CompareValues(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", al[i], bl[i], diffs, depth + 1);
            return;
        }

        CompareByName(path, aType, a, b.GetType(), b, diffs, depth + 1);
    }

    private static bool IsScalar(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(decimal)
        || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);

    private static bool ScalarEquals(object a, object b)
    {
        if (a is double da && b is double db) return Math.Abs(da - db) < 1e-9;
        if (a is float fa && b is float fb) return Math.Abs(fa - fb) < 1e-6f;
        if (a.GetType().IsEnum && b.GetType().IsEnum)
            return Convert.ToInt64(a, CultureInfo.InvariantCulture) == Convert.ToInt64(b, CultureInfo.InvariantCulture);
        return Equals(a, b);
    }

    private static List<object?> ToList(IEnumerable e)
    {
        var l = new List<object?>();
        foreach (var x in e) l.Add(x);
        return l;
    }

    private static string? Fmt(object? o) => o switch
    {
        null => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString(),
    };

    public static string Render(IReadOnlyList<Diff> diffs)
    {
        var sb = new StringBuilder();
        foreach (var d in diffs)
            sb.Append("  ").Append(d.Path).Append(": expected ").Append(d.Expected ?? "<null>")
              .Append(", actual ").Append(d.Actual ?? "<null>").Append('\n');
        return sb.ToString();
    }
}
