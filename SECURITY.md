# Security Policy

## Supported versions
The 1.0 line is in release candidate (`1.0.2-rc.1`); no stable release has been published to nuget.org yet,
and no release has been tagged. Until one is, the default branch (`master`) is the only supported version and
is where security fixes land. Once releases begin, this section will name the supported release line.

(If you are reading this from a package you obtained somewhere other than a future official release, treat it
as unsupported — there is nothing published to compare it against.)

## Reporting a vulnerability
Please report security issues privately via GitHub Security Advisories
("Report a vulnerability" on the repository Security tab). Do **not** open a
public issue for undisclosed vulnerabilities.

We aim to acknowledge reports within 72 hours and to provide a remediation
timeline within 7 days.

## Scope
DwarfMapper is a compile-time source generator. It performs no reflection and
no runtime code generation. Reports of interest include: generated code that
is not memory-safe, an analyzer that fails to block an unprovable `unsafe`
blit, or any supply-chain concern in the build/release pipeline.

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
dynamic reflection. Every blit lives in **emitted** code in your assembly, which you can read (see *Auditing
what the generator actually emits*). The library assembly itself offers nothing of that kind to reach.

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

`AllowNonPublic` widens what the generator will *bind to*, strictly within the C# accessibility rules — it is
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

### `[Reinterpret]` is the one place you can override a proof

It tells the generator to treat two element types as byte-compatible without the name-level proof. It does
**not** disable the byte-layout check: element types of different width are refused, and `IntPtr`/`UIntPtr`
are refused outright because their width is the running platform's rather than the build machine's. Use it
only where you can state why the bytes line up.
*Held by `ReinterpretSafetyTests`.*

## Build integrity
Releases are deterministic, ship a CycloneDX SBOM, and are produced from the
audited GitHub Actions workflow in `.github/workflows/`.

## Auditing what the generator actually emits
A source generator writes code into your assembly at compile time. You do not
have to take our word for what that code is — the compiler will write it to disk
for you:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Rebuild, and every file DwarfMapper contributed appears under that path, grouped
by generator. It is ordinary, readable C#: assignments, null checks, and helper
methods. There is no reflection, no runtime emit, no `unsafe` block that the
`[Reinterpret]` analyzer has not proven blittable, and nothing that reaches the
network or the filesystem.

Two things worth knowing when you audit:

- **Output is deterministic.** The same inputs produce byte-identical output,
  including under member reordering, so you can diff two builds and expect
  silence. A diff that is not silent is worth reporting.
- **Generated files are build output, not sources.** Do not add the directory
  to source control or to the compile item group — it is emitted for reading,
  and including it produces duplicate definitions.

If the emitted code and the documented behaviour disagree, that is a report we
want, whether or not it is exploitable.
