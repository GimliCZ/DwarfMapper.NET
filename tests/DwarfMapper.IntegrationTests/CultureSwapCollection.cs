// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests;

/// <summary>
///     The serial collection for every test that swaps <c>CultureInfo.CurrentCulture</c> /
///     <c>CurrentUICulture</c> (to prove generated Parse/ToString stay invariant). The swap is restored in a
///     <c>finally</c>, but during the window it is ambient state: xunit reuses worker threads and culture
///     flows with the ExecutionContext, so a de-DE window overlapping a concurrently running
///     formatting-sensitive test would be a race with no deterministic reproduction. Marking the collection
///     <c>DisableParallelization</c> makes xunit run it on its own, AFTER the parallel collections finish —
///     the same containment the assembly-wide <c>CollectionBehavior</c> switch used to buy, at the cost of
///     only these classes instead of all of them (see <c>AssemblyInfo.cs</c>).
/// </summary>
[CollectionDefinition("culture-swap", DisableParallelization = true)]
public sealed class CultureSwapCollection;
