<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: the `List<T>` element loop pays `Add`'s bookkeeping per element

**Found 2026-08-23**, while auditing every collection pathway the way round 25 audited the blit. Filed rather
than built: round 25 is closed, and this changes emission for a much wider set of shapes than round 25
touched, so it deserves its own safety analysis and test pass.

## What the audit found

Every other fill pathway is already optimal:

| pathway | emitted shape | verdict |
|---|---|---|
| array target | `new T[n]` + indexed writes | optimal — no bookkeeping |
| `Dictionary` | `new Dictionary<,>(src.Count)` + `__r[key] = value` | optimal — pre-sized, indexer is a single lookup-and-insert |
| `HashSet` | `new HashSet<T>(src.Length)` + `Add` | optimal — pre-sized, and a set has no span to write through |
| blit | one `Memmove` | optimal — verified at instruction level (see `benchmarks/results/2026-08-23-round25-kernels.md`) |
| **`List<T>`** | `new List<T>(n)` + **`Add` per element** | **the gap** |

The list IS pre-sized, so `Add` can never grow it — yet every element still pays `Add`'s full bookkeeping.
From the disassembly:

```asm
inc  dword ptr [rsi+14]   ; _version++          every element
mov  rdx,[rsi+8]          ; reload _items       every element
cmp  eax,ecx / jbe        ; capacity check      every element — CANNOT fail, we pre-sized
lea  eax,[rcx+1]
mov  [rsi+10],eax         ; _size++             every element
```

## Measured

BenchmarkDotNet, one run, N=1000, `Add`-loop as the declared baseline so the ratio is computed in-process.
Two runs reported — this round learned the hard way that a number is comparable only within its own run
composition.

| shape | `Add` loop | `SetCount` + span write | ratio |
|---|---|---|---|
| **value elements** (`Sv[] → List<Dv>`, int→long conversion) | 1,312 / 1,242 ns | **816 / 865 ns** | **1.61x / 1.44x** |
| reference elements (`Src[] → List<Dst>`, carries a string) | 5,320 / 4,808 | 5,312 / 5,210 | 1.00x / **0.92x** |

**The split is the finding.** For value elements the win is real and reproduces (roughly ten standard
deviations; SD 22–85 against a ~400 ns gap). For reference elements there is nothing to win — allocating a
thousand destination objects dominates, and the second run measured `SetCount` slightly *worse*.

## The proposal

Emit `CollectionsMarshal.SetCount` + span-indexed writes instead of `Add`, **only** when all of:

1. the target is the `List<T>` family (`List`, `IList`, `IReadOnlyList`, `ICollection`, `IReadOnlyCollection`);
2. **the element type is a value type** — measured, not assumed; reference elements gain nothing and may lose;
3. the source count is known up front (array `Length` or `List.Count`), which is already the pre-sizing condition;
4. it is the **create** path, not update-into — see below.

All four are compile-time facts, so the choice is made in the generator with no runtime branch.

## Safety analysis

`SetCount` makes the list report a `Count` covering memory nothing has written yet. Unlike the blit — where
nothing between `SetCount` and the copy can throw — the element mapper here is arbitrary generated code that
**can** throw: `CreateChecked` overflow, an unmapped enum value, a user converter.

That is acceptable **only on the create path**, and the reason is that the partially-filled list is a local
which is returned solely on success. If the mapper throws, the list is unreachable garbage and no caller ever
observes the default-valued tail.

It would NOT be acceptable if the list were caller-provided, which is why condition 4 excludes update-into:
there a throw would leave the caller holding a list whose tail is silently `default`. That distinction is the
whole safety argument and must be pinned by a test, not a comment.

## Why this may matter more than round 25's blit

Round 25's fast paths require *unmanaged* element types, and the corpus survey found they fire on 9 of 97
collection helpers — real DTOs carry strings. This applies to any `List<T>` destination with a value-type
element needing conversion: `int[] → List<long>`, enum collections under `ByName`, nullable conversions,
struct pairs that are layout-incompatible. Those are shapes that appear in ordinary business mapping, not
just in numeric or telemetry code.

Worth counting the qualifying sites in the corpus before committing to it — the same measurement that
correctly deflated round 25's expectations.
