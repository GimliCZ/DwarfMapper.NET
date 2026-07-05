// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;

namespace DwarfMapper.Generator.Collections;

/// <summary>
///     An array wrapper with value (structural) equality, so it can be used inside
///     incremental-generator models without defeating caching.
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[]? array)
    {
        _array = array;
    }

    public int Count => _array?.Length ?? 0;

    public T this[int index] => _array![index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array is null || other._array is null) return _array is null && other._array is null;

        if (_array.Length != other._array.Length) return false;

        for (var i = 0; i < _array.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(_array[i], other._array[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (_array is null) return 0;

        var hash = 17;
        foreach (var item in _array) hash = hash * 31 + (item?.GetHashCode() ?? 0);

        return hash;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Factory helpers for <see cref="EquatableArray{T}" />.</summary>
public static class EquatableArray
{
    public static EquatableArray<T> From<T>(IEnumerable<T> items)
        where T : IEquatable<T>
    {
        return new EquatableArray<T>(items.ToArray());
    }
}
