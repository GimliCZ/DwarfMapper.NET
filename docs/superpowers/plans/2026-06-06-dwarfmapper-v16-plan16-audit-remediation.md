# DwarfMapper.NET — Plan 16: Audit Remediation + Synthetic/Fuzz Foundation + CRA Hardening

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax. TDD throughout.

**Goal:** Act on the mass audit. Three thrusts, in order: **(A) fix the genuine code bugs**, **(B) build a synthetic/property/fuzz test foundation** that automatically catches the bug classes we keep finding by hand (uncompilable output, silent value mismatch, round-trip breakage), **(C) CRA hardening + README/spec accuracy.**

**Why this order:** fix known bugs first (we have precise repros), then stand up the fuzzer as the permanent safety net (on a now-correct codebase), then harden supply-chain + correct the public docs.

**Tech Stack:** As the repo. The fuzz foundation is dependency-free (seeded `System.Random` + a custom schema generator + Roslyn emit/load + the existing `DwarfMapper.Testing.ObjectFactory`/`StructuralComparer`). No FsCheck/CsCheck dependency — keeps the minimal-deps/CRA posture; we get reproducibility from explicit seeds and breadth from many iterations.

**Builds on the current tree** (main, 527379d): generator pipeline + `GeneratorTestHarness.Run`/`RunAndGetCompilationErrors`; `DwarfMapper.Testing` (`ObjectFactory`, `Fuzzer`, `StructuralComparer`, `RoundTrip`). Diagnostics DWARF001–022.

**Conventions:** SPDX header on new files; CPM; warnings-as-errors; warning-clean. New diagnostics → `AnalyzerReleases.Unshipped.md`. Each task: TDD, commit, independent review, fold to trunk.

---

# Part A — Bug fixes

## Task A1: `[AfterMap]` on a value-type target (the silent bug)

**Problem:** for a struct target, `var __dwarf_target = new S{}; Hook(__dwarf_target); return __dwarf_target;` passes the struct by value → hook mutations are silently lost.

**Fix:** support a `ref` target parameter, and **diagnose** a by-value after-hook on a value-type target (`DWARF023`).

- [ ] **Step 1 (failing tests)** in `tests/DwarfMapper.Generator.Tests/HookTests.cs`:
  - `AfterMap_byvalue_on_struct_target_reports_DWARF023`: struct `Dst`, `[AfterMap] void Fix(Dst d)` → diagnostics contain `DWARF023`.
  - `AfterMap_ref_on_struct_target_emits_ref_call`: struct `Dst`, `[AfterMap] void Fix(ref Dst d)` → no error, generated contains `Fix(ref __dwarf_target)`, `RunAndGetCompilationErrors` empty.
  - `AfterMap_byvalue_on_class_target_unchanged`: class `Dst`, `[AfterMap] void Fix(Dst d)` → still emits `Fix(__dwarf_target)`, no DWARF023 (reference target mutations stick).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** Add `DWARF023` (`AfterMapValueTargetByValue`, Error): "[AfterMap] on a value-type target '{0}' must take the target parameter by `ref`, or its changes are lost". Add to `AnalyzerReleases.Unshipped.md`.
- [ ] **Step 4:** `HookCall` (Model) gains `bool TargetByRef`. In `MapperExtractor.CollectHooks`, capture each after-hook's TARGET-parameter `RefKind` (P1 for 2-param, P0 for 1-param). In per-method after-hook matching: let `targetIsValue = targetType.IsValueType`, `targetIsRef = (that param's RefKind == RefKind.Ref)`. If `targetIsValue && !targetIsRef` → emit `DWARF023`, skip the hook. Else include `new HookCall(name, takesSource, TargetByRef: targetIsRef)`.
- [ ] **Step 5:** `MapEmitter` after-hook emit: prefix the target arg with `ref ` when `HookCall.TargetByRef` (e.g. `Fix(s, ref __dwarf_target)` / `Fix(ref __dwarf_target)`).
- [ ] **Step 6:** run all generator tests + build 0/0. Commit `fix(gen): [AfterMap] value-type target must be ref (DWARF023); emit ref call`.

## Task A2: `[Reinterpret]` + `[MapIgnore]` conflict diagnostic

- [ ] **Step 1 (failing test)** in `BlitTests.cs`: `Reinterpret_and_MapIgnore_conflict_reports_DWARF012` — `[Reinterpret("V")]` + `[MapIgnore("V")]` on the same method → diagnostics contain `DWARF012` (reuse `IgnoreExplicitConflict`).
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** In `MapperExtractor.ResolveMembers`, when a reinterpret member is also in `ignores`, emit `DWARF012` (IgnoreExplicitConflict) and skip — mirror the `[MapProperty]`+`[MapIgnore]` handling. (Place the check in the reinterpret branch / the post-loop unknown-member check, whichever cleanly covers the ignored case.)
- [ ] **Step 4:** tests + build. Commit `fix(gen): [Reinterpret]+[MapIgnore] conflict reports DWARF012`.

## Task A3: DWARF015 emitted per call site (decouple from synth dedup)

**Problem:** `EnumConverter.AddEnumByName` gates DWARF015 emission inside `if (!synth.ContainsKey(name))`, so a second method using the same enum pair gets no diagnostic at its own location.

- [ ] **Step 1 (failing test)** in `EnumToEnumTests.cs`: `Incomplete_enum_used_in_two_methods_reports_DWARF015_for_each` — two mapping methods both map `Src(A,B,Blue)`→`Dst(A,B)` by name; assert `diagnostics.Count(d => d.Id=="DWARF015") >= 2` (one per method location). (Confirm it currently yields 1 → FAIL.)
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Restructure `EnumConverter.AddEnumByName` (and `TryCreate`'s by-name path) so the **completeness diagnostics are computed and emitted every call** (using the call's `location`), while the **synthesized method body is still deduped** by name. I.e. split "report missing members at `location`" from "register the switch method once".
- [ ] **Step 4:** tests + build. Commit `fix(gen): DWARF015 reported at each call site (decoupled from synth dedup)`.

## Task A4: nullable value source composes with synthesized conversions

**Problem:** `TryResolveConversion`'s nullable branch only succeeds when the underlying has an *implicit* conversion to the target; `E1?`→`E2` and `List<E1?>`→`List<E2>` fall through to DWARF005.

- [ ] **Step 1 (failing tests)** in `NullHandlingTests.cs`:
  - `NullableEnum_to_enum_composes`: `enum E1{A,B} enum E2{A,B}` `E1? S`→`E2 S` → no error, compiles; with default Throw the emitted access unwraps then converts.
  - `NullableEnum_to_int_composes`: `E1? S`→`int S` → compiles.
- [ ] **Step 2:** FAIL (currently DWARF005).
- [ ] **Step 3:** In `TryResolveConversion`'s nullable-value branch, after detecting `IsNullableValue(srcType, out underlying)`, **recursively** `TryResolveConversion(underlying, tgtType, ...)`. If it resolves with a converter (`conv`), compose: the member access must unwrap the nullable then apply the converter. This needs the emitter to support "null-handling + converter" together. Simplest sound emission for the Throw strategy: `conv(src.X ?? throw …)`; for SetDefault: `conv(src.X.GetValueOrDefault())`. To carry this, allow `MemberMap` to have BOTH `ConverterMethod` and `NullHandling` set, and update `MapEmitter` so when both are present it emits `Conv(<nullhandled access>)`. (Today the emitter treats them as mutually exclusive — make converter wrap the null-handled access.) Keep the existing single-aspect paths unchanged.
- [ ] **Step 4:** run ALL generator + integration tests (ensure no regression to existing converter/null tests) + build 0/0. Commit `fix(gen): nullable value source composes with synthesized/Use conversions`.

## Task A5: `DiagnosticInfo` equality guard

- [ ] **Step 1:** No test (it's a latent-fragility note). In `Diagnostics/DiagnosticInfo.cs`, add an XML/inline comment on the `Descriptor` member documenting that descriptors MUST be the `static readonly` singletons from `DiagnosticDescriptors` (record equality uses reference identity for `DiagnosticDescriptor`; constructing one inline would break incremental caching). Optionally override equality to compare `Descriptor.Id` instead. Minimal: the comment.
- [ ] **Step 2:** build 0/0. Commit `docs(gen): document DiagnosticInfo descriptor-identity equality assumption`.

---

# Part B — Synthetic / property / fuzz test foundation (the centerpiece)

**Design.** Three property-based suites, all seeded + reproducible, dependency-free. Place generator-driven ones in `tests/DwarfMapper.Generator.Tests`. Best-technique rationale: a *schema generator* produces random valid DTO pairs + mapper source; we assert invariant **properties** over hundreds of seeds (vs hand-picked examples). This is exactly the oracle that would have caught CS1912/CS0120/CS8510/CS1503 automatically.

## Task B1: synthetic schema generator + "compiles-always" property

**Files:** `tests/DwarfMapper.Generator.Tests/Fuzzing/SyntheticSchema.cs` (generator), `tests/DwarfMapper.Generator.Tests/Fuzzing/CompilesAlwaysFuzzTests.cs`.

- [ ] **Step 1:** Build `SyntheticSchema`: given an `int seed`, deterministically emit a C# source string containing a random but VALID mapper:
  - A random number (1–12) of members, each `MemberN` with a type drawn from a weighted pool: `int,long,short,byte,double,float,bool,string,System.Guid,System.DateTime`; a generated `enum En{A,B,C}`; a nested generated struct (sequential, recursion depth ≤ 2) of scalars; `T[]`, `System.Collections.Generic.List<T>`, `System.Collections.Generic.HashSet<T>`, `T?` (nullable scalar) of a scalar element.
  - Two classes `Src`/`Dst` with the **same member names**; for v1 keep member TYPES identical between Src and Dst (so the map is total and round-trippable). Emit a `[DwarfMapper] public partial class M { public partial Dst ToDst(Src s); }`. (Element/nested types that differ are exercised by dedicated example tests already; the fuzzer's job is breadth over shapes + the compile invariant.)
  - Return the source string; expose the type/member schema (for B2's oracle) via a small record.
  - The generator must itself be deterministic for a seed (only `new Random(seed)`).
- [ ] **Step 2 (property test):** `CompilesAlwaysFuzzTests`: `[Theory]` over a `MemberData` of e.g. seeds 0..199 (and a few large fixed seeds). For each: `var (diag, _) = Run(src); Assert.DoesNotContain(diag, d => d.Severity==Error); Assert.Empty(RunAndGetCompilationErrors(src));`. On failure, the assertion message MUST include the seed and the generated source (so any discovered counterexample is replayable). Run it; it should be GREEN on the fixed codebase (Part A done). If it finds a real counterexample, STOP and report it (do not weaken the fuzzer).
- [ ] **Step 3:** build + test. Commit `test(fuzz): synthetic schema generator + compiles-always property (200 seeds)`.

## Task B2: behavioral property — generated mapper == identity (differential) + round-trip

**Files:** add `GeneratorTestHarness.EmitAssembly(source)` (emit the generator-updated compilation to a `MemoryStream`, `Assembly.Load` it, return the assembly + any emit errors); `tests/DwarfMapper.Generator.Tests/Fuzzing/BehavioralFuzzTests.cs`.

- [ ] **Step 1:** `EmitAssembly`: reuse the same compilation construction as `RunAndGetCompilationErrors`, then `compilation.Emit(ms)`; if `EmitResult.Success`, `Assembly.Load(ms.ToArray())`. Return `(Assembly?, ImmutableArray<Diagnostic> errors)`.
- [ ] **Step 2 (property test):** for seeds 0..N: generate a schema (Src/Dst identical-shape, identical member types — so the mapping is value-preserving), `EmitAssembly`, reflectively `new M().ToDst(src)` where `src = ObjectFactory.Create(srcType, seed)`; assert `StructuralComparer.Diff(src, dst)` is EMPTY (every value copied correctly). This is the differential oracle: identical-shape mapping must be value-preserving. Include the seed + diff dump in failure messages.
- [ ] **Step 3 (round-trip property):** for a subset of seeds, also emit a `[RoundTrip] partial Src ToSrc(Dst d)` inverse + reference `DwarfMapper.Testing` (the gen-tests project already references it), then invoke the generated `VerifyRoundTrip_ToDst` reflectively (or call `RoundTrip.Verify` over the loaded types) and assert it does not throw.
- [ ] **Step 4:** build + test (these are slower — keep N modest, e.g. 50, and `[Trait]`-tag them so they can be filtered). Commit `test(fuzz): behavioral differential + round-trip property over emitted assemblies`.

> If B2's emit/load proves flaky or slow in CI, scope it down to the differential property only and keep round-trip as a smaller fixed-seed set. Correctness of the property matters more than the count.

---

# Part C — CRA hardening + docs accuracy

## Task C1: CI supply-chain hardening

**File:** `.github/workflows/ci.yml`.
- [ ] Pin the `CycloneDX` dotnet tool to an explicit version (`dotnet tool install --global CycloneDX --version <pin>`).
- [ ] Add SLSA build-provenance attestation for the SBOM artifact: add `actions/attest-build-provenance` (SHA-pinned) on the `sbom` job with `permissions: id-token: write, attestations: write, contents: read`, attesting the generated SBOM (or built package) — honoring the SLSA comment already in the file.
- [ ] Add a second AOT/trim gate RID (`win-x64`) to `aot-trim-gate` (matrix or a second step) so platform-specific trim/AOT warnings are caught. Keep `-warnaserror`.
- [ ] Validate YAML (`python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"`). Commit `ci: pin CycloneDX, SLSA provenance attestation, multi-RID AOT gate`.

## Task C2: `[Reinterpret]` doc precision

- [ ] In `src/DwarfMapper/ReinterpretAttribute.cs` XML doc, state explicitly: the forced path verifies ONLY that both element types are unmanaged and (at runtime) equal in size; **field-by-field correspondence is entirely caller-asserted** (no name/layout/`Pack` check). Build 0/0. Commit `docs: clarify [Reinterpret] verifies size only; field correspondence is caller-asserted`.

## Task C3: README + spec accuracy pass

**File:** `README.md`, `docs/superpowers/specs/2026-06-06-dwarfmapper-design.md`.
- [ ] README: replace the Quick-start `[MapWith(typeof(...))]` line with the real `[MapProperty(nameof(...), nameof(...), Use = nameof(Method))]`. Remove/relabel the **`MapTo(src, ref dest)` / `Span<T>` zero-alloc overloads** claim (not implemented) and the **"configure completeness severity globally/per-mapper"** claim (not implemented) — either delete or move to a clearly-marked "Planned" list. Fix the **repo-layout** table (`DwarfMapper.Generator.Tests`, add `DwarfMapper.IntegrationTests` + `DwarfMapper.Testing.Tests`, drop the phantom `DwarfMapper.Benchmarks`). Remove the **BenchmarkDotNet "in-repo" claim** (or move to Planned). Update **Status** from "Pre-alpha — design phase" to reflect feature-complete-v1 (local; pre-release). Move **Flattening/Hooks/Projection** out of the "v2 — unbuilt" roadmap into shipped (leave `async` + `MapTo`/span + benchmarks as genuinely planned).
- [ ] Spec: update Status from "design phase"; replace `[MapWith]` with `[MapProperty(Use=...)]`; mark projection/hooks/flattening as shipped; note the float-tolerance open item is still open in `StructuralComparer`.
- [ ] Commit `docs: correct README/spec to match shipped reality (no [MapWith], no phantom benchmarks/MapTo, status, roadmap)`.

---

## Self-Review
- A1 fixes the only *silent* correctness bug (struct after-hook) with DWARF023 + ref support. ✅
- A2–A4 close the genuine diagnostic/gap findings; A5 documents the latent equality assumption. ✅
- B1/B2 stand up a reproducible, dependency-free property/fuzz foundation that asserts the two invariants we keep breaking by hand — generated code always compiles, and identical-shape mapping is value-preserving + round-trips — over hundreds of seeds, with replayable counterexamples. This is the "build on it" base. ✅
- C1–C3 address the CRA supply-chain gaps and the public-doc drift. ✅

**False positives explicitly NOT actioned** (verified during audit aggregation): `AddStringToEnum` CS8510 (string patterns are distinct — not a bug); `[RoundTrip]` no-op-without-Testing (the `GetTypeByMetadataName` gate makes the README correct). The `[MapProperty]`+`CaseInsensitive` interaction is left as the documented Plan-2 design (explicit names matched exactly) — revisit only if the owner wants it relaxed.

**Deferred (not in this plan):** shrinking for fuzz counterexamples (we report seed+source, which is enough to replay); FsCheck/CsCheck adoption; relaxing `[MapProperty]` to honor `CaseInsensitive`; `MapTo`/span overloads; benchmarks; `async`.
