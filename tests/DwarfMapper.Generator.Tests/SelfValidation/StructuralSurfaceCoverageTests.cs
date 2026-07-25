// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Completeness gates for two shipping surfaces that are neither attributes, enums, nor diagnostics — so
///     every existing scan (which reflects only over those three taxonomies) looks straight past them:
///     <list type="bullet">
///         <item>the signature-triggered map MODES on <c>MapMethodModel</c> (projection / span / async-stream),
///         recognised by method-signature shape rather than any attribute; and</item>
///         <item>the fluent <c>MapConfig&lt;S,T&gt;</c> configuration methods.</item>
///     </list>
///     Both were fully covered by hand-written tests, but by nothing that FAILS when a new mode or method ships
///     untested — the exact structural gap that let earlier holes hide. These make that impossible.
/// </summary>
public sealed class StructuralSurfaceCoverageTests
{
    private static readonly Assembly GeneratorAssembly = typeof(DwarfGenerator).Assembly;

    // The signature-triggered map modes, each mapped to the test files that must exercise it. There is no
    // reflective predicate that separates a "map mode" from an internal codegen flag (both are just bool record
    // params), so this classification is deliberate and hand-kept — the count tripwire below is what forces it
    // to stay honest when MapMethodModel grows.
    private static readonly (string Flag, string[] AnchorFiles)[] SignatureTriggeredModes =
    {
        ("IsProjection", new[] { "ProjectionTests.cs", "ProjectionRuntimeTests.cs" }),
        ("IsSpanMap", new[] { "SpanMapGeneratorTests.cs", "SpanMapRuntimeTests.cs" }),
        ("IsAsyncStreamMap", new[] { "AsyncStreamMapGeneratorTests.cs", "AsyncStreamMapRuntimeTests.cs" }),
    };

    // Reflected count of MapMethodModel's bool flags at the time this gate was written. A change here means a
    // flag was added or removed: if it is a NEW SIGNATURE-TRIGGERED MODE (a map shape recognised from the method
    // signature, like IsSpanMap), add it to SignatureTriggeredModes with anchor tests; if it is an internal
    // codegen flag, just bump this baseline. Either way the change forces a conscious coverage decision — a new
    // user-facing map mode can no longer ship dark. Same spirit as the T4 allowlist-size gates.
    private const int MapMethodModelBoolFlagBaseline = 15;

    private static PropertyInfo[] MapMethodModelBoolFlags()
    {
        var model = GeneratorAssembly.GetType("DwarfMapper.Generator.Model.MapMethodModel");
        Assert.True(model is not null,
            "MapMethodModel not found — the model moved; this self-validation must be updated.");
        return model!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))
            .ToArray();
    }

    [Fact]
    public void MapMethodModel_bool_flag_set_is_unchanged_or_a_new_mode_is_classified()
    {
        var flags = MapMethodModelBoolFlags();
        Assert.True(flags.Length == MapMethodModelBoolFlagBaseline,
            $"MapMethodModel now has {flags.Length} bool flags, baseline is {MapMethodModelBoolFlagBaseline}: "
            + string.Join(", ", flags.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal))
            + ". If you added a SIGNATURE-TRIGGERED MAP MODE (recognised from the method signature, like a span "
            + "or async-stream map), add it to SignatureTriggeredModes with generator+runtime anchor tests. If it "
            + "is an internal codegen flag, bump MapMethodModelBoolFlagBaseline. A new map mode must not ship dark.");
    }

    [Fact]
    public void Every_signature_triggered_mode_flag_exists_and_is_anchored_by_real_tests()
    {
        var flagNames = MapMethodModelBoolFlags().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var testRoots = new[]
        {
            Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests"),
            Path.Combine(RepoRoot(), "tests", "DwarfMapper.IntegrationTests"),
        };

        foreach (var (flag, anchorFiles) in SignatureTriggeredModes)
        {
            Assert.True(flagNames.Contains(flag),
                $"Signature-triggered mode flag '{flag}' no longer exists on MapMethodModel — it was renamed or "
                + "removed. Update SignatureTriggeredModes.");

            foreach (var file in anchorFiles)
            {
                var path = testRoots.Select(r => Path.Combine(r, file)).FirstOrDefault(File.Exists);
                Assert.True(path is not null,
                    $"Coverage anchor '{file}' for mode '{flag}' not found — the mode's dedicated tests moved or "
                    + "were deleted, leaving the mode unexercised. Restore them or update the anchor.");
                var text = File.ReadAllText(path!);
                Assert.True(text.Contains("[Fact]", StringComparison.Ordinal)
                            || text.Contains("[Theory]", StringComparison.Ordinal),
                    $"Coverage anchor '{file}' for mode '{flag}' contains no [Fact]/[Theory] — it is hollow.");
            }
        }
    }

    [Fact]
    public void Every_public_MapConfig_method_is_exercised_by_a_MapConfig_test()
    {
        // MapConfig<S,T> is a plain sealed class — invisible to Scan4 (attributes) and Scan5 (enums). A new fluent
        // method could ship with no test. Require every public method name to appear in the MapConfig test files.
        var methodNames = typeof(MapConfig<,>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // drop property/operator accessors, if any are ever added
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(methodNames.Count >= 7,
            $"Only {methodNames.Count} public MapConfig methods reflected — this gate would be near-vacuous. "
            + "MapConfig moved or its shape changed.");

        var corpus = string.Concat(
            new[]
                {
                    Path.Combine(RepoRoot(), "tests", "DwarfMapper.Generator.Tests"),
                    Path.Combine(RepoRoot(), "tests", "DwarfMapper.IntegrationTests"),
                }
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*MapConfig*.cs", SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        Assert.False(string.IsNullOrEmpty(corpus),
            "No *MapConfig*.cs test files found — the corpus scan would be vacuous.");

        // Each method is called as `.Name(` or `.Name<` on a MapConfig instance in its tests.
        var missing = methodNames
            .Where(n => !corpus.Contains($".{n}(", StringComparison.Ordinal)
                        && !corpus.Contains($".{n}<", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "Public MapConfig method(s) with no call in any *MapConfig*.cs test: " + string.Join(", ", missing)
            + ". Add a test that configures a mapping using the method.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
