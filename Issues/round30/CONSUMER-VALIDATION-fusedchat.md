<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Consumer validation: FusedChat on the round-30 build

**2026-09-10.** The owner asked for round 30 to be *"tested on fusedchat"*. Everything in this repository
is self-validation — a corpus, a gallery, a golden manifest, all authored here. FusedChat is the first
check that is not: a real 32-project application that consumes DwarfMapper as a NuGet package and was
ported to it from AutoMapper.

## A correction that belongs at the top

**"fusedchat" was read as "fused chain" for two consecutive requests.** The owner asked to *"measure
fusedchat changes"* and then *"how about fusedchat tests I requested?"*, and both times it was taken as the
map-fusion work — the `A -> B -> C` chain — because fusion was the live topic. It is their application, and
it is on this machine at `C:\Users\Jouda\RiderProjects\fusedchat-dwarfmapper`, alongside `fusedchat-docker`
and `MedbotOmega` (which even carries `tests/FusedChat.Mapping.{Harness,Tests,Benchmarks}`).

The fusion work produced by that misreading is not wasted — the equivalence oracle and the observability
criterion are real results — but they answered a question nobody asked. **One directory listing would have
settled it, and it was never run.** Same failure as the null-ternary finding: an inference from context,
carried a long way, without the cheap check that would have falsified it.

## What was run

| | |
|---|---|
| consumer | `fusedchat-dwarfmapper`, 32 projects, 8 of them declaring `[DwarfMapper]` mappers |
| how it consumes | `PackageReference Include="DwarfMapper"`, resolved from this repo's `artifacts/nuget` via the solution's own `NuGet.config` |
| package under test | **`DwarfMapper.1.1.0-rc12`**, packed from `ee2c82b` — the current tip |
| superseded run | rc11 from `e980931`; re-run because the generator CHANGED after it (the `[MapShare]`
de-silencing fix `53cd56e` now emits `DWARF090` where it previously said nothing, and FusedChat has 8
mapper-declaring projects that could have tripped it) |
| previous pin | `1.1.0-rc5` — the app was several release candidates behind |
| surface exercised | `[assembly: DwarfMapperValidationRoot]`, `AllowNonPublic = true`, the ambient `IDwarfMapper` facade, and per-project analyzer references (the package marks the analyzer `PrivateAssets="all"`, so it does not flow transitively) |

## Result

```
dotnet restore FusedChat.sln          RESTORE_EXIT=0
dotnet build   FusedChat.sln -c Release --no-restore   BUILD_EXIT=0
dotnet test    FusedChat.sln -c Release --no-build     TEST_EXIT=0
```

| | |
|---|---|
| compile errors | **0** |
| assemblies built | **32** |
| **DWARF diagnostics of any severity** | **0** |
| other warnings | 190, **every one `NU1903`** — pre-existing advisories on `System.Security.Cryptography.Xml` in FusedChat's own dependency graph, unrelated to this package |

**rc12 re-run, 2026-09-10:** `RESTORE_EXIT=0`, `BUILD_EXIT=0`, `TEST_EXIT=0` — 32 assemblies, **0 errors,
0 DWARF diagnostics**, 711 tests (707 passed, 4 skipped). The new `DWARF090` surfaces nothing in FusedChat:
no `[MapShare]` sits on an element-wise endpoint there. The build reports 1,042 warnings on a FULL rebuild
(`CS8618` x584, `NU1903` x190, the rest FusedChat's own nullability) — the rc11 run showed only 190 because
it was incremental, which is worth recording so the two numbers are not read as a regression.

| test project | passed | failed | skipped |
|---|---:|---:|---:|
| `FusedChat.Core.Tests` | 455 | 0 | 0 |
| `FusedChat.Clients.Tests` | 110 | 0 | 0 |
| `FusedChat.Omega.Tests` | 59 | 0 | 0 |
| `FusedChat.Web.Components.Tests` | 33 | 0 | 0 |
| `FusedChat.Server.Tests` | 29 | 0 | 3 |
| `FusedChat.Api.Tests` | 18 | 0 | 1 |
| `FusedChat.Web.Api.Tests` | 3 | 0 | 0 |
| **total** | **707** | **0** | **4** |

The four skips are the application's own: three throughput/latency tests in
`RuntimeOmegaHostedServicePerformanceTests` and one controller case. None is DwarfMapper-related.

## What this does and does not establish

**Establishes:** every generator change from round 29 and round 30 so far — the constant-index dense fill,
`[MapShare]`, `[MapDenseEnumKeys]`, the null-arm behaviour pinned by `ElementNullArmTests`, the span map —
compiles and runs inside a real application across an rc5 -> rc11 jump, with no diagnostic and no test
failure. That is a stronger statement than the corpus can make, because none of this code was written to
be mapped by us.

**Does not establish:**

1. **No behavioural diff was taken.** The suite passing says the app agrees with its own expectations, not
   that the mapped VALUES are identical to what rc5 produced. A true A/B would map the same payloads
   through both packages and compare structurally. `MedbotOmega/tests/FusedChat.Mapping.Harness` looks
   built for exactly that and was not used here.
2. **No performance measurement.** Round 29's figures are all from this repository's own benchmark suite.
   Whether the collection work moves anything in FusedChat is unmeasured.
3. **The four skipped tests were skipped before this run too** — they are not evidence either way.
4. **One release candidate, one machine, Windows.**

## Housekeeping note for the owner

**`fusedchat-dwarfmapper`'s working tree was modified:** eight `.csproj` files were repinned from
`1.1.0-rc5` to `1.1.0-rc11`. Nothing was committed there, and that repository already had unrelated
uncommitted changes before this run. Revert with `git -C ../fusedchat-dwarfmapper checkout -- src` if the
rc5 pin should stand, or keep rc11 if the app should track the round-30 build.
