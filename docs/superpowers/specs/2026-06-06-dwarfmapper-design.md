# DwarfMapper.NET — design spec

**Date:** 2026-06-06
**Status:** Approved (design phase)
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
5. **Authoring model — declarative only.** Partial classes + partial methods (Mapperly mechanics) with attribute config (`[MapProperty]`, `[MapIgnore]`, `[MapWith]`, `[Flatten]`, `[RoundTrip]`). No runtime fluent config (AutoMapper-style ergonomics expressed declaratively to stay AOT-safe).
6. **Scope — Rich documented, core built first.** README documents the full Rich surface; projection/hooks/flattening/async are explicitly v2 on the roadmap because they are heaviest to prove correct.

## Architecture

- **`DwarfMapper`** (`netstandard2.0`) — attributes + abstractions, zero deps.
- **`DwarfMapper.Generator`** (`netstandard2.0`) — Roslyn incremental generator + analyzers/diagnostics.
- **`DwarfMapper.Testing`** (`net10.0`) — fixtures, fuzzer, round-trip, informed dumps, xUnit/NUnit theory sources.
- Repo: `src/`, `tests/` (incl. BenchmarkDotNet), `samples/`.

## Pipeline

`sort` (canonical resolved-name order) → `pair` (ordinal) → `prove` (completeness gate = build error; blit proof = unmanaged + size + layout + transform-free) → `emit` (direct assignments + inline converters + null guards; bulk `MemoryMarshal.Cast`/`Unsafe.CopyBlock`/SIMD for proven runs).

## Anti-mislinking features

- Completeness diagnostics — unmapped destination members error by default (configurable severity).
- `[RoundTrip]` — generator emits a fuzz-driven `Back(Forward(x)) ≡ x` harness consumed by `DwarfMapper.Testing`.
- Informed dumps — mapping-graph-aware diff: diverging member, src/dest values, resolved path, repro seed.

## Performance guarantees

0 allocation in the mapper; `MapTo(src, ref dest)` + span overloads for zero-alloc into caller memory; bulk/SIMD on blittable collections; in-repo BenchmarkDotNet vs. Mapperly/Mapster/AutoMapper.

## Open items for implementation planning

- Diagnostic ID scheme and default severities.
- Exact attribute surface and conflict rules (e.g., `[MapProperty]` + `[Flatten]` precedence).
- Structural-equality contract used by round-trip (custom comparers, float tolerance).
- Fixture generation strategy (defaults + edge values from generated metadata).
- Blit-proof rules matrix (which type shapes qualify) and fallback diagnostics.
