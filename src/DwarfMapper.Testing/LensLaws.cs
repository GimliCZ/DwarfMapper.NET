// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Testing
{
    /// <summary>
    ///     Fuzzes the two lens laws an update-into endpoint is expected to satisfy, over a <b>pre-existing</b>
    ///     destination.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A mapper pair is a lens: <c>Map</c> is <i>get</i> and <c>Update(source, destination)</c> is
    ///         <i>put</i>. <see cref="RoundTrip" /> already checks <b>PutGet</b> — <c>Back(Forward(x)) ≡ x</c>.
    ///         The bidirectional-transformation literature says a well-behaved lens also satisfies two more, and
    ///         both are about what happens to a destination that <b>already holds data</b>:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>GetPut</b> — writing the same source twice equals writing it once. The destination reaches a
    ///             fixed point on the first write.
    ///         </item>
    ///         <item>
    ///             <b>PutPut</b> — writing two sources in sequence equals writing only the last one. No trace of the
    ///             first write survives.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         The pre-existing destination is the whole point, and it is what distinguishes this from the
    ///         generator suite's own idempotence fuzz, which updates into a freshly constructed destination and so
    ///         can never observe a member the mapper does not write. Every member the mapper leaves alone is
    ///         indistinguishable from a member it writes correctly when the destination started empty.
    ///     </para>
    ///     <para>
    ///         <b>PutPut does not hold for every legal mapper, and that is by design.</b> It requires the set of
    ///         members written to be independent of the source's <i>values</i>. A mapper using
    ///         <c>[MapNullSkip]</c>, <c>When=</c>, or any other conditional assignment decides what to write from
    ///         the source it was given, so a member the first source wrote and the second source skipped keeps the
    ///         first source's value — a genuine PutPut violation, and the correct answer for that endpoint. Call
    ///         <see cref="VerifyLastWriteWins" /> to assert a mapper is unconditional; do not call it on one that
    ///         is documented not to be. GetPut has no such caveat: the same source makes the same decisions, so
    ///         <see cref="VerifyIdempotent" /> applies to conditional mappers too.
    ///     </para>
    /// </remarks>
    public static class LensLaws
    {
        /// <summary>
        ///     XORed into the item seed to build the pre-existing destination. Public so that the seed carried by a
        ///     <see cref="LensLawException" /> replays the whole failing case by hand.
        /// </summary>
        public const int DestinationSeedSalt = 0x5F3DC0DE;

        /// <summary>
        ///     XORed into the item seed to build the second source in <see cref="VerifyLastWriteWins{TSource,TDestination}" />.
        /// </summary>
        public const int SecondSourceSeedSalt = 0x2B1E7A17;

        /// <summary>
        ///     Assert <b>GetPut</b>: over <paramref name="iterations" /> seeded source/destination pairs,
        ///     <c>update(s, d)</c> applied twice leaves the destination exactly where one application left it.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="update" /> is null.</exception>
        /// <exception cref="LensLawException">A second application changed the destination.</exception>
        public static void VerifyIdempotent<TSource, TDestination>(
            Action<TSource, TDestination> update,
            int seed = 12345,
            int iterations = 100)
        {
            if (update is null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            var rng = new Random(seed);
            for (var i = 0; i < iterations; i++)
            {
                var itemSeed = rng.Next();
                var source = ObjectFactoryV2.Create<TSource>(itemSeed);

                var once = NewDestination<TDestination>(itemSeed);
                update(source, once);

                var twice = NewDestination<TDestination>(itemSeed);
                update(source, twice);
                update(source, twice);

                Assert("GetPut", itemSeed, i, once, twice);
            }
        }

        /// <summary>
        ///     Assert <b>PutPut</b>: over <paramref name="iterations" /> seeded triples, <c>update(a, d)</c>
        ///     followed by <c>update(b, d)</c> leaves the destination exactly where <c>update(b, d)</c> alone
        ///     would. See the type remarks — a deliberately conditional mapper does not satisfy this law.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="update" /> is null.</exception>
        /// <exception cref="LensLawException">The first write left a trace the last write did not overwrite.</exception>
        public static void VerifyLastWriteWins<TSource, TDestination>(
            Action<TSource, TDestination> update,
            int seed = 12345,
            int iterations = 100)
        {
            if (update is null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            var rng = new Random(seed);
            for (var i = 0; i < iterations; i++)
            {
                var itemSeed = rng.Next();
                var first = ObjectFactoryV2.Create<TSource>(itemSeed);
                var second = ObjectFactoryV2.Create<TSource>(itemSeed ^ SecondSourceSeedSalt);

                var lastOnly = NewDestination<TDestination>(itemSeed);
                update(second, lastOnly);

                var bothWrites = NewDestination<TDestination>(itemSeed);
                update(first, bothWrites);
                update(second, bothWrites);

                Assert("PutPut", itemSeed, i, lastOnly, bothWrites);
            }
        }

        // Both laws compare two destinations that started life structurally identical, so the same seed rebuilds
        // either one; only the write sequence applied to them differs.
        private static TDestination NewDestination<TDestination>(int itemSeed)
        {
            return ObjectFactoryV2.Create<TDestination>(itemSeed ^ DestinationSeedSalt);
        }

        private static void Assert(string law, int itemSeed, int iteration, object? expected, object? actual)
        {
            var diffs = StructuralComparer.Diff(expected, actual);
            if (diffs.Count > 0)
            {
                throw new LensLawException(law, itemSeed, iteration, diffs);
            }
        }
    }
}
