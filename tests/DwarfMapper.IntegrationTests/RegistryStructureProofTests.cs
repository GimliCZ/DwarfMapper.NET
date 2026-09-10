// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Concurrent;

namespace DwarfMapper.IntegrationTests
{
    /// <summary>
    ///     Standing proof of the round-30 ambient-dispatch defect: the structure that was there was linear in
    ///     the number of registered maps, the structure that replaced it is flat, and both facts are measured
    ///     here rather than asserted in a commit message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why prove a fixed bug.</b> The fix is one line of type choice —
    ///         <see cref="ConcurrentBag{T}" /> to a copy-on-write array — and it looks arbitrary to anyone who
    ///         does not already know that a bag's enumerator COPIES. Someone reading
    ///         <c>DwarfMapperRegistry</c> and wanting a concurrent collection would reach for the bag again
    ///         and be entirely reasonable. The commit message explains it; nothing enforces it.
    ///     </para>
    ///     <para>
    ///         These tests are the enforcement. They exercise the two data structures DIRECTLY, outside the
    ///         registry, so the comparison stays valid however <c>DwarfMapperRegistry</c> is later refactored,
    ///         and they carry the numbers that make the choice obvious.
    ///     </para>
    ///     <para>
    ///         Measured 2026-09-10: bag enumeration costs <c>80 + 24·N</c> bytes. At N = 10 that is 296 B and
    ///         indistinguishable from a legitimate cost — which is exactly why the benchmark suite's exact-byte
    ///         allocation pins could never have caught it, and why the slope, not the byte count, is the thing
    ///         worth testing.
    ///     </para>
    /// </remarks>
    [Collection("allocation-isolated")]
    public class RegistryStructureProofTests
    {
        private const int Small = 10;
        private const int Large = 2_500;

        private static (Type Source, Type Destination, Func<object, object> Map) Entry()
        {
            return (typeof(int), typeof(long), static x => x);
        }

        /// <summary>
        ///     THE DEFECT. Asserts the bag IS linear — a test that fails if the premise of the fix ever stops
        ///     being true, which would mean the fix is now cargo cult and the reasoning needs revisiting.
        /// </summary>
        /// <remarks>
        ///     A deliberately inverted assertion. Every other test in this assembly asserts good behaviour;
        ///     this one pins the bad behaviour of the structure that was removed, because that is the whole
        ///     justification for having removed it. If a future .NET makes bag enumeration allocation-free,
        ///     this test goes red and the correct response is to delete it and the paragraph in
        ///     <c>DwarfMapperRegistry</c> that cites it — not to silence it.
        /// </remarks>
        [Fact]
        public void The_removed_structure_was_linear_in_the_number_of_registered_maps()
        {
            var smallBag = new ConcurrentBag<(Type, Type, Func<object, object>)>();
            for (var i = 0; i < Small; i++) smallBag.Add(Entry());

            var largeBag = new ConcurrentBag<(Type, Type, Func<object, object>)>();
            for (var i = 0; i < Large; i++) largeBag.Add(Entry());

            var atSmall = CostContract.BytesPerOperation(() => Walk(smallBag), 2_000);
            var atLarge = CostContract.BytesPerOperation(() => Walk(largeBag), 2_000);

            var perEntry = (atLarge - atSmall) / (Large - Small);

            Assert.True(perEntry > 8,
                $"ConcurrentBag enumeration cost {atSmall:F0} B at {Small} entries and {atLarge:F0} B at " +
                $"{Large:N0} — a slope of {perEntry:F1} B/entry. The round-30 fix replaced this structure " +
                "BECAUSE it was linear. If the slope is now gone, the fix's justification is gone with it: " +
                "delete this test and the remark it backs in DwarfMapperRegistry rather than muting it.");

            // And the size of the problem at consumer scale, stated so the number is not folklore.
            Assert.True(atLarge > 20_000,
                $"at {Large:N0} entries a bag walk allocated {atLarge:F0} B — the defect was that this is " +
                "paid on EVERY ambient interface-path call.");
        }

        /// <summary>
        ///     THE FIX. The same workload over the copy-on-write array the registry now uses: flat, and
        ///     allocating nothing at all.
        /// </summary>
        [Fact]
        public void The_replacement_structure_is_flat_against_scale()
        {
            var arrays = new Dictionary<int, (Type, Type, Func<object, object>)[]>
            {
                [Small] = BuildArray(Small),
                [Large] = BuildArray(Large)
            };

            var (atSmall, atLarge) = CostContract.MeasureAgainstScale(
                scale => () => Walk(arrays[scale]),
                Small,
                Large);

            Assert.True(atLarge <= CostContract.FlatCeiling(atSmall),
                "copy-on-write array walk (the structure DwarfMapperRegistry.InterfaceMaps now uses) is NOT " +
                $"flat against scale: {atSmall:F0} B at {Small} entries, {atLarge:F0} B at {Large:N0}. " +
                $"That is roughly {(atLarge - atSmall) / (Large - Small):F2} B per entry — an indexed for " +
                "loop over an array must not allocate per element.");
        }

        /// <summary>
        ///     The two structures, same workload, side by side — so the ratio is a measured artefact rather
        ///     than two numbers a reader has to hold in their head from separate tests.
        /// </summary>
        [Fact]
        public void At_consumer_scale_the_replacement_is_orders_of_magnitude_cheaper()
        {
            var bag = new ConcurrentBag<(Type, Type, Func<object, object>)>();
            for (var i = 0; i < Large; i++) bag.Add(Entry());
            var array = BuildArray(Large);

            var bagBytes = CostContract.BytesPerOperation(() => Walk(bag), 2_000);
            var arrayBytes = CostContract.BytesPerOperation(() => Walk(array), 2_000);

            Assert.True(arrayBytes < 64,
                $"the replacement allocated {arrayBytes:F0} B per walk; a for-loop over an array field must " +
                "allocate nothing.");

            Assert.True(bagBytes > arrayBytes * 100,
                $"at {Large:N0} entries: bag {bagBytes:F0} B/walk vs array {arrayBytes:F0} B/walk. The " +
                "round-30 finding was a 2,330x reduction on this path; a ratio below 100x means the " +
                "structures are no longer meaningfully different and this proof has stopped proving.");
        }

        private static (Type, Type, Func<object, object>)[] BuildArray(int n)
        {
            var array = new (Type, Type, Func<object, object>)[n];
            for (var i = 0; i < n; i++) array[i] = Entry();
            return array;
        }

        /// <summary>
        ///     The bag's walk: <c>foreach</c>, which is the only way to read a bag and is exactly what
        ///     <c>DwarfMapperRegistry.Map</c> did before the fix.
        /// </summary>
        private static void Walk(ConcurrentBag<(Type Source, Type Destination, Func<object, object> Map)> entries)
        {
            foreach (var entry in entries)
                if (entry.Source is null)
                {
                    throw new InvalidOperationException("unreachable; keeps the walk observable");
                }
        }

        /// <summary>
        ///     The array's walk: an indexed <c>for</c> over the array reference, which is what
        ///     <c>DwarfMapperRegistry.Map</c> does now.
        /// </summary>
        /// <remarks>
        ///     Deliberately NOT <c>foreach</c> over <see cref="IEnumerable{T}" />. That would box an
        ///     enumerator per walk and charge the array arm an allocation the real code does not make — the
        ///     comparison would then measure how the test iterates rather than how the registry does.
        /// </remarks>
        private static void Walk((Type Source, Type Destination, Func<object, object> Map)[] entries)
        {
            for (var i = 0; i < entries.Length; i++)
                if (entries[i].Source is null)
                {
                    throw new InvalidOperationException("unreachable; keeps the walk observable");
                }
        }
    }
}
