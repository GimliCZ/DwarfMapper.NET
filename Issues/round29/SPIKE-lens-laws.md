<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Spike: the lens laws — what the update-into endpoint actually satisfies

Phase 4. The plan's instruction was: *"state the laws the update-into endpoint satisfies and fuzz them —
GetPut for `Update(src, dest)` is exactly 'mapping the same source twice is idempotent', which a fuzz oracle
can check today; it is cheap and it closes the loop the theory says is open."*
(`RESEARCH-hardware-mode.md` §11b.7.)

Unlike the arena spike, this one **shipped**: `DwarfMapper.Testing.LensLaws`, with tests, because the
finding was not "here is a number to weigh" but "here is a law nothing checks".

## What was already true

`[RoundTrip]` and `RoundTrip.Verify` check **PutGet** — `Back(Forward(x))` reproduces `x`. That was known.

What was *not* known before this spike, and is the substantive output: the generator suite **already fuzzes
update-into idempotence**, in `MetamorphicPropertyFuzzTests.Update_into_is_idempotent`, and it does so over
a destination built by `Activator.CreateInstance(dstType)`.

```csharp
var dst = Activator.CreateInstance(dstType)!;   // ← every member at its default
update.Invoke(mapper, [source, dst]);
// …compare dst against source, twice
```

**A fresh destination cannot distinguish a member the mapper leaves alone from a member it writes
correctly.** Both read as "equal to the source's corresponding value" when the source's value happens to
match the default, and both read as "absent" otherwise — and the comparison is against the *source*, so a
member the mapper never touches is scored by whether the default coincides. Every partiality defect —
a member dropped from the write set by a resolution bug, a directive that silently stops applying — is
outside what that instrument can see. This is the round's signature failure shape for the seventh time, and
it was sitting inside a test whose name says "idempotent".

`PutPut` was not checked at all, in any form.

## What was built

`LensLaws`, two verifiers, both over a **populated** destination drawn from the same seeded factory the
round-trip verifier already uses:

| | law | what it asserts |
|---|---|---|
| `VerifyIdempotent` | GetPut | `update(s, d)` twice equals `update(s, d)` once |
| `VerifyLastWriteWins` | PutPut | `update(a, d); update(b, d)` equals `update(b, d)` alone |

Both destinations in a comparison are built from one item seed, so a failure carries **one integer that
replays the entire case** — through `LensLaws.DestinationSeedSalt` and `LensLaws.SecondSourceSeedSalt`,
which are public for exactly that reason. `LensLawException` names the law and renders the structural diff,
the same informed-dump shape `RoundTripException` established.

## The result worth recording: PutPut is not universal, and saying so is the point

**PutPut requires the set of members written to be independent of the source's *values*.** A mapper using
`[MapNullSkip]` or `[MapProperty(When = ...)]` decides what to write *from the source it was given*, so a
member the first source wrote and the second skipped keeps the **first** source's value. That is a genuine
PutPut violation and the correct behaviour for that endpoint.

So `VerifyLastWriteWins` is **opt-in per mapper**, not a blanket assertion, and the test suite carries the
violation as a *positive* control rather than hiding it: a null-skipping updater must throw, and the same
updater must still satisfy GetPut. Asserting both directions is what stops the caveat from reading as
"conditional mappers are simply unverifiable".

GetPut has no such caveat — the same source makes the same decisions — which is why it is the law the
research named as checkable "today".

## What this does not close

- **The generator's own fuzz still uses a fresh destination.** `LensLaws` is a consumer-facing verifier; it
  does not retroactively fix `MetamorphicPropertyFuzzTests`. Pointing that fuzz at a populated destination
  is a separate change with its own population and its own failures to triage — round-30 work, recorded in
  `Issues/round30/BLIND-INSTRUMENTS.md` alongside the other instruments trusted for coverage they lack.
- **No law is asserted about `Map` versus `Update` agreeing.** `Update(s, new TDest())` should equal
  `Map(s)` for a total mapper, and the two are emitted by different code paths. That is the third law worth
  having and it is not built here.
