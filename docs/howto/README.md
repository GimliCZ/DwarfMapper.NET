<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: swapping your project over to DwarfMapper

Task-oriented walkthroughs for moving a **working** codebase from another mapper to DwarfMapper.
Each guide is a do-this-then-that sequence with before→after snippets, the build errors you should
expect along the way, and how to prove the swap was lossless.

These guides are deliberately *practical*. For the exhaustive references they lean on, see:

- [`../MIGRATION.md`](../MIGRATION.md) — every feature/mechanic of AutoMapper 14 / Mapster / Mapperly mapped 1:1 to its DwarfMapper equivalent (with divergence notes).
- [`../COMPARISON.md`](../COMPARISON.md) — capability matrix, testing-approach comparison, ceremony scorecard, and benchmarks.
- [`../CORRECTNESS.md`](../CORRECTNESS.md) — what DwarfMapper guarantees and which test/diagnostic enforces each guarantee.

---

## Which guide do I need?

Answer three questions:

**1. What are you mapping with today?**

| You're on… | Start here |
|---|---|
| AutoMapper (`Profile`, `CreateMap`, `IMapper`) | [migrate-from-automapper.md](migrate-from-automapper.md) |
| Mapster (`src.Adapt<T>()`, `TypeAdapterConfig`) | [migrate-from-mapster.md](migrate-from-mapster.md) |
| Mapperly (`[Mapper] partial class`) | [migrate-from-mapperly.md](migrate-from-mapperly.md) |
| Hand-written mapping methods / no library | [migrate-from-handwritten.md](migrate-from-handwritten.md) |

**2. New to all of them?** Read [common-changes.md](common-changes.md) first — it covers the five things that
change *regardless* of which library you're leaving (the partial-class model, the deleted config/DI layer,
the `DWARF001` completeness wall, the call-site change, and what AOT/trimming gives you for free). Every
per-library guide assumes you've skimmed it.

**3. Care about deployment, AOT, trimming, package size, or the GPL obligation?**
Read [deploy-and-optimize.md](deploy-and-optimize.md) — the deployment-and-efficiency guide.

---

## The five-minute version

Every migration, from every library, is the same three moves (detailed in [common-changes.md](common-changes.md)):

1. **Reference one package** (`DwarfMapper` — it bundles the generator) and bump the consuming project to `net10.0`.
2. **Collapse your runtime config into one `partial class`** — `[DwarfMapper]` on the class, one
   `[GenerateMap<Src,Dst>]` line (or one `partial` method) per pair. POCOs stay attribute-free.
3. **Fix the build errors.** DwarfMapper turns "I forgot to map a property" into a compile error
   (`DWARF001`). Map it or `[MapIgnore]` it. When the build is green, the swap is done — and provably complete.

```diff
- // AutoMapper
- var cfg = new MapperConfiguration(c => c.CreateMap<Order, OrderDto>());
- var dto = cfg.CreateMapper().Map<OrderDto>(order);
+ // DwarfMapper
+ [DwarfMapper]
+ [GenerateMap<Order, OrderDto>]
+ public partial class Mappers { }
+ var dto = new Mappers().Map(order);
```

---

## Status note

DwarfMapper is **pre-release — `1.0.0-rc.1`, a release candidate.** Install it with the `--prerelease`
flag (`dotnet add package DwarfMapper --prerelease`). See the repository [README](../../README.md#status).
