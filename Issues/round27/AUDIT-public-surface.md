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

## [A4] The member-by-member pass — COMPLETE, and the surface is clean

Done 2026-08-26, before promotion. Rather than reading 278 lines for typos, the pass hunted the hazard that
actually matters: **accidental surface** — a public member nothing was ever meant to call, which promotion
would freeze forever.

Method: probe every declared type and member against the consumer-shaped corpus (`tests/`, `samples/`,
`benchmarks/`). A public element no consumer-shaped code touches is either infrastructure — and must be marked,
per A1 — or vestigial, and should be deleted while that is still free.

| population | result |
|---|---|
| public types unreferenced by consumer-shaped code | **0** |
| public members unreferenced by consumer-shaped code | **0** |

A first pass reported 8 unreferenced types and 3 members. Both were artifacts of the probe, not findings.
Attribute *usage* is `[AutoNest]`, not `AutoNestAttribute`, so the full type name never appears at a use site.
And the three members — `GenerateWrapperMap.Wrapper`, `MapCollectionKey.CollectionMember` and `.KeyMember` —
are get-only properties backed by **required** constructor parameters, used positionally: the read-back of a
mandatory argument rather than surface nobody asked for. Corrected probe: zero in both populations.

So the surface carries no vestigial members. The two structural findings above, A1 and A2, were the whole
debt, and both were fixed before the freeze.

---

## The freeze is armed, and it was proved rather than assumed

277 entries promoted for `DwarfMapper` and 38 for `DwarfMapper.Testing` — **315 total**. From here a rename or
a removal is a declared break, which is the point of doing it now.

Proved rather than assumed: a public enum member was added, the analyzer refused it with **RS0016**, and the
member was reverted. A ratchet nobody has watched bite is a ratchet nobody knows is connected.

---

## What this audit did not cover

The entries were probed for accidental surface rather than read line by line for taste. A member whose *name*
or *shape* is merely unfortunate — a noun where a verb belongs, an overload that could have been a default —
would not be caught here, and is now frozen. That trade was accepted deliberately: the alternative was to
delay the freeze through the restructuring rounds, which is exactly when an accidental surface change is most
likely to slip in unnoticed.
