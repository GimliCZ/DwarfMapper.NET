// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     <b>A heavy theory's rows, dealt across classes so they run in parallel.</b> Round 34 (E12).
    ///     <para>
    ///         xunit runs one class's tests one after another ("tests within a single test collection will not be run in
    ///         parallel against each other", and a class is a collection by default), so a theory with hundreds or
    ///         thousands of rows in one class sets a parallel run's length: <c>FeatureCombinationFuzzTests</c>' 3,565
    ///         deep-tier rows ran alone for the deep tier's last 14 s, and <c>CombinatorialEngineTests</c>' identity cells
    ///         finished build-test's run 5 s after an even spread would have. A split theory keeps its body once, in its
    ///         class, and each part is a class named after it with the part's number (<c>CombinatorialEngineTestsPart2</c>),
    ///         a theory over its share of the rows, dealt round-robin here. The parts are top-level classes rather than
    ///         nested ones because xunit only runs public classes and CA1034 refuses public nested types; the name keeps
    ///         every name-substring filter in CI and the scripts matching.
    ///     </para>
    ///     <para>
    ///         Stryker's sessions run serially, so the split changes nothing there; the killers-first ranking ranks a part
    ///         as its outer class (<see cref="KillersFirstOrderer.RankOf" />). <c>TheoryPartsTests</c> holds every split
    ///         theory's parts to dealing each row exactly once.
    ///     </para>
    /// </summary>
    internal static class TheoryParts
    {
        /// <summary>The rows whose position modulo <paramref name="parts" /> is <paramref name="part" />.</summary>
        internal static IEnumerable<T> Of<T>(IEnumerable<T> rows, int part, int parts)
        {
            return rows.Where((_, i) => i % parts == part);
        }
    }
}
