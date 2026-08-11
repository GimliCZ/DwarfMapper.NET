# Dict re-measurement on Windows (2026-08-11) — the platform split is real

This is the Windows run that `2026-07-25-realistic-payloads-comparable.md` and `CLAUDE.md` have been waiting
for. **It settles the question, and not in the direction the correction banner assumed.**

## The question

The 2026-07-25 Windows run called the `Dict` row "the headline" at a ~2× lead over Mapperly. Four Linux
re-measurements put that lead at ~1.14×. `Dict_Dwarf` reproduced across platforms; `Dict_Mapperly` did not
(20,050 ns on Windows against 11.37 µs on Linux). A Mapperly version change, a benchmark change and a
difference in work done were all ruled out. What was left was a straight either/or that no amount of Linux
measurement could decide: **was the Windows number an outlier, or a genuine Windows characteristic?**

## Result

```
BenchmarkDotNet v0.14.0 ; Windows 10 Pro 10.0.19045
Runtime=.NET 10.0.1 (10.0.125.57005), X64 RyuJIT AVX2 ; GC=Concurrent Workstation ; Job: DefaultJob
SDK 10.0.101 (global.json pin) ; Riok.Mapperly 4.3.1 ; Mapster 10.0.8 ; AutoMapper 14.0.0
Command: dotnet run -c Release -- --anyCategories Dict
```

| Method          | N    | Mean      | Error     | StdDev    | Gen0   | Gen1   | Allocated |
|---------------- |----- |----------:|----------:|----------:|-------:|-------:|----------:|
| Dict_Dwarf      | 1000 |  9.315 µs | 0.1831 µs | 0.3393 µs | 1.8463 | 0.1831 |  30.39 KB |
| Dict_Mapperly   | 1000 | 19.801 µs | 0.3934 µs | 0.5515 µs | 1.8616 | 0.1831 |  30.45 KB |
| Dict_Mapster    | 1000 | 28.894 µs | 0.5728 µs | 0.7448 µs | 6.1035 | 1.0071 |  99.98 KB |
| Dict_AutoMapper | 1000 | 21.269 µs | 0.4197 µs | 0.7780 µs | 6.1035 | 1.0071 |  99.92 KB |

## What it means

**The Windows figure reproduces almost exactly, seventeen days and one SDK pin later.**

| | 2026-07-25 (Windows) | 2026-08-11 (Windows) | Linux (4 runs) |
|---|---|---|---|
| `Dict_Dwarf`    | 9,964 ns  | 9,315 ns  | ~10.10 µs |
| `Dict_Mapperly` | 20,050 ns | 19,801 ns | 11.37 µs |
| ratio           | ~2.01×    | **2.13×** | ~1.14× |

So it was **not an outlier**. `Dict_Mapperly` really does cost ~1.74× more on this Windows host than on the
Linux host, while `Dict_Dwarf` lands within ~7% on both. Every previously-ruled-out explanation stays ruled
out: same pinned Mapperly 4.3.1, same benchmark, and allocation is within 0.2% of Linux on both rows — both
implementations genuinely copy and convert the same amount.

**The honest headline is therefore platform-qualified, not retracted.** "~2× faster than Mapperly on Dict" is
true on this Windows host and false on the Linux host; "~1.14×" is the reverse. Quoting either as *the*
number without naming the platform is what was wrong — not the measurement.

What is *not* platform-dependent, and remains the stronger half of the row:

- **Allocation: 30.39 KB against 99.98 KB — 3.29× less than Mapster and AutoMapper.** Load-independent, and
  it reproduces on both platforms.
- **Allocation parity with Mapperly** (30.39 vs 30.45 KB), which is what makes the throughput comparison
  meaningful at all: the two are doing the same work, so the time difference is not explained away by one
  of them allocating less.

## Recommendation

Cite the allocation figure unqualified. If a throughput number is quoted for `Dict`, name the platform and
give both, e.g. "1.1–2.1× faster than Mapperly depending on host (Linux/Windows)". `README.md` and
`docs/COMPARISON.md` do not currently claim the 2× figure, so nothing there needs changing.

The remaining open question is *why* Mapperly's dictionary path is slower on Windows. That is a question
about Mapperly, not about DwarfMapper, and nothing here depends on the answer.
