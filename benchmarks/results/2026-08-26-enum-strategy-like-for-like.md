# Enum mapping, compared like-for-like — 2026-08-26

**The headline: the published "DwarfMapper is 3.98x slower than Mapperly on enums" was an artifact of the
benchmark, not a property of the generator.** Measured on equal terms the gap is 1.6 % in one direction and
reversed in the other, and the finding that was filed against our emission is withdrawn.

## What was wrong

`docs/COMPARISON.md` headed the row **"Enum (scalar member, by name)"** and printed Mapperly at 3.3 ns
against DwarfMapper's 13.0 ns. The gap was real. The label was not.

Enum mapping has two legitimate strategies, and the four libraries do not agree on a default:

| library | default strategy |
|---|---|
| DwarfMapper | **by name** |
| AutoMapper 14.0.0 | **by name** |
| Mapperly 4.3.1 | by value |
| Mapster 10.0.8 | by value |

Every library sat at its default, so the row compared a `switch` over declared member names against a raw
`(EnumDst)s.Status` cast. Two of the three columns DwarfMapper was measured against were answering a
different question, and the difference was published as a deficiency in our emission.

They do not even agree on the answer. The benchmark's enums are deliberately reordered —
`BenchStatus {Pending=0, Active=1, Closed=2}` against `BenchStatusDto {Closed=0, Pending=1, Active=2}` — so a
value cast maps `Pending` to `Closed`. Roughly a 4x "win" was partly the price of being wrong.

## The fix

Not "force everyone to by-name" — that makes one comparison honest and leaves the whole by-value class
unmeasured. Each strategy gets its own category, and every library appears where it can actually be
configured. DwarfMapper opts into the cast with `EnumStrategy.ByValue`, which is a supported documented
option (the same switch that lets an enum array take the blit fast path), not a benchmark-only contrivance.

## Measured

BenchmarkDotNet v0.14.0, DefaultJob, .NET 10.0.1 X64 RyuJIT AVX2, AMD Ryzen 5 5600 (12 logical / 6 physical),
Windows 10 22H2, non-dedicated machine. Payloads drawn from a 512-entry fixture ring, per the no-static-
payloads rule. One run, `--filter *Enum*`, 166 s.

| Method | Category | Mean | Error | StdDev | Allocated |
|---|---|---:|---:|---:|---:|
| `Enum_Dwarf` | EnumByName | 12.505 ns | 0.1177 | 0.1044 | 24 B |
| `Enum_Mapperly` | EnumByName | **12.304 ns** | 0.0698 | 0.0583 | 24 B |
| `Enum_AutoMapper` | EnumByName | 73.637 ns | 1.4438 | 1.4180 | 48 B |
| `EnumByValue_Dwarf` | EnumByValue | **2.865 ns** | 0.0322 | 0.0269 | 24 B |
| `EnumByValue_Mapperly` | EnumByValue | 3.010 ns | 0.0423 | 0.0375 | 24 B |
| `EnumByValue_Mapster` | EnumByValue | 12.263 ns | 0.0816 | 0.0723 | 24 B |

### Reading it

* **By name, we are level with Mapperly** — 12.505 against 12.304 ns, a 1.6 % difference near enough the
  run-to-run spread to carry no meaning. The earlier claim that the switch was the problem does not survive:
  told to walk names, Mapperly pays the same. AutoMapper, also by name, takes 73.6 ns.
* **By value, we are first** — 2.865 ns against Mapperly's 3.010 and Mapster's 12.263. Mapster performs a
  cast and still measures like a switch, because its per-call dispatch dominates what the cast costs.
* **Every row allocates 24 B** (AutoMapper 48 B), so all six include a destination allocation. That is why a
  three-arm switch reads as ~10 ns of delta rather than ~1 ns: the strategy sits on top of an allocation, and
  a 512-entry ring keeps the branch genuinely unpredictable.

## Why the numbers moved from the last sweep

The by-name pair is unchanged in substance (12.97 → 12.505 ns for DwarfMapper is run-to-run drift on a
non-dedicated machine). What changed is Mapperly's column: 3.3 ns → 12.3 ns, because it is now being asked
to do the same work. Nothing was optimised between the two runs.

## The semantics are executable, not asserted

`tests/DwarfMapper.DifferentialTests/EnumOrderSensitivityTests.cs` maps divergently ordered enums through all
four libraries and pins what each returns, including DwarfMapper under both strategies. It exists because
writing it corrected two confident guesses:

* **AutoMapper** — I expected by value, reasoning from the ledger sentence "AutoMapper maps enums by VALUE".
  That sentence is about an *undefined* value passing through as a raw number; it is not a claim about
  defined members, which AutoMapper 14 matches by name.
* **Mapster** — I expected by name, because 12.3 ns looks switch-shaped. It is a cast.

Both guesses were plausible and both were wrong. The table now rests on the tests rather than on inference.

## Consequences recorded elsewhere

* `docs/COMPARISON.md` — the single Enum row is now two, and the `§` footnote is rewritten. The previous
  "improving this is filed, not fixed" is **withdrawn**: the deficiency it named did not exist.
* By-name safety is a **default, not a tax** — `EnumStrategy.ByValue` is a one-line opt-out, and taking it
  puts DwarfMapper first in its class.
