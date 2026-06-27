// SPDX-License-Identifier: GPL-2.0-only

// Runtime-coverage self-validation. The other assembly scans (AssemblyScanTests Scan 4/5) only require
// a feature to be referenced in SOME test source — which a compile-only generator test satisfies. This
// scan goes further: every public feature attribute and enum must be exercised in the INTEGRATION
// (runtime/behavioural) project specifically, so adding a feature without a runtime test that actually
// executes the mapping fails the build. Behavioural correctness (overflow throwing, guards gating,
// cycles terminating, values produced) is the class of bug compile-time checks structurally cannot see.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
/// Feature attributes/enums exempt from the RUNTIME-coverage requirement (must stay tiny + justified).
///
/// RoundTrip — a compile-time anti-mislinking VERIFICATION feature: [RoundTrip] pairs a method with its
///             inverse and the generator proves the pairing (DWARF020/021). Its runtime behaviour is
///             identical to an ordinary map (no distinct runtime semantics), so a dedicated runtime test
///             would add no behavioural signal. Covered by RoundTripGenTests + ReverseMap runtime tests.
///
/// DwarfMapperOptions — an assembly-level compile-time option (generated-extension visibility). Its only
///             observable effect is cross-assembly accessibility, which a single integration assembly cannot
///             exercise behaviourally; a runtime test would be hollow. Covered by FacadeExtensionsGeneratorTests.
/// </summary>
file static class RuntimeCoverageExempt
{
    public static readonly IReadOnlySet<string> AttributeUsageNames =
        new HashSet<string>(StringComparer.Ordinal) { "RoundTrip", "DwarfMapperOptions" };

    public static readonly IReadOnlySet<string> EnumTypeNames =
        new HashSet<string>(StringComparer.Ordinal);
}

public sealed class RuntimeCoverageScanTests
{
    private static readonly Assembly DwarfMapperAssembly = typeof(DwarfMapperAttribute).Assembly;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(RuntimeCoverageScanTests).Assembly.Location)!);
        while (dir != null)
        {
            if (dir.GetFiles("DwarfMapper.NET.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot locate repository root (DwarfMapper.NET.sln).");
    }

    private static readonly Lazy<string> IntegrationTestText = new(() =>
    {
        var dir = Path.Combine(FindRepoRoot(), "tests", "DwarfMapper.IntegrationTests");
        return string.Concat(
            Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Select(File.ReadAllText));
    });

    // SCAN R1 — every public feature attribute is exercised in the integration (runtime) project.
    [Fact]
    public void Every_public_attribute_has_a_runtime_test()
    {
        var blob = IntegrationTestText.Value;
        var missing = DwarfMapperAssembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(Attribute).IsAssignableFrom(t))
            .Select(t =>
            {
                // Reflection names generic attributes as e.g. "GenerateMapAttribute`2" — strip the arity
                // suffix, then the "Attribute" suffix, to get the usage name "GenerateMap".
                var name = t.Name.Split('`')[0];
                return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
            })
            .Where(usage => !RuntimeCoverageExempt.AttributeUsageNames.Contains(usage))
            // Usage as `[Name` or `[Name(` (generic [GenerateMap<...>] uses the same prefix).
            .Where(usage => !blob.Contains("[" + usage, StringComparison.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Public attribute(s) with NO runtime/behavioural test in DwarfMapper.IntegrationTests: "
            + string.Join(", ", missing)
            + ". Add a runtime test that executes a mapping using the attribute, or (rarely) add it to "
            + "RuntimeCoverageExempt with justification.");
    }

    // SCAN R2 — every public enum type is exercised in the integration (runtime) project.
    [Fact]
    public void Every_public_enum_has_a_runtime_test()
    {
        var blob = IntegrationTestText.Value;
        var missing = DwarfMapperAssembly.GetTypes()
            .Where(t => t.IsPublic && t.IsEnum)
            .Select(t => t.Name)
            .Where(name => !RuntimeCoverageExempt.EnumTypeNames.Contains(name))
            .Where(name => !blob.Contains(name + ".", StringComparison.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Public enum(s) with NO runtime/behavioural test in DwarfMapper.IntegrationTests: "
            + string.Join(", ", missing)
            + ". Add a runtime test that maps under that strategy, or add it to RuntimeCoverageExempt.");
    }
}
