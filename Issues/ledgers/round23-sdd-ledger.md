# Round 23 - the product defects the compiler arc found, and the r23 gate slice - execution ledger
Plan: docs/superpowers/plans/2026-08-23-round23-product-defects-and-r23-gates.md (commit fd2a5d2).
Branch feat/round23-product from master d0e5bca (round-22 merge; suite 7,818/0 across 9 projects).
Rulings in force from r22: per-dimension 97% reframe; ledger-only adjudication (RAW-score gates +
documented offset); deep-tier ceiling raised (all legs nightly); Sonar rejected; D-e complete (Scan9 guards
all ids); badges = generated-from-source-of-truth, no external service (S7).

N0 RULED BY MAINTAINER 2026-08-23 after being shown the concrete shapes: **(a) LIFT**.
`HasValue ? Helper(v.Value) : null` generalized over re-kinded pairs AND collection/dictionary element
loops; reference-source side gets the null-check equivalent. One ruling, both defects - the plan's
"decide once for both" premise upheld.
Cost if wrong (recorded per house rule): consumers whose code currently CATCHES the
InvalidOperationException, or relies on it as a guard, silently stop receiving it - a behaviour change
requiring a CHANGELOG `### Fixed` entry and a before/after measurement. Bounded: only paths that currently
THROW change, and they change from crashing to propagating null into a destination whose declared type
already accepts null. I5's shapes emit nothing today (they do not compile), so no consumer can depend on
them. Reversible in one commit.
Rejected branches and why: (b) refuse - I7's shape WORKS today for anyone whose data is never null there,
so refusal turns working builds into broken ones; (c) document - I5 cannot take it at all, uncompilable
output is never a documentable semantic, and I7 would keep a kind-dependent inconsistency no user can
predict from the types.
Execution order: N1 (I5) then N2 (I7) in ONE agent - same root family, so the sibling hunt is done once and
two philosophies cannot emerge; separate commits per the plan.

---

## N1 — I5 (commit 603047a) and N2 — I7 (commit 4b7caa4). Both DONE, 2026-08-23.

**The placement decision, recorded because the plan's two-commit split could not be taken literally.**
The plan assigns the resolver-gate widening to N2. It cannot live there: I5's own filing scopes the defect
to include *struct source × CLASS dest* elements ("still diverges"), and the element resolver reaches that
cell by recursing through the very same `TryResolveConversion` the plain member uses. Widening for one and
not the other is the parallel path round 20 forbids. So **the gate landed in N1**, and with it I7's forward
half (Struct→Class / Struct→Record / RecordStruct→Class), whose oracle pin was flipped in the same commit
rather than left red. N2 kept what is genuinely its own and is the riskier half anyway: the reverse genre
(reference source → `Nullable<D>`, a different root cause needing a new `NullHandling` value), the I7
sampled-space exclusion, the six-cell kind-pair table, the docs, and the deep re-runs over the twice-widened
space. Two ordered commits, each green, each carrying its own defect's accounting.

**Root causes — two joined faults, not one.**
1. `CollectionConverter.ElementExpr` and `DictionaryConverter.Expr` DISCARDED the element's `NullHandling`
   whenever a converter was present: `if (conv is not null) return conv + "(" + item + ")"`, with the
   `switch` over the enum in the unreachable `else`. Not "the lift was missing" — *no* value of the decision
   reached the emitted element, and the `CS1503` was only its loudest consequence.
2. The resolver gated `NullableProject` on both sides being `Nullable<T>` — on the destination's KIND rather
   than on whether it can hold the null. Same confusion, one layer up.
3. (N2) The reverse genre's throw came from INSIDE the synthesized helper, whose value-type return leaves it
   no way to answer null. Only the call site could fix it → `NullableProjectRef`.

**Sibling hunt — searched, found, fixed.** All four consumers of `NullHandling` were read end to end:
`MapEmitter.AppendValueExpression` (already composed; gained `(ctx, depth)` threading inside the ternary — a
recursion-capable converter would have lost it, CS7036 waiting), `CollectionConverter.ElementExpr` (fixed;
`SynthesizeInPlace` shares the helper and inherits it), `DictionaryConverter.Expr` (fixed, key AND value,
both entry points), `MapperExtractor.Flatten.AppendFlatNodeMemberExpr` (identical discard shape; fixed for
guard-inheritance, no measured repro — stated as such in the code). `MapperExtractor.Projection` has its own
nullable logic and does NOT share the gate; it refuses the cross-kind cells with `DWARF028`, loudly, so it
was FILED as **I14** rather than folded in.

**Filed rather than fixed: I14** — `Map` lifts the cross-kind nullable nested member, `Project` errors
`DWARF028` and takes the whole mapper down with `DWARF078`. Measured with a 4-cell probe, not deduced. Loud,
so out of scope for a ruling about silent behaviour.

**Measurements.** Solution build 0 W / 0 E (samples included) at both commits. Suite 7,818 → 7,830 (N1) →
7,836 (N2), 0 failures, ~77 s foreground. CompilerTests 25 → 37 → 43. Census 866/866;
`EmittedInvalidCodeCellCeiling` still exactly 0; RatchetInvariantScan + GateBandLogic + DeepTierSelf 15/15.
Deep tier, both exclusions gone, run twice for the I11 identity check — identical digests both times:
K0 250 `c61225343ab6395e`, K1 1000 `e1606cb3dbe47261`, MR-1/MR-2 250 `3f7843d07afa1597`, MR-3 150
`f7b67ecbe6be3ad6`; all executed, 0 refused, **0 divergences**, project-alone 35.1 s / 34.9 s (round-22 era
31.1–34.4 s over a narrower space). The digests moved at both commits, as they must: each deleted exclusion
puts cells back into the sampled space. Widening the space by two cell families surfaced NO further
divergence.

**Follow-up (a53adc8).** The I7 CHANGELOG bullet cross-references "the entry above" and had been prepended,
so it pointed at nothing — the two entries are now ordered I5 then I7 (commit order, and the order the second
reads in). `InternalEnumCoverageSelfValidationTests`' remark still enumerated `NullHandling` without
`NullableProjectRef`; the test reflects over the enum so it was green while its prose was stale. Full suite
re-measured AT HEAD after both: **7,836 / 0**, 78 s, build 0 W / 0 E.
N1+N2: DONE (603047a I5+shared gate, 4b7caa4 I7 both directions, a53adc8 docs). CONTROLLER ACCEPTS incl.
the commit-placement deviation: the resolver gate could NOT live in N2 because I5's own filing scopes
struct-source x CLASS-dest elements, which reach the gate by recursion - splitting it would have built the
parallel path round 20 forbids. THREE root causes, one WIDER than the filing: the converter branch
DISCARDED NullHandling entirely, so ThrowIfNull/ValueOrDefault never reached the emitted element either -
CS1503 was only the loudest consequence. Gate widened from destination KIND to destination CAPABILITY
(TryGetNullableCapableTarget); un-annotated/oblivious keeps the documented throw. New NullHandling
.NullableProjectRef for the reverse genre (the throw lived INSIDE a value-returning helper, so only the
call site could lift it). Sibling hunt found a LATENT CS7036 (ternary not threading (ctx,depth)) and fixed
Flatten for guard-inheritance. Pins: I5 row dropped KnownSilentCsIds, new rekinded-dest row; I7 throw-pin
DELETED and replaced by a six-cell lift test WITH THE DIAGONALS AS CONTROLS (a fix lifting four by breaking
two would pass a four-cell test); both keyed exclusions deleted. Deep re-run twice, identical digests,
0 divergences over the WIDENED space (digests moved vs r22 because the space grew - framing recorded).
Suite 7,836/0; EmittedInvalidCode still exactly 0; census 866/866.
I14 FILED not fixed: Map and Project DISAGREE on a cross-kind nullable nested member - Map lifts, Project
errors DWARF028 and DWARF078 then takes the WHOLE mapper down, so a mapper carrying both methods stops
generating entirely. Loud, so outside a ruling about silent behaviour; measured with a 4-cell probe.
CONTROLLER NOTE: I14 is severe despite being loud (whole-mapper kill) and is the same endpoint-inconsistency
genre as I7 - queue it as its own task after the Layer-1 remainder, before Layer 2.

---

## Layer-1 remainder — N3 (B28), N4 (I6), N5 (I4), N6 (B37). All DONE, 2026-08-23.

Commits, in execution order: **N4 `ff744ea`**, **N5 `86f68f4`**, **N3 `9589e5a`**, **N6 `1decd32`**.
One task per commit, explicit pathspecs, nothing pushed.

### N4 — I6, and the sibling the one-liner would have shipped

**Diagnosis verified before the fix, both directions**, because a one-line fix that treats a symptom is
worse than none. With `-p:PublishAot=true`: `NETSDK1207` verbatim on `src/DwarfMapper.Generator` and
`src/DwarfMapper.CodeFixes`, the two netstandard2.0 projects the global property flows down into. Without
it: the publish reaches `Generating native code` and emits a native image. So the flag was the whole cause
and AotBench's own `<PublishAot>true</PublishAot>` is sufficient — the filing was right.

**What the filing did NOT say, and what makes this more than one line.** Removing the flag leaves *nothing
on the command line asserting AOT-ness*, and a framework-dependent publish still drops a runnable apphost
under `publish/`, so the behavioural gate would stay green while proving nothing. That is the exact trap
`ci.yml`'s aot-trim-gate documents and closes with an "Assert the publish really was NativeAOT" step; the
local stage never inherited it. It does now (no managed assembly, no `runtimeconfig.json`), and the publish
directory is resolved through the RID instead of a bare `-Recurse` over `bin/Release` that could gate on a
stale publish from an earlier RID or TFM.

**Sabotage demo:** a planted `DwarfMapper.AotBench.runtimeconfig.json` reds stage 3 naming the file;
removed, green. **Stage 3 measured green end to end** — NativeAOT confirmed, all six AotBench
correctness/determinism checks passed, exit 0.

**Do the local stage and CI's `aot-trim-gate` prove the same thing? NO — and they should not.** Stated
because the task asked, and because the divergence is not documented anywhere else:

| | housekeeping stage 3/4 | ci.yml `aot-trim-gate` |
|---|---|---|
| project | `samples/DwarfMapper.AotBench` | `samples/DwarfMapper.AotSample` |
| RIDs | one, the host's | matrix: `linux-x64` **and** `win-x64` |
| IL2xxx/IL3xxx | not gated | `-warnaserror` — the trim/AOT-warning gate |
| NativeAOT asserted | **now yes** (was not) | yes, since 2026-07-26 |
| executes the binary | yes — codegen correctness/determinism, SIMD/blit/cycle/depth/registry | yes — the sample's 36 `return 1` behavioural sites |

Two different samples, two different questions: CI proves the emission is *trim/AOT-clean* on both OSes;
the local stage proves the emitted code is *correct and deterministic* under NativeAOT. Neither subsumes
the other, and the local stage is the only one that runs the determinism battery. Left as is; the asymmetry
is now recorded rather than assumed to be an oversight.

**Environment finding, recorded so the next agent does not lose an hour to it.** With the flag gone the
publish got far enough to fail at the LINKER: `MSB3073 … 'vswhere.exe' is not recognized`. Root cause traced
and it is neither the repo nor the maintainer's machine: `NoDefaultCurrentDirectoryInExePath=1` is set in
the AGENT HARNESS process only (the `User` and `Machine` scopes are both empty, `Process` is `1`).
`VsDevCmd.bat` L180-181 does `pushd <VS Installer dir>` then runs a bare `vswhere.exe`, relying on cmd's
current-directory-in-PATH behaviour; that variable disables it, the error goes to STDOUT, and ilcompiler's
`findvcvarsall.bat` output is parsed with `Split('#')[0]`, so `CppLinker` becomes the noise plus the real
path. Clearing the variable for the run makes the link succeed. Nothing to fix in the repo — and no gate was
weakened to work around it.

### N5 — I4, the widened decontamination sweep

`Assert-NoMutatedProductBinaries` in `scripts/gate-checks.ps1`, called after **all three** legs in
`housekeeping.ps1` and once per leg in `ci.yml`'s mutation matrix. The CI step **dot-sources the same
function** rather than re-implementing the scan in bash — `Assert-MutantsWereTested` and
`Assert-StrykerConfigSane` both carry "the two must stay in step" warnings because they are duplicated; this
one is not.

Mechanism: the reference set of product assembly NAMES comes from what `src/**/bin` actually builds (so a
new product project is swept the day it first builds), and every file of one of those names under
`tests/**/bin` is byte-scanned for Stryker's marker.

**Restricting to product names is measured, not assumed.** A naive "every `DwarfMapper*.dll` under
`tests/**/bin`" scan reds on a perfectly clean tree: `DwarfMapper.Generator.Tests.dll` (Debug and Release)
carries the literal `Stryker` in its own sources — 2 of 60 files matched before the restriction, 0 of 44
after.

Three vacuity guards, because a sweep that looks at nothing passes exactly like a clean one: no src
originals; no product copies under `tests/**/bin`; and originals that already carry the marker (the
scanner's own premise — if a clean build matches, the marker has stopped discriminating). The oracle is file
CONTENT, never a timestamp: P5's original detection rode the run's mutate/compile window, which is the
wall-clock oracle H7 forbids. It FAILS naming every offending file rather than deleting it — silently
deleting the evidence would turn a Stryker behaviour change into a no-op.

**In-task ruling, with the rejected alternative recorded** (the row asked for the choice): folded into the
housekeeping/gate-checks post-leg proofs, not into `RepoWriteGuard`. RepoWriteGuard is a WRITE-time guard
*inside the test process*; Stryker plants these DLLs from outside any test process, before the tests run, so
there is no write for it to intercept. T3-H1 NOTE 3 is the precedent for where post-leg proofs live.

**Sabotage demo, twice.** By hand through the real call path: a marked `DwarfMapper.Generator.dll` planted
in `tests/DwarfMapper.CorpusTests/bin/Debug/net10.0` — one of the six bins P5 named — reds the sweep naming
the full path; removed, green (44 product assemblies under `tests/**/bin`, none mutated). And made
permanent as `MutationDecontaminationSweepTests`: five scenarios (clean / planted / no-originals /
no-copies / dirty-originals) driven through the REAL pwsh function against fake repo trees, plus a wiring
pin that reds if any of the four call sites is deleted.

The pwsh battery spawn and its H7 termination bound were extracted to `PwshBattery` and shared with
`GateBandLogicTests` rather than copied. `Show` now flattens a multi-line gate message onto its one CASE
line — without that the assertions naming files silently stopped checking anything, and it failed exactly
that way on the first run.

### N3 — B28, the row round 22 never dispositioned

Both filed failure modes reproduced before the fix, and a third the filing predicted but nobody had
measured:

| cell | before | after |
|---|---|---|
| registry, `ImmutableArray<Leaf>` member, no usings | `CS1061 'ImmutableArray<Leaf>' does not contain a definition for 'Count'` | compiles, pre-sizes `new List<LeafDto>(s.Length)` |
| registry, same + `global using System.Linq;` | `CS1503 Argument 1: cannot convert from 'method group' to 'int'` | compiles |
| registry, reference type with EXPLICIT `ICollection<T>.Count` | `CS1061` | compiles, no capacity argument |
| **class engine**, same reference type | **`CS1061`** | compiles |
| `List` / `HashSet` / `ICollection<T>` / `IReadOnlyCollection<T>` | `s.Count` | `s.Count`, byte-identical |

The fourth row is the one that matters for the filing's "not confined to value types": the class engine's
`Shape` constructor downgrades a value-type source to `CountKind.None` (`Nullable<T>` exposes no count),
which masked `ImmutableArray` there — and left the reference-type half live.

Fix: `CollectionConverter.TryGetEnumerableElement` resolves the count by MEMBER LOOKUP for classes and
structs — public instance `int Count`, else public instance `int Length`, walking base types and stopping at
the first type declaring the name so a hiding `public new string Count` cannot be read past. **Interfaces
and type parameters keep the interface reading**, and that is the correctness argument rather than an
exception: ordinary member lookup on an interface-typed value sees its base interfaces' `Count`, and on a
type parameter it sees its constraints'.

**`Length`-vs-`Count` decision, recorded as the row asked:** `Count` wins where both exist. Churn-minimising
by construction — every pre-sizing source reaching this predicate today exposes `Count`, so no existing
emission moves, and arrays never reach it (the `IArrayTypeSymbol` branch answers `Length` first). `Length`
is the fallback for the array-shaped types that expose it instead.

**Sibling hunt.** Class engine: same helper, fixed by the same change, its live half measured red first.
`DictionaryConverter.TryGetKeyValue`: carried its OWN copy of the interface test for
`new Dictionary(src.Count)` — now shares the predicate, with a fixture test. `EmitArray`'s unknown-count
`Enumerable.TryGetNonEnumeratedCount` probe: a RUNTIME test that binds regardless of explicit
implementations — pinned not-applicable, not fixed. No new diagnostic, so no five-file sync; a `### Fixed`
CHANGELOG entry, because generated code that did not compile is user-visible.

13 new tests (`CollectionCountMemberLookupTests`), including the four must-not-move cells as controls.

### N6 — B37, with its own re-measurement

`ArgumentsFor` now rotates each `{Member}` onto the next name the fixture declares, ordinal-sorted (the
parse returns a SET; an unordered pool would make the census depend on hash iteration order — the R4
oracle). Variant 1 returns the declaration untouched and is pinned, because axes 1 and 2 render at variant 1
too. Seven ×2 cases now differ: `Flatten` `"Child"`+`"Id"`, `FlattenGraph` `"Root","Flat"`+`"Children","Id"`,
`MapCollectionKey` `"Items","Id"`+`"Label","Items"`, `MapIgnoreSource` `"Extra"`+`"Id"`, `MapValue` and
`MapValue<T>` `"Name"`+`"Tag"`, `Reinterpret` `"Data"`+`"Id"`.

**A collapse the row did not name.** `MapNullSkip<S,T>`'s ×2 was degenerate too, from `SampleArgument`'s
variant-blind `bool` constant rather than from a declared list — `(true)` twice, so the matrix structurally
could not reach a CONTRADICTING pair. The plan's own Layer 4 table records B24 as unmeasurable *until N6
lands* for exactly this reason, so fixing only the declared-arguments half would have left the dependency
unmet. `bool` now varies `true`/`false`. Note precisely what that buys: the cell reads Honoured (output
differs), which says the pair is rendered and acts — not that the second application is honoured. Telling
"both honoured" from "second silently discarded" needs B24's own assertion. N6 makes the row measurable; it
does not measure it.

**Residual, exactly pinned with a reason each** (`SurfaceMultiplicityAxisTests.IdenticalByConstruction`, 10
entries): every ×2 case whose whole argument list is `typeof`s or empty, `[MapDerivedType]`'s declared
`typeof(SrcDerived), typeof(DstDerived)` among them. Closing that one needs a second derived pair in the
fixture — a declaration/fixture change, not a rotation — so it is recorded, not forced.

**Prose flagged, not silently patched.** The axis-3 comment in `BuildCases` DEFENDED identical-twice as "the
sharpest form of the multiplicity question"; it is overruled in the commit, with the reason. Three ×2
readings quoted inside closed `DeclaredDivergences` findings carry dated re-measurements beside them so the
history stays readable as history: MapValue at Projection `DWARF042` → `DWARF064`, FlattenGraph at CreateMap
`DWARF087` → `DWARF034`, Flatten `DWARF017` → `DWARF016` (`DWARF090` unchanged at the two element-wise
endpoints).

**Sabotage demo:** making `RotateMembers` variant-blind again reds the new guard naming all four collapsed
cases (`Flatten/0`, `FlattenGraph/0`, `MapValue/0`, `MapValue/1`); reverted, green.

### Measurements at the branch tip (`1decd32`)

- Whole-solution build `--no-incremental`, samples included: **0 warnings / 0 errors**, ~28 s.
- Full suite, foreground, 9 projects: **7,853 / 0**. Baseline 7,836 → 7,838 (N5, +2) → 7,851 (N3, +13) →
  7,853 (N6, +2). Generator.Tests ~59 s; the whole run stays inside the ~90 s fast-tier cap.
- Surface-matrix census **866/866**, unchanged by N6 (content moved, no cell added or removed; the cell
  theory itself is 854 rows, measured identical before and after the rotation, and the other 12 are the
  population facts).
- `EmittedInvalidCodeCellCeiling` still exactly **0**.
- RatchetInvariantScan + GateBandLogic + DeepTierSelf: **15 / 15**.
- **The full local pipeline ran END TO END for the first time this year** — `housekeeping.ps1 -Coverage`
  at tip, foreground, **12:52**, `HOUSEKEEPING PASSED`, tracked tree clean afterwards. Every stage in ONE
  invocation: stage 0 locked-mode restore + NuGet audit; stage 1 suite green under coverage collection;
  stage 1b all five floors INSIDE the R2 band and none demanding a raise (DwarfMapper 91.2 / floor 91.2,
  Generator **93.7 / floor 93.7 — unmoved by N3's product change and 13 tests**, DocTooling 95.7 / 95.7,
  CodeFixes 92.4 / 92.4, Testing 83.6 / floor 83.2); stage 2 exhaustion 766/766 in 10:24; stage 3 the fixed
  AOT stage, NativeAOT confirmed, all AotBench checks passed. This run is also the first time
  `housekeeping.ps1` was PARSED by pwsh since N5 inserted the three sweep calls — a mechanical edit nobody
  had executed, which is the H4/H8 genre.
- Live-tree decontamination sweep green at 44 product assemblies under `tests/**/bin`, none mutated. The
  `-Mutation` leg was deliberately NOT run: an in-situ Stryker execution belongs to M2's re-measurement, and
  running it here would produce a score nobody asked for.

### Nothing filed rather than fixed

No new I-row. The two judgement calls that could have become one — N4's local-vs-CI asymmetry and N6's
`[MapDerivedType]` residual — are both recorded in place (the table above, and the exactly-pinned residual
population) rather than deferred to a ruling: neither is blocking and neither is a defect.


---

## Layer 3 — S4, S6, S5, S2. All DONE, 2026-08-23. Every one of them UNEXERCISED.

Four nightly legs, four commits, one pathspec each: `de5f696` (S4), `e9d2a77` (S6), `3405353` (S5),
`aed031e` (S2). S3 and S7 were not this agent's — they need `src/**` and `tests/**`, which a concurrent
agent held for the whole session, so nothing outside `.github/workflows/`, `scripts/`, `Issues/` and
`docs/` was touched.

**Read this before quoting any of it as "in CI".** GitHub runs `schedule` only on the repository's DEFAULT
branch. Every job below is therefore *declared* and not *run* — the same distinction the top-of-file
comment in `ci.yml` already draws for the round-21 nightly tier, and the same one Layer 0's Z1 exists to
close. Master was pushed today, so the nightly tier is armed and these become real on the next merge+push.
Nobody may say "reproducibility is verified" or "the package size is gated" until a run id exists.

### S4 — reproducible-build verification, and the honest verdict it produced

The row's exit criterion said *"two builds of the same commit produce identical hashes, or the
non-determinism is named and recorded."* It is the second one, and the shape of it is worth having in the
ledger because it would otherwise be re-discovered every time somebody runs `sha256sum` on two packs.

**Measured** — Windows, SDK 10.0.101, at `81c4ace`, in a detached throwaway worktree so the concurrent
agent's tree was never disturbed. `CI=true` (which is what flips `ContinuousIntegrationBuild`,
`DeterministicSourcePaths` and `RestoreLockedMode`), both rounds from `git clean -xdf`:

| layer | result |
|---|---|
| whole-file SHA-256 of each `.nupkg` / `.snupkg` | **DIFFERENT** on every pack, all four files |
| `DwarfMapper.1.0.2-rc.1.nupkg` length | 253,420 B then 253,421 B — it differs by a **byte** |
| every build-produced entry inside | **BYTE-IDENTICAL**: `lib/net10.0/DwarfMapper.dll`, both analyzer DLLs, the `.nuspec`, `README.md`, the XML doc, `[Content_Types].xml`, and the `.snupkg` PDBs |

The whole difference is two OPC parts, neither of them build output:
`package/services/metadata/core-properties/<32 hex>.psmdcp` is named after a GUID NuGet draws fresh per
pack (its **content** was byte-identical), and `_rels/.rels` differs on exactly the one line that Targets
it — the sibling relationship pointing at the `.nuspec` kept a byte-identical `Id` across both runs.

So the verdict is **DwarfMapper's packages reproduce; NuGet's OPC envelope does not**, and
`scripts/repro-pack-check.py` is written to say exactly that and nothing looser. It does not skip the two
parts, it **normalises** them: the `.psmdcp` is compared by content under a canonical name, `.rels` is
compared byte-for-byte after replacing only the psmdcp `Target`+derived-`Id` pair, and the entry-NAME sets
are compared with the GUID canonicalised so an added or removed entry still fails. Whole-file hashes are
printed and never gated. Vacuity guards: same non-empty package set in both directories, ≥ 1 `.dll`/`.pdb`
among the compared entries, exactly one `.psmdcp` per package (that count is the premise the exemption
rests on).

**Sabotage demo**, four ways through the real script against the two real packs, each reverted: one
flipped bit in `lib/net10.0/DwarfMapper.dll`; a dropped `README.md`; a doctored `.psmdcp` body; a rewritten
`.nuspec` relationship in `.rels`. All four red naming the entry, and the two OPC parts get their own
wording so a red on them is not misreported as "build output moved". Clean pair: **26 entries compared
across 4 packages, 6 of them compiled assemblies/symbols, exit 0.**

**Nothing was added to make this pass.** `Deterministic`, `ContinuousIntegrationBuild` on `$(CI)`,
`EmbedUntrackedSources`, SourceLink + snupkg were all already set. The leg asks *"same inputs, same
bytes?"*; it does **not** ask *"same bytes from two different paths?"*, which is strictly harder, which
nobody has asked, and which is deliberately not claimed anywhere in the job or the script.

### I15 — filed, not fixed: `dotnet pack` is red today, on a suppression that already exists

Found while establishing the two-pack protocol, diagnosed to the line, and the remedy **proven** rather
than deduced. `src/DwarfMapper/DwarfMapper.csproj` sets
`<ApiCompatSuppressionFile>ApiCompatSuppressions.xml</ApiCompatSuppressionFile>` in a `<PropertyGroup>`.
The SDK reads `ApiCompatSuppressionFile` as an **ITEM** — `Microsoft.NET.ApiCompat.ValidatePackage.targets`
passes `SuppressionFiles="@(ApiCompatSuppressionFile)"` — and `…Common.targets` defaults that item to
`$(MSBuildProjectDirectory)/CompatibilitySuppressions.xml`, or to the legacy **property**
`$(CompatibilitySuppressionFilePath)`. A same-named property is simply never read. T1's intentional
rc-phase CP0006 suppression (`62f9ecf`) has therefore been inert since it was written, and `dotnet pack`
**exits 1**, reproduced from both the repo root and the project directory.

Proof of the remedy: copying the identical file to the SDK's default name makes the same pack **exit 0**
with no CP0006 — so the suppression's content is correct and only its wiring is not. Three one-line fixes
are listed in the row; whichever is taken, the inert property must go in the same commit.

**Why it hid:** `dotnet pack` runs in exactly one place — `release.yml`, on a version tag. No CI job packs.
The next tag would have failed the release build at the pack step. Left to the maintainer because `src/**`
was another agent's tree. S4 and S6 pass `-p:EnablePackageValidation=false` with that reason written into
both job comments; that neither hides nor fixes it, and re-enabling validation somewhere nightly is the
real closure.

### S6 — the package-size ceiling, and what R1 costs at this precision

`Assert-PackageSizeWithinCeiling` appended as a self-contained function at the **end** of
`scripts/gate-checks.ps1` — the placement is deliberate, so a concurrent edit to the R2 band checks above
it cannot collide — with its own nightly `package-size` job that **dot-sources** it rather than
re-implementing it (the same reason the mutation matrix dot-sources `Assert-NoMutatedProductBinaries`).

**Measured** 2026-08-22, Windows, SDK 10.0.101, `81c4ace`, `CI=true`, from `git clean -xdf`, two packs:

| package | measured | ceiling |
|---|---:|---:|
| `DwarfMapper.1.0.2-rc.1.nupkg` | 253,420 B / 253,421 B | **247 KB** |
| `DwarfMapper.Testing.1.0.2-rc.1.nupkg` | 48,508 B / 48,508 B | **47 KB** |

(the one-byte wobble is the `.psmdcp` name S4 measured, not build output).

**State the cost of R1 here rather than let the first red surprise someone: the headroom on DwarfMapper is
~530 bytes.** Any change adding half a kilobyte of IL is expected to re-measure the number in its own
commit. That is the same bargain the one-decimal coverage floors and the byte-exact allocation pins make,
and the failure message says exactly what to do. **A live consequence:** the ceiling is pinned at
`81c4ace`; the concurrent agent's generator work lands after it, and the first nightly on master may well
red for that reason alone. That is the ratchet working, not a defect — the fix is a re-measurement in the
commit that grows the package.

Deliberately **one-sided**, unlike the R2 band above it and unlike the two-directional allocation pins.
A package that shrinks is not a finding the way a smaller allocation is: allocated bytes are a behavioural
fact whose unexplained movement means the code changed, while package size has no correctness meaning at
all. The plan's own S6 wording is "raise-only-with-re-measure" and this follows it; if the forcing
direction is ever wanted, `Test-CoverageWithinBand` is the template and the comment says so.

Three vacuity guards: a ceilinged package that was **not produced** fails (the lesson
`allocation-baseline.json`'s missing-scenario guard taught), an empty or absent directory fails, and a
packed `.nupkg` with **no** ceiling fails — a newly shipped package must arrive with its measured ceiling
in the same commit. `.snupkg` is printed and not gated: symbols are not the consumer payload.

**Sabotage demo**, six scenarios through the real pwsh function against the real packs, each reverted:
600 bytes appended (247 → 248 KB, red naming both numbers), a deleted Testing package (red naming the
ceiling that lost its subject), an extra `DwarfMapper.Extra.9.9.9.nupkg` (red naming the unpinned package),
an empty directory, a non-existent directory, and the clean pair green before and after.

### S5 — the cross-OS legs and the preview canary

`cross-platform`: one matrix job over `windows-latest` and `macos-latest`, `fail-fast: false` because those
are two independent questions. It runs the sln **without** the `Category!=SurfaceMatrix` filter, following
`deep-test`'s precedent rather than `build-test`'s — the nightly tier wants the whole population, and a
second cross-OS job to preserve the isolation argument would double the runner cost of a leg whose point is
breadth. No coverage gate: the floors are measured on one platform and R1's "the floor equals the
measurement" cannot mean two measurements at once. It carries the ISSUE-038 SDK drift assertion
**per-runner** — `build-test` proves the pin on ubuntu and says nothing about the other two images — with
`shell: bash`, mandatory because `windows-latest` defaults to pwsh and that step is `sh`. Locked-mode
restore is expected to hold on both; if macOS ever reds NU1004 while ubuntu is green, that IS the finding
and it belongs in a ledger, not an exemption.

`preview-sdk-canary`: **verified live before the leg was written, not assumed** — the .NET release index
publishes channel 11.0 in support-phase `preview` with SDK `11.0.100-preview.7.26381.103`, and the
`setup-dotnet` SHA this workflow already pins accepts `dotnet-quality`. `continue-on-error: true`, a
deliberate clone of `roslyn-forward-compat`'s shape, with the flip condition written into the job: flip to
`false` on the first green run on pushed master, citing that run id — Z3's precedent, the same obligation,
and *"a gate that cannot fail is decoration"* is that leg's own sentence.

Its vacuity guard is the step that earns its keep: it asserts the resolved SDK's **major ≥ 11**. Without
it, a `setup-dotnet` fallback to an installed 10.0.x leaves every step below green and the canary becomes a
slow duplicate of `build-test` that proves nothing about the next major — the `Assert-MutantsWereTested`
genre exactly. It is **expected red at first**, and the comment names the reason so it is not misdiagnosed:
`TreatWarningsAsErrors` with `AnalysisLevel=latest-all` turns every analyzer rule a new SDK *adds* into a
build error, which is a real forward-compat signal but a different one from "the generator will not load".

Nightly rather than per-push, deliberately: the preview channel moves continuously, so a per-push leg would
put its own churn in every PR's log while being unable to fail anything.

### S2 — alert-only, and why the obvious spelling would have been decoration

The interesting part is not the threshold, it is that **the naive configuration compares nothing at all**.
Read out of `github-action-benchmark` v1.22.1's own `dist/src/write.js`: `handleAlert()` returns EARLY when
both `comment-on-alert` and `fail-on-alert` are false, so with neither set the `alert-threshold` input is
dead and no comparison happens. `comment-on-alert` needs a `github-token` and `contents: write` — an
automated repository write this workflow does not take.

The wiring that works tokenlessly: **`fail-on-alert: true` with `continue-on-error: true` ON THE STEP.**
The threshold becomes real; the job and the workflow stay **green**; the alert table lands in the log as an
ignored step failure plus a job summary (`summary-always: true`). Verified from the same source that
`writeBenchmark()` calls `writeBenchmarkToExternalJson` → `handleSummary` → `handleAlert` in that order, so
the data file is written *before* the alert throws and a regression does not freeze the baseline.

The cost is stated in the job rather than hidden: `continue-on-error` also swallows a genuine action error.
That is why the step **before** it is a deterministic input check that DOES fail the job — absence and
malformation are not nondeterministic measurements, and "the alert leg could not find its subject" must not
read as "no regression". `conformance-gate.sh` fails on absence for the same reason.

**That guard was executed, not merely written**: the `run:` text was extracted from the parsed YAML and
driven through bash five ways — a valid report (2/2 benchmarks with `FullName` + numeric `Statistics.Mean`,
exit 0), entries lacking them (exit 1 naming the two fields the adapter reads), an empty `Benchmarks` array
(exit 1), malformed JSON (exit 1), a missing file (exit 1, listing what did arrive). The adapter's field
contract was read out of `extract.js`; the same two fields are what `Assert-BenchAllocationsPinned` already
reads out of that report, which is how the report shape was confirmed without running a 7-minute smoke.

The platform caveat is in the job's own comment as the exit criterion demanded: the Dict-vs-Mapperly figure
is **2.13× Windows / ~1.14× Linux**, these are **Linux** numbers, and they must never be quoted as the
Windows ones. The 3.29× allocation lead is the platform-independent claim and round-21 T8 already pins the
deterministic half exactly. The threshold is 150 % and generous on purpose: the smoke is a ShortRun
(1 launch, 3 warmup, 3 iterations) on a shared 4-core runner.

Storage is `external-data-json-path` + `actions/cache` — the action's documented tokenless mode, **not**
`auto-push` to gh-pages, because an automated push is the one thing this repository does not do. The cache
key **rotates** per run with a prefix restore-key: `actions/cache` never overwrites an existing key, so the
README's fixed-key recipe would pin the baseline to the first night forever. GitHub's 7-day eviction is
named in the comment; a week-long gap loses the baseline and the action then reports no previous benchmark,
visibly.

### Measurements at the Layer-3 tip (`aed031e`), run in an isolated detached worktree

The concurrent agent held `src/**` and `tests/**` all session, so every build and every pack ran in a
throwaway `git worktree` at `81c4ace` with the four Layer-3 commits' files checked into it — no shared
`bin/`, no shared `obj/`, no interference in either direction.

- Whole-solution build `--no-incremental`, **samples included**: **0 warnings / 0 errors**, 41.7 s.
- Full suite, foreground, all 9 projects, **no filter** (SurfaceMatrix included): **7,853 / 0** in 1:16 —
  identical to the baseline recorded at `1decd32`, as it must be: nothing this layer touched is compiled.
  Generator.Tests 6,764 (5,898 + the 866-cell census).
- YAML: `yaml.safe_load` clean after every append; 14 jobs parse, the four new ones with the intended
  `if:`, `needs:`, `continue-on-error:` and `timeout-minutes:` values read back out of the parsed document
  rather than eyeballed.
- Pack, two rounds, `git clean -xdf` between: 22.0 s and 20.0 s (Windows, 12 cores, warm NuGet cache).
- Windows suite basis for the S5 estimate: build 13 s warm, 56 s for 6,987 tests with the matrix excluded.

**Every `timeout-minutes` added this layer is a STATED ESTIMATE and says so in the job**, with its
derivation from the local figures above written out — 25 (reproducible-build), 15 (package-size), 20
(cross-platform), 20 (preview-sdk-canary), 10 (bench-wall-time-alert). Replacing each with a real
hosted-runner wall-clock is a Z1-style first-run obligation, and so is the ubuntu re-validation of S6's
Windows-measured ceiling — where a per-OS difference is a re-measurement in the commit that sees it, never
a tolerance band (the Z2 rule, applied to sizes).

### What was refused, and why

- **No gate on wall-clock (R4).** S2 alerts and cannot fail. The one thing in that job that CAN fail is the
  deterministic presence/shape check on its input, which is an absence oracle, not a measurement.
- **No workaround for I15 from a distance.** Setting `CompatibilitySuppressionFilePath` in
  `Directory.Build.props` would have made `dotnet pack` green from outside `src/**` and left the inert
  property in the csproj as a trap. Filed instead.
- **No `auto-push`, no `github-token`, no permission widening.** `permissions: contents: read` is unchanged
  for all four new jobs.
- **No edit to `deep-test` or `housekeeping.ps1`.** S3 lands in the deep tier and belongs to another agent;
  every Layer-3 leg is free-standing so the two cannot collide. The cost is one extra pack in
  `package-size` that `reproducible-build` also performs — accepted, because a size red and a
  reproducibility red mean different things and one job cannot report two verdicts.
- **`CiGateScanTests` was NOT extended** to declare the four new jobs, though it is exactly the right place
  for it: that file is `tests/**`, which was out of bounds. Four `[InlineData]` rows are the whole change,
  and until they exist a deleted job here is a silent deletion.

---

## I14 — the last Layer-1 item. DONE, 2026-08-23. Commits `0be2794` (lift), `2ed1be4` (scoped kill) and the signpost guard below.

**Ruling: (a) LIFT — and the whole-mapper kill goes anyway.** The two are separable and both landed. (a)
removes I14's own trigger; the cascade outlives it, because a HashSet target, a `Use=` converter, a hook and
`ReferenceHandling` still refuse and must not cost the consumer their `Map`.

*Decided from the projection's own emission vocabulary, not from principle.* The probe that reproduced the
four failing cells also showed the two diagonals emitting every fragment the four need: struct→struct ships
`__s.M.HasValue ? (global::D1?)new D1 { … } : null`, class→class ships `__s.M == null ? null : new D1 { … }`.
The missing directions are recombinations — `HasValue ? new D1 { … } : null` for a value-typed source into a
nullable-annotated reference (natural type is the target, so no cast), and
`== null ? null : (global::D1?)(new D1 { … })` for a reference source into `Nullable<D1>` (the cast is
REQUIRED: `null` and a struct have no best common type, CS0173). No new construct class enters the tree.

**Cost if wrong:** these cells emit nothing today — a hard build stop — so no working consumer can regress.
The residual risk is a provider failing to translate a recombination of constructs it already translates on
the diagonals. Bounded, reversible in one commit.

**Rejected branch (b), refuse cleanly:** the lift IS expressible, so refusing would enshrine at `Project` the
endpoint disagreement N0's LIFT ruling exists to remove at `Map`, and would leave the same member answering
differently depending on which method the caller reached for. That is the defect, not the remedy.

**Root causes — the same confusion twice, one function apart.** (1) `ResolveProjectionExpr` gated on
`IsNullableValue(tgtType)`, the destination's KIND; widened to `TryGetNullableCapableTarget`, the predicate N1
wrote for `.Map`, plus a new branch for the reverse genre (reference source → `Nullable<U>`), which nothing
above caught because `Nullable<D1>` is excluded from `IsMappableObjectPair` by name. (2) The cascade:
`HasBlockingError` was `Any(d => d.IsError)`; `DiagnosticInfo.ScopedToMethod` and
`TryScopeProjectionRefusalToItsMethod` confine a DWARF028 to its own method, and **only** a DWARF028 — every
other error a projection collects describes the SOURCE MODEL, is equally true of the `Map` methods over the
same pair, and keeps the whole-class kill. The method is dropped either way: a projection carrying only the
members that resolved returns the rest silently unset.

**Sibling hunt — searched / found / fixed.** Searched all three `HasBlockingError` consumers, both
`EmitDWARF028` sites outside `Projection.cs`, and eight Map/Project shape probes.
- **FIXED, pre-existing and silent:** `class Src { Nested? N }` → `class Dst { NestedStruct N }` through
  `Project` emitted `== null ? null : new NestedStruct { … }` — **CS0037**, the `EmittedInvalidCode` genre,
  reported by nothing. Verified against the UNMODIFIED tree before claiming it pre-existed.
  `ResolveProjectionNestedObjectExpr` asked whether the source could be null without asking whether the
  target could hold the result.
- **FILED, measured, not fixed — I17:** a `DWARF001` on one `Map` method kills every other method on the
  class. Identical escalation shape, different contract (completeness), far wider blast radius; needs its own
  ruling.
- **FILED — I18:** the compiler-testing arc never samples `Project` at all.
- **Checked and NOT defects:** enum by-name across a nullable pair (documented in DWARF028's own reason list;
  an expression tree cannot switch on names); `S1? → D1` into a null-incapable target (`Map` applies
  NullStrategy, `Project` refuses — the honest split, now pinned in both directions).

**Evidence is runtime, not emission (B19).** The six kind pairs are EXECUTED over `AsQueryable()` with a null
row and a non-null row in ONE query, so the lift, the value and the cardinality are all asserted, **with the
two diagonals in the table as CONTROLS** — a fix that lifted four cells by breaking two would pass a four-cell
test. The scoped-kill tests do not claim "it emitted": they count CS8795 (exactly one, on the projection) and
assert the ABSENCE of DWARF078, with a class-level DWARF010 as the control. *Stated limit:* LINQ-to-Objects
proves the tree compiles and evaluates, not that an ORM translates it — no provider runs here.

**No deep re-run, and the reason is a measurement.** `grep -rl IQueryable` over `tests/DwarfMapper.CompilerTests`,
`tests/DwarfMapper.DifferentialTests` and `tests/DwarfMapper.CorpusTests` returns **nothing**: projections are
not in the sampled space, so no I14 exclusion ever existed, none was deleted, and the space is byte-identical.
K0/K1/MR digests therefore cannot have moved. That gap is I18, and it is why both defects here were found by
hand probes.

**One more defect, found reviewing the new mechanism and fixed rather than filed.** DWARF096 claims "the rest
of this mapper WAS generated", which is FALSE when some OTHER method on the class also has an error — the
class dies anyway and DWARF078 says so, so both were reported and one was lying. Reproduced before fixing (a
cleanly-refused projection beside a Map method with an unmapped member). The scoped signpost now stands down
under `HasBlockingError`; the DWARF028 underneath is reported either way. The earlier class-level control could
not catch it — its DWARF010 lived in the projection's OWN diagnostic range, so no DWARF096 was ever minted.

**Measured.** Whole-solution build **0 W / 0 E** with samples at every commit. Suite **7,853 → 7,864**
(lift, +11 tests) **→ 7,872** (scoped kill, +5 tests, +3 NegativeCases rows) **→ 7,873 / 0** (the
signpost guard, +1), foreground, ~78 s. Baseline
re-measured at `81c4ace` in this session rather than taken on trust: 7,853 exactly. Census **866 / 866**;
`EmittedInvalidCodeCellCeiling` still exactly **0**; RatchetInvariantScan + GateBandLogic + DeepTierSelf
**15 / 15**; `src/` analyzers clean. Five-file sync for **DWARF096** complete, index 91 → 92, and the
NegativeCases ratchet tightened with it (DWARF028 left `PredatesThisProject`, **66 → 65**).
CONTROLLER LOG (round 23, Layer 1 + Layer 3 partial):
N3-N6 ACCEPTED (ff744ea I6, 86f68f4 I4, 9589e5a B28, 1decd32 B37, 81c4ace evidence). I6's one-liner alone
would have been A REGRESSION IN DISGUISE (framework-dependent publish still drops an apphost; gate green,
proves nothing) - now asserts absence of managed assembly + runtimeconfig.json. Local AOT stage vs CI's
aot-trim-gate documented as NOT proving the same thing, deliberately unharmonised. I4's naive sweep REDS ON
A CLEAN TREE (Generator.Tests.dll contains the literal "Stryker") - name restriction measured; its own test
first "passed" while checking nothing until Show flattened multi-line messages. B28 found a THIRD mode
nobody had measured + the class engine + 3 siblings. B37 found a collapse the row never named (MapNullSkip
bool variant-blind) and distinguishes "makes B24 measurable" from "measures B24".
S2/S4/S5/S6 ACCEPTED (de5f696, e9d2a77, 3405353, aed031e, f5a6f28, 7393f5b). HEADLINE I15: dotnet pack
EXITS 1 TODAY - ApiCompatSuppressionFile set as a PROPERTY, SDK reads an ITEM; round-21's CP0006
suppression inert since written; next release tag would have failed at release.yml's pack step; no CI job
packs so nobody saw it. S4's honest verdict: DwarfMapper's own bytes DO reproduce, NuGet's OPC envelope does
not (per-pack GUID in .psmdcp) - normalised BY NAME, not skipped. S2 found the naive config compares
NOTHING (handleAlert returns early when both comment and fail modes are off) + a fixed cache key would
freeze the baseline at night one. All five new jobs DECLARED BUT NEVER RUN; two first-run reds pre-named.
I14 ACCEPTED (0be2794 lift, 2ed1be4 scoped kill + DWARF096, a37df51 records, eea657c signpost guard).
Ruling (a) decided FROM THE PROJECTION'S OWN EMISSION VOCABULARY: the two working diagonals already ship
every fragment the four failing cells need, so the lift is a recombination, not a new construct class.
Cascade proved gone BY COUNTING (2xCS8795+DWARF078+0 chars -> 1xCS8795+DWARF096+Map body), with a
class-level DWARF010 control still killing everything. Sibling hunt found a PRE-EXISTING SILENT CS0037
(EmittedInvalidCode genre), verified against the unmodified tree first. Advisor review caught DWARF096 and
DWARF078 contradicting each other. Deep re-run correctly NOT run, and the reason IS a measurement.
Suite 7,873/0.
OPEN AND QUEUED: I15+I16+I18 dispatched (release-blocker first). Then Layer 2 (M1-M5, needs a quiet
machine for the re-measures), then I17 (ruling from evidence, I14 precedent named), S3, S7, Layer 4 V1-V3,
wrap, merge. SHUTDOWN ORDER WITHDRAWN by the maintainer - do not power off.
PROCESS NOTE: two agents sharing one worktree+branch produced a near-miss (a git stash swept the other's
edit, recovered). SEQUENTIAL ONLY in this worktree from here.

---

## Round 23 — I15 FIXED (release blocker)

`dotnet pack` is green. **Remedy (1) of the three: rename to the SDK's default name**
(`src/DwarfMapper/ApiCompatSuppressions.xml` → `CompatibilitySuppressions.xml`), inert
`<ApiCompatSuppressionFile>` property deleted in the same commit.

**Why (1) and not (2) or (3).** It is the remedy the filing PROVED rather than deduced; it removes wiring
instead of adding more; and — the deciding argument, which none of the three descriptions in the row
mentions — it is the only one that keeps the SDK's own remediation advice working. The CP0006 message
says to rebuild with `/p:ApiCompatGenerateSuppressionFile=true`; that switch WRITES to
`CompatibilitySuppressions.xml`. Under remedy (2) or (3) the SDK's suggested fix quietly produces a
second, unread file beside the real one — the same trap, one level out.

**Exit codes, foreground, SDK 10.0.101** (read from `$?` on a redirect: a piped `$?` reads the pipe's
tail, and the first repro of this nearly printed `EXIT=0` on a red pack):

| pack | before | after |
| --- | --- | --- |
| `src/DwarfMapper/DwarfMapper.csproj` | **1** (CP0006) | **0** |
| `src/DwarfMapper.Testing/DwarfMapper.Testing.csproj` | 0 | **0** |
| `DwarfMapper.NET.sln` (release.yml's shape) | — | **0** |

**The suppression is ACTIVE, shown by making it fail.** Emptying `<Suppressions>` (content only, file in
place) → same pack **exit 1, CP0006**. Restoring the block → **exit 0**. So the green is the
suppression's doing, not the baseline drifting, validation switching itself off, or the break vanishing.

**`DwarfMapper.Testing` — checked, NOT the same shape.** `EnablePackageValidation` is on, but there is no
`ApiCompatSuppressionFile` property, no suppression file, and deliberately no
`PackageValidationBaselineVersion` (never published — nothing to download). Structural validation only,
nothing to suppress, so nothing inert waiting to matter.

**Trap named at the fix site.** The deleted property's place now carries it: item not property,
`SuppressionFiles="@(ApiCompatSuppressionFile)"`, default `CompatibilitySuppressions.xml`, legacy
property `$(CompatibilitySuppressionFilePath)`, a same-named property warns about nothing — and the
escape hatch for a non-default name is an `<ItemGroup>`, never a `<PropertyGroup>`.

**Two comments retired because the fix falsified them.** `reproducible-build` and `package-size` both
argued for `-p:EnablePackageValidation=false` partly from *"it is ALSO broken today (I15)"*. Gone. **The
flag stays** on both, carried by the two reasons that were always sufficient on their own: validation
downloads the rc.1 baseline from nuget.org (an external input a reproducibility leg must not have), and
its red means *"the API changed"* where those jobs' reds mean *"the bytes moved"* / *"the package grew"* —
one job cannot report two verdicts.

**Left open and named:** running validation *nightly* rather than only at a version tag. It needs a job of
its own (its own verdict), and minting a nightly job in this commit would have collided with I16 pinning
the current five job names into `CiGateScanTests` in the very next one. Its own task, not a rider on a
one-line release-blocker.

**Measured:** whole-solution build **0 W / 0 E** with samples; suite **7,873 / 0** foreground, ~69 s;
census **866/866**; `EmittedInvalidCodeCellCeiling` **0**.

---

## Round 23 — I16 FIXED, and the filing's own scope was too small

**The claim was verified, not taken, and it does not hold.** The row said *"the whole change is five
`[InlineData]` rows"*. Two prerequisites checked first: all five new jobs live in `ci.yml` (the only file
the scan reads — `release.yml` declares none of them, so no filename parameterisation), and all five sit
at two-space indent (what the literal `"\n  " + job + ":"` matcher needs). Both hold, and the scan pins
nothing else — no job count, no step shapes. So five rows *would* have worked, for those five jobs.

**Then the job keys were enumerated instead of trusted, and the hole was EIGHT.** `surface-matrix`,
`roslyn-forward-compat` and `sbom` were also unlisted, and had been since well before round 23. The list
of gates was **6 of 14** while a task was actively auditing it. `surface-matrix` is the sharp one: the
BLOCKING job that runs the 866-cell census — this round's own headline measurement — deletable in silence
by the very test whose doc comment says a deleted job "makes every build green and every proof silent".

**So the finding is the list, not the eight jobs.** A hand-maintained list of things-to-check drifts
behind the thing it checks, because a missing row fails nothing. Five rows would have made it 11 of 14
and left the failure mode fully armed for the next job anyone adds.

**Fix: 8 rows + a completeness `[Fact]`.** `Every_job_in_ci_yml_is_covered_by_a_row_of_this_scan` parses
`ci.yml`'s own two-space job keys — anchored AFTER `jobs:`, because `on:` and `permissions:` carry
two-space keys too (`push:`, `schedule:`, `contents:`) and a whole-file sweep would demand rows for them —
and asserts set equality **in both directions**. A job with no row reds; a row for a job that no longer
exists reds too, because a stale row is a scan that proves nothing while looking like it proves
something, which is this class's own subject. Non-vacuity floor (`> 5` jobs parsed) so a shape change is
loud about the PARSE rather than quiet about the comparison. Rows moved `[InlineData]` → one
`[MemberData]` source for one reason only: the guard must read the same collection the theory does. Two
lists can drift; one cannot.

**Scope did not widen with the list.** Declaring a job proves it EXISTS — not that it is a required check,
not that nobody re-ran the workflow with it skipped. And per the filing's note, three rows say so in their
own consequence text: `preview-sdk-canary`, `bench-wall-time-alert` and `roslyn-forward-compat` are all
`continue-on-error`, so for those the test asserts DECLARATION and nothing about a verdict.

**Sabotage demo, both directions, foreground, reverted by explicit pathspec (no stash):**

- Deleted the whole `cross-platform` job (64 lines) → **2 reds, both naming it**:
  `The_workflow_declares_the_gate_job(job: "cross-platform")` — *"Windows and macOS would stop being
  tested at all…"* — and the guard — *"this scan has row(s) for job(s) ci.yml no longer declares:
  [cross-platform]"*.
- Appended a `brand-new-nightly-gate:` job with no row — precisely how I16 came to exist → guard reds:
  *"ci.yml declares job(s) that no row of this scan names: [brand-new-nightly-gate]"*.

**Measured:** build **0 W / 0 E** with samples; suite **7,873 → 7,882 / 0** (+9) foreground, ~67 s;
census **866/866**; scans **15/15**; `EmittedInvalidCodeCellCeiling` **0**.

---

## Round 23 — I18 DONE: the projection endpoint is inside the arc, and it paid on its first run

`grep -rl IQueryable` over the compiler-test projects no longer returns nothing.

**Reach: K0 and K1. K2 left, with the reason.**

- **K0** — `TypeGraphSmokeTests.Sampled_graphs_with_a_projection_compile_silently_clean_or_refuse_loudly`:
  must-compile-or-refuse over graphs whose mapper declares `IQueryable<D0> Project(IQueryable<S0>)`
  beside `Map`.
- **K1** — new `ProjectionAgreementTests`, the leg that matters: `Project` over a one-element
  `AsQueryable()` must equal `Map`'s result for the same source instance, diffed with the SHIPPED
  `[RoundTrip]` differ, plus the two accepted `PinnedCorpus` rows as deterministic anchors.
  Deliberately NOT a second naive oracle — that would re-litigate every semantic K1 already arbitrates.
  `Map` is the reference *because* K1 holds it to the naive oracle, so agreement inherits that verdict
  and a disagreement localises to the projection. The filing's own "fifth mode, not a new instrument".
- **K2 left** — each MR iteration is 2–3 full runs, and the agreement relation already expresses the
  consistency property a projection MR would test. Triple cost to re-derive it over the same grammar.

**Stated limit (B19):** `AsQueryable` means LINQ-to-Objects, so enumeration compiles and evaluates the
tree. It does not prove an ORM translates it. No provider runs.

**The decision that carries the task: `Project` is OPT-IN at the renderer, never unconditional.**
Emitting it always would have re-cut every existing population — the projection refuses a strictly wider
grammar and ONE error-severity refusal makes the whole run `RefusedLoudly`, so cases K0/K1/MR sample as
accepted today would have flipped to "refused" and stopped being checked, every digest moving with
nothing saying why. **Proven:** K0's digest is still `74230fddc0bc6ce5`, K1's still `91e8992439c99f57`.

**Determinism (R4 / I11) respected:** two registered populations, both through `PinnedSampling`, both
pinned in `DeepTierSelfTests`. No unseeded fast-tier gate.

**Counts chosen off the ACCEPT column, not the wall column** (2026-08-23, 12-core reference machine):

| cases | 25 | 50 | 100 | 200 | 1000 (deep) |
| --- | --- | --- | --- | --- | --- |
| accepted / executed | **2** | 7 | **21** | 48 | **233** |
| refused | 23 | 43 | 79 | 152 | 767 |

At the obvious 25 each leg would have carried its whole invariant on TWO cases, one grammar tightening
from tripping its own vacuity floor. Fast **100** buys 21 for the same ≈1 s; deep ×10 = 1000.

**Digests** (each identical across two runs): fast smoke `d714bc2721ff2ec9`, fast agreement
`fd851b04e07defbe`; deep smoke `2f37f8364390478e`, deep agreement `e1606cb3dbe47261`.
**Wall:** CompilerTests fast 5.9 / 5.9 / 5.9 s (was 2–3 s); deep 45.0 / 44.4 s (was 31–34 s).

### Found and fixed, infrastructure, no I-row: the arc could not have compiled a projection at all

First run of the smoke leg reddened on EVERY sample with **CS1069** — *"the type name 'Queryable' could
not be found … forwarded to assembly System.Linq.Queryable"*. `CompilerTestHarness`'s reference set is an
`AppDomain.GetAssemblies()` sweep, which sees only what the process has already LOADED, and nothing here
had ever touched `Queryable`. From the outside a missing metadata reference is indistinguishable from a
silent miscompilation, which is why the leg reported it as one — and why the assertion now prints
diagnostic MESSAGES, not bare ids. Pinned with a `typeof(System.Linq.Queryable)` reference, which also
forces the load so it cannot go missing by accident of test ordering.

### FOUND, SHRUNK, CLASSIFIED, PINNED, FILED AS I19 — first run of the agreement leg

**A null source collection maps to an EMPTY destination collection through `Map` and to `null` through
`Project`.** `Map` emits the documented `NullCollections = AsEmpty` helper; `Project` emits
`__s.M == null ? null : …` and never consults the option — `NullCollections` appears nowhere in the
projection pipeline. Shrunk by hand to one `List<int>` member across two classes (the sampled case's
record-struct source, CtorParam shape, Guid elements and three spare nodes were all irrelevant).

**Classified: PRODUCT defect.** `docs/options.md` states one behaviour and does not except `Project`.
It is I14's shape one contract over — the same member answering differently depending on which method
the caller reached for, which I14's ruling called *"the defect, not the remedy"*.

**Filed, not fixed, and NOT absorbed.** `I19_a_null_source_collection_diverges_between_the_endpoints`
pins the divergence deterministically and asserts the wrong behaviour ON PURPOSE, with the four-step
cleanup written into its assertion message so the fix cannot land silently — the recorded-divergence
shape the surface matrix already uses, which fails the build the moment it starts working. The sampled
leg excludes exactly one population axis (`Populate(…, nullCollections: false)`), named at the call site
and on the parameter, deletable with the fix — the same narrow exclusions I5 and I7 used and retired.

**Measured:** build **0 W / 0 E** with samples; suite **7,882 → 7,887 / 0** (+5) foreground, ~69 s;
census **866/866**; scans **15/15**; `EmittedInvalidCodeCellCeiling` still exactly **0**.

---

## I19 — `Project` ignores `NullCollections`. DONE, 2026-08-23. Commit `cbfd415`.

**Ruling: HONOUR the option — candidate (a) of the three the surface-matrix store itself listed.** Decided
from the DOCUMENTED OPTION, not from the endpoint's convenience: `docs/options.md` states one behaviour for
`NullCollections` with no endpoint qualifier, and an empty-collection materialisation *is* expressible in an
expression tree — so this is not the "a provider cannot translate it" class DWARF028 exists for, and **no cell
needed I14's scoped-refusal mechanism**. `DWARF096` / `ScopedToMethod` were available and are not used.

**Cost if wrong:** the emitted null arm is a `new List<T>()` / `Array.Empty<T>()` / `Enumerable.Empty<T>()`
inside a conditional a provider must translate. No provider runs here (B19), so what bounds the claim is that
**no ternary is added where none existed** — the guard was already emitted for every source member that may be
null, and only its null arm changed. Reversible in one commit.

**Rejected, both named by the store's own row.** (b) refuse with DWARF028 unless `AsNull`: a capability
regression ("you cannot project a nullable collection under default options") larger than the divergence it
closes. (c) document the split as intentional: the code and the documented default may not disagree in
private, and I14's ruling already called the same member answering differently per endpoint *"the defect, not
the remedy"*.

**Root cause: one read that never happened.** The option reached four `.Map` call sites and not the fifth. It
is now threaded through all four projection resolvers, and the effective answer is computed with **the same
line the runtime endpoint uses** — `nullAsNull && IsNullableReferenceType(tgtType)` — which is what keeps the
endpoints agreeing in a nullable-OBLIVIOUS context too, where both degrade.

**Residual, RULED not overlooked, and now in `docs/options.md`.** The guard is still gated on
`ProjectionSourceMayBeNull`, so a `#nullable`-enabled consumer whose NON-nullable collection member is null at
runtime throws from `Project` where `Map` returns empty. Guarding unconditionally would close it and insert a
null check into EVERY correctly-annotated projection — the one branch that can stop a working consumer's query
translating. Pinned, so widening it later is a decision rather than a drift.

**The alarm's four-step cleanup, done, plus one.** The divergence pin was REPLACED by a pin on the documented
value (sampling proves the endpoints AGREE, and agreement is satisfied by both being wrong together);
`nullCollections: false` is gone from `RunAgreementLeg`; and the exclusion is gone from
`ReflectionOracle.Populate`'s **parameter**, not just its documentation — no caller passed `false` any more,
and a dead population axis threaded through four methods is a lie about coverage.

**Evidence is runtime.** 16 executed cells (option × target-nullability × four target kinds), both endpoints,
one query, with the AsNull-over-non-nullable degrade as the control and a non-null row in every cell.

**Sibling hunt, and it paid.** Every documented class option was checked against the projection pipeline.
**FILED as I20:** under `[DwarfMapper(ImplicitConversions = false)]`, `long → double` is an **Error DWARF038**
at `.Map` and **no diagnostic at all** at `.Project`. It also invalidates a standing `NotApplicable` excuse
whose own liveness check cannot see it, because `OptionProbe`'s fixture carries no cross-category numeric pair.

**Deep re-run: executed, digests correctly did NOT move, and that is a measurement.** The deleted exclusion was
a POPULATION axis, not a case-space one, and the digest is over the pinned CASE SET. Proven against a
throwaway worktree at `9ccf849`: all six deep digests byte-identical before and after.

**Measured.** Build **0 W / 0 E** with samples; suite **7,887 → 7,905 / 0**; census **866/866**; scans
**15/15**; `EmittedInvalidCodeCellCeiling` **0**. Surfaces moved with the behaviour in the same commit:
options.md, CHANGELOG `### Fixed`, `OptionContractTests` NotApplicable → **Honoured** (`NotApplicablePin`
7 → 6), the declared divergence retired (`DivergenceFindingCeiling` 2 → 1, `DivergentCellCeiling` 4 → 2),
SURFACE-MATRIX-FINDINGS.md RESOLVED.

---

## I17 — a `DWARF001` on one method kills every other method. DONE, 2026-08-23. Commit `a0a1796`.

**Ruling: `DWARF001` is PER-METHOD — and the rule that makes withholding SAFE is that the method's
DECLARATION must survive it.**

The first half is decided from the contract: completeness is evaluated over one (source, target) pair and
honours one method's `[MapIgnore]` set, and the diagnostic's own remedy names *the method*. Unit of evaluation
and unit of remedy are both the method. The second half is a MEASUREMENT, taken by probing the fix: withholding
a `[GenerateMap]` pair made a sibling emit **CS0103** in a file the consumer cannot edit — the
`EmittedInvalidCode` genre, ceiling 0.

**Cost if wrong:** the build fails either way (DWARF001 unchanged, still an Error), so nothing that compiled
compiles differently; the residual risk is a class-level analysis reading a DECLARED method that is not
EMITTED, and the design answers that rather than sampling it.

**Rejected: "an incomplete mapper is a class-level statement."** Refuted by the contract's own units, and in
the other direction by the `[GenerateMap]` measurement — an incomplete pair there kills a mapper that has no
partial declaration for it, so 100 % of the CS8795 is collateral.

**THE FIRST CUT WAS WRONG AND THE SUITE CAUGHT IT.** I14's implementation (do not add the model) reddened
`RestatesBaseTests`: `CheckRestatedBases` reads `methods`, so a withheld pair stopped being a DECLARED pair and
`DWARF084` degraded from *"finds no base pair"* to the false *"names a pair this mapper does not declare"*.
One measured instance of a class — the DWARF060 collision pass, `[RestatesBase]` drift, recursion capability
and `publicMethodLocs` (keyed BY INDEX into that list) all ask what the mapper DECLARES. The model is now
built and recorded with a new `MapMethodModel.Withheld`; only the **six** emission and aggregation sites skip
it. Enumerable and checked, which is the round's own lesson about a fix applied to 1 of N sites.

**Families, each CHECKED.** Scoped: create-map, update-into, projection (whose scopable set widened to
{DWARF028, DWARF001} — reading completeness as per-method at four endpoints and class-level at the fifth would
make the same error proportional in four places and not the fifth). NOT scoped, with reasons that are rulings
and are pinned as controls: span and async-stream map their ELEMENT pair through a mapper **shared by every
route that reaches it**, so its incompleteness is true of each of them (the DWARF090 argument); the same for a
nested pair; `[GenerateMap]` on the CS0103 measurement.

**Proof by counting.** Before **1×DWARF001 + DWARF078 + 2×CS8795 + 0 bytes**; after **1×DWARF001 + 1×DWARF097
+ exactly 1×CS8795 naming MapBad**, with MapGood's body **byte-identical** to a solo mapper's — so a scoping
that quietly perturbed the sibling fails even though every count passes. **Runtime evidence exists in the one
shape that can carry it:** an implicitly-private `partial void` may legally have no implementing part, so the
withheld method simply disappears, the assembly loads with no C# error, and the surviving sibling is executed.

**Measured.** Build **0 W / 0 E** with samples; suite **7,905 → 7,925 / 0**; census **866/866**; scans
**15/15**; `EmittedInvalidCodeCellCeiling` still exactly **0** — and the CS0103 above is why that is a claim
and not a formality. Six-file sync for **DWARF097**, index **92 → 93**; two case files moved from DWARF078 to
DWARF097 in their EXPECT sets; `MapMethodModelBoolFlagBaseline` **15 → 16**. No deep re-run owed: no exclusion
was deleted and the renderer builds COMPLETE pairs, verified by comparing every fast-tier digest (all
unchanged).

CONTROLLER LOG (round 23, I19 + I17):
I19 ACCEPTED (cbfd415). Ruling taken FROM THE DOCUMENTED OPTION, and the store's own candidate (a) was the
one that survived. The scoped-refusal mechanism was available and deliberately NOT used - every cell is
expressible. Alarm test REPLACED, not deleted: agreement alone is satisfied by both endpoints being wrong
together. The population axis was deleted at the PARAMETER, not just its doc. Deep digests correctly did
not move and that was PROVEN against a throwaway worktree, not asserted. Sibling audit filed I20 - the same
family one option over, and SILENT.
I17 ACCEPTED (a0a1796). Ruling has TWO halves and the second is a measurement: the first implementation
(I14's) produced CS0103 in generated code for a [GenerateMap] pair, and a separate first cut degraded
DWARF084 - both caught before landing, one by probing and one by the suite. The mechanism changed as a
result: the model is RECORDED and tagged, not dropped, so every class-level analysis still sees the
declaration. Span/async/nested keep the class kill for the DWARF090 shared-pair reason, pinned as controls.
Suite 7,925/0.
OPEN: I20 filed (ImplicitConversions silently unread at Project, plus a NotApplicable excuse that passes its
own liveness check while being wrong).
PROCESS NOTE (self-caught, one commit later): the I17 CHANGELOG entry was WRITTEN BEFORE the CS0103 probe
reversed the [GenerateMap] half, and never re-read - so it shipped in a0a1796 promising a behaviour the code
refuses ("this reaches ... the [GenerateMap] pair", "two shapes keep the whole-class kill" when there are
three). No gate catches it: Scan9 checks that an id is ANNOUNCED, not that the prose is TRUE. Corrected in a
docs-truth commit, together with docs/diagnostics.md's dwarf096 section and DWARF096's descriptor
description, both of which still read as DWARF028-only after the projection's scopable set widened to
{DWARF028, DWARF001}. Same shape as the DWARF096/DWARF078 contradiction I14 caught late. RULE FOR THE NEXT
AGENT: after any design reversal, re-read every consumer-facing surface already written for the old design -
CHANGELOG, docs/diagnostics.md, and the descriptor description field, which is the one nobody remembers.

S7 + S3 + the README badge audit: DONE (3851903 audit, 1a7bf3e S7, 63e7248 S3, bdede39 the I21 filing).

BADGE AUDIT FIRST, AND IT WAS NOT COSMETIC: master had moved under this branch. 8328c81 ("removing
unnecessary badges") deleted commit-activity, code-size and top-language; this branch still carried all
three, so merging it would have RESURRECTED badges the maintainer deliberately removed. The block is now
master's surviving six with the findings applied to those and only those: ?branch=master on the CI badge
(Serilog's convention - an unpinned badge renders whichever branch ran last); the workflow's `name:`
renamed ci -> CI, verified safe first (every reference in tests/scripts/docs is to the PATH, CiGateScanTests
parses two-space job keys under `jobs:`, nothing reads `name:`); the vpre badge labelled "NuGet prerelease"
(this project ships only rc's); ?logo=dotnet on the .NET badge; ?label=last%20commit dropped as the default.
SKIPPED with the reason: the ?label=code finding (that badge no longer exists on master). The licence badge
needs no change and must NOT become dynamic - GitHub's licence API answers NOASSERTION/"Other" for this repo
because LICENSE opens with four lines of preamble before the GPL text. LICENSE deliberately untouched.

S7's mechanism is the EXISTING one, which was the point: a `<!-- table: quality-badges -->` region in
README, rendered by DocTableInjector, byte-compared by DocsAreSnippetCurrentTests' heal-or-fail. Eight
badges (5 coverage floors, 3 mutation raw scores) plus a generated note carrying each leg's break. The join
that earns its keep: the mutation raw score comes from the ledger and the break from the stryker config THE
LEDGER ROW NAMES, so a re-measure that forgets to move break renders that badge off-green - drift made
visible instead of a colour someone must remember to update. Colour derived in ONE function that IS
gate-checks.ps1's two band checks, with the truncation granularity passed in so the badge cannot reach a
different verdict from the check. Placed under ## Status, not the top block, because the maintainer had
just pruned that block. Renderer is test-project code (GeneratedDocsAreCurrentTests' precedent): DocTooling
is coverage-floored AND a mutation target, and the measured numbers must not move because the thing that
renders them was added.

S3 DID NOT LAND THE GATE THE ROW ASKED FOR, and that is the deliberate half. The row wanted a ~1.5x
wall-clock ratio gate; R4 forbids gating a nondeterministic oracle and H7 forbids gating a clock. Recorded
instead (benchmarks/results/2026-08-23-generator-compile-cost.md): 1,000 mappers in one compilation, three
runs, TIME TO FIRST EMIT 3,637/3,502/3,762 ms ~= 3.6 s per 1,000; parse 366/374/398 ms; 1,002 generated
files. GATED instead, and it is the better gate: editing ONE mapper re-extracts ONE and re-emits TWO files,
MEASURED IDENTICAL AT 40 AND AT 1,000 - the O(1)-per-keystroke property a wall-clock ratio only ever
observes indirectly.

THREE TRAPS S3 WALKED INTO AND OUT OF, all written into the results file because the next agent will walk
into them too:
  1. Remove+Add is not an edit - it appends, reordering every later tree, and all N recompute. That looks
     exactly like a regression and is an artefact of the modelling. ReplaceSyntaxTree; 1.
  2. "Did the step RUN" (anything but Cached) IS RED ON A HEALTHY TREE. FAWMN's transform takes a semantic
     model, so Roslyn re-executes it for every attributed class on ANY compilation change - the strict
     metric measures the host, not this generator, and reads N/N at baseline. The gate counts changed
     VALUES, the existing GeneratorCacheAssert idiom. A first sabotage attempt
     (.Combine(CompilationProvider) on the per-mapper step) is invisible to both metrics AND CORRECTLY SO -
     it changes nothing the baseline was not already doing. The real sabotage had to break value-equality:
     `object SabotageTag` on MapperClassModel, two facts red naming the counts, reverted.
  3. The fast tier's per-1,000 normalisation is a LIE worth 6x: 880 ms at 40 mappers normalises to 22.0 s
     per 1,000 against the 3.6 s actually measured at 1,000, because at N=40 the figure is nearly all JIT
     and warm-up. The test refuses to print it below N=500. A wrong number that is printed gets quoted.

FILED NOT FIXED: I21, maintainer-requested - emitted code is invisible to every analyzer in the solution.
All four facts re-verified at tip before filing (EmitCompilerGeneratedFiles/CompilerGeneratedFilesOutputPath
set NOWHERE; no .editorconfig or .globalconfig sets generated_code; MapEmitter.cs:14 writes
`// <auto-generated/>` first, which is what makes Roslyn's analyzer driver skip the output; so Meziantou,
CA at AnalysisMode=All and BannedApi see zero lines of what the product generates). It is the standing form
of what rounds 22-23 kept finding by hand (I5's CS1503, I14's CS0037, I17's CS0103) and a scoped answer to
B19. The row carries the mechanism note that matters most - emitting files to disk does NOT make analyzers
read them - and both risks (vacuity trap needs a sabotage demo; W7-scale triage flood, so correctness and
performance families only, never style).

MEASURED: whole-solution build 0 W / 0 E with samples (30.7 s); suite 7,925 -> 7,952 / 0 foreground (+27:
23 badge-renderer tests, 4 cost tests); census 866/866; scans 15/15; EmittedInvalidCodeCellCeiling exactly
0. S3's four facts also green at DWARF_DEEP=1 (1,000 mappers). No new allowlists; no pushes.

---

## Layer 4 — THE RECONCILIATION, discharged first. 2026-08-23.

The round-23 plan was written before round 22's W4–W7 landed and carries an explicit obligation saying so.
Discharged before any file was opened, by re-reading `Issues/round20/TASKS.md` and the merged tip rather than
working the plan's list. **W4, W5, W6 and W7 all landed; round 22 merged to master at `d0e5bca`.**

| Plan said | Actually | Evidence |
|---|---|---|
| V1 = B4, B5, B8, B10, B12, B13, B14, "whichever W5 did not land" | **all seven `DONE`** — V1 has an empty body | each row's closing note in `TASKS.md`, r22 W5 |
| V2 ⊃ C5 "W6's remainder — reconcile" | **`DONE`**, both remainders routed to the shared helpers | r22 W6 |
| V2 ⊃ B19 doc half "if W6 landed it, this row closes there" | **`DONE`** — W6 wrote the limitation into `SurfaceParityTests`' class doc | r22 W6 |
| V2 ⊃ F1 "flip with evidence" | **`DONE`** — merged `dc385d4` / `d131c76`, recorded | r22 W6 |
| V2 ⊃ F2 "capture the r22 log before that worktree is removed" | **`DONE`** — `Issues/ledgers/round22-sdd-ledger.md` at `52fdc26`; `git worktree list` shows only master and r23 | r22 W6 + `52fdc26` |
| V2 ⊃ B17 | **still `TODO`** — survives, its own commit | — |
| B23 "may already be done — reconcile; S7 blocked behind it" | **`DONE`** (`072c7ca`); S7 has since landed too (`1a7bf3e`) | r22 W4 |
| H3 "round-22 work, reconcile" | **`DONE`** (`7b2ee1d` + `d57ac03`) | r22 W7 |
| B28 "NEVER DISPOSITIONED → N3" | **`DONE`** this round (`9589e5a`) | r23 N3 |
| B37 → N6 | **`DONE`** this round (`1decd32`) — **and it unblocks B24** | r23 N6 |
| I4 / I5 / I6 / I7 → N5 / N1 / N4 / N2 | **all `DONE`** (`86f68f4`, `603047a`, `ff744ea`, `4b7caa4`) | r23 Layer 1 |
| V3 = B18, B24, B31 | **all three still `TODO`** — survives whole | — |

**V1 is closed with no work, and that is the reconciliation paying for itself.** Working the plan's list would
have re-derived seven guards that are already in the tree and produced conflicts against W5's own.

**The V2 sweep's contents are NOT the ones the plan predicted, and this is the second finding.** W6 already
fixed four of the six items the plan named (the `EmittedInvalidCode` 10, the Machines table's
`PredatesTheChangelog` and mutation-survivor rows, the F1 merge-handoff section, F1/F2's statuses). One
survived (the ratchet table still lists `PredatesTheChangelog` **76** — W6 corrected the *Machines* row and
not the table beside it). And a new layer accrued **that round 23 itself created**: the NOW block still names
`feat/round22-gates` / `DwarfMapper-r22`; the live-ceiling line and the ratchet table quote
`DivergenceFinding` **2** / `DivergentCell` **4** when I19 took them to **1** / **2**;
`DirectCompileErrorCallBaseline` **53** (live **55**) and `MapMethodModelBoolFlagBaseline` **15** (live
**16**); the two-remaining-findings sentence still names `NullCollections`@`Projection`, which I19 closed;
and **D-a is superseded** — it asked to KEEP that divergence and document the keeping, and I19 ruled the
other way and wrote the residual into `docs/options.md` instead. *A stale-status sweep goes stale. The lesson
is that it belongs at the END of a round, not in the plan written before it.*

**State-at-round-start re-verified rather than taken from the plan** (foreground, this worktree, `76acad2`):
whole solution **0 warnings / 0 errors** with samples (13.4 s); suite **7,952 / 0** — 6,848 Generator.Tests
+ 809 Integration + 89 NegativeCases + 52 CompilerTests + 52 Differential + 35 Testing.Tests + 24 CleanCorpus
+ 23 ConsumerTests.Host + 20 CorpusTests; census **866 / 866**; `EmittedInvalidCodeCellCeiling` exactly **0**.

---

## Layer 4 — V2's B17, I20, and V3. All DONE, 2026-08-23.

### V2 — B17 (`230099c`) plus the sweep (`2eb0520`)

**B17, reproduced before it was fixed.** `int N` → `string N` under `StringFormat = "N0"` emitted
`__DwarfMap_FmtStrF_int_60f7d740` (called) **and** `__DwarfMap_FmtToStr_int_05ecacc1` (called by nothing): the
format-aware converter REPLACES what the member's conversion resolved to, and the replaced one stayed in the
synthesized-helper table. No behaviour change — the method was dead — but it is generated code in a file the
consumer cannot edit, once per formatted member.

The remedy snapshots the synthesized table before the member's conversion resolves and drops **only the keys
that call added**. A helper an earlier member already needed is in the snapshot and survives; a later member
needing the same conversion re-adds it, because every synthesizer is add-if-absent and returns the name either
way.

**The control is the interesting half**, and both directions were sabotage-demoed. Disabling the removal reds
the orphan test naming B17; making the removal over-eager (snapshot cleared) reds the `before` control with a
real compile error while `after` stays green — which is the re-add route working as designed. Runtime evidence
per B19: a mixed mapper is executed and the plain members must neither vanish nor pick up the neighbour's
format.

**Sibling hunt.** `conv` is reassigned after a resolution at exactly ONE site in the pipeline, reached from all
three branches that honour `StringFormat`. A ten-shape orphan sweep — collections, dictionaries, enums,
enum↔string, nested, nested-in-collection, narrowing, parse, nullable lift, cross-category — found no other
unreferenced helper; the probe was deleted after it reported clean.

### I20 — two commits, and the row understated the defect

**`d26d1ea` — the `.Map` half, which I20 treated as the CORRECT endpoint.** Probing the nullable permutations
before touching the projection found **three separate mechanisms**, all measured against the unmodified tree:

1. `NumericConverter.IsCrossCategoryLossy` read `SpecialType` directly, so `Nullable<long>` classified as *not
   a numeric basic type* and `long? → double?` / `long → double?` passed in **silence at every severity** —
   while C# LIFTS the implicit conversion, so the direct-assign path took them and the precision loss shipped.
2. The unwrap-then-assign arm asked no lossiness question at all → `long? → double` silent.
3. **All three conversion recursions that cross a `Nullable<>` wrapper DROPPED `implicitConversions`**, so
   `long? → int?` and `string → int?` reported DWARF038 as a **Warning** under `ImplicitConversions = false`
   and the mapper was generated anyway. The strict setting was silently off for the whole nullable half of the
   type space. The collection-element and dictionary key/value recursions beside them always passed it.

Fixed in the shared classifier rather than at its call sites: it is shared by the class engine and the
`[MapTo]` registry precisely so the two cannot drift, and a guard outside it would recreate that drift.
**The whole suite was GREEN with all three defects live** — the corpus hole the row predicted.

**`a6938ac` — the projection half. Ruling: `Project` REPORTS, with the runtime endpoint's own emitter.**

*Cost if wrong:* the emission is byte-identical — a diagnostic and not one character of generated code — so no
working query can stop translating. The residual risk is a FALSE build break under strict, bounded by the
widening diagonals pinned silent at both endpoints and by the whole solution building 0 W / 0 E with samples.
Reversible in one commit.

*Rejected branch — DWARF028 plus I14's scoped kill.* `DWARF028` says *a query provider cannot translate this*,
which is false: `long → double` is a widening cast. Under the permissive default it would refuse a member
`.Map` happily maps — the capability regression I19 rejected as its branch (b). And scoping was rejected on
I14's own rule: a `DWARF028` describes THIS endpoint's translatability, every other error describes the SOURCE
MODEL and is equally true of the `.Map` methods over the same pair. A lossy type pair is the second kind, so
the whole-class kill is correct; **the absence of `DWARF096` is asserted, not assumed.**

**The fixture gap, and it paid on its first run.** `OptionContractTests` declared the row `NotApplicable` on an
excuse **both of whose halves were TRUE** and whose conclusion was false — the pair it never considered was
neither widening nor narrowing but cross-category. B3's live re-measurement could not see it because the
fixture carried no cross-category member. One member added to `SurfaceFixtures.NarrowingConversion`, and the
live check went **red on the first run**: *"the excuse is STALE … re-declare the row as Refused with the id
measured"*. Row → `Refused`/`DWARF038`, `NotApplicablePin` **6 → 5**, and the generated option-support matrix
re-rendered the Projection cell `n/a (loud)` → `DWARF038` from the measurement itself.

**No sampled-space exclusion existed to delete** — `grep -rn ImplicitConversions` over `CompilerTests`,
`DifferentialTests` and `CorpusTests` returns nothing — so no K0/K1 deep re-run was owed and no digest could
have moved. `MapToGenerator` emits no projection surface, so the rule-1 gap does not exist there.

### V3 — one policy, three rows, three commits

**The policy:** *a directive the generator reads and declines to act on is reported at its own site, with the
reason and the remedy; it is an **Error** only where no defensible behaviour exists to keep, otherwise the
documented behaviour is kept and the report is a **Warning**.* Under it the three rows take different
mechanics, which is what W1 did too (one policy → `DWARF095` + `DWARFR12`).

**B31 → `DWARF098`, Warning (`cb868be`).** The fallback construction is safe and documented, so the behaviour
is kept and only the report was missing. The message names the specific filter and the remedy for THAT filter.
*The probe caught a wrong remedy before it shipped*: the first draft told every inaccessible constructor to set
`AllowNonPublic`, and a `private` ctor is still filtered with that option set — the option reaches only as far
as the consumer's own assembly can see. Both accessibility cases are pinned in both directions.

**B24 → `DWARF099`, Error (`9d4371c`).** No defensible behaviour to keep: the two declarations have IDENTICAL
scope, so first-wins is an accidental rank. Measured before the fix — `(true,false)` generated 726 characters,
`(false,true)` 705, neither with a diagnostic. Identical duplicates stay accepted, pinned at emission *and*
executed at runtime, because the refusal itself has no runtime and the accepted case must still mean what one
declaration means. The A6 boundary (method-vs-pair stays most-specific-wins) is now a test of its own.
The row was unmeasurable until **N6** made the matrix's `×2` axis render two DIFFERENT applications.

**B18 → no new id; `DWARF088` reaches its third placement (`f453521`).** Nothing to keep or invent — a member
of the mapper class belongs to neither type of any mapped pair. *The row understated it*: EVERY form is inert
there, the legal class/method form included, which then left the completeness gate demanding the member the
caller believed they had excluded. The co-located fork was **verified, not assumed** (a directive on a real
`[GenerateMap]` host member is read and NOT reported — pinned, because *skipped on that path* and *never
reached on that path* look identical until one is wrong). The standing pin was inverted, which its own remarks
had asked for.

### Checked and CLEARED, not filed: the other two dropped arguments

The same three nullable recursions I20 fixed also drop `isPreserve` and `isSetNull`. Probed rather than
assumed — a class-inside-`Nullable<struct>` nested pair and a reference-source-into-`Nullable<U>` pair, each
under default / `ReferenceHandling = Preserve` / `OnCycle = SetNull`: **six cells, no generator diagnostic and
no CS error in any of them**, and the generated length differs per mode (1897 / 2131 / 2279), so the options
do reach emission by other paths. A struct cannot contain itself, so a cycle cannot route through a
`Nullable<T>` nested edge. Recorded here rather than carried silently; no I-row.

### Diagnostic ids allocated this round

`DWARF096` (I14), `DWARF097` (I17), **`DWARF098` (B31)**, **`DWARF099` (B24)**. The round-23 brief's "next free
DWARF097" was stale before Layer 4 opened. **Next free: `DWARF100` / `DWARFR13`.**

### Measurements at the Layer-4 tip (`f453521`), foreground

| | |
|---|---|
| Whole solution, samples included | **0 warnings / 0 errors** |
| Suite | **8,008 / 0** (from 7,952 at round start) |
| Census | **866 / 866** |
| `EmittedInvalidCodeCellCeiling` | exactly **0** |
| Scans (RatchetInvariantScan + GateBandLogic + DeepTierSelf) | **15 / 15** |
| `src/` analyzers | clean |
| New allowlists | none |
| Pushes | none |

Suite progression, each measured in its own commit: 7,952 → 7,956 (B17) → 7,968 (I20 `.Map`) → 7,981 (I20
projection) → 7,995 (B31) → 8,006 (B24) → 8,008 (B18).

**Process note, worth more than any one fix.** An intermediate `dotnet test --no-build` after building ONLY
the generator project reported the suite GREEN while B18's standing pin was live and failing, because the test
project still carried the previous generator dll. The whole-solution build is not only the over-eager-refusal
detector; without it, `--no-build` measures the wrong binary.

### Two claims closed at the final review, both by measurement rather than reasoning

**B24's "the matrix now poses this question directly" is MEASURED, not inferred.** Rendering the catalog's
`×2` case for `MapNullSkipAttribute<TSource, TTarget>` produces exactly

```
[MapNullSkip<Src, Dst>(true)]
[MapNullSkip<Src, Dst>(false)]
```

— a contradicting pair, because the type's single public constructor takes a `bool` and `SampleArgument`
rotates it (`variant == 1 ? "true" : "false"`, whose own comment names B24 as the reason). That cell now draws
`DWARF099`; the census held at **866 / 866** and no ceiling moved. Before N6 the two applications were
byte-identical and the cell asked nothing.

**`docs/options.md`'s `AllowNonPublic` row does NOT contradict DWARF098's message.** Checked because B31's
remedy turns on exactly this distinction: the row already reads *"`private`/`protected` are never usable (the
generated code could not compile)"*, which is the same rule the diagnostic now states at the call site. No
edit owed.
