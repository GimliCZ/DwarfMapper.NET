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

This started as two bundles and a plain parameter list for everything else, on the estimate that a pass would
need a handful of extras. **The estimate was wrong, and the measurement is why there are three.**

The free-variable tool that sized the candidates under-reports: it misses names that appear only inside a
nested call's argument list. Checking each of `ResolveMembers`' 26 parameters against the span text directly
gives the real figures — **pass 1 needs 19 parameters, pass 3 needs 20**, against a guideline of about a
dozen. Two independent passes overlapping on 14 read-only inputs is not overflow to absorb; it is a request
object asking to be named.

So the bundles mirror `ExtractCore`'s three, because the same three roles are present:

- **`MemberRequest`** — what resolution was asked to do. The 21 parameters some pass still reads: the two
  types, the directives, the surrounding facts.
- **`MemberLookups`** — what the prologue derives from the request and every pass then reads: `Comparer`,
  `Flexible`, `WritableByName`, `SourceGroups`, `FlattenInfos`, `ReservedConverters`, `ExtrasByTarget`.
- **`MemberAccumulators`** — what the passes fill: `Result`, `Diagnostics`, `Synthesized`, `HandledTargets`,
  `ConsumedExtraParams`, `ConsumedFlattenRoots`.

Every pass takes exactly those three.

**Membership was grepped, not judged.** Each parameter was checked for write-sites inside the body, and two
results contradict how the names read. `diagnostics` and `synthesized` arrive as parameters and look like
inputs — the passes write to both, so they are accumulators. `ignores` is written too, but only in the
prologue, folding in the obsolete members; by the time the request is built it is settled, so it is an input.
`reservedConverters` is the mirror case on the lookups side: mutable-looking, written only in the prologue.
Guessing would have misplaced at least three of these.

**The grep has a blind spot, and it was found by using it twice.** A write-site scan sees only assignments in
the text it scans — it cannot see mutation through a callee. Running it over `TryResolveConversion` reported
`synthesized` as read-only, which is true of that method's own text and false in effect: it is handed to
helpers that write into it. Re-checking `MemberRequest` against that discovery found one field where the same
thing was already true, `NestedRegistry`, which passes consult and register nested maps into.

It stays in the request, and the record says why rather than glossing it: the passes' relationship to it is
ask-and-register — a collaborator — not fill-for-the-caller-to-drain, which is what the accumulators are.
`synthesized` is kept out of the conversion request for the same reason read the other way: nothing consults
it, helpers only write into it. **For anything with mutating members, check the argument positions, not just
the assignments.**

Three parameters are deliberately **absent** from all three bundles — `flattenRoots`, `mapPropertyExtras` and
`mapperReservedConverters` are consumed by the prologue to build the lookups and no pass reads them again. A
bundle carrying fields nobody reads misrepresents what resolution depends on.

Two locals stay out because they are genuinely phase-local: `explicitSeen` (declared at 233, used once at 236)
and `unflattenRoots`, which is filled by the helper that reads it. A local declared one line above a span
looks like shared state to any tool working on spans; it is worth checking each rather than trusting the list.

All three records are private, positional, and carry **no defaults** — the ISSUE-043/044 totality rule, so
adding a field breaks every construction site instead of silently defaulting it.

**Deliberately not done here:** `ResolveMembers`' own 26-parameter signature. Bundling *those* would touch
every caller and inflate a diff that has to be proven byte-identical, and it is the same
model-the-working-set-first design work this stage defers for `ProcessDeclaredMethod`. If a phase needs more
than about a dozen parameters even with the two records, that is the signal to do it — as its own change, not
mid-extraction.

## What landed

| method | before | after | how it was cut |
|---|---:|---:|---|
| `ResolveMembers` | 905 | **263** | four independent passes |
| `TryResolveConversion` | 890 | **339** | six dispatch arms |
| `ResolveFlattenGraphDirectives` | 1,116 | **69** | loop body out, then seams re-derived inside it |

The third produced a 1,077-line method of its own, which was then cut the same way: the heterogeneous
path (439), the derived-type arms (205) and the edge/leaf partition (84) came out, leaving 573.

Nothing in the generator pipeline now exceeds 650 lines, against three methods above 890 when the stage
started. Every step was proven byte-identical against the 973-case golden manifest, with the corpus already
proven to reach every phase (`scripts/seam-reach.ps1`, 26/26).

## What the stage taught, beyond the cuts

**Bracing a span answers a narrower question than it looks like.** It reports what a span DECLARES that
something later needs. For a span that is one `if` or `foreach`, that is always nothing — everything inside
is block-scoped already — so such a span always reads "separable", truthfully and uselessly. The binding
question there is what the span NEEDS, and that comes from listing declarations at the method's own
indentation. Both questions matter; they are not the same question.

**Ask the compiler which jumps escape.** Wrapping a loop body in a local function and building makes every
`continue` or `break` that targeted the outer loop fail with CS0139 and leaves the inner ones silent. On the
flatten loop that named nine of twenty-six, and proved no `break` escaped — which is what made a void
extraction expressible at all. The alternative, converting by text, had already compiled cleanly on a
different loop and stopped the generator emitting.

**A write-site grep cannot see mutation through a callee.** It reported `synthesized` read-only in
`TryResolveConversion`, which is true of the text and false in effect. Checking argument positions as well
is what kept it out of the request — and what found `NestedRegistry` sitting in the same position on the
member side.

**Estimates about parameter counts were wrong by a factor of two.** The two-bundle design assumed a handful
of extras; measuring found 19 and 20. Three bundles followed from the measurement, not from taste.

## Still open

- `ResolveOneFlattenGraphDirective` (573) — two certified spans remain: the traversal-helper synthesis (128,
  three locals escape and would need hoisting) and the writable-member loop (135, shares three).
- `ProcessDeclaredMethod` (644) — its middle is the accumulating working set described below, unchanged.
- `ResolveProjectionMembers` (530) and `ResolveProjectionExpr` (405) were never in this stage's scope; they
  have not been derived and nothing is claimed about them.

## The region this stage does NOT cover

The middle of `ProcessDeclaredMethod` — `MapDerivedType` (187 lines, shares 1 local), the
collection/dictionary stage (216, shares 9) and `FlattenGraph` (134, shares 3). The compiler ruled those
non-separable, and the reason is structural rather than incidental: they are a **pipeline that accumulates a
shared working set**, each stage adding to the same `ignores` / `explicitMaps` / `ctor` / `members`. Their
seam comments mark stages, not boundaries.

Cutting them means modelling that working set as a type first — which is design, and belongs to its own
round rather than being forced here. Recorded so its absence is a decision.
