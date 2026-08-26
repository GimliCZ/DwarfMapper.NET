<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Audit: the public surface, before it is frozen

**2026-08-26.** The maintainer decided to promote the 278 `PublicAPI.Unshipped.txt` entries to
`Shipped.txt` — arming the stability ratchet — **after** an audit. This is that audit.

The rule that makes it urgent: **while a symbol sits in Unshipped, renaming or removing it is free and the
analyzer says nothing. After promotion it is a declared break.** Every accident promoted is an accident kept.

Scope: `src/DwarfMapper` (278 entries) and `src/DwarfMapper.Testing`. `CodeFixes` is `IsPackable=false` and
`DocTooling` is internal tooling — neither ships, so neither needs a baseline. The generator's consumer-facing
types all live in `DwarfMapper`.

---

## Not a defect: the empty `Shipped.txt`

Recorded here because it looks alarming and is not. `PublicAPI.Shipped.txt` holds one line, `#nullable
enable`; all 278 entries are in `Unshipped.txt`. That is PublicApiAnalyzers working as designed —
**the library has never released.** `git tag -l` returns one tag, `mapconfig-pre-rebase`, which is a branch
backup rather than a version, and `CHANGELOG.md` has only an `[Unreleased]` section.

---

## [A1] Infrastructure and consumer API are indistinguishable

**No `EditorBrowsable` attribute appears anywhere in `src/DwarfMapper`.** So a type that exists *only* because
emitted code must call it looks, in IntelliSense and in the frozen baseline, exactly like a type a consumer is
invited to use.

`DwarfRefContext` is the clear case. Verified: the only thing that constructs it is the generator's own
emitter (`MapEmitter.cs:271, :873, :986` write `new global::DwarfMapper.DwarfRefContext(...)` into generated
source). No consumer code, no sample, no test constructs one directly. It is public because **generated code
in other assemblies must reach it** — a real constraint, not an oversight.

But the consequence of promoting it unmarked is that five members (`TryEnterNode`, `ExitNode`, `SetReference`,
`TryGetReference`, and the `AbsoluteMaxDepth = 1000` const) become a frozen contract that nobody designed as
one, on a type whose whole purpose is cycle bookkeeping for the emitter.

**Proposed before promotion:** mark genuine infrastructure `[EditorBrowsable(EditorBrowsableState.Never)]`
and say so in its XML doc. This changes no signature — the surface stays exactly as large — but it labels the
frozen contract for what it is, and it stops the type appearing in consumer completion lists.

This needs a classification pass over all public types, not just this one. `DwarfMapperFacade.Instance`,
`IDwarfMapper` and the exception types are consumer-facing; `DwarfRefContext` is not. The rest need deciding.

---

## [A2] `DwarfRefContext`'s constructor is the REG-02 hazard, on the public surface

```csharp
public DwarfRefContext(int maxDepth, bool preserve = false, bool setNull = false)
```

Two adjacent same-typed optional booleans. This is precisely the shape
`ResolverParameterDisciplineTests` was built this round to forbid inside the generator — and here it is on the
API about to be frozen forever.

**The emitter already calls it two different ways.** Counted across generated output in the test and benchmark
trees:

| emitted form | sites |
|---|---:|
| `new DwarfRefContext(10, true)` — **positional bool** | **28** |
| `new DwarfRefContext(1000, preserve: false, setNull: true)` — named | 13 |
| `new DwarfRefContext(16)` — depth only | 27 |

`(10, true)` does not say *true what*. Today it means `preserve`. If the two parameters were ever transposed,
28 emitted call sites would silently change meaning and every one would still compile — the exact failure
mode ISSUE-043 and ISSUE-044 were, which is why the generator now bans the shape internally.

**Why now and not later:** the only caller is the emitter, so changing the signature costs a regeneration and
nothing else. After promotion it is a declared break for every consumer, forever, to fix a hazard that costs
nothing to fix today.

**Proposed:** make the two flags non-optional and have the emitter always pass them by name (the 13 sites
already do); or replace them with a single flags enum, which removes the transposition class entirely. Either
is free now. The golden manifest will move — this is an emitted-bytes change, not a structural one — so it
lands as a deliberate re-bless with the diff reviewed, not under the byte-identity lock.

---

## [A3] Optional parameters elsewhere on the public surface — reviewed, mostly fine

Five public members carry defaults:

| member | verdict |
|---|---|
| `AutoNestAttribute(bool enabled = true)` | **keep** — `[AutoNest]` / `[AutoNest(false)]` is the intended idiom |
| `MapNullSkipAttribute(bool enabled = true)` | **keep** — same idiom, both generic and non-generic forms |
| `DwarfMapMissingException(…, bool isUpdate = false)` | **review** — a trailing bool on an exception ctor; low risk, but it is the fourth positional argument |
| `DwarfRefContext(…, bool preserve, bool setNull)` | **fix** — see A2 |

The attribute idiom is deliberate and reads correctly at the use site, which is the test that matters. Listed
so the exemption is a decision rather than an oversight.

---

## What this audit does not cover

The **278 entries have not each been read**. This pass looked for structural hazard classes — infrastructure
leaking as consumer API, and the parameter shapes this repository has already been bitten by twice. A
member-by-member review of every property and overload is the remaining work before promotion, and it is
where an accidentally-`set`-able property or a vestigial overload would surface.

`DwarfMapper.Testing`'s baseline is also unaudited. It ships, so it needs the same pass — and §1b already
found two overlapping object factories in it (N5), which is exactly the kind of thing that must be settled
*before* a freeze rather than after.
