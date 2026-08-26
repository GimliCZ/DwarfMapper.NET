<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 27 — architecture & housekeeping: the plan, re-grounded

**Status: proposed, awaiting scope approval. Nothing here has been executed.**

This plan supersedes the numbers in `Issues/round26/ROUND26-ARCH-MAINTAINABILITY-RFC.md`. The RFC's analysis
holds; **its measurements do not.** It was written at `fac40f3`, before rounds 25 and 26 landed 31 commits.
Every grounding figure has moved, one of them enough to make the RFC's own proposed test fail on arrival.

Re-measured at `1c300f3` (rounds 25+26 merged; suite 8,196 green; mutation 95.85 / 97.48 / 84.32, all within
band).

---

## 1. Grounding, re-measured

| fact | RFC `fac40f3` | now `1c300f3` | delta |
|---|---:|---:|---|
| `ExtractCore` body | 2,751 | **3,556** | **+805 (+29 %)** |
| `ExtractCore` cyclomatic complexity | — | **408** | new measurement |
| `ExtractCore` cognitive complexity | — | **865** | new measurement |
| `MapperExtractor.cs` | 3,939 | 4,901 | +962 |
| partial family total | — | 14,296 across 11 files | — |
| `…Projection.cs` | 1,487 | 2,089 | +602 |
| `…Conversions.cs` | 1,305 | 1,803 | +498 |
| `…Flatten.cs` | 1,567 | 1,808 | +241 |
| `DiagnosticDescriptors.cs` | 1,537 / 90+ | 1,746 / **96** | +209 / +6 |
| methods with ≥7 parameters | 30 | **40** | +10 |
| worst signature | 16 (`ResolveProjectionCtorExpr`) | **36 (`ResolveMembers`)** | +20 |
| `w.Line` calls / raw-string emitters | 149 / **0** | 166 / **0** | the choice holds |
| registry mirrored dictionaries | 4 | 4 | unchanged |

**The growth law is confirmed and accelerating.** The RFC recorded +514 lines on `ExtractCore` between two
audit intervals and called that its entire justification. This interval added **+805**. Two rounds of
performance work, none of it aimed at this method, still deposited a quarter of its current size into it.

**Method-count caveat.** The 30 → 40 figure is like-for-like (methods only). A first crude regex reported 47
by sweeping model-record constructors into the same population; that count is wrong and is not used here. All
headline figures in this document come from Roslyn (`roslyn-lens`), not text matching.

### Beyond `ExtractCore`: three more methods in the same size class

| method | logical LOC | cyclomatic |
|---|---:|---:|
| `ResolveFlattenGraphDirectives` | 852 | 130 |
| `ResolveMembers` | 665 | 149 |
| `TryResolveConversion` | 623 | 148 |
| `ResolveProjectionMembers` | 353 | 72 |

The RFC scoped decomposition to `ExtractCore` alone. These are named here so their exclusion is a decision
rather than an omission — see §5.

---

## 2. Four corrections to the RFC

**C1 — every ceiling in the RFC is stale, and one is already red.** Its `StructureRatchetTests` pins
`ExtractCore ≤ 2,800` "(today: 2,751)". Today is 3,556: landing that code verbatim fails on arrival. Per the
repo's standing rule, **no ceiling is written except at a value measured in the same commit that introduces
it.**

**C2 — `RefactorLockTests` is largely already built.** The RFC proposes a corpus-wide SHA256 manifest as new
code. `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` already holds **973 cases**, one line per
case, SHA256 over generated source *plus full diagnostics*, with a no-auto-bless guard (`DWARF_GOLDEN_UPDATE`,
and `GoldenManifest.Load` refuses to self-create). The harness exists; what is missing is proof it *reaches*
what we intend to move — see C3. This makes the decomposition cheaper than the RFC costed it.

**C3 — the byte-identity lock is a claim until seam reach is proven.** A 973-case manifest locks whatever the
corpus generates. A seam no case reaches is **not** locked: moving it can change bytes only for inputs outside
the corpus while the manifest stays green. `GoldenFeatureCoverageTests` proves each *feature* case fires its
feature; nothing yet maps the **29 seams** to corpus reach. Coverage verification is therefore promoted to
**step 0**, ahead of any code motion.

**C4 — model records must be excluded from the context collapse.** `MapMethodModel`, `MemberMap` and
`MapperClassModel` are incremental-pipeline records whose positional shape and structural equality **are the
cache semantics** — the reason `EquatableArray<T>` exists in this codebase at all. Collapsing their parameters
into a context object is a different risk class from tidying a resolver signature: it can break incremental
caching silently, with no test failing. R27-02 covers **methods only**.

---

## 3. New findings

### [N1] REG-02 was never implemented, and the hazard it targeted is live — *priority item*

The RFC states "REG-02's optional-param ban stays." **It does not exist.** No test in the repository inspects
parameter defaults (`HasExplicitDefaultValue` / `EqualsValueClause` / `IsOptional` appear nowhere under
`tests/`). REG-02 is a proposal in `Issues/round19/REGRESSION-ARCHITECTURE-TESTS-RFC.md`, never built.

That matters because REG-02 names the exact vocabulary behind ISSUE-043 and ISSUE-044 — *"optional parameter
defaults to the permissive value; one call site forgets it"* — and on `ResolveMembers` today:

| parameter REG-02 named | state |
|---|---|
| `caseInsensitive` | **required** — the one ISSUE-044 actually fixed |
| `autoNest` | optional, defaults `false` |
| `allowNonPublic` | optional, defaults `false` |
| `explicitOnly` | optional, defaults `false` |
| `ignoreObsolete` | optional, defaults `false` |
| `implicitConversions` | optional, defaults `true` |

**21 of `ResolveMembers`' 36 parameters carry defaults.** The fix that would have made this class of bug
impossible was applied to one parameter; the guard that would have generalised it was written down and not
built. That is the same shape as the audit lesson *"a fix applied to 1 of N identical construction sites."*

R27-00 lands the ban **first**: it is cheap, it closes a live hazard, and every later signature migration must
not silently reintroduce a default.

### [N2] The 29 seams are real, named, and unevenly sized

Verified by walking the method body: 29 box-heading comments, spans from 2 to 355 lines. Several are `End X`
pair-markers (`End Plan 21`, `End Fix 1`, `End MF-A fix`, `End MF-B fix`), so **distinct phases number ~20**,
not 29 — use 20 for effort estimates. The RFC's mechanic ("the comments become the method names") is sound.

### [N3] Emission style needs no work — the RFC's one withdrawal still holds

166 `w.Line` calls, **zero** raw-string emitters. Re-confirmed at `1c300f3`: the codebase made this choice
cleanly and it has not drifted under two rounds of emission changes.

### [N4] Registry drift risk is unchanged

Still four hand-mirrored `ConcurrentDictionary` tables (`Maps` / `Ambiguous` / `UpdateMaps` /
`UpdateAmbiguous` at `DwarfMapperRegistry.cs:22,23,57,71`). The asymmetry that shipped once has not recurred,
but nothing structurally prevents it.

---

## 4. The plan

Each entry states its **red-when** — what turns the guard red — because a guard nobody can make fail is the
failure mode this repository keeps finding.

### [R27-00] Land REG-02: the optional-parameter ban — *first*

Roslyn scan over `Pipeline/`: a resolver may not declare an optional parameter whose name is in the option
vocabulary (`autoNest`, `allowNonPublic`, `explicitOnly`, `ignoreObsolete`, `caseInsensitive`,
`implicitConversions`, …). Existing offenders enter a **shrink-only allowlist** pinned at today's count; the
allowlist empties as R27-02 migrates each site.

*Red-when:* a new optional option-parameter appears, or an allowlisted row regrows after migration.
*Cost:* small. *Closes:* a live ISSUE-043/044 hazard on the hottest resolver.

### [R27-01] Step 0 — prove seam reach before moving anything

Map the ~20 distinct phases of `ExtractCore` against the 973-case golden corpus: instrument a run to record
which phases each case reaches, and assert **every phase is exercised by ≥1 case**. Gaps become corpus
additions that land *ahead* of any extraction.

*Red-when:* a phase has no covering case — i.e. the byte-identity lock does not actually cover it.
This is the entry that converts the RFC's safety argument from claim to proof.

### [R27-02] `ExtractionContext` — collapse the signatures (methods only)

`internal sealed record ExtractionContext(Compilation, OptionSet, MapperDefaults)`, one construction site per
mapper extraction, **no defaults anywhere** — keeping the totality that made required-params the right fix
while removing the parameter bloat that fix cost. Positional bools become named `OptionSet` properties, so the
transposition hazard dies with the parameter lists.

**Model records are OUT OF SCOPE** — see C4.
*Red-when:* a resolver gains a parameter instead of a context field (the R27-00 scan, ratcheted down).

### [R27-03] Decompose `ExtractCore` along its own seams

One phase per commit, each proven byte-identical against the 973-case manifest. No logic edit in the same
commit as a move. Phase methods take/return the R27-02 context, so extraction and de-parameterisation land as
one motion. A structure ratchet pins each phase at its **measured** post-split length.

*Red-when:* any emitted byte moves during a structural commit; any phase grows past its measured row.

### [R27-04] `RegistryTable<TDelegate>` — one implementation, two instances

As the RFC wrote it: extract `Register/TryGet/IsProvided/IsAmbiguous` plus the base/interface walk into a
generic; public surface stays byte-compatible via thin forwarders. The round-13 torture suite becomes
**table-generic**, so a future table inherits its four invariants on arrival.

*Red-when:* read and update tables diverge in any operation's semantics.

### [R27-05] Split `DiagnosticDescriptors.cs` by category with owned id ranges

1,746 lines / 96 descriptors → partial family, each declaring a disjoint id range in a header comment. Id
allocation stops being tribal knowledge. Ids and wordings do not change, so the existing sync pins
(AnalyzerReleases, docs, wording) stay green by construction.

*Red-when:* a descriptor lands outside its file's declared range, or ranges overlap.

### [R27-06] `ConversionPolicy` — one declarative table, three consumers

Deferred to its natural trigger (when the R25 conversion rows land). Unchanged from the RFC.

### [R27-07] Repo hygiene — the "entire solution" part

- `CLAUDE.md` carries two open decisions dated 2026-07-26. The file's own header says to delete each item once
  decided, and warns that a stale copy "has become the kind of stale prose the rest of this repository is
  built to prevent." Decide both, then delete them.
- The round-20 plan file under `.claude/plans/` describes work long since landed.
- Two known harness bugs, filed but unfixed: the AOT stale-binary guard false positive, and the
  reproducible-build script not replicating CI's job.

---

## 5. Recommended scope, and the decision left open

**Recommended for this round:** R27-00, R27-01, R27-02, R27-03, R27-05, R27-07.

R27-00 closes a live hazard for very little work. R27-01 must precede any motion. R27-02 → R27-03 are the
round's substance, and the measured growth makes them urgent. R27-05 and R27-07 are independent and cheap.
R27-04 is genuinely valuable but the drift it guards has not recurred; R27-06 has an explicit trigger that has
not fired.

**The open decision is the three other giants** — `ResolveFlattenGraphDirectives` (852 LLOC), `ResolveMembers`
(665), `TryResolveConversion` (623). Recommendation: **file them for round 28** rather than absorb them.
R27-03 alone is ~20 commits, and the seam-comment mechanic that makes `ExtractCore` mechanically splittable
does not obviously exist in the other three — each needs its own seam analysis first. Taking them on now risks
a round that lands half-finished, which is worse for a refactor round than a smaller one that completes.

---

## 6. Verification protocol — non-negotiable for every commit

1. **Whole solution** builds (`samples/` included), 0 errors / 0 warnings.
2. Full suite green (8,196 at branch point).
3. **Byte-identity:** the 973-case golden manifest unchanged. A structural commit that moves one byte is a
   defect, not a surprise.
4. No ceiling or floor is written except at a value measured in the same commit.
5. No logic edit shares a commit with a move.
