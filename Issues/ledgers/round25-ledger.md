<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 25 — execution ledger

**A session record, not a copy of a `.superpowers/sdd/` ledger** — same as round 24. It lives here because
`grep -rn 'Ruling:' Issues/ledgers/` is how a decision taken on the maintainer's behalf gets found later, and
a round absent from that sweep is a round whose reasoning is lost.

Substance: `Issues/round25/TASKS.md` (the plan, with each task's outcome recorded in place),
`Issues/round25/ROUND25-SIMD-RFC.md` (the research it came from), and
`benchmarks/results/2026-08-23-round25-kernels.md` (every number quoted below).

Branch `feat/round25-simd`, off `master` at `2da36be`.

**The through-line of this round: three of the RFC's seven entries did not survive measurement, and one
survived in a smaller form than proposed.** That is not a failure of the RFC — v2 measured kernels in
isolation, which scores an idea against nothing rather than against what the product already does. It is the
reason every entry here was probed before it was built.

---

## Ruling: the RFC's "name-independent" blit is REFUSED. 2026-08-23.

R25-02 specified matching on "offsets and types, not member names". The existing proof requires field names
to align, deliberately — its own comment says *positional == name-based requires same names*. DwarfMapper
maps by NAME, so a positional reinterpret is equivalent only when the names agree; name-independence would
blit `struct A(int X, int Y)` onto `struct B(int Y, int X)` and silently swap the values.
**Cost if wrong:** none — the behaviour the RFC asked for already ships as the explicit `[Reinterpret]`
opt-in, so a caller who wants positional semantics can still say so, in their own declaration.

## Ruling: DWARF100 is scoped to a NEAR-MISS, narrower than the RFC. 2026-08-23.

R25-07 asked for a diagnostic "whenever a pair looks blittable but cannot be proven". Built that way it fires
on every ordinary struct-array mapping whose members differ, and a hint that common is suppressed wholesale
by the first consumer who meets it — taking the cases worth reading with it. Scoped instead to pairs blocked
by exactly one identifiable thing; differing field counts or types stay silent.
**Cost if wrong:** a consumer with an unusual shape gets no hint. Cheap to widen — the classifier is one
function.

## Ruling: DWARF100 stays **Info**. 2026-08-23. Severity was reconsidered twice and the answer did not move.

The maintainer's requirement was stated plainly — *"don't force user, but give him suggestion"* — and asked
first for Warning, then for Error. Both were attempted; neither delivers that requirement, for reasons found
by building rather than by arguing:

* **Warning is advisory only until someone enables `TreatWarningsAsErrors`**, at which point it forces. Not
  hypothetical: flipping the severity broke THIS repository's own build, because the T4 scalar-twin struct is
  deliberately `[StructLayout(Auto)]` and therefore a genuine near-miss. A consumer with a perfectly correct
  mapping would have had to add a `NoWarn` line to keep building. That is the trap `DWARF070` sprang once.
* **Error is categorically worse here, and the reason is specific to this generator.**
  `MapperClassModel.HasBlockingError` is `Diagnostics.Any(d => d.IsError && !d.ScopedToMethod)`, and a
  blocking error SUPPRESSES EMISSION. So an Error on DWARF100 would not merely fail a build — a mapping that
  is completely correct, and merely copies element-by-element instead of in one block, would generate **no
  code at all**, collapsing the consumer's build into `DWARF078` plus a `CS8795` cascade. A performance hint
  would delete the feature it is hinting about.

Info is the only severity in Roslyn that keeps "suggest, do not force" unconditionally. The visibility
problem that made Warning tempting is real and is answered where it belongs — `docs/howto/deploy-and-optimize.md`
names DWARF100 in the fast-path section, so somebody tuning a hot path finds it by looking rather than by
being interrupted.
**Cost if wrong:** a consumer who never opens the IDE's suggestion list misses a speed-up on a mapping that
already works. Reversible in one line — but see the Error finding above before reaching for it.

## Ruling: no small-`n` guard on the List blit, against the RFC's `Count >= 32`. 2026-08-23.

Measured locally: the crossover is between n=4 and n=8, not 32, and below it the penalty is tens of
nanoseconds against multi-fold gains above. A runtime branch to dodge a 20 ns loss, calibrated on a constant
already known wrong on one machine, costs more than it buys.
**Cost if wrong:** a few nanoseconds on collections of four elements.

## Ruling: the perf gate sits at n=1000, not at the large size the plan inherited. 2026-08-23.

The plan specified ">= 1.5x at the large-n regime". On this hardware that gate **fails on green code**: at
n=65536 the struct shapes measure 0.79–1.16x and array→List reproduces *below* 1.0 across two runs, because
at ~1 MB both arms are bandwidth-bound. The win lives in-cache, which is where real DTO collections live.
**Cost if wrong:** a large-collection regression would not be caught by this gate. Accepted: the gate's
purpose is detecting a de-emitted fast path, not measuring throughput.

## Ruling: enum arrays blit only where the SCALAR path is itself a reinterpret. 2026-08-23.

R25-03's gate — "underlying types equal AND value sets identical" — would have shipped a behaviour change.
The default `ByName` strategy emits a switch ending in `throw new ArgumentOutOfRangeException`, and an enum
may legally hold any value of its underlying type, so a blit passes through what the mapping rejects. Only
`ByValue` over the same underlying type, and an enum against its own underlying primitive, qualify — both
are identity `CreateChecked`. (The RFC's predicate was separately unsound: `{A=1,B=2}` and `{A=2,B=1}` have
identical value *sets* while by-name mapping sends 1→2 and a blit preserves 1.)
**Cost if wrong:** `ByName` users keep the element loop. Correct by construction; the alternative was silent.

## Ruling: R25-06's metadata allowlist is DECLINED. 2026-08-23.

Measured first: `LayoutIdentical` returns true immediately for two identical types, so a `decimal` or `Guid`
FIELD never reaches the in-source check. The realistic shape — a DTO carrying a money and an identifier — has
always blitted. What remained was exotic, and buying it means weakening the in-source rule that the whole
refusal gate rests on.
**Cost if wrong:** an element type that IS a metadata struct paired with a different layout-aligned type
stays scalar. Nobody has asked for it. The short-circuit is now PINNED, which is what the task actually
bought: it was load-bearing and accidental.

## Ruling: R25-01 checked narrowing is PARKED PERMANENTLY, with a pin. 2026-08-23.

`TensorPrimitives.ConvertChecked` is scalar-only by design (fallback declares `Vectorizable => false`; the
Vector128/256/512 paths throw), throws without naming the faulting element, and leaves partial writes. Both
contestants being scalar, the container's 1.49x is anomalous and must not be cited; the RFC's own hand-written
kernel measured 0.27x. An emitted-source pin now fails the build if a `TensorPrimitives` call or a vector
kernel ever appears on the narrowing path.
**Cost if wrong:** a future genuine vectorised narrow has to delete a test to land — which is the point.

## Ruling: R25-04 skip-if-identical is NOT SHIPPED. **RATIFIED BY THE MAINTAINER 2026-08-23** ("T5 should be skipped")

Probing before building found two facts the plan did not have. Its justification is largely already served —
`[MapCollectionKey]` keeps the destination list instance and upserts by key, which is the EF
"don't dirty unchanged entities" story for the same-element `List<T>` members navigation properties are. And
the cost is worse than believed: comparing first loses **even on a hit** from n=16 to n=16,384, bottoming at
0.28x. New public attribute surface, overlapping an existing option, to buy a 3.5x slowdown in the common
case is a bad trade.
**Cost if wrong:** the non-keyed and cross-element-type cases `[MapCollectionKey]` cannot reach have no
answer — if that gap is ever felt, the measurement above is what the feature's documentation must carry, and
n >= 65,536 is the band to recommend it in rather than warn about. Raised as a ratification item because the
plan had already committed to shipping it; the maintainer ruled **skip** the same day, so the decision is
theirs and not an inference from a benchmark.
*(One correction to the record came out of the measurement: the container concluded skip-if-identical is
NEVER faster, at 0.49x for n=65536. Locally a hit there is **4.20x faster** — skipping a megabyte of writes
beats paying for them. "Never faster" is wrong at the top end and right everywhere else.)*

## Ruling: Dictionary and HashSet cannot blit — a refusal on four grounds, not a backlog item. 2026-08-23.

Asked directly by the maintainer. Probed rather than recalled: `CollectionsMarshal` exposes a span for
`List<T>` only; `Dictionary` and `HashSet` keep entries in a private nested `Entry` struct whose layout is an
implementation detail; reaching it needs reflection or unsafe punning, against two stated project
commitments; and hash codes are baked into the entries, so changing the key type invalidates every one.
`Queue`/`Stack` likewise — private arrays, and `Queue` wraps head-to-tail so it is not even contiguous in
logical order.
**Cost if wrong:** none identified. Recorded WITH the reasoning so round 26 does not re-derive it.

---

## Ruling: `bool` non-normalization is pinned as an EQUIVALENCE, not as a value. 2026-08-23.

The plan posed this as a binary — pin the non-normalization as documented behaviour, or normalize and give up
the blit for `bool`-bearing types. Measuring first showed a third answer is the right one.

**Measured** (.NET 10.0.1, x64, Release): a `bool` holding byte 2 survives *every* path unchanged — the
element loop, the block copy, a plain `bool[]` loop, and even a box/unbox round trip. The two emitted
strategies do not diverge, so this was never a correctness question. *(Also measured, and contrary to what
one would expect: `weird == true` evaluates TRUE, because the JIT lowers `x == true` to `x != 0`. The odd
byte is invisible even to equality.)*

So the tests pin **that the two paths agree**, across bytes 0, 1, 2 and 0xFF — and deliberately do NOT pin a
particular byte value. The C# specification says nothing about non-canonical bools; preserving byte 2 is
current JIT behaviour, not something DwarfMapper is in a position to promise. Pinning the value would turn an
implementation detail of .NET into a contract owed to consumers forever. Pinning the agreement catches what
would actually be a defect — the two paths drifting apart, which is exactly how a future JIT change would
surface.
**Cost if wrong:** none identified. The weaker pin cannot fail for a reason that is not a real divergence.

---

## Still open for the maintainer

Nothing. Every item raised during round 25 is now decided and recorded:

* **T5 skip-if-identical** — ruled **skip** by the maintainer.
* **`DWARF100` severity** — **Info**, after Warning and Error were both attempted and found to break the
  stated requirement.
* **`DWARF100` near-miss scoping** — narrower than R25-07, on the noise argument.
* **`bool` non-normalization** — pinned as an equivalence.
