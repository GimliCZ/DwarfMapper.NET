# Design: shared engine core — ending the two-engine drift

- **Date:** 2026-07-22
- **Status:** Approved (design); ready for implementation planning
- **Component:** `DwarfMapper.Generator` (both engines) + tests

## 1. Problem

**Sub-project 2 of four** in the generator maintainability programme:

1. ~~generator testing framework~~ — landed (`4e79370`)
2. **shared engine core** ← this spec
3. emission layer (indented writer)
4. split `MapperExtractor.cs` (7,265 lines)

DwarfMapper has **two mapping engines**: the `[DwarfMapper]` class model (`MapperExtractor`) and the `[MapTo]`
registry front door (`MapToGenerator`). They re-implement the same concepts independently, and that drift has a
measured cost — it directly caused **5 of the 32 audit issues**:

| Issue | What drifted |
|---|---|
| 003 | `IsCrossCategoryNumeric` was **private** to `MapperExtractor`, so the registry emitted silently lossy numeric conversions |
| 009 | Registry checked property accessibility but not **accessor** accessibility → CS0272/CS0271 from generated code |
| 010 | Registry had no `To{Name}` collision check → CS0111 |
| 011 | Registry had no parameterless-ctor check → CS1729 |
| 020 | Registry called the shared `TryGetEnumerableElement` and **discarded the count it asked for** |

Two of the four still-open audit issues (015 divergent hashes, 019) are the same root cause. Each was fixed as an
instance; the *class* of defect remains open.

**A new divergence found while designing this spec — and it is worse than the audit's.** The class model's
`ReadableMembers`/`WritableMembers` walk the base-type chain and interfaces with name de-duplication and
accessor-usability rules. `MapToGenerator` enumerates only `type.GetMembers()` — the file contains **zero**
`BaseType` references. So for a `[MapTo]` type with inherited members:

- an inherited **destination** member is never enumerated → **silently never mapped** (silent data loss, which
  this library's core tenet forbids);
- an inherited **source** member is invisible → the destination reports `DWARFR02` "unmapped" for a member that
  genuinely exists (a loud but *wrong* diagnostic).

No test covers `[MapTo]` with inheritance — a corpus hole of exactly the family the audit kept finding.

## 2. Goals / Non-goals

**Goals**

- One home for the member/type facts both engines need, so a rule cannot be true in one engine and false in the
  other.
- Fix the inherited-member divergence (silent data loss) as a consequence of unification, not as a side quest.
- Close ISSUE-015 (hash duplication) honestly.
- Leave the drift **structurally harder to reintroduce**, not merely absent today.

**Non-goals**

- **Unifying the conversion/synthesis chain** (`MapToGenerator.Resolver` vs `MapperExtractor.TryResolveConversion`).
  Both *emit code as strings*, so they are entangled with emission; unifying them before the indented-writer
  layer exists (sub-project 3) means doing that work blind. The scalar converters (`NumericConverter`,
  `ParsableConverter`, `EnumConverter`) are **already shared**, so the remaining win is small and the risk is the
  highest in the programme. Deferred deliberately, recorded here so it is not mistaken for an oversight.
- Splitting `MapperExtractor.cs` (sub-project 4).
- Any change to the `[DwarfMapper]` class model's observable behaviour. Only the registry's behaviour changes,
  and only where it was wrong.
- Renaming generated helpers. See §4.1.

## 3. Architecture

A new `src/DwarfMapper.Generator/Core/` namespace holding engine-agnostic facts. Both engines call it; neither
owns it.

```
Core/
  StableHash.cs      FNV-1a, one home, algorithms named (§4.1)
  TypeFacts.cs       fully-qualified display, object-type classification, target validity (§4.2)
  MemberFacts.cs     readable/writable member enumeration incl. base walk + accessor rules (§4.3)
        ▲                                   ▲
        │                                   │
  MapperExtractor (class model)      MapToGenerator (registry)
```

`Core` depends only on Roslyn. It must **not** reference `MapperExtractor`, `MapToGenerator`, or any emitter —
that direction of dependency is what let the rules drift in the first place.

## 4. Components

### 4.1 `StableHash`

FNV-1a currently exists in **10 files**. Nine are byte-identical; `NestedMappingRegistry`'s is algorithmically
different (per-byte: `hash ^= (byte)(c & 0xFF)` then `hash ^= (byte)(c >> 8)`, versus per-char `h ^= c`).

Hashes feed **generated helper names** (`__DwarfMapObj_<hash>`, `__DwarfMap_Coll_<hash>`, …). Therefore:

- the nine identical copies collapse into `StableHash.Fnv1a` — **behaviour-neutral, zero manifest churn**;
- the per-byte variant becomes `StableHash.Fnv1aPerByte` **in the same file**, with a comment stating that it
  differs, why it is kept, and that changing it would rename generated helpers across the corpus.

This closes ISSUE-015 as *"one home, both algorithms named and visible"* rather than as a rename storm that
would produce a large manifest diff for no behavioural benefit. The divergence stops being accidental.

### 4.2 `TypeFacts`

Engine-agnostic type questions, today duplicated or engine-private:

- `Fq(ITypeSymbol)` — fully-qualified display (duplicated verbatim in both engines).
- `IsObjectType(INamedTypeSymbol)` — "is this a mappable object rather than a scalar/collection".
- `IsMappableTarget(INamedTypeSymbol target, INamedTypeSymbol source)` and
  `HasAccessibleParameterlessCtor(INamedTypeSymbol)` — the target-validity rules whose absence produced
  ISSUE-011.

### 4.3 `MemberFacts` — the substantive change

One implementation of readable/writable member enumeration, carrying the class model's semantics:

- walks the **base-type chain**, and interfaces (which have no base chain);
- **de-duplicates by name** so a shadowing override does not yield twice;
- applies **accessor-level** usability, not just property-level (the ISSUE-009 rule);
- excludes indexers, constants, implicitly-declared and (for writables) read-only fields;
- supports the class model's existing `allowNonPublic` + `Compilation` parameters, which the registry passes as
  the defaults (`false`/`null`) — the registry has no `AllowNonPublicConstructors` equivalent today, and this
  spec does not add one.

**Return shape.** The two engines want different things: the class model uses `(string Name, ITypeSymbol Type)`,
the registry needs the `ISymbol` itself (to read `[MapProperty]`/`[MapIgnore]` attributes off it). `MemberFacts`
returns the richer `(ISymbol Symbol, string Name, ITypeSymbol Type)`; the class model projects to its pair at the
call site. One enumeration, no information lost.

**This is the behaviour change.** The registry gains base-type walking, so inherited members become visible and
the silent data loss is fixed.

## 5. Testing strategy

The safety net from sub-project 1 is what makes this tractable — but it must be used deliberately, not
retrospectively.

**Characterise before changing.** Add a `[MapTo]`-with-inheritance test *before* the refactor, asserting the
current (wrong) behaviour is wrong — i.e. a test that fails today. Because **no existing corpus case uses
inheritance with `[MapTo]`**, this is manifest-neutral: the 971 pinned fingerprints do not move. The history then
reads *bug proven → core extracted → bug fixed*, rather than one opaque diff in which a behaviour change and a
code move are indistinguishable.

**Expected golden-manifest impact.**

| Change | Manifest effect |
|---|---|
| `StableHash` consolidation (9 identical copies) | none — same algorithm, same names |
| `TypeFacts` extraction | none — pure moves |
| `MemberFacts` adoption by the class model | none — same semantics, same implementation |
| `MemberFacts` adoption by the registry | **only** cases whose types have base classes — none exist in the corpus today |

So a correct implementation should leave all 971 fingerprints **unchanged**. Any movement is a signal to
investigate, not to regenerate. New golden cases are added for the inheritance shapes so the fixed behaviour is
pinned going forward.

**Ratchet.** A test asserting that `MapToGenerator` and `MapperExtractor` contain no private re-implementation of
the extracted concepts — concretely, that neither file declares its own `ReadableMembers`, `WritableMembers`,
`IsObjectType`, `Fq`, or an FNV constant (`2166136261`). This is what makes the drift structurally harder to
reintroduce rather than merely absent today, and it is the same mechanism used throughout sub-project 1.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Registry gains base-walk → unexpected output change | Golden corpus over 971 cases; any unexpected fingerprint move is investigated, never blanket-regenerated |
| Inherited members now visible → previously-passing user code gains new mapped members | This is the bug fix. It is a **behaviour change in a prototype-tier feature** (`RegistryDiagnostics` documents `[MapTo]` as prototype). Documented in the diagnostics doc. |
| Shared core becomes a dumping ground | Scope fixed to three files with stated responsibilities; the conversion chain is an explicit non-goal |
| Consolidating hashing renames helpers | Explicitly avoided — the divergent variant is kept and named (§4.1) |

## 7. Out of scope

- The conversion/synthesis chain (sub-project 3).
- `MapperExtractor.cs` splitting (sub-project 4).
- Remaining open audit issues 016, 019, 023 — 019 is blocked on the emission layer; 016 and 023 are unrelated to
  engine drift.
