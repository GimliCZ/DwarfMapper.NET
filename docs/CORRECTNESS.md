<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Correctness — why DwarfMapper is the mapper that *proves* your mappings

Object mappers fail in two quiet ways: a field silently goes to the **wrong place** (mislinking), or a
field silently goes **nowhere** (a forgotten/renamed member). Runtime mappers discover these in production;
even the source-generator peers mostly leave them to your tests. DwarfMapper's organizing principle is that
**a mapping you can't compile is better than a mapping you can't trust** — and that the proof should be
mechanical, not manual.

Speed isn't the moat (the other source generators tie us). *Provable correctness and reproducibility* is —
and it's structurally hard for a runtime/reflection mapper to copy. This page collects the guarantees and,
crucially, **the test/diagnostic that enforces each one**, so the claims are checkable, not marketing.

## 1. Every destination is accounted for — at build time

A destination member with no source is a **build error** (`DWARF001`), not a silent default. You cannot
ship an incomplete map; intentional drops are explicit and auditable via `[MapIgnore]`. Source-side
coverage is opt-in (`RequiredMapping = Both` → `DWARF039`); note `DWARF039` is an **IDE suggestion** by
default (not build-breaking) — escalate it with `dotnet_diagnostic.DWARF039.severity = error` in
`.editorconfig` to make an unconsumed source field fail the build.

> **This is a DTO-drift contract gate.** Because the map is resolved at compile time, a DTO or entity that
> changes shape and breaks a mapping **fails the build** — your two types provably cannot drift apart
> unnoticed. AutoMapper offers this only as a test-time `AssertConfigurationIsValid()`; Mapster offers no
> equivalent. *Evidence:* the completeness gate is exercised throughout the generator suite and is enforced
> by construction — suppressing the diagnostic in `.editorconfig` doesn't help: the incomplete method body
> isn't generated, so the build still fails (with a rawer compiler error).

## 2. The resolved mapping is visible, not inferred

Every generated public method carries an XML-doc **mapping plan**: `target ← source`, `via {converter}`,
`= {constant}`, `(?? {substitute})`, `(when {predicate})`, `[ctor]`. It shows on IDE hover and at the top
of the generated `.g.cs`, so a reviewer can see *what maps to what* without reading the body — the readable
face of anti-mislinking. *Evidence:* `MapEmitter.EmitMappingPlanDoc`; rendered for every public method
(snapshot-tested).

## 3. Round-trips are verified, not hoped

Tag a forward/back pair `[RoundTrip]` and the generator emits a seeded fuzz harness proving
`Back(Forward(x)) ≡ x` with a mapping-aware diff on failure. No inverse → `DWARF020`; ambiguous → `DWARF021`.
One attribute replaces the fixtures you'd otherwise hand-maintain. *Evidence:* `DwarfMapper.Testing` +
the `[RoundTrip]` generation path.

## 4. The codegen is deterministic and incrementally cached — proven

- **Deterministic:** running the generator twice over the same input yields byte-identical output (and
  identical diagnostics). A non-deterministic emission would mean flaky snapshots and non-reproducible
  builds. *Evidence:* `DeterminismFuzzTests` (behavioural + advanced-feature seeds + Preserve/SetNull).
- **Incrementally cached:** an unrelated edit leaves the mapper's pipeline steps `Cached`/`Unchanged` — the
  generator does not recompute or re-emit. *Evidence:* `IncrementalCachingTests`
  (`TrackIncrementalGeneratorSteps`), including a negative control proving a *relevant* edit *does*
  recompute.
- **Cache-safe by construction:** every pipeline model is a value-equatable record holding only cache-safe
  fields (no raw `ImmutableArray`, no leaked `ISymbol`/`Compilation`). *Evidence:* `ModelCacheSafetyTests`
  — a structural meta-test that fails the instant a non-equatable field is introduced, so caching can't
  silently regress.

## 5. Reflection-free → NativeAOT / trim / regulated targets — proven

The emitted code uses **no runtime reflection** (concrete member access only), which is what makes it
NativeAOT- and trim-safe and viable for embedded/regulated/CRA-conscious deployments where the runtime
mappers structurally cannot go.

- *Evidence (source level):* `ReflectionFreeMetaTests` asserts the generated code contains no
  reflection-for-mapping tokens (`System.Reflection`, `Activator`, `GetProperty/Field/Method`,
  `MakeGenericType`, `dynamic`, …) across 64 fuzz seeds + every advanced feature.
- *Evidence (end to end):* the CI AOT gate publishes `samples/DwarfMapper.AotSample` with **zero**
  IL2xxx/IL3xxx warnings (this proves trim/AOT-clean compilation; the behavioural gate in its `Program.cs`
  is run locally, not executed by CI).
- *Evidence (stability under AOT):* `samples/DwarfMapper.AotBench` is published with NativeAOT and run
  **locally** as a native binary (not a CI job) — SIMD widen/blit are bit-exact at every vector-boundary size, Preserve topology is
  deterministic over 100 000 runs, and the depth/SetNull guards hold over tens of thousands of runs. Timing
  is *steadier* than the JIT (no tiering jitter). The one AOT usage caveat — default baseline SIMD width
  (`Vector<int>.Count == 4` vs the JIT's 8), restored with `<IlcInstructionSet>native</IlcInstructionSet>`
  — is documented in [`COMPARISON.md`](COMPARISON.md#nativeaot-benchmarking--stability); it is a perf knob,
  not a correctness issue (output is bit-identical either way).

## 6. No silent `StackOverflowException`

Recursion-capable mappings carry a depth bound (`MaxDepth`, default 64) applied uniformly across direct,
collection, and dictionary edges. Exceeding it throws a **catchable** `DwarfMappingDepthException`, never a
process-killing `StackOverflowException`. Cycles are handled deliberately: `Preserve` reconstructs the full
topology; `OnCycle = SetNull` breaks back-edges into a finite acyclic projection. *Evidence:* the
depth/graph runtime suites and the AOT gate's cycle checks.

---

## The one-line version

> **DwarfMapper proves your mappings are right and reproducible:** complete (or it won't compile),
> visible (the plan is documented on every method), round-trippable (verified, not hoped), deterministic
> and incrementally cached (proven), and reflection-free for AOT (proven) — with no silent overflow.

Each clause above names the test or diagnostic that enforces it. That enforceability — not raw throughput —
is the differentiator the runtime mappers can't structurally match. See
[`COMPARISON.md`](COMPARISON.md) for the capability/perf matrix and
[`IMPROVEMENT-PLAN.md`](IMPROVEMENT-PLAN.md) for what's next.
