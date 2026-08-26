<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Phase 0 design — surface governance and security

**Status: design. Execution order in §6.**

The instruction: *nail down the architecture of the repository, especially public API control, and harden every
step, because this is close to a compiler library.* This is that design.

Its first job was to find out what already exists. **Most of it does.** The repository already carries a
surface-governance architecture better than the one this design would otherwise have proposed, and the
correct move is to add two axes to it rather than build a second catalogue beside it — the failure mode
`RepoPaths` records ("three test files carried a private copy of this walk").

---

## 1. What already exists — do not rebuild

| surface | instrument | state |
|---|---|---|
| every public attribute is classified | `[DwarfSurface(SurfaceCategory)]`, enforced by `SurfaceDeclarationTests.Every_public_attribute_declares_a_DwarfSurface_category` | **armed** |
| each class carries a distinct proof obligation | `SurfaceObligationTests`, one test per category | **armed** |
| endpoint claims are true in both directions | `SurfaceParityTests` — over-claim fails, under-claim fails | **armed** |
| diagnostic ids as public API | `AnalyzerReleases.*` + wording pins + CHANGELOG | **armed** |
| emitted bytes | 973-case golden manifest, no auto-bless | **armed** |
| generated code uses no reflection | `ReflectionFreeMetaTests` | **armed** |
| type/member inventory | `PublicAPI.*.txt` + PublicApiAnalyzers | inventoried, **not frozen** |

`SurfaceCategory`'s own docstring states the principle this design must extend rather than violate:

> There is deliberately no `Exempt` member. Every category below carries a DIFFERENT mandatory obligation;
> classifying an element redirects its proof rather than waiving it. This replaces six independent allowlist
> dictionaries, each of which was one person typing a reason once.

Any axis added below obeys that: **every value carries an obligation; no value waives one.**

---

## 2. Axis 1 — Discoverability

### The gap, stated precisely

My first measurement — "15 of 29 attributes have no Gallery example" — was true but misleading, and the
correction matters. `SurfaceObligationTests` already requires a `ConsumerDirective` to appear in a **runnable
sample**, and its corpus is *everything under `samples/`*. Its own failure text says:

> Add a Conformance feature (`samples/DwarfMapper.Conformance`) asserting its observable runtime difference,
> **or a Gallery example if it deserves prose.**

So the existing obligation is about **proof**, and Conformance discharges it. Measured split:

| | count |
|---|---:|
| in the Gallery | 14 / 29 |
| in Conformance | 24 / 29 |
| **Conformance-proven but absent from the Gallery** | **10** |
| in no sample at all (non-`ConsumerDirective` categories) | 5 |

The suite is green because every element meets its category's obligation. **What is missing is not proof but
lookup** — a human asking "how do I use `[MapCollectionKey]`?" gets a Conformance assertion, not an example.
That is exactly the stated concern, and it is a *new obligation dimension*, not a hole in the old one.

### Design

Add to `DwarfSurfaceAttribute`:

```csharp
public Discoverability Discovery { get; set; } = Discoverability.GalleryExample;
```

```csharp
internal enum Discoverability
{
    /// Obligation: a Gallery region exists AND the generated Gallery README quotes it.
    GalleryExample,

    /// Obligation: a Conformance feature exists AND docs/ carries a prose section.
    /// For elements a Gallery example cannot honestly show — a build-failure-only element cannot
    /// compile, a cross-assembly element needs two projects the Gallery does not have.
    ConformanceOnly,

    /// Obligation: the element is [EditorBrowsable(Never)] AND does NOT appear in the Gallery.
    /// A consumer-facing example of an API the generator writes would document usage that does not exist.
    Infrastructure
}
```

`GalleryExample` is the **default on purpose**, matching `SurfaceEndpoints.All`: a newly added attribute
acquires the strongest obligation unless someone deliberately narrows it.

The `Infrastructure` value is the load-bearing one. Its obligation is **inverted** — such an element appearing
in the Gallery *fails*. That converts the exemption from a waiver into a provable claim, which is the whole
reason the category enum has no `Exempt`. `DwarfProvidesMap` and `DwarfRequiresMap` qualify on evidence: the
generator writes `[assembly: global::DwarfMapper.DwarfProvidesMap(typeof(...), typeof(...))]` into consumer
assemblies, verified in real generated output.

**Estimated work:** ~10–11 new Gallery examples, 2 `Infrastructure`, ~3 `ConformanceOnly`.

**This is one work item with the already-decided strict orphan rule**: every new Gallery region needs a
document quoting it, and the generated illustrated Gallery README is that document.

---

## 3. Axis 2 — Security relevance

Nothing today classifies surface by security consequence, and the CRA-defensive posture makes that a gap in
documentation as much as in code.

```csharp
public SecuritySurface Security { get; set; } = SecuritySurface.None;
```

| value | obligation |
|---|---|
| `None` | none — but see the vacuity control below |
| `TrustBoundary` | a `SECURITY.md` section naming the boundary, **and** a test pinning the invariant |
| `MemorySafety` | a test proving the unsafe path cannot be reached without its compile-time proof |
| `ResourceBound` | the bound is pinned by a test, **and** every site enforcing it is proven to agree |

### Vacuity control — the part that makes this more than a comment

`None` as a default is exactly the shape that passes vacuously, which this repository treats as the primary
failure mode. So the axis is paired with a **detector**: a scan over the generator for the three mechanisms
that create security consequence, asserting that anything reaching one is declared with the matching value.

| mechanism detected in generator source | implies |
|---|---|
| `IsSymbolAccessibleWithin` / accessibility widening | `TrustBoundary` |
| blit / `MemoryMarshal` / reinterpret emission | `MemorySafety` |
| a clamp against a documented maximum | `ResourceBound` |

**Stated limit, honestly:** this proves *declared ⊇ detected* for three known mechanisms. It does not prove the
taxonomy complete, and a novel mechanism would be undetected until someone adds a detector. That is a real
bound on the guarantee and is written here rather than discovered later.

---

## 4. The security invariants, and what is already true

Measured at `7f09a4e`. Three hold, one is partly covered, **one is broken**.

### [SEC-1] The shipped runtime contains no memory-unsafe or reflective surface — *holds, untested*

`src/DwarfMapper` contains zero `unsafe`, `MemoryMarshal`, `Unsafe.`, `stackalloc`, `fixed`, or `DllImport`.
Every blit lives in *emitted* code behind a compile-time proof, so the library assembly itself offers no
memory-unsafe surface at all.

`ReflectionFreeMetaTests` pins that **generated code** is reflection-free — it does **not** pin the runtime
assembly. This invariant is true today and guarded by nothing.

### [SEC-2] A registration can never be replaced — *holds, partly tested*

`Maps.TryAdd` is append-only; a duplicate marks `Ambiguous` and the first registration stands. There is no
overwrite path, so a hostile assembly cannot *replace* a map. `RegistryConcurrencyTortureTests` and
`AmbientRegistryTests` cover the runtime behaviour; no structural scan prevents a future `[key] =` or
`AddOrUpdate` from being introduced.

**What does NOT hold, and needs a written trust boundary rather than a fix:** `TryGet` does not consult
`IsAmbiguous`, so **registration order decides** and a shadowed duplicate resolves silently unless the
consumer opts into `[DwarfMapperValidationRoot]`. `Register` is public and must be — consumer assemblies
self-register from module initialisers. Anyone able to load an assembly into the process generally owns the
process already, so this is very likely *acceptable*; the point is that acceptable and undocumented are
different states, and only the first is defensible.

### [SEC-3] The depth bound is single-sourced — **BROKEN**

`DwarfRefContext.AbsoluteMaxDepth = 1000` is documented as "the absolute hard cap. No `MaxDepth` value can
exceed this." The runtime clamps to it. **The generator clamps to a hard-coded literal `1000`** in
`MapperExtractor.Attributes.cs`, with the comment *"Clamp to [1, 1000] — matches
DwarfRefContext.AbsoluteMaxDepth"* — a claim nothing verifies.

It cannot simply reference the const: the generator is `netstandard2.0` and does not reference the runtime.
That constraint is real and is not the defect. The defect is that **a security-relevant bound exists in two
independently maintained places with no test on either side**. Raise `AbsoluteMaxDepth` and the generator goes
on refusing anything above 1000 — silently capping users below the documented bound.

This is the "fix applied to 1 of N identical sites" shape, on a resource limit.

### [SEC-4] `AllowNonPublic` never exceeds C# accessibility — *holds, untested at the boundary*

```csharp
ctor.DeclaredAccessibility == Accessibility.Public
  || (allowNonPublic && compilation.IsSymbolAccessibleWithin(ctor, compilation.Assembly))
```

Real Roslyn accessibility, honouring `InternalsVisibleTo`; no reflective bypass. What is untested is the
*negative*: that a `private` constructor in a **foreign** assembly is still refused with `AllowNonPublic` on.
That is the assertion which would catch a future widening.

### [SEC-5] `[Reinterpret]` cannot bypass the byte-layout proof — *holds, tested*

Round 26 found and fixed the one real hole here; `ReinterpretSafetyTests` pins it, including the subtle
`{long}` vs `{int,int}` same-total-size refusal.

---

## 5. The freeze, and what must precede it

Promotion of the 278 `Unshipped` entries to `Shipped` was decided. `AUDIT-public-surface.md` covers the
structural hazards; the member-by-member review remains. Two fixes must land **before** promotion, because
each is free now and a declared break afterwards:

* **A1** — mark infrastructure `[EditorBrowsable(EditorBrowsableState.Never)]` (no signature changes).
* **A2** — `DwarfRefContext(int maxDepth, bool preserve = false, bool setNull = false)`: two adjacent
  same-typed optional bools, emitted positionally at 28 sites and by name at 13. The only caller is the
  emitter, so this costs a regeneration today.

A2 moves the golden manifest. It is an **emitted-bytes** change, so it lands as a deliberate re-bless with the
diff reviewed — never under the byte-identity lock, which exists to catch exactly this and must not be used to
hide it.

---

## 6. Execution order

1. **SEC-1** — pin "the runtime assembly carries no unsafe/reflective surface". Pure addition; cannot break.
2. **SEC-3** — single-source the depth bound across the `netstandard2.0` boundary, and pin it on both sides.
3. **SEC-2 structural** + the written trust boundary in `SECURITY.md`.
4. **SEC-4** — the negative accessibility test.
5. **Axis 2** — `Security` property, detector, and the obligations above.
6. **A1 / A2** — infrastructure marking, then the ctor fix with a reviewed re-bless.
7. **Member-by-member audit** of the 278 + `DwarfMapper.Testing` → **promote to `Shipped`**.
8. **Axis 1** — `Discovery` property, the Gallery gate, ~10–11 examples, the generated illustrated README, and
   the strict orphan rule, as one item.

Restructuring (R27-01 onward) begins only after 8 — as decided.
