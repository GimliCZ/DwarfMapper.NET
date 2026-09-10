<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Three defects found while closing coverage gaps in the SetNull/MapDerivedType emitters

**Filed 2026-09-10**, during the `/goal` coverage sweep (measure → test → measure → accept/revert, core
outward). Writing tests to close `EmitSetNullGuardedBody`'s branches surfaced a shipped-wrong remedy; chasing
an apparent generator "nondeterminism" while writing `[MapDerivedType]`/`[AfterMap]` tests surfaced a second,
unrelated, genuinely new defect. All three are fixed; this records what was wrong and why, so the next sweep
doesn't re-derive it.

## 1. DWARF108's shipped remedy did not work (commit `475d7a0`, then wrongly "fixed" by `81d125e`)

DWARF108 fires when `OnCycle = SetNull` reaches a recursion-capable pair whose destination is a non-nullable
value type. The first shipped remedy text told users to suppress it with `#pragma warning disable DWARF108`
or a `.editorconfig` `dotnet_diagnostic.DWARF108.severity = none`. **Neither works.** Every generator
diagnostic is reported through `DiagnosticInfo.ToDiagnostic()` against a `LocationInfo.ToLocation()` —
`Location.Create(FilePath, TextSpan, LineSpan)`, an `ExternalFile`-kind `Location` with `SourceTree == null`.
Roslyn's compiler-level pragma/editorconfig suppression pipeline is tree-scoped; a `Location` disconnected
from the live `SyntaxTree` object can never be matched by it, regardless of whether the location is otherwise
"real" (non-null, correct file/span). This is a Roslyn limitation for *generator*-reported diagnostics, not
a bug in this generator — proven empirically with a real MSBuild warnings-as-errors build, not just the
in-process test harness.

`81d125e` first tried to fix the documentation by claiming `<NoWarn>` was the *only* mechanism that works,
because DWARF108 (unlike DWARF076) supposedly wasn't part of some "SupportedDiagnostics contract." That
explanation was invented, not read from the code, and was wrong: `SelfMapDiagnosticTests` already had a
passing test, `DWARF076_is_suppressed_by_SuppressMessage_on_the_mapper`, contradicting it directly. The
`advisor` review caught this as blocking on its second pass.

**The real mechanism** (found by reading it, in `MapperExtractor.Conversions.cs`):
`HasSuppressMessage(ISymbol symbol, string diagnosticId)` reads `[SuppressMessage("DwarfMapper",
"DWARFxxx:...")]` directly off a symbol via `symbol.GetAttributes()` at generation time — a
generator-internal mechanism the generator itself chooses to honor, entirely bypassing the compiler's
suppression pipeline (no `Location`/`SyntaxTree` needed). DWARF076 already used it; DWARF108 simply hadn't
been wired to it. `<NoWarn>` is the other mechanism that genuinely works, because it's an MSBuild-level,
whole-compilation filter applied by diagnostic ID *after* generation completes, not a Roslyn tree-scoped
pragma/config lookup.

Fixed in `24694ad`: `ApplySetNullPostPass` now takes the mapper's `INamedTypeSymbol` and gates the
diagnostic-add (not the unconditional fallback-to-plain-depth-guard) on `HasSuppressMessage(classSymbol,
"DWARF108")`. Descriptor message, `docs/diagnostics.md`, `CHANGELOG.md`, the `NegativeCases` fixture, and the
integration-test fixture were all rewritten to state `[SuppressMessage]` and `<NoWarn>` as the two working
mechanisms and `#pragma`/`.editorconfig` as the ones that cannot ever work for this class of diagnostic.

**Lesson:** an advisor's plausible-sounding mechanism explanation (the first "no SourceTree" theory) can be
half right and still license a wrong fix if the other half — *which* suppression paths the generator itself
chooses to honor — isn't independently verified against an existing passing test for a sibling diagnostic.

## 2. DWARF109 (new): `[AfterMap]` by-ref hook matched to a pair by the wrong conversion rule

Every `HookCall` construction site (five of them, in `MapperExtractor.Phases.cs`) matches a hook to a pair
using "does the destination type implicitly convert to the hook's declared parameter type?" — correct for an
ordinary by-value parameter, where the compiler upcasts the argument at the call site. It is wrong once the
hook parameter is `ref`, because C# has no `ref` covariance: `ref DerivedDto` does not bind to a `ref
BaseDto` parameter even though `DerivedDto` converts to `BaseDto` by value. Through `[MapDerivedType]`, a
hook declared `[AfterMap] void Finish(ref AnimalDto d)` matched the dispatch method's own pair (destination
really is `AnimalDto`) AND every derived arm's own declared pair (destination `DogDto`, by-value-convertible
but not identical) — emitting `Finish(ref __dwarf_target)` where `__dwarf_target` was typed `DogDto`, i.e.
CS1503 in a `.g.cs` no consumer can edit.

New diagnostic DWARF109 (Error, `ScopedToMethod: true` — an unscoped Error would suppress the *entire*
mapper class's emission via `MapperClassModel.HasBlockingError`, taking down the unrelated, correctly-typed
`Map(Dog d)` method along with the mismatched one). The generator now skips adding the `HookCall` for the
mismatched pair only, via a shared `RefHookTargetMismatches` helper called at all five `HookCall`
construction sites.

**The remedy text went through one more correction after the fix itself was already right.** The first
version told users to "declare a separate `[AfterMap]` overload for this destination type" as an
alternative to dropping `ref`. That doesn't work either: a base-typed `ref` hook matches *every* derived
pair by the same by-value rule, so adding a second, correctly-typed overload for `Dog` leaves the original
`Finish(ref AnimalDto d)` still mismatched against `Dog`'s pair — DWARF109 keeps firing there. There is no
overload set that clears this diagnostic for every pair at once; dropping `ref` (a reference-type
destination never needs it) is the only fix that generalizes. Caught by `advisor`, fixed before commit —
worth naming because it's the same failure shape as #1: a remedy that reads as plausible until walked
through by hand against the actual matching rule.

## 3. Related, independently-discovered: `var`-typed switch-expression local in `EmitDerivedDispatchBody`

While debugging what first looked like nondeterminism between `GeneratorTestHarness.Run()` (in-process,
prints the correct method's text) and `RunAndGetCompilationErrors()` (real `GetDiagnostics()`, reported
CS1503) — it was not nondeterminism. The CS1503 was in a *different* method than the one I was reading:
`Map(Animal a)`, the dispatch method, not `Map(Dog d)`. `EmitDerivedDispatchBody`'s `hasAfter` branch emitted
`var __dwarf_target = {p} switch {...}` — `var` infers the switch expression's own best-common-type over the
arms (the single concrete arm's return type, e.g. `DogDto`) rather than the method's declared, more general
return type (`AnimalDto`), because `var` gets no target-typing context the way `return {p} switch` gets from
the method's own return type. This was silently masked for by-value hooks (implicit upcast at both the hook
call and the final `return` swallows the narrower inferred type) but broke for `ref` hooks, which need an
exact type. Fixed by typing the local explicitly as `method.ReturnTypeFullName`.

**Lesson, general:** the apparent-nondeterminism debugging session cost real time and turned out to be a
plain misreading of which method/line an error was actually in — always verify the line number against the
*actual* generated source text before trusting an assumption about which method produced a diagnostic.

## Deferred, still open (not fixed this round, flagged by `advisor` as non-blocking)

- **DWARF108 fires twice per pair** — one message names a synthesized helper identifier
  (`__DwarfMap_Obj_...`) that isn't user-facing. Should be deduped to one diagnostic per pair.
- **The fuzzer's `SyntheticSchema` never generates a struct destination under `OnCycle = SetNull`** — a
  corpus hole. DWARF108's own bug (`475d7a0`) existed specifically in that combination and was found by hand,
  not by the fuzzer, which cannot reach it at all today.
- **`docs/diagnostics.md#dwarf076` and `SelfMapDiagnosticTests.cs` claim `.editorconfig
  dotnet_diagnostic.DWARFxxx.severity = none` works project-wide**; `SuppressionPathwayTests` and a
  file-glob-scoped test both contradict that. Nobody has tried a `.globalconfig` with `is_global = true`
  (a different suppression path than a file-scoped `.editorconfig` section) — that two-minute test would
  settle whether the docs are wrong or just imprecise about which `.editorconfig` shape works.
- **Mutation-testing leg** hasn't run since `RefHookTargetMismatches` (5 call sites) and the
  `HasSuppressMessage` wiring in `ApplySetNullPostPass` were added — both are new mutation surface, unverified
  by that leg as of this finding.

## Files touched

`MapperExtractor.Phases.cs` (`ApplySetNullPostPass`, `RefHookTargetMismatches`, 5 `HookCall` sites),
`MapperExtractor.cs` (call site), `MapEmitter.cs` (`EmitDerivedDispatchBody`), `DiagnosticDescriptors.cs`
(DWARF108 message rewrite, DWARF109 new descriptor), `DiagnosticInfo.cs` (`ScopedToMethod` doc comment),
`AnalyzerReleases.Unshipped.md`, `docs/diagnostics.md`, `CHANGELOG.md`, `docs/generated/diagnostics-index.md`,
`tests/DwarfMapper.Generator.Tests/SetNullCycleGeneratorTests.cs`,
`tests/DwarfMapper.Generator.Tests/SetNullStructDestinationRuntimeTests.cs`,
`tests/DwarfMapper.Generator.Tests/MapDerivedTypeGeneratorTests.cs`,
`tests/DwarfMapper.IntegrationTests/DwarfMapper.IntegrationTests.csproj`,
`tests/DwarfMapper.IntegrationTests/SetNullAdversarialRuntimeTests.cs`,
`tests/DwarfMapper.NegativeCases/Cases/DWARF108_SetNullStructDestination.cs`,
`tests/DwarfMapper.NegativeCases/Cases/DWARF109_AfterMapRefTargetTypeMismatch.cs` (new).

Commits: `475d7a0`, `41781c1`, `81d125e`, `7b39034`, `24694ad`, and the DWARF109-remedy-text correction
committed alongside the `EmitDerivedDispatchBody` re-measurement (see round 30's coverage-sweep commits
following this one).
