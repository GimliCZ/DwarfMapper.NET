// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Runtime.ExceptionServices;
using CsCheck;
using Xunit.Abstractions;
using DwarfMapper.TestInfrastructure;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     The CompilerTests sampling seam (round 22, I11): a property test in this project runs a FIXED,
    ///     replayable list of cases, never a fresh random draw.
    ///     <para>
    ///         <b>Why this exists.</b> K0/K1/K2 were written as <c>gen.Sample(assert, iter: n)</c>. CsCheck's
    ///         <c>seed</c> argument pins <i>the first iteration only</i> — measured 2026-08-22 on CsCheck
    ///         4.7.0: with <c>seed</c> fixed and <c>threads: 1</c>, iteration 1 is constant and iterations
    ///         2..n come from a randomly seeded thread PCG, so the executed case set differs on every run.
    ///         That makes a fast-tier <c>[Fact]</c> a gate on a nondeterministic oracle, which invariant
    ///         <b>R4</b> forbids outright (<c>RatchetInvariantScanTests</c>), and it is how MR-3 shipped able
    ///         to redden at random — the I11 finding.
    ///     </para>
    ///     <para>
    ///         <b>The shape.</b> Case <c>k</c> is the single sample CsCheck draws from PCG stream <c>k</c> at
    ///         <see cref="BaseSeed" />, run as its own one-iteration <c>Sample</c> call. Consequences, all
    ///         wanted: the case set is a pure function of the generator and the population count, so two runs
    ///         execute the SAME cases; the fast list is a strict PREFIX of the deep list, so a deep run is a
    ///         superset of the fast one and a case index means the same thing in both tiers; and a red is
    ///         replayable by index forever (the printed seed string is <c>PCG.ToString()</c>, which
    ///         <c>PCG.Parse</c> — and therefore <c>CsCheck_Seed</c> — round-trips).
    ///     </para>
    ///     <para>
    ///         <b>What was traded away.</b> CsCheck's shrinking is effectively gone here, and it was already
    ///         mostly notional: a one-iteration sample has no budget left to shrink with (the I11 repro run
    ///         reported "0 shrinks, 7 skipped, 8 total" — the failing draw consumed the budget). The repro
    ///         artifact is therefore the assertion message's <c>GraphSpec.Describe()</c> plus the case index,
    ///         and house practice is unchanged: a finding is minimized by hand and pinned as a
    ///         <see cref="PinnedCorpus" /> row, which survives generator edits that would rot any seed string.
    ///     </para>
    ///     <para>
    ///         <b>Exploration still belongs to the deep tier</b>, and it stays deterministic there: widen by
    ///         raising the population's deep count (more pinned streams), not by re-rolling the same count.
    ///         A deep red is then as replayable as a fast one.
    ///     </para>
    ///     <para>
    ///         H7 termination: the case loop is <c>Parallel.For</c> over <c>[0, count)</c> with
    ///         <c>count</c> read once from the deep-tier catalog; every inner <c>Sample</c> runs exactly one
    ///         iteration on one thread.
    ///     </para>
    /// </summary>
    internal static class PinnedSampling
    {
        /// <summary>
        ///     The pinned PCG seed every case stream is drawn at. An arbitrary constant — its VALUE carries no
        ///     meaning, its STABILITY is the whole point: changing it silently swaps the entire case set of
        ///     every population in this project, so treat it the way the deep-tier catalog treats a fast count.
        ///     (Distinct streams rather than sequential seeds: sequential seeds measurably collide — 62/64
        ///     distinct first draws at 2026-08-22 against 64/64 for distinct streams.)
        /// </summary>
        private const ulong BaseSeed = 0xD2A4F9C1B7E35608UL;

        /// <summary>The CsCheck seed string of case <paramref name="index" /> — printed with every red.</summary>
        public static string SeedFor(int index)
        {
            return new PCG((uint)index, BaseSeed).ToString();
        }

        /// <summary>
        ///     Runs <paramref name="body" /> over the pinned case set of <paramref name="population" /> and
        ///     writes the case-set digest to <paramref name="output" />. The digest is REPORTED, never gated:
        ///     it changes legitimately whenever the generator's grammar changes, so pinning it would be a
        ///     ratchet on the generator rather than on the product — but two runs of the same tree must print
        ///     the same value, which is what makes determinism checkable from a log.
        /// </summary>
        /// <param name="population">The deep-tier catalog entry that owns the case count.</param>
        /// <param name="gen">The generator; case <c>k</c> is its single draw from stream <c>k</c>.</param>
        /// <param name="describe">Stable one-line description of a case, for the digest.</param>
        /// <param name="body">The relation/oracle to run per case. Must be thread-safe.</param>
        /// <param name="output">xunit sink for the digest line.</param>
        /// <param name="label">Name of the relation, for the digest line.</param>
        public static void Run<T>(
            DeepPopulation population,
            Gen<T> gen,
            Func<T, string> describe,
            Action<T> body,
            ITestOutputHelper output,
            string label)
        {
            var count = DeepTier.Count(population);
            var descriptions = new string?[count];
            AggregateException? failures = null;

            try
            {
                Parallel.For(0,
                    count,
                    index =>
                        gen.Sample(sample =>
                            {
                                // First assignment only: a CsCheck shrink would re-enter with a DIFFERENT case and
                                // the digest must describe the pinned set, not the shrink trail.
                                descriptions[index] ??= describe(sample);
                                body(sample);
                            },
                            seed: SeedFor(index),
                            iter: 1,
                            threads: 1));
            }
            catch (AggregateException e)
            {
                failures = e;
            }

            // Written even on a red: the digest is the determinism evidence, and a run that fails is exactly
            // when knowing WHICH case set ran matters.
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{label}: pinned case set = {count} cases, digest {Digest(descriptions)}"));

            // Rethrow the FIRST failure with its stack intact rather than leaving it inside Parallel.For's
            // AggregateException — the assertion messages in this project carry the repro (and CsCheck's own
            // message carries the pinned seed string, so the case is replayable straight from the log) and
            // must arrive unwrapped.
            if (failures is not null)
            {
                ExceptionDispatchInfo.Capture(failures.Flatten().InnerExceptions[0]).Throw();
            }
        }

        /// <summary>
        ///     FNV-1a over the index-ordered case descriptions. Index order is already canonical (the array is
        ///     written by index), so <c>Parallel.For</c>'s interleaving cannot reach the digest.
        /// </summary>
        private static string Digest(string?[] descriptions)
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                for (var i = 0; i < descriptions.Length; i++)
                    foreach (var c in string.Create(CultureInfo.InvariantCulture,
                                 $"{i}|{descriptions[i] ?? "<not-drawn>"}\n"))
                        h = (h ^ c) * 1099511628211UL;

                return h.ToString("x16", CultureInfo.InvariantCulture);
            }
        }
    }
}
