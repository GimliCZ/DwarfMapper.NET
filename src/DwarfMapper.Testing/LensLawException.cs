// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Testing
{
    /// <summary>Thrown when an update-into endpoint violates one of the lens laws, with an informed dump.</summary>
    public sealed class LensLawException : Exception
    {
        /// <summary>Creates a lens-law failure naming the law, the offending seed, iteration, and diffs.</summary>
        public LensLawException(string law, int seed, int iteration, IReadOnlyList<MemberDiff> diffs)
            : base(Build(law, seed, iteration, diffs))
        {
            Law = law;
            Seed = seed;
            Iteration = iteration;
            Diffs = diffs;
        }

        /// <summary>The law that was violated, e.g. <c>GetPut</c> or <c>PutPut</c>.</summary>
        public string Law { get; }

        /// <summary>
        ///     The fuzz ITEM seed that produced the failure. Every instance in the failing iteration derives from
        ///     it: the source is <c>ObjectFactoryV2.Create&lt;TSource&gt;(Seed)</c>, the pre-existing destination is
        ///     <c>Create&lt;TDestination&gt;(Seed ^ LensLaws.DestinationSeedSalt)</c>, and the second source of a
        ///     <c>PutPut</c> failure is <c>Create&lt;TSource&gt;(Seed ^ LensLaws.SecondSourceSeedSalt)</c>. One
        ///     integer replays the whole case by hand.
        /// </summary>
        public int Seed { get; }

        /// <summary>The iteration index within the run.</summary>
        public int Iteration { get; }

        /// <summary>The structural differences found.</summary>
        public IReadOnlyList<MemberDiff> Diffs { get; }

        private static string Build(string law, int seed, int iteration, IReadOnlyList<MemberDiff> diffs)
        {
            var header = string.Format(
                CultureInfo.InvariantCulture,
                "Lens law {0} violated [seed: {1}, iteration: {2}]\n",
                law,
                seed,
                iteration);
            return header + StructuralComparer.Render(diffs);
        }
    }
}
