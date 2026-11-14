<!-- SPDX-License-Identifier: GPL-2.0-only -->

# C# 15 union mapping, runnable

Five examples of mapping C# 15 unions with DwarfMapper. They need the **.NET 11 SDK**: C# 15, and the Roslyn 5.12
compiler that loads the generator build which maps unions. This folder's `global.json` selects SDK 11, so:

```
cd samples/DwarfMapper.Unions
dotnet run
```

Each example checks its own result, and the program prints one `DWARF-UNION-SAMPLE ok ...` line per example (and
exits non-zero on a wrong value).

| # | File | Shows |
|---|---|---|
| 01 | [`01_UnionToUnion.cs`](01_UnionToUnion.cs) | one union into another, each case paired by the declared method that maps it |
| 02 | [`02_ExplicitArms.cs`](02_ExplicitArms.cs) | `[MapDerivedType]` arms on the union map, and a case both unions share passed through |
| 03 | [`03_ValueCases.cs`](03_ValueCases.cs) | value-type cases, named with the `typeof` form |
| 04 | [`04_UnionMembers.cs`](04_UnionMembers.cs) | a union member, a list of unions, a case-typed member into a union member |
| 05 | [`05_EmptyUnion.cs`](05_EmptyUnion.cs) | the empty union, and a nullable union member keeping null apart from it |

The walkthrough is [Map C# 15 unions](../../docs/howto/map-unions.md).

The project references the sources in `src/` by default. CI runs it from the packed package instead
(`-p:UsePackedDwarfMapper=true -p:DwarfMapperPackageVersion=<v> -p:DwarfMapperPackageSource=<dir>`), so that the SDK
picks the generator build out of the package's Roslyn-versioned analyzer folders itself, as it does for a consumer.
This project is not in `DwarfMapper.NET.sln`, which builds on the pinned .NET 10 SDK.
