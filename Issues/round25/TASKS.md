<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 25 — SIMD expansion of the collection fast paths, ordered by layer

## What this file is

`ROUND25-SIMD-RFC.md` is research and it is finished: v1 proposed seven entries, v2 measured them and
falsified two, v3 hardened the survivors against primary sources. **This file does not re-derive any of
that.** It transcribes v3's landing order into executable tasks, resolves the two places where the RFC
contradicts itself or the machine, and carries the standing rules forward.

Prep lands on `master`. Execution branches — `feat/round25-simd`, per the round-23 precedent.

## Two corrections the RFC needs before anyone executes it

**1. The v2 measurements were taken on hardware this repository does not have, and the harness is gone.**
The RFC says the benchmark source is "preserved at `/home/claude/bench/Program.cs` for re-runs". That is a
cloud-container path; it does not exist here, and no SIMD or blit benchmark source exists in `benchmarks/`.
The v2 environment was an AVX2+AVX-512 container with `Vector<long>.Count=4`, on a shared core. **Every
ratio in the v2 table is therefore DIRECTION-ONLY until re-measured locally** — the house rule the
Dict-vs-Mapperly figure taught us (2.13x on Windows against ~1.14x on Linux, for the same code) applies with
full force to numbers taken on a machine nobody here can re-run. In particular the **1.5x gate constant must
be pinned from a local measurement, in the commit that introduces the gate**, never inherited from the
container.

**2. v1 and v3 disagree about when the differential harness lands.** v1 says R25-05 "lands FIRST, before any
entry above"; v3's final order puts it fifth. Both are right about different halves, and v3 is what split
them: the **semantic** differential dissolved into per-entry regression tests (scalar-oracle round-trips,
byte-equality rows against the element loop), while **R25-05-final is only the perf gate**. So the rule for
this round is:

> The scalar oracle test ships in the SAME COMMIT as the blit it guards — no exceptions, it is the
> correctness proof. The perf gate trails, because a gate calibrated before there is anything to measure is
> a constant someone invented.

**3. R25-02 is mostly ALREADY SHIPPED, and the one part of it that is not shipped must never be built.**
Found by reading the gate and its tests rather than the RFC. Evidence:
`src/DwarfMapper.Generator/Pipeline/BlittableProof.cs`,
`src/DwarfMapper.Generator/Pipeline/MapperExtractor.Conversions.cs:317`, and the pins in
`tests/DwarfMapper.Generator.Tests/BlitTests.cs`.

* **Distinct blittable struct pairs already blit, including nested ones.** `LayoutIdentical` recurses
  through nested structs, and `Layout_identical_structs_blit` and `Nested_layout_identical_structs_blit`
  pin it. The RFC's premise — "only primitive arrays hit the blit today" — is false; this landed as
  Plan 15. **The v2 container's 35.8x "struct blit CONFIRMED" row therefore justified nothing that was not
  already built**: it measured standalone kernels against a product gap that does not exist.
* **The RFC's "name-independent (offsets and types, not member names)" is wrong and must be rejected.**
  The proof requires field names to align, deliberately — the source comment says it outright: *positional
  == name-based requires same names*. DwarfMapper maps by NAME, so a positional reinterpret is only
  equivalent when the names line up. Making it name-independent would blit `struct A(int X, int Y)` onto
  `struct B(int Y, int X)` and silently swap the values, which is precisely the mislinking this library
  exists to turn into a build error. `Different_field_names_do_not_blit` pins the refusal.
* **The name-independent behaviour the RFC asks for already exists as an explicit opt-in** —
  `[Reinterpret]` forces the blit past the name proof, pinned by `Reinterpret_forces_blit_skipping_name_proof`.
  A caller who genuinely wants positional semantics says so, and it is their declaration, not our guess.

So **T2's real scope is the List-involved shapes only** — see the rewritten task below.

---

## Status at round close — 2026-08-23

| task | outcome |
|---|---|
| **T0-A** measure locally | DONE — `benchmarks/results/2026-08-23-round25-kernels.md`; overturned two inherited constants |
| **T0-B** layout-equivalence gate | DONE — `DWARF100`, Info, scoped to a near-miss |
| **T1** enum blit | DONE — `ByValue`/underlying only; `ByName` refused on the oracle |
| **T2** List-involved shapes | DONE — array→List, List→array, List→List, plus the whole interface family |
| **T3** metadata allowlist | DECLINED on measurement; the short-circuit it relied on is now pinned |
| **T4** standing perf gate | DONE — ratio gate at n=1000, sabotage-proven both directions |
| **T5** skip-if-identical | NOT SHIPPED — **ruled by the maintainer 2026-08-23**, "T5 should be skipped" |
| **T6** park checked narrowing | DONE — emitted-source pin, with a control |
| **T7** `ImmutableArray<T>` | DONE — both directions, fresh-array wrap pinned |

Suite 8,040 → 8,117. Solution 0 errors / 0 warnings, samples included. Full nightly battery green —
deep suite, coverage floors, ILVerify, benchmark smoke with allocation pins 17/45 exact, and the new blit
ratio gate. Rulings and their cost-if-wrong: `Issues/ledgers/round25-ledger.md`.

**Nothing is left open for the maintainer.** T5 was ruled skip; `DWARF100`'s severity settled at Info after
Warning and Error were both attempted and measured against the stated requirement; the `bool` question is
answered at the foot of this file.

## Layer 0 — instruments, before any emission change

The house rule, learned six times over: a product fix graded by a broken instrument produces a green result
that means nothing. Both tasks here are instruments, not features.

### T0-A — rebuild the kernel-isolation harness in-repo, and re-measure locally

Rebuild what the container had: each candidate measured against its **scalar twin in the same process**, so
shared noise cancels; preallocated destinations, so allocation cost is identical between the arms and the
kernel is what is actually being timed. Sizes must straddle the two regimes v2 identified — in-cache
(n≈16 and n≈1,024) and bandwidth-bound (n≈65,536) — because the ratios differ by an order of magnitude
between them, and a single-size measurement picks a winner by accident.

Payloads come from the fuzzer and fixtures, not hand-built uniform literals (standing rule: uniform data
misrepresents branch prediction and cache behaviour alike).

Output: a dated file in `benchmarks/results/`, carrying local ratios. **Exit criterion: a class that does not
clear the bar locally does not ship, regardless of what the container measured.**

**Measure only the genuinely open paths** (correction 3 removed one of the RFC's headline rows from the
work-list):

* `SetCount` + span copy against the `Add` loop, across the small-`n` region — this is what pins the
  threshold constant, so it is the one measurement the later gate depends on;
* enum blit against the **actual current scalar enum loop**, not against a hand-written stand-in;
* the R25-06 classes.

Do **not** re-measure array→array struct blit. It is shipped product behaviour, so a benchmark of it
measures the past, not a decision.

### T0-B — R25-07, the layout-equivalence gate, minting **DWARF100**

`DWARF099` is the highest id in the generator today, so this is `DWARF100`.

This is Layer 0 rather than a feature because **the gate is the safety property every later task leans on**.
A wrong blit is silent memory corruption — the one failure class this project treats as forbidden — and
widening the allowlist before the refusal logic exists would make each widening carry its own ad-hoc proof.

Gate: unmanaged ∧ `Sequential`/`Explicit` layout ∧ field-by-field offset-and-type equality ∧ equal pack.
Anything else refuses and takes the scalar path.

Hard refusals to encode together with the reason, because they LOOK blittable: `DateTime` and
`DateTimeOffset` are `[StructLayout(Auto)]`, so their layout is not guaranteed and they must never blit.

The diagnostic is **informational** — the mapping still works. The id exists so a consumer who expected the
fast path learns why they did not get it.

**SHIPPED as `DWARF100`, narrower than the RFC specified — a deviation, recorded for ratification.** The RFC
asked for it "whenever a pair *looks* blittable but the generator cannot prove identical layout". Built that
way it would fire on every ordinary struct-array mapping whose members happen to differ, and an
informational diagnostic that common gets suppressed wholesale by the first consumer who meets it — taking
the cases worth reading down with it. So it is scoped to a genuine **near-miss**: a pair whose field counts
or field TYPES differ is silent, because that is an ordinary mapping and not a missed fast path. Three
blockers report, each with its remedy: non-Sequential layout, a metadata-declared struct, and misaligned
field names.

Two findings from building it, neither of which was in the RFC:

* **A bare name mismatch already fails loudly as `DWARF001`, an Error** — the members cannot be mapped at
  all — so the hint would be redundant noise beside it. The name-mismatch near-miss earns its place only
  once `[MapProperty]` has reconciled the names and the mapping SUCCEEDS. That is the shape where the slow
  path is genuinely invisible, and it is pinned as its own test.
* **Shape must be checked before the layout blockers.** The natural order — blockers first, as the proof
  itself does it — is wrong: every pair of distinct METADATA structs would report without anything having
  looked at their fields. `decimal` is not in `IsPrimitive`, so `decimal[] → Guid[]` is a reachable pair with
  nothing in common that announced itself as nearly layout-identical. Caught by review, not by the suite,
  and now pinned.

Five-file sync, since this mints an id and `Scan9` fails the build without the CHANGELOG entry:
`AnalyzerReleases.Unshipped.md` · `docs/diagnostics.md` (fence-exempt, non-compiling illustration) · a
`NegativeCases` row pinning id AND remedy wording · `CHANGELOG.md` · `docs/generated/diagnostics-index.md`.

---

## Layer 1 — the two confirmed wins, largest first

Both were confirmed by v2 and hardened by v3, and both ride `Buffer.Memmove` behind a generation-time proof.
Neither may land before T0-B, whose gate they call.

### T1 — R25-03, enum arrays as underlying-primitive blit

The largest measured win (container: up to 76x in-cache, 6.4x at bandwidth). `BlittableProof` excludes
`TypeKind.Enum` today, with the reason stated in the source: *by-name enum mapping != byte copy*. R25-03 is
the case for overriding that, and it must clear two bars the RFC states too loosely.

**Real scope.** `Status[] → Status[]` (same type) already takes the Clone/memmove path, pinned by
`Same_type_array_still_uses_clone_not_reinterpret`, so it is NOT a win here. The genuine targets are
**cross-type enum** pairs and **enum ↔ underlying primitive**.

**The RFC's predicate is unsound as written.** It says "value sets identical". That is not enough:
`Src { A = 1, B = 2 }` and `Dst { A = 2, B = 1 }` have identical value *sets*, but by-name mapping sends
`1 → 2` while a blit preserves `1`. The correct predicate is **per-name value identity** — for every member,
the same name carries the same underlying value — which is the same theorem the struct proof enforces:
positional must equal name-based. Reuse the enum converter's existing member analysis, but assert the
stronger property.

**Check before writing any code:** what the current scalar path emits for a cross-enum element when the
source holds an **undefined** value. Enums can carry any value of their underlying type, so this is
reachable without any cast in the caller's code. If the scalar oracle throws or substitutes on an undefined
value, a blit that preserves it is a **semantic change** and the gate must exclude that case; if the oracle
is a plain cast, the blit matches and there is nothing to do. Read the emission — the scalar path is the
oracle, and this is exactly the kind of divergence that is invisible in a green test suite.

### T2 — R25-02, REWRITTEN: blit the List-involved shapes

**Not** "extend blit to structs" — that shipped in Plan 15 (see correction 3). The gate at
`MapperExtractor.Conversions.cs:317` requires `Target == Array && SourceIsArray`, so exactly three shapes
are still scalar even when the element pair is provably blittable:

* `Array → List<T>`
* `List<T> → Array`
* `List<T> → List<T>`

Source side is `CollectionsMarshal.AsSpan(srcList)`; target side is `CollectionsMarshal.SetCount` plus a span
copy. The element proof is **unchanged** — the existing `BlittableProof.CanReinterpret` is reused verbatim,
never relaxed.

**The name-alignment requirement STAYS.** Anyone reading the RFC will be tempted to "fix" the proof to be
name-independent; that would be silent mislinking, and the opt-in for it (`[Reinterpret]`) already exists.
Leave a comment at the gate saying so, because the RFC will outlive the memory of this decision.

Two constraints v3 extracted that carry over unchanged:

* the small-`n` guard is real — below the threshold, `Add` wins about 2x — and the threshold is expressed as
  a **`Vector<T>.Count` multiple**, following dotnet/runtime's own idiom, not as a bare `32`;
* an emitted-shape pin must assert **no throwing statement between `SetCount` and the copy**. `SetCount`
  exposes uninitialized memory until the copy completes, and a throw inside that window is observable.
  Making it structurally impossible beats documenting it.

---

## Layer 2 — widening, once the gate exists and the wins are proven

### T3 — R25-06, expand the allowlist: `Guid`, same-nullability `Nullable<T>`, same-type `decimal`

All three ride the identical template. The load-bearing test is the **cross-nullability refusal**:
`T?[] → T[]` must take the loud path and never blit. That single test is what fails if the gate is subtly
wrong, so it matters more than the three positive cases combined.

**OUTCOME: no allowlist. Measured first, and the cases worth having already work.** `LayoutIdentical`
returns true immediately for two IDENTICAL types, so a `decimal` field against a `decimal` field, or a
`Guid` against a `Guid`, never reaches the in-source check at all — only the TOP-LEVEL element type must be
source-declared. So the realistic shape the RFC was reaching for, a DTO struct carrying a money and an
identifier, has always blitted. Same-type element pairs (`Guid[] → Guid[]`) are identity and already take
the `Clone()` memmove, which is the same `Buffer.Memmove` underneath.

What is left is exotic — an element type that IS a metadata struct paired with a *different*, layout-aligned
type. Buying that would mean punching a hole in the in-source rule, which is the safety property T0-B rests
on, for a shape nobody has asked for. **Declined**, on the same reasoning as `sealed` in round 24: a real
safety rule is not worth weakening for a hypothetical gain.

What DID come out of the task is coverage. The short-circuit was load-bearing and accidental — nothing
stopped a future tightening of the recursion from silently dropping every DTO that carries a `decimal` or a
`Guid` — so `BlitMetadataFieldTests` now pins it, along with the cross-nullability refusal and a `DateTime`
look-alike refusal.

*(For the record, the original obstacle analysis, which still governs if anyone revisits this.)*
**The obstacle is specific, and the fix must not be a relaxation.** `BlittableProof.IsSourceSequential`
requires the type to be declared **in source**, and the reason is sound: only for a source-declared struct
does an absent `[StructLayout]` reliably mean the C# default of Sequential. `Guid` and `decimal` are
metadata types and fail that check today. So the change is an explicit **well-known allowlist** —
`System.Guid`, `System.Decimal`, and `Nullable<T>` of an allowlisted `T` with exactly matching nullability —
punched through as named exceptions. Weakening the general in-source rule to admit them would silently admit
every other metadata struct too, including the `[StructLayout(Auto)]` ones T0-B exists to refuse.

### T7 — `ImmutableArray<T>`, the one other collection with a public route to its storage

**NEW, maintainer question 2026-08-23: "can Dictionary and the other ICollection/IDictionary formats blit
too?"** The API surface was probed rather than recalled, and the answer splits three ways. Two of them are
already tasks; this is the third.

`ImmutableCollectionsMarshal` exposes `AsArray`, `AsImmutableArray` and `AsMemory` — public, safe, zero-copy
access to the backing array in both directions. `ImmutableArray<T>` is already a `TargetKind`, so a provable
element pair can go: unwrap to `T[]`, blit into a fresh array, re-wrap. It is the array theorem plus two
wrapper calls, and it reuses `BlittableProof` untouched.

The re-wrap must take a **freshly allocated** array and never the source's own, or two immutable values would
share storage — which for an immutable type is a correctness bug, not an optimisation.

### The refusals, recorded so round 26 does not re-ask

**`Dictionary<K,V>` and `HashSet<T>`: no, and not merely "not yet".** Four independent reasons, in order of
how final they are:

1. **No public span over the storage.** `CollectionsMarshal` offers exactly `AsSpan(List<T>)`,
   `SetCount(List<T>)`, `AsBytes(BitArray)`, and per-key `GetValueRefOrNullRef` /
   `GetValueRefOrAddDefault` for dictionaries. There is no dictionary or set equivalent of `AsSpan`.
2. **The storage is a private nested `Entry` struct** — measured: `Dictionary<int,int>` holds `int[] _buckets`
   plus `Entry[] _entries`, and `HashSet<T>` the same pair. `Entry`'s layout is an implementation detail, not
   a contract, so a blit over it is a layout assumption that **cannot be proven** — exactly what T0-B exists
   to refuse. Reaching it needs reflection or unsafe punning, which collides with the project's
   accessibility-and-no-reflection commitment and with AOT/trim safety.
3. **Hash codes are baked into the entries.** Change the key type and every stored hash is wrong. Keep the
   key type and comparer and the dictionary is the same dictionary — territory the BCL copy constructor
   already fast-paths.
4. So the achievable dictionary win is a different feature entirely: **not rehashing** when key type and
   comparer are unchanged. That is not exposed publicly either.

**`Queue<T>` and `Stack<T>`: no.** Both hold a private `T[] _array` with no marshal accessor, and `Queue<T>`
additionally wraps head-to-tail, so its backing array is not even contiguous in logical order.

**The `ICollection` family needs nothing of its own.** `IList<T>`, `IReadOnlyList<T>`, `ICollection<T>`,
`IReadOnlyCollection<T>` and `IEnumerable<T>` all materialise to `List<T>`, so **T2 covers all of them in one
go** — that is the answer to the "other ICollection formats" half of the question.

*(Noted in passing: `CollectionsMarshal.AsBytes(BitArray)` is a public byte span over a `BitArray`. Not a
task — DwarfMapper does not map `BitArray` — but it is the only other blittable surface the BCL hands out,
so it is worth knowing it exists before someone asks a third time.)*

---

## Layer 3 — the standing perf gate

### T4 — R25-05-final, ratio-based and same-process, never absolute-time

The research is unambiguous that absolute-time gating on shared runners does not work: a 2% absolute gate
yields roughly 45% false positives, and holding a 1% false-positive rate needs about a 7% threshold. A
same-process SIMD-vs-scalar ratio cancels shared contention, because both arms feel it equally.

Gate at the large-`n` regime, using the constant pinned by **T0-A's local measurement**. The small-`n` guard
region is explicitly **excluded from hard gating** and asserted only directionally — `Add` winning below the
threshold IS the guard's own regression test.

Sabotage rule (round 13, non-negotiable): de-vectorize one kernel in a scratch branch, show the gate goes
red, revert. An unproven gate is a gate that passes because it measures nothing.

---

## Layer 4 — semantics, not performance

### T5 — R25-04, skip-if-identical, as an opt-in whose cost is documented

v2 falsified this **as a perf feature**, and the RFC retracts the implied benefit: `SequenceEqual` reads two
streams where `CopyTo` reads one and writes one, so even the identical case costs about 2x a plain copy. It
never wins.

It survives on its real justification — EF change-tracker semantics, not dirtying unchanged entities — and
therefore ships opt-in, documented so that the **cost is stated in the same breath as the benefit**. A
feature documented as a speed-up when it is measurably a slow-down is precisely the kind of claim this
repository exists to prevent.

### OUTCOME: NOT SHIPPED — recommended on the evidence below, and RULED BY THE MAINTAINER 2026-08-23

**This section previously committed to shipping it. Two facts found by probing before building changed the
picture, and both are the kind of fact that is supposed to change a plan.**

**1. Its stated justification is already largely served.** Update-into assigns a collection member
wholesale — `dst.Member = <new collection>` — which is what dirties a tracked entity. But
`[MapCollectionKey]` (G6) already exists and does the opposite: it **keeps the existing list instance** and
upserts into it by key. For the same-element-type `List<T>` members that EF navigation properties actually
are, the "don't dirty unchanged entities" story is therefore already available, under an option that also
does something useful on a miss. R25-04 would add a second, overlapping way to ask for it.

**2. The cost is worse than the RFC knew, in exactly the range that matters.** Measured locally — see
`benchmarks/results/2026-08-23-round25-kernels.md` — comparing first is a loss **even on a hit** from n=16
through n=16,384, bottoming out at **0.28x** around a thousand elements. The one place it wins is a hit at
n=65,536 (**4.20x**), where skipping a megabyte of writes beats paying for them; the container had measured
0.49x there and concluded "never faster", which is wrong at the top end and right everywhere else.

So the feature would be: new public attribute surface, overlapping an existing option, to buy a 3.5x
slowdown in the common case and a win only for very large already-identical collections. **Declining is the
better engineering call, and it is the same evidence-driven outcome this round already reached for R25-01
and R25-06.**

This was raised for ratification rather than closed unilaterally, because unlike those two the plan had
already said "ships". **The maintainer ruled "T5 should be skipped" on 2026-08-23**, so it is closed by
decision rather than by inference from a benchmark.

If the EF semantics are ever wanted for the non-keyed and cross-element-type cases that `[MapCollectionKey]`
cannot reach, the measurement above is what the feature's documentation must carry — and n ≥ 65,536 is the
band to recommend it in, rather than warn about.

---

## Parked

### T6 — R25-01, checked narrowing: parked permanently, with a pin that keeps it parked

Not "not yet" — **parked on evidence**. `TensorPrimitives.ConvertChecked` is scalar-only by design (its
fallback operator declares `Vectorizable => false`, and the Vector128/256/512 paths throw
`NotSupportedException`), it throws without identifying the faulting element, and it leaves partial writes
behind. Both contestants being scalar, the container's 1.49x is anomalous and **must not be cited**. RyuJIT
compiles `checked((int)x)` to a flags test plus a not-taken `jo` — near-free, which is why nothing beats it.

Ship an **emitted-source pin** asserting the checked-narrow emission contains no `TensorPrimitives` call and
no vector kernel. The pin is the point: it prevents silent adoption without a parity-and-perf proof.

Record the one credible future route in the source comment, rather than in a document nobody will find: a
`ConvertTruncating`-style vectorized narrow (vectorized since dotnet/runtime PR #116895) plus a vectorized
any-lane-out-of-range mask, falling to the scalar loop only on a nonzero mask.

---

## Standing rules, carried forward

* **`dotnet build DwarfMapper.NET.sln` — the whole solution, samples included.** `dotnet test` does not
  cover `samples/`, and that is exactly where an over-eager emission change surfaces.
* **House invariants no SIMD path may alter:** checked narrowing is exact-at-bound and throws one past;
  output is byte-deterministic; no SIMD path changes a diagnostic outcome; AOT- and trim-safe, intrinsics
  only.
* **The scalar path stays the permanent semantic oracle.** Every blit is proved against it, never against a
  hand-written expectation.
* **Endianness** deserves one pinned comment-test: the blit is a same-endian reinterpret, not serialization.
  Both supported targets are little-endian, which is a fact with a shelf life.
* **No floor or threshold moves except to a value measured in the same commit.**
* **Explicit pathspec on every commit.** No `git commit -a`, no `git add .`.
* **No push without explicit per-instance approval.**

## The open question, now answered

`bool[]` non-normalization is **settled as an equivalence** (2026-08-23). Measured: a `bool` holding byte 2
survives the element loop, the block copy, a `bool[]` loop and a box/unbox round trip unchanged — the two
emitted strategies never diverge, so this was never a correctness question.

`BoolBlitEquivalenceRuntimeTests` therefore pins **that the two paths agree** over bytes 0, 1, 2 and 0xFF,
and deliberately does not pin a byte value: the C# specification says nothing about non-canonical bools, so
preserving byte 2 is the JIT's behaviour rather than a promise DwarfMapper can make. The weaker pin still
catches the only thing that would be a defect — the two paths drifting apart.
