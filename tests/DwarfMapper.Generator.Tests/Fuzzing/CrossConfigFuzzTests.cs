// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Testing;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// IMPROVEMENT-PLAN item 18: cross config modes PAIRWISE, plus update-into under Preserve/SetNull. The
/// behavioural Src/Dst use identical member types, so no conversion diagnostic can fire — every combination
/// of orthogonal config options is therefore a "valid" combination that must compile with no errors AND map
/// value-preservingly on acyclic input. This is the ExpectedError oracle's trivial-but-broad slice: all
/// option pairs are expected-valid for the identity shape, so divergence (a build error, or a value change)
/// is a real bug.
/// </summary>
public class CrossConfigFuzzTests
{
    // Orthogonal config fragments. Each is value-neutral on the identical-typed acyclic behavioural shape.
    private static readonly string[] ConfigFragments =
    {
        "CaseInsensitive = true",
        "SkipNullSourceMembers = true",
        "AllowNonPublic = true",
        "ImplicitConversions = false",
        "ReferenceHandling = global::DwarfMapper.ReferenceHandlingStrategy.Preserve",
        "OnCycle = global::DwarfMapper.OnCycleStrategy.SetNull",
        "NullCollections = global::DwarfMapper.NullCollectionStrategy.AsNull",
        "NameConvention = global::DwarfMapper.NameConvention.Flexible",
        "EnumStrategy = global::DwarfMapper.EnumStrategy.ByValue",
        "NullStrategy = global::DwarfMapper.NullStrategy.SetDefault",
    };

    public static IEnumerable<object[]> Pairs()
    {
        // All unordered pairs (i < j) of config fragments; a fixed seed per pair keeps it deterministic.
        for (var i = 0; i < ConfigFragments.Length; i++)
            for (var j = i + 1; j < ConfigFragments.Length; j++)
                yield return new object[] { ConfigFragments[i], ConfigFragments[j], i * 100 + j };
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Config_pair_compiles_clean_and_is_value_preserving(string a, string b, int seed)
    {
        var attrArgs = a + ", " + b;
        var src = SyntheticSchema.GenerateBehavioralWithConfig(seed, attrArgs, SyntheticSchema.EmitMode.None);

        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);

        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            $"config pair [{attrArgs}] failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

        var srcType = asm!.GetType("Fuzz.Src")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var instance = ObjectFactory.Create(srcType, new Random(seed), 0)!;
        var dst = mapperType.GetMethod("Map")!.Invoke(mapper, new[] { instance })!;

        var diff = CrossTypeComparer.Compare(instance, dst);
        Assert.True(diff.Count == 0,
            $"config pair [{attrArgs}] not value-preserving:\n{CrossTypeComparer.Render(diff)}\n--- source ---\n{src}");
    }

    public static IEnumerable<object[]> UpdateModeSeeds() =>
        from seed in Enumerable.Range(0, 20)
        from cfg in new[]
        {
            "ReferenceHandling = global::DwarfMapper.ReferenceHandlingStrategy.Preserve",
            "OnCycle = global::DwarfMapper.OnCycleStrategy.SetNull",
        }
        select new object[] { seed, cfg };

    [Theory]
    [MemberData(nameof(UpdateModeSeeds))]
    public void Update_into_under_preserve_or_setnull_is_value_preserving(int seed, string cfg)
    {
        // Update-into combined with a reference-handling mode must still produce the correct values on
        // acyclic input (the mode only changes cycle handling, which acyclic data never exercises).
        var src = SyntheticSchema.GenerateBehavioralWithConfig(seed, cfg, SyntheticSchema.EmitMode.UpdateInto);

        var (diags, _) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);

        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            $"seed={seed} cfg=[{cfg}] failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

        var srcType = asm!.GetType("Fuzz.Src")!;
        var dstType = asm.GetType("Fuzz.Dst")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;

        var instance = ObjectFactory.Create(srcType, new Random(seed), 0)!;
        var dst = Activator.CreateInstance(dstType)!;
        mapperType.GetMethod("Update")!.Invoke(mapper, new[] { instance, dst });

        var diff = CrossTypeComparer.Compare(instance, dst);
        Assert.True(diff.Count == 0,
            $"seed={seed} cfg=[{cfg}] update-into not value-preserving:\n{CrossTypeComparer.Render(diff)}\n--- source ---\n{src}");
    }
}
