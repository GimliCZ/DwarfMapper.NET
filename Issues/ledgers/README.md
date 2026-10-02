<!-- SPDX-License-Identifier: GPL-2.0-only -->

# SDD ledgers, committed for durability

These are copies of the git-ignored SDD ledgers from `.superpowers/sdd/`, committed because they are the
only record of the rulings made during rounds 19 and 20 — decisions taken on the maintainer's behalf, each
with what it costs if wrong. A lost worktree would lose them.

Search for `Ruling:` to find every decision.

The rounds 19 and 20 copies were committed in `656042c2`, on top of `069987f7`. Until round 32 this line read
"Copied at commit $(git …)": the command was written into the file and never run. Each later file's capture
point is the commit that added it. Find it with:

    git log --diff-filter=A --format=%h -- Issues/ledgers/<file>

Some later files also state their capture in their own header.
