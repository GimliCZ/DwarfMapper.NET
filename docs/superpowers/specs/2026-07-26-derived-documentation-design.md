<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Derived documentation — design

**Date:** 2026-07-26 · **Status:** approved, not yet implemented

## The problem

The repository already has a documentation machine, and it stops at exactly the wrong place.

`docs/generated/` derives three artefacts from the code and fails the build when the committed copy drifts:
`api-reference.md` (473 lines, from the compiled assembly plus its XML docs), `diagnostics-index.md`
(73 diagnostics, from the descriptor fields), and `option-support-matrix.md` (48 lines, every cell *measured*
by compiling the same source with and without the option). That machine is good. It covers the **reference**
layer — the lists a reader consults once they already know what they are looking for.

It does not cover the **example** layer, which is everything a reader meets first:

- **63 fenced blocks** across `README.md`, `docs/`, `docs/howto/` and the Gallery README. 32 are `csharp`.
  Not one of them is compiled. Every one is free to describe an API that no longer exists.
- The Gallery declares its 15 examples **three times**: the file itself, a hand-written `Example.Run()` line
  in `Program.cs`, and a hand-written row in `samples/DwarfMapper.Gallery/README.md`. No test touches the
  latter two.
- `docs/options.md` restates every option's type and default, and `docs/diagnostics.md` restates all 73 ids
  with their severities and titles — both facts the generated pages already derive. Two sources for one truth.

The asymmetry is stark: a renamed *option* surfaces as a doc diff in the same commit, while a renamed
*attribute* leaves 32 code fences quietly wrong. The existing scans (Scan7, Scan8, `ClaimMechanismScanTests`)
prove a doc section *exists*, states a fix, or names a live test. None can prove its **content** is still true.

This design extends the existing guarantee to the example layer, using the same heal-or-fail contract, and
then proves the extension can actually fail.

## Non-goals

- **No literate-programming rewrite.** Prose stays hand-written and hand-owned. Only the mechanical parts —
  code bodies, option/type/default columns, id/severity/title lines, the example index — are derived.
- **No second execution mechanism.** The pipeline runs under `dotnet test`, as it does today. A `dotnet tool`
  would mean two ways to regenerate docs, and one of them would rot.
- **No doc tooling in the shipped package.** `[DocExample]` lives in the samples, never in `DwarfMapper`.
  Documentation infrastructure must not enlarge the public API surface consumers take a dependency on.
- **Competitor code is not compiled.** The 14 `diff` fences in the migration guides contain AutoMapper /
  Mapster / Mapperly source. They stay hand-written, explicitly marked, with the reason recorded.

## Decisions

| # | Decision | Rejected alternative, and why |
|---|---|---|
| 1 | **The compiled sample is the truth; docs embed it.** | A generated cookbook page would leave the prose docs — the ones people actually read — still hand-written. |
| 2 | **Every compilable C# fence converts, with a ratchet.** | A front-door-only pass leaves the migration guides drifting, and migration guides are what convert users. |
| 3 | **Attribute for identity, comment markers for regions.** | Markers alone leave the example catalogue hand-maintained (nothing to reflect over). Attribute alone forces whole-type snippets, burying the 2 interesting lines in 12. |
| 4 | **One tiered corpus: grow the Gallery 15 → 26.** | Extracting from `Conformance` means renaming its `F07S`/`F07D` fixtures to be teachable — the work of growing the Gallery, minus the progression. A separate `Docs` project gives two answers to "where is the example for X". |
| 5 | **Inject facts into hand-written pages; fail on undocumented additions.** | Deleting the duplicate tables splits the reader across two pages, and the generated reference has no room for "why you'd reach for this". |
| 6 | **CsCheck 4.7.0 for the properties.** | This closes open item 4 of `docs/research/testing-conformance-REPORT.md` with the library that report chose. FsCheck 3.3.4 (BSD-3-Clause) needs separate shrinkers, and shrinking is the entire reason to add a PBT library here. Apache-2.0 sets no new precedent: xunit 2.9.3 is Apache-2.0 and already underpins the whole suite; test-only dependencies are not distributed, so GPLv2 combined-work rules do not reach them. |

## Architecture

### Project layout

A new **non-test** library holds the pipeline:

```
src/DwarfMapper.DocTooling/          (new, net10.0, IsPackable=false)
    ApiReferenceRenderer.cs          (moved from the test project, unchanged)
    SnippetScanner.cs                (new — the source-scanning half)
    ExampleCatalogue.cs              (new — the reflection half)
    DocSnippetInjector.cs            (new)
    DocTableInjector.cs              (new)
    GalleryIndexRenderer.cs          (new)
    OptionTableRenderer.cs           (phase 4)
    DiagnosticSectionRenderer.cs     (phase 4)
```

The two scanners are separate types rather than one `SnippetCatalogue`: they read different sources (files on
disk versus a loaded assembly) and fail for different reasons, and keeping them apart is what lets the
reconciliation tests play one off against the other.

The test project keeps only thin `[Fact]` shells plus the existing `AssertCurrent`. Three reasons this is a
library and not more test code:

1. **Stryker structurally cannot mutate a test project.** `stryker-config.json` names
   `"project": "DwarfMapper.Generator.csproj"` and treats `tests/DwarfMapper.Generator.Tests` as the
   *test-projects*. A renderer living there can never be mutated, so nothing would prove the doc tests kill
   a defect in the renderer itself.
2. `DocTooling` — not the test project — takes the reference on the Gallery assembly, keeping that dependency
   direction sane.
3. The renderers become independently testable units with no xunit coupling.

`IsPackable=false`: this is build-time infrastructure, never shipped. It inherits `LangVersion=latest` from
`Directory.Build.props` (C# 14.0 under SDK 10.0.110), so new code should use current idioms — collection
expressions, `field`, primary constructors — rather than the older shapes elsewhere in the repo.

### ① `[DocExample]` — the sample-side declaration

Lives in `samples/DwarfMapper.Gallery/DocExample.cs`. `Tier` is an enum so ordering and grouping are the
compiler's problem, not a string convention's.

```csharp
// <snippet: deep-paths>
[DwarfMapper]
public partial class OrderMapper
{
    [MapProperty("Customer.Address.City", nameof(OrderDto.City))]
    public partial OrderDto Map(Order order);
}
// </snippet>

[DocExample(6, Tier.Configuration, "Deep dotted paths",
            Shows = "reach a nested member without a lambda")]
public static class Example
{
    public static void Run() { /* … */ }
}
```

Note that the region wraps the **mapper**, while the attribute sits on the **runner**. A snippet region is
therefore *not* nested inside the attributed type — a mapping method must live in a `[DwarfMapper] partial
class`, so the two cannot be the same type. Association is by **ordinal → filename prefix**, using the
Gallery's existing `NN_Name.cs` convention: ordinal 6 binds to the file matching `06_*.cs`, searched
recursively so `ex15/15_CoLocated.cs` resolves. A test asserts exactly one file per ordinal and that every
ordinal is unique, because a silent collision here would bind an example's index entry to another example's
code.

### ② `SnippetCatalogue` — the scanner

Two independent reads that must reconcile:

- **Reflection** over the Gallery assembly → every `[DocExample]` type: ordinal, tier, title, `Shows`, and its
  `Run` method. This is the catalogue that drives the runner and the generated index. This is the
  assembly-scanning half.
- **Source scan** over `samples/**/*.cs` → every `// <snippet: id>` … `// </snippet>` region, dedented, marker
  lines stripped.

### ③ `DocSnippetInjector` — the writer

Walks the doc files and replaces the body of two marker kinds:

- `<!-- snippet: id -->` … `<!-- endsnippet -->` — a fenced code block from a sample region.
- `<!-- table: name -->` … `<!-- endtable -->` — a table whose mechanical columns come from reflection and
  whose prose column is **carried over from the committed file, keyed by the row's first cell** (the option
  name, or the diagnostic id). Keying on position instead would re-associate every prose cell the moment a
  row is inserted, silently attributing one option's description to another.

It reuses `AssertCurrent` verbatim: write the corrected file into the working tree, then **fail**. That
contract is already documented in `GeneratedDocsAreCurrentTests` and the reasoning stands unchanged — a
silently-healing doc test goes green in CI while the file people read stays stale.

Prose carry-over is the mechanism behind decision 5. When reflection reports a row the committed file has no
prose for, the row renders with an empty cell and the test fails naming it. A newly added option or diagnostic
therefore cannot ship undocumented — the same trick the library plays on unmapped members, aimed at the docs.

### ④ The ratchet — `DocFenceScanTests`

Every ```` ```csharp ```` fence in a scanned doc must be either inside a snippet region, or immediately
preceded by `<!-- fence-exempt: reason -->`. A new hand-written C# fence fails the build. `diff`, `bash`,
`xml` and `ini` fences are out of scope by language, not by exemption — no marker needed.

### The reconciliation contract

Three rules, each naming a distinct failure:

| Rule | Prevents |
|---|---|
| Every doc marker resolves to **exactly one** region | a duplicate id silently rendering whichever was found first |
| Every region is referenced by **≥1 doc** | a snippet maintained forever that no reader ever sees |
| Every `[DocExample]` owns **≥1 region** | an example that runs in the Gallery but is invisible to the docs |

Rules 1 and 2 apply to all of `samples/**`; rule 3 only to the Gallery. That split is deliberate: it lets
`docs/howto/deploy-and-optimize.md` quote `samples/DwarfMapper.AotSample` — code the CI gate proves
NativeAOT-publishes with zero IL2xxx/IL3xxx warnings — instead of hand-written AOT advice, while keeping
exactly one *teaching* corpus.

## The sample corpus

Gallery goes 15 → 26 files. `Tier` groups the generated index so it reads as a path rather than a 26-row wall.

| Tier | # | Content |
|---|---|---|
| Basics | 01–05 | existing: flat, rename, built-in conversions, nested, collections |
| Configuration | 06–14 | existing: deep paths, flatten, `Use=`, `When=`/`[MapValue]`, record target, projection, ergonomics/DI, nested-list config ×2 |
| Front doors | 15–16 | 15 co-located *(exists)* · **16 `[MapTo]` registry** |
| Advanced | 17–24 | **17 update-into · 18 span map · 19 async stream · 20 reference handling & cycles · 21 blittable/SIMD · 22 `[Reinterpret]` · 23 `[FlattenGraph]` · 24 `[MapDerivedType]`** |
| Testing | 25–26 | **25 `[RoundTrip]` · 26 informed dumps** |

Eleven new files, one per README topic that currently has no sample to be extracted from. Tier Testing needs a
`DwarfMapper.Testing` project reference in the Gallery — acceptable: it is a sample executable, not an AOT
target, and `DwarfMapper.Testing` is documented as reflection-based and test-only.

`Program.cs` loses its 15 hand-written `Example.Run()` lines and becomes a loop over the reflected catalogue,
ordered by tier then ordinal. The Gallery README table is generated from the same catalogue.

## Documentation inventory

| File | C# fences | Treatment |
|---|---|---|
| `README.md` | 15 | snippet-backed |
| `docs/diagnostics.md` | 5 | snippet-backed + **injected** id/severity/title per section (77 sections, 73 ids) |
| `docs/options.md` | 0 | **injected**: 3 tables — class options, assembly options, strategy enums |
| `docs/howto/common-changes.md` | 5 | snippet-backed |
| `docs/howto/migrate-from-automapper.md` | 4 | snippet-backed; its `diff` fences exempt |
| `docs/howto/ambient-cross-assembly-maps.md` | 1 | snippet-backed |
| `docs/howto/migrate-from-handwritten.md` | 1 | snippet-backed |
| `docs/howto/migrate-from-mapster.md` | 1 | snippet-backed |
| `docs/howto/migrate-from-mapperly.md` | 0 | `diff` fences exempt |
| `docs/howto/deploy-and-optimize.md` | 0 | snippets from `AotSample` where it currently gives advice in prose |
| `samples/DwarfMapper.Gallery/README.md` | 0 | **injected** index table from the catalogue — *not* fully generated: its declaration-style comparison and "lambda note" are hand-written prose worth keeping |
| `CONTRIBUTING.md` | — | ground rules gain a fourth: user-facing behaviour needs a doc snippet, not only the two tests |

`docs/COMPARISON.md`, `docs/CORRECTNESS.md`, `docs/MIGRATION.md`, `docs/RELEASING.md`, `docs/SECURITY.md` and
`docs/IMPROVEMENT-PLAN.md` are untouched: they carry no C# fences that describe the current API.

## Proving the machine can fail

A doc test that cannot fail is the same lie as stale prose. Three independent mechanisms:

### Stryker — breadth over the renderers

A second config, `stryker-config.doctooling.json`, mutating `src/DwarfMapper.DocTooling` with the doc tests as
its test-projects. Stryker takes a single `project` per config, so this is a second file and a second
invocation from `scripts/housekeeping.ps1 -Mutation`, not an extra entry in the existing config. Thresholds
start at the existing `high 90 / low 80 / break 70` and are baselined on first run.

### The hand-rolled battery — named defects

`scripts/mutation-battery.sh` gains `M29+`. The precedent is already there twice over: M26 and M27 name
`GeneratedDocsAreCurrentTests` as their guard, and **M21 already mutates `docs/diagnostics.md` itself**
(downgrading an ERROR's `**Fix:**` to optional advice, guarded by Scan8). Doc mutants are established practice
in this repo, not a new idea.

New mutants, each a defect this design could plausibly ship:

| id | Mutation | Should be killed by |
|---|---|---|
| M29 | dedent drops a tab instead of preserving relative indentation | a CsCheck dedent property |
| M30 | duplicate snippet id renders the first match instead of failing | reconciliation rule 1 |
| M31 | prose carry-over falls back to the previous row's text instead of empty | the options-table currency test |
| M32 | the fence ratchet's exemption check accepts any HTML comment, not `fence-exempt` | `DocFenceScanTests` |
| M33 | `[DocExample]` reflection filter drops non-public types, silently shrinking the catalogue | reconciliation rule 3 |

The battery already restores `docs/` after every mutant. This design makes that restore materially more
load-bearing — under a doc mutant, far more tracked files are now rewritten — so its own guard
(`git checkout -- docs/`) is retained and re-verified.

### CsCheck — properties with shrinking

`CsCheck` `4.7.0` (Apache-2.0, `lib/net8.0`) added to `Directory.Packages.props`, referenced by the test
project only. Four properties, chosen because a text transformer is a far better PBT target than the generator
ever was:

| Property | What shrinking buys |
|---|---|
| `inject(inject(doc)) == inject(doc)` | idempotence; a failure shrinks to the one marker shape that re-expands |
| `extract(render(body)) == body`, arbitrary bodies | a 40-line random failure shrinks to "a body whose line starts with ` ``` `" |
| malformed markers yield a diagnostic, never a partial write or a lost file | shrinks to the minimal bad marker sequence |
| dedent preserves relative indentation | shrinks to the minimal tab/space mix |

Property 3 matters most: the injector writes into tracked files. A malformed marker that truncates a doc would
be a data-loss bug in the documentation pipeline, and the failure mode is silent.

## Phases

Each phase leaves the build green and the suite passing.

1. **Extract `DocTooling`.** New library; move `ApiReferenceRenderer`; test project keeps the `[Fact]` shells.
   Pure refactor — the three generated docs must be byte-identical afterwards, which is itself the test.
2. **Snippet machinery.** `[DocExample]`, `SnippetCatalogue`, `DocSnippetInjector`, the three reconciliation
   rules, `DocFenceScanTests`. Retrofit the 15 existing Gallery examples with attributes and regions; generate
   `Program.cs`'s loop and the Gallery README. Convert the README's 15 fences and the howtos' 12.
3. **Grow the corpus.** The 11 new Gallery examples, each with regions, each referenced by the doc section it
   illustrates. This is the phase that lets the previously-unbacked prose become snippet-backed.
4. **Injected tables.** `OptionTableRenderer`, `DiagnosticSectionRenderer`, prose carry-over, and the
   fail-on-undocumented-addition test. Rewrite `docs/options.md` and `docs/diagnostics.md` around the markers,
   including that file's remaining 5 C# fences — they are converted here rather than in phase 2 so
   `diagnostics.md` is opened once, not twice. All 32 C# fences are then accounted for: 15 README + 12 howto
   (phase 2) + 5 diagnostics (phase 4).
5. **Prove it.** CsCheck properties, `stryker-config.doctooling.json`, battery mutants M29–M33, `CONTRIBUTING.md`
   ground rule.

**Phase 6 — droppable.** Several README fences show *what the generator emits*. Those could be backed by the
generator's real output, using the driver harness and Verify snapshots that already exist, making
"you write X → it emits Y" a derived claim. It is the most persuasive content a source generator's docs can
carry, but it is a distinct mechanism from snippet extraction, and dropping it weakens no gate defined above.

## Risks

| Risk | Mitigation |
|---|---|
| The test project referencing a sample project is a new dependency direction | `DocTooling` holds it, not the tests. If the Gallery ever fails to build, the doc tests fail loudly rather than silently skipping. |
| Snippet regions clutter the sample files they teach from | Regions are comments; the Gallery's `AnalysisMode=Recommended` already relaxes analyzer hygiene for readability. Region granularity is chosen per example, not mandated per member. |
| 26 examples dilute the Gallery's "simplest first" promise | `Tier` grouping in the generated index; the progression is preserved *within* tiers. |
| The ratchet blocks a legitimate quick doc edit | `<!-- fence-exempt: reason -->` is one line and self-documenting. The reason is required, so an exemption is a recorded decision rather than a silent bypass. |
| Phase 3 writes 11 new examples against features the author knows least well | Each new example must run in `Program.cs` and assert its own output, so a wrong example fails the Gallery run rather than teaching a wrong thing. |

## Verification

The design is implemented when all of the following hold:

- `dotnet build DwarfMapper.NET.sln -c Release` is warning-clean (`TreatWarningsAsErrors` is on repo-wide).
- `dotnet test DwarfMapper.NET.sln -c Release` passes, including the new reconciliation, ratchet, injected-table
  and CsCheck tests.
- `dotnet run --project samples/DwarfMapper.Gallery` runs all 26 examples in tier order with no hand-written
  call list.
- Deleting a Gallery example's file fails the build via a doc marker that no longer resolves.
- Renaming an option fails `docs/options.md`'s currency test in the same commit.
- Adding a diagnostic descriptor with no prose section fails the diagnostics-table test.
- `scripts/mutation-battery.sh` reports 0 survivors and 0 stale across all mutants including M29–M33.
- `git status` is clean after a full test run — the heal-or-fail tests either pass or fail, and never leave a
  rewritten tracked file behind as a side effect of a green run.
