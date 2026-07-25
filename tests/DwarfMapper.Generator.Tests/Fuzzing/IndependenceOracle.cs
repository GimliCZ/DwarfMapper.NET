// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// The INDEPENDENCE oracle: a mapped graph must share no mutable collection instance with the graph it was
/// mapped from.
/// <para>
/// This closes a real hole in the oracle suite. Every other oracle compares <b>values</b>
/// (<see cref="CrossTypeComparer" />) or identity <b>within</b> one graph (the topology oracles). None of
/// them can see aliasing <b>across</b> the map boundary — because an aliased collection has, by definition,
/// exactly the right values. So a destination member that was handed the source's own <c>List&lt;T&gt;</c>
/// passed every check we had, while in fact mutating either side silently corrupted the other.
/// </para>
/// <para>
/// Scope note: only COLLECTIONS are required to be fresh. A plain object member of the same type on both
/// sides is legitimately reference-copied by the generator (there is no map for it, so it is assigned across),
/// and the behavioural schema deliberately uses identical Src/Dst member types — so asserting "no shared
/// references at all" would fire on that by design. A mapped collection, by contrast, is ALWAYS rebuilt, so
/// sharing one is always a bug.
/// </para>
/// </summary>
internal static class IndependenceOracle
{
    private const int MaxDepth = 32;

    /// <summary>Every collection instance reachable from <paramref name="root" />, by reference identity.</summary>
    public static HashSet<object> CollectCollections(object? root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var collections = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(root, seen, collections, 0);
        return collections;
    }

    /// <summary>
    /// The collection instances reachable from BOTH graphs — i.e. the aliases. Empty means the map produced an
    /// independent value, which is the contract.
    /// </summary>
    public static HashSet<object> SharedCollections(object? source, object? mapped)
    {
        var shared = CollectCollections(source);
        shared.IntersectWith(CollectCollections(mapped));
        return shared;
    }

    public static string Render(HashSet<object> shared)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in shared)
        {
            var count = o is ICollection c ? c.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
            sb.Append("  SHARED ").Append(o.GetType().Name).Append(" (Count=").Append(count).Append(')').AppendLine();
        }

        return sb.ToString();
    }

    private static void Walk(object? o, HashSet<object> seen, HashSet<object> collections, int depth)
    {
        if (o is null || depth > MaxDepth)
        {
            return;
        }

        // Value-ish leaves can't be aliased in any way that matters (and string is interned).
        if (o is string || o is decimal || o is DateTime || o is DateTimeOffset || o is TimeSpan || o is Guid)
        {
            return;
        }

        var type = o.GetType();
        if (type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        if (!seen.Add(o))
        {
            return; // already visited — also breaks cycles
        }

        if (o is IEnumerable enumerable)
        {
            collections.Add(o);
            foreach (var item in enumerable)
            {
                Walk(item, seen, collections, depth + 1);
            }

            return;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead)
            {
                continue;
            }

            object? value;
            try
            {
                value = p.GetValue(o);
            }
            catch (TargetInvocationException)
            {
                continue;
            }

            Walk(value, seen, collections, depth + 1);
        }
    }
}
