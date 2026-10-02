<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Research (round 32 T06): why two legs cannot publish to the Stryker dashboard, and the options

**Question.** In nightly run 213, the generator and pipeline legs failed at "Publish the verified report to the
Stryker dashboard" with `HTTP 413 {"message":"request entity too large"}`. The other legs published. What does the
step send? Why is it too large for two legs and not for the others? Which fix keeps the publish loud (protocol
I-1)?

The decision is the owner's: D1 in `ROUND32-TASKS.md`. This file gathers the evidence and gives a recommendation.
Labels follow protocol §7.

## 1. What the step sends [EVIDENCE: `.github/workflows/ci.yml`]

The step PUTs the leg's whole `mutation-report.json` to
`https://dashboard.stryker-mutator.io/api/reports/<project>/<ref>?module=<leg>`, with `--data-binary @"$report"`.
The body is uncompressed JSON.

The step runs only on `schedule` and `workflow_dispatch`. The report has already been measured, checked for
non-vacuity and kept as an artifact by then, as the step's own failure text says: "a failed upload loses no
measurement".

## 2. Run 213, leg by leg [EVIDENCE: the job logs, and the run's artifact list]

| Leg | Tests in the run | Tested mutants | Upload | Artifact (zip of the `.json` and `.html` reports) |
|---|---:|---:|---|---:|
| codefixes | 6,814 | 174 | published | 3,638,913 B |
| doctooling | 6,814 | 290 | published (the dashboard answered with the report's `href`) | 7,927,241 B |
| testing | 7,993 | 110 | published | 87,020,398 B |
| generator | 8,351 | 415 | **HTTP 413** | 109,415,879 B |
| pipeline | 8,351 | 284 | **HTTP 413** | 113,525,712 B |
| runtime | — | — | never reached: the leg died in its config check (T01) | — |

Size does not follow the tested-mutant count. The testing leg tests 110 mutants and its artifact is 87 MB; the
doctooling leg tests 290 and its artifact is 8 MB. The dashboard's limit sits between what the testing leg sent
and what the generator leg sent. Neither JSON size is known, because the artifacts cannot be opened from this
session (§6).

## 3. What a report is made of

### 3.1 What the pinned reporter writes [MEASURED: decompiled `dotnet-stryker` 4.16.0, CI's pin]

- **Excluded files are placeholders.** A file the config's `mutate` globs exclude is written as
  `SourceFile.Ignored`: the text "File ignored by mutate filter", and no mutants
  (`JsonReport.GenerateFileReportComponents`). So the generator leg's 11,393 mutants "Removed by mutate filter",
  and the sources of the files they sit in, are not in its report.

  *Correction:* an earlier draft of this research inferred the opposite, that the report carries the whole
  mutated project. The reporter's code overturned that before the draft was committed.
- **Every mutant of an included file carries two test-id lists,** `coveredBy` and `killedBy`, whatever its status
  (`JsonMutant`'s constructor: `CoveringTests.GetIdentifiers()` and `KillingTests.GetIdentifiers()`).
  - A test id is a VsTest GUID: 36 characters, 39 B in a JSON list.
  - For a static mutant, the covering set is every test whose coverage is not `Exact`, which is nearly the
    whole suite (`CoverageAnalyser.CoverageForThisMutant`; `FINDING-T02-ci-phantom-kills.md` §4).
- **`testFiles` carries every test file's full source,** plus each test's id and name (`JsonTestFile`).

### 3.2 Sizes measured on this tree [MEASURED]

- Every `.cs` file under `tests/` comes to 7,109,116 B; `DwarfMapper.Generator.Tests` alone is 5,315,579 B. That
  is an upper bound on what test sources add to a report: 7.1 MB in the JSON, and the same again inside the HTML
  report. Test sources compress well, so it is a small share of any artifact.
- The legs' mutated sources are a handful of files each.

### 3.3 So the bulk is test-id lists [INFERRED, by elimination and arithmetic]

What is left to fill 87–113 MB of zipped reports is `coveredBy` and `killedBy`. Their size grows as mutants
times covering tests:
- one list naming all 8,351 tests is 326 KB;
- 415 mutants with such a list come to 135 MB per list kind.

Random hex digits compress only about twofold, so these lists survive zipping. That fits all three large
artifacts, and the two small ones:
- the generator, pipeline and testing legs mutate code that most of the suite runs, through the generator and
  the testing toolkit;
- DocTooling and CodeFixes are reached by few tests, so their lists are short.

**What would settle it:** the composition of one real report. The appendix gives a ten-line measurement for an
artifact the owner downloads.

## 4. The options

| | Option | What the dashboard shows | Against I-1 | Cost |
|---|---|---|---|---|
| A | Upload the score only | the badge and score, no report viewer | loud: a failed upload still fails | small. The body shape is unconfirmed here (below) |
| B | Upload the report filtered to scoreable mutants (Killed, Survived, Timeout, NoCoverage) and their files | the viewer, with only the gated mutants | loud | **does not address the size.** By §3.1, excluded files are already placeholders. Inside included files, B removes CompileError mutants, whose lists are empty because a mutant that never compiled was never covered, and the generator leg's 83 "block already covered" mutants. The 415 mutants that carry the long lists all stay |
| B′ | Upload every mutant and status, without the `coveredBy` lists and the `testFiles` sources | the viewer, with every mutant, status and killing test, but no per-mutant "covered by" list | loud; the score is unchanged, because no status changes | a few lines of Python in the step. Must stay schema-valid: `coveredBy` and `testFiles` are optional in the report schema [INFERRED: the documentation host is blocked] |
| C | Stop failing the job on a failed upload (`continue-on-error`) | whatever last succeeded, possibly stale | **silences a red.** The step's own comment names the failure: "a badge that can quietly lie is worse than no badge at all" | none, and that is the problem |

**Option A's shape.** The pinned Stryker's own client cannot do it. `DashboardClient` publishes a full report
(`PublishReport`), or real-time mutant batches (`PublishMutantBatch`, `PublishFinished`). Every `mutationScore`
string in `Stryker.Core.dll` belongs to the embedded HTML viewer [MEASURED: strings and the decompiled client].
The step uses its own `curl`, so the shape is the dashboard's to accept. StrykerJS's `dashboard.reportType:
"mutationScore"` sends `{"mutationScore": <number>}` [INFERRED: from outside this repository; the dashboard's
documentation is outside this session's network policy].

## 5. Recommendation

**Measure first, then B′; fall back to A if B′ is still refused. Reject C. Reject B as first drafted.**

- **Measure.** One downloaded artifact and the appendix give the split. If test-id lists are the bulk, as §3.3
  predicts, B′ removes most of the body.
- **B′.** It keeps what the dashboard is for: each module shows every mutant the leg is gated on, with its status
  and killing tests. It changes nothing the gate measures.
- **A** is the floor if the limit sits below even a list-free report.

Each option needs a dispatch to prove against the real endpoint. Starting one is the owner's (protocol §4).

## 6. Owed

- Opening an artifact: the download URL the API returns is refused by this session's egress proxy ("CONNECT
  tunnel failed, response 403").
- The run-213 artifacts expire on 2026-12-31: `mutation-report-generator` (11221921690) and
  `mutation-report-pipeline` (11227658774).

## Appendix: measuring a downloaded report

```python
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
size = lambda o: len(json.dumps(o, separators=(",", ":")).encode())
m = [x for f in d["files"].values() for x in f.get("mutants", [])]
print("total", size(d))
print("file sources", sum(len(f.get("source", "").encode()) for f in d["files"].values()))
print("coveredBy", sum(size(x.get("coveredBy") or []) for x in m))
print("killedBy", sum(size(x.get("killedBy") or []) for x in m))
print("testFiles", size(d.get("testFiles") or {}))
```
