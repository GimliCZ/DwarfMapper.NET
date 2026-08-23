# DwarfMapper.NET — Round 16 forensic audit, as a task list

Source: every-file forensic read on `84e13e7` (1.0.2-rc.1) — never-read perimeter (DocTooling, benchmarks,
samples/Conformance, scripts, workflows, full 313-file/51k-line test corpus) plus new instruments over already-read
src (emitted-literal `global::` sweep, suppression audit, line-ending policy, assertion forensics).

---

## Round 17 — executing the list, as its own instrument

Running a finding is a stronger instrument than reading one, and it corrected this document eight times. Work
landed on `master` as `caf279a`, `6128558`, `99bc8ee`, `7378851`, `f3e2670`, `b207443`, `23594e6`, `d2fc4b0`,
`e569665`, `723a50b`, `e4dd185`; each item entry below carries its status.

**What execution corrected in this document:**

1. **The baseline was never green here. [MEASURED]** ISSUE-048 was filed as a hazard for "a contributor with
   `core.autocrlf=true`". That contributor is the maintainer: this machine has `core.autocrlf=true` in
   `~/.gitconfig`, 641 tracked files were checked out `i/lf w/crlf`, and the two named `DocSnippetInjectorTests`
   were **already red at the pristine tip before any edit**. The prior round's Linux simulation predicted the
   right two tests for the right reason. Renormalizing the tree took the suite from 5288/5290 to 5290/5290,
   with **zero** git-visible change — the index was always LF; only the working tree disagreed.
2. **DWARFR is nine ids, not seven** (`DWARFR01`–`R09`), and the rows belong in **Unshipped.md** —
   `Shipped.md` is intentionally empty until the first stable release and that invariant was left intact.
   The suppression was **not** a prototype-tier carve-out: deleting one row fails the build with
   `error RS2000` at `RegistryDiagnostics.cs(44,9)`, so it was masking a live warnings-as-errors rule.
3. **ISSUE-044's site list enumerated 19 of its own claimed 20.** The compiler found `Flatten.cs:233` —
   a *partial* omitter that passes `compilation` and drops only `allowNonPublic`, invisible to a scan asking
   "which calls omit both arguments?"
4. **ISSUE-044 had a twenty-first site the instrument could not see at all.**
   `ConstructorSelector.cs:187` calls `MemberFacts` **directly** rather than through the
   `ReadableMembers`/`WritableMembers` wrappers the scan enumerated. Same defect, different caller shape:
   a constructor fed by an internal source member scored unsatisfiable, so overload selection preferred a
   narrower constructor. *Instrument-power statement: a scan over the wrappers has no power over direct
   callers of what the wrappers wrap.*
5. **The ISSUE-038 fix as written would have broken this machine.** The ledger said pin `"10.0.110"`; only
   `10.0.101` and `9.0.308` are installed here. Pinned `10.0.101` with `rollForward: disable` instead, and
   CI now installs exactly that and asserts the match.

**What execution found that no round had filed — and it is the important one:**

> **Threading the flag everywhere was wrong.** The obvious reading of ISSUE-044 — "the option should reach
> every member lookup" — is false for projection. A projection becomes an expression tree that a query
> provider translates, and it **cannot** read a non-public member; the resolver deliberately enumerates
> public members only and reports DWARF028 naming the real reason. The first version of the fix threaded
> `allowNonPublic` in there. It compiled, the whole solution built, and it broke the contract —
> `OptionContractTests` and `ProjectionRuntimeParityTests` caught it with the message *"A projection that
> accepts the option and quietly ignores it produces silently wrong data."* This is the repo's own
> "fix caused the bug it prevented" pattern, live. The decision is now a named constant carrying the
> rationale, plus a test pinning the exception next to the rule so it is not "tidied up" later.

**Instrument failures disclosed this round:**

- `dotnet test … | tail` reports **tail's** exit code. The first baseline run was recorded as exit 0 while
  two tests were failing. Every later run captured `$?` from the unpiped command.
- A regex over `MapperExtractor\.[A-Za-z]+\.cs` silently excluded `MapperExtractor.cs` (no middle segment),
  hiding one of the twenty sites until the pattern was widened.
- `grep -oP` fails outright under some locales ("`-P` supports only unibyte and UTF-8 locales"). It broke
  twice locally, which is why the CI drift check ships as `sed`.
- Two `perl -0pi -e` in-place edits silently mangled a source file (one deleted an identifier from a
  parameter list). Caught by the build, restored from a backup taken first — but a scripted edit that
  *half*-applies is a worse failure mode than one that errors.

**All thirteen items are now run.** Three more corrections came out of the last three:

6. **The ledger's "two-sided CI leg (5.0.0 floor + 5.6.0 latest)" is not buildable as written.** A generator
   referencing Roslyn 5.6.0 cannot load in a 5.0.0 host — `CS9057`, measured. "Latest" is therefore a second
   *SDK*, not a second package version, and the leg had to be rewritten as a job that installs a newer 10.0.x
   SDK and drops the pin for its own working copy. It ships `continue-on-error` until its first green run,
   because it was authored without a machine that has a newer SDK; the comment says to flip it.
7. **ISSUE-046's candidate 3 rests on a false premise.** "A query provider cannot express the AsEmpty guard"
   is wrong: it translates, to *identical SQL* to AsNull. The real finding is about shape — `??` on the
   navigation is the one form EF refuses, and `??` on the projected list translates but is **vacuous**
   (byte-identical SQL), which would look like the option was honoured while changing nothing.
8. **The `Dict` Windows figure was not an outlier.** It reproduces to within 1.2% seventeen days later
   (2.13×). The headline is *qualified* — name the platform — not withdrawn. CLAUDE.md decision #3 is closed.

**Still not done, and not automatable:** the in-editor smoke check for the Roslyn bump (DWARF001 squiggle +
code fix in VS 2026 / VS Code). That one needs a human with an IDE.

**Two instrument failures from the last three items**, in the same spirit as the list above:

- The EF probe's first version had only a control in the final `Select`. EF Core permits *client* evaluation
  there, so the control passed and the probe concluded that even untranslatable code translates. A control
  in a `WHERE` — which EF must translate — is what gave it power. "It ran without throwing" is not evidence.
- The torture test's first two versions had no power at all, one of them because the registered lambda closed
  only over an outer local and the compiler hoisted it, so every thread passed the *same* delegate. Both
  broken registries passed it. Measured power is now recorded in the file itself.

---

## New fixes from this round

- [x] **DONE (`6128558`) — ISSUE-047 (Low/Medium) — track shipped DWARFR diagnostics in AnalyzerReleases.**
      `Registry/RegistryDiagnostics.cs:15-16` suppresses RS2000/RS2001, so shipped ids ~~DWARFR01–07~~
      **DWARFR01–R09 (nine, per round 17)** appear in neither `AnalyzerReleases.Shipped.md` nor `.Unshipped.md`,
      while the suite *enforces* that sync for DWARF ids (`AssemblyScanTests`).
      **As fixed:** nine rows added to **`.Unshipped.md`** (`Shipped.md` stays intentionally empty pre-stable),
      suppressions deleted, class doc comment corrected, and `Scan1f/1g/1h/1i` + non-vacuity `Scan1j` added with
      `ParseAnalyzerReleases` parameterized by id pattern so both families read one table through one parser.
      The "document the prototype carve-out instead" option was **ruled out by measurement**: removing one row
      fails the build with `error RS2000`, so the suppression masked a live rule.

- [x] **DONE (`caf279a`) — ISSUE-048 (Low) — add `.gitattributes`; the repo has none. [MEASURED]**
      Raw-string literals adopt checkout line endings, so a contributor with `core.autocrlf=true` gets CRLF
      expected-values vs LF-producing code. **Demonstrated by paired experiment** (same commit `84e13e7`,
      SDK 10.0.110): a `--config core.autocrlf=true` clone (CRLF terminators confirmed via `file(1)`) fails
      **2 of 9** `DocSnippetInjectorTests` — `Fills_an_empty_marker_pair_with_a_fenced_block` and
      `Preserves_the_markers_indentation_so_a_fence_inside_a_list_item_stays_in_it` — while the LF control
      clone passes 9/9. (Corrects the earlier reasoned claim of "3 sites": measured exposure is 2 tests.)
      Fix: commit `*.cs text eol=lf` / `*.md text eol=lf` (plus `* text=auto` and binary excludes); the CRLF-clone
      run above is the regression check. Optionally note the policy in CONTRIBUTING.
      **Round 17, on Windows:** not hypothetical — those same 2 tests were red on the maintainer's own tree at
      the pristine tip. Policy committed (including `*.txt`, which covers the 85 Verify snapshots the audit did
      not call out), tree renormalized to 658 `i/lf w/lf` with zero git-visible change, suite 5290/5290.

## Optional polish (below issue threshold)

- [ ] (still open, cosmetic) Gate the docs-regenerator writes (`GeneratedDocsAreCurrentTests.cs:118`,
      `DocsAreSnippetCurrentTests.cs:71`) behind not-CI for workspace purity. **Not** a self-heal hazard — both
      write-then-`Assert.Fail` with a recorded rationale, so CI cannot self-bless; the write merely dirties a
      throwaway CI workspace. Cosmetic.

## Verified sound this round — no action, kept for the record

- [x] **Emitted-code `global::` hygiene**: 75 coarse hits all triaged benign (metadata names, hint names,
      comments); refined scan of emitted-statement literals = **0** unqualified type refs. The classic
      generator-collision bug class is absent.
- [x] **Conformance gate is real and fail-closed**: `ci.yml:156` runs `scripts/conformance-gate.sh` and fails on
      artifact *absence* too; the AOT job behaviourally **executes** the published native binary (`ci.yml:136-150`).
      (My first grep truncated at `head -8` and nearly filed this as "gate never invoked" — the near-miss is
      disclosed here as a fragment-hygiene reminder.)
- [x] **Workflows**: least-privilege `permissions:` blocks, every action SHA-pinned, no `pull_request_target`.
- [x] **Benchmarks**: `MemoryDiagnoser`, hand-mapped `Baseline = true`, returned-value sinks (BDN-idiomatic),
      reflection confined to `[GlobalSetup]` and documented.
- [x] **Suppressions**: 3 total in src; CA1508 is a documented false positive; the RS2000/2001 pair *is*
      ISSUE-047 above.
- [x] **Test-corpus assertion forensics**: `[Fact(Skip=…)]` = 0, empty catch = 0 across 51,124 lines; all 52
      zero-assert candidates resolved to house helper vocabulary (`Col.*`, `R.Check`, `NoErrors`,
      `Expect*/Has*` families) — **zero genuinely assertion-free tests**.
- [x] **Docs self-heal review**: the two DocTooling-era writers are loud regenerators (write **then fail**),
      unlike ISSUE-036's `DWARF_SELF_HEAL` which clears its own failure. ISSUE-036 remains the only
      pass-enabling unguarded writer; its shared `CiEnvironment` guard recommendation stands.

Depth caveat: DocTooling (10 files / 915 lines) was audited via its behavioural questions (write scope, CI
coupling, injection surfaces) and its tests, not line-by-line; the round's conclusions do not depend on its
internals.

## Corrections applied (criteria compliance pass)

- [x] **ISSUE-048 upgraded from reasoned to measured** — paired CRLF/LF experiment above; exposure count
      corrected from "3 sites" to **2 failing tests**. The probe clone was deleted after the run.
- [x] **Custody gate applied**: `git status --porcelain` verified clean on `84e13e7` before any measurement this
      session. (Recorded practice change after the round-12 contamination: rounds 8–11 lacked this gate and paid
      for it with the withdrawn ISSUE-039.)
- [x] **Instrument-power statements** (what each round-16 scan *cannot* see):
      - Dead-code name-counting is blind to reflection-constructed and string-composed member names; a name with
        one occurrence could still be invoked dynamically. All six flagged members were read before claiming.
      - Zero-assert scanning has power equal to the completeness of the helper allowlist (`Col.*`, `R.Check`,
        `NoErrors`, `Has*/Expect*` …); a throwing helper outside the vocabulary would hide. Residue was driven to
        0 by reading bodies, not by trusting the list.
      - The `global::` sweep covers string literals only; code assembled via `CodeWriter` calls at runtime is
        covered by the round-10/11 output measurements instead.
      - One instrument failure disclosed: `grep -c $'\r'` returned 0 on a file `file(1)` proves is CRLF —
        `file(1)` was used as the authoritative line-ending instrument.
- [x] **Measured-vs-inferred labels for this round's verdicts**: MEASURED — global:: refined scan (0 hits),
      assertion forensics (0 residue), CRLF exposure (2/9 vs 9/9), CI gate wiring (full-file read of `ci.yml`
      after the truncated-grep near-miss). INFERRED — DocTooling internals (behavioural questions only, caveat
      above), ISSUE-047 impact on consumer tooling (mechanism argued from RS2000's purpose, not demonstrated
      against a consumer).

## Standing ledger (pre-existing, restated as tasks for one view)

- [x] **DONE (`99bc8ee`) — ISSUE-044 (Medium — only functional defect)**: thread `compilation`/`allowNonPublic` through
      `ReadableMembers` (13 omitting sites) and `WritableMembers` (7), make the params **required**.
      Sites: `Members.cs:146,:165,:719,:733`; `Flatten.cs:31,:83,:130,:440,:509,:644,:706,:1000,:1091`;
      `Projection.cs:92,:172,:247,:277,:713`; `MapperExtractor.cs:2562`.
      **Round 17 corrections:** that list is 19 paths, not 20 — `Flatten.cs:233` was missing (partial omitter),
      and `ConstructorSelector.cs:187` was invisible to the instrument entirely (direct `MemberFacts` caller).
      Defaults were removed from `MemberFacts` itself, not just the wrappers, which is what closes the class.
      **Projection is the deliberate exception** — see the round-17 note above; threading the flag there breaks
      the DWARF028 contract.
- [x] **DONE (`7378851`) — ISSUE-038 (Medium)**: pin the SDK for real — `"10.0.110"` + `rollForward: "disable"` + CI drift check —
      or stop calling it pinned.
      **Round 17:** `10.0.110` is not installed on the maintainer's machine (only `10.0.101` and `9.0.308`), so
      that pin would have broken the local build. Pinned `10.0.101` + `rollForward: disable`, all six
      `setup-dotnet` steps changed from `10.0.x` to the same exact version, plus a CI step that names both
      versions on mismatch. CONTRIBUTING documents the strictness.
- [x] **DONE (`99bc8ee`) — ISSUE-043 (Low, latent)**: pass `autoNest` at `Projection.cs:741`; required-params fix above retires the
      class.
- [x] **DONE (`23594e6`) — ISSUE-045 (Low)**: decide the `char` conversion policy (integral vs text-boundary) and pin the full
      matrix in one Theory.
- [x] **EXPERIMENT RUN (`e4dd185`; pick still yours) — ISSUE-046 (Medium)**: run the deciding experiment — EF Core + SQLite, does
      `?? new List<T>()` translate in a projection — then pick candidate 1 (honour AsEmpty) or 3 (document
      AsNull-by-nature).
- [x] DONE (`d2fc4b0`, as a REWRITE) — the round-13 **registry torture test** (`RegistryConcurrencyTortureTests.cs`, delivered, red-green
      validated). **Round 17: it is not recoverable.** The file is absent from the repo, from git history, and
      from this machine — the session that produced it ran elsewhere and its filesystem did not survive. This is
      a rewrite, and it cannot be red-green validated against a fixed implementation; verify it has teeth by
      temporarily breaking the registry instead.
- [x] DONE (`f3e2670`) — Round-15 trust trio: refresh root `SECURITY.md` ("pre-1.0" is stale), add `CHANGELOG.md` + release-notes
      step, add the `EmitCompilerGeneratedFiles` audit paragraph.
- [x] DONE (`723a50b`, one manual step left) — Roslyn floor: bump M.CA to **5.0.0** (+ `?? "null"` at `MapperExtractor.cs:2440`), declare
      "SDK 10.0.100+/Roslyn 5.0", two-sided CI leg (5.0.0 floor + 5.6.0 latest with Analyzers ≥5.3.0), and an
      in-editor smoke check (DWARF001 squiggle + code fix in VS 2026 / VS Code).

## Suggested order

1. ISSUE-044 + commit torture test (functional correctness first).
2. ISSUE-047 + ISSUE-048 + ISSUE-038 + trust trio (one hygiene sweep, all tiny).
3. Roslyn 5.0 bump with its CI legs.
4. ISSUE-046 experiment, then ISSUE-045 policy decision.
