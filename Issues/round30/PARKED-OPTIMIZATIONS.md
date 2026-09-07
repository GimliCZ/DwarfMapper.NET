# Parked optimizations — measured, not forgotten

Round 29 evaluated every strategy in `Issues/round29/RESEARCH-hardware-mode.md` §§4–8. Most were shipped or
rejected outright. This file holds the ones that were **measured, found real but not worth their cost**, so
that a later round reopens them on evidence rather than rediscovering them from scratch — and does not
re-propose them without meeting the bar that parked them.

## 16-byte permuted blit (§8b)

**Measured:** 0.77× at N = 1,000, 0.95× at N = 100,000 — `XYZW` → `WZYX` as a single byte shuffle. The only
permutation in the study that pays at all.

**Why it is parked, in the research's own words:** *"the only permutation that pays, and only 23 % at 1k —
not enough to justify a new emission path with its own proof, near-miss text and mutation surface. 8b is
dropped except as a measured, gated future item for ≥16-byte layouts."*

**Reopen only if all three hold:**

1. A consumer shape is shown where permuted 16-byte layouts dominate a hot path — the 23 % is real but it is
   23 % of the copy, not of the mapping.
2. The win survives a proper sweep. Every figure here is two points; the 2026-09-07 six-decade sweep
   (`Issues/round29/sweep-results.md`) overturned two conclusions drawn from two points, in both directions.
3. The cost side is counted honestly: a new emission path in this repository is not just code. It owes a
   generation-time proof, near-miss diagnostic text, a `NegativeCases` row, surface-matrix cells at every
   endpoint, mutation surface inside a leg's `mutate` globs, and a golden row. That is the arithmetic that
   parked it, and it has not changed.

**Rejected outright, for the record, so they are not re-proposed as "untried":** the 12-byte three-window
shuffle (**2.21× slower** at 1k, 1.35× at 100k) and the mixed-width column transpose (**2.93× slower**,
1.82×). Both lose to a scalar loop the JIT already emits as two or three register moves. The surviving rule
is narrower than "SIMD is faster": **SIMD wins when it replaces per-element WORK or when the whole block is
a pure move, and loses when it replaces a copy the JIT has already reduced to register moves.**

## Spannable classes (§13)

**Measured** (`Issues/round29/PlanProbe4.cs`, `plan4-results.md`): 0.68× at N = 1,000, **1.00× at 100,000**,
and **1.19× slower** for an auto-layout class. Allocation identical in every arm — it saves no memory at all.

**Why it is parked and would need a policy change to reopen:** it requires `MemoryMarshal.CreateSpan` over an
object interior, which is on this round's banned-symbol list by owner policy (no `unsafe`, no `Unsafe.*`, no
spans over object interiors). The probe's own comment states the hazard exactly — *"no `unsafe` keyword — but
no bounds either: the span's length is the caller's claim about the object's layout"* — and its auto-layout
arm demonstrates the failure, since the CLR may reorder fields and the copy then reads the wrong bytes.

A 32 % win at small N that vanishes by 100k, saves no allocation, and trades a memory-safety guarantee for
it. The trade was bad on the numbers before policy was considered.

## TensorPrimitives conversions (§5, S3)

**Measured in the mapper's own shape** (`V3<float>[]` → `V3<double>[]`): 0.60× cache-resident, **0.97× at
100,000** — *"confirmed for cache-resident sizes"*.

**A caution recorded because it nearly caused the wrong decision:** a 2026-09-07 sweep of a bare
`int[]` → `long[]` conversion measured 0.15–0.25× and was briefly read as promoting this item. That shape is
**not one the mapper emits** — it has no struct and no per-element mapping work. The general width-change
case additionally needs a transpose (gather → convert → scatter), and transposes were measured 1.82–2.93×
slower. Reopen only against a measurement of the shape the generator would actually emit.

The competitive position also does not call for it: at N = 1,000 the mapper's `Widen` is already 745 ns
against Mapperly 928, Mapster 1,192 and AutoMapper 1,257.
