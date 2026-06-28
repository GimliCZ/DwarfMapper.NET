// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
/// IMPROVEMENT-PLAN item 11: example-based parity/round-trip/update-into tests promoted to metamorphic
/// properties over N random inputs (instead of a single hand-picked input each). The behavioural schema uses
/// identical Src/Dst member types, so these transformations are intentionally lossless / idempotent.
/// </summary>
public class MetamorphicPropertyFuzzTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(0, 50).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Round_trip_is_idempotent(int seed)
    {
        var src = SyntheticSchema.GenerateBehavioralForMode(seed, SyntheticSchema.EmitMode.RoundTrip);
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            $"seed={seed} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

        var srcType = asm!.GetType("Fuzz.Src")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var forward = mapperType.GetMethod("Forward")!;
        var backward = mapperType.GetMethod("Backward")!;

        var original = ObjectFactory.Create(srcType, new Random(seed), 0)!;
        var there = forward.Invoke(mapper, new[] { original })!;
        var back = backward.Invoke(mapper, new[] { there })!;

        // Backward(Forward(x)) must value-equal x for these lossless shapes.
        var diff = CrossTypeComparer.Compare(original, back);
        Assert.True(diff.Count == 0,
            $"seed={seed} round-trip not idempotent:\n{CrossTypeComparer.Render(diff)}\n--- source ---\n{src}");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Update_into_is_idempotent(int seed)
    {
        // Update(s, freshDst) makes the destination equal s; applying Update(s, dst) AGAIN must not change it.
        // Single assembly throughout (Update's parameter type is assembly-local, so the destination instance
        // it mutates must also come from this assembly).
        var src = SyntheticSchema.GenerateBehavioralForMode(seed, SyntheticSchema.EmitMode.UpdateInto);
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            $"seed={seed} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

        var srcType = asm!.GetType("Fuzz.Src")!;
        var dstType = asm.GetType("Fuzz.Dst")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var update = mapperType.GetMethod("Update")!;

        var source = ObjectFactory.Create(srcType, new Random(seed), 0)!;
        var dst = Activator.CreateInstance(dstType)!;

        update.Invoke(mapper, new[] { source, dst });
        var firstPass = CrossTypeComparer.Compare(source, dst);
        Assert.True(firstPass.Count == 0,
            $"seed={seed} update-into not value-preserving:\n{CrossTypeComparer.Render(firstPass)}\n--- source ---\n{src}");

        update.Invoke(mapper, new[] { source, dst }); // again
        var secondPass = CrossTypeComparer.Compare(source, dst);
        Assert.True(secondPass.Count == 0,
            $"seed={seed} update-into was not idempotent (a second Update changed the destination):\n{CrossTypeComparer.Render(secondPass)}");
    }
}
