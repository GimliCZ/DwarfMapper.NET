// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Testing.Tests
{
    public class LensSource
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";
    }

    public class LensDest
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";

        // Never written by LensUpdateMapper. A fresh destination cannot tell "left alone" from "written
        // correctly", which is exactly the blind spot the pre-existing destination removes.
        public string Note { get; set; } = "";
    }

    [DwarfMapper]
    public partial class LensUpdateMapper
    {
        [MapIgnore("Note")]
        public partial void Update(LensSource source, LensDest destination);
    }

    // Hand-written negative controls. They take nullable members because the violations being demonstrated
    // are about source VALUES deciding what gets written, which is what null-skipping does.
    public class LensPatch
    {
        public int Age { get; set; }

        public string? Name { get; set; }
    }

    public class LensPatchTarget
    {
        public int Age { get; set; }

        public string? Name { get; set; }
    }

    public class LensLawTests
    {
        [Fact]
        public void A_generated_update_into_satisfies_GetPut()
        {
            var m = new LensUpdateMapper();

            var ex = Record.Exception(() =>
                LensLaws.VerifyIdempotent<LensSource, LensDest>(m.Update, 7, 50));

            Assert.Null(ex);
        }

        [Fact]
        public void A_generated_update_into_satisfies_PutPut()
        {
            var m = new LensUpdateMapper();

            var ex = Record.Exception(() =>
                LensLaws.VerifyLastWriteWins<LensSource, LensDest>(m.Update, 7, 50));

            Assert.Null(ex);
        }

        [Fact]
        public void The_oracle_hands_the_update_a_populated_destination_and_the_sequence_it_documents()
        {
            // The whole difference between this oracle and the generator suite's own idempotence fuzz, which
            // updates into Activator.CreateInstance(dstType). If the destinations handed over here were
            // all-default, both laws would hold vacuously for any partial mapper. A probe updater that records
            // what it was given observes the real thing rather than a reconstruction of it.
            var seen = new List<LensDest>();

            LensLaws.VerifyIdempotent<LensSource, LensDest>((_, d) => seen.Add(d), 12345, 5);

            // Three applications per iteration: once into the first destination, twice into the second. Pinning
            // the count proves the oracle applies the sequence its documentation claims.
            Assert.Equal(15, seen.Count);
            Assert.Contains(seen, d => StructuralComparer.Diff(new LensDest(), d).Count > 0);
        }

        [Fact]
        public void An_accumulating_update_violates_GetPut_and_the_dump_names_the_law_and_the_member()
        {
            // A counter is the textbook non-idempotent write: the second application of the SAME source moves
            // the destination again.
            static void Accumulate(LensPatch source, LensPatchTarget destination)
            {
                destination.Age = unchecked(destination.Age + 1);
            }

            var ex = Assert.Throws<LensLawException>(() =>
                LensLaws.VerifyIdempotent<LensPatch, LensPatchTarget>(Accumulate, 1, 20));

            Assert.Equal("GetPut", ex.Law);
            Assert.Contains(ex.Diffs, d => d.Path.EndsWith(".Age", StringComparison.Ordinal));
            Assert.Contains("Lens law GetPut violated", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_null_skipping_update_violates_PutPut_which_is_the_documented_caveat_not_a_bug()
        {
            // The caveat on LensLaws made concrete. A [MapNullSkip]-shaped updater decides what to write from
            // the source's VALUES, so a member the first source wrote and the second skipped keeps the FIRST
            // source's value. That is a genuine PutPut violation and the correct behaviour for that endpoint —
            // which is why VerifyLastWriteWins is opt-in per mapper rather than a blanket assertion.
            var ex = Assert.Throws<LensLawException>(() =>
                LensLaws.VerifyLastWriteWins<LensPatch, LensPatchTarget>(NullSkipping, 1, 200));

            Assert.Equal("PutPut", ex.Law);
            Assert.Contains(ex.Diffs, d => d.Path.EndsWith(".Name", StringComparison.Ordinal));
        }

        [Fact]
        public void The_same_null_skipping_update_still_satisfies_GetPut()
        {
            // GetPut has no conditional-write caveat: the same source makes the same decisions, so an endpoint
            // that fails PutPut for good reasons must still reach a fixed point. Asserting BOTH directions is
            // what keeps the previous test from reading as "conditional mappers are simply unverifiable".
            var ex = Record.Exception(() =>
                LensLaws.VerifyIdempotent<LensPatch, LensPatchTarget>(NullSkipping, 1, 200));

            Assert.Null(ex);
        }

        [Fact]
        public void Both_verifiers_reject_a_null_update_by_name()
        {
            Assert.Equal("update",
                Assert.Throws<ArgumentNullException>(
                    () => LensLaws.VerifyIdempotent<LensSource, LensDest>(null!, 7, 1)).ParamName);
            Assert.Equal("update",
                Assert.Throws<ArgumentNullException>(
                    () => LensLaws.VerifyLastWriteWins<LensSource, LensDest>(null!, 7, 1)).ParamName);
        }

        [Fact]
        public void A_failure_carries_the_seed_that_replays_it_and_the_diffs_the_replay_produces()
        {
            // The informed dump is only useful if one integer rebuilds the whole failing case. Replay the
            // reported seed through the documented salts and reproduce the exact diff list.
            var ex = Assert.Throws<LensLawException>(() =>
                LensLaws.VerifyLastWriteWins<LensPatch, LensPatchTarget>(NullSkipping, 1, 200));

            var first = ObjectFactoryV2.Create<LensPatch>(ex.Seed);
            var second = ObjectFactoryV2.Create<LensPatch>(ex.Seed ^ LensLaws.SecondSourceSeedSalt);

            var lastOnly = ObjectFactoryV2.Create<LensPatchTarget>(ex.Seed ^ LensLaws.DestinationSeedSalt);
            NullSkipping(second, lastOnly);

            var bothWrites = ObjectFactoryV2.Create<LensPatchTarget>(ex.Seed ^ LensLaws.DestinationSeedSalt);
            NullSkipping(first, bothWrites);
            NullSkipping(second, bothWrites);

            var replayed = StructuralComparer.Diff(lastOnly, bothWrites);

            Assert.Equal(
                ex.Diffs.Select(d => (d.Path, d.Expected, d.Actual)),
                replayed.Select(d => (d.Path, d.Expected, d.Actual)));
            Assert.Contains(
                "[seed: " + ex.Seed.ToString(CultureInfo.InvariantCulture) + ", iteration: " +
                ex.Iteration.ToString(CultureInfo.InvariantCulture) + "]",
                ex.Message,
                StringComparison.Ordinal);
        }

        // The [MapNullSkip] shape, shared by the three tests that need it: a conditional write set that
        // depends on the source's values.
        private static void NullSkipping(LensPatch source, LensPatchTarget destination)
        {
            destination.Age = source.Age;
            if (source.Name is not null)
            {
                destination.Name = source.Name;
            }
        }
        /// <summary>
        ///     The last-write-wins verifier runs EXACTLY the iterations it was asked for, and writes three times
        ///     in each: once into the destination that sees only the last write, and twice into the one that
        ///     sees both. Nothing observed either number before, so an off-by-one in the loop bound was invisible
        ///     while changing how much evidence a passing verification carries.
        /// </summary>
        [Fact]
        public void The_last_write_wins_verifier_runs_exactly_the_iterations_it_was_asked_for()
        {
            var writes = 0;

            LensLaws.VerifyLastWriteWins<Sample, Sample>((source, destination) =>
                {
                    writes++;
                    destination.Id = source.Id;
                    destination.Name = source.Name;
                    destination.Maybe = source.Maybe;
                    destination.Nums = source.Nums;
                    destination.Child = source.Child;
                },
                seed: 7,
                iterations: 3);

            Assert.Equal(9, writes);
        }

    }
}
