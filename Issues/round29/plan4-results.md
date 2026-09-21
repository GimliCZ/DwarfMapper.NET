```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19045.6332/22H2/2022Update)
AMD Ryzen 5 5600, 1 CPU, 12 logical and 6 physical cores
Frequency: 14318180 Hz, Resolution: 69.841 ns, Timer: HPET
.NET SDK 10.0.101
  [Host] : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX2
  short  : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX2

Job=short  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=span  

```
| Method              | N      | Mean       | Error        | StdDev     | Ratio | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|-------------------- |------- |-----------:|-------------:|-----------:|------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| **Gather_Sequential**   | **1000**   |   **3.110 μs** |     **3.858 μs** |  **0.2115 μs** |  **1.00** |    **0.08** |   **1.9073** |        **-** |        **-** |   **31.27 KB** |        **1.00** |
| SpanCopy_Sequential | 1000   |   2.118 μs |     1.913 μs |  0.1048 μs |  0.68 |    0.05 |   1.9073 |        - |        - |   31.27 KB |        1.00 |
| SpanCopy_Auto       | 1000   |   2.393 μs |     3.113 μs |  0.1707 μs |  0.77 |    0.06 |   1.9073 |        - |        - |   31.27 KB |        1.00 |
|                     |        |            |              |            |       |         |          |          |          |            |             |
| **Gather_Sequential**   | **100000** | **547.339 μs** |    **45.064 μs** |  **2.4701 μs** |  **1.00** |    **0.01** | **143.5547** | **143.5547** | **143.5547** | **3125.06 KB** |        **1.00** |
| SpanCopy_Sequential | 100000 | 548.228 μs |   248.653 μs | 13.6295 μs |  1.00 |    0.02 | 134.7656 | 134.7656 | 134.7656 | 3125.06 KB |        1.00 |
| SpanCopy_Auto       | 100000 | 651.293 μs | 1,239.901 μs | 67.9632 μs |  1.19 |    0.11 | 131.8359 | 131.8359 | 131.8359 | 3125.06 KB |        1.00 |
