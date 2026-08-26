<!-- SPDX-License-Identifier: GPL-2.0-only -->

# The seam stage — deriving cut lines for the methods that have none

`ExtractCore` was decomposable because it carried seam comments: twenty-six of them, marking phases someone
had already identified in prose. The three remaining giants carry none. This is how their seams were derived,
and what the derivation found.

**Every figure here is measured, not estimated.** Sizes are raw line counts at `4f8d013`; separability is the
compiler's verdict, not a reading.

---

## The method

Two steps, the same two the seam-marked work used — with step one derived instead of read.

**1. Derive candidates from top-level structure.** A method without seams still has shape: each `if`,
`foreach`, `while` or `switch` at the body's own indentation is a self-contained unit with a natural
boundary. Listing those with their sizes gives the candidate cuts. Nothing about this is a judgement call;
it is the syntax tree the code already has.

**2. Ask the compiler whether each candidate is a phase.** Wrap the candidate in explicit braces in place and
build. A clean build means it declares nothing the rest of the method needs — it can be lifted. `CS0103`
means it shares locals, and the error names each one.

That second step is the round's most useful instrument and it arrived late. Three boundary bugs — the hoisted
collections, `DeclKey`, and the async span — were all found *after* extraction, by a build failure or a
regex. Bracing asks the same question first, and asks the authority rather than an approximation. It costs
one build per candidate.

---

## What the derivation found

### `ResolveMembers` — 905 lines, four candidates, **all separable**

| span | lines | opens with | verdict |
|---|---:|---|---|
| 234–519 | 286 | `foreach (var (srcName, tgtName, useMethod) in explicitMaps` | **SEPARABLE** |
| 521–578 | 58 | `foreach (var mv in mapValues ??` | **SEPARABLE** |
| 580–876 | 297 | `foreach (var target in targets)` | **SEPARABLE** |
| 936–984 | 49 | `if (options.SkipNullSourceMembers && result.Count > 0)` | **SEPARABLE** |

690 of 905 lines are covered by candidates, and every one is liftable — four independent passes over the
member set, the best-shaped of the three.

The third span is listed four lines wider than the `foreach` itself, and deliberately: lines 580–583 build
`targets` from `targetType` and `compilation` alone. Left outside, `targets` becomes a parameter; pulled in,
it becomes a local and the phase owns its own input. Re-probed at the wider span to confirm — still separable.
Worth checking for each candidate, since a construction line sitting just above a loop is common.

**Separable does not mean independent.** These four passes share a working set, and the measured extents say
so plainly: `handledTargets` spans passes 1–3, `writableByName` 1–2, `reservedConverters` 1 and 3, `result`
all four. What the compiler certified is narrower and is the only thing that matters for extraction — no
span *declares* something a later span needs, so every shared item can be passed in rather than having to be
hoisted out afterwards. That distinction is the whole reason these are cuttable and the `ProcessDeclaredMethod`
middle is not.

### `TryResolveConversion` — 890 lines, six candidates, **all six probed, all separable**

| span | lines | opens with | verdict |
|---|---:|---|---|
| 117–308 | 192 | `if (DictionaryConverter.TryResolve(` | **SEPARABLE** |
| 310–543 | 234 | `if (CollectionConverter.TryResolve(` | **SEPARABLE** |
| 576–621 | 46 | *(dispatch arm)* | **SEPARABLE** |
| 623–683 | 61 | `if (IsNullableValue(srcType, out var underlying))` | **SEPARABLE** |
| 685–734 | 50 | *(dispatch arm)* | **SEPARABLE** |
| 880–920 | 41 | `if (autoNest && nestedRegistry is not null && ...)` | **SEPARABLE** |

The shape is a dispatch chain: each candidate tries one conversion kind and returns when it succeeds. That
makes them natural `bool` phases, exactly like the three endpoint handlers already extracted from
`ProcessDeclaredMethod` — `return` means "I claimed this", falling through means "not mine".

### `ResolveFlattenGraphDirectives` — 1,116 lines, **one 1,073-line loop**

A single `foreach (var (srcNavName, tgtCollName) in rawDirectives)` covering 96 % of the method. Structurally
identical to `ExtractCore`'s per-method loop, and it takes the same treatment: extract the loop body, convert
`continue` to `return`, then re-derive seams inside the extracted method where they become top-level.

**Precondition, learned the hard way:** resolve every jump against its enclosing construct before converting.
On the last loop body, eighteen of twenty-nine `continue` statements targeted the loop and eleven targeted
inner loops; converting all of them by text compiled cleanly and made the generator stop emitting. Also
confirm no `break` targets the loop — one cannot be expressed as a void extraction at all.

---

## How the shared working set is passed

`ResolveMembers` builds thirteen locals in a prologue and the four passes read and write them. Passing all
thirteen individually would put phase 3 near twenty parameters, so they are bundled the way
`MethodExtractionContext` already bundles `ExtractCore`'s — **split by direction, which the write-site
measurements settle rather than taste**:

- **`MemberLookups`** — built once in the prologue, never written again (`reservedConverters`' only writes are
  at 215/221/229, all prologue): `Comparer`, `Flexible`, `WritableByName`, `SourceGroups`, `FlattenInfos`,
  `ReservedConverters`.
- **`MemberAccumulators`** — what the passes mutate: `Result`, `HandledTargets`, `ConsumedExtraParams`,
  `ConsumedFlattenRoots`.

Three of the thirteen are not shared at all and move *into* phase 1 rather than becoming parameters:
`explicitSeen` (declared at 233, used once at 236), `unflattenRoots` with its comment, and the `extrasByTarget`
build — the phase takes `mapPropertyExtras` and builds its own. A local declared one line above a span reads
as shared state to any tool that works on spans; it is worth checking each one rather than trusting the list.

Everything else stays a plain parameter, `in MapperOptions` included. Both records are private, positional,
and carry **no defaults** — the ISSUE-043/044 totality rule, so adding a field breaks every construction site
instead of silently defaulting it.

**Deliberately not done here:** `ResolveMembers`' own 26-parameter signature. Bundling *those* would touch
every caller and inflate a diff that has to be proven byte-identical, and it is the same
model-the-working-set-first design work this stage defers for `ProcessDeclaredMethod`. If a phase needs more
than about a dozen parameters even with the two records, that is the signal to do it — as its own change, not
mid-extraction.

## Landing order

1. **`ResolveMembers`** — four independent passes, all separable, no early-return contract needed. The
   cleanest and therefore the one that proves the pattern on a seamless method.
2. **`TryResolveConversion`** — the dispatch chain, as `bool` phases.
3. **`ResolveFlattenGraphDirectives`** — loop-body extraction, then re-derive inside.

Each phase is one commit, proven byte-identical against the 973-case golden manifest, with the corpus already
proven to reach every phase (`scripts/seam-reach.ps1`, 26/26).

---

## The region this stage does NOT cover

The middle of `ProcessDeclaredMethod` — `MapDerivedType` (187 lines, shares 1 local), the
collection/dictionary stage (216, shares 9) and `FlattenGraph` (134, shares 3). The compiler ruled those
non-separable, and the reason is structural rather than incidental: they are a **pipeline that accumulates a
shared working set**, each stage adding to the same `ignores` / `explicitMaps` / `ctor` / `members`. Their
seam comments mark stages, not boundaries.

Cutting them means modelling that working set as a type first — which is design, and belongs to its own
round rather than being forced here. Recorded so its absence is a decision.
