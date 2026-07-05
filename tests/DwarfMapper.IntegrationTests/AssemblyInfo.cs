// SPDX-License-Identifier: GPL-2.0-only

// The security regression tests temporarily swap CultureInfo.CurrentCulture (to prove our generated
// Parse/ToString stay invariant). That mutation is process-thread-wide, so running tests in parallel
// could let the de-DE window bleed into a concurrently-running test. Disabling parallelization keeps the
// suite deterministic; it costs little (the whole integration suite runs in well under a second).

[assembly: CollectionBehavior(DisableTestParallelization = true)]
