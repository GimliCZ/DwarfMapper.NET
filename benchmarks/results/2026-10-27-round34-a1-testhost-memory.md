# Round 34 A1 — Generator.Tests' test host held native memory, not GC heap (2026-10-27)

**Rig.** Round 33's Linux reproduction of CI's test-host memory, run under the heavy-test lock:
- `mcr.microsoft.com/dotnet/sdk:10.0.101` in Docker Desktop. The VM has 16.7 GB, close to a hosted runner's 16 GB.
- `--cpuset-cpus 0-3`, so `ProcessorCount` reads 4.
- No `--memory` flag. A cgroup limit would cap the GC heap, and the runner has none.
- Generator.Tests is built in Debug, the configuration Stryker builds.
- The suite runs with `RunConfiguration.DisableParallelization=true`. That is the line Stryker 4.16 writes, so a
  mutation leg runs the suite serially.
- Every trial runs in a fresh container.
- The two ILVerify facts fail in every trial in both arms, because the image has no ilverify.

**Why.** On CI the largest Generator.Tests host per mutation leg reached 4.7-9.8 GB. On run 40477294097 (2026-10-27)
the pipeline leg's 16 GB runner went down to 0.6 GB available. Round 33 had already seen that the test host, not
Stryker, was the risk. Its open experiments were a gcdump, a GC heap limit, and the `2f3b290` PLINQ arm.

## What the memory was (`28ebb7c`, before)

| Instrument | Reading |
|---|---|
| dotnet-counters (`counters-1`) | working set 6.76 GB at the end; GC committed size never above 0.41 GB; GC heap after the last collection never above 0.38 GB; assemblies 76 → 1,740 |
| gcdump at 2 GB and 4 GB RSS (`dump-1`) | 112 MB and 214 MB of live objects |
| `/proc/<pid>/status` each second (`maps-1`) | RssAnon up to 5.4 GB; RssFile 0.08 GB; RssShmem 0.22 GB |
| `/proc/<pid>/smaps` at 5 GB RSS | 4.87 GB anonymous, most of it in fully resident ~64 MB VMAs: glibc malloc arenas |
| MemAvailable | fell 15.07 → 7.91 GB as the host rose to 7.09 GB: real memory, not page cache |

So the GC heap was about 6 % of the process. The rest was native memory that `malloc` had handed out. A GC heap
limit (`DOTNET_GCHeapHardLimitPercent`, round 33's candidate) would not have touched it.

The RssAnon steps line up with the symbol-level unit tests, matched by the TRX start and end times of the tests
running in each second:

| Run time | RssAnon step | Running |
|---|---|---|
| 41-42 s | +556, +656 MB | `TypeInterfacesCoverageTests` |
| 64-66 s | +341, +415, +351 MB | `TransferModelShapeTests`, `DenseEnumUnderlyingTypeTests` |
| 70-72 s | +500, +711, +746 MB | `CharConversionPolicyTests`, `FlattenGraphFuzzTests`, `BlittableProofCoverageTests` |
| 93 s | +658 MB | `FeatureCombinationInvalidTests`, `TypeFactsTests`, `LayoutHygieneTests` |

## Cause

Those tests, and about thirty others, built their metadata references inline on every compile. Some referenced the
corlib alone; others referenced every assembly the test host had loaded. Two things combine:
- `MetadataReference.CreateFromFile` copies the whole image into native memory so that it can close the file.
- That copy is freed only when the reference is finalized. A GC that sees a ~0.4 GB managed heap has no reason to run
  finalizers soon, and glibc keeps the memory that is freed.

`GeneratorTestHarness` already built its own set once, for exactly the related reason of metadata I/O contention.
The helpers copied the older inline pattern.

## Fix (`3144bab`)

- `GeneratorTestHarness.ReferenceTo(path | Assembly)` creates one reference per assembly file for the host, and the
  harness's own set goes through it.
- `LoadedAssemblyReferences()` is the every-loaded-assembly sweep the helpers did inline. It is read at the call, so
  each compilation sees the same files it did before.
- 53 call sites in 37 files moved to these helpers.
- `MetadataReferenceReuseScanTests` forbids the API anywhere else in the project. It found 52 call sites outside the
  harness before the change and none after.

## A/B, `28ebb7c` (before) and `3144bab` (after), alternating

| Trial | Peak test host (VmHWM) | Lowest MemAvailable | Peak working set | Max GC committed | Assemblies at end | Suite |
|---|---|---|---|---|---|---|
| before-1 | 5.63 GB | 8.38 GB | 5.58 GB | 0.45 GB | 1,714 | 2 m 1 s |
| after-1 | **0.83 GB** | 13.26 GB | 0.82 GB | 0.43 GB | 1,735 | 1 m 28 s |
| before-2 | 6.92 GB | 7.23 GB | 6.89 GB | 0.40 GB | 1,539 | 1 m 45 s |
| after-2 | **0.85 GB** | 13.39 GB | 0.83 GB | 0.42 GB | 1,632 | 1 m 28 s |
| before-3 | 8.33 GB | 5.97 GB | 8.13 GB | 0.40 GB | 1,723 | 1 m 43 s |
| after-3 | **0.82 GB** | 13.48 GB | 0.82 GB | 0.44 GB | 1,529 | 1 m 28 s |

- Peak: median 6.92 → 0.83 GB, **8.3x lower**. The arms are far apart: after's highest is 0.85 GB, before's lowest
  5.63 GB. The before arm also spread 2.7 GB from run to run, while the after arm spread 0.03 GB.
- The GC heap is the same in both arms, so the whole difference is the native memory.
- Suite: median 1 m 45 s → 1 m 28 s, **16 % faster**, again with no overlap. The tests no longer read and copy the
  reference files on every compile.
- Managed allocation fell from 16.0 to 14.7 GB, and gen2 collections from 20-24 to 13-15.

Not changed: `EmitAssembly`'s `Assembly.Load(byte[])` still leaves ~1,500-1,700 contexts that can never unload. They
cost about 0.3 GB here, and the global `DwarfMapperRegistry` holds delegates into them (11,161 at the end of `dump-1`),
which would root a collectible context as well.

**Prediction for CI, not yet a measurement.** The mutation legs run Generator.Tests in exactly this serial mode, two
hosts at a time on a 16 GB runner. The largest host per leg should fall from 4.7-9.8 GB to about 1 GB. The first
nightly after this reaches master will show whether it does.
