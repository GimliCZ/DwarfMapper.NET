# Security Policy

## Supported versions
DwarfMapper is in release candidate; there is no stable release yet. Two versions are supported:

- the latest prerelease of `DwarfMapper` on nuget.org;
- the default branch, `master`, where security fixes land first.

A fix ships in the next prerelease. Older prereleases are not patched: upgrade to the latest.

The three release candidates published so far, 1.0.0-rc.1 to 1.0.2-rc.1, were pushed to nuget.org by hand. They
have no tag, no GitHub Release, no SBOM and no build-provenance attestation, so they cannot be verified against a
build of this repository. SBOMs and build provenance apply from the first tagged release on (see *Build integrity*).
A package obtained anywhere other than nuget.org or this repository's GitHub Releases is unsupported.

## Reporting a vulnerability
Please report security issues privately by email to **dwarfmapper@seznam.cz**. Do **not** open a public issue
for an undisclosed vulnerability.

Say which version or commit you found it on. A reproducer helps most, and the most useful form is a failing test.

## Response times
We aim to acknowledge reports within 72 hours and to provide a remediation
timeline within 7 days.

## Scope
DwarfMapper is a compile-time source generator with a small runtime library. The main `DwarfMapper` package performs
no reflection and no runtime code generation. The optional `DwarfMapper.Testing` package does use reflection, by
design, to build random test instances; it belongs in test projects only and is not on the mapping path (see
[docs/SECURITY.md](docs/SECURITY.md#caveat-dwarfmappertesting)).

Reports of interest include: generated code that is not memory-safe, a blit emitted for an element pair the
generator did not prove layout-identical, or any supply-chain concern in the build/release pipeline.

The *Trust model* below lists what DwarfMapper guarantees and the test that holds each guarantee. The
mapper failure modes it is designed against, the claim register, over-posting guidance and how to audit the emitted
code are in [docs/SECURITY.md](docs/SECURITY.md).

## Build integrity
Releases are built by `.github/workflows/release.yml` from a version tag, with deterministic build settings. From
the first tagged release on, that workflow:

- attests build provenance for every `.nupkg` and `.snupkg` with a keyless Sigstore signature bound to this
  repository's GitHub identity - verify a package with
  `gh attestation verify <package> --repo GimliCZ/DwarfMapper.NET`;
- attaches a CycloneDX SBOM and a `SHA256SUMS` file to the GitHub Release, next to the packages.

The SBOM and the checksums are Release assets, not files inside the package. The hand-pushed release candidates
named under *Supported versions* have neither.

## Trust model

A source generator sits inside your build, so "what does this library actually guarantee" deserves an answer
more specific than "it does no reflection". Each invariant below names the test that holds it; a scan asserts
those tests exist, so this section cannot quietly drift out of date.

### What is proven at compile time, and what holds at runtime

The generator proves things the runtime then does not need to re-check. A blit is emitted **only** where the
element pair was proven layout-identical during compilation, which is why the emitted copy carries no size
guard: a runtime check there would be re-asking a question already answered, and answering it late.

The consequence is the one worth internalising: **the safety argument lives in the analyzer, not the
library.** Suppressing DwarfMapper's diagnostics is therefore not a style choice — `DWARF022` and its
neighbours are the proof obligation, and silencing one removes the check rather than the requirement.

### The shipped runtime has no unsafe surface

`src/DwarfMapper` contains no `unsafe`, `stackalloc`, `fixed`, `MemoryMarshal`, `Unsafe.`, `DllImport`, and no
dynamic reflection. Every blit lives in **emitted** code in your assembly, which you can read (see
[Auditing what the generator actually emits](docs/SECURITY.md#auditing-what-the-generator-actually-emits)). The
library assembly itself offers nothing of that kind to reach.

The emitted blit is a `MemoryMarshal.Cast` block copy: safe C#, no `unsafe` block, so your project needs no
`AllowUnsafeBlocks`. Every shipped assembly - the runtime, the generator, the code fixes and `DwarfMapper.Testing` -
bans `System.Runtime.CompilerServices.Unsafe` at build time through its `BannedSymbols.txt`.

It also carries **no trimming or AOT escape hatch** — no `UnconditionalSuppressMessage`,
`RequiresUnreferencedCode`, or `DynamicallyAccessedMembers`. The registry keeps a flat list of
interface-keyed registrations specifically so that `GetType().GetInterfaces()` — which trips IL2075 — never
has to be called, and so that suppressing it never has to happen.

*Held by `ShippedRuntimeSafetyTests`.*

### The ambient registry: what it guarantees, and what it does not

`DwarfMapperRegistry.Register` is **public and callable by any loaded assembly**. That is necessary rather
than incidental: every consumer assembly self-registers its maps from a module initialiser, which is what
lets maps cross assembly boundaries without references or reflection.

**Guaranteed — a registration can never be replaced.** The tables are append-only. `TryAdd` never overwrites;
a duplicate pair is marked ambiguous and the *first* registration stands. There is no overwrite path, so one
assembly cannot substitute its own map for a pair another assembly already claimed.
*Held by `RegistryAppendOnlyTests` (structural) and `RegistryConcurrencyTortureTests` (behavioural).*

**Not guaranteed — registration order decides, and a shadowed duplicate is silent by default.** First-wins
means the assembly that registers a pair *first* owns it, and `TryGet` does not consult the ambiguity table:
a later duplicate is recorded but resolution does not fail. Opt into `[DwarfMapperValidationRoot]` to have
ambiguity reported (`DWARF061`) rather than tolerated.

We consider this acceptable rather than ideal, and the reasoning is worth stating plainly: an attacker who
can load an assembly into your process already has code execution there, and at that point the mapping table
is not the weakest thing available to them. If your threat model includes untrusted assemblies in-process,
turn validation on and treat ambiguity as a build failure.

### Accessibility is never bypassed

`AllowNonPublic` — settable per mapper with `[DwarfMapper]`, or for a whole assembly with
`[DwarfMapperDefaults]` — widens what the generator will *bind to*, strictly within the C# accessibility rules — it is
implemented with Roslyn's own accessibility APIs (`IsSymbolAccessibleWithin`, `GivesAccessTo`), so
`[InternalsVisibleTo]` is honoured and a member your code could not legally touch stays out of reach. It is
not a reflective back door: a `private` constructor is refused even with the flag set.
*Held by `ConstructorSelectorHardeningTests` — `Private_ctor_with_flag_still_reports_DWARF026`.*

### Recursion is bounded

Mapping a cyclic graph under the default `ReferenceHandling = None` is depth-limited, so a hostile or merely
circular object graph cannot exhaust the stack silently — it throws `DwarfMappingDepthException`. The cap is
`DwarfRefContext.AbsoluteMaxDepth`, and `[DwarfMapper(MaxDepth = N)]` is clamped to it by **both** the
generator and the runtime, which compile the same constant from one linked source file.
*Held by `RecursionBoundTests`.*

The default is `MaxDepth = 64` (`DwarfLimits.DefaultMaxDepth`), so the bound applies whether or not a mapper
configures one. This is the failure class of **CVE-2026-32933** in AutoMapper 14.0.0
([GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x)): uncontrolled recursion, where roughly
25,000-30,000 nesting levels exhaust the stack and take the process with them. A stack overflow cannot be caught,
so there is no application-level mitigation once it happens. The advisory's own recommended fix is a default
maximum depth, which is what DwarfMapper has shipped from the start — the deep graph throws
`DwarfMappingDepthException`, the caller catches it, the process lives.

Pinned at the advisory's own depth: 30,000 levels through a self-reference, through a `List<T>` and through a
`Dictionary<string,T>`, under both `ReferenceHandling.None` and `.Preserve` — six shapes, each mapped on a thread
with a 1 MB stack, the size a request thread gets. A stack overflow in any of them would kill the test host, which
is what makes the test meaningful rather than decorative.
*Held by `DeepRecursionPocTests`.*

### `[Reinterpret]` is the one place you can override a proof

It tells the generator to treat two element types as byte-compatible without the name-level proof. It does
**not** disable the byte-layout check: element types of different width are refused, and `IntPtr`/`UIntPtr`
are refused outright because their width is the running platform's rather than the build machine's. Use it
only where you can state why the bytes line up.
*Held by `ReinterpretSafetyTests`.*

### `[MapDenseEnumKeys]` turns a lookup into an index, and the bound is proven before it does

It writes an enum-keyed dictionary into a fixed-size `[InlineArray(n)]` struct by indexing with the key's
numeric value. Before a single line is emitted, the generator proves that **every member the key enum
declares** lands inside `[Offset, Offset + n)` — both bounds, so a negative member cannot index in front of
the array, and in a width that cannot wrap, so a member outside `int` cannot cast its way into range. A shape
that fails the proof is `DWARF105`, an error: there is deliberately **no** bounds-checked slow path that
would let an unprovable shape compile.

What a compile-time proof cannot reach is a key that is not any declared member — `(TEnum)999` is legal C#.
The emitted loop therefore range-checks the computed index and throws `ArgumentOutOfRangeException` naming
the key. That check is not decoration: for an enum whose underlying type is wider than `int`, a bare `(int)`
cast of such a key wraps (`(int)(E)0x1_0000_0001` is `1`) and the write would land in a slot belonging to a
different key with nothing thrown at all.

Nothing here is `unsafe`. `[InlineArray]` is a safe language feature and indexing it stays bounds-safe; the
proof is what keeps a *provably wrong* mapping from being emitted in the first place.
*Held by `MapDenseEnumKeysTests` (every refusal) and `MapDenseEnumKeysRuntimeTests` (the undeclared key).*
