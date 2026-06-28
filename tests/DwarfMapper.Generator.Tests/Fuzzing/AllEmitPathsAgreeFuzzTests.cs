// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// IMPROVEMENT-PLAN item 5: metamorphic "all emit paths agree" value oracle. For each seed the SAME behavioural
/// Src/Dst shape is emitted four ways — None, Preserve, SetNull, and update-into — and an ACYCLIC instance
/// (same seed → structurally identical across the four assemblies) is mapped through each. On acyclic input
/// every mode must produce a value-identical result that also equals the source; divergence is a bug (this is
/// exactly the Preserve/SetNull/update-into value-regression class that historically hid behind compile-only
/// fuzz coverage).
/// </summary>
public class AllEmitPathsAgreeFuzzTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 50).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void All_emit_modes_agree_on_acyclic_input(int seed)
    {
        var modes = new[]
        {
            SyntheticSchema.EmitMode.None,
            SyntheticSchema.EmitMode.Preserve,
            SyntheticSchema.EmitMode.SetNull,
            SyntheticSchema.EmitMode.UpdateInto,
        };

        object? canonicalSrc = null;
        var results = new Dictionary<SyntheticSchema.EmitMode, object>();

        foreach (var mode in modes)
        {
            var src = SyntheticSchema.GenerateBehavioralForMode(seed, mode);
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
            Assert.True(asm is not null,
                $"seed={seed} mode={mode} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

            var srcType = asm!.GetType("Fuzz.Src")!;
            var dstType = asm.GetType("Fuzz.Dst")!;
            var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
            var mapper = Activator.CreateInstance(mapperType)!;

            // Same seed → ObjectFactory walks the identical (structurally equal) shape with the identical
            // pseudo-random sequence, so the source instance is the same across all four assemblies.
            var srcInstance = ObjectFactory.Create(srcType, new Random(seed), 0)!;
            canonicalSrc ??= srcInstance;

            object dstInstance;
            if (mode == SyntheticSchema.EmitMode.UpdateInto)
            {
                dstInstance = Activator.CreateInstance(dstType)!;
                mapperType.GetMethod("Update")!.Invoke(mapper, new[] { srcInstance, dstInstance });
            }
            else
            {
                dstInstance = mapperType.GetMethod("Map")!.Invoke(mapper, new[] { srcInstance })!;
            }

            // Each mode is value-preserving vs the source.
            var vsSrc = CrossTypeComparer.Compare(srcInstance, dstInstance);
            Assert.True(vsSrc.Count == 0,
                $"seed={seed} mode={mode} not value-preserving:\n{CrossTypeComparer.Render(vsSrc)}\n--- source ---\n{src}");

            results[mode] = dstInstance;
        }

        // And all four modes agree with each other (compare each against None's result).
        var baseline = results[SyntheticSchema.EmitMode.None];
        foreach (var mode in modes.Where(m => m != SyntheticSchema.EmitMode.None))
        {
            var diff = CrossTypeComparer.Compare(baseline, results[mode]);
            Assert.True(diff.Count == 0,
                $"seed={seed} mode={mode} diverged from None:\n{CrossTypeComparer.Render(diff)}");
        }
    }
}
