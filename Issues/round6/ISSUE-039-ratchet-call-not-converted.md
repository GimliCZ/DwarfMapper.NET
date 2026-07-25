# [ISSUE-039] Direct-compile-error ratchet fired on an unconverted call

| | |
|---|---|
| **Severity** | Medium |
| **Type** | Failing test (tooling working as designed) |
| **Component** | Tests / adoption ratchet |
| **Finding ID** | R6-2 |
| **Affects** | `FixtureAdoptionScanTests.Direct_compile_error_calls_have_not_grown` |
| **Status** | **NOT reproducing on current master** — count is at baseline (ratchet green) |

## Claim (external audit round 6)
At the audited state, the suite was red: `Direct_compile_error_calls_have_not_grown` counted 51 direct
`GeneratorTestHarness.RunAndGetCompilationErrors` calls against a baseline of 50, because
`NestedNullableParameterTests.cs:86` used a raw `RunAndGetCompilationErrors` + `Assert.Empty(errors)` instead of
the shared `GeneratorAssert` fixture.

## Verification on current master
`dotnet test --filter Direct_compile_error_calls_have_not_grown` **PASSES** — the direct-call count is at the
baseline of 50. The audited state predated the maintainer's audit-fix commits, which changed the surrounding
tests; on current master the count is not over the baseline.

## Lesson (kept, because it recurred live)
This ratchet is **zero-slack by design and it works**. When ISSUE-040's regression tests were first written using
raw `RunAndGetCompilationErrors`, they pushed the count to 52 and the ratchet went red immediately with a message
naming the file, the count, and both remedies. The fix was to route those tests through the shared fixture
(`GeneratorAssert.EmitsCompilableCode`) — and ultimately to a unit test with no direct call at all — **not** to
raise the baseline. Raise `DirectCompileErrorCallBaseline` only for a test that genuinely asserts a specific CS id
or error count; convert everything else.

No action required on master; recorded so the ratchet's behaviour and the correct response are on file.
