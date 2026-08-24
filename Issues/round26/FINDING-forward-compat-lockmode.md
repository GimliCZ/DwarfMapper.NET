<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: the forward-compat leg cannot hold both an unpinned SDK and a pinned lock file

**Observed 2026-08-23** — CI run #154, job `roslyn-forward-compat`, three `NU1004` errors:

```
The package reference Microsoft.DotNet.ILCompiler version has changed from [10.0.1, ) to [10.0.11, ).
The package reference Microsoft.NET.ILLink.Tasks  version has changed from [10.0.1, ) to [10.0.11, ).
```

## This was predicted, and the prediction was right

The workflow already carried a note from round 22:

> this leg inherits `RestoreLockedMode` from `CI=true` like every job. It deliberately runs an UNPINNED newer
> SDK … if this leg ever reds with `NU1004` while build-test stays green, that IS the diagnosis (SDK-driven
> graph drift, not a lock-file defect).

That is exactly what happened, and the diagnosis is confirmed rather than revised.

**Mechanism.** `Microsoft.DotNet.ILCompiler` and `Microsoft.NET.ILLink.Tasks` are never referenced by hand —
the SDK adds them implicitly for `PublishAot` and `IsTrimmable`, at a version that tracks the **runtime patch
the host SDK carries**. `global.json` pins `10.0.101` with `rollForward: disable`, so every other job resolves
`10.0.1`. This leg deletes `global.json` on purpose, gets whatever `10.0.x` the runner has, and resolves
`10.0.11`. Locked mode then correctly refuses.

**It is not a lock-file defect.** Regenerating the lock file would be wrong twice over: it would pin whatever
the maintainer's machine happens to have, and it would break again on the next runner image.

## What the note did not anticipate

The consequence. Restore fails, so the leg never reaches the question it exists to ask — *does the generator
still load and run under a newer compiler?* It had been dying before its own test since the SDK moved, and a
leg that cannot run its test is worth nothing regardless of what its failure is diagnosing.

## Resolution

`RestoreLockedMode=false` **for this leg only**. Locked mode stays on for every other job, which is where
lock-file integrity is genuinely gated — `build-test`, `reproducible-build` and the rest all keep the pin.

This is not exempting the finding; the finding is recorded here. It is admitting that a deliberately-unpinned
SDK and a pinned lock file are contradictory requirements in the same job, and choosing the one the leg is
named after.

**What this costs:** if a newer SDK ever changed the graph in a way that mattered, this leg would now restore
it silently instead of reding. That risk sits where it belongs — every *pinned* job still refuses graph drift,
so the only thing this leg can no longer detect is drift caused by the unpinned SDK it deliberately uses.
