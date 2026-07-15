// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DwarfMapper;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
/// Tests the tests. Nearly every self-validation scan has the shape "everything the machinery finds ⊆
/// everything referenced", which passes VACUOUSLY the moment the machinery finds NOTHING — a moved type, a
/// renamed assembly, or a broken repo-root walk turns the scan into a silent no-op that reports success while
/// checking zero things. These assert the machinery each scan depends on returns a real, non-trivial set, so
/// the subset checks built on it can never pass over an empty universe. This is the cheap-but-decisive guard
/// against the whole class of "the self-audit quietly stopped auditing" regression.
/// </summary>
public class SelfAuditNonVacuityTests
{
    private static readonly Assembly RuntimeAssembly = typeof(DwarfMapperAttribute).Assembly;
    private static readonly Assembly GeneratorAssembly = typeof(DwarfGenerator).Assembly;

    [Fact]
    public void Descriptor_reflection_finds_the_full_diagnostic_family()
    {
        // Backs Scan1 (descriptor↔AnalyzerReleases sync), Scan2 (no dead descriptor), Scan3 (every id tested),
        // Scan7 (every id documented). If this returned an empty set, all four would pass over nothing.
        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .ToList();

        Assert.True(descriptors.Count >= 60,
            $"Only {descriptors.Count} DiagnosticDescriptor fields reflected — the descriptor-based scans would "
            + "be near-vacuous. The type moved or its field shape changed.");
    }

    [Fact]
    public void Public_attribute_and_enum_reflection_is_non_empty()
    {
        // Backs Scan4 / RuntimeCoverage R1 (every attribute tested), Scan5 / R2 / R3 (every enum + value tested).
        var attributeCount = RuntimeAssembly.GetTypes()
            .Count(t => t.IsPublic && !t.IsAbstract && typeof(Attribute).IsAssignableFrom(t));
        var enumCount = RuntimeAssembly.GetTypes().Count(t => t.IsPublic && t.IsEnum);

        Assert.True(attributeCount >= 10,
            $"Only {attributeCount} public attribute types reflected — the attribute-coverage scans would be "
            + "near-vacuous.");
        Assert.True(enumCount >= 6,
            $"Only {enumCount} public enum types reflected — the enum-coverage scans would be near-vacuous.");
    }

    [Fact]
    public void The_internal_taxonomy_enums_reflect_with_values()
    {
        // Backs Scan6 (TargetKind) + InternalEnumCoverage (CountKind/DictTargetKind/NullHandling).
        foreach (var metadataName in new[]
                 {
                     "DwarfMapper.Generator.Pipeline.CollectionConverter+TargetKind",
                     "DwarfMapper.Generator.Pipeline.CollectionConverter+CountKind",
                     "DwarfMapper.Generator.Pipeline.DictionaryConverter+DictTargetKind",
                     "DwarfMapper.Generator.Model.NullHandling",
                 })
        {
            var t = GeneratorAssembly.GetType(metadataName);
            Assert.True(t is { IsEnum: true }, $"Internal enum '{metadataName}' not found — a coverage scan over it would be vacuous.");
            Assert.NotEmpty(Enum.GetNames(t!));
        }
    }

    [Fact]
    public void The_generator_and_test_source_trees_are_found_and_non_empty()
    {
        // Backs every scan that reads source text (Scan2 generator source, Scan3/4/5 test source, Scan7 docs,
        // InternalEnumCoverage). A broken repo-root walk would make them all read "" and pass vacuously.
        var root = RepoRoot();
        var generatorCs = Directory.EnumerateFiles(
            Path.Combine(root, "src", "DwarfMapper.Generator"), "*.cs", SearchOption.AllDirectories)
            .Count(NotBuildOutput);
        var testCs = Directory.EnumerateFiles(
            Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Count(NotBuildOutput);

        Assert.True(generatorCs >= 20, $"Only {generatorCs} generator .cs files found — source scans would be vacuous.");
        Assert.True(testCs >= 50, $"Only {testCs} test .cs files found — source scans would be vacuous.");
        Assert.True(File.Exists(Path.Combine(root, "docs", "diagnostics.md")), "diagnostics.md not found — Scan7 would be vacuous.");
    }

    private static bool NotBuildOutput(string f) =>
        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }
}
