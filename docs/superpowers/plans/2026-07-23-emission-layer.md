# Emission Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace hand-counted indentation and `\n` escapes in the helper-body emitters with one `CodeWriter`, without changing a single byte of generated output.

**Architecture:** A new `Core/CodeWriter.cs` owns indentation and line endings. The four converters with the highest `\n` density migrate to it one at a time; the 973-case golden manifest verifies each migration is byte-identical. `MapEmitter` is deliberately out of scope (sub-project 4 restructures it).

**Tech Stack:** C# / .NET 10 (tests), `netstandard2.0` (generator), Roslyn, xUnit.

## Global Constraints

- Every new file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, `Nullable=enable`. A warning fails the build. Watch **CA1062** (`ArgumentNullException.ThrowIfNull`), **CA1305** (`CultureInfo.InvariantCulture`), **CA1861**, **IDE0060**, **IDE0051**.
- `src/DwarfMapper.Generator` is **netstandard2.0** — no net10-only APIs. Test projects are `net10.0`.
- **THE GOLDEN MANIFEST MUST NOT MOVE IN ANY TASK OF THIS PLAN.** All 973 fingerprints stay byte-identical. This whole sub-project is behaviour-neutral. If a fingerprint moves, the migration changed output — **investigate, never run `DWARF_GOLDEN_UPDATE=1`**. There is no sanctioned manifest change anywhere in this plan.
- Baseline: **5,570 tests green, 0 warnings**, on `master`.
- Test output is Czech: `Úspěšné!` = passed, `Neúspěšné` = failed. Report **solution-wide** totals (sum of all four test projects).
- Golden check (run after every task): `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusTests"`
- Full suite: `dotnet test DwarfMapper.NET.sln --nologo`

---

## File Structure

| File | Change |
|---|---|
| `src/DwarfMapper.Generator/Core/CodeWriter.cs` | **Create** — indentation + LF line endings |
| `tests/DwarfMapper.Generator.Tests/Core/CodeWriterTests.cs` | **Create** — unit tests for the primitive |
| `src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs` | Migrate (121 `\n`) |
| `src/DwarfMapper.Generator/Pipeline/EnumConverter.cs` | Migrate (50 `\n`) |
| `src/DwarfMapper.Generator/Pipeline/DictionaryConverter.cs` | Migrate (24 `\n`) |
| `src/DwarfMapper.Generator/Registry/MapToGenerator.cs` | Migrate `Emit` (13 `\n`) |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs` | Ratchet |

---

### Task 1: `CodeWriter`

**Files:**
- Create: `src/DwarfMapper.Generator/Core/CodeWriter.cs`
- Test: `tests/DwarfMapper.Generator.Tests/Core/CodeWriterTests.cs`

**Interfaces:**
- Produces: `internal sealed class CodeWriter` with `Line(string)`, `Line()`, `Raw(string)`, `Indent()`, `Block(string)`, `ToString()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DwarfMapper.Generator.Tests/Core/CodeWriterTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Core;

namespace DwarfMapper.Generator.Tests.Core;

/// <summary>
///     Unit tests for the emission primitive. The golden manifest proves the COMPOSITION is right; these prove
///     the primitive itself is, which the manifest cannot isolate.
/// </summary>
public class CodeWriterTests
{
    [Fact]
    public void Writes_lines_with_LF_and_no_indent_at_level_zero()
    {
        var w = new CodeWriter();
        w.Line("a").Line("b");

        Assert.Equal("a\nb\n", w.ToString());
    }

    [Fact]
    public void Indent_adds_four_spaces_per_level_and_restores_on_dispose()
    {
        var w = new CodeWriter();
        w.Line("outer");
        using (w.Indent())
        {
            w.Line("inner");
            using (w.Indent()) w.Line("deeper");
        }

        w.Line("back");

        Assert.Equal("outer\n    inner\n        deeper\nback\n", w.ToString());
    }

    [Fact]
    public void Blank_line_carries_no_trailing_whitespace()
    {
        // Current generated output has no trailing spaces. Emitting indentation on an empty line would
        // change bytes on nearly every generated file and move the golden manifest.
        var w = new CodeWriter();
        using (w.Indent())
        {
            w.Line("x");
            w.Line();
            w.Line("y");
        }

        Assert.Equal("    x\n\n    y\n", w.ToString());
    }

    [Fact]
    public void Block_emits_braces_at_the_header_level_and_indents_the_body()
    {
        var w = new CodeWriter();
        using (w.Block("if (x)")) w.Line("y();");

        Assert.Equal("if (x)\n{\n    y();\n}\n", w.ToString());
    }

    [Fact]
    public void Raw_bypasses_indentation_and_adds_no_newline()
    {
        // Helper bodies are spliced pre-formatted (MapEmitter.cs:44 appends synth.Code verbatim), so Raw must
        // not re-indent or terminate them.
        var w = new CodeWriter();
        using (w.Indent()) w.Raw("already   formatted\n");

        Assert.Equal("already   formatted\n", w.ToString());
    }

    [Fact]
    public void Initial_indent_offsets_every_line()
    {
        var w = new CodeWriter(2);
        w.Line("x");

        Assert.Equal("        x\n", w.ToString());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~CodeWriterTests"`
Expected: FAIL to compile — `CodeWriter` does not exist.

- [ ] **Step 3: Implement**

Create `src/DwarfMapper.Generator/Core/CodeWriter.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     Builds generated source with indentation and line endings handled once, instead of hand-counted in
///     string literals. The codebase had 439 hard-coded indent runs and 247 literal <c>\n</c> escapes inside
///     emitted strings; that layering is what makes emitters hard to edit correctly.
///     <para>
///     Line endings are always LF, never <c>Environment.NewLine</c>: generated output is LF-normalised
///     repo-wide (audit ISSUE-024) so fingerprints stay identical across Windows and Linux. Using the platform
///     newline here would reintroduce exactly the divergence that fix removed.
///     </para>
/// </summary>
internal sealed class CodeWriter
{
    private const string IndentUnit = "    ";

    private readonly StringBuilder _sb = new();
    private int _level;

    public CodeWriter(int initialIndent = 0)
    {
        _level = initialIndent;
    }

    /// <summary>Writes an indented line terminated by LF.</summary>
    public CodeWriter Line(string text)
    {
        for (var i = 0; i < _level; i++) _sb.Append(IndentUnit);
        _sb.Append(text).Append('\n');
        return this;
    }

    /// <summary>
    ///     Writes a blank line. Deliberately emits no indentation — current generated output carries no
    ///     trailing whitespace, and adding it would change bytes on nearly every generated file.
    /// </summary>
    public CodeWriter Line()
    {
        _sb.Append('\n');
        return this;
    }

    /// <summary>
    ///     Appends text verbatim: no indentation, no terminator. For splicing blocks that already carry their
    ///     own formatting, such as the synthesized helper bodies appended at <c>MapEmitter.cs:44</c>.
    /// </summary>
    public CodeWriter Raw(string text)
    {
        _sb.Append(text);
        return this;
    }

    /// <summary>Indents one level until the returned scope is disposed.</summary>
    public IDisposable Indent()
    {
        _level++;
        return new Scope(this, closeBrace: false);
    }

    /// <summary>Writes <paramref name="header" />, then <c>{</c>, indents, and closes with <c>}</c> on dispose.</summary>
    public IDisposable Block(string header)
    {
        Line(header);
        Line("{");
        _level++;
        return new Scope(this, closeBrace: true);
    }

    public override string ToString()
    {
        return _sb.ToString();
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool _closeBrace;
        private readonly CodeWriter _writer;
        private bool _disposed;

        public Scope(CodeWriter writer, bool closeBrace)
        {
            _writer = writer;
            _closeBrace = closeBrace;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _writer._level--;
            if (_closeBrace) _writer.Line("}");
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~CodeWriterTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Full suite + golden check**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green (5,576 = 5,570 + 6). Nothing uses `CodeWriter` yet, so the manifest cannot have moved.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/Core/CodeWriter.cs tests/DwarfMapper.Generator.Tests/Core/
git commit -m "feat: CodeWriter — one owner for indentation and line endings

The Roslyn cookbook recommends an indenting writer over both SyntaxNode generation and ad-hoc strings; this
codebase had neither, with 439 hand-counted indent runs and 247 literal \\n escapes inside emitted strings.

Three decisions are forced by how this codebase emits: LF-only endings (Environment.NewLine would undo the
ISSUE-024 fix that made fingerprints OS-stable), blank lines carry no trailing indent (current output has none),
and Raw() splices pre-formatted helper bodies without re-indenting them.

Nothing consumes it yet — migrations follow one file at a time, each verified byte-identical against the
973-case golden manifest."
```

---

### Task 2: Migrate `CollectionConverter` (121 `\n`)

The largest and most self-contained emitter. Its output is heavily exercised by the corpus, so the manifest is a strong verifier here.

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs`

**Interfaces:**
- Consumes: `CodeWriter` from Task 1.

- [ ] **Step 1: Record the baseline**

Before editing, capture what the emitters currently produce so you can prove equivalence independently of the manifest:

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~CollectionTaxonomyTests" 2>&1 | tail -3
```
Expected: PASS. Note the count.

- [ ] **Step 2: Migrate the emitter methods**

In `CollectionConverter.cs`, replace `StringBuilder`-with-literal-`\n` emission with `CodeWriter`. Add
`using DwarfMapper.Generator.Core;`.

Mechanical translation rules — apply them exactly, and change nothing else:
- `sb.Append("        var __r = …;\n");` → `w.Line("var __r = …;");` at the matching indent level.
- A line whose literal begins with N×4 spaces becomes a `Line` at indent level N (established via `Indent()`
  scopes), with those leading spaces REMOVED from the literal.
- `sb.Append("        {\n");` … `sb.Append("        }\n");` pairs become `using (w.Block(header))`.
- A literal `"\n"` on its own becomes `w.Line()`.
- Where a helper body string is returned to the caller for splicing, return `w.ToString()`.

Do NOT change any emitted text, spacing inside a line, or ordering. The only thing moving is where the
indentation and newline come from.

- [ ] **Step 3: Build**

Run: `dotnet build DwarfMapper.NET.sln --nologo`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: THE VERIFICATION — golden manifest must not move**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusTests"`
Expected: **PASS**.

If it FAILS, the migration changed output. The failure names the changed case ids; use them to find the
divergent emitter method. Common causes, in order of likelihood: a leading-space run left in a literal that is
now double-indented; a missing or extra trailing newline at the end of a helper body; a blank line that gained
indentation. **Do not regenerate the manifest** — it is telling you the truth.

- [ ] **Step 5: Full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green, solution-wide, unchanged count from Task 1.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/CollectionConverter.cs
git commit -m "refactor: CollectionConverter emits through CodeWriter

Largest concentration of hand-rolled emission — 121 literal \\n escapes and their hand-counted indent runs —
now expressed as Line/Block/Indent. Byte-identical: all 973 golden fingerprints unchanged, which is the proof
the translation was mechanical rather than a rewrite."
```

---

### Task 3: Migrate `EnumConverter` (50 `\n`) and `DictionaryConverter` (24 `\n`)

Two files, same mechanical translation as Task 2. `EnumConverter` contains the `[Flags]` span parser where the
extension-method-in-instance-form trap previously bit, so it benefits most from clearer emission.

**Files:**
- Modify: `src/DwarfMapper.Generator/Pipeline/EnumConverter.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/DictionaryConverter.cs`

**Interfaces:**
- Consumes: `CodeWriter` from Task 1.

- [ ] **Step 1: Migrate `EnumConverter`**

Apply the same translation rules as Task 2 Step 2. Add `using DwarfMapper.Generator.Core;`.

Take particular care with the `[Flags]` string-parse emitter: it emits `MemoryExtensions` calls in **static
form** (`global::System.MemoryExtensions.AsSpan(v)`), deliberately, because generated code carries no `using`
directives and the instance form would not compile. Preserve that exactly — do not "simplify" it to `v.AsSpan()`.

- [ ] **Step 2: Golden check after the first file**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusTests"`
Expected: PASS. Verify after EACH file, not once at the end — it localises any divergence to one file.

- [ ] **Step 3: Migrate `DictionaryConverter`**

Same rules. Note it emits both a mutable-dictionary path and an immutable builder path; both must stay
byte-identical.

- [ ] **Step 4: Golden check again**

Same command. Expected: PASS.

- [ ] **Step 5: Full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green, count unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/Pipeline/EnumConverter.cs src/DwarfMapper.Generator/Pipeline/DictionaryConverter.cs
git commit -m "refactor: EnumConverter and DictionaryConverter emit through CodeWriter

74 more literal \\n escapes replaced by Line/Block/Indent. The [Flags] span parser keeps its static-form
MemoryExtensions calls — generated code carries no using directives, so the instance form would not compile.
All 973 golden fingerprints unchanged."
```

---

### Task 4: Migrate `MapToGenerator.Emit` (13 `\n`) and ratchet the emitters

**Files:**
- Modify: `src/DwarfMapper.Generator/Registry/MapToGenerator.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs`

**Interfaces:**
- Consumes: `CodeWriter` from Task 1. The scan file already has an `AllGeneratorFiles()` helper and a `RepoRoot()` from sub-project 2 — reuse them rather than adding new ones.

- [ ] **Step 1: Migrate `Emit`**

Same translation rules. `Emit` builds the registry's extension class; its collection helper also emits a
pre-sized buffer (audit ISSUE-020) — preserve the capacity argument exactly.

- [ ] **Step 2: Golden check**

Run the `GoldenCorpusTests` filter. Expected: PASS.

- [ ] **Step 3: Add the ratchet**

In `GeneratorTestingScanTests.cs`, add:

```csharp
    /// <summary>
    ///     Keeps the migrated emitters on CodeWriter. Hand-counted indentation and \n escapes inside emitted
    ///     strings are what made these files hard to edit correctly — the same layering produced two
    ///     extension-method-in-instance-form bugs that could not compile. A migrated file must not regress.
    /// </summary>
    [Fact]
    public void Migrated_emitters_do_not_reintroduce_hand_rolled_emission()
    {
        var migrated = new[]
        {
            Path.Combine("Pipeline", "CollectionConverter.cs"),
            Path.Combine("Pipeline", "EnumConverter.cs"),
            Path.Combine("Pipeline", "DictionaryConverter.cs"),
            Path.Combine("Registry", "MapToGenerator.cs"),
        };

        var offenders = new List<string>();
        foreach (var relative in migrated)
        {
            var path = Path.Combine(RepoRoot(), "src", "DwarfMapper.Generator", relative);
            Assert.True(File.Exists(path), $"Migrated emitter not found: {relative}. If it moved, update this list.");

            var text = File.ReadAllText(path);

            // A literal \n inside a string argument is the hand-rolled form CodeWriter replaces.
            var newlineEscapes = System.Text.RegularExpressions.Regex.Matches(text, @"\\n""").Count;
            if (newlineEscapes > 0) offenders.Add($"{relative}: {newlineEscapes} literal \\n escape(s)");
        }

        Assert.True(offenders.Count == 0,
            "Migrated emitter(s) reintroduced hand-rolled emission:\n  " + string.Join("\n  ", offenders)
            + "\nUse CodeWriter (Line/Block/Indent) — hand-counted indentation and \\n escapes are what this "
            + "migration removed.");
    }
```

- [ ] **Step 4: Prove the ratchet fires**

Temporarily add a `sb.Append("x\n");`-style literal to `CollectionConverter.cs`, run the gate, confirm it goes
RED naming that file, then revert. Confirm `git status` shows `src/` clean before committing. Do NOT commit the
temporary edit.

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Migrated_emitters_do_not_reintroduce_hand_rolled_emission"`

- [ ] **Step 5: Full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green, +1 test.

- [ ] **Step 6: Commit**

```bash
git add src/DwarfMapper.Generator/Registry/MapToGenerator.cs tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs
git commit -m "refactor: registry Emit uses CodeWriter; ratchet the migrated emitters

Completes the migration of the four highest-density emitters (208 of 247 literal \\n escapes). The ratchet
fails if any migrated file regains a literal \\n inside an emitted string, and was verified to fire by
reintroducing one. MapEmitter is deliberately excluded — sub-project 4 restructures it, and migrating it now
would mean migrating it twice.

All 973 golden fingerprints unchanged across every task in this sub-project."
```

---

## Self-Review

**Spec coverage:** §4.1 `CodeWriter` → Task 1. §4.2 migration order (CollectionConverter → EnumConverter →
DictionaryConverter → MapToGenerator) → Tasks 2, 3, 4. §5 `CodeWriter` unit tests → Task 1. §5 golden-manifest
verification → every task. §5 ratchet → Task 4. §3 non-goal (MapEmitter excluded) → honoured; stated in Task 4's
commit message.

**Placeholder scan:** none — every step carries code or an exact command.

**Type consistency:** `CodeWriter`'s six members are defined in Task 1 and consumed in Tasks 2–4. `RepoRoot()`
and `AllGeneratorFiles()` in Task 4 already exist in `GeneratorTestingScanTests.cs` from sub-project 2.

**Known risk:** Tasks 2–4 are mechanical translation at scale, where a single leftover leading-space run
silently double-indents a line. The golden manifest is the detector and must be run **after each file**, not
once at the end — that is what localises a divergence to one emitter instead of three.
