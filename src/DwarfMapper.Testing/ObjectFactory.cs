// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Testing;

/// <summary>Builds deterministically-seeded, fully-populated instances for fixtures and fuzzing.</summary>
public static class ObjectFactory
{
    private const int MaxDepth = 6;

    /// <summary>Create a populated instance of <typeparamref name="T"/> for the given seed.</summary>
    public static T Create<T>(int seed = 0) => (T)Create(typeof(T), new Random(seed), 0)!;

    /// <summary>Create a populated instance of <paramref name="type"/> using <paramref name="rng"/>.</summary>
    public static object? Create(Type type, Random rng, int depth)
    {
        if (rng is null)
        {
            throw new ArgumentNullException(nameof(rng));
        }
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            return Create(underlying, rng, depth);
        }

        if (type == typeof(string))
        {
            return "s" + rng.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
        }
        if (type == typeof(bool))
        {
            return rng.Next(0, 2) == 1;
        }
        if (type == typeof(byte))
        {
            return (byte)rng.Next(1, 256);
        }
        if (type == typeof(sbyte))
        {
            return (sbyte)rng.Next(1, 127);
        }
        if (type == typeof(short))
        {
            return (short)rng.Next(1, short.MaxValue);
        }
        if (type == typeof(ushort))
        {
            return (ushort)rng.Next(1, ushort.MaxValue);
        }
        if (type == typeof(int))
        {
            return rng.Next(1, int.MaxValue);
        }
        if (type == typeof(uint))
        {
            return (uint)rng.Next(1, int.MaxValue);
        }
        if (type == typeof(long))
        {
            return (long)rng.Next(1, int.MaxValue);
        }
        if (type == typeof(ulong))
        {
            return (ulong)rng.Next(1, int.MaxValue);
        }
        if (type == typeof(float))
        {
            return (float)(rng.NextDouble() * 1000);
        }
        if (type == typeof(double))
        {
            return rng.NextDouble() * 1000;
        }
        if (type == typeof(decimal))
        {
            return (decimal)(rng.NextDouble() * 1000);
        }
        if (type == typeof(char))
        {
            return (char)rng.Next('A', 'Z' + 1);
        }
        if (type == typeof(Guid))
        {
            var b = new byte[16];
            rng.NextBytes(b);
            return new Guid(b);
        }
        if (type == typeof(DateTime))
        {
            return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 5_000_000));
        }
        if (type == typeof(DateTimeOffset))
        {
            return new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(rng.Next(0, 5_000_000));
        }
        if (type == typeof(TimeSpan))
        {
            return TimeSpan.FromSeconds(rng.Next(1, 5_000_000));
        }
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
            for (var i = 0; i < n; i++)
            {
                array.SetValue(Create(elementType, rng, depth + 1), i);
            }
            return array;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = type.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(type)!;
            var n = depth >= MaxDepth ? 0 : rng.Next(1, 4);
            for (var i = 0; i < n; i++)
            {
                list.Add(Create(elementType, rng, depth + 1));
            }
            return list;
        }

        if (type.IsInterface || type.IsAbstract || depth >= MaxDepth)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor is null)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        var instance = ctor.Invoke(null);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanWrite && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0)
            {
                p.SetValue(instance, Create(p.PropertyType, rng, depth + 1));
            }
        }
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.IsInitOnly)
            {
                f.SetValue(instance, Create(f.FieldType, rng, depth + 1));
            }
        }
        return instance;
    }
}
