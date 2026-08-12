// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.DifferentialTests;

/// <summary>
///     Compares two mapped results member by member and says WHERE they first disagree.
/// </summary>
/// <remarks>
///     <para>
///         <c>Assert.Equal(a, b)</c> on two DTOs reports "not equal" and a type name, which for a fifteen-member
///         object is the beginning of the investigation rather than the end. This walks the graph and returns a
///         path — <c>Items[2].Address.City</c> — because the whole value of a differential harness is that a
///         disagreement arrives pre-diagnosed.
///     </para>
///     <para>
///         Reflective on purpose. The point is to compare what the mappers PRODUCED without either of them
///         being asked what it thinks it produced.
///     </para>
/// </remarks>
internal static class MemberComparer
{
    /// <summary>Every path at which the two objects differ, empty when they agree.</summary>
    public static IReadOnlyList<string> Differences(object? expected, object? actual, string path = "")
    {
        var found = new List<string>();
        Walk(expected, actual, string.IsNullOrEmpty(path) ? "<root>" : path, found, 0);
        return found;
    }

    private static void Walk(object? expected, object? actual, string path, List<string> found, int depth)
    {
        // Deep enough to cross every nesting level the shapes use, shallow enough that a cyclic graph
        // (which some shapes deliberately contain) cannot run away.
        if (depth > 12) return;

        if (expected is null || actual is null)
        {
            if (!ReferenceEquals(expected, actual))
                found.Add($"{path}: {Describe(expected)} vs {Describe(actual)}");
            return;
        }

        var type = expected.GetType();

        if (IsScalar(type))
        {
            if (!Equals(expected, actual))
                found.Add($"{path}: {Describe(expected)} vs {Describe(actual)}");
            return;
        }

        if (expected is IDictionary expectedDict && actual is IDictionary actualDict)
        {
            if (expectedDict.Count != actualDict.Count)
            {
                found.Add($"{path}: {expectedDict.Count} entries vs {actualDict.Count}");
                return;
            }

            foreach (DictionaryEntry entry in expectedDict)
            {
                if (!actualDict.Contains(entry.Key))
                {
                    found.Add($"{path}[{entry.Key}]: present vs missing");
                    continue;
                }

                Walk(entry.Value, actualDict[entry.Key], $"{path}[{entry.Key}]", found, depth + 1);
            }

            return;
        }

        if (expected is IEnumerable expectedSeq && actual is IEnumerable actualSeq)
        {
            var left = expectedSeq.Cast<object?>().ToList();
            var right = actualSeq.Cast<object?>().ToList();

            if (left.Count != right.Count)
            {
                found.Add($"{path}: {left.Count} items vs {right.Count}");
                return;
            }

            for (var i = 0; i < left.Count; i++) Walk(left[i], right[i], $"{path}[{i}]", found, depth + 1);
            return;
        }

        // Two objects. Compare by the RUNTIME type of the expected side: a polymorphic result that came back
        // as the base type is itself a difference, and comparing only base members would hide it.
        if (actual.GetType() != type)
        {
            found.Add($"{path}: runtime type {type.Name} vs {actual.GetType().Name}");
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
            Walk(property.GetValue(expected), property.GetValue(actual),
                $"{path}.{property.Name}", found, depth + 1);
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(Guid) || type == typeof(DateOnly) || type == typeof(TimeOnly)
        || Nullable.GetUnderlyingType(type) is { } inner && IsScalar(inner);

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "?"
    };
}
