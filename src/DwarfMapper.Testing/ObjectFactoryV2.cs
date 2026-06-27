// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Testing;

/// <summary>
/// Extended deterministic object factory (Plan 19 Part E self-materializer).
/// Constructs instances of ANY supported DwarfMapper type from a seed:
/// - every basic scalar type
/// - every supported collection / dictionary / immutable / interface target (populated up to a bounded
///   depth; empty at the depth cap to terminate recursion)
/// - nested objects and records (bounded depth)
/// - reference-graph fixtures: self-loop, 2-node cycle, diamond, owner graph A→B/B⇄C/C⇄D/B⇄D.
/// Deterministic and replayable: same seed always produces the same value.
/// </summary>
public static class ObjectFactoryV2
{
    private const int DefaultMaxDepth = 6;

    // ── Public entry points ──────────────────────────────────────────────────────

    /// <summary>Create a populated instance of <typeparamref name="T"/> for the given seed.</summary>
    public static T Create<T>(int seed = 0) =>
        (T)Create(typeof(T), new Random(seed), 0)!;

    /// <summary>
    /// Create a populated instance of <paramref name="type"/> using <paramref name="rng"/>.
    /// Supports every scalar, T?, array, List, HashSet, IReadOnlyList, IReadOnlyCollection,
    /// ICollection, IList, ISet, IReadOnlySet, IEnumerable, ImmutableArray, ImmutableList,
    /// IImmutableList, ImmutableHashSet, IImmutableSet, Dictionary, IDictionary,
    /// IReadOnlyDictionary, ImmutableDictionary, IImmutableDictionary,
    /// and arbitrary class/struct/record types.
    /// </summary>
    public static object? Create(Type type, Random rng, int depth)
    {
        if (rng is null) throw new ArgumentNullException(nameof(rng));
        if (type is null) throw new ArgumentNullException(nameof(type));

        // Nullable<T> → unwrap and recurse
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return Create(underlying, rng, depth);

        // ── Scalars ──────────────────────────────────────────────────────────
        if (type == typeof(string))
            return "s" + rng.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(bool))   return rng.Next(0, 2) == 1;
        if (type == typeof(byte))   return (byte)rng.Next(1, 256);
        if (type == typeof(sbyte))  return (sbyte)rng.Next(1, 127);
        if (type == typeof(short))  return (short)rng.Next(1, short.MaxValue);
        if (type == typeof(ushort)) return (ushort)rng.Next(1, ushort.MaxValue);
        if (type == typeof(int))    return rng.Next(1, int.MaxValue);
        if (type == typeof(uint))   return (uint)rng.Next(1, int.MaxValue);
        // C9: occasionally emit out-of-int-range long values so the long→int CreateChecked
        // overflow path is exercised. 1-in-4 chance.
        if (type == typeof(long))
            return rng.Next(0, 4) == 0
                ? rng.NextInt64(long.MinValue, long.MaxValue)
                : (long)rng.Next(1, int.MaxValue);
        if (type == typeof(ulong))  return (ulong)rng.Next(1, int.MaxValue);
        if (type == typeof(float))  return (float)(rng.NextDouble() * 1000);
        if (type == typeof(double)) return rng.NextDouble() * 1000;
        if (type == typeof(decimal)) return (decimal)(rng.NextDouble() * 1000);
        if (type == typeof(char))   return (char)rng.Next('A', 'Z' + 1);
        if (type == typeof(Guid))
        {
            var b = new byte[16]; rng.NextBytes(b); return new Guid(b);
        }
        if (type == typeof(DateTime))
            return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 5_000_000));
        if (type == typeof(DateTimeOffset))
            return new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(rng.Next(0, 5_000_000));
        if (type == typeof(TimeSpan))
            return TimeSpan.FromSeconds(rng.Next(1, 5_000_000));

        // ── Enum ─────────────────────────────────────────────────────────────
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(rng.Next(values.Length));
        }

        // ── Array ─────────────────────────────────────────────────────────────
        if (type.IsArray)
        {
            var elemType = type.GetElementType()!;
            var n = depth >= DefaultMaxDepth ? 0 : rng.Next(1, 4);
            var array = Array.CreateInstance(elemType, n);
            for (var i = 0; i < n; i++)
                array.SetValue(Create(elemType, rng, depth + 1), i);
            return array;
        }

        if (type.IsGenericType)
        {
            var gtd = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();

            // ── List<T> ───────────────────────────────────────────────────────
            if (gtd == typeof(List<>))
                return MakeList(args[0], rng, depth);

            // ── HashSet<T> ────────────────────────────────────────────────────
            if (gtd == typeof(HashSet<>))
                return MakeHashSet(args[0], rng, depth);

            // ── IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyList<T>, IReadOnlyCollection<T>
            //    → materialise as List<T>
            if (gtd == typeof(IEnumerable<>) ||
                gtd == typeof(ICollection<>) ||
                gtd == typeof(IList<>) ||
                gtd == typeof(IReadOnlyList<>) ||
                gtd == typeof(IReadOnlyCollection<>))
                return MakeList(args[0], rng, depth);

            // ── ISet<T>, IReadOnlySet<T> → HashSet<T>
            if (gtd == typeof(ISet<>) || gtd == typeof(IReadOnlySet<>))
                return MakeHashSet(args[0], rng, depth);

            // ── ImmutableArray<T>
            if (gtd == typeof(ImmutableArray<>))
            {
                var elemType = args[0];
                var list = (IList)MakeList(elemType, rng, depth)!;
                // ImmutableArray.CreateRange<T>(IEnumerable<T>) — pick the correct single-arg overload
                var createRange = FindImmutableCreateRange(typeof(ImmutableArray), elemType);
                if (createRange is null)
                    return ImmutableArray<int>.Empty; // fallback (shouldn't happen)
                return createRange.Invoke(null, new object[] { list });
            }

            // ── ImmutableList<T>, IImmutableList<T>
            if (gtd == typeof(ImmutableList<>) ||
                (type.IsInterface && args.Length == 1 &&
                 typeof(IImmutableList<>).MakeGenericType(args[0]).IsAssignableFrom(type)))
            {
                var elemType = args[0];
                var list = (IList)MakeList(elemType, rng, depth)!;
                var createRange = FindImmutableCreateRange(typeof(ImmutableList), elemType);
                if (createRange is null) return ImmutableList<int>.Empty;
                return createRange.Invoke(null, new object[] { list });
            }

            // ── ImmutableHashSet<T>, IImmutableSet<T>
            if (gtd == typeof(ImmutableHashSet<>) ||
                (type.IsInterface && args.Length == 1 &&
                 typeof(IImmutableSet<>).MakeGenericType(args[0]).IsAssignableFrom(type)))
            {
                var elemType = args[0];
                var list = (IList)MakeList(elemType, rng, depth)!;
                var createRange = FindImmutableCreateRange(typeof(ImmutableHashSet), elemType);
                if (createRange is null) return ImmutableHashSet<int>.Empty;
                return createRange.Invoke(null, new object[] { list });
            }

            // ── Dictionary<K,V>
            if (gtd == typeof(Dictionary<,>))
                return MakeDictionary(args[0], args[1], rng, depth);

            // ── IDictionary<K,V>, IReadOnlyDictionary<K,V>
            if (gtd == typeof(IDictionary<,>) || gtd == typeof(IReadOnlyDictionary<,>))
                return MakeDictionary(args[0], args[1], rng, depth);

            // ── ImmutableDictionary<K,V>, IImmutableDictionary<K,V>
            if (gtd == typeof(ImmutableDictionary<,>) ||
                (type.IsInterface && args.Length == 2))
            {
                // Check for IImmutableDictionary interface
                var iimmutDictType = typeof(IImmutableDictionary<,>).MakeGenericType(args[0], args[1]);
                if (iimmutDictType.IsAssignableFrom(type) || gtd == typeof(ImmutableDictionary<,>))
                {
                    var plainDict = (IDictionary)MakeDictionary(args[0], args[1], rng, depth)!;
                    var builderMethod = typeof(ImmutableDictionary).GetMethods(BindingFlags.Public | BindingFlags.Static);
                    // Use ImmutableDictionary.CreateRange(IEnumerable<KVP>)
                    var kvpType = typeof(KeyValuePair<,>).MakeGenericType(args[0], args[1]);
                    var enumerableKvpType = typeof(IEnumerable<>).MakeGenericType(kvpType);
                    foreach (var m in builderMethod)
                    {
                        if (m.Name == "CreateRange" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2)
                        {
                            try
                            {
                                var concrete = m.MakeGenericMethod(args[0], args[1]);
                                // Build a list of KVPs
                                var kvpList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(kvpType))!;
                                foreach (DictionaryEntry entry in plainDict)
                                    kvpList.Add(Activator.CreateInstance(kvpType, entry.Key, entry.Value)!);
                                return concrete.Invoke(null, new object[] { kvpList });
                            }
                            catch (TargetInvocationException) { /* try next overload */ }
                            catch (ArgumentException) { /* try next overload */ }
                            catch (InvalidOperationException) { /* try next overload */ }
                        }
                    }
                    // Fallback: return the plain Dictionary
                    return plainDict;
                }
            }
        }

        // ── Interface or abstract → try to pick a concrete ──────────────────
        if (type.IsInterface || type.IsAbstract || depth >= DefaultMaxDepth)
            return type.IsValueType ? Activator.CreateInstance(type) : null;

        // ── Class / struct / record ──────────────────────────────────────────
        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor is null)
        {
            // Try first available public ctor (for records)
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            var pc = ctors[0];
            var parms = pc.GetParameters();
            var pvals = new object?[parms.Length];
            for (var i = 0; i < parms.Length; i++)
                pvals[i] = Create(parms[i].ParameterType, rng, depth + 1);
            return pc.Invoke(pvals);
        }

        var instance = ctor.Invoke(null);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanWrite && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0)
                p.SetValue(instance, Create(p.PropertyType, rng, depth + 1));
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.IsInitOnly)
                f.SetValue(instance, Create(f.FieldType, rng, depth + 1));
        }
        return instance;
    }

    // ── Graph fixture builders ───────────────────────────────────────────────────

    /// <summary>
    /// Build a self-loop graph fixture using reflection.
    /// The returned type must have a settable property named <paramref name="selfPropName"/>
    /// of the same type.
    /// </summary>
    public static object MakeSelfLoop(Type nodeType, string selfPropName, Random rng)
    {
        if (nodeType is null) throw new ArgumentNullException(nameof(nodeType));
        var node = Create(nodeType, rng, 0)!;
        var prop = nodeType.GetProperty(selfPropName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"Property '{selfPropName}' not found on {nodeType.Name}");
        prop.SetValue(node, node);
        return node;
    }

    /// <summary>
    /// Build a 2-node mutual cycle: a.Next=b, b.Next=a.
    /// Returns (a, b).
    /// </summary>
    public static (object A, object B) MakeTwoNodeCycle(Type nodeType, string nextPropName, Random rng)
    {
        if (nodeType is null) throw new ArgumentNullException(nameof(nodeType));
        var a = Create(nodeType, rng, 0)!;
        var b = Create(nodeType, rng, 0)!;
        var prop = nodeType.GetProperty(nextPropName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"Property '{nextPropName}' not found on {nodeType.Name}");
        prop.SetValue(a, b);
        prop.SetValue(b, a);
        return (a, b);
    }

    /// <summary>
    /// Build the owner graph fixture A→B, B⇄C, C⇄D, B⇄D using four types.
    /// Each type is constructed via <see cref="Create"/>; then back-edges are wired.
    /// Returns (a, b, c, d).
    /// </summary>
    public static (object A, object B, object C, object D) MakeOwnerGraph(
        Type typeA, Type typeB, Type typeC, Type typeD,
        string bPropOnA,
        string cPropOnB, string dPropOnB,
        string bPropOnC, string dPropOnC,
        string bPropOnD, string cPropOnD,
        Random rng)
    {
        var a = Create(typeA, rng, 0)!;
        var b = Create(typeB, rng, 0)!;
        var c = Create(typeC, rng, 0)!;
        var d = Create(typeD, rng, 0)!;

        void Set(object target, string propName, object value)
        {
            var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"Property '{propName}' not found on {target.GetType().Name}");
            p.SetValue(target, value);
        }

        Set(a, bPropOnA, b);
        Set(b, cPropOnB, c);
        Set(b, dPropOnB, d);
        Set(c, bPropOnC, b);
        Set(c, dPropOnC, d);
        Set(d, bPropOnD, b);
        Set(d, cPropOnD, c);
        return (a, b, c, d);
    }

    /// <summary>
    /// Build a diamond graph: root.Left = root.Right = sharedChild.
    /// </summary>
    public static (object Root, object SharedChild) MakeDiamond(
        Type rootType, Type childType,
        string leftProp, string rightProp,
        Random rng)
    {
        var root = Create(rootType, rng, 0)!;
        var child = Create(childType, rng, 0)!;

        void Set(object target, string propName, object? value)
        {
            var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"Property '{propName}' not found on {target.GetType().Name}");
            p.SetValue(target, value);
        }

        Set(root, leftProp, child);
        Set(root, rightProp, child);
        return (root, child);
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private static object MakeList(Type elemType, Random rng, int depth)
    {
        var listType = typeof(List<>).MakeGenericType(elemType);
        var list = (IList)Activator.CreateInstance(listType)!;
        var n = depth >= DefaultMaxDepth ? 0 : rng.Next(1, 4);
        for (var i = 0; i < n; i++)
            list.Add(Create(elemType, rng, depth + 1));
        return list;
    }

    private static object MakeHashSet(Type elemType, Random rng, int depth)
    {
        var setType = typeof(HashSet<>).MakeGenericType(elemType);
        var add = setType.GetMethod("Add")!;
        var set = Activator.CreateInstance(setType)!;
        var n = depth >= DefaultMaxDepth ? 0 : rng.Next(1, 4);
        for (var i = 0; i < n; i++)
            add.Invoke(set, new[] { Create(elemType, rng, depth + 1) });
        return set;
    }

    private static object MakeDictionary(Type keyType, Type valType, Random rng, int depth)
    {
        var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valType);
        var dict = (IDictionary)Activator.CreateInstance(dictType)!;
        var n = depth >= DefaultMaxDepth ? 0 : rng.Next(1, 3); // smaller to avoid duplicate key conflicts
        var seen = new HashSet<object>();
        for (var i = 0; i < n; i++)
        {
            var key = Create(keyType, rng, depth + 1);
            if (key is null || seen.Contains(key)) continue;
            seen.Add(key);
            var val = Create(valType, rng, depth + 1);
            dict[key] = val;
        }
        // Guarantee at least one entry for non-depth-capped cases
        if (dict.Count == 0 && depth < DefaultMaxDepth)
        {
            var key = Create(keyType, rng, depth + 1);
            if (key is not null)
                dict[key] = Create(valType, rng, depth + 1);
        }
        return dict;
    }

    /// <summary>
    /// Locate the single-arg <c>CreateRange&lt;T&gt;(IEnumerable&lt;T&gt;)</c> overload on
    /// the given immutable factory class (ImmutableArray / ImmutableList / ImmutableHashSet).
    /// Returns null if not found.
    /// </summary>
    private static MethodInfo? FindImmutableCreateRange(Type factoryType, Type elemType)
    {
        foreach (var m in factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != "CreateRange" || !m.IsGenericMethodDefinition) continue;
            var gargs = m.GetGenericArguments();
            if (gargs.Length != 1) continue;
            var parms = m.GetParameters();
            if (parms.Length != 1) continue;
            // Verify the single parameter is IEnumerable<T>
            var paramType = parms[0].ParameterType;
            if (!paramType.IsGenericType) continue;
            if (paramType.GetGenericTypeDefinition() != typeof(IEnumerable<>)) continue;
            return m.MakeGenericMethod(elemType);
        }
        return null;
    }
}
