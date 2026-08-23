<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 24 — execution ledger

**This is a session record, not a copy of a `.superpowers/sdd/` ledger.** Rounds 19–23 ran in SDD worktrees
and their ledgers here are copies; round 24 ran directly on `master` in one session, so there was no
git-ignored original to preserve. It lives in this directory anyway for one reason: the directory is where
`grep -rn 'Ruling:' Issues/ledgers/` finds every decision taken on the maintainer's behalf, and a round whose
rulings are absent from that sweep is a round whose rulings are lost.

Substance is in `Issues/round24/ROUND24-GENERATED-CODE-AUDIT.md`. This file records only what was DECIDED,
with the date and the cost if wrong, per the house rule.

Scope of the round, as the maintainer framed it: the repo-wide reformat repair ("formatting fixes"), then
`EmitCompilerGeneratedFiles` and the static-analysis audit of the emitted output ("emit and analyze fixes").

---

## Ruling: blanket suppression of the generated-code findings is REFUSED. 2026-08-23.

Asked directly whether suppressing was reasonable. It is not, for the findings as a population: CA1822 fired
1,066 times, and 360 of those are a real and actionable win on generator-owned helpers. A blanket
suppression would have buried that actionable subset along with the noise, and the audit would have reported
"clean" while measuring nothing — the failure mode this repository has now produced six times and exists to
prevent.
**Cost if wrong:** none identified; the alternative (per-rule severity in `.editorconfig`) is still open and
is what an always-on gate needs.

## Ruling: the emission property lives in `Directory.Build.targets`, Debug-only. 2026-08-23.

Not `.props`, which the maintainer's own note proposed. Measured, not argued: `.props` is imported BEFORE the
SDK defines `BaseIntermediateOutputPath`, so the output landed in project roots, the `**/*.cs` glob compiled
it, and the build produced 2,004 × `CS0111` plus 1,084 × `CS0757`.
**Cost if wrong:** the audit is opt-in and Debug-only rather than always-on. Accepted deliberately — see the
blocker below.

## Ruling: `sealed` on generated types — NO. 2026-08-23.

CA1852's 4 sites are all the co-located `[GenerateMap]`-on-a-DTO path. Declined because the type is emitted
`partial`, which IS a consumer extension point, and Gallery example 15 documents the emitted type by name.
**Cost if wrong:** a virtual dispatch not devirtualized, on a type normally reached through a static
extension method where the call is already direct. Cheap to revisit; the argument to beat is the `partial`
one, not the perf one.

## Ruling: `static` on the 360 generator-owned helpers — worth doing, but SPECIFIED not improvised. 2026-08-23.

Filed as **I22** rather than shipped. A wrongly-`static` helper does not fail here; it fails in a CONSUMER's
build, which is the silent-emitted-CS-error class this repository already hunts as I5, I14 and I17.
**Cost if wrong (i.e. if it is never done):** a `this` argument on every synthesized helper call, forgone
inlining. Real, but recoverable at any time.

## Ruling: the model-only shortcut for I22 is REFUSED, and I22 stays filed. 2026-08-23.

Attempted after the maintainer said proceed. A predicate over the EXISTING model — "static iff no hooks, no
converter, no predicate, no value-expression" — needing no new state, is unsound for three independently
verified reasons (constructor-injected instance state; the helper call graph existing only as rendered text
inside `SynthesizedMethod(string, string)`, making `static` a fixed point over a graph the model does not
hold; and `IsSynthesized` being a naming convention rather than a flag). **Accepted by the maintainer the
same day**, closing round 24 with I22 open.
**Cost if wrong:** none to correctness — the default stays instance, which is today's behaviour. The cost is
the deferred win above.

## Ruling: floors are raised to MEASURED values in the same commit, never predicted. 2026-08-23.

Mutation `break` to 84 / 95 / 97 and all five coverage floors re-measured. Enforced against me, not by me:
raising `break` to 84 broke R3 until the equivalents ledger was refreshed in the same commit, and a floor
written as `93.4458` broke `RatchetInvariantScanTests`, which demands the `line/branch` provenance pair.
**Cost if wrong:** a floor above the achievable score blocks every future commit until lowered — which is the
ratchet working, not failing.
