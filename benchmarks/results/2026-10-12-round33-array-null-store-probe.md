# Round 33 probe — the array path's null-element cost is the STORE, not the test (2026-10-12)

AMD Ryzen 5 5600, Windows 10 22H2, .NET 10.0.11, X64 RyuJIT AVX2, BenchmarkDotNet 0.14.0 DefaultJob, one process, one
payload draw (`RealisticPayloads.Elements<FlatSrc>(1000, 3)`). The machine was in interactive use (StdDev ~5 %).

## What was already settled, and not re-measured

`NullCheckProbeBenchmarks` (2026-09-09) put DwarfMapper's `FlatSrc[] → FlatDst[]` gap to Mapperly at ~9 % and
localised it to the per-element null test; the callee's `ThrowIfNull` costs nothing. The test is a decided behaviour —
a null element maps to null, where Mapperly's would throw (`6fa7308`, `Issues/round30/FINDING-array-null-ternary.md`).
That finding concluded "there is no version of removing it that keeps the behaviour".

## The one new arm

`SkipNullStore` keeps the test and the behaviour (a new array is all-null) but moves the store inside it, so every
non-null store writes the call's result instead of a merged `?:` value.

| Method        | Mean     | StdDev   | Ratio | Allocated |
|---------------|---------:|---------:|------:|----------:|
| Current       | 5.936 µs | 0.272 µs |  1.00 | 46.9 KB   |
| SkipNullStore | 5.277 µs | 0.246 µs |  0.89 | 46.9 KB   |
| Unguarded     | 5.665 µs | 0.093 µs |  0.96 | 46.9 KB   |

`Current` is the emitted loop verbatim; `Unguarded` is Mapperly's shape (no null test — not DwarfMapper's semantics).

## What to believe

- The gap is mostly the store shape: keeping the test and storing the call result directly is 11 % faster than the
  emitted loop, and faster even than the test-free loop — plausibly because, after the test, the JIT knows the element
  is non-null and can drop the element mapper's own `ThrowIfNull` once inlined.
- **Decision pending.** 1.12x is below round 31's bar (≥ 1.3x, consistently). Before any emission change it needs
  confirming at 100 and 10,000 elements on a quiet machine, as the 2026-09-09 sweep was.
