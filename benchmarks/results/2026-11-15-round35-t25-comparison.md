<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 35 T25 — DwarfMapper against today's Mapperly and Mapster

The owner, 2026-11-14: "run entire performance and usage comparison against dwarf mapper? ... up to date versions (not
automapper of course) ... Target will be performance, security, validations, docs and samples validations." Design:
[`docs/superpowers/specs/2026-11-15-current-competitor-comparison-design.md`](../../docs/superpowers/specs/2026-11-15-current-competitor-comparison-design.md).

**Versions** (latest stable on nuget.org, 2026-11-14): Riok.Mapperly **4.3.1**, Mapster **10.0.14** (the repository
pinned 10.0.8; this branch takes T9's bump), BenchmarkDotNet **0.15.8** (T9's bump). AutoMapper is not measured — the
owner's exclusion; 14.0.0 is its last MIT release and carries three advisories. Prereleases (Mapperly 5.0.0-next.13,
Mapster 10.1.0-pre04) appear only where a cell is a capability question.

## Performance

`benchmarks/DwarfMapper.Benchmarks`' `MapperBenchmarks`, every arm but AutoMapper's (filter
`*Dwarf* *Mapperly* *Mapster* *_Hand* *BlitRatio*`), BenchmarkDotNet 0.15.8 DefaultJob, the suite's own payloads
(`ObjectFactoryV2`, rings of distinct draws, N = 1,000 for the collection rows). The same commit, one run per platform:

- **Windows**: this machine (AMD Ryzen 5 5600, 12 logical CPUs), Windows 10 22H2, SDK 10.0.101, runtime .NET 10.0.11.
- **Linux**: `mcr.microsoft.com/dotnet/sdk:10.0.101`, Ubuntu 24.04.3, runtime .NET **10.0.1** (the image's), the container
  on four logical CPUs of the same machine.

Mean, **Windows / Linux**:

| Scenario | hand-written | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 |
|---|---:|---:|---:|---:|
| Flat (1 object) | 4.6 / 6.6 ns | 4.7 / 6.8 ns | 4.9 / 6.9 ns | 13.5 / 16.4 ns |
| Flatten (`Order.Customer.Name`) | — | 5.0 / 7.1 ns | 5.0 / 7.1 ns | 13.4 / 16.6 ns |
| Nested object | — | 10.8 / 17.2 ns | 10.7 / 17.1 ns | 19.6 / 26.6 ns |
| Enum, by name | — | 12.7 / 13.2 ns | 12.4 / 13.0 ns | — |
| Enum, by value | — | 3.4 / **10.8** ns | 3.1 / **5.2** ns | 11.9 / 14.5 ns |
| Array (1,000 reference elements) | — | 5.88 / 7.81 µs | **4.86** / 7.77 µs | 6.03 / 7.65 µs |
| List (1,000 reference elements) | — | 5.46 / 7.83 µs | 5.57 / 8.16 µs | 5.32 / 8.57 µs |
| Dictionary (1,000 entries) | — | **8.18 / 9.40 µs** | 18.92 / 10.91 µs | 25.57 / 20.82 µs |
| Blit (1,000 blittable structs) | — | **0.41 / 0.51 µs** | 0.95 / 1.08 µs | 0.98 / 1.13 µs |
| Value-element list (`int[]` → `List<long>`) | — | **0.32 / 0.43 µs** | 0.88 / 1.06 µs | 0.91 / 1.08 µs |
| Widen (`int[]` → `long[]`) | — | **0.32 / 0.45 µs** | 0.42 / 0.53 µs | 0.65 / 0.80 µs |
| Nested graph, mixed fills | — | **0.83 / 1.23 µs** | 1.70 / 2.03 µs | 1.02 / 1.46 µs |

**Allocations are identical in every row on both platforms, except two**: the dictionary (DwarfMapper 31,120 B,
Mapperly 31,176 B, Mapster **102,376 B**) and the nested graph (DwarfMapper and Mapster 8,112 B, Mapperly 9,456 B).
Allocated bytes are deterministic, and were the same on both platforms to the byte.

Read with the two runs side by side:

- **Where DwarfMapper is ahead on both platforms**, it is a path the others do not have: the blit (2.1–2.3×), the
  value-element list (2.4–2.8×), the SIMD widen (1.2–1.3× over Mapperly, 1.8–2× over Mapster) and the nested graph's
  fills (1.6–2× over Mapperly). The same leads as the August–September table in `docs/COMPARISON.md`.
- **Level, within a few per cent, on both**: flat, flatten, nested, by-name enum, the reference list.
- **Mapperly ahead**: the reference-element array on Windows (17 %), the null guard DwarfMapper puts on each element and
  Mapperly does not — the trade `docs/COMPARISON.md` already states, measured again. On Linux the three are level.
- **Platform-dependent — never quote one platform's figure as the other's**: the dictionary is 2.3× over Mapperly on
  Windows and 1.16× on Linux (the split `2026-08-11-dict-windows-rerun.md` recorded). The by-value enum reads level on
  Windows and 2.1× in Mapperly's favour on Linux (10.8 against 5.2 ns), and the Linux gap is **not a cost of the
  generated code**: see "The Linux by-value enum gap" below.
- **Mapster** takes 1.5–3.5× as long as the generators on every single-object row, and allocates 3.3× on the
  dictionary.

The union suite (`benchmarks/DwarfMapper.UnionBenchmarks`, BenchmarkDotNet 0.14.0 — 0.15.8 cannot run .NET 11) was run
again against Mapster 10.0.14 on both platforms: the same picture as round 35 T24's, the generated union map level with
the same code hand-written in its shape (Windows 6.42 against 6.35 ns) and Mapster about 3× on the hierarchy.

### The Linux by-value enum gap

Measured until it had a cause, because a 2× row would otherwise have been read as a defect in what DwarfMapper emits.

- **Not the runtime patch.** The suite's two by-value enum methods on Linux runtimes 10.0.1 and 10.0.15 (images
  sdk:10.0.101 and sdk:10.0.404): 10.5 against 5.1 ns, and 10.9 against 5.5 ns.
- **Not the emitted code.** BenchmarkDotNet's disassembler (`--disasm`, runtime 10.0.1) shows both methods fully
  inlined, the enum conversion compiled to one `mov` in each — `int.CreateChecked` leaves nothing behind — and the same
  allocation helper. DwarfMapper's differs by its `ArgumentNullException.ThrowIfNull` (a `test`/`je` pair) and the frame
  its cold throw path needs: 127 bytes against 79.
- **Not that null check either.** [`comparison-probes/enum-gap/`](../comparison-probes/enum-gap) rebuilds both methods
  verbatim, and each with other forms of the check:

| Method, ns | Linux run 1 | Linux run 2 | Windows |
|---|---|---|---|
| Mapperly's, verbatim | 5.46 | 5.42 | 3.71 |
| Mapperly's + `ThrowIfNull` | 9.66 | 6.45 | 3.83 |
| DwarfMapper's, verbatim | 9.85 | 9.36 | 3.87 |
| DwarfMapper's without the check | 5.48 | **19.63** | 3.69 |
| DwarfMapper's with `if (src is null) Throw();` | 5.36 | 5.22 | 3.60 |

Linux: sdk:10.0.101 image, runtime 10.0.1, Docker on WSL2. Windows: the host, runtime 10.0.11. Both on the same AMD Ryzen
5 5600. Run 2 differs from run 1 only by three more variants in the probe. Yet the method without a null check, whose
73 bytes are the same as Mapperly's, went from 5.5 to 19.6 ns, and Mapperly's with the check from 9.7 to 6.4 ns. The same
code measured 3.6× apart in two processes, so at five nanoseconds this Linux setup measures where code and data land, not
what the code does. On Windows every variant is within 0.3 ns. The row is recorded as a measurement artefact. The
emitted check stays: it has been `ThrowIfNull` since `4103d26d`, chosen to keep the map inside the JIT's inlining budget,
and changing it on this evidence would chase noise.

## Validation: what each library says about a mistake, and when

Eight mistakes people make, each compiled once per library with every option at its default (the probe:
[`benchmarks/comparison-probes/validation`](../comparison-probes/validation), one build per cell, SDK 10.0.101; the build's diagnostics are the build-time answer, running the
result is the runtime one). Mapster has no build-time step; its opt-in strictness
(`RequireDestinationMemberSource`) is the extra column, and it acts when the configuration compiles, at run time.

| Mistake | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 | Mapster, strict |
|---|---|---|---|---|
| A destination member with no source | **build error** DWARF001 | warning RMG012; at run time the member keeps its default | silent; keeps its default | throws `CompileException` |
| A source member nothing reads | silent (DWARF039 with `RequiredMapping = Both`) | warning RMG020 | silent | silent |
| `string` into `int` | warning DWARF038 (a parse is applied); `"abc"` throws `FormatException` | silent; throws `FormatException` | silent; throws `FormatException` | the same |
| A nullable into a non-nullable member | warning DWARF070; a null is stored | silent; the assignment is **skipped** - the member keeps its default | silent; a null is stored | the same |
| An enum member with no counterpart (by name) | **build error** DWARF015 | warning RMG038; at run time the value **2, which the target enum does not define** | silent; the undefined value 2 | the same |
| A read-only destination member | **build error** DWARF007 | warning RMG066 ("No members are mapped"); the value is **lost** (0) | silent; lost | silent; lost |
| A constructor parameter nothing supplies | **build error** DWARF024 | **build error** RMG013 | silent; `null` passed to the constructor | throws `CompileException` |
| `CustomerName` from `Customer.Name` (no flattening declared) | **build error** DWARF001 — flattening is declared, never guessed | flattened by convention | flattened by convention | the same |

Counted: **DwarfMapper refuses five of the eight in the build and warns on two**; the eighth (a source member nothing
reads) is a deliberate default, with a switch that makes it a diagnostic. **Mapperly** refuses one in the build and
warns on five — and every warned case still compiles to a mapper whose output is quietly wrong: a default left in place,
a value lost, an enum value its type does not define. Under warnings-as-errors those five become errors too, which is
the configuration Mapperly's documentation recommends. **Mapster** says nothing before run time; strictness turns two
cases into a throw when the configuration compiles, and leaves the other six as they were.

The last row is a design difference rather than a mistake: Mapperly and Mapster flatten by naming convention, and
DwarfMapper does not guess (`[Flatten]` or `[MapProperty]` declares it). The convention is convenient; it is also what
links two members because their names happen to line up.

## Security: what each library does with hostile shapes

The probe [`benchmarks/comparison-probes/security`](../comparison-probes/security) runs one process per cell — a stack overflow cannot be caught, it ends the process, so a
cell is how the process ended — on SDK 10.0.101 in a Linux container (main-thread stack 8 MB, `ulimit -s 8192`). Every
option at its default.

| Shape | DwarfMapper | Mapperly 4.3.1 | Mapster 10.0.14 |
|---|---|---|---|
| A linked chain 100,000 deep | refused: `DwarfMappingDepthException` at the default depth of 64 (`MaxDepth` goes to 1,000) — catchable | maps it | maps it |
| A node that references itself | `DwarfMappingDepthException` — catchable | **stack overflow: the process dies** (exit 134) | **stack overflow: the process dies** |
| Re-entry: a user converter that calls the mapper again, 100,000 deep | `InsufficientExecutionStackException` — catchable (round 35 T20) | **stack overflow: the process dies** | did not finish in 300 s |
| The same, 1,000 and 10,000 deep | maps (0 s) | maps (0 s) | maps (0 s, then 3 s) |
| NativeAOT: publish | clean | clean | IL3053, IL2104 (Mapster and Mapster.Core) |
| NativeAOT: run a flat map | maps | maps | **throws** `MissingMethodException` (no parameterless constructor for `Mapster.NameMatchingStrategy`) |

What a reader should take from it:

- **No input makes DwarfMapper end the process.** Each recursive shape becomes an exception the caller can catch — the
  depth guard for a deep or cyclic graph, the stack check for re-entry through user code. The cost is the other side of
  the same guard: a legitimately deep chain (here 100,000 links) is refused, and `MaxDepth` stops at 1,000.
- **Mapperly and Mapster map the deep chain on an 8 MB stack**, and on the same shapes with a cycle or with re-entry they
  run until the stack ends. A `StackOverflowException` is not an exception a server can handle: one request with a
  cyclic payload takes the process down. (On a 1 MB stack — Windows' main thread — the deep chain itself would end the
  same way; not measured here.) Mapperly can opt into reference handling (`UseReferenceHandling`) and Mapster into
  `PreserveReference` and `MaxDepth`; the table is their defaults.
- **Mapster's re-entry slows down faster than its depth grows** — 3 s at 10,000 levels, more than 300 s at 100,000 — so
  the 100,000 case hit the 300 s limit before the stack ran out.
- **Mapster does not run under NativeAOT**: it publishes with trim and AOT warnings, then fails on the first map. The two
  generators publish clean and run.

## Docs and samples

**Links.** Every relative link in every tracked Markdown file, resolved against the file's folder
([`benchmarks/comparison-probes/linkcheck.py`](../comparison-probes/linkcheck.py)): 228 files, 324 relative links, **none to a missing file or folder**. 28 links point at a
heading anchor that does not exist, all of them inside one historical document,
`Issues/round20/SURFACE-MATRIX-FINDINGS.md` (`#D1` ... `#D20`, `#N4`, `#G5` — the headings they name carry longer
titles, so GitHub's anchors are longer than the links).

**Samples.** Five runnable samples; what CI runs of each:

| Sample | CI | What a run proves |
|---|---|---|
| `DwarfMapper.Conformance` | the `conformance` job | its 78 assertions |
| `DwarfMapper.AotSample` | `aot-trim-gate`, `net11` | publishes as NativeAOT and runs its checks |
| `DwarfMapper.Unions` | `net11` (from the packed package) | five examples, each checking its own result |
| `DwarfMapper.Gallery` | **built, never run** | — |
| `DwarfMapper.AotBench` | built, never run | (a benchmark, not a check) |

The Gallery is the one that matters: its ~40 files are the examples README and the how-tos quote, and no CI step and no
test runs them. Running it would catch an example that throws; it would not catch one that prints a wrong value, because
39 of the 40 files print their result rather than check it.

## Found on the way

- **BenchmarkDotNet 0.15.8 cannot run a .NET 11 process** — its SDK validator throws `NotImplementedException:
  GetRuntimeVersion not implemented for NotRecognized` before the first benchmark. The union suite stays on 0.14.0 (a
  `VersionOverride` on that project, with the reason beside it). T9's bump was proven on .NET 10 only.
- **T9's BenchmarkDotNet commit, cherry-picked alone, carries a lock file the branch cannot restore in locked mode**
  (NU1004): it names T9's earlier DependencyInjection 10.0.15. Corrected here; T9's own branch is consistent.
- **Mapster cannot compile `Include` arms into an abstract record destination** (round 35 T24): still so on 10.0.14,
  fixed in the 10.1.0-pre04 prerelease.

## Not done here, and proposed

- **Run the Gallery in CI**, and pin its output: running it catches an example that throws; comparing its output with a
  committed copy catches one that prints a wrong value, which 39 of its 40 files would otherwise get away with.
- **The 28 dead anchors** in `Issues/round20/SURFACE-MATRIX-FINDINGS.md` (a historical document) — fix, or leave as
  history; and the link check itself as a test, so the count of dead file links stays at zero.
- **The validation and security probes as tests** in `tests/DwarfMapper.DifferentialTests`, so a library that changes
  behaviour turns a test red instead of leaving this document stale — the design's intent; this pass measured first,
  because what to assert had to be known before it could be asserted.
