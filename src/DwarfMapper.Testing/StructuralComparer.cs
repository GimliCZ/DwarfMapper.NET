// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DwarfMapper.Testing;

/// <summary>Deep, path-aware structural comparison producing an "informed dump" of differences.</summary>
public static class StructuralComparer
{
    private const int MaxDepth = 12;
    private const double FloatEpsilon = 1e-9;

    /// <summary>Compare two object graphs and return every difference with its member path.</summary>
    public static IReadOnlyList<MemberDiff> Diff(object? expected, object? actual)
    {
        var diffs = new List<MemberDiff>();
        Compare("root", expected, actual, diffs, 0);
        return diffs;
    }

    /// <summary>Render diffs as readable lines.</summary>
    public static string Render(IReadOnlyList<MemberDiff> diffs)
    {
        if (diffs is null)
        {
            throw new ArgumentNullException(nameof(diffs));
        }
        var sb = new StringBuilder();
        foreach (var d in diffs)
        {
            sb.Append("  ").Append(d.Path).Append(": expected ").Append(d.Expected ?? "<null>")
              .Append(", actual ").Append(d.Actual ?? "<null>").Append('\n');
        }
        return sb.ToString();
    }

    private static void Compare(string path, object? expected, object? actual, List<MemberDiff> diffs, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }
        if (expected is null || actual is null)
        {
            if (!(expected is null && actual is null))
            {
                diffs.Add(new MemberDiff(path, Fmt(expected), Fmt(actual)));
            }
            return;
        }

        var type = expected.GetType();
        if (IsScalar(type))
        {
            if (!ScalarEquals(expected, actual))
            {
                diffs.Add(new MemberDiff(path, Fmt(expected), Fmt(actual)));
            }
            return;
        }

        if (expected is IEnumerable le && actual is IEnumerable re)
        {
            var el = ToList(le);
            var rl = ToList(re);
            if (el.Count != rl.Count)
            {
                diffs.Add(new MemberDiff(path + ".Count", Fmt(el.Count), Fmt(rl.Count)));
            }
            var n = Math.Min(el.Count, rl.Count);
            for (var i = 0; i < n; i++)
            {
                Compare(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", el[i], rl[i], diffs, depth + 1);
            }
            return;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanRead && p.GetIndexParameters().Length == 0)
            {
                Compare(path + "." + p.Name, p.GetValue(expected), p.GetValue(actual), diffs, depth + 1);
            }
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            Compare(path + "." + f.Name, f.GetValue(expected), f.GetValue(actual), diffs, depth + 1);
        }
    }

    private static bool IsScalar(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
        || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);

    private static bool ScalarEquals(object a, object b)
    {
        if (a is double da && b is double db)
        {
            return Math.Abs(da - db) < FloatEpsilon;
        }
        if (a is float fa && b is float fb)
        {
            return Math.Abs(fa - fb) < FloatEpsilon;
        }
        return Equals(a, b);
    }

    private static List<object?> ToList(IEnumerable e)
    {
        var list = new List<object?>();
        foreach (var item in e)
        {
            list.Add(item);
        }
        return list;
    }

    private static string? Fmt(object? o) => o switch
    {
        null => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => o.ToString(),
    };
}
