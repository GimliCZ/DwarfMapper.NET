<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: migrate from Mapperly to DwarfMapper

A step-by-step walkthrough for moving off **Riok.Mapperly 4.x**. Mapperly is DwarfMapper's closest
analogue — also a Roslyn source generator built around `[Mapper] partial class` with `partial Dst Map(Src)`
methods. As a result this migration is **nearly mechanical**, often *less* ceremony than what you had, and
you gain several features Mapperly doesn't offer.

> Read [common-changes.md](common-changes.md) first. Exhaustive feature table:
> [`../MIGRATION.md` §3](../MIGRATION.md#3-from-riokmapperly-4x).

---

## The one mental-model shift

There barely is one — the shapes are the same. The only philosophical change: **Mapperly's completeness
check is an opt-in warning; DwarfMapper's is a non-optional build error.** Where Mapperly let an unmapped
member slide (or warn), DwarfMapper stops the build until you map or ignore it. Budget for clearing those
on the first build.

---

## Step 1 — Reference the packages

Follow [common-changes.md §1](common-changes.md#change-1--reference-the-packages-target-net10):
the single `DwarfMapper` package, target `net10.0`. Keep `Riok.Mapperly` installed until Step 5.

## Step 2 — Rename the core attributes

The class and method shapes carry straight over:

| Mapperly | DwarfMapper |
|---|---|
| `[Mapper]` | `[DwarfMapper]` |
| `partial Dst Map(Src s);` | keep as-is, **or** collapse to `[GenerateMap<Src, Dst>]` on the class |
| `partial void Map(Src s, Dst d);` (existing target) | `partial void Update(Src s, Dst d);` |
| `[UserMapping]` precedence | automatic — a non-partial `Dst Convert(Src)` method is preferred over synthesis |

```diff
- [Mapper]
- public partial class OrderMapper
- {
-     public partial OrderDto ToDto(Order src);
- }
+ [DwarfMapper]
+ public partial class OrderMapper
+ {
+     public partial OrderDto ToDto(Order src);   // identical shape
+ }
```

Or, if you'd rather drop the method signatures entirely:

```diff
+ [DwarfMapper]
+ [GenerateMap<Order, OrderDto>]
+ public partial class OrderMapper { }
```

## Step 3 — Port member attributes (mostly a rename)

| Mapperly | DwarfMapper |
|---|---|
| `[MapProperty(nameof(S.A), nameof(D.B))]` | identical `[MapProperty]` |
| `[MapProperty("A.B.C", "D")]` (deep path) | identical dotted path (`DWARF043`/`DWARF044` for unknown/nullable-hop) |
| `[MapperIgnoreTarget(nameof(D.X))]` | `[MapIgnore(nameof(D.X))]` |
| `[MapperIgnoreSource(nameof(S.X))]` | `[MapIgnoreSource("X")]` (with `RequiredMapping = Both`) |
| `[MapPropertyFromSource]` / `[MapValue]` | `[MapValue]` (const + `Use=`, type-checked `DWARF040–042`) |
| referenced-method converter | `[MapProperty(src, tgt, Use = nameof(M))]` |
| `[MapDerivedType<DS, DD>]` | `[MapDerivedType<DS, DD>]` — **same attribute name** |

## Step 4 — Translate mapper options

`[Mapper(...)]` options become `[DwarfMapper(...)]` options:

| Mapperly | DwarfMapper |
|---|---|
| `PropertyNameMappingStrategy.CaseInsensitive` | `[DwarfMapper(CaseInsensitive = true)]` |
| `RequiredMappingStrategy.Target` / `Both` | `[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Target/Both)]` |
| `EnumMappingStrategy.ByName` / `ByValue` | `[DwarfMapper(EnumStrategy = EnumStrategy.ByName/ByValue)]` — **default is ByName in both** |
| strict lossy-conversion diagnostics | `[DwarfMapper(ImplicitConversions = false)]` (flips `DWARF038` suggestions into build errors) |
| `UseReferenceHandling` | `[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]` — DwarfMapper reconstructs the **full** topology (Mapperly's is partial) |
| `[UseMapper]` / `[UseStaticMapper]` (compose mappers) | call another mapper from a `Use=` method, or nest types — no first-class mapper-injection attribute |

Good news on enums and case-sensitivity: the defaults match, so most `[Mapper]` option lines port verbatim.

## Step 5 — Build, clear DWARF001, verify, remove Mapperly

1. Build. The likely new failures are `DWARF001` for members Mapperly only **warned** about (or didn't, if
   you hadn't opted into strict required-mapping). Map / `[MapValue]` / `[MapIgnore]` each.
2. Verify with `[RoundTrip]` — a capability Mapperly doesn't have, so this is net-new safety
   ([common-changes.md §5](common-changes.md#change-5--prove-the-swap-was-lossless)).
3. Remove the `Riok.Mapperly` `PackageReference`.

---

## What you gain by switching

These have no Mapperly equivalent (see [`../COMPARISON.md`](../COMPARISON.md#capability-matrix)):

- **`[RoundTrip]` verification** — fuzz-driven `Back(Forward(x)) ≡ x` with mapping-aware failure dumps.
- **A non-optional completeness build-error gate** — not a warning you can forget to escalate.
- **Blittable/SIMD bulk-copy** (`MemoryMarshal.Cast`) and **SIMD widening** (`Vector.Widen`) fast-paths —
  ~2× faster on blittable struct arrays and primitive-widen arrays; Mapperly copies element-by-element.
- **Zero-alloc `Span<T>` mapping** and **`IAsyncEnumerable` streaming**.
- **`[FlattenGraph]`** graph degradation (homogeneous + heterogeneous).
- **`OnCycle = SetNull`** (System.Text.Json-style cycle breaking) and **full-topology `Preserve`** — uniform
  across direct, `List<T>`, and `Dictionary<K,V>` edges, with a "never a silent StackOverflow" guarantee.

---

## Known divergences & non-goals (Mapperly-specific)

- **`[MapEnum]` / `[MapEnumValue]` per-value remap** — differing enum member sets are a build error to
  resolve explicitly rather than a per-value remap attribute.
- **`[UseMapper]` / `[UseStaticMapper]` mapper injection** — no first-class attribute; compose by calling
  another mapper from a `Use=` method or by nesting types.
- **`[MapperIgnoreObsoleteMembers]`** — obsolete constructors are excluded automatically; otherwise mark
  members with `[MapIgnore]` per-member.

Full divergence notes: [`../MIGRATION.md` §3](../MIGRATION.md#3-from-riokmapperly-4x).
