<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Derived documentation — phases 1–2 implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every C# code fence in the README and the how-to guides an extract of compiled sample code, and make the Gallery's example catalogue come from reflecting over the sample assembly instead of three hand-maintained lists.

**Architecture:** A new non-test library `src/DwarfMapper.DocTooling` holds the pipeline: a source scanner that finds `// <snippet: id>` regions in `samples/**`, a reflection scanner that finds `[DocExample]` types in the Gallery assembly, and an injector that rewrites `<!-- snippet: id -->` and `<!-- table: name -->` blocks in markdown. The existing test project keeps only thin `[Fact]` shells plus the established `AssertCurrent` heal-or-fail contract, which writes the corrected file into the working tree and then **fails**. A ratchet test then refuses any new hand-written C# fence.

**Tech Stack:** .NET 10 (`net10.0`), C# 14, xunit 2.9.3, Roslyn 4.14.0. No new NuGet packages in these two phases — CsCheck arrives in phase 5.

**Spec:** `docs/superpowers/specs/2026-07-26-derived-documentation-design.md`

## Global Constraints

- **.NET 10 SDK required.** `~/.dotnet` is first on `PATH` via `~/.bashrc`; `/usr/bin/dotnet` is SDK 8 and fails with `NETSDK1045`. Verify with `dotnet --version` → `10.0.110`.
- **Every source file starts with** `// SPDX-License-Identifier: GPL-2.0-only` (markdown files use `<!-- SPDX-License-Identifier: GPL-2.0-only -->`).
- **Builds must be warning-clean.** `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, `AnalysisMode=All` repo-wide. Do not suppress an analyzer without a justification comment.
- **`LangVersion=latest`** is inherited from `Directory.Build.props` and resolves to C# 14.0. Prefer current idioms (collection expressions, primary constructors, `field`) in new code.
- **`DwarfMapper.DocTooling` sets `IsPackable=false`** and must **not** reference xunit. Doc tooling never enters a shipped package.
- **Heal-or-fail, never heal-and-pass.** A doc-currency test writes the corrected file and then calls `Assert.Fail`. A silently-healing test goes green in CI while the committed file stays stale.
- **Allowlists only shrink.** Follow the existing idiom of `DiagnosticTestAllowlist` and `OptionGaps.KnownSilent`: every entry carries a justification, and a test fails when an entry is no longer needed.
- **Commit with `git commit -s`** (DCO sign-off, per `CONTRIBUTING.md`).
- **Do not run a Rider/ReSharper full cleanup on these files.** It makes semantic edits — it strips `Inherited = false` from `[AttributeUsage]` and rewrites `Verifier.Verify` → `Verify`. If one runs, re-check the attribute declarations.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/DwarfMapper.DocTooling/DwarfMapper.DocTooling.csproj` | the build-time doc pipeline project; `IsPackable=false` |
| `src/DwarfMapper.DocTooling/DocToolingException.cs` | the one failure type; carries `file:line` in its message |
| `src/DwarfMapper.DocTooling/ApiReferenceRenderer.cs` | *moved* from the test project, xunit dependency removed |
| `src/DwarfMapper.DocTooling/SnippetScanner.cs` | parses `// <snippet: id>` regions out of sample source, dedents them |
| `src/DwarfMapper.DocTooling/ExampleCatalogue.cs` | reflects `[DocExample]` out of the Gallery assembly, binds each to its file |
| `src/DwarfMapper.DocTooling/DocSnippetInjector.cs` | rewrites `<!-- snippet: id -->` blocks in markdown |
| `src/DwarfMapper.DocTooling/GalleryIndexRenderer.cs` | renders the tiered Gallery index table |
| `src/DwarfMapper.DocTooling/DocTableInjector.cs` | rewrites `<!-- table: name -->` blocks, carrying prose over by row key |
| `src/DwarfMapper.DocTooling/RepoLayout.cs` | repo-root walk-up and the canonical doc/sample paths |
| `samples/DwarfMapper.Gallery/DocExample.cs` | `DocExampleAttribute` + `Tier` enum — sample-side, never shipped |
| `samples/DwarfMapper.Gallery/guides/GuideFixtures.cs` | `Customer`/`Order`/`Address` + DTOs, in the vocabulary the docs already use |
| `samples/DwarfMapper.Gallery/guides/3{0..5}_*.cs` | six composite examples the README and guides quote (Task 8) |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/SnippetScannerTests.cs` | unit tests for region parsing, dedent, malformed markers |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/DocSnippetInjectorTests.cs` | unit tests for injection and idempotence |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs` | the three reconciliation rules |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs` | the ratchet + the shrink-only allowlist |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/DocsAreSnippetCurrentTests.cs` | heal-or-fail over every doc carrying markers |

**Modified:**

| File | Change |
|---|---|
| `DwarfMapper.NET.sln` | add the `DocTooling` project under the `src` solution folder |
| `tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj` | reference `DocTooling` |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratedDocsAreCurrentTests.cs:61-83` | the API-reference facts call into `DocTooling` |
| `tests/DwarfMapper.Generator.Tests/SelfValidation/ApiReferenceRenderer.cs` | deleted (moved) |
| `samples/DwarfMapper.Gallery/*.cs` (15 files) | add `[DocExample]`, add `// <snippet: …>` regions |
| `samples/DwarfMapper.Gallery/Program.cs` | reflected loop replaces 15 hand-written calls |
| `samples/DwarfMapper.Gallery/README.md` | index table becomes an injected `<!-- table: gallery-index -->` |
| `samples/DwarfMapper.Gallery/DwarfMapper.Gallery.csproj` | nothing in these phases (the `DwarfMapper.Testing` reference arrives in phase 3) |
| `README.md` | 15 C# fences become snippet markers |
| `docs/howto/*.md` (5 files) | 12 C# fences become snippet markers; 14 `diff` fences get exempt markers |
| `CONTRIBUTING.md` | fourth ground rule |

**Deviation from the spec, deliberate:** the spec says the Gallery README is "fully generated". It is not, in this plan. That file carries hand-written prose worth keeping — the four-way declaration-style comparison and the "lambda note" — and generating it wholesale would delete them. Only its 15-row index table is mechanical, so it becomes an injected table. Update the spec's inventory row to match when this plan lands.

---

## Task 1: Extract the DocTooling library

Pure refactor. The three files under `docs/generated/` must be **byte-identical** afterwards; that invariance is the test.

**Files:**
- Create: `src/DwarfMapper.DocTooling/DwarfMapper.DocTooling.csproj`
- Create: `src/DwarfMapper.DocTooling/DocToolingException.cs`
- Create: `src/DwarfMapper.DocTooling/RepoLayout.cs`
- Create: `src/DwarfMapper.DocTooling/ApiReferenceRenderer.cs`
- Delete: `tests/DwarfMapper.Generator.Tests/SelfValidation/ApiReferenceRenderer.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratedDocsAreCurrentTests.cs`
- Modify: `DwarfMapper.NET.sln`

**Interfaces:**
- Consumes: nothing (first task).
- Produces:
  - `DwarfMapper.DocTooling.DocToolingException : Exception` — ctor `(string message)`.
  - `DwarfMapper.DocTooling.RepoLayout` — `static string Root { get; }`, `static string Docs { get; }`, `static string Samples { get; }`, `static string GalleryRoot { get; }`.
  - `DwarfMapper.DocTooling.ApiReferenceRenderer` — `static string Render(string doNotEditBanner)`.

- [ ] **Step 1: Capture the byte-exact baseline of the generated docs**

```bash
cd "$(git rev-parse --show-toplevel)"
sha256sum docs/generated/*.md > /tmp/docs-baseline.sha256
cat /tmp/docs-baseline.sha256
```

This is the assertion for step 9. Keep the file.

- [ ] **Step 2: Create the project file**

`src/DwarfMapper.DocTooling/DwarfMapper.DocTooling.csproj`:

```xml
<!-- SPDX-License-Identifier: GPL-2.0-only -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <!-- Build-time documentation infrastructure. Never packed: doc tooling must not enlarge the public
             API surface consumers take a dependency on. -->
        <IsPackable>false</IsPackable>
        <!-- Deliberately NO xunit reference. This library is mutated by Stryker, which treats test projects
             as the test-projects rather than the mutation target — a renderer living in the test project
             could never be mutated, so nothing would prove the doc tests catch a defect in the renderer. -->
    </PropertyGroup>
    <ItemGroup>
        <ProjectReference Include="..\DwarfMapper\DwarfMapper.csproj"/>
    </ItemGroup>
</Project>
```

- [ ] **Step 3: Add the failure type**

`src/DwarfMapper.DocTooling/DocToolingException.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>
///     The one failure type the doc pipeline raises. Every message must name the offending
///     <c>file:line</c>, because these fire during a test run where the only diagnostic anyone sees is the
///     exception text.
/// </summary>
/// <remarks>
///     Derives from <see cref="InvalidOperationException" /> rather than <see cref="Exception" /> so that
///     <see cref="RepoLayout.Root" /> can report an unusable working tree from a property getter without
///     tripping CA1065 (which is an error here). That is also the accurate base: every one of these means the
///     pipeline was asked to run against a repository state it cannot work in.
/// </remarks>
public sealed class DocToolingException : InvalidOperationException
{
    public DocToolingException(string message) : base(message)
    {
    }

    public DocToolingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DocToolingException()
    {
    }
}
```

The three-constructor shape is required by CA1032, which is an error here.

- [ ] **Step 4: Add the repo-layout helper**

`src/DwarfMapper.DocTooling/RepoLayout.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>
///     Resolves repository-relative paths from the test-run working directory. Every consumer needs these
///     and none of them should re-implement the walk-up.
/// </summary>
public static class RepoLayout
{
    private static string? _root;

    /// <summary>The repository root, found by walking up from the running assembly to the directory holding
    /// <c>DwarfMapper.NET.sln</c>.</summary>
    public static string Root
    {
        get
        {
            if (_root is not null) return _root;
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "DwarfMapper.NET.sln")))
                dir = Path.GetDirectoryName(dir);
            return _root = dir
                           ?? throw new DocToolingException(
                               "Could not find DwarfMapper.NET.sln above " + AppContext.BaseDirectory
                               + ". The doc pipeline reads and rewrites files in the working tree, so it "
                               + "cannot run detached from the repository.");
        }
    }

    public static string Docs => Path.Combine(Root, "docs");
    public static string Samples => Path.Combine(Root, "samples");
    public static string GalleryRoot => Path.Combine(Samples, "DwarfMapper.Gallery");
}
```

- [ ] **Step 5: Move ApiReferenceRenderer, removing the xunit dependency**

Copy `tests/DwarfMapper.Generator.Tests/SelfValidation/ApiReferenceRenderer.cs` to `src/DwarfMapper.DocTooling/ApiReferenceRenderer.cs` and make exactly three changes:

1. Change the namespace from `DwarfMapper.Generator.Tests.SelfValidation` to `DwarfMapper.DocTooling`.
2. Replace the `Assert.True` in `LoadSummaries` (currently at lines 182-185) with a throw:

```csharp
        if (!File.Exists(xmlPath))
            throw new DocToolingException(
                $"No XML documentation beside {assembly.GetName().Name} at {xmlPath}. The API reference is "
                + "rendered from it, so an empty page would misrepresent documented code as undocumented. "
                + "Check GenerateDocumentationFile is still true for that project.");
```

3. Delete the now-unused `using DwarfMapper;` only if the compiler reports it unused — `typeof(DwarfMapperAttribute)` still needs it, so it most likely stays.

Change nothing else. Any other edit risks perturbing the rendered bytes, which step 9 checks.

- [ ] **Step 6: Delete the old copy and wire the reference**

```bash
git rm tests/DwarfMapper.Generator.Tests/SelfValidation/ApiReferenceRenderer.cs
```

In `tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj`, add to the existing `ProjectReference` group:

```xml
        <ProjectReference Include="..\..\src\DwarfMapper.DocTooling\DwarfMapper.DocTooling.csproj"/>
```

In `tests/DwarfMapper.Generator.Tests/SelfValidation/GeneratedDocsAreCurrentTests.cs`, add the using:

```csharp
using DwarfMapper.DocTooling;
```

The two `[Fact]`s that call `ApiReferenceRenderer.Render(DoNotEdit)` need no other change — the type name is unchanged, only its namespace.

- [ ] **Step 7: Register the project in the solution**

```bash
cd "$(git rev-parse --show-toplevel)"
dotnet sln DwarfMapper.NET.sln add src/DwarfMapper.DocTooling/DwarfMapper.DocTooling.csproj \
  --solution-folder src
```

- [ ] **Step 8: Build and verify warning-clean**

```bash
dotnet build DwarfMapper.NET.sln -c Release
```

Expected: `0 Warning(s)`, `0 Error(s)`. A CA1032 error on `DocToolingException` means step 3's three constructors were not all added.

- [ ] **Step 9: Run the doc tests and verify the generated files did not move a byte**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GeneratedDocsAreCurrentTests"
sha256sum -c /tmp/docs-baseline.sha256
git status --short docs/
```

Expected: tests PASS, all three checksums `OK`, and `git status docs/` reports **nothing**. A modified file under `docs/generated/` means the move changed the rendering — diff it and revert the accidental edit; do not commit a regenerated file as if the refactor were intentional.

- [ ] **Step 10: Commit**

```bash
git add src/DwarfMapper.DocTooling DwarfMapper.NET.sln \
        tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj \
        tests/DwarfMapper.Generator.Tests/SelfValidation/
git commit -s -m "refactor(docs): extract the doc pipeline into DwarfMapper.DocTooling

Stryker mutates a project and treats test projects as the test-projects, so a
renderer living in the test project can never be mutated — nothing proved the
doc tests would catch a defect in the renderer itself. Moving the pipeline into
a non-test library makes it a mutation target, and puts the coming reference on
the Gallery assembly in the library rather than in the tests.

Pure refactor: docs/generated/*.md are byte-identical, which is the test."
```

---

## Task 2: `[DocExample]` and the reflected catalogue

**Files:**
- Create: `samples/DwarfMapper.Gallery/DocExample.cs`
- Create: `src/DwarfMapper.DocTooling/ExampleCatalogue.cs`
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs`
- Modify: `samples/DwarfMapper.Gallery/*.cs` (15 example files — attribute only, no regions yet)
- Modify: `src/DwarfMapper.DocTooling/DwarfMapper.DocTooling.csproj`

**Interfaces:**
- Consumes: `RepoLayout.GalleryRoot`, `DocToolingException` (Task 1).
- Produces:
  - `DwarfMapper.Gallery.Tier` — enum: `Basics`, `Configuration`, `FrontDoors`, `Advanced`, `Testing`.
  - `DwarfMapper.Gallery.DocExampleAttribute` — ctor `(int ordinal, Tier tier, string title)`; properties `Ordinal`, `Tier`, `Title` (get-only), `Shows` (get/set, defaults to `""`).
  - `DwarfMapper.DocTooling.DocExampleEntry` — `sealed record (int Ordinal, string Tier, string Title, string Shows, string RelativeFile, MethodInfo Run)`.
  - `DwarfMapper.DocTooling.ExampleCatalogue` — `static IReadOnlyList<DocExampleEntry> Scan()`, ordered by tier then ordinal.

- [ ] **Step 1: Write the failing test**

`tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The reconciliation contract between the two independent reads of the sample corpus: reflection over
///     the Gallery assembly, and a source scan of samples/**. Either one alone can go stale silently; the
///     point of this file is that they cannot go stale in the same direction.
/// </summary>
public class DocReconciliationTests
{
    [Fact]
    public void Every_gallery_example_is_discovered_by_reflection()
    {
        var examples = ExampleCatalogue.Scan();

        // 15 examples exist as of this task. The assertion is >= so adding one is not a failure, but
        // silently LOSING the catalogue (a reflection filter that matches nothing) is.
        Assert.True(examples.Count >= 15,
            $"Only {examples.Count} [DocExample] types found. The Gallery has 15 example files, so a "
            + "smaller number means the reflection filter is dropping them, not that they were deleted.");
    }

    [Fact]
    public void Example_ordinals_are_unique()
    {
        var duplicates = ExampleCatalogue.Scan()
            .GroupBy(e => e.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(e => e.Title))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Duplicate [DocExample] ordinals. Ordinal binds an example to its NN_*.cs file, so a collision "
            + "would bind one example's index entry to another's code:\n" + string.Join("\n", duplicates));
    }

    [Fact]
    public void Every_example_binds_to_exactly_one_source_file()
    {
        // Scan() throws if an ordinal matches zero or two files; this asserts the resolved paths are real
        // and distinct, which a buggy glob could satisfy vacuously by resolving everything to one file.
        var files = ExampleCatalogue.Scan().Select(e => e.RelativeFile).ToList();

        Assert.All(files, f => Assert.True(
            File.Exists(Path.Combine(RepoLayoutProbe.Root, f)), $"resolved file does not exist: {f}"));
        Assert.Equal(files.Count, files.Distinct(StringComparer.Ordinal).Count());
    }
}

/// <summary>Test-side access to the repo root without duplicating the walk-up.</summary>
internal static class RepoLayoutProbe
{
    public static string Root => RepoLayout.Root;
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests"
```

Expected: FAIL to **compile** — `ExampleCatalogue` does not exist. A compile failure is the correct first red here; there is nothing to run yet.

- [ ] **Step 3: Add the sample-side attribute**

`samples/DwarfMapper.Gallery/DocExample.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Gallery;

/// <summary>Where an example sits in the learning progression. An enum, not a string, so ordering and
/// grouping are the compiler's problem rather than a convention's.</summary>
public enum Tier
{
    Basics,
    Configuration,
    FrontDoors,
    Advanced,
    Testing
}

/// <summary>
///     Declares a Gallery example. Reflected over by DwarfMapper.DocTooling to build the runner order and
///     the generated index table, so that neither is a hand-maintained list.
///     <para>
///         Deliberately declared here in the sample and not in the DwarfMapper package: documentation
///         infrastructure must not enlarge the public API surface consumers depend on.
///     </para>
/// </summary>
/// <remarks>
///     <c>Inherited = false</c> is load-bearing — an inherited [DocExample] would report one example twice.
///     A Rider/ReSharper full cleanup is known to strip it; re-check after any cleanup run.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DocExampleAttribute(int ordinal, Tier tier, string title) : Attribute
{
    /// <summary>Position in the progression. Binds this example to its <c>NN_*.cs</c> file.</summary>
    public int Ordinal { get; } = ordinal;

    /// <summary>Which group of the generated index this example appears under.</summary>
    public Tier Tier { get; } = tier;

    /// <summary>Short title, rendered as the index row's heading.</summary>
    public string Title { get; } = title;

    /// <summary>One clause describing what the example demonstrates; the index's "Shows" column.</summary>
    public string Shows { get; set; } = "";
}
```

- [ ] **Step 4: Add the reflected catalogue**

`src/DwarfMapper.DocTooling/ExampleCatalogue.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper.Gallery;

namespace DwarfMapper.DocTooling;

/// <summary>One Gallery example, as reflection sees it, bound to the file that defines it.</summary>
public sealed record DocExampleEntry(
    int Ordinal,
    string Tier,
    string Title,
    string Shows,
    string RelativeFile,
    MethodInfo Run);

/// <summary>
///     The example catalogue, read by reflecting over the Gallery assembly. This is the assembly-scanning
///     half of the pipeline: the runner order and the generated index both come from here, so neither is a
///     list anyone maintains by hand.
/// </summary>
public static class ExampleCatalogue
{
    /// <summary>Every declared example, ordered by tier and then ordinal — the reading order.</summary>
    public static IReadOnlyList<DocExampleEntry> Scan()
    {
        var files = Directory
            .GetFiles(RepoLayout.GalleryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
            .ToList();

        return typeof(DocExampleAttribute).Assembly
            .GetTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<DocExampleAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => Build(x.Type, x.Attr!, files))
            .OrderBy(e => (int)Enum.Parse<Tier>(e.Tier))
            .ThenBy(e => e.Ordinal)
            .ToList();
    }

    private static DocExampleEntry Build(Type type, DocExampleAttribute attr, List<string> galleryFiles)
    {
        // GetTypes() rather than GetExportedTypes(): a non-public example would otherwise vanish from the
        // catalogue silently, shrinking the index rather than failing.
        var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                  ?? throw new DocToolingException(
                      $"[DocExample] type {type.FullName} has no 'public static void Run()'. The Gallery "
                      + "runner invokes it by reflection, so an example without one would be indexed but "
                      + "never run.");

        var prefix = attr.Ordinal.ToString("D2", CultureInfo.InvariantCulture) + "_";
        var matches = galleryFiles
            .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1)
            throw new DocToolingException(
                $"[DocExample({attr.Ordinal}, …)] on {type.Name} resolves to {matches.Count} files matching "
                + $"'{prefix}*.cs' under {RepoLayout.GalleryRoot} (expected exactly 1"
                + (matches.Count > 1 ? $"): {string.Join(", ", matches.Select(Path.GetFileName))}" : ").")
                + " Ordinal binds an example to its file; zero matches means the file was renamed, and two "
                + "would bind the index entry to whichever was found first.");

        return new DocExampleEntry(
            attr.Ordinal,
            attr.Tier.ToString(),
            attr.Title,
            attr.Shows,
            Path.GetRelativePath(RepoLayout.Root, matches[0]).Replace('\\', '/'),
            run);
    }
}
```

- [ ] **Step 5: Reference the Gallery from DocTooling**

In `src/DwarfMapper.DocTooling/DwarfMapper.DocTooling.csproj`, add to the `ProjectReference` group:

```xml
        <!-- The example catalogue is reflected out of the Gallery assembly. This library, not the test
             project, carries that dependency. If the Gallery stops building, the doc tests fail loudly
             rather than quietly skipping. -->
        <ProjectReference Include="..\..\samples\DwarfMapper.Gallery\DwarfMapper.Gallery.csproj"/>
```

- [ ] **Step 6: Annotate the 15 existing examples**

Add exactly one attribute line above each `public static class Example`. Do not restructure the files. Titles and `Shows` text come from the current Gallery README rows so nothing is invented:

| File | Attribute |
|---|---|
| `01_FlatMap.cs` | `[DocExample(1, Tier.Basics, "Flat map", Shows = "the simplest map — [GenerateMap<A,B>], same names and types")]` |
| `02_Rename.cs` | `[DocExample(2, Tier.Basics, "Rename a member", Shows = "[MapProperty(nameof(...), nameof(...))]")]` |
| `03_BuiltInConversions.cs` | `[DocExample(3, Tier.Basics, "Built-in conversions", Shows = "automatic widening and enum-by-name")]` |
| `04_Nested.cs` | `[DocExample(4, Tier.Basics, "Nested objects", Shows = "auto-nesting a nested (S,T) pair")]` |
| `05_Collections.cs` | `[DocExample(5, Tier.Basics, "Collections", Shows = "lists and arrays, element-by-element and bulk copy")]` |
| `06_DeepPaths.cs` | `[DocExample(6, Tier.Configuration, "Deep dotted paths", Shows = "\"Customer.Address.City\" — what others do with a lambda")]` |
| `07_Flatten.cs` | `[DocExample(7, Tier.Configuration, "Flatten", Shows = "[Flatten(\"Address\")] lifts sub-members to the top level")]` |
| `08_CustomConversion.cs` | `[DocExample(8, Tier.Configuration, "Custom conversion", Shows = "Use = nameof(Method) — the method body is the \"lambda\"")]` |
| `09_ConditionalAndValue.cs` | `[DocExample(9, Tier.Configuration, "Conditional and constant values", Shows = "When=, NullSubstitute=, and [MapValue]")]` |
| `10_RecordTarget.cs` | `[DocExample(10, Tier.Configuration, "Immutable record target", Shows = "constructor binding")]` |
| `11_Projection.cs` | `[DocExample(11, Tier.Configuration, "IQueryable projection", Shows = "the one place a Select(s => …) lambda is generated")]` |
| `12_Ergonomics.cs` | `[DocExample(12, Tier.Configuration, "Extension method and DI", Shows = "the generated x.ToGemDto() and AddDwarfMappers()")]` |
| `13_NestedListConfig.cs` | `[DocExample(13, Tier.Configuration, "Configure a collection-element map", Shows = "rename Person.Name inside a List<Person>")]` |
| `14_NestedListConfigErgonomic.cs` | `[DocExample(14, Tier.Configuration, "The same, with no partial methods", Shows = "pair-scoped [MapProperty<Person, PersonDto>] on the class")]` |
| `ex15/15_CoLocated.cs` | `[DocExample(15, Tier.FrontDoors, "Co-located on the DTO", Shows = "[GenerateMap] on a plain sealed DTO — no partial, no [DwarfMapper]")]` |

Each file needs `using DwarfMapper.Gallery;` only if its namespace is not already nested under it — `01_FlatMap.cs` declares `namespace DwarfMapper.Gallery.Ex01`, so `Tier` and `DocExampleAttribute` resolve without a using. Add one only where the compiler complains.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests"
```

Expected: 3 tests PASS. `Only N [DocExample] types found` means an attribute was missed; the message names the count.

- [ ] **Step 8: Verify the whole solution is still warning-clean**

```bash
dotnet build DwarfMapper.NET.sln -c Release
```

Expected: `0 Warning(s)`. The Gallery sets `AnalysisMode=Recommended`, but `DocExample.cs` is new public API in a sample — if CA1515 or similar fires, the csproj already has a `NoWarn` list to extend with a justification comment.

- [ ] **Step 9: Commit**

```bash
git add samples/DwarfMapper.Gallery src/DwarfMapper.DocTooling \
        tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs
git commit -s -m "feat(docs): reflect the Gallery example catalogue instead of listing it

The Gallery declared each of its 15 examples three times — the file, a
Program.cs call line, and a README row — with no test touching the latter two.
[DocExample] makes the file the single declaration and reflection the reader.

Ordinal binds an example to its NN_*.cs file, and the binding is asserted to be
exactly one file per ordinal: zero matches means a rename went unnoticed, two
would bind an index entry to whichever file was found first."
```

---

## Task 3: The snippet scanner

**Files:**
- Create: `src/DwarfMapper.DocTooling/SnippetScanner.cs`
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/SnippetScannerTests.cs`

**Interfaces:**
- Consumes: `DocToolingException`, `RepoLayout.Samples` (Task 1).
- Produces:
  - `DwarfMapper.DocTooling.SnippetRegion` — `sealed record (string Id, string Body, string RelativeFile, int StartLine)`. `Body` is dedented, `\n`-joined, with no trailing newline.
  - `DwarfMapper.DocTooling.SnippetScanner` — `static IReadOnlyList<SnippetRegion> ScanFile(string relativePath, string text)` and `static IReadOnlyDictionary<string, SnippetRegion> ScanAll()`.

- [ ] **Step 1: Write the failing tests**

`tests/DwarfMapper.Generator.Tests/SelfValidation/SnippetScannerTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Unit tests for the region parser. It rewrites tracked files, so the malformed-input cases matter as
///     much as the happy path: a marker bug that truncated a document would be silent data loss in the
///     documentation pipeline.
/// </summary>
public class SnippetScannerTests
{
    [Fact]
    public void Extracts_a_region_and_strips_the_markers()
    {
        const string source = """
            class C
            {
                // <snippet: demo>
                var x = 1;
                // </snippet>
            }
            """;

        var region = Assert.Single(SnippetScanner.ScanFile("F.cs", source));

        Assert.Equal("demo", region.Id);
        Assert.Equal("var x = 1;", region.Body);
        Assert.Equal(3, region.StartLine);
    }

    [Fact]
    public void Dedents_to_the_shallowest_line_preserving_relative_indentation()
    {
        const string source = """
            // <snippet: demo>
                    if (a)
                    {
                        b();
                    }
            // </snippet>
            """;

        Assert.Equal("if (a)\n{\n    b();\n}", SnippetScanner.ScanFile("F.cs", source)[0].Body);
    }

    [Fact]
    public void Dedents_by_common_prefix_not_by_character_count()
    {
        // A tab is one character but not one space. Counting instead of matching the actual prefix string
        // would cut a tab-indented line at the wrong place and silently corrupt the rendered snippet.
        var source = "// <snippet: demo>\n\tone\n\t\ttwo\n// </snippet>";

        Assert.Equal("one\n\ttwo", SnippetScanner.ScanFile("F.cs", source)[0].Body);
    }

    [Fact]
    public void Blank_lines_inside_a_region_survive_as_empty_lines()
    {
        const string source = """
            // <snippet: demo>
                a();

                b();
            // </snippet>
            """;

        Assert.Equal("a();\n\nb();", SnippetScanner.ScanFile("F.cs", source)[0].Body);
    }

    [Fact]
    public void An_unclosed_region_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "// <snippet: demo>\nvar x = 1;\n"));

        Assert.Contains("never closed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("F.cs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_close_without_an_open_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "var x = 1;\n// </snippet>\n"));

        Assert.Contains("no matching open", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_region_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(() => SnippetScanner.ScanFile(
            "F.cs", "// <snippet: a>\n// <snippet: b>\nx\n// </snippet>\n// </snippet>\n"));

        Assert.Contains("still open", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_region_is_a_loud_failure()
    {
        // An empty body would render as an empty code fence, which reads as "this feature needs no code".
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "// <snippet: demo>\n// </snippet>\n"));

        Assert.Contains("empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_with_no_id_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(
            () => SnippetScanner.ScanFile("F.cs", "// <snippet: >\nx\n// </snippet>\n"));

        Assert.Contains("empty id", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handles_crlf_line_endings()
    {
        var source = "// <snippet: demo>\r\n    var x = 1;\r\n// </snippet>\r\n";

        Assert.Equal("var x = 1;", SnippetScanner.ScanFile("F.cs", source)[0].Body);
    }

    [Fact]
    public void Finds_several_regions_in_one_file()
    {
        const string source = """
            // <snippet: a>
            one
            // </snippet>
            filler
            // <snippet: b>
            two
            // </snippet>
            """;

        var regions = SnippetScanner.ScanFile("F.cs", source);

        Assert.Equal(["a", "b"], regions.Select(r => r.Id));
        Assert.Equal(["one", "two"], regions.Select(r => r.Body));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~SnippetScannerTests"
```

Expected: FAIL to compile — `SnippetScanner` does not exist.

- [ ] **Step 3: Implement the scanner**

`src/DwarfMapper.DocTooling/SnippetScanner.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>One extractable region of sample source, already dedented and ready to sit inside a fence.</summary>
public sealed record SnippetRegion(string Id, string Body, string RelativeFile, int StartLine);

/// <summary>
///     Finds <c>// &lt;snippet: id&gt;</c> … <c>// &lt;/snippet&gt;</c> regions in sample source. This is the
///     source-scanning half of the pipeline; the compiled sample is the truth and this is how the docs read
///     it, so a snippet cannot describe code that does not build.
///     <para>
///         Every malformed shape throws rather than degrading. The injector writes into tracked files, and a
///         marker bug that silently dropped or truncated a region would be data loss in the documentation.
///     </para>
/// </summary>
public static class SnippetScanner
{
    private const string OpenPrefix = "// <snippet:";
    private const string CloseMarker = "// </snippet>";

    /// <summary>Every region in every sample file, keyed by id. Duplicate ids across files are refused here
    /// rather than resolved, because "whichever was found first" is not a documentation contract.</summary>
    public static IReadOnlyDictionary<string, SnippetRegion> ScanAll()
    {
        var result = new Dictionary<string, SnippetRegion>(StringComparer.Ordinal);

        foreach (var path in Directory
                     .GetFiles(RepoLayout.Samples, "*.cs", SearchOption.AllDirectories)
                     .Where(IsNotBuildOutput)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(RepoLayout.Root, path).Replace('\\', '/');
            foreach (var region in ScanFile(relative, File.ReadAllText(path)))
            {
                if (result.TryGetValue(region.Id, out var first))
                    throw new DocToolingException(
                        $"Duplicate snippet id '{region.Id}': {first.RelativeFile}:{first.StartLine} and "
                        + $"{region.RelativeFile}:{region.StartLine}. A doc marker must resolve to exactly "
                        + "one region — rename one of them.");

                result[region.Id] = region;
            }
        }

        return result;
    }

    private static bool IsNotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal);

    /// <summary>Parses one file's regions. <paramref name="relativePath" /> is used only for messages.</summary>
    public static IReadOnlyList<SnippetRegion> ScanFile(string relativePath, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var found = new List<SnippetRegion>();
        var body = new List<string>();
        string? openId = null;
        var openLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(OpenPrefix, StringComparison.Ordinal))
            {
                var id = ParseId(trimmed, relativePath, i + 1);
                if (openId is not null)
                    throw new DocToolingException(
                        $"{relativePath}:{i + 1}: snippet '{id}' opens while '{openId}' (line {openLine}) is "
                        + "still open. Nested snippet regions are not supported.");

                openId = id;
                openLine = i + 1;
                body.Clear();
                continue;
            }

            if (string.Equals(trimmed, CloseMarker, StringComparison.Ordinal))
            {
                if (openId is null)
                    throw new DocToolingException(
                        $"{relativePath}:{i + 1}: '{CloseMarker}' with no matching open marker.");

                found.Add(new SnippetRegion(
                    openId, Dedent(body, openId, relativePath, openLine), relativePath, openLine));
                openId = null;
                continue;
            }

            if (openId is not null) body.Add(lines[i]);
        }

        if (openId is not null)
            throw new DocToolingException(
                $"{relativePath}:{openLine}: snippet '{openId}' is never closed with '{CloseMarker}'.");

        return found;
    }

    private static string ParseId(string trimmedLine, string relativePath, int line)
    {
        var close = trimmedLine.IndexOf('>', StringComparison.Ordinal);
        if (close < 0)
            throw new DocToolingException(
                $"{relativePath}:{line}: malformed snippet marker '{trimmedLine}' — expected "
                + "'// <snippet: id>'.");

        var id = trimmedLine[OpenPrefix.Length..close].Trim();
        if (id.Length == 0)
            throw new DocToolingException($"{relativePath}:{line}: snippet marker has an empty id.");

        return id;
    }

    /// <summary>
    ///     Removes the longest whitespace prefix common to every non-blank line. Matched as a STRING, not
    ///     counted as characters: a tab is one character but not one space, so counting would cut a
    ///     tab-indented line at the wrong offset and corrupt the rendered snippet.
    /// </summary>
    private static string Dedent(List<string> body, string id, string relativePath, int openLine)
    {
        var kept = new List<string>(body);
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[0])) kept.RemoveAt(0);
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);

        if (kept.Count == 0)
            throw new DocToolingException(
                $"{relativePath}:{openLine}: snippet '{id}' is empty. An empty region renders as an empty "
                + "code fence, which reads as \"this feature needs no code\".");

        var nonBlank = kept.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var prefix = Whitespace(nonBlank[0]);
        foreach (var line in nonBlank)
        {
            var w = Whitespace(line);
            while (prefix.Length > 0 && !w.StartsWith(prefix, StringComparison.Ordinal))
                prefix = prefix[..^1];
        }

        return string.Join('\n', kept.Select(l =>
            string.IsNullOrWhiteSpace(l) ? "" : l[prefix.Length..]));
    }

    private static string Whitespace(string line) => line[..(line.Length - line.TrimStart().Length)];
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~SnippetScannerTests"
```

Expected: 11 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.DocTooling/SnippetScanner.cs \
        tests/DwarfMapper.Generator.Tests/SelfValidation/SnippetScannerTests.cs
git commit -s -m "feat(docs): scan snippet regions out of sample source

Dedent matches the common whitespace prefix as a string rather than counting
characters — a tab is one character but not one space, and counting would cut a
tab-indented line at the wrong offset.

Every malformed shape throws: unclosed, nested, orphan close, empty body, empty
id, duplicate id across files. The injector rewrites tracked documents, so a
marker bug that dropped a region silently would be data loss in the docs."
```

---

## Task 4: The snippet injector

**Files:**
- Create: `src/DwarfMapper.DocTooling/DocSnippetInjector.cs`
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocSnippetInjectorTests.cs`

**Interfaces:**
- Consumes: `SnippetRegion`, `SnippetScanner`, `DocToolingException` (Tasks 1, 3).
- Produces:
  - `DwarfMapper.DocTooling.InjectionResult` — `sealed record (string Markdown, IReadOnlySet<string> ReferencedIds)`.
  - `DwarfMapper.DocTooling.DocSnippetInjector` — `static InjectionResult Inject(string markdown, IReadOnlyDictionary<string, SnippetRegion> regions, string docPath)`.

- [ ] **Step 1: Write the failing tests**

`tests/DwarfMapper.Generator.Tests/SelfValidation/DocSnippetInjectorTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

public class DocSnippetInjectorTests
{
    private static IReadOnlyDictionary<string, SnippetRegion> Regions(params (string Id, string Body)[] rs) =>
        rs.ToDictionary(r => r.Id, r => new SnippetRegion(r.Id, r.Body, "F.cs", 1), StringComparer.Ordinal);

    [Fact]
    public void Fills_an_empty_marker_pair_with_a_fenced_block()
    {
        const string doc = """
            Text.

            <!-- snippet: demo -->
            <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "var x = 1;")), "d.md");

        Assert.Equal("""
            Text.

            <!-- snippet: demo -->
            ```csharp
            var x = 1;
            ```
            <!-- endsnippet -->
            """, result.Markdown.TrimEnd());
        Assert.Equal(["demo"], result.ReferencedIds);
    }

    [Fact]
    public void Replaces_a_stale_body_rather_than_appending_to_it()
    {
        const string doc = """
            <!-- snippet: demo -->
            ```csharp
            var old = 0;
            ```
            <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "var fresh = 1;")), "d.md");

        Assert.Contains("var fresh = 1;", result.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("var old = 0;", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_idempotent()
    {
        const string doc = """
            <!-- snippet: demo -->
            <!-- endsnippet -->
            """;
        var regions = Regions(("demo", "var x = 1;"));

        var once = DocSnippetInjector.Inject(doc, regions, "d.md").Markdown;
        var twice = DocSnippetInjector.Inject(once, regions, "d.md").Markdown;

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Preserves_prose_outside_the_markers_verbatim()
    {
        const string doc = """
            # Heading

            Before.

            <!-- snippet: demo -->
            <!-- endsnippet -->

            After, with a `<!-- snippet: -->`-looking mention inline.
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("demo", "x")), "d.md");

        Assert.Contains("# Heading", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("Before.", result.Markdown, StringComparison.Ordinal);
        Assert.Contains("After, with a", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_id_is_a_loud_failure()
    {
        var ex = Assert.Throws<DocToolingException>(() => DocSnippetInjector.Inject(
            "<!-- snippet: ghost -->\n<!-- endsnippet -->\n", Regions(("demo", "x")), "d.md"));

        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no sample defines it", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unclosed_marker_is_a_loud_failure_and_not_a_truncation()
    {
        // The dangerous failure mode: swallowing the rest of the file while looking for a close marker.
        var ex = Assert.Throws<DocToolingException>(() => DocSnippetInjector.Inject(
            "<!-- snippet: demo -->\nprose that must not be eaten\n", Regions(("demo", "x")), "d.md"));

        Assert.Contains("endsnippet", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_every_referenced_id_for_orphan_detection()
    {
        const string doc = """
            <!-- snippet: a -->
            <!-- endsnippet -->
            <!-- snippet: b -->
            <!-- endsnippet -->
            """;

        var result = DocSnippetInjector.Inject(doc, Regions(("a", "1"), ("b", "2")), "d.md");

        Assert.Equal(["a", "b"], result.ReferencedIds.OrderBy(x => x, StringComparer.Ordinal));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocSnippetInjectorTests"
```

Expected: FAIL to compile — `DocSnippetInjector` does not exist.

- [ ] **Step 3: Implement the injector**

`src/DwarfMapper.DocTooling/DocSnippetInjector.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.DocTooling;

/// <summary>The rewritten document, plus which snippet ids it referenced (for orphan detection).</summary>
public sealed record InjectionResult(string Markdown, IReadOnlySet<string> ReferencedIds);

/// <summary>
///     Rewrites <c>&lt;!-- snippet: id --&gt;</c> … <c>&lt;!-- endsnippet --&gt;</c> blocks in markdown,
///     replacing each body with a fenced extract of the named sample region. Prose outside the markers is
///     copied through untouched — this injects into hand-written documents, it does not generate them.
/// </summary>
public static class DocSnippetInjector
{
    private const string OpenPrefix = "<!-- snippet:";
    private const string CloseMarker = "<!-- endsnippet -->";

    public static InjectionResult Inject(
        string markdown, IReadOnlyDictionary<string, SnippetRegion> regions, string docPath)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(regions);

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new StringBuilder();
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith(OpenPrefix, StringComparison.Ordinal))
            {
                sb.Append(line).Append('\n');
                i++;
                continue;
            }

            var id = ParseId(trimmed, docPath, i + 1);
            if (!regions.TryGetValue(id, out var region))
                throw new DocToolingException(
                    $"{docPath}:{i + 1}: snippet '{id}' is referenced here but no sample defines it. Add a "
                    + $"'// <snippet: {id}>' region to a file under samples/, or fix the id.");

            referenced.Add(id);
            var closeIndex = FindClose(lines, i + 1, docPath, id, i + 1);

            sb.Append(line).Append('\n');
            sb.Append("```csharp\n").Append(region.Body).Append("\n```\n");
            sb.Append(lines[closeIndex]).Append('\n');
            i = closeIndex + 1;
        }

        return new InjectionResult(sb.ToString().TrimEnd() + "\n", referenced);
    }

    private static string ParseId(string trimmedLine, string docPath, int line)
    {
        var end = trimmedLine.IndexOf("-->", StringComparison.Ordinal);
        if (end < 0)
            throw new DocToolingException(
                $"{docPath}:{line}: malformed marker '{trimmedLine}' — expected '<!-- snippet: id -->'.");

        var id = trimmedLine[OpenPrefix.Length..end].Trim();
        if (id.Length == 0)
            throw new DocToolingException($"{docPath}:{line}: snippet marker has an empty id.");

        return id;
    }

    /// <summary>
    ///     Finds the closing marker. Running off the end throws rather than treating the rest of the file as
    ///     a snippet body — that would delete every following paragraph on the next write.
    /// </summary>
    private static int FindClose(string[] lines, int from, string docPath, string id, int openLine)
    {
        for (var i = from; i < lines.Length; i++)
            if (string.Equals(lines[i].TrimStart(), CloseMarker, StringComparison.Ordinal))
                return i;

        throw new DocToolingException(
            $"{docPath}:{openLine}: snippet '{id}' is never closed with '{CloseMarker}'. Refusing to treat "
            + "the rest of the file as its body.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocSnippetInjectorTests"
```

Expected: 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DwarfMapper.DocTooling/DocSnippetInjector.cs \
        tests/DwarfMapper.Generator.Tests/SelfValidation/DocSnippetInjectorTests.cs
git commit -s -m "feat(docs): inject sample regions into markdown snippet markers

Injection, not generation: prose outside the markers is copied through
verbatim, so these stay hand-written documents.

An unclosed marker throws instead of consuming the rest of the file as a
snippet body — that failure mode would delete every following paragraph on the
next write, and it would look like a successful regeneration."
```

---

## Task 5: Reconciliation and heal-or-fail over the real docs

Wires the pieces to the working tree for the first time. Nothing is converted yet, so the marker set is empty and every rule holds trivially — which is the point: the harness must be green *before* it has work to do, or a later red is ambiguous.

**Files:**
- Create: `src/DwarfMapper.DocTooling/DocSet.cs`
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocsAreSnippetCurrentTests.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs`

**Interfaces:**
- Consumes: `SnippetScanner.ScanAll`, `DocSnippetInjector.Inject`, `ExampleCatalogue.Scan`, `RepoLayout` (Tasks 1–4).
- Produces:
  - `DwarfMapper.DocTooling.DocSet` — `static IReadOnlyList<string> All { get; }` (repo-relative paths of every markdown file the pipeline owns), `static string Read(string relativePath)`.

- [ ] **Step 1: Add the document set**

`src/DwarfMapper.DocTooling/DocSet.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>
///     The markdown files the pipeline owns. An explicit list, not a glob: docs/superpowers/ holds specs and
///     plans whose code blocks are design sketches of code that does not exist yet, and a glob would demand
///     they resolve to real samples.
/// </summary>
public static class DocSet
{
    public static IReadOnlyList<string> All { get; } =
    [
        "README.md",
        "CONTRIBUTING.md",
        "docs/diagnostics.md",
        "docs/options.md",
        "docs/COMPARISON.md",
        "docs/CORRECTNESS.md",
        "docs/MIGRATION.md",
        "docs/howto/README.md",
        "docs/howto/ambient-cross-assembly-maps.md",
        "docs/howto/common-changes.md",
        "docs/howto/deploy-and-optimize.md",
        "docs/howto/migrate-from-automapper.md",
        "docs/howto/migrate-from-handwritten.md",
        "docs/howto/migrate-from-mapperly.md",
        "docs/howto/migrate-from-mapster.md",
        "samples/DwarfMapper.Gallery/README.md"
    ];

    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoLayout.Root, relativePath));
}
```

- [ ] **Step 2: Write the failing heal-or-fail test**

`tests/DwarfMapper.Generator.Tests/SelfValidation/DocsAreSnippetCurrentTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Heal-or-fail over every document the pipeline owns: the corrected text is written into the working
///     tree so the diff is right there, and then the test FAILS so it has to be committed.
///     <para>
///         Deliberately not healing quietly. A healing doc test goes green in CI while the committed file
///         people actually read stays stale — the state this whole pipeline exists to prevent.
///     </para>
/// </summary>
public class DocsAreSnippetCurrentTests
{
    [Fact]
    public void Every_snippet_marker_in_every_doc_matches_its_sample()
    {
        var regions = SnippetScanner.ScanAll();
        var stale = new List<string>();

        foreach (var relative in DocSet.All)
        {
            var committed = DocSet.Read(relative);
            var injected = DocSnippetInjector.Inject(committed, regions, relative).Markdown;

            if (string.Equals(Normalise(committed), Normalise(injected), StringComparison.Ordinal)) continue;

            File.WriteAllText(Path.Combine(RepoLayout.Root, relative), injected);
            stale.Add(relative);
        }

        Assert.True(stale.Count == 0,
            "Snippet(s) in these documents no longer match the sample code they came from. Each file has "
            + "been regenerated in your working tree — review the diff and commit it:\n  "
            + string.Join("\n  ", stale)
            + "\n\nThis fails rather than healing quietly on purpose: a healing doc test goes green in CI "
            + "while the file people read stays stale.");
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
```

- [ ] **Step 3: Add the two remaining reconciliation rules**

Append to `tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs`, inside the class:

```csharp
    [Fact]
    public void No_snippet_region_is_orphaned()
    {
        // A region no document references is maintained forever and read by nobody.
        var regions = SnippetScanner.ScanAll();
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relative in DocSet.All)
            referenced.UnionWith(
                DocSnippetInjector.Inject(DocSet.Read(relative), regions, relative).ReferencedIds);

        var orphans = regions.Values
            .Where(r => !referenced.Contains(r.Id))
            .Select(r => $"{r.Id} ({r.RelativeFile}:{r.StartLine})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "Snippet region(s) that no document references. Reference them, or delete the markers:\n  "
            + string.Join("\n  ", orphans));
    }

    [Fact]
    public void Every_gallery_example_owns_at_least_one_snippet_region()
    {
        // An example the docs cannot quote is invisible to every reader who does not browse samples/.
        // Enforced only for the Gallery: regions in AotSample exist to be quoted, not to be examples.
        var regions = SnippetScanner.ScanAll().Values
            .GroupBy(r => r.RelativeFile, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var silent = ExampleCatalogue.Scan()
            .Where(e => !regions.ContainsKey(e.RelativeFile))
            .Select(e => $"{e.Ordinal:D2} {e.Title} ({e.RelativeFile})")
            .ToList();

        Assert.True(silent.Count == 0,
            "Gallery example(s) with no '// <snippet: …>' region, so no document can quote them:\n  "
            + string.Join("\n  ", silent));
    }
```

- [ ] **Step 4: Run and expect the third rule to FAIL**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests|FullyQualifiedName~DocsAreSnippetCurrentTests"
```

Expected: `Every_gallery_example_owns_at_least_one_snippet_region` FAILS listing all 15 examples — no regions exist yet. The other tests PASS. This is the red that Task 7 turns green.

- [ ] **Step 5: Mark the failing rule as pending, with the reason**

Do **not** weaken the assertion. Add `Skip` naming the task that satisfies it, so the red is recorded rather than lost:

```csharp
    [Fact(Skip = "Regions are added in Task 7. Remove this Skip in that task's first step — it is the "
                 + "test that proves the retrofit is complete.")]
    public void Every_gallery_example_owns_at_least_one_snippet_region()
```

- [ ] **Step 6: Verify green**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests|FullyQualifiedName~DocsAreSnippetCurrentTests"
git status --short
```

Expected: 5 pass, 1 skipped, and `git status` shows **no modified markdown** — with no markers present, injection is a no-op, which is the harness proving it is inert before it has work.

- [ ] **Step 7: Commit**

```bash
git add src/DwarfMapper.DocTooling/DocSet.cs \
        tests/DwarfMapper.Generator.Tests/SelfValidation/DocsAreSnippetCurrentTests.cs \
        tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs
git commit -s -m "feat(docs): heal-or-fail snippet currency and the reconciliation rules

Three rules, each naming a distinct decay: a marker must resolve to exactly one
region, a region must be referenced by at least one document, and a Gallery
example must own at least one region.

The document set is an explicit list rather than a glob — docs/superpowers/
holds specs whose code blocks sketch code that does not exist yet, and a glob
would demand they resolve to real samples.

The example-owns-a-region rule is Skipped until the retrofit lands, so the red
is recorded rather than quietly weakened."
```

---

## Task 6: The fence ratchet

**Files:**
- Create: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs`

**Interfaces:**
- Consumes: `DocSet.All` and `DocSet.Read` (Task 5).
- Produces: nothing consumed by later tasks; Tasks 9 and 10 shrink its allowlist.

- [ ] **Step 1: Write the test with a shrink-only allowlist**

`tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.DocTooling;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     The ratchet. Every ```csharp fence must be inside a snippet marker pair or carry an explicit
///     exemption, so hand-written C# cannot creep back into the documentation after the conversion.
///     <para>
///         Scoped to csharp fences on purpose. diff/bash/xml/ini fences are out of scope by language and
///         need no marker: competitor "before" code cannot compile here, and a shell command is not an API.
///     </para>
/// </summary>
public class DocFenceScanTests
{
    private const string ExemptMarker = "<!-- fence-exempt:";

    /// <summary>
    ///     Documents whose csharp fences are not yet converted, with the task that converts them. This list
    ///     must only SHRINK. An entry that is no longer needed fails the companion test below, so the
    ///     ratchet tightens as the conversion lands rather than being quietly retained.
    /// </summary>
    private static readonly Dictionary<string, string> Unconverted = new(StringComparer.Ordinal)
    {
        ["README.md"] = "converted in Task 9",
        ["docs/howto/ambient-cross-assembly-maps.md"] = "converted in Task 10",
        ["docs/howto/common-changes.md"] = "converted in Task 10",
        ["docs/howto/migrate-from-automapper.md"] = "converted in Task 10",
        ["docs/howto/migrate-from-handwritten.md"] = "converted in Task 10",
        ["docs/howto/migrate-from-mapster.md"] = "converted in Task 10",
        ["docs/diagnostics.md"] = "converted in phase 4, when its tables are injected"
    };

    [Fact]
    public void No_hand_written_csharp_fence_outside_a_snippet_or_an_exemption()
    {
        var offenders = new List<string>();

        foreach (var relative in DocSet.All.Where(d => !Unconverted.ContainsKey(d)))
            offenders.AddRange(UnbackedFences(relative, DocSet.Read(relative)));

        Assert.True(offenders.Count == 0,
            "Hand-written C# fence(s) found. Back each with a '<!-- snippet: id -->' pair whose region "
            + "lives in a compiled sample, or mark it '<!-- fence-exempt: reason -->' immediately above:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_unconverted_entry_still_has_an_unbacked_fence()
    {
        // An entry left behind after conversion would silently re-permit hand-written fences in that file.
        var fixedUp = Unconverted.Keys
            .Where(d => UnbackedFences(d, DocSet.Read(d)).Count == 0)
            .ToList();

        Assert.True(fixedUp.Count == 0,
            "These documents have no unbacked csharp fences left — remove them from Unconverted so the "
            + "ratchet tightens: " + string.Join(", ", fixedUp));
    }

    /// <summary>Returns "path:line" for every ```csharp fence that is neither inside a snippet marker pair
    /// nor immediately preceded by an exemption comment.</summary>
    private static List<string> UnbackedFences(string relative, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var offenders = new List<string>();
        var insideSnippet = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("<!-- snippet:", StringComparison.Ordinal)) insideSnippet = true;
            else if (string.Equals(trimmed, "<!-- endsnippet -->", StringComparison.Ordinal))
                insideSnippet = false;

            if (!trimmed.StartsWith("```csharp", StringComparison.Ordinal) || insideSnippet) continue;

            var exempt = PrecedingComment(lines, i)?.StartsWith(ExemptMarker, StringComparison.Ordinal)
                         ?? false;
            if (!exempt) offenders.Add($"{relative}:{i + 1}");
        }

        return offenders;
    }

    /// <summary>The nearest non-blank line above <paramref name="index" />, trimmed.</summary>
    private static string? PrecedingComment(string[] lines, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0) continue;
            return trimmed;
        }

        return null;
    }
}
```

- [ ] **Step 2: Run it**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocFenceScanTests"
```

Expected: both PASS. Every document with csharp fences is currently in `Unconverted`, and each genuinely still has unbacked fences.

- [ ] **Step 3: Prove the ratchet is not vacuous**

Temporarily append a hand-written fence to a document that is *not* in the allowlist:

```bash
printf '\n```csharp\nvar sneaky = 1;\n```\n' >> docs/COMPARISON.md
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocFenceScanTests"
git checkout -- docs/COMPARISON.md
```

Expected: `No_hand_written_csharp_fence_outside_a_snippet_or_an_exemption` FAILS naming `docs/COMPARISON.md:<line>`. If it passes, the scan is not reaching that file — fix it before committing, or the ratchet is decoration.

- [ ] **Step 4: Verify the tree is clean again**

```bash
git status --short
```

Expected: only the new test file is untracked. `docs/COMPARISON.md` must be unmodified.

- [ ] **Step 5: Commit**

```bash
git add tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs
git commit -s -m "test(docs): ratchet refusing hand-written C# fences

Every csharp fence must sit inside a snippet marker pair or carry an explicit
'<!-- fence-exempt: reason -->'. The reason is required, so an exemption is a
recorded decision rather than a silent bypass.

Files not yet converted sit in a shrink-only allowlist, following the
DiagnosticTestAllowlist / OptionGaps.KnownSilent idiom: a companion test fails
when an entry is no longer needed, so the ratchet tightens as conversion lands.

diff/bash/xml/ini fences are out of scope by language — competitor \"before\"
code cannot compile here and a shell command is not an API."
```

---

## Task 7: Retrofit the Gallery — regions, generated index, reflected runner

**Files:**
- Create: `src/DwarfMapper.DocTooling/GalleryIndexRenderer.cs`
- Create: `src/DwarfMapper.DocTooling/DocTableInjector.cs`
- Modify: `samples/DwarfMapper.Gallery/*.cs` (15 files — add regions)
- Modify: `samples/DwarfMapper.Gallery/Program.cs`
- Modify: `samples/DwarfMapper.Gallery/README.md`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocReconciliationTests.cs`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocsAreSnippetCurrentTests.cs`

**Interfaces:**
- Consumes: `ExampleCatalogue.Scan`, `SnippetScanner.ScanAll`, `DocSet` (Tasks 2, 3, 5).
- Produces:
  - `DwarfMapper.DocTooling.DocTableInjector` — `static string Inject(string markdown, string tableName, IReadOnlyList<string> renderedRows, string docPath)`; replaces the body of a `<!-- table: name -->` … `<!-- endtable -->` pair.
  - `DwarfMapper.DocTooling.GalleryIndexRenderer` — `static IReadOnlyList<string> RenderRows()`.

- [ ] **Step 1: Un-skip the pending rule so the task starts red**

In `DocReconciliationTests.cs`, change `[Fact(Skip = "…")]` back to `[Fact]` on `Every_gallery_example_owns_at_least_one_snippet_region`, then:

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~Every_gallery_example_owns_at_least_one_snippet_region"
```

Expected: FAIL listing all 15 examples.

- [ ] **Step 2: Add one region per example**

In each of the 15 files, wrap the *teaching* lines — the mapper declaration and the attributes that configure it — in a region. Wrap the mapper, **not** the `Example` class: the `Run` method is harness noise, and the DTO definitions are usually obvious from context. Use the ids below; Tasks 8 and 9 reference them by name.

| File | Region id | Wrap |
|---|---|---|
| `01_FlatMap.cs` | `flat-map` | the `[DwarfMapper] [GenerateMap<Person, PersonDto>] public partial class Mapper` declaration |
| `02_Rename.cs` | `rename` | the mapper class with its `[MapProperty]` |
| `03_BuiltInConversions.cs` | `built-in-conversions` | the mapper class |
| `04_Nested.cs` | `nested` | the mapper class |
| `05_Collections.cs` | `collections` | the mapper class |
| `06_DeepPaths.cs` | `deep-paths` | the mapper class with its dotted `[MapProperty]` |
| `07_Flatten.cs` | `flatten` | the mapper class with `[Flatten]` |
| `08_CustomConversion.cs` | `custom-conversion` | the mapper class **and** the `Use=` target method |
| `09_ConditionalAndValue.cs` | `conditional-and-value` | the mapper class |
| `10_RecordTarget.cs` | `record-target` | the target `record` declaration **and** the mapper class |
| `11_Projection.cs` | `projection` | the mapper class with its `Project` method |
| `12_Ergonomics.cs` | `ergonomics` | the mapper class **and** the `AddDwarfMappers()` call site |
| `13_NestedListConfig.cs` | `nested-list-config` | the mapper class |
| `14_NestedListConfigErgonomic.cs` | `nested-list-config-ergonomic` | the mapper class with its pair-scoped attributes |
| `ex15/15_CoLocated.cs` | `co-located` | the DTO carrying `[GenerateMap]` |

Where two non-adjacent constructs both matter (08, 10, 12), nesting is refused by the scanner — use a single region spanning both, or move the constructs adjacent. Prefer moving them adjacent; do not add a second region id, because Task 8's markers assume one id per example.

Example, `06_DeepPaths.cs`:

```csharp
// <snippet: deep-paths>
[DwarfMapper]
public partial class Mapper
{
    [MapProperty("Customer.Address.City", nameof(OrderDto.City))]
    public partial OrderDto Map(Order order);
}
// </snippet>
```

- [ ] **Step 3: Verify rule 3 goes green and rule 2 goes red**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests"
```

Expected: `Every_gallery_example_owns_at_least_one_snippet_region` PASSES; `No_snippet_region_is_orphaned` now FAILS listing all 15 ids, because no document references them yet. That red is correct and is cleared by step 7 and Task 8.

- [ ] **Step 4: Add the table injector**

`src/DwarfMapper.DocTooling/DocTableInjector.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.DocTooling;

/// <summary>
///     Rewrites the body of a <c>&lt;!-- table: name --&gt;</c> … <c>&lt;!-- endtable --&gt;</c> pair. Kept
///     separate from the snippet injector because a table's rows are rendered from reflection while a
///     snippet's body is extracted from source — same marker shape, different truth source.
/// </summary>
public static class DocTableInjector
{
    public static string Inject(
        string markdown, string tableName, IReadOnlyList<string> renderedRows, string docPath)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(renderedRows);

        var open = $"<!-- table: {tableName} -->";
        const string close = "<!-- endtable -->";

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines, l => string.Equals(l.TrimStart(), open, StringComparison.Ordinal));
        if (start < 0)
            throw new DocToolingException(
                $"{docPath}: no '{open}' marker. The table is rendered from code and has nowhere to go.");

        var end = Array.FindIndex(lines, start + 1,
            l => string.Equals(l.TrimStart(), close, StringComparison.Ordinal));
        if (end < 0)
            throw new DocToolingException(
                $"{docPath}: '{open}' is never closed with '{close}'. Refusing to treat the rest of the file "
                + "as table body.");

        var sb = new StringBuilder();
        for (var i = 0; i <= start; i++) sb.Append(lines[i]).Append('\n');
        foreach (var row in renderedRows) sb.Append(row).Append('\n');
        for (var i = end; i < lines.Length; i++) sb.Append(lines[i]).Append('\n');

        return sb.ToString().TrimEnd() + "\n";
    }
}
```

- [ ] **Step 5: Add the index renderer**

`src/DwarfMapper.DocTooling/GalleryIndexRenderer.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.DocTooling;

/// <summary>
///     Renders the Gallery index from the reflected catalogue, grouped by tier. Replaces a hand-maintained
///     table that no test touched — deleting an example used to leave its row behind indefinitely.
/// </summary>
public static class GalleryIndexRenderer
{
    public static IReadOnlyList<string> RenderRows()
    {
        var rows = new List<string> { "| # | Example | Shows |", "|---|---|---|" };
        string? tier = null;

        foreach (var e in ExampleCatalogue.Scan())
        {
            if (!string.Equals(tier, e.Tier, StringComparison.Ordinal))
            {
                tier = e.Tier;
                rows.Add($"| | **{Spaced(e.Tier)}** | |");
            }

            var file = Path.GetRelativePath(
                Path.Combine(RepoLayout.Root, "samples", "DwarfMapper.Gallery"),
                Path.Combine(RepoLayout.Root, e.RelativeFile)).Replace('\\', '/');

            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {e.Ordinal:D2} | [`{Path.GetFileName(file)}`]({file}) | {e.Shows} |"));
        }

        return rows;
    }

    /// <summary>"FrontDoors" reads as a heading, not an identifier.</summary>
    private static string Spaced(string tier) => tier switch
    {
        "FrontDoors" => "Front doors",
        _ => tier
    };
}
```

- [ ] **Step 6: Convert the Gallery README's table to a marker**

In `samples/DwarfMapper.Gallery/README.md`, replace the 15-row index table (and its header row) with:

```markdown
<!-- table: gallery-index -->
<!-- endtable -->
```

Leave every other part of the file untouched — the "Which declaration style should I use?" comparison and "The lambda note" are hand-written prose worth keeping, which is why this file gets an injected table rather than being generated wholesale.

- [ ] **Step 7: Extend the currency test to cover the table**

In `DocsAreSnippetCurrentTests.cs`, replace the body of the loop so tables are injected alongside snippets:

```csharp
        foreach (var relative in DocSet.All)
        {
            var committed = DocSet.Read(relative);
            var injected = DocSnippetInjector.Inject(committed, regions, relative).Markdown;

            if (string.Equals(relative, "samples/DwarfMapper.Gallery/README.md", StringComparison.Ordinal))
                injected = DocTableInjector.Inject(
                    injected, "gallery-index", GalleryIndexRenderer.RenderRows(), relative);

            if (string.Equals(Normalise(committed), Normalise(injected), StringComparison.Ordinal)) continue;

            File.WriteAllText(Path.Combine(RepoLayout.Root, relative), injected);
            stale.Add(relative);
        }
```

- [ ] **Step 8: Run, let it heal the README, then re-run**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
git diff samples/DwarfMapper.Gallery/README.md
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
```

Expected: first run FAILS and writes the table; the diff shows 15 rows in 3 tier groups; the second run PASSES. Review that diff properly — it is the first output this pipeline has produced.

- [ ] **Step 9: Replace Program.cs's hand-written call list**

`samples/DwarfMapper.Gallery/Program.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// DwarfMapper Gallery — a progression of mapping examples, simplest first.
// Each NN_*.cs file is a self-contained, annotated example. Run this project to execute them all:
//   dotnet run --project samples/DwarfMapper.Gallery
//
// The example list is DISCOVERED, not written down: every [DocExample] type is found by reflection and run
// in tier order. Adding a file adds a step; deleting one removes it. The same catalogue renders the index
// table in README.md, so the two cannot disagree.
//
// NOTE ON REFLECTION: this is the sample HARNESS, not the mapper. DwarfMapper itself performs no reflection
// — every map is resolved at compile time, which is what makes it AOT- and trim-safe. The Gallery is not an
// AOT target (samples/DwarfMapper.AotSample is, and it is the CI gate). Nothing here touches a mapping path.

using System.Reflection;
using DwarfMapper.Gallery;

Console.WriteLine("=== DwarfMapper Gallery — simple → advanced ===");
Console.WriteLine();

var examples = Assembly.GetExecutingAssembly().GetTypes()
    .Select(t => (Type: t, Attr: t.GetCustomAttribute<DocExampleAttribute>()))
    .Where(x => x.Attr is not null)
    .OrderBy(x => x.Attr!.Tier)
    .ThenBy(x => x.Attr!.Ordinal)
    .ToList();

if (examples.Count == 0)
    throw new InvalidOperationException(
        "No [DocExample] types found. The Gallery would print nothing and exit 0, which reads as success.");

Tier? tier = null;
foreach (var (type, attr) in examples)
{
    if (tier != attr!.Tier)
    {
        tier = attr.Tier;
        Console.WriteLine($"-- {tier} --");
    }

    var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
              ?? throw new InvalidOperationException($"{type.Name} has no public static Run().");
    run.Invoke(null, null);
}

Console.WriteLine();
Console.WriteLine($"=== {examples.Count} examples — open each NN_*.cs file for the annotated source ===");
```

- [ ] **Step 10: Run the Gallery and confirm all 15 still execute**

```bash
dotnet run --project samples/DwarfMapper.Gallery -c Release
```

Expected: tier headings, all 15 example lines, and a closing `=== 15 examples ===`. A missing example means its attribute was skipped in Task 2 step 6.

- [ ] **Step 11: Full build and test**

```bash
dotnet build DwarfMapper.NET.sln -c Release && dotnet test DwarfMapper.NET.sln -c Release
```

Expected: `0 Warning(s)`, all tests pass except `No_snippet_region_is_orphaned`, which stays red until Task 8 references the ids. If anything else fails, fix it here — do not carry a red into the next task.

- [ ] **Step 12: Commit**

```bash
git add samples/DwarfMapper.Gallery src/DwarfMapper.DocTooling \
        tests/DwarfMapper.Generator.Tests/SelfValidation/
git commit -s -m "feat(docs): Gallery index and runner come from the catalogue

Program.cs discovers examples by reflection instead of listing 15 calls, and
README.md's index becomes an injected table. Adding a file now adds a step and
a row; deleting one removes both. Previously a deleted example left its README
row behind indefinitely, because no test read that table.

The README is NOT generated wholesale, contrary to the spec's inventory row:
its declaration-style comparison and lambda note are hand-written prose worth
keeping, so only the mechanical table is injected. Spec updated to match.

The reflection is the sample harness, not the mapper — noted in Program.cs so
nobody reads it as DwarfMapper reflecting. The Gallery is not an AOT target."
```

---

## Task 8: Add the guide-fixture examples

**Why this task exists.** The docs and the Gallery do not speak the same language. Every fence in the README
and the guides is written in a `Customer` / `Order` / `Src` / `Dst` vocabulary, and most are **composites** —
one mapper doing a rename *and* a `Use=` conversion *and* a `[MapValue]` *and* an `[AfterMap]`. The Gallery
uses dwarf-themed fixtures (`Person`, `Gem`, `Place`, `Moria`) and deliberately shows one feature per example.
So for most fences there is nothing to extract *from*: conversion needs new sample code, not a marker swap.

These composites are worth having regardless. The Gallery has no example of a realistic mapper doing several
things at once, which is exactly the shape a migrating reader arrives with — so this closes a real gap in the
corpus rather than adding doc-only scaffolding.

Ordinals 30–35 are used, leaving 16–26 free for the phase-3 tiers named in the spec.

**Files:**
- Create: `samples/DwarfMapper.Gallery/guides/GuideFixtures.cs`
- Create: `samples/DwarfMapper.Gallery/guides/30_CompositeMapper.cs`
- Create: `samples/DwarfMapper.Gallery/guides/31_GenerateMapPairs.cs`
- Create: `samples/DwarfMapper.Gallery/guides/32_FourWaysToCall.cs`
- Create: `samples/DwarfMapper.Gallery/guides/33_ExplicitDirectives.cs`
- Create: `samples/DwarfMapper.Gallery/guides/34_ReverseMapAndHooks.cs`
- Create: `samples/DwarfMapper.Gallery/guides/35_AmbientFacade.cs`
- Modify: `samples/DwarfMapper.Gallery/DocExample.cs` (add `Tier.Guides`)

**Interfaces:**
- Consumes: `DocExampleAttribute`, `Tier` (Task 2); the region syntax from Task 3.
- Produces: these region ids, consumed by Tasks 9 and 10 —
  `composite-mapper`, `case-insensitive-rename`, `generate-map-pairs`, `four-ways-to-call`,
  `explicit-directives`, `reverse-map`, `after-map-hook`, `ctor-injection`, `ambient-facade`.

- [ ] **Step 1: Add the tier**

In `samples/DwarfMapper.Gallery/DocExample.cs`, add `Guides` to the `Tier` enum, **after** `Testing` so the
existing tiers keep their ordering:

```csharp
public enum Tier
{
    Basics,
    Configuration,
    FrontDoors,
    Advanced,
    Testing,

    /// <summary>Composite mappers in the vocabulary the README and the migration guides use. These exist to
    /// be quoted by the prose and to show a realistic mapper doing several things at once.</summary>
    Guides
}
```

In `GalleryIndexRenderer.Spaced`, no change is needed — `Guides` already reads as a heading.

- [ ] **Step 2: Add the shared fixtures**

`samples/DwarfMapper.Gallery/guides/GuideFixtures.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// Fixtures shared by the guide examples (30+). Named to match the vocabulary the README and the migration
// guides already use — Customer, Order, Address — so a snippet lifted into that prose reads as if it had
// been written there.

namespace DwarfMapper.Gallery.Guides;

public sealed class Address
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
    public Address Address { get; set; } = new();
}

public sealed class CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Total { get; set; } = "";
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class OrderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Total { get; set; }
    public string Source { get; set; } = "";
}
```

- [ ] **Step 3: Add 30 — the composite mapper**

`samples/DwarfMapper.Gallery/guides/30_CompositeMapper.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// 30 — One mapper doing three things at once: a rename, a custom scalar conversion, and a flatten.
// This is the shape a migrating reader arrives with, and the shape the README and the AutoMapper guide use.

using System.Globalization;

namespace DwarfMapper.Gallery.Guides;

// <snippet: composite-mapper>
[DwarfMapper]
public partial class CustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]                            // rename
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(FormatMoney))]   // conversion
    [Flatten(nameof(Customer.Address))]                                                           // Address.City -> City
    public partial CustomerDto ToDto(Customer src);

    private static string FormatMoney(decimal d) =>
        d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
// </snippet>

// <snippet: case-insensitive-rename>
[DwarfMapper(CaseInsensitive = true)]      // opt-in: match 'name' to 'Name'
public partial class LenientCustomerMapper
{
    [MapProperty(nameof(Customer.FullName), nameof(CustomerDto.Name))]  // explicit rename
    [MapProperty(nameof(Customer.Total), nameof(CustomerDto.Total), Use = nameof(Money))]
    [Flatten(nameof(Customer.Address))]
    public partial CustomerDto ToDto(Customer src);

    private static string Money(decimal d) => d.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
// </snippet>

[DocExample(30, Tier.Guides, "A composite mapper",
    Shows = "rename, custom conversion, and flatten in one mapper")]
public static class Example
{
    public static void Run()
    {
        var dto = new CustomerMapper().ToDto(new Customer
        {
            Id = 1, FullName = "Ada Lovelace", Total = 12.5m,
            Address = new Address { City = "London", Zip = "NW1" }
        });

        Console.WriteLine($"30 Composite mapper   -> {dto.Name}, {dto.Total}, {dto.City} {dto.Zip}");
    }
}
```

- [ ] **Step 4: Add 31 — two pairs on one class**

`samples/DwarfMapper.Gallery/guides/31_GenerateMapPairs.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// 31 — Declaring several pairs on one class with [GenerateMap], no method per pair.
// The overload is resolved by the SOURCE type, which is why one class cannot map one source to two targets.

namespace DwarfMapper.Gallery.Guides;

// <snippet: generate-map-pairs>
[DwarfMapper]
[GenerateMap<Order, OrderDto>]
[GenerateMap<Customer, CustomerDto>]
public partial class Mappers { }

// usage — overload resolved by the source type:
// OrderDto dto = new Mappers().Map(order);
// </snippet>

[DocExample(31, Tier.Guides, "Several pairs on one class",
    Shows = "[GenerateMap<A,B>] stacked — the AutoMapper CreateMap shape")]
public static class Example
{
    public static void Run()
    {
        var mappers = new Mappers();
        var order = mappers.Map(new Order { Id = 7, FullName = "Grace Hopper", Total = 3m });
        Console.WriteLine($"31 GenerateMap pairs  -> {order.Name}, total {order.Total}");
    }
}
```

Note: the usage line is a comment inside the region on purpose — the region is quoted into prose that
introduces the call separately, and a live statement would need a `Program`-scope variable the snippet cannot
show.

- [ ] **Step 5: Add 32 — the four call styles**

`samples/DwarfMapper.Gallery/guides/32_FourWaysToCall.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// 32 — The mapper is stateless and allocation-free, so all of these are equivalent; pick by taste.

using DwarfMapper.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Gallery.Guides;

[DwarfMapper]
[GenerateMap<Order, OrderDto>]
public partial class CallStyles { }

[DocExample(32, Tier.Guides, "Four ways to call a mapper",
    Shows = "instance, generated extension method, and DI registration")]
public static class Example
{
    public static void Run()
    {
        var order = new Order { Id = 3, FullName = "Alan Turing", Total = 1m };

        // <snippet: four-ways-to-call>
        // 1. Instance — new it (it holds no state, so this is free) or inject it.
        var byInstance = new CallStyles().Map(order);

        // 2. Convenience extension method (generated by default, in DwarfMapper.Extensions).
        var byExtension = order.ToOrderDto();          // named after the target type

        // 3. Dependency injection (when Microsoft.Extensions.DependencyInjection is referenced).
        var services = new ServiceCollection();
        services.AddDwarfMappers();                    // registers every [DwarfMapper] as a singleton
        var byDi = services.BuildServiceProvider().GetRequiredService<CallStyles>().Map(order);
        // </snippet>

        Console.WriteLine(
            $"32 Four ways to call  -> {byInstance.Name} / {byExtension.Name} / {byDi.Name}");
    }
}
```

If `AddDwarfMappers()` is not resolvable, add the `using <AssemblyName>;` the generator requires — the README
notes it lives in the assembly's root namespace. `12_Ergonomics.cs` already does this; copy its using.

- [ ] **Step 6: Add 33 — explicit directives**

`samples/DwarfMapper.Gallery/guides/33_ExplicitDirectives.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// 33 — The three ways to satisfy the completeness gate for a destination member that has no obvious source.
// This is the triage a migrating reader performs on every DWARF001.

namespace DwarfMapper.Gallery.Guides;

public sealed class Src
{
    public string Existing { get; set; } = "";
}

public sealed class Dst
{
    public string Renamed { get; set; } = "";
    public string Source { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

[DwarfMapper]
public partial class DirectiveMapper
{
    // <snippet: explicit-directives>
    [MapProperty(nameof(Src.Existing), nameof(Dst.Renamed))]  // it had a differently-named source
    [MapValue(nameof(Dst.Source), "api-v2")]                  // it's a constant/computed value
    [MapIgnore(nameof(Dst.PasswordHash))]                     // dropping it is intentional and audited
    public partial Dst ToDst(Src s);
    // </snippet>
}

[DocExample(33, Tier.Guides, "Satisfying the completeness gate",
    Shows = "[MapProperty] / [MapValue] / [MapIgnore] — the three answers to DWARF001")]
public static class Example
{
    public static void Run()
    {
        var dst = new DirectiveMapper().ToDst(new Src { Existing = "kept" });
        Console.WriteLine($"33 Explicit directives-> {dst.Renamed}, {dst.Source}, hash='{dst.PasswordHash}'");
    }
}
```

- [ ] **Step 7: Add 34 — `[ReverseMap]`, `[AfterMap]`, and constructor injection**

`samples/DwarfMapper.Gallery/guides/34_ReverseMapAndHooks.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// 34 — The three features the migration guides reach for and no earlier example covers: an inverse map
// declared once, an imperative tail, and a dependency reaching a converter.

namespace DwarfMapper.Gallery.Guides;

public interface IRateService
{
    decimal Convert(decimal amount);
}

public sealed class DoubleRates : IRateService
{
    public decimal Convert(decimal amount) => amount * 2;
}

// <snippet: reverse-map>
[DwarfMapper]
public partial class ReversibleOrderMapper
{
    [ReverseMap]
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    public partial OrderDto ToDto(Order o);

    public partial Order FromDto(OrderDto d);   // inherits the inverted Name -> FullName rename
}
// </snippet>

// <snippet: ctor-injection>
[DwarfMapper]
public partial class RatedOrderMapper(IRateService rates)   // primary constructor
{
    [MapProperty(nameof(Order.Total), nameof(OrderDto.Total), Use = nameof(ToLocal))]
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapValue(nameof(OrderDto.Source), "api-v2")]
    public partial OrderDto ToDto(Order o);

    private decimal ToLocal(decimal amount) => rates.Convert(amount);
}
// </snippet>

[DwarfMapper]
public partial class StampedOrderMapper
{
    [MapProperty(nameof(Order.FullName), nameof(OrderDto.Name))]
    [MapValue(nameof(OrderDto.Source), "api-v2")]
    public partial OrderDto ToDto(Order o);

    // <snippet: after-map-hook>
    [AfterMap]                                   // imperative tail you couldn't express declaratively
    private static void Stamp(Order o, OrderDto d) => d.Source = $"api-v2/{o.Id}";
    // </snippet>
}

[DocExample(34, Tier.Guides, "Inverse maps, hooks, and injected dependencies",
    Shows = "[ReverseMap], [AfterMap], and a primary-constructor dependency in a Use= converter")]
public static class Example
{
    public static void Run()
    {
        var round = new ReversibleOrderMapper();
        var back = round.FromDto(round.ToDto(new Order { Id = 1, FullName = "Ada", Total = 5m }));

        var rated = new RatedOrderMapper(new DoubleRates())
            .ToDto(new Order { Id = 2, FullName = "Grace", Total = 21m });

        var stamped = new StampedOrderMapper().ToDto(new Order { Id = 9, FullName = "Alan", Total = 1m });

        Console.WriteLine(
            $"34 Reverse/hooks/ctor -> {back.FullName}, rated {rated.Total}, stamped {stamped.Source}");
    }
}
```

`[AfterMap]` on a value-type target must take `ref` (DWARF023); `OrderDto` is a class, so the plain signature
above is correct.

- [ ] **Step 8: Add 35 — the ambient facade**

`samples/DwarfMapper.Gallery/guides/35_AmbientFacade.cs`:

```csharp
// SPDX-License-Identifier: GPL-2.0-only

// 35 — Injecting IDwarfMapper when the caller cannot name the concrete mapper (cross-assembly, or a service
// that maps several pairs). The facade does a Type-keyed dictionary lookup — no member reflection.

using Microsoft.Extensions.DependencyInjection;

namespace DwarfMapper.Gallery.Guides;

[DwarfMapper]
[GenerateMap<Customer, CustomerDto>]
public partial class AmbientMappers { }

// <snippet: ambient-facade>
public sealed class SettingsService(IDwarfMapper mapper)
{
    public CustomerDto Load(Customer doc) => mapper.Map<CustomerDto>(doc);
}
// </snippet>

[DocExample(35, Tier.Guides, "The ambient IDwarfMapper facade",
    Shows = "mapping when the caller cannot name the concrete mapper type")]
public static class Example
{
    public static void Run()
    {
        var services = new ServiceCollection();
        services.AddDwarfMappers();
        var provider = services.BuildServiceProvider();

        var dto = new SettingsService(provider.GetRequiredService<IDwarfMapper>())
            .Load(new Customer
            {
                Id = 4, FullName = "Edsger Dijkstra", Total = 2m,
                Address = new Address { City = "Rotterdam", Zip = "3011" }
            });

        Console.WriteLine($"35 Ambient facade     -> {dto.Name}, {dto.City}");
    }
}
```

If `AddDwarfMappers()` does not register `IDwarfMapper`, consult `docs/howto/ambient-cross-assembly-maps.md`
for the registration this facade needs and follow it — do not weaken the example to whatever compiles.

- [ ] **Step 9: Build, run, and confirm the new examples appear in the index**

```bash
dotnet build DwarfMapper.NET.sln -c Release
dotnet run --project samples/DwarfMapper.Gallery -c Release
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
git diff samples/DwarfMapper.Gallery/README.md
```

Expected: build clean; the Gallery prints 21 examples ending with a `Guides` tier of six; the currency test
FAILS once (rewriting the index table to add the `Guides` group) and PASSES on a re-run.

A `DWARF001` here means a fixture has a destination member no directive covers — that is the library working
as designed. Fix the example, not the fixture, unless the fixture genuinely lacks the member.

- [ ] **Step 10: Re-run and confirm the orphan rule is the only red**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests"
```

Expected: `No_snippet_region_is_orphaned` FAILS listing the nine new ids plus the fifteen from Task 7 — 24
regions no document quotes yet. Tasks 9 and 10 clear them.

- [ ] **Step 11: Commit**

```bash
git add samples/DwarfMapper.Gallery
git commit -s -m "feat(samples): composite guide examples in the docs' vocabulary

The docs and the Gallery did not speak the same language. Every fence in the
README and the guides is written in a Customer/Order/Src/Dst vocabulary, and
most are composites — one mapper doing a rename AND a Use= conversion AND a
[MapValue] AND an [AfterMap]. The Gallery uses dwarf-themed fixtures and shows
one feature per example, so for most fences there was nothing to extract from.

Six composite examples close that gap, and close a real one in the corpus: the
Gallery had no example of a realistic mapper doing several things at once, which
is the shape a migrating reader actually arrives with. [ReverseMap], [AfterMap],
constructor-injected converters and the ambient IDwarfMapper facade had no
example at all.

Ordinals 30-35, leaving 16-26 for the phase-3 tiers."
```

---

## Task 9: Convert the README's C# fences

**Files:**
- Modify: `README.md`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs`

**Interfaces:**
- Consumes: region ids from Tasks 7 and 8; the `Unconverted` allowlist from Task 6.
- Produces: nothing consumed later.

**Per-fence decisions.** All 15, no discretion left to the implementer. Line numbers are approximate and
shift as you edit — work **bottom-up** so earlier line numbers stay valid.

| ~Line | Shows | Action |
|---|---|---|
| 135 | quick start, `[MapTo]` + top-level program | **exempt** — `<!-- fence-exempt: [MapTo] registry sample arrives in phase 3 (#16) -->` |
| 177 | composite: rename + `Use=` + `[Flatten]` | **back** with `composite-mapper` |
| 205 | two `[GenerateMap]` on one class | **back** with `generate-map-pairs` |
| 225 | `[MapTo]` stacked per-target directives | **exempt** — `<!-- fence-exempt: [MapTo] registry sample arrives in phase 3 (#16) -->` |
| 257 | instance / extension / DI | **back** with `four-ways-to-call` |
| 308 | `CaseInsensitive` + explicit rename | **back** with `case-insensitive-rename` |
| 341 | nested auto-wiring + `Use=` | **exempt** — `<!-- fence-exempt: shows two overloads on one class purely to contrast them; no single sample shape -->` |
| 386 | `[MapValue]` constant and computed | **back** with `explicit-directives` |
| 501 | `record` target, ctor binding | **back** with `record-target` (Gallery 10) |
| 563 | update-into | **exempt** — `<!-- fence-exempt: update-into sample arrives in phase 3 (#17) -->` |
| 583 | span map | **exempt** — `<!-- fence-exempt: span-map sample arrives in phase 3 (#18) -->` |
| 598 | async streaming | **exempt** — `<!-- fence-exempt: async-stream sample arrives in phase 3 (#19) -->` |
| 611 | reference handling / cycles | **exempt** — `<!-- fence-exempt: cycles sample arrives in phase 3 (#20) -->` |
| 658 | `[RoundTrip]` | **exempt** — `<!-- fence-exempt: [RoundTrip] sample arrives in phase 3 (#25) -->` |
| 684 | `RoundTrip.Verify` | **exempt** — `<!-- fence-exempt: informed-dump sample arrives in phase 3 (#26) -->` |

Six backed, nine exempt — and eight of those nine name the phase-3 example that clears them. That is the
honest state: those fences describe features with no sample, and phase 3 is where the spec supplies them.
Do **not** invent a sample here to force a conversion.

- [ ] **Step 1: Convert the fence at ~line 501 first**

Working bottom-up, but starting with the simplest backed case to prove the loop. Replace the fence and its
body with:

```markdown
<!-- snippet: record-target -->
<!-- endsnippet -->
```

Then:

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
git diff README.md
```

Expected: FAILS, writes the body from `10_RecordTarget.cs`, and the diff shows the real sample. **Read that
diff as a reader would.** If the sample's types make the surrounding prose read wrong, widen the region in
the sample — the sample is the truth, so the fix belongs there, never in the injected text.

- [ ] **Step 2: Apply the nine exemptions**

Insert the exemption comment from the table on its own line immediately above each of the nine fences,
bottom-up (684, 658, 611, 598, 583, 563, 341, 225, 135):

```markdown
<!-- fence-exempt: span-map sample arrives in phase 3 (#18) -->
```

```csharp
[DwarfMapper]
public partial class M { public partial void Map(ReadOnlySpan<int> src, Span<long> dst); }
```

Leave the fence body exactly as it is.

- [ ] **Step 3: Convert the remaining five backed fences**

Bottom-up: 386 → `explicit-directives`, 308 → `case-insensitive-rename`, 257 → `four-ways-to-call`,
205 → `generate-map-pairs`, 177 → `composite-mapper`. Replace each fence and its body with the marker pair,
then run the currency test and read the diff:

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
git diff README.md
```

- [ ] **Step 4: Remove README from the ratchet allowlist**

In `DocFenceScanTests.cs`, delete the `["README.md"] = "converted in Task 9",` entry.

- [ ] **Step 5: Verify both ratchet tests pass**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocFenceScanTests"
```

Expected: both PASS. A failure in the first names a fence you neither backed nor exempted — the table above
covers all 15, so a hit means one was missed. A failure in the second means the entry removal in step 4 was
premature.

- [ ] **Step 6: Verify the README's existing self-check still holds**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~ConformanceArtifactScanTests"
```

Expected: PASS. That test asserts the README's advertised runtime-assertion figure matches the conformance
sample; editing the README must not disturb it.

- [ ] **Step 7: Commit**

```bash
git add README.md tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs
git commit -s -m "docs: README code comes from the compiled samples

Six fences are now extracts of code that builds and runs in the Gallery, so
they cannot describe an API that no longer exists.

Nine stay hand-written, and eight of those name the phase-3 example that will
clear them: [MapTo], update-into, span, async-stream, cycles, [RoundTrip] and
informed dumps have no sample yet. An exemption that names its follow-up is a
recorded gap; one with no reason is a bypass, which the ratchet refuses.

README leaves the allowlist: no unmarked C# can be added to it now."
```

---

## Task 10: Convert the how-to guides

**Files:**
- Modify: `docs/howto/ambient-cross-assembly-maps.md`, `common-changes.md`, `migrate-from-automapper.md`, `migrate-from-handwritten.md`, `migrate-from-mapster.md`
- Modify: `tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs`
- Modify: `CONTRIBUTING.md`

**Interfaces:**
- Consumes: region ids from Tasks 7 and 8; the `Unconverted` allowlist from Task 6.
- Produces: nothing.

**Per-fence decisions.** All 12. `migrate-from-mapperly.md` has no `csharp` fence and needs no edit.

| File | ~Line | Shows | Action |
|---|---|---|---|
| `common-changes.md` | 55 | `[GenerateMap]` bulk **and** a named partial method | **back** with `generate-map-pairs` |
| `common-changes.md` | 100 | instance / extension / DI | **back** with `four-ways-to-call` |
| `common-changes.md` | 137 | `[MapProperty]`/`[MapValue]`/`[MapIgnore]` triage | **back** with `explicit-directives` |
| `common-changes.md` | 168 | `[RoundTrip]` | **exempt** — `<!-- fence-exempt: [RoundTrip] sample arrives in phase 3 (#25) -->` |
| `common-changes.md` | 183 | `RoundTrip.Verify` | **exempt** — `<!-- fence-exempt: informed-dump sample arrives in phase 3 (#26) -->` |
| `migrate-from-automapper.md` | 66 | a single rename | **back** with `composite-mapper` |
| `migrate-from-automapper.md` | 113 | rename + `Use=` | **back** with `composite-mapper` |
| `migrate-from-automapper.md` | 141 | primary-constructor dependency | **back** with `ctor-injection` |
| `migrate-from-automapper.md` | 198 | `[ReverseMap]` | **back** with `reverse-map` |
| `migrate-from-handwritten.md` | 75 | rename + `Use=` + `[MapValue]` + `[AfterMap]` | **back** with `after-map-hook` |
| `migrate-from-mapster.md` | 78 | `Use=` + `[MapIgnore]` | **back** with `explicit-directives` |
| `ambient-cross-assembly-maps.md` | 13 | `IDwarfMapper` injection | **back** with `ambient-facade` |

Ten backed, two exempt. Two fences reference the same region (`composite-mapper` at automapper:66 and 113,
`explicit-directives` at common-changes:137 and mapster:78) — that is allowed and correct. A region may be
quoted by many documents; the rule is that a *marker* resolves to exactly one region, not the reverse.

The 14 `diff` fences need **no** marker: the ratchet scans `csharp` only, because competitor "before" code
cannot compile in this repository.

- [ ] **Step 1: Convert `ambient-cross-assembly-maps.md`**

Its single fence becomes:

```markdown
<!-- snippet: ambient-facade -->
<!-- endsnippet -->
```

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
git diff docs/howto/ambient-cross-assembly-maps.md
```

Expected: FAILS, writes the body, the diff shows `SettingsService` from example 35. The guide's prose names
`BotSettings`/`UserSettingsDocument` while the sample uses `Customer`/`CustomerDto` — **adjust the prose** to
the sample's names in the same commit. The sample is the truth; prose bends to it.

- [ ] **Step 2: Convert the remaining nine backed fences, one file at a time**

Work bottom-up within each file. After each file:

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocsAreSnippetCurrentTests"
git diff docs/howto/
```

Where the injected snippet names types the prose does not, reconcile the prose. Where the snippet shows more
than the prose needs, narrow the region in the sample and re-run — do not hand-edit injected text, because
the next test run overwrites it.

- [ ] **Step 3: Apply the two exemptions**

`common-changes.md` fences at ~168 and ~183 get the exemption comments from the table, immediately above each
fence, bodies unchanged.

- [ ] **Step 4: Empty the allowlist except the phase-4 entry**

In `DocFenceScanTests.cs`, remove all five `docs/howto/...` entries. `docs/diagnostics.md` stays with its
existing reason.

- [ ] **Step 5: Verify the orphan rule finally passes**

```bash
dotnet test tests/DwarfMapper.Generator.Tests/DwarfMapper.Generator.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DocReconciliationTests|FullyQualifiedName~DocFenceScanTests"
```

Expected: all PASS, including `No_snippet_region_is_orphaned`, red since Task 7.

If a region is still orphaned, it is one the tables above never referenced. Do not delete it to go green —
check the tables first; a Task 7 or 8 region with no consumer means a fence decision was missed.

- [ ] **Step 6: Add the fourth ground rule**

In `CONTRIBUTING.md`, extend the "Ground rules" list:

```markdown
- User-facing mapping behavior also needs a **documentation snippet**: an example under `samples/`
  carrying a `// <snippet: id>` region, referenced from a `<!-- snippet: id -->` marker in the prose that
  describes it. Hand-written ```csharp fences are refused by `DocFenceScanTests` — code in the docs is an
  extract of code that compiles, so it cannot describe an API that no longer exists.
```

- [ ] **Step 7: Full verification**

```bash
dotnet build DwarfMapper.NET.sln -c Release
dotnet test DwarfMapper.NET.sln -c Release
dotnet run --project samples/DwarfMapper.Gallery -c Release
git status --short
```

Expected: `0 Warning(s)`; every test passes; the Gallery prints 21 examples; `git status` shows only files you
intend to commit. A markdown file modified by the test run means something was still stale — commit that
regeneration too.

- [ ] **Step 8: Commit**

```bash
git add docs/howto CONTRIBUTING.md \
        tests/DwarfMapper.Generator.Tests/SelfValidation/DocFenceScanTests.cs
git commit -s -m "docs: how-to guide code comes from the compiled samples

The migration guides are what convert users, and they carried the oldest
uncompiled code in the repository. Ten of their twelve csharp fences are now
extracts; the two [RoundTrip] ones name the phase-3 sample that will back them.

Prose was adjusted to the samples' type names where the two disagreed. The
sample is the truth — bending the prose keeps one vocabulary, and bending the
snippet would just be a hand-written fence with extra steps.

Only docs/diagnostics.md remains on the ratchet allowlist, until phase 4 opens
that file to inject its tables.

CONTRIBUTING gains a fourth ground rule: user-facing behaviour needs a doc
snippet, not only a snapshot test and an integration test."
```

## Done when

- `dotnet build DwarfMapper.NET.sln -c Release` → `0 Warning(s)`, `0 Error(s)`.
- `dotnet test DwarfMapper.NET.sln -c Release` → all pass, nothing skipped.
- `dotnet run --project samples/DwarfMapper.Gallery -c Release` → 21 examples in tier order (15 existing + 6 guide composites), no hand-written call list.
- `git status --short` clean after a full test run — heal-or-fail tests pass or fail, never leaving a rewritten tracked file behind as the side effect of a green run.
- Deleting a Gallery example's file makes the build fail via a marker that no longer resolves. Verify once by hand: `git mv samples/DwarfMapper.Gallery/06_DeepPaths.cs /tmp/ && dotnet test … ; git mv /tmp/06_DeepPaths.cs samples/DwarfMapper.Gallery/`.
- `DocFenceScanTests.Unconverted` holds exactly one entry: `docs/diagnostics.md`, for phase 4.
- `docs/generated/*.md` are byte-identical to their pre-phase-1 checksums.
- All 32 C# fences are accounted for: **16 snippet-backed** (6 README + 10 guides), **11 exempt with a reason**
  (9 README + 2 guides, of which 10 name the phase-3 example that will clear them), and **5 in
  `docs/diagnostics.md`** still on the allowlist until phase 4 opens that file. Phase 2 does **not** end with
  every fence backed — it ends with every fence *accounted for*, and no way to add an unaccounted one.
