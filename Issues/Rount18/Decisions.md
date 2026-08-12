<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Round 18 — decision record

Four questions raised by `ForensicAnalysis.md` §10. Each is answered here so the repairs that depend on them
are not stalled, with the reasoning written down so the maintainer can overturn any of them cheaply.

**Status: decided by Claude under the "complete task list" directive, 2026-08-12. Maintainer-overridable.**
Where a decision is genuinely close, the runner-up is recorded with what would flip it.

---

## D2 — Collection auto-registration: opt-in or opt-out?

**Decision: ON by default, opt out with `RegisterCollectionShapes = false`.**

**And a design finding that changes the cost:** register on the *source interface*, not on every concrete
source type — which means **D2's implementation depends on R18-02** (interface lookup), not merely coexists
with it.

The registry resolves by `source.GetType()`, walking base types and — once R18-02 lands — implemented
interfaces. So a single row keyed on `IEnumerable<S>` is reached by a `List<S>`, an `S[]`, a `HashSet<S>` and
a lazy LINQ iterator alike. That collapses the source axis from "every concrete collection type a consumer
might hold" to **one**:

| | Naive (source × target) | Source-interface axis |
|---|---:|---:|
| Rows per declared element pair | ~30 | **~6** |

Six rows per pair, one per *requested destination* shape — `List<T>`, `T[]`, `ICollection<T>`,
`IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`.

**Why ON by default.** The footprint objection was the whole case for opt-in, and at six rows per pair it is
weak. Against it: this is the trap that produced 47 latent runtime throws behind a green build and slipped past
three independent safety nets. Opt-in leaves it armed for exactly the population that hits it — consumers who
do not yet know the option exists. A default that is safe, with an escape hatch for anyone who measures a
problem, is the right shape.

**Runner-up:** call-site-scoped registration (option (c)), which is strictly smaller. Rejected because it
cannot see cross-assembly call sites, and cross-assembly is the ambient registry's entire purpose.

**What would flip this:** evidence that the registration table is a measurable size or startup cost in a real
AOT-published app. Worth measuring in `DwarfMapper.AotBench` once implemented; if the delta is material, switch
the default and say so in the release notes.

---

## D3 — Facade update-into: feature, or documented non-goal?

**Decision: add it.** `void Map<TSource, TDest>(TSource source, TDest destination)` on `IDwarfMapper`.

The strongest argument for "non-goal" was that the facade is type-erased — `Map<TDest>(object)` — and
update-into needs both types. That argument does not survive contact with the signature: the proposed overload
is **fully generic in both directions**, so both types are statically known at the call site. It is no more
type-erased than the existing `Map<TSource, TDest>(TSource)`, which already ships.

The capability exists; only the front door lacks it. Update-into is generated today from declared partial
methods reading method-level `[MapProperty]`/`[MapIgnore]` — the migration used it at 16 sites by injecting the
concrete mapper alongside the facade. That is a workable pattern and also the single place a ~300-map migration
was not a near-verbatim swap.

**Scope note for R18-15.** `[MapCollectionKey]` (update-into only, merge-by-key) rides on the same path and
should be reachable through the facade too — otherwise the gap simply moves.

**What would flip this:** if registering update-into delegates meaningfully complicates the registry's key
space (it needs a distinct key from the create-map for the same pair). Check that first; if it does, the
fallback is the documented non-goal plus a prominent MIGRATION.md pattern, per R18-20.

---

## D4 — `IncludeBase` equivalent: primitive, code fix, or out of scope?

**Decision: code-fix-assisted restatement, plus a drift diagnostic. Not an inheritance primitive.**

The problem is real — it was the largest source of hand-work in the migration, and the agents independently
invented a `MIRRORS BASE` / `END MIRRORS BASE` comment convention to keep restatements traceable. But the
*cost* of restatement splits into two parts, and they are not equally bad:

| Cost | Severity |
|---|---|
| Typing the restatement | annoying, one-time, mechanical |
| **Restatement drifting from the base later** | **silent, and only ever toward wrong data** |

An inheritance primitive solves both. A code fix plus a diagnostic solves the second — the one that actually
hurts — at a fraction of the design surface, and without introducing override semantics that would have to
interact with pair-scoped attributes, `[MapDerivedType]`, and the policy layer.

It is also more in keeping with this project's philosophy. Restatement *is* explicit; every pair's
configuration stays literally visible at its own declaration, which is exactly the property that makes
DwarfMapper mappers readable without cross-referencing.

**Deliverable for R18-14** therefore becomes:

1. A code fix on a pair that declares a base-pair relationship: *"restate base configuration here"*, inserting
   the base's `[MapProperty]`/`[MapIgnore]`/`[MapValue]` attributes with a generated marker comment.
2. A diagnostic that fires when a restated block has **drifted** from the base pair it names — the marker
   comment is what makes this checkable.
3. The full primitive stays specced-but-deferred, so the option is not lost.

**What would flip this:** a second consumer hitting 15+ `IncludeBase` sites. One data point justifies removing
the drift risk; two would justify the primitive.

### Addendum (2026-08-12, on shipping R18-14): the marker comments are gone

Deliverables 1 and 2 above both assumed a **generated marker comment** would be what makes a restatement
checkable. Implementing it showed that assumption was unnecessary, and the mechanism that replaced it is
strictly stronger.

`[RestatesBase<TSource, TTarget>]` declares the relationship and the check compares the two pairs' **resolved
mappings** — the same device that made `DWARF081` work. That catches the case a comment convention cannot see
at all: a restatement which is *present, correctly bracketed, and no longer does the same thing*, because the
base gained a `Use=` converter that this pair did not. A marker comment is unverifiable prose; two `MemberMap`
records either match or they do not.

So the shipped shape is:

1. `[RestatesBase<S, T>]` — declares the relationship, emits nothing, and is asserted to change no generated
   code at all. The base pair is **inferred** (nearest declared pair up the class chain, ties refused with
   `DWARF084`) rather than named, because a fourth type argument could only ever disagree with the hierarchy.
2. `DWARF085` — the drift, on resolved mappings, with `Overrides = ["Member"]` for a deliberate divergence:
   an override is a decision, so it is stated where it is made and leaves every other member guarded.
3. A code fix offering the two honest answers — *restate the base configuration here* (which adds a missing
   attribute **and replaces a drifted one**) and *mark 'X' as a deliberate override*. It writes the attributes
   a person would have typed, with no marker brackets to preserve.

The full primitive stays specced-but-deferred, unchanged.

---

## D5 — enum→string `[Description]` precedence

**Decision: keep the default, add a diagnostic, and add an opt-out option.** Do not narrow the default.

The default is genuinely good: `[EnumMember]`, else `[Description]`, else the identifier lets `InProgress`
serialize as `"in_progress"` with no custom converter, which is why it exists.

The hazard is equally genuine and is specific to *migration*: `[Description]` is overwhelmingly a **display**
annotation. Consumers put it on enums for combo-box labels with no expectation that it becomes their
persistence format. AutoMapper used `.ToString()`, so `DonationSource.Kofi` with `[Description("Ko-Fi")]` would
have started writing `"Ko-Fi"` into a store holding `"Kofi"` — breaking reads of every existing document.

Three-part answer:

1. **`EnumStringSource = Attribute | Identifier`** (default `Attribute`), on the policy layer, so a migrating
   consumer gets a one-line parity switch instead of a per-enum converter.
2. **A diagnostic** when a declared enum↔string pair involves an enum member whose `[Description]`/
   `[EnumMember]` value differs from its identifier: *"member X will map to \"Ko-Fi\", not \"Kofi\"."* Info by
   default, escalatable. This is the make-the-silent-choice-explicit contract the project already embodies, and
   it is the part that would actually have caught this.
3. **A row in the migration checklist** (R18-19, already written).

**Rejected: narrowing the default** to honour `[EnumMember]` but not `[Description]`. It is a breaking
behaviour change for existing consumers, and it trades one silent surprise for another — someone relying on
`[Description]` today would silently start writing identifiers.

**What would flip this:** if the diagnostic proves too noisy in practice (an enum with many `[Description]`
members would fire once per member). Mitigation if so: report once per enum type, not per member.

---

## Consequences for the task graph

- **R18-01 now depends on R18-02**, not just on this decision — the source-interface axis is what makes the
  registration table cheap enough to be on by default.
- **R18-15** gains `[MapCollectionKey]` to its scope.
- **R18-14** changes shape: code fix + drift diagnostic, with the full primitive deferred rather than designed.
- **R18-D5** produces two new pieces of work folded into R18-19 and a new option + diagnostic.
