// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.DifferentialTests
{
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
            if (depth > 12)
            {
                return;
            }

            if (expected is null || actual is null)
            {
                if (!ReferenceEquals(expected, actual))
                {
                    found.Add($"{path}: {Describe(expected)} vs {Describe(actual)}");
                }

                return;
            }

            var type = expected.GetType();

            if (IsScalar(type))
            {
                if (!Equals(expected, actual))
                {
                    found.Add($"{path}: {Describe(expected)} vs {Describe(actual)}");
                }

                return;
            }

            if (expected is IDictionary expectedDict && actual is IDictionary actualDict)
            {
                if (expectedDict.Count != actualDict.Count)
                {
                    found.Add($"{path}: {expectedDict.Count} entries vs {actualDict.Count}");
                    return;
                }

                // Compare the KEY SETS as sorted sequences before looking anything up. `actualDict.Contains(key)`
                // asks the ACTUAL dictionary's own comparer, so a target built with OrdinalIgnoreCase would report
                // "A" as present when what it really holds is "a" — the two mappers would then agree on a
                // dictionary whose keys differ, which is the one thing this comparer must never do.
                var expectedKeys = expectedDict.Keys.Cast<object>()
                    .Select(k => k.ToString() ?? "").OrderBy(k => k, StringComparer.Ordinal).ToList();
                var actualKeys = actualDict.Keys.Cast<object>()
                    .Select(k => k.ToString() ?? "").OrderBy(k => k, StringComparer.Ordinal).ToList();

                if (!expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal))
                {
                    found.Add($"{path}: keys [{string.Join(", ", expectedKeys)}] vs [{string.Join(", ", actualKeys)}]");
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

            // Sequences are compared ELEMENT-WISE, deliberately before the runtime-type check below: a member
            // declared IReadOnlyList<T> that DwarfMapper materialises as List<T> and AutoMapper as T[] holds the
            // same data, and a consumer reading through the declared type cannot tell. A differential oracle
            // compares what a consumer OBSERVES, so concrete-collection-type divergence is not a difference.
            // (Pinned by MemberComparerTests — the ordering here is the whole of that policy.)
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
                Walk(property.GetValue(expected),
                    property.GetValue(actual),
                    $"{path}.{property.Name}",
                    found,
                    depth + 1);

            // FIELDS as well as properties, and not for symmetry — a ValueTuple exposes Item1/Item2 as public
            // FIELDS and has no public properties at all. Walking properties alone, this method would find
            // nothing to compare in a tuple, report no differences, and pass for any two tuples whatsoever. A
            // shape that can only ever agree is the hollow test this repository detects elsewhere and would have
            // added to itself here.
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
                Walk(field.GetValue(expected),
                    field.GetValue(actual),
                    $"{path}.{field.Name}",
                    found,
                    depth + 1);
        }

        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(DateOnly) || type == typeof(TimeOnly) || (Nullable.GetUnderlyingType(type) is { } inner && IsScalar(inner));
        }

        private static string Describe(object? value)
        {
            return value switch
            {
                null => "null",
                string s => "\"" + s + "\"",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "?"
            };
        }
    }
}
