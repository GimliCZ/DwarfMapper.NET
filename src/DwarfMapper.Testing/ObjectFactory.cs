// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace DwarfMapper.Testing;

/// <summary>Builds deterministically-seeded, fully-populated instances for fixtures and fuzzing.</summary>
public static class ObjectFactory
{
    private const int MaxDepth = 6;

    /// <summary>Create a populated instance of <typeparamref name="T" /> for the given seed.</summary>
    public static T Create<T>(int seed = 0)
    {
        return (T)Create(typeof(T), new Random(seed), 0)!;
    }

    /// <summary>Create a populated instance of <paramref name="type" /> using <paramref name="rng" />.</summary>
    public static object? Create(Type type, Random rng, int depth)
    {
        if (rng is null) throw new ArgumentNullException(nameof(rng));
        if (type is null) throw new ArgumentNullException(nameof(type));

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return Create(underlying, rng, depth);

        if (type == typeof(string)) return "s" + rng.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return rng.Next(0, 2) == 1;
        if (type == typeof(byte)) return (byte)rng.Next(1, 256);
        if (type == typeof(sbyte)) return (sbyte)rng.Next(1, 127);
        if (type == typeof(short)) return (short)rng.Next(1, short.MaxValue);
        if (type == typeof(ushort)) return (ushort)rng.Next(1, ushort.MaxValue);
        if (type == typeof(int)) return rng.Next(1, int.MaxValue);
        if (type == typeof(uint)) return (uint)rng.Next(1, int.MaxValue);
        if (type == typeof(long))
            // C9: occasionally emit out-of-int-range values so the long→int CreateChecked overflow
            // path is exercised by fuzz. 1-in-4 chance of a value that overflows int.
            return rng.Next(0, 4) == 0
                ? rng.NextInt64(long.MinValue, long.MaxValue)
                : rng.Next(1, int.MaxValue);
        if (type == typeof(ulong)) return (ulong)rng.Next(1, int.MaxValue);
        if (type == typeof(float)) return (float)(rng.NextDouble() * 1000);
        if (type == typeof(double)) return rng.NextDouble() * 1000;
        if (type == typeof(decimal)) return (decimal)(rng.NextDouble() * 1000);
        if (type == typeof(char)) return (char)rng.Next('A', 'Z' + 1);
        if (type == typeof(Guid))
        {
            var b = new byte[16];
            rng.NextBytes(b);
            return new Guid(b);
        }

        if (type == typeof(DateTime))
            return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 5_000_000));
        if (type == typeof(DateTimeOffset))
            return new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(rng.Next(0, 5_000_000));
        if (type == typeof(TimeSpan)) return TimeSpan.FromSeconds(rng.Next(1, 5_000_000));
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(rng.Next(values.Length));
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var n = depth >= MaxDepth ? 0 : rng.Next(1, 4);
            var array = Array.CreateInstance(elementType, n);
            for (var i = 0; i < n; i++) array.SetValue(Create(elementType, rng, depth + 1), i);
            return array;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(type)!;
            var n = depth >= MaxDepth ? 0 : rng.Next(1, 4);
            for (var i = 0; i < n; i++) list.Add(Create(elementType, rng, depth + 1));
            return list;
        }

        // Sets and the collection INTERFACES.
        //
        // Without this, two whole families of member were never actually exercised by the fuzzers:
        //   * HashSet<T>/ISet<T> fell through to the parameterless-ctor path below, and since a set exposes no
        //     settable properties it was populated with nothing — every set in every fuzz run was EMPTY.
        //   * every interface-typed collection (IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyList<T>,
        //     IReadOnlyCollection<T>) hit the `IsInterface -> null` bail-out below and was silently NULL.
        // So the generated "covers every supported linkage" space quietly excluded them. That is how an
        // IEnumerable<T>-target aliasing bug survived a fuzz + matrix suite: the shape was never populated.
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var elementType = type.GetGenericArguments()[0];
            var n = depth >= MaxDepth ? 0 : rng.Next(1, 4);

            if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IList<>) ||
                def == typeof(IReadOnlyCollection<>) || def == typeof(IReadOnlyList<>))
            {
                var backing = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                for (var i = 0; i < n; i++) backing.Add(Create(elementType, rng, depth + 1));
                return backing;
            }

            if (def == typeof(HashSet<>) || def == typeof(ISet<>) || def == typeof(IReadOnlySet<>))
            {
                var setType = typeof(HashSet<>).MakeGenericType(elementType);
                var set = Activator.CreateInstance(setType)!;
                var add = setType.GetMethod("Add", new[] { elementType })!;
                for (var i = 0; i < n; i++) add.Invoke(set, new[] { Create(elementType, rng, depth + 1) });
                return set;
            }

            // Dictionaries had the same defect as sets: a Dictionary<K,V> member reached the
            // parameterless-ctor path and, exposing no settable properties, came back EMPTY — so the whole
            // DictionaryConverter was being fuzzed against zero entries. The interface forms were NULL.
            if (type.GetGenericArguments().Length == 2 &&
                (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) ||
                 def == typeof(IReadOnlyDictionary<,>)))
            {
                var keyType = type.GetGenericArguments()[0];
                var valueType = type.GetGenericArguments()[1];
                var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                var dict = (IDictionary)Activator.CreateInstance(dictType)!;
                for (var i = 0; i < n; i++)
                {
                    var key = Create(keyType, rng, depth + 1);
                    if (key is null || dict.Contains(key)) continue; // keys must be unique and non-null
                    dict[key] = Create(valueType, rng, depth + 1);
                }

                return dict;
            }
        }

        if (type.IsInterface || type.IsAbstract || depth >= MaxDepth)
            return type.IsValueType ? Activator.CreateInstance(type) : null;

        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor is null)
        {
            // No parameterless constructor. Before, this simply returned null — which meant every POSITIONAL
            // RECORD and every ctor-only DTO was silently null in every fuzz run, so the constructor-mapping
            // path was never exercised by the value oracles at all. Construct it properly instead: take the
            // ctor with the fewest parameters and generate an argument for each.
            var candidate = type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (candidate is null)
                return type.IsValueType ? Activator.CreateInstance(type) : null;

            var parameters = candidate.GetParameters();
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                args[i] = Create(parameters[i].ParameterType, rng, depth + 1);

            return candidate.Invoke(args);
        }

        var instance = ctor.Invoke(null);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanWrite && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0)
                p.SetValue(instance, Create(p.PropertyType, rng, depth + 1));

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (!f.IsInitOnly)
                f.SetValue(instance, Create(f.FieldType, rng, depth + 1));

        return instance;
    }
}
