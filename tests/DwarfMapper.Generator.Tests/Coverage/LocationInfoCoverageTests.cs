// SPDX-License-Identifier: GPL-2.0-only
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

// Coverage suite for DwarfMapper.Generator.Diagnostics.LocationInfo.
// Techniques: unit, adversary, defensive, deterministic, fixture.
namespace DwarfMapper.Generator.Tests.Coverage;

/// <summary>
/// Tests for <c>LocationInfo</c> value record — covers both branches of <c>From()</c>
/// plus value equality and <c>ToLocation()</c>.
/// </summary>
public class LocationInfoCoverageTests
{
    // ─── From(Location.None) — the no-location branch ─────────────────────────

    [Fact]
    public void From_Location_None_returns_null()
    {
        var result = LocationInfo.From(Location.None);
        Assert.Null(result);
    }

    [Fact]
    public void From_null_location_returns_null()
    {
        // The From() implementation guards: if (location is null || location.SourceTree is null)
        LocationInfo? result = LocationInfo.From(null!);
        Assert.Null(result);
    }

    // ─── From(real location) — the file/span branch ────────────────────────────

    [Fact]
    public void From_real_location_returns_non_null_with_file_and_span()
    {
        var (location, expectedFile, expectedSpan) = BuildLocation(
            "namespace Test { class C { } }", "TestFile.cs");

        var info = LocationInfo.From(location);

        Assert.NotNull(info);
        Assert.Equal(expectedFile, info!.FilePath);
        Assert.Equal(expectedSpan, info.TextSpan);
    }

    [Fact]
    public void From_real_location_LineSpan_is_populated()
    {
        var (location, _, _) = BuildLocation(
            "namespace Test { class C { } }", "TestFile2.cs");

        var info = LocationInfo.From(location);

        Assert.NotNull(info);
        Assert.True(info!.LineSpan.Start.Line >= 0);
        Assert.True(info.LineSpan.Start.Character >= 0);
    }

    // ─── ToLocation() ─────────────────────────────────────────────────────────

    [Fact]
    public void ToLocation_returns_non_null_location()
    {
        // LocationInfo.ToLocation() calls Location.Create(FilePath, TextSpan, LineSpan).
        // That creates a file-path-based location (no SourceTree reference), so
        // SourceTree is null on the returned location, but GetMappedLineSpan().Path
        // and SourceSpan are set.
        var info = new LocationInfo("RoundTrip.cs", new TextSpan(6, 4),
            new LinePositionSpan(new LinePosition(0, 6), new LinePosition(0, 10)));

        var loc = info.ToLocation();

        Assert.NotNull(loc);
        Assert.Equal(new TextSpan(6, 4), loc.SourceSpan);
        // The path is accessible via GetMappedLineSpan (file-based location).
        Assert.Equal("RoundTrip.cs", loc.GetMappedLineSpan().Path);
    }

    // ─── Value equality ────────────────────────────────────────────────────────

    [Fact]
    public void Two_LocationInfo_with_same_values_are_equal()
    {
        var span1 = new TextSpan(10, 5);
        var lineSpan1 = new LinePositionSpan(new LinePosition(1, 2), new LinePosition(1, 7));

        var a = new LocationInfo("file.cs", span1, lineSpan1);
        var b = new LocationInfo("file.cs", span1, lineSpan1);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Two_LocationInfo_with_different_file_are_not_equal()
    {
        var span = new TextSpan(0, 1);
        var lineSpan = new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1));

        var a = new LocationInfo("fileA.cs", span, lineSpan);
        var b = new LocationInfo("fileB.cs", span, lineSpan);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Two_LocationInfo_with_different_span_are_not_equal()
    {
        var lineSpan = new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1));

        var a = new LocationInfo("f.cs", new TextSpan(0, 1), lineSpan);
        var b = new LocationInfo("f.cs", new TextSpan(5, 3), lineSpan);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Two_LocationInfo_with_different_line_span_are_not_equal()
    {
        var span = new TextSpan(0, 1);
        var ls1 = new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1));
        var ls2 = new LinePositionSpan(new LinePosition(1, 0), new LinePosition(1, 1));

        var a = new LocationInfo("f.cs", span, ls1);
        var b = new LocationInfo("f.cs", span, ls2);

        Assert.NotEqual(a, b);
    }

    // ─── Adversary: location with edge-case spans ─────────────────────────────

    [Fact]
    public void From_location_with_zero_length_span_does_not_throw()
    {
        var source = "class C{}";
        var (location, _, _) = BuildLocation(source, "Empty.cs", startPos: 0, length: 0);
        // No exception expected; From() should not crash on a zero-length span.
        var ex = Record.Exception(() => LocationInfo.From(location));
        Assert.Null(ex);
    }

    [Fact]
    public void From_location_at_end_of_file()
    {
        var source = "namespace T { class C { } }";
        var (location, _, _) = BuildLocation(source, "EndOfFile.cs", startPos: source.Length - 1, length: 1);
        var info = LocationInfo.From(location);
        Assert.NotNull(info);
    }

    // ─── Defensive: From always safe ──────────────────────────────────────────

    [Fact]
    public void From_Location_Create_with_empty_path_does_not_throw()
    {
        var source = "class C {}";
        var tree = CSharpSyntaxTree.ParseText(source, path: "");
        var span = new TextSpan(0, 5);
        var loc = Location.Create(tree, span);
        var info = LocationInfo.From(loc);
        Assert.NotNull(info);
        Assert.Equal("", info!.FilePath);
    }

    // ─── Deterministic: same info created twice equals ────────────────────────

    [Fact]
    public void LocationInfo_created_twice_from_same_values_is_equal()
    {
        var (location, _, _) = BuildLocation("namespace X { class Y {} }", "Deterministic.cs");
        var a = LocationInfo.From(location);
        var b = LocationInfo.From(location);
        Assert.Equal(a, b);
    }

    // ─── Fixture: record ToString contains FilePath ────────────────────────────

    [Fact]
    public void LocationInfo_ToString_contains_FilePath()
    {
        var info = new LocationInfo("src/File.cs", new TextSpan(0, 1),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1)));
        var str = info.ToString();
        Assert.Contains("src/File.cs", str, System.StringComparison.Ordinal);
    }

    // ─── IEquatable via record: Equals(null) returns false ───────────────────

    [Fact]
    public void Equals_null_returns_false()
    {
        var a = new LocationInfo("f.cs", new TextSpan(0, 1),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 1)));
        Assert.False(a.Equals((LocationInfo?)null));
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a real Roslyn Location from a source snippet and file name.
    /// </summary>
    private static (Location Location, string FilePath, TextSpan Span)
        BuildLocation(string source, string filePath, int startPos = 6, int length = 4)
    {
        if (startPos >= source.Length) startPos = 0;
        if (startPos + length > source.Length) length = source.Length - startPos;
        if (length < 0) length = 0;

        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var span = new TextSpan(startPos, length);
        var location = Location.Create(tree, span);
        return (location, filePath, span);
    }
}
