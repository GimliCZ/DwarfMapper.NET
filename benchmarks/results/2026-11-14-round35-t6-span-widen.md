# Round 35 T6 — a widening span map takes the `Vector.Widen` loop (2026-11-14)

AMD Ryzen 5 5600 (6 cores / 12 threads), Windows 10 22H2, .NET 10.0.11, X64 RyuJIT **AVX2**, BenchmarkDotNet 0.14.0
**DefaultJob**, under the heavy-test lock, nothing else running. **This is a Windows/AVX2 measurement: read the ratios
as this machine's, not as a portable claim** (the Dict-vs-Mapperly figure, 2.13x on Windows and ~1.14x on Linux, is
how far a Windows ratio can travel).

## Why

`int[] → long[]` has widened with `Vector.Widen` since round 22, and round 33 (T02) gave the List and ImmutableArray
shapes the same loop. The span endpoint, `void Map(ReadOnlySpan<int> s, Span<long> d)`, kept the plain element loop
`for (int __i = 0; __i < s.Length; __i++) d[__i] = s[__i];` - the shape the README offers for hot paths over pooled
memory. T6: measure the scalar loop against the vectorised one, vectorise if it pays.

## First look (one-minute probe, Stopwatch, best of seven, three rounds)

Span to span, the generated scalar loop against the `Vector.Widen` loop the List shape emits, ratio = scalar / vector:

| N      | int → long | float → double | byte → ushort |
|-------:|-----------:|---------------:|--------------:|
| 16     | 1.23-1.33x | 2.28-2.32x     | 0.66-0.95x    |
| 64     | 1.36-1.39x | 3.52-3.55x     | 3.68-3.79x    |
| 1,024  | 1.43-1.44x | 4.05-4.16x     | 5.69-5.86x    |
| 16,384 | 1.36-1.41x | 3.82-3.96x     | 5.49-5.80x    |

A variant that keeps spans shorter than one vector on the plain loop (`src.Length >= Vector<T>.Count` around the vector
path) read 0.84x for byte → ushort at 16, inside the same noise: it buys nothing, so the emitted loop is the List
shape's, unguarded, as the array and List shapes have it.

## A/B, one process, one payload (`Round35SpanWidenBenchmarks`)

`*_Before` is the span map as emitted before this change, verbatim; `*_After` calls the generated mapper. Nothing is
allocated by either arm (the caller owns both spans).

| Method               | N    |       Mean |   StdDev | Allocated |
|--------------------- |----- |-----------:|---------:|----------:|
| IntToLong_Before     | 16   |   7.369 ns | 0.015 ns |         - |
| IntToLong_After      | 16   |   3.121 ns | 0.004 ns |         - |
| FloatToDouble_Before | 16   |   9.681 ns | 0.002 ns |         - |
| FloatToDouble_After  | 16   |   3.572 ns | 0.003 ns |         - |
| ByteToUShort_Before  | 16   |   5.930 ns | 0.479 ns |         - |
| ByteToUShort_After   | 16   |   6.489 ns | 0.312 ns |         - |
| IntToLong_Before     | 1024 | 238.769 ns | 0.165 ns |         - |
| IntToLong_After      | 1024 | 152.680 ns | 0.137 ns |         - |
| FloatToDouble_Before | 1024 | 692.695 ns | 0.111 ns |         - |
| FloatToDouble_After  | 1024 | 153.632 ns | 0.112 ns |         - |
| ByteToUShort_Before  | 1024 | 236.501 ns | 0.156 ns |         - |
| ByteToUShort_After   | 1024 |  39.432 ns | 0.046 ns |         - |

Before → After (mean):

| Pair             | N = 16           | N = 1,024 |
|------------------|-----------------:|----------:|
| `int → long`     | 2.36x            | **1.56x** |
| `float → double` | 2.71x            | **4.51x** |
| `byte → ushort`  | 0.91x (+0.56 ns) | **6.00x** |

Every pair clears round 33's threshold (at least 1.3x at n = 1,024). The one cost: a byte-sized pair shorter than one
vector (32 elements under AVX2) never enters the vector loop and pays about half a nanosecond for the path around it,
with the two standard deviations overlapping. The array and List shapes have made the same trade since rounds 22 and
33. `float → double` gains most because the scalar conversion is a real instruction per element, which the vector form
does eight at a time; `int → long` is closest to a copy and gains least.

## What holds it

- `SpanMapWidenTests` (generator): each of the seven `Vector.Widen` pairs emits the loop and compiles clean; a mutable
  source span too; two-step widenings, a change of kind, nullable elements and same-size pairs keep what they had.
- `SpanMapWidenRuntimeTests` (runtime): every pair, every length from 0 to 70 and seven longer ones, random bits for
  the source - element for element the scalar widening, and nothing past the source's end written; a short
  destination is still refused before anything is written; nothing is allocated. Run natively and under
  `DOTNET_EnableHWIntrinsic=0`, `DOTNET_PreferredVectorBitWidth=128` and `DOTNET_EnableAVX2=0`, the configurations
  `scripts/isa-matrix.ps1` runs in CI.
