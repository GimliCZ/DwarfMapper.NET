<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 30 ledger

Round 30 ran without an SDD ledger in `.superpowers/sdd/`, so this one is written here directly. It records what
committed history cannot: corrections to claims already made in commit messages. **History is never rewritten** —
a wrong claim stays in its commit, and the correction lives here, next to the evidence for it.

## Corrections to committed history

### `85b7eb0` — "the compiler drops a named argument … before it reaches NamedArguments" is half wrong

**The claim** (`refactor(ambient): GetRootConfig reads AutoValidate through TryGetNamedArgument`, 2026-09-13):

> The compiler drops a named argument that binds to nothing, or has the wrong type, before it reaches
> NamedArguments.

The claim was used to argue that the old loop's "value is not a bool" outcome was unreachable.

**Measured** (2026-09-14, a throwaway probe compiling `[assembly: DwarfMapperValidationRoot(…)]` against the
generator test harness's reference set and reading `Assembly.GetAttributes()`):

| Named argument | Compiler | `NamedArguments` | `Value is bool` | `Equals(Value, true)` |
|---|---|---|---|---|
| `AutoValidate = true` | clean | `AutoValidate`, `Primitive`, `True` | true | true |
| `AutoValidate = 1` | **CS0029** | **`AutoValidate`, `Kind = Error`, `Value = null`** | false | false |
| `Nope = true` | CS0246 | empty | — | — |

- **Binds to nothing — true.** `Nope = true` does not reach `NamedArguments`.
- **Wrong type — false.** `AutoValidate = 1` is a compile error, but the argument is **not** dropped. It reaches
  `NamedArguments` as an error-kind constant with a null value. The old `is bool` false arm therefore did have an
  input, just one that only a build already failing with CS0029 produces.

**Consequence: none for behaviour.** `85b7eb0` replaced `value is bool b && b` with `Equals(value, true)`, and
both give `false` for the error-kind constant. So the refactor was behaviour-equivalent on every input, the
compile-error one included. Only the stated reason was wrong. Nothing to change in code; the correction is the
record.

## Owner rulings

### Runtime mutation floor, and `DwarfMapperRegistry.Key` (2026-09-14)

**Ruling:** `DwarfMapperRegistry.Key` becomes a `readonly record struct`. The runtime mutation leg's `break` stays
**97**. It is not lowered.

**Why it came up.** The runtime leg measured **95.45 %** (126/132, `StrykerOutput/2026-09-14.19-15-03`) against a
`break` of 97. A position-tolerant diff against the last pinned run (`2026-09-07.06-30-16`, 97.60 %, 122/125)
found three things:
- one real survivor from `77dc345` (`DwarfMapperRegistry.cs:222`, `candidates ??= …` → `=`), killed by
  `5fe0d76`;
- two `DwarfRefContext` depth-clamp boundary mutants gone Killed → Survived with no commit to the file. Both are
  provably equivalent. Their 2026-09-07 "kills" were `static`, `coveredBy = 0`, and credited only to a
  docs-generation test (`GeneratedDocsAreCurrentTests.The_api_reference_matches_the_public_surface`). They were
  accidental, and the pin had counted them;
- two permanently undetected mutants in the hand-written `Key` equality: `Equals(object)` NoCoverage, and
  `Equals(Key)`'s `&&` → `||`, ruled-in-practice.

With `Key` hand-written, the honest ceiling was 127/132 = 96.21 %, so 97 was unreachable.

**Options put to the owner:**
- **(A)** make `Key` a record struct. Compiler-generated equality has no source to mutate, so the two `Key`
  mutants leave the population: 127/130 = 97.69 %, floor held. Behind the registry's allocation and cost gates,
  because it is hot-path code.
- **(B)** re-pin the floor at 96.

**(A) was chosen.** Equality stays field-wise over the same two `Type` references, so lookup behaviour is unchanged;
the runtime leg's re-measure confirms the score.
