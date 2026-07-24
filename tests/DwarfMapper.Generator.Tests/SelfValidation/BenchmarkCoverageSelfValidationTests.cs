// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Ties the benchmark suite's shape coverage to the combinatorial schema's shape axis, so "we benchmark the
///     types we support" is a checked property rather than a hope.
///     <para>
///     The correctness suites cross 32 depth-one shapes; the benchmark suite measures a handful. That is a
///     defensible trade — a full mirror is ~973 cases and hours of wall-clock — but it must be a DECLARED trade.
///     ISSUE-019 is the cautionary case: an unknown-count source into an array target allocated two buffers on
///     every map for as long as the project existed, and no benchmark covered the shape, so nothing showed it.
///     </para>
///     <para>
///     Every shape must therefore be either mapped to a benchmark category or exempted WITH A REASON. Adding a
///     shape to <c>CombinatorialSchema.DepthOneShapes</c> fails this test until someone states which it is.
///     </para>
/// </summary>
public class BenchmarkCoverageSelfValidationTests
{
    /// <summary>
    ///     Shape → the benchmark category that measures it, or null to exempt. The exemption reasons are the
    ///     benchmark backlog: they are deliberately verbose, because a bare null here would silently become
    ///     "nobody ever looked at this shape".
    /// </summary>
    private static readonly Dictionary<string, string?> ShapeToBenchmarkCategory = new(StringComparer.Ordinal)
    {
        // ── Measured today ────────────────────────────────────────────────────────────────────────────
        ["raw"] = "Flat",
        ["array"] = "Array",
        ["List"] = "List",
        ["IEnumerable"] = "Seq", // ISSUE-019: unknown-count source, added only after the bug was found
        ["DictStringKey"] = "Dict",
        ["nested_object"] = "Nested",

        // ── Not measured. Each line is a real gap, not a decision that the shape does not matter. ──────
        // Cheap scalar variations: the mapping cost is a field copy, so a benchmark would measure the
        // harness rather than the mapper.
        ["nullable"] = null,
        ["nullable_ref"] = null,

        // The nullability MISMATCH is the commonest real DTO shape and carries a real null-check cost on the
        // measured path. Worth benchmarking; simply not done yet.
        ["nullable_ref_mismatch"] = null,

        // Collection families that all go through the same emitted fill loops as List/array, but with
        // different allocation strategies (pre-sized vs builder vs frozen). ISSUE-019 lived in exactly this
        // blind spot, so "similar to List" is not evidence they are free.
        ["IReadOnlyList"] = null,
        ["ICollection"] = null,
        ["IList"] = null,
        ["IReadOnlyCollection"] = null,
        ["HashSet"] = null,
        ["ISet"] = null,
        ["IReadOnlySet"] = null,
        ["Queue"] = null,
        ["Stack"] = null,

        // Immutable targets build through a Builder and then freeze — a materially different allocation
        // profile from List, and entirely unmeasured.
        ["ImmutableArray"] = null,
        ["ImmutableList"] = null,
        ["IImmutableList"] = null,
        ["ImmutableHashSet"] = null,
        ["IImmutableSet"] = null,

        // Dictionary variants beyond the single Dictionary<string,B> case.
        ["DictStringValue"] = null,
        ["IDict"] = null,
        ["IReadOnlyDict"] = null,
        ["ImmutableDict"] = null,
        ["IImmutableDict"] = null,

        // Shapes with no meaningful competitor comparison (Mapperly/Mapster/AutoMapper differ in support), so
        // they would be DwarfMapper-only coverage benchmarks.
        ["tuple"] = null,
        ["generic_box"] = null,
        ["record_type"] = null,
        ["polymorphic_dispatch"] = null
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }

    /// <summary>Reads the shape axis from the schema source, so the two cannot drift apart silently.</summary>
    private static List<string> DepthOneShapes()
    {
        var path = Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests", "Fuzzing",
            "CombinatorialSchema.cs");
        Assert.True(File.Exists(path), $"CombinatorialSchema.cs not found at {path}.");
        var text = File.ReadAllText(path);

        var start = text.IndexOf("DepthOneShapes =", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find DepthOneShapes in CombinatorialSchema.cs — the ratchet is blind.");
        var end = text.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the end of the DepthOneShapes array.");

        var body = text.Substring(start, end - start);
        var shapes = Regex.Matches(body, "\"([A-Za-z_]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        // A parse that silently returns nothing would make every assertion below vacuous — the exact defect
        // species this repo keeps finding. 20 is comfortably below the current 32 and above any plausible
        // partial parse.
        Assert.True(shapes.Count >= 20,
            $"Parsed only {shapes.Count} shapes from CombinatorialSchema.cs; the extraction has broken and this "
            + "ratchet would pass while checking nothing.");
        return shapes;
    }

    [Fact]
    public void Every_combinatorial_shape_is_benchmarked_or_explicitly_exempted()
    {
        var undeclared = DepthOneShapes()
            .Where(s => !ShapeToBenchmarkCategory.ContainsKey(s))
            .ToList();

        Assert.True(undeclared.Count == 0,
            "These shapes are crossed by the combinatorial suite but are neither benchmarked nor exempted:\n  "
            + string.Join("\n  ", undeclared)
            + "\nAdd each to ShapeToBenchmarkCategory: a category name if a benchmark measures it, or null with "
            + "a reason. Silence here would mean nobody ever decided.");
    }

    [Fact]
    public void Declared_benchmark_categories_actually_exist_in_the_benchmark_suite()
    {
        var program = Path.Combine(RepoRoot(), "benchmarks", "DwarfMapper.Benchmarks", "Program.cs");
        Assert.True(File.Exists(program), $"Benchmark Program.cs not found at {program}.");
        var text = File.ReadAllText(program);

        var declared = Regex.Matches(text, @"BenchmarkCategory\(""([A-Za-z]+)""\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(declared.Count >= 5,
            $"Found only {declared.Count} BenchmarkCategory declarations — the scan has broken.");

        var missing = ShapeToBenchmarkCategory
            .Where(kv => kv.Value is not null && !declared.Contains(kv.Value))
            .Select(kv => $"{kv.Key} -> {kv.Value}")
            .ToList();

        Assert.True(missing.Count == 0,
            "These shapes claim a benchmark category that no benchmark declares — the coverage claim is false:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    ///     Records the size of the gap. Not a failure — a measurement, so the number is visible in the suite
    ///     rather than buried in a document nobody re-reads. Tighten the bound as coverage improves.
    /// </summary>
    [Fact]
    public void Benchmark_shape_coverage_does_not_silently_regress()
    {
        var covered = ShapeToBenchmarkCategory.Count(kv => kv.Value is not null);

        Assert.True(covered >= 6,
            $"Benchmark shape coverage dropped to {covered} shapes; it was 6. A benchmark was deleted or a "
            + "category renamed — restore it rather than lowering this floor.");
    }
}
