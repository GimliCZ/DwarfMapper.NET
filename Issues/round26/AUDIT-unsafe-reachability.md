<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Audit: can a consumer reach an unsafe or broken emission?

**2026-08-23.** Backtracked from this round's performance work into the generator, asking one question: from
ordinary source a consumer can write in their IDE, is there any path to memory unsafety, silent
mis-mapping, uninitialised data, or emitted code that does not compile?

**One real hole was found, and it was mine.** It is fixed. The rest of the surface held.

## The hole: `[Reinterpret]` after the size guard was deleted

`[Reinterpret]` is the one place a consumer can override the blit's layout proof, so it is the one place
they can steer the generator. Deleting the emitted size guard was correct — type safety belongs to the
analyzer — but that guard was the only thing standing behind this override.

Measured, not theorised:

```csharp
[Reinterpret("Data")]            // int[] -> long[]
public partial D Map(C c);
```
emitted a blit with **no diagnostic and no guard, anywhere**. `MemoryMarshal.Cast<int, long>` halves the span
length, so `CopyTo` fills half the destination and zeroes the rest. Silent data loss from three lines of
ordinary consumer source.

Same-size was always the documented contract — `DWARF022`'s help text says "only sound when both element
types are unmanaged AND the same size" — it had simply never been checked outside that runtime guard. The
check now lives in the analyzer, where the contract said it should be.

**The predicate needed a second pass**, and the distinction is worth keeping:

* the **automatic** blit demands the same TYPE — `int -> uint` is a conversion the mapper should perform
  properly, not a reinterpret nobody asked for;
* the **explicit** opt-in demands only the same WIDTH — "treat these bits as unsigned" is exactly what the
  caller is there to assert, and unlike layout it is a thing they can actually know.

`IntPtr`/`UIntPtr` are refused in the opt-in path: their width is the platform's, so a pair that matches on
the build machine need not match where the consumer runs.

## The rest of the surface, probed adversarially

| shape a consumer can write | result | why that is right |
|---|---|---|
| `[StructLayout(Explicit)]` element | refused | field order is the author's, not the language's |
| `Pack = 1` against `Pack = 4` | refused | same fields, different bytes |
| `W<int>` against `W<long>` (generic struct) | refused | recursion reaches `int` vs `long` |
| `bool` field against `byte` field | refused (`DWARF005`) | not the same type; a conversion, not a copy |
| **`Auto`-layout struct nested in two Sequential ones** | **blits** | **correct** — both sides name the IDENTICAL inner type, so whatever layout the runtime picks it picks for both |
| **`nint` against `nint`** | **blits** | **correct** — same type, same platform, same width |
| `DateTime` field against a `long` field | refused | `DateTime` is `Auto`; it only *looks* byte-like |
| element pair of different size | refused | the proof reaches the primitives |

The two that blit are the interesting ones, and both are sound for the same reason: **identity of the field
TYPE, not merely of its size.** The proof short-circuits on identical types, which is exactly when an
unprovable layout stops mattering — the runtime cannot lay the same type out two different ways in one
process.

## The other new paths

**List span fill.** Three compile-time conditions gate it: value-type element, known count, and not the
`Preserve` register-before-fill path. The safety argument is that `__r` is a LOCAL returned solely on
success, so a throwing element mapper leaves unreachable garbage rather than a caller-visible list with a
default-valued tail. `Preserve` is excluded precisely because it publishes the list into the context before
filling — that exclusion has its own test.

**`ImmutableArray`.** The wrap always takes a freshly allocated array; re-wrapping the source's storage would
give two immutable values one buffer. Pinned on the emitted shape and again at runtime by mutating the source
afterwards.

**Enum blit.** Gated on the SCALAR path being a reinterpret, not on layout — `ByName` throws on a value
matching no member, and a blit would pass it through.

## What this audit does not cover

Only the paths this round touched, plus the `[Reinterpret]` override they exposed. It is not a whole-library
safety review; the exhaustion and compiler-test suites remain the standing instruments for that, and both are
green.
