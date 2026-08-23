# [RFC] DwarfMapper.NET — Round 25: SIMD expansion of the collection fast paths
# Subject: vectorize the element-conversion classes the current design leaves scalar

Grounding [MEASURED, tip `fac40f3`]: today's fast paths are (a) the reinterpret-blit array mapper —
`MemoryMarshal.Cast` with a runtime element-size guard (`CollectionConverter.cs:757-780`), and (b) a
`System.Numerics.Vector.Widen` path covering exactly the seven provably-lossless primitive widenings
(`:17-26`), gated on `Vector.IsHardwareAccelerated` with a scalar tail (`:823-830`). Everything else —
checked narrowings, enum arrays, blittable structs, `List<T>` targets — takes the scalar element loop /
`CreateChecked` path (`:20`). The entries below extend the same design language: prove the property at
generation time, emit the vector path behind a hardware gate, keep the scalar path as the always-correct
reference. Status: RFC — emitted-code templates and tests are proposed, not compiled against `fac40f3`;
substitution seams marked `// SEAM:`.

House invariants every entry must preserve (all previously measured): checked-narrowing semantics are
*exact-at-bound, `OverflowException` one past* (round-11 boundary probes); output is byte-deterministic;
loud-or-lossless — no SIMD path may change a diagnostic outcome; AOT/trim-safe (no reflection, intrinsics
only, which `Vector`/`Vector128` are under NativeAOT).

---

## [R25-01] conv: vectorize the CHECKED narrowing loops (the biggest scalar class left)

What: `long[]→int[]`, `int[]→short[]`, `int[]→byte[]`, `ulong[]→long[]`, `int[]→uint[]` … — every
      DWARF038 + `CreateChecked` collection cell currently emits a per-element scalar loop.
Why:  This is the highest-volume remaining scalar path, and it is vectorizable *without* changing the
      contract: a checked narrowing over a span is "verify all lanes in range, then narrow", which SIMD
      does at 8–32 lanes per compare. The README's performance identity is the blittable/SIMD story;
      this extends it to the guarded conversions instead of only the free ones.
How:  Two implementation options, decided by one dependency question:
      (a) **`TensorPrimitives.ConvertChecked<TFrom,TTo>`** (`System.Numerics.Tensors`, net8+) — the BCL's
          vectorized span conversion with overflow checking. One emitted call replaces the loop:
          ```csharp
          // emitted, inside the existing null/size preamble:
          var __d = new int[src.Length];
          global::System.Numerics.Tensors.TensorPrimitives.ConvertChecked<long, int>(src, __d);
          ```
          Cost: a new runtime package reference on the shipped GPL lib — a packaging decision, not a
          technical one. [INFERRED: exact single-lane `OverflowException` parity must be verified by the
          R25-05 harness before adoption — if TensorPrimitives reports overflow without identifying the
          element, the emitted catch must not promise an index the scalar path also doesn't promise.]
      (b) **Dependency-free emitted kernel** — range-check a vector block, fall to scalar on any
          out-of-range lane so the *scalar* path throws with identical semantics:
          ```csharp
          if (global::System.Numerics.Vector.IsHardwareAccelerated && src.Length >= __w)
          {
              var __min = new global::System.Numerics.Vector<long>(int.MinValue);
              var __max = new global::System.Numerics.Vector<long>(int.MaxValue);
              int __i = 0;
              for (; __i <= src.Length - __w; __i += __w)
              {
                  var __v = new global::System.Numerics.Vector<long>(src, __i);
                  if (global::System.Numerics.Vector.GreaterThanAny(__v, __max)
                      || global::System.Numerics.Vector.LessThanAny(__v, __min))
                      break;                    // scalar loop below rethrows with EXACT house semantics
                  global::System.Numerics.Vector.Narrow(__v, ...); // SEAM: pairwise narrow store
              }
              // scalar tail + scalar overflow region continue from __i — single source of truth for throw
          }
          ```
      Recommendation: (b) first (zero new dependencies, semantics provably identical because the throwing
      path *is* the scalar path), promote to (a) only if benchmarks justify the package.
Regression tests:
      ```csharp
      [Theory] // lengths straddling every vector boundary, both element orders
      [InlineData(0)] [InlineData(1)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(33)]
      public void Checked_narrowing_simd_equals_scalar(int len) { /* R25-05 harness, pairwise */ }

      [Fact] public void Overflow_at_lane_k_throws_like_scalar()
      {   // int.MaxValue+1L planted at positions {0, W-1, W, len-1}: same exception type; if the scalar
          // path leaves a partially-written destination, document it — the SIMD path must match it.
      }
      [Fact] public void Exact_bounds_survive_in_every_lane()
      {   // all-lanes int.MaxValue / int.MinValue arrays round exact — the round-11 property, vectorized.
      }
      ```
Red-when: any length/boundary/lane combination where the SIMD path and the scalar path disagree on value,
      exception type, or destination state.

---

## [R25-02] blit: extend reinterpret-blit from primitive arrays to blittable STRUCT collections

What: `Point[] → Point[]`-shaped mappings (identical sequential blittable layout both sides) and the
      `List<T>` variants currently walk the object path or per-element copy; only primitive arrays hit
      the `:757` blit today.
Why:  The proof obligation already exists in the codebase's design language (size-guard + reinterpret);
      structs of primitives are the same theorem with more fields. Real DTO models are full of
      `record struct Money(long Amount, int Currency)`-class payloads.
How:  Generation-time proof: both element types are unmanaged, `LayoutKind.Sequential`/no explicit
      packing mismatch, and **field-by-field identical primitive layout** (name-independent — offsets and
      types). Emit the existing blit with `sizeof` guard; for `List<T>` targets add:
      ```csharp
      var __dst = new global::System.Collections.Generic.List<TDst>(src.Count);
      global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(__dst, src.Count);
      var __s = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(srcList);
      var __d = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(__dst);
      global::System.Runtime.InteropServices.MemoryMarshal.Cast<TSrc, byte>(__s)
          .CopyTo(global::System.Runtime.InteropServices.MemoryMarshal.Cast<TDst, byte>(__d));
      ```
      Refuse (keep scalar path) on: auto layout, references, nullable fields, `[FieldOffset]`, differing
      pack — the gate is the feature; a wrong blit is silent corruption, the one forbidden class.
Regression tests: layout-equivalence gate tests (auto-layout pair must NOT blit — assert the scalar path
      emitted, `[MEASURED via emitted-source assertion]`); value round-trip over randomized structs vs the
      reflection oracle (R22-01 leg reused); endianness note pinned as a comment-test (both supported
      targets are little-endian; the blit is same-endian reinterpret, not serialization).
Red-when: a struct pair blits whose layouts differ, or a qualifying pair regresses to the object path.

---

## [R25-03] enum: enum arrays as underlying-primitive blit

What: `Status[] → Status[]` (same enum), `Status[] → StatusDto[]` (same underlying + identical member
      values), and `Status[] ↔ int[]` currently per-element cast in a loop.
Why:  All three are reinterpretations, not conversions — the underlying representation is identical, so
      the `:757` blit applies verbatim once the generator proves it. The `Status→StatusDto` case needs
      the value-set proof the enum converter already computes for its member-mapping diagnostics — the
      analysis exists; only the emission is scalar.
How:  Gate: underlying types equal AND (same enum ∨ value-sets identical as already computed for
      DWARF enum diagnostics ∨ target is the underlying primitive). Emit `MemoryMarshal.Cast` blit; keep
      the loop for differing underlying types or value-set mismatches (those are conversions and stay in
      the loud path).
Regression tests: same-enum blit equals loop output for random values *including undefined bit patterns*
      (blit must preserve them exactly as the loop's unchecked cast does — pin whichever the scalar path
      does today [MEASURE FIRST]); value-set-mismatch pair must not blit; `[Flags]` combinations survive.
Red-when: an enum pair with differing value sets reinterprets, or undefined values change behavior class.

---

## [R25-04] update: SIMD change-detection for update-into on blittable segments (opt-in)

What: `Update(src, dst)` writes every member unconditionally; EF change trackers then see every entity
      as modified even when nothing changed.
Why:  For blittable members/collections, "did anything change" is a span `SequenceEqual` — vectorized in
      the BCL — making skip-if-identical nearly free and the mapper change-tracker-friendly.
How:  New option (`UpdateWriteMode = WriteChangedOnly`, default unchanged — behavior additions are
      opt-in per house rule). Emit per blittable segment:
      `if (!src.AsSpan().SequenceEqual(dstSpan)) { <existing write>; }` and per scalar member a plain
      compare. **Endpoint-parity obligation:** the option lands wired to Update only and must be declared
      in `OptionGaps` for the other endpoints — the R22/REG-01 matrix will demand exactly that row.
Regression tests: identical-source update leaves destination reference-and-value untouched (assert no
      collection reallocation); one-lane difference writes; option-off preserves today's write-always
      (differential vs current behavior); parity-matrix row present.
Red-when: WriteChangedOnly skips a genuinely-changed lane, or the default mode's behavior shifts.

---

## [R25-05] test-infra: the SIMD-vs-scalar differential harness (lands FIRST, before any entry above)

What: every SIMD path in R25-01..04 must be bit-identical (values, exceptions, destination state) to the
      scalar path across the adversarial length/alignment/lane grid — and the scalar path is the oracle.
Why:  This is R22-01's differential principle turned inward: the "two independent implementations" are
      the vector and scalar emissions of the *same* cell. It also inherits the round-16 lesson — the
      hardware gate means CI must run **both** legs or the scalar leg silently becomes dead code.
How:  ```csharp
      // SPDX-License-Identifier: GPL-2.0-only
      public static class SimdParity
      {
          public static readonly int[] Lengths = { 0, 1, 3, 7, 8, 9, 15, 16, 17, 31, 32, 33, 100 };
          public static void AssertParity<TS, TD>(TS[] src)   // SEAM: emitted-mapper invocation pair
          {
              var vec = RunWithIntrinsics(src);       // normal process
              var sca = RunScalarReference(src);      // the emitted scalar tail extracted as the oracle
              Assert.Equal(sca.Outcome, vec.Outcome); // value|exception-type|dest-state triple
          }
      }
      ```
      Plus the CI leg that makes the fallback real: a job with `DOTNET_EnableHWIntrinsic=0` (and a
      second with `DOTNET_PreferredVectorBitWidth=128`) running the collection suites — the
      software-fallback and narrow-vector paths each get their own green, mirroring the AOT job's
      "execute the binary, don't just build it" discipline. Sabotage requirement per the round-13
      precedent: flip one comparison in one kernel and show the harness goes red before trusting it.
Red-when: any divergence between vector and scalar legs; or the intrinsics-off CI leg is removed and the
      scalar tails become untested dead code.

---

Landing order: R25-05 harness + CI legs → R25-03 (smallest proof, pure reinterpret) → R25-02 →
R25-01(b) → benchmark gate → optionally R25-01(a) and R25-04. Every entry keeps the scalar path as the
single source of throwing/semantic truth — SIMD here only ever accelerates a path whose behavior the
scalar twin defines, which is what keeps "loud, never silent" intact at 32 lanes per cycle.

---

# v2 addendum — MEASURED results (the "prove it improves" pass)

Environment: container CPU with AVX2+AVX-512, `Vector.IsHardwareAccelerated=true`, **`Vector<long>.Count=4`
(256-bit — .NET caps the preferred vector width at 256 by default even on AVX-512 hardware; see research
notes)**, .NET SDK 10.0.400, Release, median of 9 trials, preallocated destinations (kernel isolation;
allocation cost is identical between variants), in-range data. Single shared core — SIMD width is
unaffected, absolute numbers carry container noise; ratios are the finding.

| entry | n=16 | n=1,024 | n=65,536 | verdict |
|---|---|---|---|---|
| R25-01 kernel (checked narrow) | **0.27x** | **0.33x** | 1.08x | **FALSIFIED as proposed** |
| R25-01 TensorPrimitives | 0.07x | 0.07x | 1.49x | niche: only ≥~64k |
| baseline `Vector.Widen` class | — | — | 1.70x | existing repo choice validated |
| R25-02 struct blit | 2.62x | **35.8x** | 1.93x | **CONFIRMED — headline** |
| R25-02 `List<T>` via `SetCount`+blit | 0.46x | **10.0x** | 2.00x | confirmed with n≥~32 threshold |
| R25-03 enum blit | 5.54x | **76.1x** | 6.42x | **CONFIRMED — biggest win** |
| R25-04 skip-if-identical (hit) | — | 0.28x | 0.49x | **FALSIFIED as a perf feature** |
| R25-04 skip-if-identical (miss) | — | 0.37x | 0.95x | (cost, never gain) |

## Revised verdicts

- **R25-01 is withdrawn in its blanket form.** The scalar `checked((int)x)` loop is already ~0.8 ns/element
  — the JIT emits a tight compare-and-`jo` loop, and my vector kernel's four range-comparisons per block
  cost more than they save (3x *slower* at small/medium n). `TensorPrimitives.ConvertChecked` wins only
  1.49x at 64k elements and is catastrophic below (dispatch overhead: 0.07x at n≤1k). Revised proposal:
  keep the scalar path as-is; *optionally* emit a TensorPrimitives call behind an `n >= 65536` length
  gate if profiling ever shows huge checked-narrow collections in the wild. Low priority.
- **R25-02 and R25-03 are the real submission.** Enum-array reinterpret is the single largest measured win
  (up to **76x** in-cache, 6.4x at bandwidth), struct blit close behind (**35.8x** in-cache). Both large-n
  results converge to ~2x — the memory-bandwidth ceiling, expected and healthy. The `List<T>` path needs a
  small-n guard (`Count >= 32`, below which `Add` wins 2x).
- **R25-04 is reframed, not deleted.** Skip-if-identical is *never* faster: `SequenceEqual` reads two
  streams where `CopyTo` reads one and writes one, so the identical case costs ~2x a plain copy. The
  feature's justification was always EF change-tracker semantics (don't dirty unchanged entities); it
  survives **as an opt-in semantic feature with a measured perf cost**, and its doc must say so. The
  original RFC's implied perf benefit is retracted.
- **R25-05 gains one clause**: the harness grows a *perf gate* — each SIMD emission must beat its scalar
  twin by ≥1.5x at its declared target size in the nightly benchmark leg, or it reverts to scalar. "Prove
  it improves" becomes a standing check, not a one-time review.

## Research notes from the measurement

1. **256-bit default on AVX-512 hardware.** `Vector<T>` sized to 256 bits despite avx512bw/dq present —
   .NET's default `PreferredVectorBitWidth` avoids 512-bit unless opted in (historic frequency-throttling
   caution). Follow-up: rerun R25-02/03 with `DOTNET_PreferredVectorBitWidth=512`; the blit paths go
   through `memmove` (already width-optimal) so only R25-01's kernel would care — another reason it loses.
2. **Why checked-scalar is hard to beat**: the overflow check compiles to a flags test the CPU executes
   in parallel with the truncating move; the vector version must *materialize* the range predicate. SIMD
   wins on arithmetic density, and a checked narrow has almost none.
3. **The in-cache vs bandwidth regimes explain every ratio**: 1k-element rows (fits L1/L2) show the true
   compute advantage (10–76x); 64k rows show the DRAM ceiling (~2x). Mapper workloads are typically the
   in-cache regime — DTO collections of tens-to-thousands — which is exactly where the confirmed entries
   are strongest.
4. **Dispatch overhead dominates below ~64 elements** for any helper call (TensorPrimitives' 0.07x at
   n=16); emitted inline blits (R25-02/03) dodge this entirely, which is an argument for emission over
   library calls at this project's typical sizes.

Revised landing order: **R25-03 → R25-02 (+n≥32 List guard) → R25-05 harness with perf gate → R25-04 as
semantic opt-in (docs state the cost) → R25-01 parked** pending real-world evidence of ≥64k checked
narrowings. Benchmark source preserved at `/home/claude/bench/Program.cs` for re-runs.

---

# v3 addendum — research-hardened entries (What / Why / How / Regression tests)

The deep-research pass (sources: MS Learn SIMD docs, .NET 8 hardware-intrinsics devblog, dotnet/runtime
sources & PR #116895, MemoryPack, SVE devblog, CodSpeed/Quansight CI-noise data) converts into the
following revised and new entries. Evidence-backed vs inferred is labeled per entry.

## [R25-01-final] conv: checked narrowing — PARKED PERMANENTLY, with the only viable future route recorded

What: retire the vectorized checked-narrow proposal; retract the container's TensorPrimitives 1.49x@64k
      figure as artifact-suspect.
Why:  [EVIDENCE] `TensorPrimitives.ConvertChecked` is **scalar-only by design** — the fallback operator
      declares `Vectorizable => false` and all Vector128/256/512 paths throw `NotSupportedException`
      (dotnet/runtime source). It throws `OverflowException` without identifying the faulting element and
      leaves **partial writes** in the destination. Since both contestants are scalar, the measured 1.49x
      is theoretically anomalous (baseline codegen or container artifact) and must not be cited. RyuJIT's
      `checked((int)x)` compiles to a flags-test + not-taken `jo` — near-free, which is why nothing beats it.
How:  keep the scalar loop as the sole emission. Record the one credible future route in the source
      comment: a `ConvertTruncating`-style vectorized narrow (that path IS vectorized since PR #116895)
      **plus** a vectorized any-lane-out-of-range mask, falling to the scalar loop only on a nonzero mask
      — prototype-gated behind the R25-05 perf gate at ≥1.5x across all three sizes.
Regression tests: (a) a pin that the checked-narrow emission contains no TensorPrimitives call and no
      vector kernel (emitted-source assertion — prevents silent adoption without the parity+perf proof);
      (b) the existing boundary tests (exact-at-bound, throw-one-past) stay the semantic contract any
      future prototype must match, including destination-state on overflow.

## [R25-06] blit: expand the allowlist — Guid[], same-nullability Nullable<T>[], same-type decimal[]

What: three additional element classes qualify for the reinterpret blit.
Why:  [EVIDENCE] `Guid` is a 16-byte all-blittable-fields struct (serializer-standard blit target);
      `Nullable<T>` layout (hasValue + padding + T) is identical when source and destination nullability
      match exactly; `decimal→decimal` is a trivially safe same-type reinterpret. All three ride the same
      `Buffer.Memmove` path the measured 6–76x wins use.
How:  extend the blit gate's allowlist with: `Guid` (same-type), `Nullable<T>` where source and dest are
      both `T?` of the same blittable `T`, `decimal` (same-type only — no conversion form exists). Emit
      the identical Cast-through-`byte` template (ARM-alignment-safe direction).
Regression tests: value round-trip vs the reflection oracle for each class incl. `Guid.Empty`/max-bytes
      patterns and `null`/`HasValue=false` cells preserved bit-exactly; a **cross-nullability refusal**
      test (`T?[]→T[]` must take the loud path, never blit); differential row: blit output byte-equals
      the element-loop output for 10k random elements per class.

## [R25-07] gate: compile-time layout-equivalence diagnostic (the MemoryPack discipline as a DWARF id)

What: a new diagnostic (next free DWARF id) — "blit refused: layout not provably equivalent" — emitted
      whenever a pair *looks* blittable but the generator cannot prove identical layout; plus hard
      refusals for known-unsafe types.
Why:  [EVIDENCE] the blit's single correctness risk is layout divergence, and the ecosystem precedent
      (MemoryPack's zero-encoding, x50–x200 on struct arrays) survives production precisely because
      blittability is proven at generation time. `DateTime`/`DateTimeOffset` are `[StructLayout(Auto)]`
      — layout is NOT guaranteed — so they must refuse the blit even though they "look" like 8/10-byte
      values. `bool[]` blits are semantically identical to the loop (non-0/1 bytes preserved either way)
      but the non-normalization must be documented.
How:  gate = unmanaged ∧ Sequential/Explicit layout ∧ field-by-field offset+type equality ∧ equal pack;
      Auto-layout or unequal ⇒ diagnostic + scalar path. The diagnostic is informational (the mapping
      still works) — it exists so a consumer expecting the fast path learns *why* they didn't get it.
Regression tests: NegativeCases wording pin for the new id; `DateTime[]`/`DateTimeOffset[]` pairs assert
      scalar path + diagnostic [MEASURED via emitted-source assertion]; an auto-layout struct pair
      refuses; a `bool[]` pin asserting blit and loop produce byte-identical output for non-0/1 patterns
      (documents the non-normalization contract); the R22-01 differential leg re-runs over every
      allowlisted class so gate changes can't silently widen.

## [R25-05-final] perf: the gate specification, hardened by the CI-noise data

What: finalize the harness as **ratio-based, same-process** gating; never absolute-time on shared runners.
Why:  [EVIDENCE] GitHub-hosted runners show a coefficient of variation where a 2% absolute gate yields
      ~45% false positives and holding 1% false-positive rate needs a ~7% threshold (CodSpeed); the
      dotnet/performance answer is ResultsComparer with a percentage threshold + noise floor, and
      BenchmarkDotNet ships Mann–Whitney U via `--statisticalTest`. A same-process SIMD-vs-scalar ratio
      cancels shared noise because both arms feel the same contention.
How:  nightly BenchmarkDotNet job: each blit entry measured against its scalar twin in one run; gate =
      ratio ≥1.5x at the large-n regime; small-n (List guard region) explicitly EXCLUDED from hard gates
      and asserted only directionally (`Add` wins below threshold — that is the guard's own regression
      test). Report `--statisticalTest` U-test; merge-gate via ResultsComparer at 5–10%. The List-blit
      threshold constant is expressed as a `Vector<T>.Count` multiple (dotnet/runtime's own idiom), and
      one emitted-shape pin asserts **no throwing statement between `CollectionsMarshal.SetCount` and the
      copy** — the uninitialized-exposure hazard from the SetCount docs made structurally impossible.
Regression tests: the gate is the test; plus the round-13 sabotage rule — de-vectorize one kernel
      (replace blit with the loop) in a scratch branch and show the ratio gate goes red before trusting it.
Follow-ups recorded, not gated: one confirmatory Graviton 4 run (128-bit SVE2 — in-cache ratios will
      shrink, bandwidth ratios transfer) before publishing cross-platform numbers; an optional
      `DOTNET_MaxVectorTBitWidth=512` curiosity run (memory-bound blits predicted indifferent).

Final landing order (v3): R25-07 gate → R25-03 enum blit → R25-02 struct blit (+`Vector<T>.Count`-multiple
List guard) → R25-06 allowlist → R25-05 harness+gate → R25-04 as documented-cost semantic opt-in →
R25-01 parked with its emitted-source pin. The through-line the research confirmed: every win this round
ships rides `Buffer.Memmove` behind a generation-time proof — emission over library calls, physics over
width, and the scalar twin as the permanent semantic oracle.
