# Benchmark results — full sweep (2026-07-24, Windows)

Measured at `ef3d432`, i.e. after the four-part maintainability programme (testing framework, shared engine
core, emission layer, `MapperExtractor` split) and the ISSUE-016 / 019 / 023 fixes.

Reproduce:

```
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks -- --filter '*'
dotnet publish -c Release -r win-x64 samples/DwarfMapper.AotBench   # needs vswhere.exe on PATH
```

```
BenchmarkDotNet v0.14.0
Windows 10 Pro 10.0.19045
Runtime=.NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2
GC=Concurrent Workstation
HardwareIntrinsics=AVX2,AES,BMI1,BMI2,FMA,LZCNT,PCLMUL,POPCNT VectorSize=256
Job: DefaultJob
```

## Read this before quoting any number

**The payloads are hand-built and uniform.** `GlobalSetup` constructs data inline (`Id = i`, `Name = "n" + i`,
`Active = i % 2 == 0`) — near-constant string lengths, **no nulls**, no boundary values, perfectly regular
branches. That under-measures exactly where mappers differ: null-handling branches, null-substitute paths,
conversion edge cases, and branch misprediction. These numbers are a baseline for this commit, not a
realistic-workload comparison. Sourcing payloads from the fuzzer/fixture generators — and handing the identical
instance to every mapper — is the agreed next step for this suite.

**The `Dict` row is not a like-for-like comparison.** `DictSrc.M` and `DictDst.M` are both
`Dictionary<string, int>`, and Mapperly returns the *source dictionary by reference* — 104 B is the wrapper
object alone; 1,000 entries cannot be copied in 104 bytes. DwarfMapper, Mapster and AutoMapper all genuinely
copy. Among the mappers that actually copy, DwarfMapper is the cheapest by ~3x in both time and allocation. Do
not read "Mapperly is 780x faster at dictionaries" out of this table — it is measuring aliasing against copying.

## BenchmarkDotNet suite

| Method             | Categories | N    | Mean          | Error       | StdDev      | Median        | Ratio | Gen0   | Gen1   | Allocated |
|------------------- |----------- |----- |--------------:|------------:|------------:|--------------:|------:|-------:|-------:|----------:|
| Array_Dwarf        | Array      | 1000 |  5,431.027 ns |  88.6592 ns |  74.0344 ns |  5,447.332 ns |     ? | 2.8687 | 0.4730 |   48048 B |
| Array_Mapperly     | Array      | 1000 |  5,158.463 ns |  99.3396 ns | 118.2568 ns |  5,140.578 ns |     ? | 2.8687 | 0.4730 |   48048 B |
| Array_Mapster      | Array      | 1000 |  6,166.261 ns | 119.1006 ns | 158.9960 ns |  6,184.018 ns |     ? | 2.8687 | 0.4730 |   48048 B |
| Array_AutoMapper   | Array      | 1000 |  5,945.283 ns | 111.7871 ns | 109.7899 ns |  5,922.527 ns |     ? | 2.8687 | 0.4730 |   48048 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Blit_Dwarf         | Blit       | 1000 |    527.947 ns |  10.5835 ns |  22.7821 ns |    529.396 ns |     ? | 0.7210 | 0.0305 |   12048 B |
| Blit_Mapperly      | Blit       | 1000 |  1,059.332 ns |  17.8030 ns |  15.7819 ns |  1,056.725 ns |     ? | 0.7210 | 0.0305 |   12048 B |
| Blit_Mapster       | Blit       | 1000 |  1,097.014 ns |  17.8966 ns |  14.9445 ns |  1,092.497 ns |     ? | 0.7210 | 0.0305 |   12048 B |
| Blit_AutoMapper    | Blit       | 1000 |  1,143.022 ns |  19.9262 ns |  17.6641 ns |  1,142.958 ns |     ? | 0.7210 | 0.0305 |   12048 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Dict_Dwarf         | Dict       | 1000 |  9,024.628 ns | 179.4423 ns | 443.5370 ns |  8,992.657 ns |     ? | 1.8463 | 0.1831 |   31120 B |
| Dict_Mapperly ⚠    | Dict       | 1000 |     11.577 ns |   0.2777 ns |   0.2598 ns |     11.632 ns |     ? | 0.0062 |      - |     104 B |
| Dict_Mapster       | Dict       | 1000 | 27,991.866 ns | 536.3738 ns | 573.9140 ns | 27,689.262 ns |     ? | 6.1035 | 1.0071 |  102376 B |
| Dict_AutoMapper    | Dict       | 1000 | 26,303.712 ns | 234.7346 ns | 196.0140 ns | 26,288.521 ns |     ? | 6.1035 | 1.0071 |  102320 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Enum_Dwarf         | Enum       | 1000 |      4.612 ns |   0.1375 ns |   0.1350 ns |      4.565 ns |     ? | 0.0014 |      - |      24 B |
| Enum_Mapperly      | Enum       | 1000 |      3.425 ns |   0.1158 ns |   0.2517 ns |      3.348 ns |     ? | 0.0014 |      - |      24 B |
| Enum_Mapster       | Enum       | 1000 |     11.967 ns |   0.2321 ns |   0.2580 ns |     11.981 ns |     ? | 0.0014 |      - |      24 B |
| Enum_AutoMapper    | Enum       | 1000 |     75.937 ns |   1.3491 ns |   1.2619 ns |     75.689 ns |     ? | 0.0029 |      - |      48 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Flat_Hand          | Flat       | 1000 |      4.880 ns |   0.1363 ns |   0.1208 ns |      4.869 ns |  1.00 | 0.0024 |      - |      40 B |
| Flat_Dwarf         | Flat       | 1000 |      4.864 ns |   0.0940 ns |   0.0834 ns |      4.856 ns |  1.00 | 0.0024 |      - |      40 B |
| Flat_Mapperly      | Flat       | 1000 |      4.937 ns |   0.1415 ns |   0.1629 ns |      4.944 ns |  1.01 | 0.0024 |      - |      40 B |
| Flat_Mapster       | Flat       | 1000 |     14.024 ns |   0.2709 ns |   0.2782 ns |     13.985 ns |  2.88 | 0.0024 |      - |      40 B |
| Flat_AutoMapper    | Flat       | 1000 |     52.717 ns |   0.5727 ns |   0.4782 ns |     52.778 ns | 10.81 | 0.0024 |      - |      40 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Flatten_Dwarf      | Flatten    | 1000 |      5.321 ns |   0.1514 ns |   0.1969 ns |      5.325 ns |     ? | 0.0029 |      - |      48 B |
| Flatten_Mapperly   | Flatten    | 1000 |      5.345 ns |   0.1550 ns |   0.2016 ns |      5.305 ns |     ? | 0.0029 |      - |      48 B |
| Flatten_Mapster    | Flatten    | 1000 |     13.919 ns |   0.2649 ns |   0.2478 ns |     13.994 ns |     ? | 0.0029 |      - |      48 B |
| Flatten_AutoMapper | Flatten    | 1000 |     54.269 ns |   1.1290 ns |   1.1088 ns |     53.924 ns |     ? | 0.0029 |      - |      48 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| List_Dwarf         | List       | 1000 |  6,572.062 ns | 130.8345 ns | 145.4222 ns |  6,527.357 ns |     ? | 2.8763 | 0.4730 |   48112 B |
| List_Mapperly      | List       | 1000 |  6,142.409 ns |  90.6425 ns |  75.6906 ns |  6,162.587 ns |     ? | 2.8763 | 0.4730 |   48112 B |
| List_Mapster       | List       | 1000 |  5,925.313 ns | 118.4068 ns | 131.6088 ns |  5,898.939 ns |     ? | 2.8763 | 0.4730 |   48112 B |
| List_AutoMapper    | List       | 1000 |  8,765.346 ns | 155.4778 ns | 137.8271 ns |  8,743.787 ns |     ? | 3.3875 | 0.5341 |   56656 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Nested_Dwarf       | Nested     | 1000 |     11.688 ns |   0.2745 ns |   0.3051 ns |     11.704 ns |     ? | 0.0067 |      - |     112 B |
| Nested_Mapperly    | Nested     | 1000 |     11.619 ns |   0.2792 ns |   0.2742 ns |     11.605 ns |     ? | 0.0067 |      - |     112 B |
| Nested_Mapster     | Nested     | 1000 |     23.213 ns |   0.2454 ns |   0.2049 ns |     23.264 ns |     ? | 0.0067 |      - |     112 B |
| Nested_AutoMapper  | Nested     | 1000 |     62.339 ns |   1.2605 ns |   1.1174 ns |     61.908 ns |     ? | 0.0067 |      - |     112 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Seq_Dwarf          | Seq        | 1000 |  7,707.630 ns | 151.6362 ns | 197.1699 ns |  7,621.640 ns |     ? | 2.8687 | 0.4501 |   48088 B |
|                    |            |      |               |             |             |               |       |        |        |           |
| Widen_Dwarf        | Widen      | 1000 |    404.123 ns |   7.9718 ns |  11.4329 ns |    405.588 ns |     ? | 0.4811 |      - |    8048 B |
| Widen_Mapperly     | Widen      | 1000 |    517.833 ns |  10.0275 ns |  26.2402 ns |    516.566 ns |     ? | 0.4811 |      - |    8048 B |
| Widen_Mapster      | Widen      | 1000 |    735.880 ns |  14.6187 ns |  34.4580 ns |    723.760 ns |     ? | 0.4807 |      - |    8048 B |
| Widen_AutoMapper   | Widen      | 1000 |    781.813 ns |  15.6820 ns |  34.7502 ns |    774.367 ns |     ? | 0.4807 |      - |    8048 B |

`Seq` is new in this sweep (ISSUE-019): an `IEnumerable<T>` source into an array target, i.e. a count that is
not knowable at compile time. It allocates **48,088 B** against `Array_Dwarf`'s 48,048 B — a 40-byte delta,
confirming the runtime-count probe fills a **single** exactly-sized buffer rather than growing a `List<T>` and
copying it out. No competitor row: no other mapper in the suite is configured for this shape yet.

## NativeAOT sample

```
Vector.IsHardwareAccelerated = True ; Vector<int>.Count = 4
[1] SIMD widen boundary correctness (incl. negatives / sign extension)   PASS
[2] SIMD blit boundary correctness (struct reinterpret)                  PASS
[3] Preserve cycle determinism x100000                                   PASS
[4] Depth-guard catchable x20000                                         PASS
[5] OnCycle=SetNull acyclic projection x50000                            PASS
[5b] Ambient registry (module-init + IDwarfMapper) under NativeAOT       PASS
[6] Timing under NativeAOT (ns/op: median [min..max])
  Flat (1 obj)         8.6 [8.3..12.4]
  Array (1000)      9308.4 [8883.8..10124.8]
  Blit (1000)        474.7 [434.0..624.9]
  Widen (1000)       515.4 [480.9..737.4]
== AOT STABILITY: all correctness/determinism checks passed ==
```

The AOT binary reports `Vector<int>.Count = 4` (128-bit) where the JIT host reports `VectorSize=256`, so the
AOT `Widen` figure is on a narrower SIMD width and is not comparable with the JIT `Widen_Dwarf` row above.

**Toolchain note.** `dotnet publish -r win-x64` fails with `MSB3073 ... exited with code 123` when
`vswhere.exe` is not on `PATH`: ILCompiler cannot locate the MSVC linker. Fix by prepending
`C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`. This matters because the stale
`publish/` output from a previous run is left in place, so a failed publish followed by running the binary
silently reports **month-old** numbers — which is exactly what happened on the first attempt here (the stale
binary was dated 2026-06-27 and did not even contain check `[5b]`).
