# DwarfMapper.NET — design spec

**Date:** 2026-06-06
**Status:** Approved — feature-complete v1 (pre-release; not yet published to NuGet)
**Public artifact:** `README.md` (the design contract). This file records the locked decisions and the *why*.

## Problem

Existing C# mappers (AutoMapper, Mapster, Mapperly) optimize for ergonomics or raw speed. None treat the most common real-world failure — **forgetting to wire up a property** — as a compile error. That bug class is why teams hand-write fixtures and forward/back round-trip assertions and pay ongoing maintenance for them.

## Goal

A compile-time mapper where mislinking is structurally hard, maps are round-trip verified with near-zero maintenance, failures are self-explaining, and performance matches the source-gen floor (beating it on blittable data).

## Locked decisions

1. **Core mechanism — hybrid.** Canonical *sort* of members by resolved name → *ordinal pairing* (general path) → *auto-upgrade to blittable bulk/SIMD copy* when the generator can prove layout compatibility. Sorting buys (a) declaration-order-independent pairing that kills silent re-linking and (b) contiguous blittable runs for the fast-path.
2. **Pitch framing — resilience-first, speed second.** Headline is anti-mislinking + round-trip correctness (the user's actual pain). SIMD/blit is "fastest where physics allows," not the main claim. Honest stance: equal to Mapperly on ordinary DTOs (both emit direct assignments — the JIT floor), faster only on blittable collections.
3. **Security — all four interpretations:** zero reflection / AOT- & trim-safe (CI-gated); over-posting protection (only resolved members written; ambiguity = error); provably-safe blit (analyzer-gated, falls back to assignments if unprovable); compile-time completeness diagnostics (no silent data loss).
4. **Testing toolkit — separate package + analyzer hooks.** `DwarfMapper.Testing` (fixtures, seeded property-based fuzzer, round-trip verifier, informed dumps) plus generator-emitted metadata/hooks.
5. **Authoring model — declarative only.** Partial classes + partial methods (Mapperly mechanics) with attribute config (`[MapProperty]`, `[MapIgnore]`, `[MapProperty(Use=...)]`, `[Flatten]`, `[RoundTrip]`, `[BeforeMap]`, `[AfterMap]`). No runtime fluent config (AutoMapper-style ergonomics expressed declaratively to stay AOT-safe). Note: `[MapWith]` does NOT exist; custom per-member conversion is `[MapProperty(source, target, Use = nameof(Method))]`.
6. **Scope — v1 shipped.** All features documented in the README (flat/nested/collection mapping, enums, null handling, flattening, hooks, IQueryable projection, blittable fast-path, RoundTrip, DwarfMapper.Testing) are built and covered by tests. Remaining planned items: `MapTo`/span zero-alloc overloads, async mapping, in-repo benchmarks, NuGet publish.
7. **License — GPL-2.0-only** (`SPDX: GPL-2.0-only`), no commercial tier. Strong copyleft chosen deliberately: because DwarfMapper is a source generator that emits into the consumer's assembly, GPLv2 makes downstream distributed apps GPLv2 too — the intended "free, and profit happens in the open" stance. Known trade-offs accepted: SaaS/hosted use is not distribution so it does not trigger disclosure (AGPL was explicitly declined); GPLv2-only is incompatible with Apache-2.0/GPLv3 (non-issue given zero dependencies). LICENSE file + per-file SPDX headers required.

## Architecture

- **`DwarfMapper`** (`net10.0`) — attributes + abstractions, zero deps. (Project is .NET 10 only.)
- **`DwarfMapper.Generator`** (`netstandard2.0`) — Roslyn incremental generator + analyzers/diagnostics.
- **`DwarfMapper.Testing`** (`net10.0`) — fixtures, fuzzer, round-trip, informed dumps, xUnit/NUnit theory sources.
- Repo: `src/` (DwarfMapper, DwarfMapper.Generator, DwarfMapper.Testing), `tests/` (DwarfMapper.Generator.Tests, DwarfMapper.IntegrationTests, DwarfMapper.Testing.Tests), `samples/` (DwarfMapper.AotSample). No BenchmarkDotNet project exists in-repo (benchmarks are planned).

## Pipeline

`sort` (canonical resolved-name order) → `pair` (ordinal) → `prove` (completeness gate = build error; blit proof = unmanaged + size + layout + transform-free) → `emit` (direct assignments + inline converters + null guards; bulk `MemoryMarshal.Cast`/`Unsafe.CopyBlock`/SIMD for proven runs).

## Anti-mislinking features

- Completeness diagnostics — unmapped destination members error by default (configurable severity).
- `[RoundTrip]` — generator emits a fuzz-driven `Back(Forward(x)) ≡ x` harness consumed by `DwarfMapper.Testing`.
- Informed dumps — mapping-graph-aware diff: diverging member, src/dest values, resolved path, repro seed.

## Performance guarantees

0 allocation in the mapper; bulk/SIMD on blittable collections. `MapTo(src, ref dest)` and span overloads for zero-alloc into caller memory are **planned** (not yet implemented). In-repo BenchmarkDotNet suite is **planned** (not yet implemented).

## Implementation status notes

- Diagnostic ID scheme and default severities: **shipped** (DWARF001–DWARF023 defined in DiagnosticDescriptors.cs; all errors by default; no per-mapper severity configuration — completeness is always a build error by design).
- Attribute surface and conflict rules: **shipped** (`[MapProperty]`, `[MapIgnore]`, `[Flatten]`, `[BeforeMap]`, `[AfterMap]`, `[RoundTrip]`, `[Reinterpret]`).
- Structural-equality float tolerance: **shipped** — `StructuralComparer` uses `FloatEpsilon = 1e-9` for `float`/`double` comparisons (see `src/DwarfMapper.Testing/StructuralComparer.cs`).
- Fixture generation strategy: **shipped** (seeded `ObjectFactory.Create<T>` + `Fuzzer.Generate<T>` in DwarfMapper.Testing).
- Blit-proof rules matrix: **shipped** (unmanaged + Sequential + same Pack + matching ordered field names/types, recursive; `[Reinterpret]` escape hatch with size guard).
- **Built-in scalar conversion matrix (Plan 17, shipped):** Implicit/identity (zero-cost direct assign); integral narrowing via `INumberBase<T>.CreateChecked` (throws `OverflowException`, never silent truncation); `string → T` via `IParsable<T>.Parse(v, InvariantCulture)` (loud on bad input); `T → string` via `IFormattable.ToString(null, InvariantCulture)` (culture-stable). Enum↔integral and enum↔enum ByValue also use `CreateChecked`. Float/decimal → integral is NOT auto-handled (requires `[MapProperty(Use=...)]` to avoid silent fraction loss). All emitted calls are concrete static/instance invocations — reflection-free, AOT/trim-safe.
