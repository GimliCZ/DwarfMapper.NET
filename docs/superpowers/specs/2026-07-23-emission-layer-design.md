# Design: emission layer — an indenting writer for synthesized helpers

- **Date:** 2026-07-23
- **Status:** **IMPLEMENTED and MERGED** (status corrected 2026-07-24) — `48b392b` on `master`. Core/CodeWriter landed; CollectionConverter, EnumConverter, DictionaryConverter and Registry/MapToGenerator all migrated to zero hand-rolled `
`; ratchet added and sabotage-proven. All 973 golden fingerprints unmoved. AggregateEmitter recorded as an explicit non-goal (§3).
- **Component:** `DwarfMapper.Generator` (converters that emit helper bodies) + tests

## 1. Problem

**Sub-project 3 of four** in the generator maintainability programme:

1. ~~generator testing framework~~ — landed (`4e79370`)
2. ~~shared engine core~~ — landed (`3b62b1a`)
3. **emission layer** ← this spec
4. split `MapperExtractor.cs`

The Roslyn cookbook is explicit: *"We do not recommend generating `SyntaxNode`s when generating syntax for
`AddSource`"* — it recommends an **indented text writer** wrapping a `StringBuilder`. DwarfMapper uses neither.
It hand-builds strings, and the measurements are:

| Measure | Count |
|---|---|
| `AppendLine` calls | 371 |
| Literal `\n` inside emitted strings | 247 |
| Hard-coded indent literals (`"    …`) | 439 |
| `IndentedTextWriter` uses | **0** |

**Indentation is hand-managed as string literals in 439 places.** Every nesting level is a manually-counted
run of spaces inside a quoted string, and every line break is a `\n` escape inside a C# string that is itself
inside a generated C# file.

**This is not a theoretical fragility — it bit twice in one session.** Both times the emitted code called an
*extension method* in instance form, which cannot compile because generated files carry no `using` directives:
first `src.ConfigureAwait(false)` (async-stream fix), then `v.AsSpan()` (the `[Flags]` allocation fix). Both
needed the static form. The escaping layers also caused repeated authoring failures: editing a C# string that
contains `\n` escapes, from a shell, mangles the payload silently.

**Where the fragility actually lives.** The `\n` density is concentrated in the converters that build
synthesized helper bodies, not in `MapEmitter` (which uses `AppendLine` and splices helpers verbatim at
`MapEmitter.cs:44`):

| File | Literal `\n` |
|---|---|
| `CollectionConverter.cs` | **121** |
| `EnumConverter.cs` | 50 |
| `DictionaryConverter.cs` | 24 |
| `Registry/MapToGenerator.cs` | 13 |
| `ParsableConverter.cs` | 6 |
| `NumericConverter.cs` | 1 |

## 2. The enabling measurement

A writer is only worth introducing if it can reproduce today's output **byte-identically** — otherwise the
971-case golden manifest churns and the refactor stops being verifiable.

Measured across all 81 committed snapshots: **every non-empty line is indented at a clean multiple of four
spaces.** There are no odd indents, no tabs, no drift. So current output is already exactly what a 4-space
indenting writer produces, and the migration can be genuinely behaviour-neutral.

This is the fact that makes this sub-project tractable, and it was measured before designing rather than
assumed.

## 3. Goals / Non-goals

**Goals**

- One `CodeWriter` that owns indentation and line endings, so neither is ever hand-counted again.
- Migrate the emitters where `\n` density is highest — the converters that build helper bodies.
- Keep output **byte-identical**: the golden manifest must not move.
- Leave a ratchet so raw `\n`-in-string emission cannot silently return to the migrated files.

**Non-goals**

- **Migrating `MapEmitter.cs`.** It is the 1,196-line file sub-project 4 will restructure, and it uses
  `AppendLine` (which already handles line breaks) rather than dense `\n` literals. Migrating it now means
  migrating it again after the split — waste, and it enlarges the riskiest diff in the programme. Deferred
  deliberately; recorded so it is not read as an oversight.
- **Migrating `Pipeline/AggregateEmitter.cs`.** Absent from the file table above, which counted 215 of the 247
  escapes across six files; this one holds 14 more. It is out of scope on the merits, not by oversight: 3 of
  its escapes are in the `Header` const (`AggregateEmitter.cs:26`) and the other 11 are `.Append('\n')` **char**
  literals closing `Append` chains, not multi-line string payloads. Its indentation never exceeds two levels and
  every emitted name is `global::`-qualified, so the extension-method-in-instance-form trap that motivated this
  sub-project structurally cannot occur there. Same species as the deferred `MapEmitter`: a writer would buy
  change-risk and no safety.
- Generating `SyntaxNode`s. The cookbook advises against it and `NormalizeWhitespace()` is prohibitively
  expensive.
- Any change to emitted output. This sub-project is behaviour-neutral by construction.
- Reformatting generated code "nicely". Output must stay byte-identical, quirks included.

## 4. Architecture

```
Core/
  StableHash.cs     (exists)
  MemberFacts.cs    (exists)
  CodeWriter.cs     ← new: indentation + line endings, nothing else
        ▲
        │  used by the helper-body builders
  CollectionConverter · EnumConverter · DictionaryConverter · MapToGenerator.Emit
```

`CodeWriter` depends only on `System.Text`. It knows nothing about mapping, symbols, or Roslyn — it is a
formatting primitive, and keeping it that way is what stops it accreting engine logic.

### 4.1 `CodeWriter`

```csharp
internal sealed class CodeWriter
{
    public CodeWriter(int initialIndent = 0);
    public CodeWriter Line(string text);      // indent + text + "\n"
    public CodeWriter Line();                 // bare "\n", no indent (blank lines carry no trailing spaces)
    public CodeWriter Raw(string text);       // verbatim, no indent, no newline — for spliced pre-built blocks
    public IDisposable Indent();              // one level in; out on Dispose
    public IDisposable Block(string header);  // header line, "{", indent … dedent, "}"
    public override string ToString();
}
```

Three decisions, each forced by how this codebase actually emits:

1. **LF only, never `Environment.NewLine`.** Output is LF-normalised repo-wide (audit ISSUE-024); using the
   platform newline would reintroduce the Windows/Linux fingerprint divergence that fix removed.
2. **A blank line emits `"\n"`, not indent-then-newline.** Current output has no trailing whitespace; emitting
   indentation on empty lines would change bytes on nearly every generated file.
3. **`Raw` exists because helper bodies are spliced pre-formatted.** `MapEmitter.cs:44` appends
   `synth.Code` verbatim, and those strings already carry their own indentation. `Raw` keeps that path working
   during migration instead of forcing a big-bang rewrite.

### 4.2 Migration order

Highest `\n` density first, one file per task, each verified against the manifest:

1. `CollectionConverter` (121) — the worst, and self-contained
2. `EnumConverter` (50) — includes the `[Flags]` span parser where the extension-method trap bit
3. `DictionaryConverter` (24)
4. `Registry/MapToGenerator.Emit` (13)

`ParsableConverter` (6) and `NumericConverter` (1) are left alone: below the threshold where a writer earns its
change-risk, and both emit single-line bodies.

## 5. Testing strategy

**The golden manifest is the whole verification story.** Each migration task must leave all 973 fingerprints
byte-identical. A moved fingerprint means the migration changed output — investigate, never regenerate.
Migrating a file whose output does not move is proof the writer reproduced it exactly.

**`CodeWriter` gets its own unit tests**, because the manifest only proves *the composition* is right, not that
the primitive is. Cover: indent in/out nesting, `Block` emitting `{`/`}` at the right levels, blank lines
carrying no trailing whitespace, `Raw` bypassing indentation, and LF-only endings.

**Ratchet.** After a file is migrated it must not regain hand-rolled emission: assert the migrated files contain
no literal `\n` inside emitted string arguments and no hard-coded indent runs. Scoped by directory glob, not
hard-coded paths — the lesson from sub-project 2, where a path-scoped ratchet would have silently lost coverage
when sub-project 4 splits a file.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A migration silently changes output | Golden manifest, checked per task; never regenerated in this sub-project |
| `CodeWriter` itself is subtly wrong | Its own unit tests, plus 973 fingerprints exercising it through real emission |
| Migration churn without benefit | Scope limited to the four highest-`\n` files; two low-density converters explicitly excluded |
| Re-migrating `MapEmitter` after the split | Explicitly deferred to after sub-project 4 |

## 7. Out of scope

- `MapperExtractor.cs` splitting (sub-project 4).
- Audit ISSUE-019 (immutable-buffer pre-sizing) — it was blocked on emission clarity, and becomes tractable
  after this, but is not part of this sub-project.
- Audit issues 016 and 023 — unrelated to emission.
