<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 30 item H — cost is a contract, and the contract is "flat"

**Owner ruling, 2026-09-10**, after the ambient-dispatch finding: *"We need to lock down linear increases of
function usages over time — those things are hard stops"*, and the suite should *"loop and measurably confirm
for every test that the memory did not shift."*

## 1. What was actually wrong with the gates

Nothing was missing from the gates in the ordinary sense. `allocation-baseline.json` pins 28 scenarios to the
**exact byte** and fails in both directions; an unexplained decrease is a finding too. It is one of the
strictest instruments in the repository, and it could not have caught item F's defect at any point in its
life:

| registered maps | bag walk |
|---:|---:|
| 1 | 80 B |
| 10 | 296 B |
| 1,000 | 24,058 B |
| 2,738 | 65,768 B |

`80 + 24·N`. At the scale a benchmark process runs at, the defect costs a few hundred bytes and looks exactly
like a legitimate cost — `Nested_Dwarf` is honestly pinned at 112 B. It would have been measured, pinned as
correct, and held byte-identical forever while a consumer paid 56 KB per call.

> **The pins measure an absolute value at ONE scale. The defect was a SLOPE. A pin at a single point on a
> line cannot see the line's gradient.**

This is the **third** occurrence of that shape. `BenchmarkCoverageSelfValidationTests` records ISSUE-019 — an
unknown-count source into an array target allocated two buffers on every map for as long as the project
existed. Round 29's collection sweep closed on the same sentence: every collection claim rested on one
element count. The pattern is not "someone forgot a test"; it is **the gate varied the wrong dimension**, and
no amount of tightening a pin fixes that.

## 2. What was built

**A measurement primitive** (`tests/DwarfMapper.IntegrationTests/CostContract.cs`) shared by every cost
contract, so flatness is judged by one rule rather than a threshold invented per test:

* `BytesPerOperation` — per-thread allocation, warmed up so the window is steady-state.
* `MeasureAgainstScale` — cost against the size of ambient state. **Refuses a lever below 50×**: a slope
  measured between two nearby scales is noise reported as a result.
* `MeasureOverTime` — cost early versus after 50,000 intervening calls, the accumulation axis.
* `FlatCeiling` — the shared tolerance (25 % + 256 B), wide enough for JIT tiering, nowhere near admitting a
  linear term.

It **measures and returns**; it never asserts. Assertions live in the test, where a reader sees them — a
rule `TestTheTestsScanTests` enforces, and which correctly rejected the first version of this work.

**An architecture gate** (`CostContractScanTests`) enumerating the runtime's per-call surface — 16 members
across `DwarfMapperRegistry`, `DwarfRefContext`, `IDwarfMapper`, `DwarfMapperFacade` — and requiring each to
name the test that pins its cost slope, or an `EXEMPT:` with a reason. Three scans:

1. every per-call member is classified,
2. every named test actually exists in the integration suite (so the table cannot rot into reassuring
   strings),
3. the enumeration is non-empty, in the shape `SelfAuditNonVacuityTests` established — a subset check over an
   empty universe passes while verifying nothing.

Verified to have teeth: removing one contract entry turns scan 1 red naming the member.

**A standing proof of the defect** (`RegistryStructureProofTests`) measuring the removed structure and its
replacement side by side, outside the registry so it survives refactoring. One test asserts the **old**
structure IS linear — deliberately inverted, because that is the fix's entire justification. If a future .NET
makes bag enumeration allocation-free, it goes red and the correct response is to delete it and the remark it
backs, not to mute it.

## 3. Where this deliberately departs from the ruling

The ruling says *every* test should loop and confirm memory did not shift. **That is not what was built, and
the difference is deliberate.**

* Most of the ~8,800 tests are generator tests that run Roslyn compilations. Allocation there is dominated by
  the compiler by three orders of magnitude; a memory assertion on them measures Roslyn, not DwarfMapper, and
  would be noise wearing the costume of a gate.
* Allocation measurement requires serialisation — the existing tests are in `[Collection("allocation-isolated")]`
  for exactly this reason. Making it universal serialises the entire suite, which currently runs 8,841 tests
  in ~77 s.
* Cost is only meaningful on code a consumer executes per operation. That surface is **16 members**, and it
  is now covered **exhaustively** with a scan that fails when it grows.

Exhaustive over the surface where cost lives beats uniform over tests where it does not. The goal — no
undetected slope — is met more completely this way, and the gate cannot be forgotten because the build breaks
when the surface grows.

## 4. What this does not yet cover

* **The generated code.** Contracts here cover the hand-written runtime. An emitted mapper's cost is pinned
  by `allocation-baseline.json` at one scale, which is precisely the instrument shown above to be blind to
  slopes. The collection sweep varies element count; nothing varies *map graph* size. Open.
* **Ambient-path benchmark rows.** Deferred: moves `totalBenchmarks` 63 → 65 and R1/R2 demand a full smoke
  re-measure in the same commit.
* **The runtime leg's mutation score.** Item F added 52 lines to a mutated file, so the 73.9 % in
  `Issues/round27/AUDIT-mutation-scope.md` is unverified and flagged there rather than restated.
