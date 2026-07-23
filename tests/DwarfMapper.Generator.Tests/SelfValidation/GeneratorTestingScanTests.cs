// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper;
using DwarfMapper.Generator.Tests.Framework;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Ratchets that keep the framework applied. Deduplicating once fixes today; without a gate the next
///     generator ships uncovered — which is exactly how MapToGenerator ended up with no cacheability tests at
///     all while DwarfGenerator had six.
/// </summary>
public class GeneratorTestingScanTests
{
    [Fact]
    public void Every_generator_in_src_is_registered_for_testing()
    {
        var declared = typeof(DwarfGenerator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var registered = GeneratorRegistry.All.Select(g => g.Name)
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(declared.SequenceEqual(registered, StringComparer.Ordinal),
            "Generators in src/ do not match GeneratorRegistry.All.\n  declared:   "
            + string.Join(", ", declared) + "\n  registered: " + string.Join(", ", registered)
            + "\nAdd the new generator to GeneratorRegistry so it gets cacheability and golden coverage.");
    }

    [Fact]
    public void Every_tracking_name_constant_is_registered_in_the_battery()
    {
        // A WithTrackingName the battery never asserts is decoration. Enumerate every IIncrementalGenerator
        // type actually present in src/ — rather than a hardcoded two-tuple — so a third generator is checked
        // automatically instead of being silently exempt. The sibling ratchet above forces a new generator
        // into GeneratorRegistry, but that alone proves nothing about whether ITS step names are covered.
        var generatorTypes = typeof(DwarfGenerator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IIncrementalGenerator).IsAssignableFrom(t));

        foreach (var assemblyType in generatorTypes)
        {
            var registered = GeneratorRegistry.All.SingleOrDefault(g => g.Name == assemblyType.Name);
            Assert.True(registered is not null,
                $"'{assemblyType.Name}' has no entry in GeneratorRegistry.All — add it there, or the "
                + "battery never asserts any of its step names are cacheable.");

            var declared = assemblyType
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                            && f.Name.EndsWith("StepName", StringComparison.Ordinal))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            foreach (var name in declared)
                Assert.True(registered!.TrackingNames.Contains(name),
                    $"{assemblyType.Name} declares step name '{name}' but AllStepNames does not include it, so "
                    + "the battery never asserts that step is cacheable.");
        }
    }

    [Fact]
    public void The_golden_corpus_covers_every_registered_generator()
    {
        var covered = GoldenCorpus.Cases().Select(c => c.GeneratorName).Distinct(StringComparer.Ordinal).ToList();

        foreach (var g in GeneratorRegistry.All)
            Assert.True(covered.Contains(g.Name, StringComparer.Ordinal),
                $"Generator '{g.Name}' contributes no golden cases, so a refactor could change its output "
                + "undetected. Add feature cases for it to GoldenCorpus.FeatureCases().");
    }

    // Baselines recorded against DwarfMapper.dll on 2026-07-22: 31 public attribute types, 14 public enum
    // values (7 enums, 2 values each). GoldenCorpus.FeatureCases() is a hand-curated list, NOT derived from
    // these taxonomies — see the design spec's Known Limitations note. This ratchet is the honest substitute:
    // it cannot force a specific new case the way true derivation would, but it forces a human to notice
    // growth and decide whether the feature axis needs a new pinned case, rather than the corpus silently
    // going stale next to an attribute or enum value nobody golden-tested.
    private const int BaselineAttributeTypeCount = 31;
    private const int BaselineEnumValueCount = 14;

    [Fact]
    public void The_feature_taxonomy_has_not_grown_past_its_recorded_baseline()
    {
        var runtimeAssembly = typeof(DwarfMapperAttribute).Assembly;

        var attributeTypeCount = runtimeAssembly.GetTypes()
            .Count(t => t.IsPublic && typeof(Attribute).IsAssignableFrom(t));

        Assert.True(attributeTypeCount == BaselineAttributeTypeCount,
            $"{runtimeAssembly.GetName().Name} now has {attributeTypeCount} public attribute types; the "
            + $"recorded baseline is {BaselineAttributeTypeCount}. A taxonomy grew, so consider adding a "
            + "golden feature case, then update the baseline deliberately.");

        var enumValueCount = runtimeAssembly.GetTypes()
            .Where(t => t.IsPublic && t.IsEnum)
            .Sum(t => Enum.GetValues(t).Length);

        Assert.True(enumValueCount == BaselineEnumValueCount,
            $"{runtimeAssembly.GetName().Name} now has {enumValueCount} public enum values across its public "
            + $"enums; the recorded baseline is {BaselineEnumValueCount}. A taxonomy grew, so consider adding "
            + "a golden feature case, then update the baseline deliberately.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }

    // Keeps the shared engine core actually shared. Two-engine drift caused 5 of the 32 audit issues, and the
    // divergence this trio of ratchets guards — the registry enumerating members without walking base types —
    // silently dropped data for years because nothing forced the two engines to agree. Split into three [Fact]s
    // (rather than one bundled assertion) so each clause fails independently instead of the first assertion
    // masking whether the other two would ever have caught anything.

    private static string ExtractorText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DwarfMapper.Generator", "Pipeline",
            "MapperExtractor.cs"));

    private static string RegistryText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DwarfMapper.Generator", "Registry",
            "MapToGenerator.cs"));

    [Fact]
    public void MapToGenerator_does_not_call_GetMembers_directly()
    {
        var registry = RegistryText();

        // The registry must enumerate members only through MemberFacts. Both of its former GetMembers() calls
        // were inside its own shallow ReadableMembers/WritableMembers.
        Assert.False(registry.Contains("GetMembers()", StringComparison.Ordinal),
            "MapToGenerator calls GetMembers() directly again. Member enumeration must go through "
            + "Core.MemberFacts, or the registry silently loses inherited members as it did before.");
    }

    [Fact]
    public void Neither_engine_declares_its_own_FNV_constant()
    {
        var extractor = ExtractorText();
        var registry = RegistryText();

        // FNV-1a lives in Core.StableHash and nowhere else.
        foreach (var (name, text) in new[] { ("MapperExtractor", extractor), ("MapToGenerator", registry) })
            Assert.False(text.Contains("2166136261", StringComparison.Ordinal),
                $"{name} declares its own FNV-1a constant again. Hashing belongs to Core.StableHash — ten "
                + "copies of it is what ISSUE-015 was.");
    }

    [Fact]
    public void Both_engines_call_MemberFacts_Readable_and_Writable()
    {
        var extractor = ExtractorText();
        var registry = RegistryText();

        // A bare mention of "MemberFacts" — a stray comment, a dead using — proves nothing. Require the actual
        // call syntax for both the readable- and writable-member entry points, so a regression that keeps a
        // dead reference while re-implementing enumeration elsewhere still fails this.
        foreach (var (name, text) in new[] { ("MapperExtractor", extractor), ("MapToGenerator", registry) })
        {
            Assert.True(text.Contains("MemberFacts.Readable(", StringComparison.Ordinal),
                $"{name} no longer calls MemberFacts.Readable(...) — member enumeration may have been "
                + "re-implemented locally instead of routed through the shared core.");
            Assert.True(text.Contains("MemberFacts.Writable(", StringComparison.Ordinal),
                $"{name} no longer calls MemberFacts.Writable(...) — member enumeration may have been "
                + "re-implemented locally instead of routed through the shared core.");
        }
    }
}
