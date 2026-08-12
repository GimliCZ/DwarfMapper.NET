<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper.DifferentialTests

**One payload, three mappers, compared member by member.**

DwarfMapper, [Mapperly](https://github.com/riok/mapperly) and AutoMapper each map the same source object; the
results are walked reflectively and any disagreement is reported as a path — `Stops[1].City` — rather than as
"not equal".

## Why an outside oracle

Every real defect Round 18 found hid behind a **corpus hole**, not behind subtle generator code. This
repository's corpus is self-authored, so it shares its author's blind spots by construction. The fuzzer and
`[RoundTrip]` prove *self-consistency*, which is a genuinely weaker property: a mapper that is wrong the same
way in both directions round-trips perfectly.

Mapperly's semantics were designed by other people, and AutoMapper's by others again. Agreement with them is
evidence from outside — the one kind this repository could not previously produce.

## The accepted-divergence list is the deliverable

Three mappers designed independently will differ on documented axes. Each difference is either a defect or a
decision, and `AcceptedDivergences.cs` is where the decisions are written down — with the axis named and the
reason stated. That turns claims in `docs/COMPARISON.md` from prose into something executable: an assertion
about how DwarfMapper differs from Mapperly now fails if it stops being true.

Three guards keep the list honest:

- **Every entry states a reason.** A one-word justification is how an allowlist becomes a way of making red
  things green.
- **Every entry names an oracle this harness actually runs.**
- **Every entry is exercised.** A stale entry is worse than no entry: it reads as a documented difference
  while silently covering whatever else falls under its path prefix. If the difference is gone, delete the
  entry — that is the ratchet tightening.

The current list has one axis on it, deliberately chosen because the three mappers really do disagree:
enum→string. DwarfMapper reads `[Description]`/`[EnumMember]` by default (which is why `DWARF083` exists);
Mapperly and AutoMapper use the identifier. The *same shape* is also compared under
`EnumStringSource.Identifier`, where all three agree — which is what makes the divergence a decision with a
one-line switch rather than an incompatibility.

## When a comparison fails

One of three things is true, and the harness cannot tell you which:

1. **DwarfMapper is wrong** — the case this project exists to find.
2. **The oracle is wrong.** It happens; say so in the accepted-divergence entry.
3. **They differ deliberately** — add an `AcceptedDivergence` naming the axis *and* the reason.

## Adding shapes

Add the types to `Shapes.cs`, declare the pair on all three mappers, and add the comparison to
`ShapeCatalog.All()`. Both the per-shape assertions and the ledger check read that one enumerable, so a new
shape is covered by both without touching either test.

Shapes should be written the way real DTOs are written, not the way a generator author would choose to test
one — that is the entire point. See task **R18-29** for harvesting shapes from public projects: harvest the
*shape*, never the text.

## Licensing

`Directory.Packages.props` pins Mapperly 4.3.1 and Mapster 10.0.8 (MIT) and **AutoMapper 14.0.0 — the last MIT
release; v15+ is RPL-1.5 and GPL-incompatible**. This project is `IsPackable=false` and the shipped library
references none of them.
