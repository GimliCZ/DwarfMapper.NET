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
