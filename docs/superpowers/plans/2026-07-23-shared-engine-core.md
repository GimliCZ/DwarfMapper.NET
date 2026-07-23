# Shared Engine Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give both mapping engines one shared implementation of member enumeration and stable hashing, fixing the registry's silent inherited-member data loss as a consequence.

**Architecture:** A new `src/DwarfMapper.Generator/Core/` namespace with two files — `StableHash` (FNV-1a, one home, both algorithms named) and `MemberFacts` (readable/writable enumeration with base-type walk, interface handling, name de-duplication and accessor-level usability). `MapperExtractor` keeps thin delegating wrappers so its 31 call sites do not churn; `MapToGenerator` deletes its own enumeration and calls `MemberFacts`, thereby gaining base-type walking.

**Tech Stack:** C# / .NET 10 (tests), `netstandard2.0` (generator), Roslyn (`Microsoft.CodeAnalysis.CSharp`), xUnit, Verify.

## Global Constraints

- Every new file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, `EnforceCodeStyleInBuild=true`, `Nullable=enable`. A warning fails the build. Traps seen repeatedly in this repo: **CA1062** (validate public/internal params — `ArgumentNullException.ThrowIfNull`), **CA1305** (`CultureInfo.InvariantCulture` on `GetMessage`/`ToString`), **CA1861** (hoist constant arrays to `static readonly`), **IDE0060** (unused parameter), **IDE0051** (unused private member).
- `src/DwarfMapper.Generator` targets **netstandard2.0** — no net10-only APIs there. Test projects are `net10.0`.
- Test method names: `Descriptive_snake_case_sentences`. Test namespace matches folder.
- Baseline before starting: **5,561 tests green, 0 warnings**, on `master`.
- **The golden manifest must not move.** All 971 fingerprints are expected to stay byte-identical through every task in this plan. If any fingerprint changes, **stop and investigate** — do NOT run `DWARF_GOLDEN_UPDATE=1` to make it pass. The only sanctioned manifest change is the *addition* of the new inheritance cases in Task 5.
- Test output is Czech: `Úspěšné!` = passed, `Neúspěšné` = failed. Read the numeric counts. When reporting a suite total, quote the **solution-wide** sum across all four test projects, not just the generator project.
- Full-suite command: `dotnet test DwarfMapper.NET.sln --nologo`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/DwarfMapper.Generator/Core/StableHash.cs` | FNV-1a; `Fnv1a` (per-char) and `Fnv1aPerByte`, both named and documented |
| `src/DwarfMapper.Generator/Core/MemberFacts.cs` | Readable/writable member enumeration for both engines |
| `tests/DwarfMapper.Generator.Tests/RegistryInheritanceTests.cs` | Characterises, then pins, the inherited-member behaviour |

**Modified**

| File | Change |
|---|---|
| `src/DwarfMapper.Generator/Registry/MapToGenerator.cs` | Delete own `ReadableMembers`/`WritableMembers`/`Hash`; call `MemberFacts`/`StableHash` |
| `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` | Enumeration bodies move to `MemberFacts`; keep delegating wrappers; `Hash` → `StableHash` |
| `src/DwarfMapper.Generator/Pipeline/*.cs` (7 converters/emitters) | Local FNV copies → `StableHash.Fnv1a` |
| `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs` | Add two inheritance feature cases (Task 5) |
| `tests/DwarfMapper.Generator.Tests/Golden/GoldenFeatureCoverageTests.cs` | Markers for the two new cases (Task 5) |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs` | Drift ratchet (Task 5) |

---

### Task 1: Characterise the inherited-member divergence

Proves the bug before anything is refactored, so the history separates *"bug proven"* from *"code moved"*. No `src/` changes in this task. These tests are expected to **FAIL** at the end of this task and to stay failing until Task 4 — that is the point.

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/RegistryInheritanceTests.cs`

**Interfaces:**
- Consumes: `GeneratorTestHarness.RunMapToWithSource(string source)` → `(ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource)`; `GeneratorTestHarness.RunMapTo(string source)` → `ImmutableArray<Diagnostic>`.
- Produces: nothing later tasks consume.

- [ ] **Step 1: Write the characterisation tests**

Create `tests/DwarfMapper.Generator.Tests/RegistryInheritanceTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The [MapTo] registry enumerates only <c>type.GetMembers()</c> — MapToGenerator.cs contains zero
///     <c>BaseType</c> references — while the [DwarfMapper] class model walks the base-type chain and
///     interfaces with name de-duplication. So inherited members are invisible to the registry:
///     an inherited DESTINATION member is silently never mapped (silent data loss, which this library's core
///     tenet forbids), and an inherited SOURCE member yields a DWARFR02 "unmapped" for a member that exists.
///     No test covered [MapTo] with inheritance before these.
/// </summary>
public class RegistryInheritanceTests
{
    // Dto.Id is inherited. The registry never enumerates it, so it is never assigned — silently.
    private const string InheritedDestinationMember = """
                                                      using DwarfMapper;
                                                      namespace Demo;
                                                      public class DtoBase { public int Id { get; set; } }
                                                      public class Dto : DtoBase { public string Name { get; set; } = ""; }
                                                      [MapTo(typeof(Dto))]
                                                      public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                      """;

    // Src.Id is inherited. The registry cannot see it, so Dto.Id looks unmapped and DWARFR02 fires wrongly.
    private const string InheritedSourceMember = """
                                                 using DwarfMapper;
                                                 namespace Demo;
                                                 public class SrcBase { public int Id { get; set; } }
                                                 [MapTo(typeof(Dto))]
                                                 public class Src : SrcBase { public string Name { get; set; } = ""; }
                                                 public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                 """;

    [Fact]
    public void An_inherited_destination_member_is_mapped()
    {
        var (diagnostics, generated) = GeneratorTestHarness.RunMapToWithSource(InheritedDestinationMember);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(generated.Contains("Id = ", StringComparison.Ordinal),
            "The inherited destination member 'Id' was never assigned, so it silently stays at its default. "
            + "The registry enumerates only type.GetMembers() and does not walk the base-type chain.\n\n"
            + "--- generated ---\n" + generated);
    }

    [Fact]
    public void An_inherited_source_member_does_not_produce_a_spurious_DWARFR02()
    {
        var diagnostics = GeneratorTestHarness.RunMapTo(InheritedSourceMember);

        Assert.DoesNotContain(diagnostics, d => d.Id == "DWARFR02");
    }
}
```

- [ ] **Step 2: Run them and confirm BOTH fail**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~RegistryInheritanceTests"`

Expected: **2 failed**. `An_inherited_destination_member_is_mapped` fails because `Id = ` is absent from the emitted source; `An_inherited_source_member_does_not_produce_a_spurious_DWARFR02` fails because DWARFR02 is reported.

If either PASSES, stop and report: the divergence is not what this plan assumes and the remaining tasks need re-basing.

- [ ] **Step 3: Confirm the golden manifest is unaffected**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenCorpusTests"`
Expected: PASS. No corpus case uses inheritance with `[MapTo]`, so adding these tests moves no fingerprint.

- [ ] **Step 4: Commit (with the tests red)**

Committing a known-failing characterisation test is deliberate here: the next commits are a pure code move, and this is what proves the move fixed something rather than changed nothing. Use `--no-verify` only if a hook blocks red tests; otherwise commit normally.

```bash
git add tests/DwarfMapper.Generator.Tests/RegistryInheritanceTests.cs
git commit -m "test: characterise the registry's inherited-member blindness (RED)

MapToGenerator enumerates only type.GetMembers() and contains zero BaseType references, while the class model
walks the base chain and interfaces. So an inherited DESTINATION member is silently never mapped, and an
inherited SOURCE member produces a DWARFR02 for a member that genuinely exists. No test covered [MapTo] with
inheritance until now.

These two tests FAIL deliberately and stay red until the shared MemberFacts core lands. Manifest-neutral: no
corpus case uses inheritance with [MapTo]."
```

---

### Task 2: `StableHash` — one home for FNV-1a

**Files:**
- Create: `src/DwarfMapper.Generator/Core/StableHash.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs`, `CollectionConverter.cs`, `DictionaryConverter.cs`, `EnumConverter.cs`, `MapperExtractor.cs`, `NumericConverter.cs`, `ParsableConverter.cs`, `UserConversionConverter.cs`, `NestedMappingRegistry.cs`; `src/DwarfMapper.Generator/Registry/MapToGenerator.cs`

**Interfaces:**
- Produces: `internal static class StableHash` with `public static string Fnv1a(string s)` and `public static string Fnv1aPerByte(string s)`, both returning lowercase 8-char hex.

- [ ] **Step 1: Create the shared hash**

Create `src/DwarfMapper.Generator/Core/StableHash.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     Deterministic FNV-1a hashing, in one place. These hashes feed GENERATED HELPER NAMES
///     (<c>__DwarfMapObj_&lt;hash&gt;</c>, <c>__DwarfMap_Coll_&lt;hash&gt;</c>, …), so they must be stable across
///     processes and machines — <c>string.GetHashCode()</c> is randomised per process and must never be used here.
/// </summary>
internal static class StableHash
{
    private const uint Offset = 2166136261u;
    private const uint Prime = 16777619u;

    /// <summary>
    ///     FNV-1a over UTF-16 code units (one round per char). This is the form nine call sites already used
    ///     byte-for-byte, so routing them here changes no generated name.
    /// </summary>
    public static string Fnv1a(string s)
    {
        unchecked
        {
            var h = Offset;
            foreach (var c in s)
            {
                h ^= c;
                h *= Prime;
            }

            return h.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    ///     FNV-1a over BYTES (two rounds per char: low byte, then high byte). Used only by
    ///     <c>NestedMappingRegistry</c>.
    ///     <para>
    ///     This deliberately differs from <see cref="Fnv1a" /> and is kept rather than unified. Both feed
    ///     generated helper names, so converging them would RENAME helpers across the whole golden corpus — a
    ///     large, noisy manifest diff for zero behavioural benefit. Keeping both here, named and documented,
    ///     turns what was an accidental divergence across ten files into a deliberate one in a single file.
    ///     </para>
    /// </summary>
    public static string Fnv1aPerByte(string s)
    {
        unchecked
        {
            var hash = Offset;
            foreach (var c in s)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= Prime;
                hash ^= (byte)(c >> 8);
                hash *= Prime;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
```

- [ ] **Step 2: Build to confirm it compiles standalone**

Run: `dotnet build src/DwarfMapper.Generator/DwarfMapper.Generator.csproj --nologo`
Expected: 0 errors, 0 warnings. (`IDE0051` does not fire — the members are `public` on an `internal` type.)

- [ ] **Step 3: Route the nine identical copies through it**

In each of these files, delete the local private FNV method and replace its call sites with `StableHash.Fnv1a(...)`, adding `using DwarfMapper.Generator.Core;`:

`Pipeline/AggregateEmitter.cs`, `Pipeline/CollectionConverter.cs`, `Pipeline/DictionaryConverter.cs`, `Pipeline/EnumConverter.cs`, `Pipeline/MapperExtractor.cs`, `Pipeline/NumericConverter.cs`, `Pipeline/ParsableConverter.cs`, `Pipeline/UserConversionConverter.cs`, `Registry/MapToGenerator.cs`.

In `Pipeline/NestedMappingRegistry.cs`, delete its local method and call `StableHash.Fnv1aPerByte(...)` instead — **not** `Fnv1a`. Using the wrong one here renames generated helpers and will show up as a large manifest diff.

Find every local copy with:

```bash
grep -rn "2166136261" src/DwarfMapper.Generator --include=*.cs
```

After the edits that command must return **only** `Core/StableHash.cs`.

- [ ] **Step 4: Build, then verify the manifest did not move**

Run: `dotnet build DwarfMapper.NET.sln --nologo`
Expected: 0 errors, 0 warnings.

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green **except** the two Task 1 tests, which stay red. Solution-wide count unchanged from Task 1.

**The golden corpus test must PASS.** If it reports changed fingerprints, a call site was routed to the wrong algorithm (most likely `NestedMappingRegistry` sent to `Fnv1a`). Fix the routing; do not regenerate the manifest.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.Generator/
git commit -m "refactor: one home for FNV-1a hashing (ISSUE-015)

FNV-1a existed in ten files. Nine were byte-identical and now call StableHash.Fnv1a — behaviour-neutral, so no
generated helper name and no golden fingerprint moves.

NestedMappingRegistry's copy is algorithmically DIFFERENT (per-byte, two rounds per char, versus per-char). It is
deliberately kept as StableHash.Fnv1aPerByte rather than unified: both feed generated helper names, so converging
them would rename helpers across the corpus — a large manifest diff for zero behavioural gain. The divergence is
now deliberate, named and documented in one file instead of accidental across ten."
```

---

### Task 3: `MemberFacts` — extract the enumeration, class model delegates

Behaviour-neutral by construction: the implementation moves verbatim and `MapperExtractor` keeps wrappers with its existing signatures, so none of its 31 call sites change.

**Files:**
- Create: `src/DwarfMapper.Generator/Core/MemberFacts.cs`
- Modify: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs` (lines ~4096–4230 region)

**Interfaces:**
- Produces:
  - `internal static IEnumerable<(ISymbol Symbol, string Name, ITypeSymbol Type)> MemberFacts.Readable(ITypeSymbol type, Compilation? compilation = null, bool allowNonPublic = false)`
  - `internal static IEnumerable<(ISymbol Symbol, string Name, ITypeSymbol Type)> MemberFacts.Writable(ITypeSymbol type, Compilation? compilation = null, bool allowNonPublic = false)`
- Consumes: nothing from earlier tasks.

- [ ] **Step 1: Move the implementation**

Create `src/DwarfMapper.Generator/Core/MemberFacts.cs` containing, **moved verbatim** from `MapperExtractor.cs`:

- `ReadableMembers` (currently at `MapperExtractor.cs:4125`) — rename to `Readable`
- `WritableMembers` (currently at `:4177`) — rename to `Writable`
- `AccessorUsable` (`:4096`), `FieldUsable` (`:4102`), and `IsMemberReachable` (`:4109`) — the accessibility helpers those two depend on

Two mechanical changes while moving, and nothing else:

1. Element type becomes `(ISymbol Symbol, string Name, ITypeSymbol Type)`. Every `yield return (p.Name, p.Type);` becomes `yield return (p, p.Name, p.Type);` and every `yield return (f.Name, f.Type);` becomes `yield return (f, f.Name, f.Type);`. The registry needs the `ISymbol` to read `[MapProperty]`/`[MapIgnore]` off the member; the class model does not and will project it away.
2. Members become `internal static` (they were `private static`).

Do **not** change the walking logic, the `seen` de-duplication, the interface handling, or the accessor rules. This must be a move, not a rewrite — the golden manifest is the check that it was.

File header and class:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     The single implementation of "which members can be read from / written to a type", shared by both
///     engines. It walks the base-type chain (and, for interfaces, all transitively inherited interfaces),
///     de-duplicates by name so a shadowing override yields once, and applies ACCESSOR-level usability rather
///     than merely property-level.
///     <para>
///     This lived privately inside <c>MapperExtractor</c> while <c>MapToGenerator</c> had its own shallower
///     copy that never walked base types — so inherited members were invisible to the registry, silently
///     dropping data. One implementation is what stops that class of divergence recurring.
///     </para>
/// </summary>
internal static class MemberFacts
{
    // … moved bodies …
}
```

- [ ] **Step 2: Replace the class model's methods with delegating wrappers**

In `MapperExtractor.cs`, delete the five moved methods and put in their place:

```csharp
    // Enumeration lives in Core.MemberFacts so both engines share one implementation. These wrappers keep the
    // class model's existing (Name, Type) shape so its 31 call sites are untouched by the move.
    private static IEnumerable<(string Name, ITypeSymbol Type)> ReadableMembers(ITypeSymbol type,
        Compilation? compilation = null, bool allowNonPublic = false)
    {
        foreach (var m in MemberFacts.Readable(type, compilation, allowNonPublic))
            yield return (m.Name, m.Type);
    }

    private static IEnumerable<(string Name, ITypeSymbol Type)> WritableMembers(ITypeSymbol type,
        Compilation? compilation = null, bool allowNonPublic = false)
    {
        foreach (var m in MemberFacts.Writable(type, compilation, allowNonPublic))
            yield return (m.Name, m.Type);
    }
```

Add `using DwarfMapper.Generator.Core;` to `MapperExtractor.cs`.

If `AccessorUsable`/`FieldUsable`/`IsMemberReachable` are still referenced elsewhere in `MapperExtractor.cs`, check with:

```bash
grep -n "AccessorUsable\|FieldUsable\|IsMemberReachable" src/DwarfMapper.Generator/Pipeline/MapperExtractor.cs
```

If any remain, expose them from `MemberFacts` and call through; do not keep a second copy.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build DwarfMapper.NET.sln --nologo`
Expected: 0 errors, 0 warnings.

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green **except** the two Task 1 tests, still red (the registry has not adopted `MemberFacts` yet).

**The golden manifest must not move.** This task is a pure move affecting only the class model, which already had these semantics. A fingerprint change means the move was not verbatim — diff `MemberFacts.cs` against the original bodies rather than regenerating.

- [ ] **Step 4: Commit**

```bash
git add src/DwarfMapper.Generator/
git commit -m "refactor: extract member enumeration to Core.MemberFacts (class model delegates)

Moves ReadableMembers/WritableMembers and their accessibility helpers out of MapperExtractor into a shared
Core.MemberFacts, with the element type widened to (ISymbol, Name, Type) so the registry can read
[MapProperty]/[MapIgnore] off the member symbol.

Behaviour-neutral by construction: the bodies moved verbatim and MapperExtractor keeps wrappers with its
existing (Name, Type) signatures, so its 31 call sites are untouched and the golden manifest does not move. The
registry adopts this next, which is where behaviour changes."
```

---

### Task 4: Registry adopts `MemberFacts` — the fix

**Files:**
- Modify: `src/DwarfMapper.Generator/Registry/MapToGenerator.cs` (delete `ReadableMembers` ~line 242 and `WritableMembers` ~line 255)

**Interfaces:**
- Consumes: `MemberFacts.Readable` / `MemberFacts.Writable` from Task 3.

- [ ] **Step 1: Delete the registry's own enumeration and call the shared one**

In `MapToGenerator.cs`, delete both private methods and add `using DwarfMapper.Generator.Core;`.

The registry's call sites destructure as `(Sym, Type)` for readables and `(Symbol, Name, Type)` for writables. `MemberFacts` returns `(Symbol, Name, Type)` for both, so update the readable call sites to use `.Symbol`/`.Type` (or destructure the three-tuple). Compile errors will point at each one — there are three per method.

Pass the defaults for the two extra parameters: `MemberFacts.Readable(type)` and `MemberFacts.Writable(type)`. The registry has no non-public opt-in, so `compilation: null, allowNonPublic: false` is correct and preserves its current accessibility behaviour.

After the edit this must return nothing:

```bash
grep -n "GetMembers()" src/DwarfMapper.Generator/Registry/MapToGenerator.cs
```

Both of its `GetMembers()` calls were inside the deleted methods.

- [ ] **Step 2: Run the Task 1 characterisation tests — they must now PASS**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~RegistryInheritanceTests"`
Expected: **2 passed**. The inherited destination member is now assigned, and the spurious DWARFR02 is gone.

This is the moment the bug is fixed. If they still fail, the registry is not actually routed through `MemberFacts` — check for a leftover local enumeration.

- [ ] **Step 3: Run the full suite and scrutinise the manifest**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green, solution-wide.

The golden manifest is expected to be **unchanged**: no existing corpus case uses `[MapTo]` with a base class, so the new base-walk changes nothing already pinned. If fingerprints DO move, investigate before doing anything else — it means a registry corpus case has inheritance you did not expect, or the adoption changed more than intended. Report which case ids moved and why. Do not regenerate.

- [ ] **Step 4: Commit**

```bash
git add src/DwarfMapper.Generator/
git commit -m "fix: [MapTo] registry sees inherited members (silent data loss)

The registry enumerated only type.GetMembers() and never walked the base-type chain, so an inherited DESTINATION
member was silently never mapped and an inherited SOURCE member produced a DWARFR02 for a member that genuinely
existed. It now uses the shared Core.MemberFacts, gaining the class model's base-chain walk, interface handling,
name de-duplication and accessor-level usability.

The two characterisation tests committed red in the first commit of this series now pass. The golden manifest is
unchanged — no pre-existing corpus case used [MapTo] with inheritance, which is exactly why the bug survived."
```

---

### Task 5: Pin the fixed behaviour and ratchet the drift shut

**Files:**
- Modify: `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs` (`FeatureCases()`)
- Modify: `tests/DwarfMapper.Generator.Tests/Golden/GoldenFeatureCoverageTests.cs` (`FeatureMarkers`)
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratorTestingScanTests.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` (regenerated — additive only)

**Interfaces:**
- Consumes: `GoldenCorpus.FeatureCases()` (yields `(string Id, string Source, string GeneratorName)`), `GoldenManifest.UpdateEnvVar`.

- [ ] **Step 1: Add two golden feature cases**

In `GoldenCorpus.FeatureCases()`, append:

```csharp
        yield return ("RegistryInheritedDestination", """
                                                      using DwarfMapper;
                                                      namespace Demo;
                                                      public class DtoBase { public int Id { get; set; } }
                                                      public class Dto : DtoBase { public string Name { get; set; } = ""; }
                                                      [MapTo(typeof(Dto))]
                                                      public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                      """, "MapToGenerator");

        yield return ("RegistryInheritedSource", """
                                                 using DwarfMapper;
                                                 namespace Demo;
                                                 public class SrcBase { public int Id { get; set; } }
                                                 [MapTo(typeof(Dto))]
                                                 public class Src : SrcBase { public string Name { get; set; } = ""; }
                                                 public class Dto { public int Id { get; set; } public string Name { get; set; } = ""; }
                                                 """, "MapToGenerator");
```

- [ ] **Step 2: Add their markers**

In `GoldenFeatureCoverageTests.FeatureMarkers()`, add two rows. The marker must be text that can only appear if the inherited member was actually mapped:

```csharp
        { "RegistryInheritedDestination", "Id = source.Id" },
        { "RegistryInheritedSource", "Id = source.Id" },
```

If the emitted assignment differs from `Id = source.Id`, run the test once, read the generated output printed in the failure message, and use the real text. Do not weaken the marker to something that would match regardless.

- [ ] **Step 3: Run the feature-coverage tests**

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~GoldenFeatureCoverageTests"`
Expected: PASS, including `Every_feature_case_in_the_corpus_has_a_marker`.

- [ ] **Step 4: Add the drift ratchet**

In `GeneratorTestingScanTests.cs`, add the ratchet **and** the `RepoRoot` helper below. That file is currently
pure reflection — it has no file access and no `RepoRoot()` of its own — so the helper must come with it or the
code will not compile. (The same helper already exists in `FixtureAdoptionScanTests.cs`,
`StructuralSurfaceCoverageTests.cs` and others; this copies that established pattern rather than inventing one.)

```csharp
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
        return dir!.FullName;
    }

    /// <summary>
    ///     Keeps the shared engine core actually shared. Two-engine drift caused 5 of the 32 audit issues, and
    ///     the divergence this ratchet guards — the registry enumerating members without walking base types —
    ///     silently dropped data for years because nothing forced the two engines to agree.
    /// </summary>
    [Fact]
    public void Neither_engine_re_implements_the_shared_core()
    {
        var root = RepoRoot();
        var extractor = File.ReadAllText(Path.Combine(root, "src", "DwarfMapper.Generator", "Pipeline",
            "MapperExtractor.cs"));
        var registry = File.ReadAllText(Path.Combine(root, "src", "DwarfMapper.Generator", "Registry",
            "MapToGenerator.cs"));

        // The registry must enumerate members only through MemberFacts. Both of its former GetMembers() calls
        // were inside its own shallow ReadableMembers/WritableMembers.
        Assert.False(registry.Contains("GetMembers()", StringComparison.Ordinal),
            "MapToGenerator calls GetMembers() directly again. Member enumeration must go through "
            + "Core.MemberFacts, or the registry silently loses inherited members as it did before.");

        // FNV-1a lives in Core.StableHash and nowhere else.
        foreach (var (name, text) in new[] { ("MapperExtractor", extractor), ("MapToGenerator", registry) })
            Assert.False(text.Contains("2166136261", StringComparison.Ordinal),
                $"{name} declares its own FNV-1a constant again. Hashing belongs to Core.StableHash — ten "
                + "copies of it is what ISSUE-015 was.");

        // Both engines must actually reference the shared core.
        Assert.Contains("MemberFacts", extractor, StringComparison.Ordinal);
        Assert.Contains("MemberFacts", registry, StringComparison.Ordinal);
    }
```

- [ ] **Step 5: Prove the ratchet fires**

Temporarily reintroduce a direct `type.GetMembers()` call in `MapToGenerator.cs` (for example inside `Extract`, assigned to a discard), run the gate, and confirm it goes RED naming the file. Then revert and confirm `git status` shows `src/` clean.

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Neither_engine_re_implements_the_shared_core"`
Expected while sabotaged: FAIL naming `MapToGenerator`. Expected after revert: PASS.

Do not commit the temporary edit.

- [ ] **Step 6: Regenerate the manifest — additive only**

Run:
```bash
DWARF_GOLDEN_UPDATE=1 dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Generated_output_matches_the_golden_manifest"
```

Then inspect the diff before trusting it:

```bash
git diff --stat tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt
git diff tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt | grep -E "^[-+]" | grep -v "^[-+][-+]" | head -20
```

Expected: exactly **two added lines** (`feat:RegistryInheritedDestination`, `feat:RegistryInheritedSource`) plus the header's case count. **No `-` lines other than the header.** If any existing fingerprint changed, revert the manifest and investigate — an existing case moved, which this plan says must not happen.

- [ ] **Step 7: Add the snapshots**

In `tests/DwarfMapper.Generator.Tests/Snapshots/SnapshotTests.Golden.cs`, add:

```csharp
    [Fact] public Task Snap_Golden_RegistryInheritedDestination() => Verify(GoldenFeature("RegistryInheritedDestination"));
    [Fact] public Task Snap_Golden_RegistryInheritedSource() => Verify(GoldenFeature("RegistryInheritedSource"));
```

Run the filter, inspect the two `.received.txt` files to confirm they contain the inherited `Id` assignment, then rename them to `.verified.txt`.

Run: `dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj --nologo --filter "FullyQualifiedName~Snap_Golden_RegistryInherited"`
Expected: PASS after accepting.

- [ ] **Step 8: Full suite**

Run: `dotnet test DwarfMapper.NET.sln --nologo`
Expected: fully green, 0 warnings, solution-wide.

- [ ] **Step 9: Commit**

```bash
git add tests/
git commit -m "test: pin the fixed inheritance behaviour and ratchet the engine drift shut

Two golden cases (RegistryInheritedDestination, RegistryInheritedSource) with feature markers and readable
snapshots, so the fix cannot silently regress. Manifest change is strictly additive — two new lines, no existing
fingerprint moved.

The ratchet asserts MapToGenerator never calls GetMembers() directly again, that neither engine re-declares an
FNV constant, and that both reference the shared core. Verified non-vacuous by reintroducing a GetMembers() call
and watching it go red. Two-engine drift caused 5 of the 32 audit issues; this is what makes the next one fail
the build instead of shipping."
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| §4.1 `StableHash` (9 consolidated, per-byte kept named) | 2 |
| §4.2 `TypeFacts` — CUT | n/a by design; the cut is recorded in the spec |
| §4.3 `MemberFacts` incl. base walk, dedup, accessor rules, rich return shape | 3 |
| §4.3 "this is the behaviour change" — registry adoption | 4 |
| §5 characterise-before-changing | 1 |
| §5 expected manifest impact (all four rows) | 2, 3, 4 (verify unchanged); 5 (additive only) |
| §5 ratchet | 5 |
| §1 the inherited-member divergence | 1 (proven), 4 (fixed), 5 (pinned) |

**Placeholder scan:** none — every step carries the code or the exact command.

**Type consistency:** `MemberFacts.Readable`/`Writable` return `(ISymbol Symbol, string Name, ITypeSymbol Type)` in Task 3 and are consumed with that shape in Task 4. `StableHash.Fnv1a`/`Fnv1aPerByte` defined in Task 2, referenced in Task 5's ratchet by constant rather than by name. `GoldenFeature(...)` in Task 5 Step 7 is the existing helper in `SnapshotTests.Golden.cs`.

**Known risk for the implementer:** Task 3 is the one place a "move" can silently become a "rewrite". The golden
manifest is the check — it must not move in Tasks 2, 3 or 4. Only Task 5 adds lines, and only two.

**Pre-execution verification (run 2026-07-23, before dispatch).** Every assumption in this plan was checked
against the source. Confirmed: the cited `MapperExtractor` line numbers; that both of `MapToGenerator`'s
`GetMembers()` calls sit inside the two methods Task 4 deletes (so the ratchet is precise); that
`GeneratorTestHarness.RunMapToWithSource` and `SnapshotTests.Golden.GoldenFeature` exist; and that the
registry really emits `Id = source.Id`, so Task 5's marker is real rather than guessed. Two defects were
found and fixed in the plan itself: `IsMemberReachable` is at `:4109` not `:4108`, and
`GeneratorTestingScanTests` has no `RepoRoot()` helper — the ratchet as first written would not have
compiled, so the helper now ships with it.
