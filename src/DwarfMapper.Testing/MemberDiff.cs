// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Testing;

/// <summary>A single difference found by <see cref="StructuralComparer"/>.</summary>
public sealed class MemberDiff
{
    /// <summary>Creates a member difference.</summary>
    public MemberDiff(string path, string? expected, string? actual)
    {
        Path = path;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>Member path, e.g. <c>Order.Lines[2].Price</c>.</summary>
    public string Path { get; }

    /// <summary>Rendered expected value.</summary>
    public string? Expected { get; }

    /// <summary>Rendered actual value.</summary>
    public string? Actual { get; }
}
