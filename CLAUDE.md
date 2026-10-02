<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Instructions for agents

DwarfMapper.NET is developed in numbered rounds, mostly by agents, under rules its owner has set one ruling at a
time. `Issues/ROUND-PROTOCOL.md` describes how that works: the pattern, the goal an unattended run pursues, and the
rules it keeps. The per-turn procedure is the `round` skill. Read the protocol before you start or continue a round,
and before any unattended work.

These rules hold in every session, attended or not. The protocol's §3 and §4 give the full text.

- **One SDK.** Build and test only with the SDK that `global.json` pins. If it cannot be installed, say so and
  stop. Never use another SDK or loosen the pin.
- **Loud, never silent.** Never silence a reported problem, and never loosen a gate to get green. That rules out:
  - a new suppression (`NoWarn`, `#pragma`, `[SuppressMessage]`) or skipped test that no approved task or owner
    ruling calls for;
  - a lowered floor;
  - regenerated golden output that a task did not declare.
- **Evidence.** A fix starts with a test that fails for the stated reason. A number in a commit or a document was
  measured on this tree, or it is labelled as inference.
- **Rulings stand.** Recorded owner rulings under `Issues/` stand. Before you call something a defect or propose
  work, search those rulings and `git log`, after unshallowing the clone.
- **Pushing.** Push only where the owner or this session's instructions allow: in a cloud session, its designated
  branch; on the owner's machine, only with the owner's say-so for each push. Never amend, rebase or force-push.
- **Owner-only actions.** Unless the owner asked for it in this session, never:
  - push to `master`, merge, tag or release;
  - publish a package;
  - open or label a PR, or trigger a workflow;
  - change repository or environment settings;
  - edit these instructions.
- **Outside this repository,** change nothing except the toolchain, package caches and scratch files.

This file holds no counts and no status. It changes only by owner ruling, flagged in the commit subject.
