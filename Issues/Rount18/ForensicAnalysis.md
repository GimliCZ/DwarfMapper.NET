<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Round 18 — forensic analysis of `ImplementationRecord.txt`

**Source:** `Issues/Rount18/ImplementationRecord.txt`, 9,120 lines — a full session transcript of migrating
FusedChat (MedbotOmega, ~300 directed maps across 12 AutoMapper profiles) onto DwarfMapper.

**Why this document exists.** That transcript is the first time DwarfMapper has been driven by a real consumer
at scale rather than by its own test corpus. It found four generator defects, one blocking runtime gap, and a
long tail of ergonomics friction. This file dissects the record fragment by fragment and turns each finding
into a maintenance or repair task.

**Citations** are `ImplementationRecord.txt:NNNN`.

**Verified before writing:**

- `git log master..HEAD` — 4 fix commits on `fix/use-converter-scoping-and-dwarf076-suppression`.
- `dotnet build DwarfMapper.NET.sln` (whole solution, **samples included**) → **0 errors, 0 warnings**.
- `grep samples/` for each policy option, to make the "not exercised" claim precise rather than rhetorical.
- `[AttributeUsage]` on `DwarfMapperAttribute` / `DwarfMapperDefaultsAttribute`, to pin the scoping claim.

**"All members" is `SkipNullSourceMembers`** — confirmed by the maintainer, 2026-08-12. DwarfMapper's
equivalent of AutoMapper's `ForAllMembers(o => o.Condition((_,_,src) => src != null))`, and the thing the record
keeps colliding with (`:228`, `:294`, `:369`, `:481`, `:3790`, `:5016`). It is present, documented, exercised in
**no** sample file, and carries a live correctness gap. See §3.4, §5 and §7.

---

## 1. Executive summary

| | Count |
|---|---:|
| Generator/runtime **defects**: found | 5 |
| …fixed on the current branch | 4 |
| …still open | 1 (blocking) |
| **Structural limitations** surfaced (not defects, but they cost real work) | 4 |
| Feature gaps that forced hand-work | 6 |
| Documentation defects | 7 |
| Policy options with **zero** coverage in `samples/` | 6 of 15 |

**The single most important pattern.** Every defect Round 18 found was **consumer-shaped**: multi-assembly,
runtime-registry, ambient-facade, polymorphic. Not one of them was reachable from a generator snapshot test —
and the suite is 5,341 generator tests strong. The 47-site blocker (§3.1) slipped past *three* independent
safety nets simultaneously. That is a schema hole, not a diligence failure, and §6 proposes the fix.

**Second pattern, and the reason this matters more than a normal bug list.** Of the five defects, **three were
silent-data-loss bugs behind a green build**:

- a `Use=` converter applied to a member it never named (`:4259`),
- a `[MapConstructor]` factory adopted as a collection element converter, mapping whole collections to blanks
  (`:7893`),
- a declared enum→enum pair emitting `return new DstKind { };` — source discarded, zero returned (`:5876`).

DwarfMapper's entire pitch is *compile-time certainty*. A green build that silently writes wrong data is the
one failure mode the project cannot afford, so those three deserve a standing regression category, not just
three tests.

---

## 2. Fragment analysis — defects already fixed

Listed for completeness and because each one implies follow-up work beyond the fix itself.

### 2.1 `Use=` converters were auto-adopted by members that never named them

> `:4259` — `Assert.DoesNotContain() Failure: Sub-string found` / `String: ···".Code),\n Code = Decorate(src.C"···`
> `:4308` — *"Root cause found — `MapperExtractor.Conversions.cs:397-410`. The generator scans all non-partial
> user methods for one whose signature matches `srcType → tgtType` and adopts it as an automatic converter."*

**What it cost.** Reported by a subagent at `:3163`, dismissed as unverified for ~1,100 lines because the
isolated repro was blocked by `obj/` contention (`:3130`), and only confirmed at `:4259`. In FusedChat it would
have written a date-prefixed document id (`"20260811_<guid>"`) into the plain `DonationId` column of every new
premium record.

**Fixed** in `47a1867`. Note the fix needed two attempts: per-method reservation was insufficient because a
sibling method with no `Use=` of its own still stole the converter (`:4648`). Reservation is now mapper-wide.

**Residual concern.** The underlying behaviour — *any* method whose signature matches is silently adopted as a
converter — is still live for methods that no `Use=` names. That is a useful convenience and also an
action-at-a-distance hazard: adding an unrelated private helper `string Foo(Guid g)` to a mapper class can
silently change existing maps. → **R18-11**.

### 2.2 A declared enum→enum pair silently returned zero

> `:5876` — *"The generated code for a top-level enum→enum pair is: `return new NotificationType { };`
> ← src is ignored entirely; always returns default (0)"*

**What it cost.** `Global` mapped to `User`. Found only because the golden master compared actual values; a
completeness-only test would have passed. The same pair used as a *member* resolved correctly — only the
declared top-level path constructed instead of converting.

**Fixed** in `69870ee` by routing enum targets through the same conversion path top-level collection and
dictionary pairs already used.

**Residual concern.** The bug class is "a declared top-level pair whose target is a *value* gets object-mapped."
Collections, dictionaries and enums are now handled. What about other value-like targets — `string`, primitives,
`Guid`, `DateTimeOffset`, a struct with no settable members? → **R18-12**.

### 2.3 `[MapConstructor]` factory adopted as a collection element converter

> `:7893` — *"the same root cause as the `Use=` one I already fixed: a `[MapConstructor]` factory is being
> adopted as a general-purpose element converter"*

**What it cost.** `Add(Create(item))` constructed each element and assigned nothing — a whole collection mapped
to `Empty` singletons, green build, no diagnostic. Found by a subagent, 4,000 lines after the first instance of
the identical root cause.

**Fixed** in `870b7d9`. That the *same root cause* produced a second distinct data-loss bug is itself the
finding: the reservation rule needed extending to constructor arguments, and the collection-pair call site
wasn't passing the reserved set at all (`:7987`).

### 2.4 `ObjectFactory` returned `null` for every abstract and interface type

> `:5363` — *"`ObjectFactory.cs:165` returns null for any abstract or interface type … So the fuzzer can never
> exercise polymorphic graphs — the exact thing `[MapDerivedType]` exists to map."*

**What it cost.** Nine phantom `ArgumentNullException` parity failures that read as the most serious finding of
the migration (`:5218`), and a wrong-hypothesis detour through `ObjectFactoryV2` that *worsened* results
(`:5300`) before being reverted on measurement. Your prompt — "we should have solution for abstracts" — is what
turned it around.

**Fixed** in `b5590a5`.

**Residual concern, and it is sharper than it looks.** `[MapDerivedType]` and `[FlattenGraph]` are DwarfMapper's
polymorphism story, and until this commit the fuzz/round-trip infrastructure **structurally could not reach
either**: any abstract-membered graph was fabricated with a null there, so every `[RoundTrip]` result over such
a graph was vacuous.

The suite was re-run after the fix and came back **green** — 6,106 tests at `:5577`, 6,111 at `:9110`. That is
the finding, not the reassurance: if the corpus contained abstract- or interface-membered graph types,
un-vacuum-ing their fixtures should have changed *something*. A wholly green run implies **the corpus barely
contains that shape at all**. → **R18-13** (audit the fuzz/`RoundTrip` corpus for polymorphic coverage and add
abstract/interface-membered graph types; the green post-fix run is the evidence that they are missing).

### 2.5 `DWARF076` could not be suppressed in-file, and the docs promised it could

> `:3178` — *"`docs/diagnostics.md` says `DWARF076` can be silenced with `#pragma warning disable` or
> `[SuppressMessage]`, but neither works because Roslyn doesn't run generator-reported diagnostics through
> pragma filtering."*

**What it cost.** **Four independent agents** hit this (`:3778`, `:3830`). A warnings-as-errors consumer had no
local escape hatch for a deliberate clone — only a project-wide `.editorconfig` kill switch.

**Fixed** in `47a1867`: `[SuppressMessage]` is now honoured; `#pragma` cannot be (Roslyn platform limit) and
the doc was corrected, with a test pinning current behaviour so a future Roslyn change is noticed.

**Residual concern.** The fix covers `DWARF076` only. **Every other `DWARF…` id has the same problem** and the
suppression table still implies otherwise for all of them. → **R18-04**.

---

## 3. Fragment analysis — open defects

### 3.1 BLOCKING — the ambient registry derives nothing from element maps (47 call sites)

> `:7647` — `unresolvable=FusedChat.Omega.Interfaces.IOmegaData :: DwarfMapMissingException: No DwarfMapper map is registered for 'List<FusedChat.Database.Entities.Store>' -> 'ICollection<StoreItem>'`
> `:7661` — *"AutoMapper derived collection maps implicitly from the element map; the ambient registry needs the
> collection pair declared. Notably the `DWARF061` root couldn't catch this — the source type isn't statically
> known at that call site."*

**This is the most consequential finding in the record.** 47 call sites, each throwing on first use, in a branch
that built at 0 errors with the validation root active and passed 706 tests.

Why all three safety nets were blind (`:7377`):

| Net | Why it missed |
|---|---|
| 706 consumer tests | construct services directly or mock them; never resolve through DI |
| Parity suite (233/244) | replays *declared* pairs; ambient collection resolution is not one |
| `DWARF061` validation root | needs the **source** type; at `Map<ICollection<T>>(data)` only the destination is static |

It surfaced only because a benchmark happened to build the real DI graph and count unresolvable registrations.

**The fix that was applied** is consumer-side: ~40 hand-declared collection pairs plus 5 `.ToList()` calls. That
works, but it is exactly the tax that will meet every future consumer.

**The repair task** (**R18-01**) is upstream, and there is an AOT-safe shape: for every declared element pair
`(S,T)`, have the generator **auto-register the common collection shapes at compile time** —
`List<S>→List<T>`, `List<S>→ICollection<T>`, `S[]→…`, `IEnumerable<S>→…` — gated by an opt-out. No reflection,
no runtime synthesis, no trim hazard: just extra entries in the generated registration table. That is what
AutoMapper did, minus the reflection.

### 3.2 The registry walks base types only, never interfaces — "two keys per call site"

> `:8604` — *"`DWARF061` validates the static argument type at build; the registry resolves by `source.GetType()`
> and walks base types only, never interfaces. A method declared `ICollection<T>` returning `List<T>` needs
> **both** pairs."*
> `:8605` — *"That's why my first single-pair fix turned the graph green while 46 sites stayed broken — I'd
> proved the wrong thing."*

Two independent gates keyed on two different types, and satisfying one gives a green build that still throws.
This is a genuine correctness-of-ergonomics defect: the build says yes, the runtime says no.

**R18-02.** Make `DwarfMapperRegistry` lookup try the source's implemented interfaces after its base chain
(closed generic interfaces are known at registration time — no reflection needed beyond what already happens).
Failing that, at minimum make `DwarfMapMissingException` name *both* the runtime type and the interfaces it
tried, so the "declare two pairs" remedy is discoverable from the message.

### 3.3 A collection pair synthesizes past a declared element pair — the `DWARF024`/`DWARF008` catch-22

> `:8421` — *"a collection pair whose element pair is a class-level `[GenerateMap]` with a factory synthesizes a
> fresh element mapper instead of reusing the declared one, and the synthesized one can't construct."*
> `:8460` — *"the element pair is simultaneously 'has a factory' (declared) and 'must construct itself'
> (synthesized), and no attribute satisfies both."*

Worked around at `:8646` by replacing `[MapConstructor]` with constructor-**parameter** binding, which satisfies
both paths. The generalised lesson recorded at `:9004` is sound — *prefer parameter binding over a factory* —
but the underlying defect stands: **a collection pair should reuse a declared `[GenerateMap]` element pair
rather than synthesizing past it.**

**R18-03.** Make collection/dictionary element resolution consult declared class-level `[GenerateMap]` pairs,
not only declared partial *methods*. Until then, emit a diagnostic when a synthesized element mapper is created
for a pair that is already declared on the same class — right now the divergence is silent.

### 3.4 `SkipNullSourceMembers` — the "all members" feature, and its two real problems

This is where your two observations meet, so it gets the most space.

**Problem A — scope.** Verified: `[DwarfMapper]` is `AttributeTargets.Class`, `[DwarfMapperDefaults]` is
`AttributeTargets.Assembly`. There is no pair or method scope.

> `:481` — *"`SkipNullSourceMembers` lives in the policy layer (assembly or mapper only — not pair-scoped), so
> the 9 `ForAllMembers` maps genuinely do need their own partial class."*

AutoMapper's `ForAllMembers` was **per-map**. So a profile mixing patch-merge maps with ordinary maps cannot be
translated 1:1 — it must be split into extra classes. FusedChat needed `RepositoryLexiconPatchMappers`
(`:3789`) and `DtosNullSafeMappers` (`:5011`) purely to carry one boolean.

**Problem B — and this is a live correctness gap, still open.**

> `:3805` — *"**Known parity gap.** The four patch-class pairs correctly emit `if (src.X is not null)` guards, but
> `UserLexiconDocument → UserLexicon` lives in the main class and **synthesizes its own private copies** of the
> nested document→model helpers, which carry **no null guards**. AutoMapper reused the registered, guarded maps.
> The pair cannot move into the patch class because it also nests `AlertsLexiconDocument → AlertsLexicon`, which
> carries an `[AfterMap]` hook. **This is a real behavioural difference, not a cosmetic one** — Mongo can
> deserialize nulls into these members."*

Precise mechanics (I checked `MapperExtractor.cs:1338`): a synthesized nested helper **does** inherit
`skipNullSrc` — but from *its enclosing class*. So the class split forced by Problem A causes the **same logical
nested pair to exist twice with opposite null semantics**, depending on which class pulled it in. Problem A
causes Problem B.

And the escape hatch is blocked in both directions: the pair can't move into the guarded class because it also
nests a pair carrying an `[AfterMap]` hook.

**R18-05 (repair).** Give `SkipNullSourceMembers` a pair scope — e.g. `[MapNullSkip<S,T>]`, or accept it as a
named argument on `[GenerateMap<S,T>]`. This removes the forced class split, and with it the divergent-duplicate
gap. It is the highest-value ergonomics fix in this list.

**R18-06 (diagnostic, cheap, do it regardless).** When a synthesized nested helper is emitted for a `(S,T)` pair
that another `[DwarfMapper]` class in the same compilation also maps **under different policy options**, emit an
Info/Warning naming both classes and the differing options. That turns a silent behavioural fork into a visible
one, which is this project's whole thesis.

### 3.5 `[MapDerivedType]` arms: the textbook fix doesn't compose

> `:8792` — `DWARF035: [MapDerivedType] pair (AliasCommand, CommandOverviewDataDto) is not mappable: no declared
> partial overload and not auto-nestable.`
> `:8838` — *"`DWARF013` is firing on the reverse direction too — the arm is fighting the existing pair structure."*

The arm must resolve to a declared partial **overload** of the dispatching method; adding one makes the reverse
direction ambiguous, because that pair already owns a method. Renaming into an overload didn't help. The
symptom being fixed was real and user-visible: an `AliasCommand` inside a `List<Command>` silently lost its
`Alias` in API responses, because the collection loop binds at compile time while AutoMapper dispatched on the
runtime type.

It was ultimately solved with an `[AfterMap]` hook doing the downcast by hand (`:8840`) — correct, but that is
polymorphic dispatch re-implemented manually in the presence of a feature built for exactly that.

**R18-07.** Let `[MapDerivedType]` resolve an arm to a **differently-named** declared method (or a class-level
`[GenerateMap]` pair), and stop the arm from making the reverse pair ambiguous. Add the two-sources-one-target
shape to the conformance corpus — `F19` covers polymorphism but evidently not this shape.

### 3.6 Lazy LINQ sources are undeclarable, and fail at runtime

> `:8414` — *"`Where(...)`/`SelectMany(...)` return private `System.Linq` iterator types. No attribute can name
> them and they do not derive from `IEnumerable<T>` — they implement it."*

Five call sites needed a load-bearing `.ToList()`. The limitation is legitimate and inherent to an AOT-safe
exact-pair registry; the *failure mode* is not — it is a runtime throw with a message that names an
unpronounceable type.

**R18-08.** Two cheap improvements: (a) make `DwarfMapMissingException` detect a compiler-generated iterator
source and say *"materialize with `.ToList()` before mapping"*; (b) an analyzer warning at
`Map<ICollection<T>>(expr)` when `expr`'s static type is a lazy LINQ result. Note (b) partially closes the
`DWARF061` blind spot from §3.1 as a side effect.

### 3.7 `[MapConstructor]` blocks `init`-only members, silently

> `:4194` — *"the generated code contains `Identifier = source.Identifier`. Direct construction fills an object
> initializer, where `init` members are assignable, so nothing is lost."*
> `:5096` — *"unlike the `[MapConstructor]` factory path, which is exactly why `UserCommand` still loses
> `Identifier`."*
> `:5034` (Dtos ledger) — a map that *"compiled green but silently dropped `Identifier`, `TotalArguments` and
> `IsCoreCommand`"* through a factory; backed out on the principle **"lossy-but-green is worse than undone."**

Under `[MapConstructor]` the factory owns construction, so `init`-only members can't be assigned afterwards
(`CS8852`) and are silently left at whatever the factory set. This bit the same codebase **twice**, and the
second time it produced a map that passed the build and dropped three members.

**R18-09.** Emit a diagnostic when `[MapConstructor]` is in force and a destination member is `init`-only and
not set by the factory: *"member X cannot be assigned through a factory; it will keep the factory's value."*
This is precisely the "make the silent choice explicit" contract `DWARF001` already embodies. Also document the
`:9004` rule — **prefer constructor-parameter binding to a `[MapConstructor]` factory** — in `options.md`.

---

## 4. Fragment analysis — feature gaps and diagnostic ergonomics

### 4.1 No `IncludeBase` equivalent — the largest single source of hand-work

> `:303` — *"`IncludeBase` | 15 | No primitive — restate shared `[MapProperty]`/`[MapIgnore]` on each derived
> method (script-generated to avoid drift)"*
> `:3838` — *"five `IncludeBase` sites restated (one more than the brief predicted), each bracketed by
> `MIRRORS BASE` / `END MIRRORS BASE` comments"*

The agents invented a *comment convention* to compensate. That is a strong signal: when consumers build
scaffolding around a missing primitive, the primitive is missing. Restated configuration also drifts — silently,
and only in the direction of wrong data.

**R18-14.** Design an inheritance primitive: `[IncludeBase<S,T>]`, or option inheritance from a base mapper
class. Non-trivial (override semantics, diagnostics for conflicting restatements) — worth a spec, not a patch.

### 4.2 The ambient facade has no update-into overload

> `:599` — *"`IDwarfMapper` has no update-into overload — both methods construct a new destination."*
> `:7232` — *"my original '11 call sites' count was wrong because I only scanned `.cs`"* — 5 more in `.razor`.

16 call sites had to inject the **concrete** mapper alongside the facade, which is the one place the migration
was not a swap. Update-into *is* supported (via the concrete mapper, reading method-level attributes) — it just
isn't reachable through the ambient front door.

**R18-15.** Add `void Map<TSource,TDest>(TSource source, TDest destination)` to `IDwarfMapper`, registered from
declared update-into partial methods. If that's a deliberate non-goal, say so in `MIGRATION.md` under a
prominent heading — it is the single biggest call-site delta in a real migration.

### 4.3 Hand-written converters can't join the ambient registry

> `:5205` — *"five are **hand-written converters rather than `[GenerateMap]` pairs** … They are plain methods,
> so they never self-register into the ambient registry."*

Object↔collection shapes and other non-mapping shapes must be hand-written, and then become invisible to every
facade call site and to the parity harness.

**R18-16.** A `[DwarfProvidesMap]`-style opt-in attribute on a hand-written method that registers it into the
ambient registry under an explicit `(S,T)` key.

### 4.4 `required` + `[MapIgnore]` is a compile error, with no guidance

> `:3370` — *"the profile did `.Ignore()`, but `Id` is `required`, so `[MapIgnore]` yields an illegal object
> initializer (`CS9035`)"*
> `:3792` — *"each is `required`, so `[MapIgnore]` is `CS9035`"*
> `:3841` — *"profile ignored it, but it is `required` (`CS9035`)"*

Three separate agents hit it and each independently invented the same workaround (`[MapValue("")]` /
`Use = …null!`). The user is told nothing by `CS9035`; they have to reverse-engineer the generated initializer.

**R18-10.** Diagnostic: *"destination member X is `required`; `[MapIgnore]` would emit an illegal object
initializer. Use `[MapValue]` to supply a placeholder, or make X non-`required`."*

### 4.5 `CS8795` is a cascade, and it hides the real error

> `:3499` — *"I told agents 'CS8795 means the analyzer isn't wired.' That's one cause, but here it's a cascade —
> the generator hit `DWARF007`/`DWARF024`/`DWARF026`, so it emitted nothing, and every partial method then
> looked unimplemented."*

Recurs at `:6540`, `:7272`, `:8829`. Any generator error suppresses emission for the whole class, producing a
wall of `CS8795` that buries the actual `DWARF…` line.

**R18-17.** When a class fails to emit because of a reported diagnostic, emit a stub or a single explanatory
diagnostic — *"no code was generated for `M`; fix the DWARF errors above"* — so the first thing the user reads is
the real cause. Cheap, high-value.

### 4.6 The per-project `PrivateAssets="all"` opt-in trap

> `:1017` — *"Wiring is proven in 1 of 7 projects. The other 6 compile, but with no `[DwarfMapper]` classes yet
> that only proves the reference resolves, not that the analyzer runs there."*

The generator deliberately does not flow transitively, so every project declaring mappers needs both references.
The failure mode is a `CS8795` wall (see 4.5) that looks like an attribute-syntax problem.

**R18-18.** Document the two-line-per-project pattern prominently in `MIGRATION.md`, including the
`Directory.Build.targets`-not-`.props` subtlety (`:826` — `.props` imports before the csproj body, so a
`<UseDwarfMapper>` property set in the project isn't visible to a condition there). That one is a genuinely
non-obvious MSBuild fact the record establishes with evidence.

---

## 5. Documentation repairs

Round 18 produced a behavioural-differences list that belongs in `docs/howto/migrate-from-automapper.md` as a
**pre-flight checklist**. Each of these was discovered the expensive way.

**R18-19 — behavioural differences checklist.**

| Difference | Record | Consequence if missed |
|---|---|---|
| `NullStrategy` defaults to `Throw`; AutoMapper substituted `default` | `:472` | *"would have built clean and failed in production"* |
| `EnumStrategy` defaults to `ByName`; AutoMapper is `ByValue` | `:370` | silent value shift |
| enum→string prefers `[EnumMember]`/`[Description]` over the identifier | `:3109` | **`DonationSource.Kofi` would persist as `"Ko-Fi"` instead of `"Kofi"`, breaking reads of every existing Mongo document** |
| `SkipNullSourceMembers` guards *nullable-typed* members; AutoMapper's `Condition` was a runtime value check | `:5043` | a non-nullable-typed member holding a runtime null is now copied |
| enum→string parse is case-**sensitive**; `Enum.Parse` ignored case | `:5382` | throws on hand-edited data |
| the registry needs **two keys** per facade call site | `:8604` | green build, runtime throw |
| declare a collection pair beside its **element** pair, not its call site | `:8419` | fresh convention-only map fails completeness |
| a `private` "ctor for automapper" must become `internal` + `[InternalsVisibleTo]` | `:3898` | `DWARF026`, no escape |
| `CS8795` is a cascade — read the `DWARF…` lines above it | `:3499` | hours lost on the wrong hypothesis |

**R18-04 — suppression table.** `docs/diagnostics.md` still implies `#pragma warning disable` works for
`DWARF…` ids generally. It works for none of them. Fix the general table the way `DWARF076`'s section was fixed.

**R18-20 — `MIGRATION.md` non-goals.** State plainly, with the reason, what does not survive a migration:
`ResolutionContext` / `opts.Items` (`:5058` → becomes an ordinary method parameter), the runtime
`Map(obj, srcType, destType)` form, open-generic converters, `IncludeBase`, object↔collection shapes, and
facade update-into (pending R18-15).

---

## 6. The bigger testing project — you are right, and here is the precise shape

Your instinct is correct, and the record proves *why* rather than merely suggesting it. Measured coverage of the
policy options in `samples/`:

| Option | Covered in `samples/`? |
|---|---|
| `CaseInsensitive`, `NameConvention`, `EnumStrategy`, `NullStrategy`, `NullCollections`, `AutoNest`, `ReferenceHandling`, `OnCycle`, `RequiredMapping` | yes |
| **`SkipNullSourceMembers`** | **no** |
| **`AllowNonPublic`** | **no** |
| **`AutoMatchMembers`** | **no** |
| **`IgnoreObsoleteMembers`** | **no** |
| **`[MapConstructor<S,T>]`** (pair-scoped *factory*) | **no** — note `F23` covers `[DwarfMapperConstructor]`, which is ctor *selection*, a different attribute |
| **`[assembly: DwarfMapperDefaults]`** | **no** |

Six of fifteen policy options, plus the pair-scoped factory attribute, are exercised **nowhere** in Gallery
(26 examples) or Conformance (F01–F30). Four of those six are precisely what a real migration reaches for
first.

But adding six more Conformance features would not have caught a single Round-18 defect. **Every one of them
needed a shape the current corpus structurally cannot express**: more than one assembly, a service container, an
ambient registry resolving at runtime, a polymorphic list, a top-level collection pair. `DwarfMapper.Conformance`
is one project, one assembly, no DI.

**R18-21 — `tests/DwarfMapper.ConsumerTests`: FusedChat in miniature.**

A small **multi-project** solution slice, four assemblies, wired exactly as a consumer wires it, with **runtime**
assertions rather than generated-source assertions:

```
ConsumerTests.Contracts     — domain types + DTOs; no mapper
ConsumerTests.ProviderA     — [DwarfMapper] classes, public, self-registering
ConsumerTests.ProviderB     — a second provider; overlapping nested pairs, DIFFERENT policy options
ConsumerTests.Host          — DI container, [assembly: DwarfMapperValidationRoot], the actual tests
```

The `Host` assembly must **not** reference the providers' mapper types directly — that is the whole point of the
ambient registry, and it is the condition under which the 47-site blocker exists.

> **Hard constraint (maintainer, 2026-08-12): the project must be *opaque* — no reference to any existing
> project in this repo.** Not the other test projects, not `samples/`. No shared fixtures, no shared harness, no
> shared type corpus. It takes a `ProjectReference` to `src/DwarfMapper` plus the generator as
> `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` — the same two lines a real consumer writes — and
> owns its own domain types.
>
> The reasoning is the point of the whole exercise: reusing the existing corpus would import the existing
> corpus's blind spots, which is exactly what let all five Round-18 defects through. An opaque project can only
> pass by exercising the real consumer surface.
>
> One sub-decision to confirm before building: whether `DwarfMapper.Testing` may be referenced. It is a
> *shipped* library, so a real consumer legitimately could — but if fixtures must be fully self-owned,
> hand-write them.

What it asserts, each line traceable to a Round-18 failure:

1. **Ambient resolution across assemblies** — `IDwarfMapper.Map<TDest>(source)` for every declared pair,
   resolved from `Host` with no project reference to the mapper class. (§3.1)
2. **Collection shapes through the facade** — `List<S>`, `S[]`, `IEnumerable<S>`, and a lazy `Where()`
   iterator, asserting either a correct map or a *good* exception message. (§3.1, §3.6)
3. **Static-vs-runtime type divergence** — a method declared `ICollection<T>` returning `List<T>`. (§3.2)
4. **Polymorphic elements** — an `AliasCommand`-shaped derived type inside a `List<Base>`, asserting the derived
   member survives. (§3.5)
5. **Options matrix** — one mapper class per policy option, each asserting the *observable runtime difference*
   the option makes; and specifically a nested pair reachable from two classes with **different**
   `SkipNullSourceMembers`, asserting they don't silently diverge. (§3.4)
6. **Construction paths** — `[MapConstructor]` factory vs. constructor-parameter binding over the same type,
   asserting `init`-only members survive the second and are visibly lost by the first. (§3.7)
7. **Non-public access** — `internal` ctor + `[InternalsVisibleTo]` + `AllowNonPublic = true`, across the
   assembly boundary. (`:3898`)
8. **Update-into** — through the concrete mapper, and through the facade once R18-15 lands. (§4.2)
9. **DI-graph smoke test** — build the container, resolve every registration, assert **0 unresolvable**. This is
   the single check that caught the blocker, and it is ~30 lines.

**R18-22 — negative/expected-diagnostic project.** A companion project compiled with
`ExpectedDiagnostics` assertions, so "this shape must produce `DWARF0nn`" is testable end-to-end rather than
only at the snapshot layer. It gives R18-06, R18-09, R18-10 and R18-17 a home.

**Sequencing note.** R18-21 is worth building *before* R18-01/02/03, because it is the harness that proves those
fixes and would have caught them originally.

---

## 7. Your second ask — show "all members" in the examples

Confirmed: `SkipNullSourceMembers` appears in `docs/options.md` and `docs/howto/migrate-from-automapper.md`, and
in **zero** sample files. It is documented and undemonstrated, which is exactly how a present feature gets asked
for repeatedly.

**R18-23 — Gallery example `27_PatchMerge.cs`.** The patch-merge idiom is the reason the option exists and the
reason it kept coming up, so show *that*, not the flag:

- a `PatchDto` with nullable members, merged into a populated entity via update-into;
- a null member visibly **not** clobbering the destination;
- the contrast with the default (null overwrites), side by side, so the difference is legible;
- a note that the option is class-scoped — and, once R18-05 lands, the pair-scoped form.

**R18-24 — Conformance `F31`–`F36`** for the six uncovered options above, each asserting an observable runtime
difference. Cheap, mechanical, and it makes the "every option is exercised" claim true.

**R18-25 — a coverage ratchet.** A test that reflects over the public option surface and fails when an option
has no Conformance feature. This repo already ratchets doc fences and snippet regions; the same discipline
applied to the *option surface* would have prevented this gap from forming. Given the standing note in
`CLAUDE.md` about derived-documentation ratchets, this fits the house style exactly.

---

## 8. Consumer-side leftovers (FusedChat, not DwarfMapper)

Kept separate deliberately — these are MedbotOmega's to close, not this repo's.

- **`UserCommand.Identifier` still lost** (`:9114`). Fix is the `internal` + `[InternalsVisibleTo]` treatment you
  approved for `CustomizedCommand`. It changes a domain type, so it was left for you.
- **`verify` exits 1 (233/244)** (`:9113`). Every difference is explained, but it needs a committed
  accepted-divergence allowlist before it can be a CI gate.
- **One divergence is DwarfMapper being right** (`:6001`): `ViewsCounter.PlatformsToShow` source `[4,5]` →
  AutoMapper `[0]`. AutoMapper could not convert `HashSet<BotPlatform>` → `HashSet<int>` element-wise and
  collapsed the set to a single zero. That field is *which platforms the user chose to display*. **The migration
  fixed a live data-loss bug in production** — that is a genuinely quotable result for `COMPARISON.md`, with the
  caveat that it is one anecdote, not a benchmark.
- **23 commits on `mapper-to-dwarf-transition`**, unpushed, awaiting review.

---

## 9. Task list

Ordered by value-per-effort within each group. Nothing here is started.

All 35 items are mirrored into the session task list with dependencies wired. Decisions are
tracked as tasks too — `R18-D2` blocks `R18-01`, `R18-D3` blocks `R18-15`, `R18-D4` blocks `R18-14` — so a
pending answer cannot quietly become a stalled repair. `R18-21` blocks `R18-01`/`R18-02`/`R18-03` because it is
the harness that proves them.

### Do first

| ID | Task | Effort |
|---|---|---|
| **R18-00** | Merge `fix/use-converter-scoping-and-dwarf076-suppression` into `master`. Whole-solution build incl. samples verified clean (0 errors, 0 warnings); 6,111 tests green per `:9110`. | S |
| **R18-21** | `tests/DwarfMapper.ConsumerTests` — the multi-assembly runtime harness (§6). Build this before the fixes it validates. | L |
| **R18-01** | Auto-register common collection shapes for every declared element pair, at compile time. Closes the 47-site blocker for all future consumers. | M |
| **R18-05** | Pair-scoped `SkipNullSourceMembers`. Removes the forced class split *and* the divergent-duplicate gap. | M |

### Repair — correctness

| ID | Task | Effort |
|---|---|---|
| **R18-02** | Registry lookup: try implemented interfaces after the base chain; or name them in the exception. | S |
| **R18-03** | Collection element resolution must reuse a declared class-level `[GenerateMap]` pair; diagnose silent synthesis otherwise. | M |
| **R18-06** | Diagnose the same `(S,T)` nested pair synthesized under differing policy options in one compilation. | S |
| **R18-07** | `[MapDerivedType]` arms: allow a differently-named declared method; don't poison the reverse pair. | M |
| **R18-09** | Diagnose `init`-only members unassignable under `[MapConstructor]`. | S |
| **R18-12** | Audit declared top-level pairs whose target is value-like (string, primitive, `Guid`, member-less struct) for the §2.2 bug class. | S |
| **R18-13** | Audit the fuzz/`[RoundTrip]` corpus for polymorphic coverage; add abstract/interface-membered graph types. The green post-fix run is the evidence they're absent. | M |
| **R18-11** | Decide the policy on signature-matched auto-adoption of arbitrary user methods as converters; at minimum, diagnose ambiguity/action-at-a-distance. | M |

### Repair — ergonomics and messages

| ID | Task | Effort |
|---|---|---|
| **R18-17** | Stop the `CS8795` cascade burying the real `DWARF…` error. | S |
| **R18-10** | Diagnose `required` + `[MapIgnore]` with the `[MapValue]` remedy. | S |
| **R18-08** | Lazy-LINQ source: better exception text + an analyzer hint at the call site. | M |
| **R18-16** | Registration opt-in for hand-written converters. | S |

### Features

| ID | Task | Effort |
|---|---|---|
| **R18-15** | Facade update-into overload — or an explicit documented non-goal. | M |
| **R18-14** | `IncludeBase` equivalent. Spec first. | L |

### Coverage and docs

| ID | Task | Effort |
|---|---|---|
| **R18-23** | Gallery `27_PatchMerge.cs` — the "all members" ask. | S |
| **R18-24** | Conformance `F31`–`F36` for the six uncovered options. | M |
| **R18-25** | Coverage ratchet over the public option surface. | S |
| **R18-19** | Behavioural-differences pre-flight checklist in `migrate-from-automapper.md`. | M |
| **R18-04** | Correct the general suppression table — `#pragma` works for no `DWARF…` id. | S |
| **R18-20** | `MIGRATION.md` non-goals section. | S |
| **R18-18** | Per-project wiring + `.targets`-not-`.props` documentation. | S |
| **R18-22** | Expected-diagnostic negative test project. | M |
| **R18-26** | Record the `PlatformsToShow` AutoMapper data-loss finding in `COMPARISON.md` — as one anecdote with its triggering shape, not as a benchmark. | S |

### Decisions (tracked, because an unanswered question is a stalled repair)

| ID | Blocks | Question |
|---|---|---|
| **R18-D2** | R18-01 | Collection auto-registration: opt-in, opt-out, or call-site-scoped? |
| **R18-D3** | R18-15 | Facade update-into: feature, or documented non-goal? |
| **R18-D4** | R18-14 | `IncludeBase`: full primitive, code-fix-assisted restatement, or out of scope? |
| **R18-D5** | — | enum→string `[Description]` precedence: configurable, narrowed, or warned about? |

### Consumer-side (MedbotOmega — listed for completeness, not this repo's work)

| ID | Task |
|---|---|
| **R18-C1** | Recover `UserCommand.Identifier` via the `internal` ctor + `[InternalsVisibleTo]` treatment. |
| **R18-C2** | Commit an accepted-divergence allowlist so `verify` can gate CI; assert allowlisted pairs still diverge. |
| **R18-C3** | Review and merge `mapper-to-dwarf-transition` — blocked on R18-00, R18-C1, R18-C2. |
| **R18-C4** | De-duplicate `BadgeData → BadgeDataDto`, declared on two classes. |

---

## 10. Open questions for you

Each is tracked as a **decision task** (`R18-D…`) so it does not evaporate. Q1 is closed.

1. ~~**Is "all members" `SkipNullSourceMembers`?**~~ **Answered 2026-08-12: yes.** R18-05 and R18-23 proceed as
   written.
2. **R18-01 — is compile-time collection auto-registration acceptable?** It grows the generated registration
   table for every declared pair. An opt-out (`[DwarfMapper(RegisterCollectionShapes = false)]`) or an opt-*in*
   are both defensible. Your call, since it trades binary size against the single worst consumer trap found.
3. **R18-15 — is facade update-into a deliberate non-goal?** The record treats it as a gap; you may have
   designed it out on purpose. Either answer is fine, but it should be *stated*, because 16 call sites in one
   codebase hit it.
4. **R18-14 — is `IncludeBase` in scope at all?** Two consumers-worth of evidence says it is the largest single
   source of hand-work, but it is also the largest design surface in this list.
5. **The `[Description]` enum→string precedence** (`:3109`) is a *feature* that behaved as a data-corruption
   hazard for a migrating consumer. Should enum→string precedence be settable per-mapper, or is the
   migration-guide warning (R18-19) enough?
