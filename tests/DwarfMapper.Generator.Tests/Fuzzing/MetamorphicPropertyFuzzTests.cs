// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Testing;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.Generator.Tests.Fuzzing
{
    /// <summary>
    ///     IMPROVEMENT-PLAN item 11: example-based parity/round-trip/update-into tests promoted to metamorphic
    ///     properties over N random inputs (instead of a single hand-picked input each). The behavioural schema uses
    ///     identical Src/Dst member types, so these transformations are intentionally lossless / idempotent.
    /// </summary>
    public class MetamorphicPropertyFuzzTests
    {
        // 250 seeds — see TestPopulation.MetamorphicSeeds.
        public static IEnumerable<object[]> Seeds()
        {
            return Enumerable.Range(0, TestPopulations.Count(TestPopulation.MetamorphicSeeds)).Select(i => new object[]
            {
                i
            });
        }

        /// <summary>
        ///     The round-trip seeds are dealt into two classes (round 34 E12, <see cref="TheoryParts" />): the two
        ///     theories took 21 s in one class, 13 s of it the round trips.
        /// </summary>
        private const int Parts = 2;

        public static IEnumerable<object[]> Seeds(int part)
        {
            return TheoryParts.Of(Seeds(), part, Parts);
        }

        internal static void RoundTripIsIdempotent(int seed)
        {
            var src = SyntheticSchema.GenerateBehavioralForMode(seed, SyntheticSchema.EmitMode.RoundTrip);
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
            Assert.True(asm is not null,
                $"seed={seed} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

            var srcType = asm.GetType("Fuzz.Src")!;
            var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
            var mapper = Activator.CreateInstance(mapperType)!;
            var forward = mapperType.GetMethod("Forward")!;
            var backward = mapperType.GetMethod("Backward")!;

            var original = ObjectFactoryV2.Create(srcType, new Random(seed), 0)!;
            var there = forward.Invoke(mapper,
                new[]
                {
                    original
                })!;
            var back = backward.Invoke(mapper,
                new[]
                {
                    there
                })!;

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

            var srcType = asm.GetType("Fuzz.Src")!;
            var dstType = asm.GetType("Fuzz.Dst")!;
            var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
            var mapper = Activator.CreateInstance(mapperType)!;
            var update = mapperType.GetMethod("Update")!;

            var source = ObjectFactoryV2.Create(srcType, new Random(seed), 0)!;
            var dst = Activator.CreateInstance(dstType)!;

            update.Invoke(mapper,
                new[]
                {
                    source, dst
                });
            var firstPass = CrossTypeComparer.Compare(source, dst);
            Assert.True(firstPass.Count == 0,
                $"seed={seed} update-into not value-preserving:\n{CrossTypeComparer.Render(firstPass)}\n--- source ---\n{src}");

            update.Invoke(mapper,
                new[]
                {
                    source, dst
                }); // again
            var secondPass = CrossTypeComparer.Compare(source, dst);
            Assert.True(secondPass.Count == 0,
                $"seed={seed} update-into was not idempotent (a second Update changed the destination):\n{CrossTypeComparer.Render(secondPass)}");
        }
    }

    // Round 34 (E12): MetamorphicPropertyFuzzTests's heavy theory, dealt into parts by TheoryParts so the parts run in
    // parallel; each part is a class of its own because xunit runs one class's tests one after another.
    public sealed class MetamorphicPropertyFuzzTestsPart0
    {
        [Theory]
        [MemberData(nameof(MetamorphicPropertyFuzzTests.Seeds), 0, MemberType = typeof(MetamorphicPropertyFuzzTests))]
        public void Round_trip_is_idempotent(int seed)
        {
            MetamorphicPropertyFuzzTests.RoundTripIsIdempotent(seed);
        }
    }

    public sealed class MetamorphicPropertyFuzzTestsPart1
    {
        [Theory]
        [MemberData(nameof(MetamorphicPropertyFuzzTests.Seeds), 1, MemberType = typeof(MetamorphicPropertyFuzzTests))]
        public void Round_trip_is_idempotent(int seed)
        {
            MetamorphicPropertyFuzzTests.RoundTripIsIdempotent(seed);
        }
    }
}
