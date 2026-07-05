<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: migrate from hand-written mapping to DwarfMapper

A step-by-step walkthrough for replacing **manual mapping code** — the `ToDto()` methods, extension
methods, and `new XDto { A = x.A, B = x.B, … }` blocks you wrote by hand. This is the migration with the
least conceptual baggage (no third-party config to port) and the biggest correctness upside: hand-written
mapping is exactly where "forgot to copy the new field" bugs live, and that class of bug becomes a compile
error.

> Read [common-changes.md](common-changes.md) first.

---

## The one mental-model shift

**You're deleting code, not translating it.** Your hand-written assignment blocks get replaced by a
generated body that produces the *same* direct assignments — but with a compile-time guarantee that every
destination member is accounted for, plus SIMD fast-paths you wouldn't hand-write. The thing you give up is
ad-hoc imperative control inside the mapping; the thing you gain is that the next person who adds a DTO
field can't forget to wire it.

---

## Step 1 — Reference the packages

Follow [common-changes.md §1](common-changes.md#change-1--reference-the-packages-target-net10):
the single `DwarfMapper` package, target `net10.0`.

## Step 2 — Replace each mapping method with a partial declaration

Find a hand-written mapper and replace its body with a `partial` declaration (the named form below keeps the `ToDto` call site). *Or* declare the pair with a class-level `[GenerateMap<Order, OrderDto>]` — that generates a `Map(order)` overload (not `ToDto`), so the call site becomes `new OrderMapper().Map(order)`:

```diff
- public static class OrderMapping
- {
-     public static OrderDto ToDto(this Order o) => new OrderDto
-     {
-         Id = o.Id,
-         Name = o.Name,
-         Total = o.Total,
-         // ...and you hope nobody adds a field to OrderDto without updating this
-     };
- }
+ [DwarfMapper]
+ public partial class OrderMapper
+ {
+     public partial OrderDto ToDto(Order o);   // generated: the same direct assignments, completeness-checked
+ }
```

Update call sites from the extension-method form to the mapper:

```diff
- var dto = order.ToDto();
+ var dto = new OrderMapper().ToDto(order);   // or inject OrderMapper
```

(If you prefer to keep an extension-method call shape, you can leave a thin one-line extension that forwards
to the mapper — but most teams just inject or `new` the mapper.)

## Step 3 — Re-express the non-trivial bits as attributes

Most of a hand-written mapper is `Dst = src` assignments that DwarfMapper does for free. The parts that
*weren't* trivial become attributes:

| You hand-wrote | DwarfMapper |
|---|---|
| `Name = o.FullName` (rename) | `[MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]` |
| `Total = Format(o.Total)` (transform) | `[MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(Format))]` |
| `City = o.Address.City` (reach into a graph) | `[MapProperty("Address.City", nameof(OrderDto.City))]` or `[Flatten(nameof(Order.Address))]` |
| `Source = "api-v2"` (constant) | `[MapValue(nameof(OrderDto.Source), "api-v2")]` |
| `// deliberately not copying PasswordHash` | `[MapIgnore(nameof(Order.PasswordHash))]` (now enforced, not a comment) |
| a `// TODO compute later` left-default member | `[MapIgnore(...)]` + fill it in `[AfterMap]` |

```csharp
[DwarfMapper]
public partial class OrderMapper
{
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(Format))]
    [MapValue(nameof(OrderDto.Source), "api-v2")]
    public partial OrderDto ToDto(Order o);

    private static decimal Format(decimal d) => Math.Round(d, 2);   // OrderDto.Total stays decimal (as in Step 2)

    [AfterMap]                                   // imperative tail you couldn't express declaratively
    private static void Stamp(Order o, OrderDto d) => d.Checksum = Compute(o);
}
```

Anything genuinely imperative that has nowhere else to go lives in `[BeforeMap]` / `[AfterMap]` — that's the
escape hatch for logic that isn't a per-member transform.

## Step 4 — Let the built-ins delete your conversion helpers

Hand-written mappers accumulate little converters: `int.Parse`, `.ToString("o")`, enum `switch`es,
null-coalescing. DwarfMapper synthesizes all of these built-in and AOT-safe — `string↔IParsable`,
`DateTime↔string` round-trip, `enum↔{enum,string,int}`, checked numeric widening/narrowing, nullable lift.
Delete the helpers and let synthesis handle them. (The one it *won't* do silently is float/decimal→int —
that still needs an explicit `Use=`, by design.)

## Step 5 — Build and clear DWARF001

Build. Every destination member your hand-written code happened to leave unset is now `DWARF001`. This is
the upgrade: the bugs that used to ship as "the new field is always null in prod" are now red squiggles.
Map / `[MapValue]` / `[MapIgnore]` each.

## Step 6 — Verify, then delete the old code

1. Because you still have the old hand-written method in source control, the highest-confidence check is an
   equivalence test: assert `oldToDto(x)` ≡ `newMapper.ToDto(x)` over a sample of real inputs, then delete
   the old method.
2. For forward/back pairs, add `[RoundTrip]` and call `VerifyRoundTrip_*` from a test
   ([common-changes.md §5](common-changes.md#change-5--prove-the-swap-was-lossless)).
3. Delete the hand-written mapping classes/extensions.

---

## When to keep mapping by hand

DwarfMapper is declarative; a few situations are genuinely better left as hand-written code (and that's
fine — you can mix them):

- **Heavily imperative transforms** that aren't a per-member function — branching, side effects, calling out
  to services mid-map. If `[BeforeMap]`/`[AfterMap]` + `Use=` can't express it cleanly, a hand-written
  method may read better.
- **Deep-merging into an existing object graph** — `Update(src, dest)` preserves top-level identity but
  **replaces** nested members/collections; it does not deep-merge. If you need true merge semantics, do it
  by hand.
- **One-off throwaway mappings** where the completeness guarantee isn't worth a mapper class.

Everything else — the vast majority of `Dst = src` drudgery — is exactly what DwarfMapper exists to remove,
faster (SIMD where the layout allows) and safer (completeness as a build error) than you'd write by hand.
