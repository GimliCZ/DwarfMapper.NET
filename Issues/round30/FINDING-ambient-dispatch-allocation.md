<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Finding: the ambient interface path allocated a copy of the registry on every call

**2026-09-10.** Opened by a consumer report: memory in a long-lived Blazor Server process (FusedChat.Web)
"infinitely increases". Reported as a suspected leak in DwarfMapper startup.

## What it was not

**Not a leak, and not startup.** Module initializers run once per assembly load, so startup cannot grow
without bound; the growth is in steady state. And nothing is retained:

| probe | result |
|---|---|
| source graph collectable after `Map` returns (Preserve mode) | yes |
| source graph collectable after `Map` returns (None mode) | yes |
| retained heap across 20,000 additional maps of a 50-node graph | flat |
| per-call allocation after 20,000 intervening calls | flat |

Those are `RetentionProbeRuntimeTests`, and they carry a **negative control** —
`The_probe_detects_retention_when_retention_is_real` deliberately roots a graph in a static list and
requires the probe to see it, then clears the root and requires the probe to see that too. Without it, four
tests that all assert *something is not alive* would pass just as happily with a broken instrument. This
repository has produced six mechanisms that reported success while measuring nothing; a retention suite
asserting only absence is the seventh waiting to happen.

## What it was

**Churn proportional to the consumer's entire map graph, on every ambient interface-path call.**

`InterfaceMaps` was a `ConcurrentBag<(Type, Type, Func<object, object>)>`, and `Map` walks it with
`foreach` when resolution falls through to the interface step. A bag's enumerator does not iterate in
place — it **copies every element into a fresh list** on each enumeration. So the per-call allocation was
O(number of registered interface maps):

| path | before | after |
|---|---:|---:|
| exact-type (dictionary hit) | 24 B/call | 24 B/call |
| **ambient interface walk** | **56,207 B/call** | **24 B/call** |
| `Provided` getter | 87,816 B/read | 44,001 B/read |

Measured at 2,738 registered pairs. 24 B is the mapped result and nothing else, so the interface path now
costs exactly what the exact-type path costs.

Under Server GC, 56 KB per call across a busy request path produces a rising sawtooth that is
indistinguishable from a leak in a memory graph — which is why the report was credible and worth chasing.
The consumer was reading a real signal; it was the wrong diagnosis of a real defect.

## Two allocations, two causes

1. **The bag.** Replaced with a copy-on-write array published through one volatile write. The access
   pattern is the whole argument: registration happens once per assembly load from a module initializer,
   lookup happens on every call forever after. Copy-on-write puts the entire cost on the rare side and
   leaves the hot side reading an array reference. Registration serialises on a `Lock`; readers never take
   it, and a reader sees either the old array or the complete new one — never a half-filled one.

2. **A list built to have its `Count` compared to 1.** The walk allocated `candidates = [ifaceSource]` on
   the FIRST match, then returned when `candidates.Count == 1`. The overwhelmingly common single-match case
   — the one that succeeds — paid for a `List<Type>` that existed only to be counted. Now the list is
   allocated on the SECOND match, which is the ambiguous case that actually needs to name candidates. The
   throw still names every interface that accepted the source.

`Provided` halved as a side effect: it read `Maps.Keys`, and `ConcurrentDictionary.Keys` materialises its
own `List` of every key, so the key set was built twice per read. It now walks the dictionary's own lazy
enumerator. The remaining 44 KB is the returned list itself, inherent to handing back a materialised
collection of 2,738 pairs — and this member is documented diagnostics/validation-only, so it is not on any
hot path.

## Why no test caught it

There was **no retention or allocation test on the ambient path at all** — `AllocationBoundRuntimeTests`
pins zero-allocation for a blittable struct map, and nothing else in the suite measured bytes. The
registry's correctness was well covered (`AmbientRegistryTests`, `RegistryConcurrencyTortureTests`); its
*cost* was not covered at any point. Correct and unaffordable passes a correctness suite.

This is the round's fifth instance of the same shape: the gap was in what the corpus measured, not in
subtle code. `ConcurrentBag` is the obvious choice for a concurrent collection and the wrong one for a
read-mostly set, and nothing in the build would ever have said so.

## Locked by

* `AmbientDispatchAllocationRuntimeTests.Interface_path_dispatch_does_not_allocate_a_copy_of_the_registry`
  — bounds the path at 512 B/call, two orders above the fixed cost and two below the defect. Any return to
  a per-call registry copy blows it immediately.
* `AmbientDispatchAllocationRuntimeTests.The_interface_path_is_actually_the_path_being_measured` — the
  control. Resolution order is exact type, then base chain, then interfaces, so a fixture that accidentally
  registered the concrete type would measure the cheap path and go green. It asserts the concrete type is
  *not* registered and that the walk resolves.
* `RetentionProbeRuntimeTests` (5 tests incl. the control) — the retention half, now permanent.

## Still open for the consumer

The library is exonerated for retention, but the consumer's process was never measured. Two hypotheses
remain that this repository's tests cannot settle:

* **`[MapShare]` aliasing** a short-lived source's collection into a long-lived destination keeps the
  source graph alive. `DWARF104` exists for exactly this. Checking it means scanning the generated `.g.cs`
  in the consumer for share sites whose destination is a cached or singleton type.
* **Not DwarfMapper at all** — Mongo driver, SignalR circuit accumulation. Only a `dotnet-gcdump` against
  the running Web process settles it, which needs the MongoDB and PostgreSQL dependencies that currently
  block a dev run.
