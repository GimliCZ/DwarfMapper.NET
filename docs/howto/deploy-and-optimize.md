<!-- SPDX-License-Identifier: GPL-2.0-only -->
# How-to: deploy and optimize with DwarfMapper

The deployment-and-efficiency companion to the migration guides. Where the per-library guides cover
*translating your maps*, this one covers what changes about **shipping and running** the result: the
package footprint, NativeAOT / trimming, build-time cost, choosing the right emit mode for hot paths, and
the one non-technical deployment constraint you must plan for — the **GPL-2.0 obligation**.

> Read [common-changes.md](common-changes.md) first. Benchmarks referenced here live in
> [`../COMPARISON.md`](../COMPARISON.md#performance--memory) and
> [`benchmarks/DwarfMapper.Benchmarks`](../../benchmarks/DwarfMapper.Benchmarks/).

---

## What lands in your deployed binary

DwarfMapper's whole deployment story is "almost nothing ships, and what runs is code you could have
hand-written."

| Concern | DwarfMapper | Runtime mappers (AutoMapper / Mapster runtime) |
|---|---|---|
| Runtime dependency | **`DwarfMapper` only** — attributes + tiny abstractions, **zero transitive deps** | the mapper engine + its dependency closure |
| The generator | a **`DevelopmentDependency` analyzer** — runs at compile time, **does not ship** in your output | n/a |
| Reflection / `Reflection.Emit` | **none** | yes (the engine *is* runtime reflection/expression-trees) |
| Allocation by the mapper | **zero** — only the destination object is allocated | mapper allocates |
| Startup cost | none — code is already emitted | first-call expression compilation / config build |

So a deployed app references a single small assembly with no dependencies, and the actual mapping is direct
field assignments (plus SIMD where layout allows). Nothing about mapping is deferred to runtime.

---

## NativeAOT and trimming

This is the headline deployment win over the runtime mappers (which are not AOT-safe). DwarfMapper emits
only concrete, non-reflective calls — `CreateChecked`, `Parse`, `ToString`, named-argument constructors,
`MemoryMarshal.Cast` — so a default `dotnet publish` for AOT produces **zero IL2xxx/IL3xxx trim/AOT
warnings**. The library is marked `IsAotCompatible` and `IsTrimmable`, and CI runs an AOT + trim gate.

To publish AOT:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r win-x64    # or linux-x64, etc.
```

### The one AOT caveat worth knowing: SIMD width

NativeAOT defaults to a **baseline instruction set** (x86-64-v1 / SSE2) for portability. That means the
`Vector.Widen` SIMD path runs at half width under default AOT (`Vector<int>.Count == 4` vs `8` under the
AVX2-detecting JIT). It stays **correct** — the scalar tail and narrower body produce bit-identical results
— just not maximally fast. To get full-width SIMD under AOT, opt into a higher ISA:

```xml
<PropertyGroup>
  <IlcInstructionSet>native</IlcInstructionSet>  <!-- build-machine ISA; or an explicit list e.g. avx2 -->
</PropertyGroup>
```

This affects only the throughput of the blit/widen fast-paths; correctness and the zero-warning AOT
guarantee are unchanged either way. (`x86-64-v3` is rejected by ILC 10.0.1 — use `native` or an explicit
ISA list.) Full detail and the stability-harness results: [`../COMPARISON.md`](../COMPARISON.md#nativeaot-benchmarking--stability).

---

## .NET 10 only — and why the generator is netstandard2.0

The consuming project must target **`net10.0`**; this is a hard requirement, not a preference. The single
exception is the **generator assembly**, which is `netstandard2.0` — because Roslyn loads source generators
*into the compiler host*, which only accepts `netstandard2.0` components. This is a Roslyn toolchain
constraint; the code the generator **emits** targets .NET 10 like the rest of your app. You never reference
the generator at runtime (it's a `DevelopmentDependency` / analyzer), so its TFM never reaches your output.

If you're not yet on .NET 10, that bump is a prerequisite of the migration — sequence it first.

---

## Build-time cost

You're moving work from runtime to build time, so it's worth knowing the build characteristics:

- **Incremental & cached.** The generator is a Roslyn *incremental* generator with deterministic output —
  editing a non-mapper file doesn't re-run mapping codegen, and identical input yields identical output
  (verified by determinism tests). Warm incremental builds are cheap.
- **Errors surface at build, not at test/runtime.** The completeness gate (`DWARF001`) and conversion
  diagnostics run during compilation. This shifts the "is my mapping correct?" feedback left — failing
  builds replace failing tests/production incidents.
- **No separate codegen/tool step.** Unlike Mapster's optional `Mapster.Tool`, there's nothing extra to run
  in CI — the generator is just part of `dotnet build`.

---

## Choosing the right emit mode for hot paths

DwarfMapper offers several emit shapes; picking the right one is the main runtime-efficiency lever. Declare
the method signature that matches your access pattern:

| Need | Declare | Why |
|---|---|---|
| Map to a fresh object | `partial Dst Map(Src s);` (or `[GenerateMap<Src,Dst>]`) | the default; only the destination allocates |
| Update a tracked/pooled instance | `partial void Update(Src s, Dst d);` | preserves identity, no new allocation of the root |
| Hot loop over a buffer, zero alloc | `partial void Map(ReadOnlySpan<Src> s, Span<Dst> d);` | element-wise into caller memory (`stackalloc`/pooled); no heap traffic |
| Stream a large/async source | `partial IAsyncEnumerable<Dst> Map(IAsyncEnumerable<Src> s);` | `async` iterator, no buffering, back-pressure preserved |
| Push mapping into the database | `partial IQueryable<Dst> Project(IQueryable<Src> q);` | emits `Select(...)` your ORM translates to SQL |

### The fast-paths you get for free

When the layout allows it, the generator beats even a hand-written name-based copy — and it's automatic:

- **Blittable bulk copy.** A layout-identical `TSrc[] → TDst[]` (both unmanaged, sequential, same packing,
  same ordered fields) is reinterpreted as a single `MemoryMarshal.Cast` block copy behind a JIT-folded size
  guard — benchmarked **~2× faster** than every competitor on a 1000-struct array, which copy field-by-field.
- **SIMD widening.** A lossless primitive widen array (`int[]→long[]` and the other six `Vector.Widen`
  pairs) is vectorized behind a hardware-acceleration guard with a scalar tail — bit-for-bit identical to
  the scalar widen, purely a throughput win.

Both are emitted **only when provably safe**; everything else falls back to the direct element loop. For a
layout-compatible pair the proof can't confirm (e.g. differing field names from a referenced assembly), opt
in with `[Reinterpret("Member")]`. You don't configure any of this for the common case — it just happens.

Measure on your own hardware:

```bash
dotnet run -c Release --project benchmarks/DwarfMapper.Benchmarks
```

---

## The deployment constraint you must plan for: GPL-2.0-only

This is the one place DwarfMapper's deployment story diverges sharply from AutoMapper/Mapster/Mapperly (all
MIT), and it's a **licensing** decision, not a technical one — but it's a hard deployment gate, so decide it
**before** you migrate.

DwarfMapper is **`GPL-2.0-only`**, strong copyleft, and it is a **source generator** — it emits code
directly into your assembly. Under GPLv2, software you build and **distribute** on top of it is a derivative
work and must itself be released under GPLv2, with corresponding source made available to whoever you
distribute the binary to.

In plain terms: **if you build on DwarfMapper and ship the binary, your project is GPLv2 too, and your users
get the source.** (This is the *conservative* reading — whether a source generator's emitted output makes your
assembly a derivative work is legally unsettled; consult counsel for commercially sensitive use.) Plan accordingly:

- **Distributing a binary product (desktop app, on-prem server, library on NuGet, mobile app)?** Your
  product must be GPLv2 with source available. If that's incompatible with your licensing, DwarfMapper is
  not the right choice — stay on an MIT mapper.
- **Running a hosted service (SaaS)?** SaaS is **not distribution** under GPLv2 — you never ship binaries to
  users, so GPLv2 does not compel you to publish source. (Closing that loophole would need AGPL, which this
  project deliberately did *not* adopt.) This is the common "safe" case.
- **GPLv2-only**, not "v2 or later", and incompatible with Apache-2.0 and GPLv3. DwarfMapper has **zero
  dependencies**, so there's no transitive-license conflict today, but it constrains what GPLv3/Apache code
  you can combine with it.

Every source file carries an SPDX header (`// SPDX-License-Identifier: GPL-2.0-only`); the full text is in
[`LICENSE`](https://github.com/GimliCZ/DwarfMapper.NET/blob/master/LICENSE), and the rationale is in the [README license section](../../README.md#license).

> **Migration sequencing tip:** resolve the license question first. It's the only part of adopting
> DwarfMapper that can't be undone with a code change later.

---

## Verifying a release you depend on

DwarfMapper releases are built to be **independently verifiable** (CRA-aligned supply chain), which matters
if you're vetting a dependency for a regulated or security-sensitive deployment:

- **Deterministic, reproducible builds** with SourceLink and embedded untracked sources.
- **Machine-readable SBOM** (CycloneDX) attached to every release.
- **Keyless signing** via GitHub OIDC through Sigstore — no private key stored anywhere; authenticity is the
  artifact's SHA-256 fingerprint plus the git identity that built it (`gh attestation verify`).
- **Hardened CI** — Actions pinned to commit SHAs, CodeQL, AOT/trim gate, coverage gate.

Step-by-step verification (fingerprint, provenance/identity, SBOM): [`../RELEASING.md`](../RELEASING.md)
and [`../SECURITY.md`](../SECURITY.md).
