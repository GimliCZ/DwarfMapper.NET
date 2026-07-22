// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
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
        // A WithTrackingName the battery never asserts is decoration.
        foreach (var (assemblyType, registeredName) in new[]
                 {
                     (typeof(DwarfGenerator), "DwarfGenerator"),
                     (typeof(DwarfMapper.Generator.Registry.MapToGenerator), "MapToGenerator"),
                 })
        {
            var declared = assemblyType
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                            && f.Name.EndsWith("StepName", StringComparison.Ordinal))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            var registered = GeneratorRegistry.All.Single(g => g.Name == registeredName).TrackingNames;

            foreach (var name in declared)
                Assert.True(registered.Contains(name),
                    $"{registeredName} declares step name '{name}' but AllStepNames does not include it, so the "
                    + "battery never asserts that step is cacheable.");
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
}
