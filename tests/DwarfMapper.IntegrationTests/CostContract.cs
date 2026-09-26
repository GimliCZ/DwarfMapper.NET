// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     The shared measurement primitive behind every cost contract in this assembly: a per-operation
    ///     allocation figure, and the two SLOPE measurements built on it. It measures; callers assert.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why slopes and not absolute budgets.</b> Round 30 lost a 56 KB-per-call defect to a gate that
    ///         pins absolute bytes exactly — <c>allocation-baseline.json</c>, which fails on a single byte of
    ///         drift in either direction. It could not see this defect because the cost was
    ///         <c>80 + 24·N</c> bytes, where N is the number of registered maps: at benchmark scale (N ≈ 10)
    ///         that is 296 B, indistinguishable from a legitimate pin, and it only becomes 56 KB at consumer
    ///         scale. A pin at one point on a line cannot see the line's gradient.
    ///     </para>
    ///     <para>
    ///         So the contract enforced here is not "this costs X bytes" but "**this cost does not depend on
    ///         how much state exists, or on how many calls came before**". That is the property a long-lived
    ///         process actually needs, and it is the one an absolute pin structurally cannot express. The two
    ///         are complementary: keep the byte pins for what they catch, add slopes for what they cannot.
    ///     </para>
    ///     <para>
    ///         <b>Vacuity.</b> <see cref="MeasureAgainstScale" /> refuses to run below the scale lever a slope
    ///         needs. A slope measured between two nearly equal scales is noise reported as a result — the
    ///         failure mode this repository has produced six times in other forms, and once already in this
    ///         very suite before the guard was added.
    ///     </para>
    /// </remarks>
    internal static class CostContract
    {
        /// <summary>
        ///     Bytes allocated per operation on the CURRENT THREAD, so a parallel test cannot contaminate it.
        /// </summary>
        /// <remarks>
        ///     Warms up first: the measured window must be steady-state, or it charges one-time JIT and
        ///     first-touch costs to the operation and every reading is inflated by an amount that depends on
        ///     what ran before.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double BytesPerOperation(Action operation, int iterations = 10_000)
        {
            ArgumentNullException.ThrowIfNull(operation);

            for (var i = 0; i < Math.Min(1_000, iterations); i++)
                operation();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
                operation();

            return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iterations;
        }

        /// <summary>
        ///     The headroom a flat cost is allowed over its baseline. Shared so every cost contract in this
        ///     assembly judges flatness by the same rule instead of each inventing a threshold.
        /// </summary>
        /// <remarks>
        ///     A per-entry cost of even one byte shows up as a difference proportional to the scale lever, so
        ///     25% plus a fixed 256 B absorbs JIT tiering and allocation-context noise without coming
        ///     anywhere near admitting a linear term.
        /// </remarks>
        internal static double FlatCeiling(double baseline)
        {
            return baseline * 1.25 + 256;
        }

        /// <summary>
        ///     Measures per-operation cost against the size of the ambient state the operation reads — the
        ///     axis that hid the round-30 registry defect. The CALLER asserts; this only measures.
        /// </summary>
        /// <remarks>
        ///     Measurement and judgement are deliberately separate. A helper that asserted would put the
        ///     assertion in a different file from the test, which is both harder to read and something
        ///     <c>TestTheTestsScanTests</c> correctly refuses.
        /// </remarks>
        /// <param name="operationAtScale">
        ///     Given a scale, returns the operation to measure. Responsible for establishing ambient state of
        ///     that size; invoked once per scale, outside the measured window.
        /// </param>
        internal static (double AtSmall, double AtLarge) MeasureAgainstScale(
            Func<int, Action> operationAtScale,
            int smallScale,
            int largeScale)
        {
            ArgumentNullException.ThrowIfNull(operationAtScale);

            // A slope needs a lever. Two nearby scales cannot distinguish 24 B/entry from noise, and a
            // measurement that cannot show the effect reads as coverage while proving nothing.
            if (largeScale < smallScale * 50)
            {
                throw new ArgumentOutOfRangeException(nameof(largeScale),
                    $"scales {smallScale} and {largeScale} are too close for a slope to be distinguishable " +
                    "from noise; use a lever of at least 50x.");
            }

            return (BytesPerOperation(operationAtScale(smallScale)),
                BytesPerOperation(operationAtScale(largeScale)));
        }

        /// <summary>
        ///     Measures per-operation cost early, then again after a large number of intervening calls — the
        ///     accumulation axis. A structure that lengthens per call shows here even when bounded in total,
        ///     which is the difference between "expensive" and "getting worse". The CALLER asserts.
        /// </summary>
        internal static (double Early, double Late) MeasureOverTime(Action operation, int intervening = 50_000)
        {
            ArgumentNullException.ThrowIfNull(operation);

            var early = BytesPerOperation(operation);

            for (var i = 0; i < intervening; i++)
                operation();

            return (early, BytesPerOperation(operation));
        }

        /// <summary>
        ///     Forces a full blocking collect with a finalizer drain, then reports the retained heap. One pass
        ///     alone leaves anything finalizable alive and reports a leak that is not there.
        /// </summary>
        internal static long RetainedBytes()
        {
            for (var i = 0; i < 2; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            return GC.GetTotalMemory(true);
        }
    }
}
