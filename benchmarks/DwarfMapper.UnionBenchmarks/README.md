<!-- SPDX-License-Identifier: GPL-2.0-only -->

# DwarfMapper.UnionBenchmarks

The performance of C# 15 union mapping (round 35, T24). It needs the **.NET 11 SDK**: unions are C# 15, and the
generator build that maps them is the Roslyn 5.12 one. So this project is not in `DwarfMapper.NET.sln` (which builds
on the pinned .NET 10 SDK), and its numbers are not in `allocation-baseline.json` (pinned per SDK, on that one).

| Class | Category | Arms |
|---|---|---|
| `PaymentBenchmarks` | `One payment` | a union of three record cases, one value per call from a ring of 512 distinct draws |
| `PaymentBenchmarks` | `Batch of 1000` | a `List` of 1,000 of them, mapped whole |
| `ReadingBenchmarks` | `One reading`, `Batch of 1000` | a union with value-type cases (`int` widened to `long`, `double`, `string`) |

The payment arms map the same data in both shapes a model can give it:

- `Union_Hand` (the baseline) and `Union_DwarfMapper`: the C# 15 union. `Union_HandCaseMethods` is the control: the
  same switch, hand-written in the generated code's structure (a public method per case, each checking its argument),
  so the part of DwarfMapper's cost that is that structure shows on its own.
- `Hierarchy_Hand`, `Hierarchy_DwarfMapper`, `Hierarchy_Mapperly`, `Hierarchy_Mapster`: an abstract base record and
  three derived records. Mapperly and Mapster appear on this shape only, because neither maps a union. What each of
  them, and AutoMapper, does with one is in the results document.

The payloads come from `ObjectFactoryV2`, like the main suite's: nulls below the root, boundary numbers, empty and
varied strings. The case mix is 60 % / 30 % / 10 % in a seeded order. One draw feeds both shapes, so a difference
between the shapes is the shape's.

## Running

From this folder (its `global.json` selects SDK 11):

```bash
dotnet run -c Release -- dry      # every arm once on every payload, results compared with the hand-written arm
dotnet run -c Release             # the whole comparison
dotnet run -c Release -- --filter '*PaymentBenchmarks*'
```

`dry` prints one `UNION-BENCH-DRY ok|WRONG` line per comparison and exits non-zero if any arm maps differently. Run
it before trusting a number: a fast arm that drops a case is wrong, not fast.

Results: [`../results/2026-11-14-round35-t24-union-mapping.md`](../results/2026-11-14-round35-t24-union-mapping.md).
