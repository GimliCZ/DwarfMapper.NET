# [RFC] DwarfMapper.NET — regression & architecture tests, with proposed code

Kernel-RFC convention applies: the code below is **proposed and not yet compiled against the tree** — it is
written in the house idioms (raw-string sources, `GeneratorTestHarness`, SelfValidation scan style, the
round-13 torture patterns) and is expected to need only mechanical adjustment. Each entry: rationale recap →
proposed test code → proposed fix (where the test implies one) → what must turn it red.

The six landing-order entries are expanded in full; the remaining eight keep their short form from v1 with
patches available on request.

---

## [REG-01] contracts: endpoint-parity matrix — PROPOSED CODE

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// Contracts/OptionEndpointParityMatrixTests.cs
//
// The most repeated defect class of the audit history (ISSUE-044, ISSUE-046, 9146d99, 763a201) is an option
// honoured at one endpoint and silently ignored at another. This matrix asserts, for every class-level
// option and every endpoint, that the Honoured cell produces OBSERVABLY DIFFERENT output between the
// option's two values — and that every silent cell is declared in OptionGaps.KnownSilent, both directions.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DwarfMapper.Generator.Tests.Contracts;

public sealed class OptionEndpointParityMatrixTests
{
    public enum Endpoint { Map, Update, Project, CollectionElement, Registry }

    /// <summary>One option under test: how to write it, and a source model that makes it bite.</summary>
    private sealed record OptionCase(
        string Name,
        string AttrOn,               // e.g. "AutoMatchMembers = false"
        string AttrOff,              // e.g. "AutoMatchMembers = true"
        string Model);               // types whose mapping must differ between On and Off

    private static readonly string EndpointMethods = """
            public partial D Map(S s);
            public partial void Update(S s, D d);
            public partial System.Linq.IQueryable<D> Project(System.Linq.IQueryable<S> q);
            public partial System.Collections.Generic.List<D> MapAll(System.Collections.Generic.List<S> s);
        """;

    private static readonly OptionCase[] Options =
    {
        new("AutoMatchMembers",
            "AutoMatchMembers = false", "AutoMatchMembers = true",
            """
            public class S { public int Id { get; set; } public int Extra { get; set; } }
            public class D { public int Id { get; set; } public int Extra { get; set; } }
            """),
        new("SkipNullSourceMembers",
            "SkipNullSourceMembers = true", "SkipNullSourceMembers = false",
            """
            #nullable enable
            public class S { public string? Name { get; set; } }
            public class D { public string? Name { get; set; } }
            """),
        // … one row per class-level option; adding an option without a row fails REG-01b below.
    };

    public static IEnumerable<object[]> Cells()
        => from o in Options from e in Enum.GetValues<Endpoint>() select new object[] { o, e };

    [Theory]
    [MemberData(nameof(Cells))]
    public void Every_cell_is_honoured_or_declared(OptionCase o, Endpoint e)
    {
        string Src(string attr) => $$"""
            using DwarfMapper;
            namespace T;
            {{o.Model}}
            [DwarfMapper({{attr}})]
            public partial class M
            {
            {{EndpointMethods}}
            }
            """;

        var on  = GeneratorTestHarness.Run(Src(o.AttrOn)).Item2;
        var off = GeneratorTestHarness.Run(Src(o.AttrOff)).Item2;
        var slice = EndpointSlice(e);                       // the emitted method body for this endpoint
        var differs = !string.Equals(Slice(on, slice), Slice(off, slice), StringComparison.Ordinal);

        var key = $"{o.Name}@{e}";
        if (OptionGaps.KnownSilent.ContainsKey(key))
        {
            Assert.False(differs,
                $"{key} is declared SILENT in OptionGaps but the outputs now differ — the gap closed; "
                + "delete the KnownSilent row so the contract records reality.");
        }
        else
        {
            Assert.True(differs,
                $"{key}: option had NO observable effect at this endpoint and no KnownSilent row declares "
                + "that. Either wire the option through (the ISSUE-044 pattern) or declare the gap.");
        }
    }

    [Fact]
    public void Every_class_level_option_has_a_matrix_row() // REG-01b — the matrix cannot go stale
    {
        var declared = typeof(DwarfMapperAttribute).GetProperties().Select(p => p.Name).ToHashSet();
        var covered  = Options.Select(o => o.Name).ToHashSet();
        var missing  = declared.Except(covered).Except(MatrixExemptions).ToList();
        Assert.True(missing.Count == 0,
            "New class-level options without a parity row: " + string.Join(", ", missing));
    }

    private static readonly string[] MatrixExemptions = { /* non-behavioural props, reviewed */ };
    private static string Slice(string gen, (string start, string end) s) { /* substring helper */ return gen; }
    private static (string, string) EndpointSlice(Endpoint e) => e switch { _ => ("", "") };
}
```

**Fix implied**: none in src today — the matrix *is* the fix for tomorrow. When a cell fails, the remedy is
either threading (044-style) or an explicit `KnownSilent` row with rationale, exactly the `fed98c4` pattern.
**Red when**: a new option lands wired to fewer than all endpoints; a documented gap silently closes; a new
option ships with no row at all (REG-01b).

---

## [REG-02] pipeline: optional-parameter ban — PROPOSED CODE + FIX PATTERN

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// SelfValidation/ResolverParameterDisciplineTests.cs
//
// ISSUE-043 and ISSUE-044 were both "optional parameter defaults to the permissive value; one call site
// forgets it". The fix made those specific parameters required; this scan makes the CLASS unreintroducible.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DwarfMapper.Generator.Tests.SelfValidation;

public sealed class ResolverParameterDisciplineTests
{
    // The option vocabulary whose defaults have bitten before, plus context objects that gate behaviour.
    private static readonly string[] ForbiddenOptionalNames =
        { "autoNest", "allowNonPublic", "explicitOnly", "ignoreObsolete", "caseInsensitive",
          "implicitConversions", "nullAsNull", "compilation" };

    // Reviewed exceptions: (file, method, parameter) triples. Empty today; additions require a comment.
    private static readonly HashSet<string> Allowlist = new() { };

    [Fact]
    public void No_pipeline_resolver_declares_a_permissive_optional_option_parameter()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.PipelineDir, "*.cs"))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            foreach (var p in m.ParameterList.Parameters)
            {
                if (p.Default is null) continue;
                var name = p.Identifier.Text;
                if (!ForbiddenOptionalNames.Contains(name)) continue;
                var key = $"{Path.GetFileName(file)}::{m.Identifier.Text}::{name}";
                if (!Allowlist.Contains(key)) offenders.Add($"{key} = {p.Default.Value}");
            }
        }
        Assert.True(offenders.Count == 0,
            "Optional option-parameters are banned in Pipeline/ (ISSUE-043/044 class). Make them required "
            + "so the compiler enforces threading at every call site:\n  " + string.Join("\n  ", offenders));
    }
}
```

**Fix implied when red**: delete the default (`bool autoNest = true` → `bool autoNest`) and let CS7036 list
every call site that must now decide explicitly — the compiler does the threading audit that rounds 8–9 did
by hand.
**Red when**: the hazard pattern reappears anywhere under `Pipeline/`.

---

## [ARCH-02] emission hygiene: permanent scans — PROPOSED CODE

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// SelfValidation/EmittedLiteralHygieneTests.cs
//
// Round-16 measured these clean once (0 / 0 / 0). Measurement decays; this test does not.

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DwarfMapper.Generator.Tests.SelfValidation;

public sealed class EmittedLiteralHygieneTests
{
    private static readonly Regex EmittedLiteral =
        new("\"\"\"(?<b>.*?)\"\"\"|@\"(?<b>(?:[^\"]|\"\")*)\"", RegexOptions.Singleline);
    private static readonly Regex Unqualified =
        new(@"(?<!global::)(?<![\w.])(System|Microsoft|DwarfMapper)\.[A-Z][\w.]*");

    [Fact]
    public void Emitted_statement_literals_qualify_every_framework_token_with_global()
    {
        var offenders =
            from file in Directory.EnumerateFiles(RepoPaths.GeneratorSrcDir, "*.cs", SearchOption.AllDirectories)
            let text = File.ReadAllText(file)
            from m in EmittedLiteral.Matches(text).Cast<Match>()
            let body = m.Groups["b"].Value
            where (body.Contains(';') || body.Contains("=>")) && body.Contains('\n')   // emitted C#, not a name
            from hit in Unqualified.Matches(body).Cast<Match>()
            let line = body[..hit.Index].Split('\n')[^1].TrimStart()
            where !line.StartsWith("//") && !line.StartsWith("using ") && !line.Contains(".g.cs")
            select $"{Path.GetFileName(file)}: {hit.Value} in \"{line[..Math.Min(60, line.Length)]}\"";
        Assert.Empty(offenders);
    }

    [Fact]
    public void No_culture_sensitive_calls_and_no_real_environment_newline()
    {
        var bad = new List<string>();
        foreach (var f in Directory.EnumerateFiles(RepoPaths.GeneratorSrcDir, "*.cs", SearchOption.AllDirectories))
            foreach (var (ln, s) in File.ReadLines(f).Select((s, i) => (i + 1, s)))
            {
                var code = s.Split("//")[0];
                if (code.Contains(".ToLower()") || code.Contains(".ToUpper()"))
                    bad.Add($"{Path.GetFileName(f)}:{ln} culture-sensitive case call");
                if (code.Contains("Environment.NewLine"))
                    bad.Add($"{Path.GetFileName(f)}:{ln} Environment.NewLine outside a comment");
            }
        Assert.Empty(bad);
    }
}
```

**Red when**: any historically-cleared emission defect class (unqualified ref, culture call, platform newline)
reappears in a new emitter or fix.

---

## [ARCH-04] consumer surface parity — PROPOSED CODE

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// SelfValidation/ConsumerSurfaceParityTests.cs
//
// Round-18 found 13 public attributes with zero presence in any consumer-shaped assembly, including the
// documented remedy for DWARF039. Same idiom as the samples-surface ratchet (36d4ae9), new audience.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DwarfMapper.Generator.Tests.SelfValidation;

public sealed class ConsumerSurfaceParityTests
{
    private static readonly string[] ConsumerRoots =
        { "tests/DwarfMapper.ConsumerTests", "tests/DwarfMapper.ConsumerTests.CleanCorpus",
          "tests/DwarfMapper.DifferentialTests", "tests/DwarfMapper.NegativeCases" };

    // Deliberate exclusions require a reason. Shrinking this list is progress; growing it is a review event.
    private static readonly Dictionary<string, string> Exclusions = new()
    {
        ["DwarfMapperValidationRoot"] = "aggregate-level plumbing; consumer story tracked in plan item …",
    };

    [Fact]
    public void Every_public_attribute_appears_in_at_least_one_consumer_assembly()
    {
        var surface = typeof(DwarfMapperAttribute).Assembly.GetExportedTypes()
            .Where(t => t.Name.EndsWith("Attribute", StringComparison.Ordinal))
            .Select(t => t.Name[..^"Attribute".Length])
            .ToHashSet(StringComparer.Ordinal);

        var corpus = string.Concat(
            ConsumerRoots.SelectMany(r => Directory.EnumerateFiles(RepoPaths.Root(r), "*.cs",
                    SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        var missing = surface
            .Where(a => !corpus.Contains("[" + a) && !Exclusions.ContainsKey(a))
            .OrderBy(a => a).ToList();

        Assert.True(missing.Count == 0,
            "Public attributes with no consumer-shaped usage (add a corpus row with a stated "
            + "MAPS/OPT-IN/REFUSES expectation, or an Exclusions entry with a reason):\n  "
            + string.Join("\n  ", missing));
    }
}
```

**Fix implied when red today**: thirteen corpus rows (or reasoned exclusions) — the round-18 hole list, with
`[BeforeMap]`, `[DwarfMapperDefaults]`, `[DwarfMapperOptions]`, `[MapIgnoreSource]`, `[FlattenGraph]` first.
**Red when**: new public surface lands generator-tested only.

---

## [REG-06] registry: update-table torture — PROPOSED CODE (delta from round-13 file)

```csharp
// Additions to RegistryConcurrencyTortureTests — same collection, same Mint<T>/RunAll infrastructure.

[Fact]
public void Same_pair_racing_UPDATE_registrations_first_wins_ambiguity_marked()
{
    var src = typeof(UpdRaceMarker); var dst = Mint<UpdRaceMarker>(0);
    var errors = RunAll(Enumerable.Range(0, Threads).Select(i => (Action)(() =>
    {
        var tag = i;
        DwarfMapperRegistry.RegisterUpdate(src, dst, (s, d) => Sink(d, tag));
    })));

    Assert.Empty(errors);
    Assert.True(DwarfMapperRegistry.IsUpdateProvided(src, dst));
    var first = ProbeUpdateTag(src, dst);
    Assert.InRange(first, 0, Threads - 1);
    Assert.Equal(first, ProbeUpdateTag(src, dst));               // winner is stable
    Assert.True(DwarfMapperRegistry.IsUpdateAmbiguous(src, dst)); // NOTE: requires the API below
}

[Fact]
public void Update_and_map_tables_do_not_alias()
{
    var src = typeof(AliasMarker); var dst = Mint<AliasMarker>(0);
    DwarfMapperRegistry.Register(src, dst, _ => "map");
    Assert.False(DwarfMapperRegistry.IsUpdateProvided(src, dst),
        "a Register on the map table must not surface as an update registration");
}
```

**Fix implied**: the contract test exposes an API asymmetry — `IsAmbiguous` exists for the map table but the
update table has no ambiguity accessor. Proposed src fix (small): mirror the map table exactly —

```csharp
// DwarfMapperRegistry.cs — proposed
private static readonly ConcurrentDictionary<Key, byte> UpdateAmbiguous = new();

public static void RegisterUpdate(Type source, Type destination, Action<object, object> map)
{
    var key = new Key(source, destination);
    if (!UpdateMaps.TryAdd(key, map))
        UpdateAmbiguous.TryAdd(key, 1);          // today: silent last-loses with no mark — the exact
}                                                 // asymmetry the map table already solved

public static bool IsUpdateAmbiguous(Type source, Type destination)
    => UpdateAmbiguous.ContainsKey(new Key(source, destination));
```

**Red when**: update-table atomicity, precedence, or ambiguity semantics diverge from the read table.

---

## [REG-05] diagnostics: no id ships unpinned — PROPOSED CODE (delta)

```csharp
// Extension to DiagnosticMessageContractTests: completeness in the OTHER direction.
[Fact]
public void Every_live_descriptor_has_a_remedy_contract_row()
{
    var live = AllDescriptors().Select(d => d.Id).Except(RetiredIds).ToHashSet();
    var pinned = RemedyContracts.Select(r => r.Id).ToHashSet();
    var unpinned = live.Except(pinned).OrderBy(x => x).ToList();
    Assert.True(unpinned.Count == 0,
        "Diagnostics shipping without a wording pin (add a RemedyContracts row asserting id AND remedy "
        + "phrase): " + string.Join(", ", unpinned));
}
```

**Red when**: the next DWARF08x lands with an id-only assertion.

---

Entries [REG-03/04/07/08] and [ARCH-01/03/05/06] retain their v1 short forms; each has the same
expansion available on request — REG-07 is CI YAML rather than C# (the `autocrlf=true` clone job from the
round-16 experiment, verbatim), and ARCH-06's fix half is the `CiEnvironment` extraction ISSUE-036 already
specifies.

RFC status recap: none of the above has been compiled against `20e032c`; `RepoPaths`, endpoint slicing, and
the update-tag probe are the three deliberately-sketched seams where the tree's real helpers should be
substituted. The contracts, invariants, and red-conditions are the reviewed content.
