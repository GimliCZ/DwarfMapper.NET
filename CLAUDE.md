<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Instructions for agents

DwarfMapper.NET is developed in numbered rounds, mostly by agents, under rules its owner has set one ruling at a
time. `Issues/ROUND-PROTOCOL.md` describes how that works: the pattern, the goal an unattended run pursues, and the
rules it keeps. The per-turn procedure is the `round` skill. Read the protocol before you start or continue a round,
and before any unattended work.

These rules hold in every session, attended or not. The protocol's §3 and §4 give the full text.

- **One SDK.** Build and test only with SDK 10.0.101 (`global.json`). If it cannot be installed, say so and stop.
  Never use another SDK or loosen the pin.
- **Loud, never silent.** Never silence a reported problem, and never loosen a gate to get green. That means no new
  `NoWarn`, `#pragma`, `[SuppressMessage]` or skipped test, no lowered floor, and no regenerated golden output that
  a task did not declare.
- **Evidence.** A fix starts with a test that fails for the stated reason. A number in a commit or a document was
  measured on this tree, or it is labelled as inference.
- **Rulings stand.** Recorded owner rulings under `Issues/` stand. Before you call something a defect or propose
  work, search those rulings and `git log`, after unshallowing the clone.
- **Owner-only actions.** Unless the owner asked for it in this session, never:
  - push to `master`, merge, tag or release;
  - publish a package;
  - change repository or environment settings;
  - edit these instructions.

  Never write outside this repository.

This file holds no counts and no status. It changes only by owner ruling, flagged in the commit subject.
