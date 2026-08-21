// SPDX-License-Identifier: GPL-2.0-only

// This assembly used to set [assembly: CollectionBehavior(DisableTestParallelization = true)], citing one
// reason: the security regression tests swap CultureInfo.CurrentCulture, and a de-DE window must not bleed
// into a concurrently running test. That constraint is real, but it is a property of a handful of classes,
// and its cost justification ("it costs little — the whole integration suite runs in well under a second")
// expired the day this assembly became a mutation-testing target: under Stryker the suite runs once per
// mutant, and an assembly-wide serial order is paid on every one of those runs
// (Issues/round21/RESEARCH.md, R21-3).
//
// The narrow constraint is now applied at its actual scope, via serial collections:
//   - "culture-swap"      (CultureSwapCollection)    — every class that swaps CurrentCulture/CurrentUICulture;
//   - "registry-torture"  (RegistryTortureCollection) — contention measurement needs a quiet machine.
// xunit runs DisableParallelization collections one at a time after the parallel collections finish, so the
// de-DE window still cannot overlap anything. Everything else parallelises again.
//
// The second shared-state hazard here — DwarfMapperRegistry, a process-wide mutable static populated by
// module initializers — was audited for this change (2026-08-21): every registering test outside the torture
// collection registers its own distinct closed types and asserts per-key (Register/TryGet/IsProvided/Map),
// and the one Provided read outside the torture collection (AmbientRegistryTests) is an Assert.Contains of
// a key that test itself registered — safe while other collections register different keys concurrently.
// A future test that asserts over registry-GLOBAL state (enumeration, counts) must join a serial collection.
