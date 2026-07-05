# Benchmark results — Flat & Blit (re-measured 2026-07-05)

Reproduce: `dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks -- --anyCategories Flat Blit`

```
BenchmarkDotNet v0.14.0, Pop!_OS 24.04 LTS
AMD Ryzen 5 5600, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.25.52411), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.0 (10.0.25.52411), X64 RyuJIT AVX2


| Method          | Categories | N    | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|---------------- |----------- |----- |-------------:|-----------:|-----------:|------:|--------:|-------:|-------:|----------:|------------:|
| Blit_Dwarf      | Blit       | 1000 |   589.592 ns | 11.3237 ns | 11.6286 ns |     ? |       ? | 0.7210 | 0.0305 |   12048 B |           ? |
| Blit_Mapperly   | Blit       | 1000 | 1,081.140 ns | 12.1360 ns | 11.3520 ns |     ? |       ? | 0.7210 | 0.0305 |   12048 B |           ? |
| Blit_Mapster    | Blit       | 1000 | 1,106.276 ns | 10.5641 ns |  9.8817 ns |     ? |       ? | 0.7210 | 0.0305 |   12048 B |           ? |
| Blit_AutoMapper | Blit       | 1000 | 1,180.931 ns | 21.2169 ns | 20.8378 ns |     ? |       ? | 0.7210 | 0.0305 |   12048 B |           ? |
|                 |            |      |              |            |            |       |         |        |        |           |             |
| Flat_Hand       | Flat       | 1000 |     6.729 ns |  0.1777 ns |  0.2311 ns |  1.00 |    0.05 | 0.0024 |      - |      40 B |        1.00 |
| Flat_Dwarf      | Flat       | 1000 |     6.691 ns |  0.1805 ns |  0.1854 ns |  1.00 |    0.04 | 0.0024 |      - |      40 B |        1.00 |
| Flat_Mapperly   | Flat       | 1000 |     6.915 ns |  0.1857 ns |  0.1824 ns |  1.03 |    0.04 | 0.0024 |      - |      40 B |        1.00 |
| Flat_Mapster    | Flat       | 1000 |    15.844 ns |  0.1408 ns |  0.1176 ns |  2.36 |    0.08 | 0.0024 |      - |      40 B |        1.00 |
| Flat_AutoMapper | Flat       | 1000 |    56.386 ns |  0.4033 ns |  0.3772 ns |  8.39 |    0.28 | 0.0024 |      - |      40 B |        1.00 |

// * Warnings *
BaselineCustomAnalyzer
```
