// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DwarfMapper.Generator.Diagnostics;

/// <summary>Value-equatable replacement for <see cref="Location"/> in pipeline models.</summary>
public sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location location)
    {
        if (location is null || location.SourceTree is null)
        {
            return null;
        }

        return new LocationInfo(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }
}
