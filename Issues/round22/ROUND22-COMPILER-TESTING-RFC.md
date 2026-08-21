# [RFC] DwarfMapper.NET — Round 22: compiler-grade testing adoption
# Subject: convert the compiler-testing assessment into filable work items with proposed code

Status: RFC — code below is written in the house idioms (SPDX headers, `GeneratorTestHarness`, raw-string
sources, CsCheck 4.7.0 which is already a pinned dependency, SelfValidation scan style) but has **not been
compiled against the current tip**; the repo has advanced past my last audited commit (the surface-matrix
changelog era). Seams expected to need mechanical substitution are marked `// SEAM:`. Entry order = the
research report's staged priority (differential oracle → metamorphic → gates → fuzzing), with the shared
type-graph infrastructure first because every later entry consumes it.

Numbering follows the maintainer's round count (22). Format per entry:
Where / Why (evidence) / Proposed code / Fix implied / Red-when.

---

## [R22-00] infra: type-graph descriptor + renderer (shared foundation)

Where: new `tests/DwarfMapper.CompilerTests/TypeGraphs/` (own project so SharpFuzz can instrument it later
       without touching the main test assemblies).
Why:   Every compiler-testing technique below consumes "a random-but-valid pair of C# type declarations".
       Csmith's core lesson applies verbatim: the generator's whole value is that its output is *valid*
       (their case: UB-free C; ours: compiling C# type graphs) — otherwise fuzz findings drown in
       invalid-input noise. The surface matrix already enumerates the dimensions by hand (feature ×
       endpoint × kind); this makes the same dimensions a generator.

Proposed code:

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// TypeGraphs/TypeGraph.cs — descriptor model. Pure data: transforms and shrinking stay trivial.

using System.Collections.Generic;

namespace DwarfMapper.CompilerTests.TypeGraphs;

public enum TypeKind { Class, Record, Struct, RecordStruct }
public enum MemberShape { AutoProp, InitOnly, Required, CtorParam, Field }
public enum CollShape { None, Array, List, IReadOnlyList, Dictionary, HashSet }

public sealed record MemberSpec(
    string Name, string ScalarType, bool Nullable, MemberShape Shape, CollShape Coll,
    string? NestedRef);                      // name of another NodeSpec => nested mapping edge

public sealed record NodeSpec(
    string Name, TypeKind Kind, IReadOnlyList<MemberSpec> Members,
    string? BaseRef);                        // inheritance edge (classes/records only)

public sealed record GraphSpec(IReadOnlyList<NodeSpec> Nodes, string RootSource, string RootDest);
```

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// TypeGraphs/TypeGraphGen.cs — CsCheck generators. Seed replay + shrinking come from CsCheck itself,
// which is the same property the [RoundTrip] harness already gives consumers.

using CsCheck;
using System.Linq;

namespace DwarfMapper.CompilerTests.TypeGraphs;

public static class TypeGraphGen
{
    private static readonly Gen<string> Scalar =
        Gen.OneOfConst("int", "long", "string", "decimal", "bool", "System.Guid",
                       "System.DateTimeOffset", "byte", "double");

    private static readonly Gen<MemberSpec> Member =
        Gen.Select(Gen.Int[0, 9999], Scalar, Gen.Bool, Gen.Enum<MemberShape>(), Gen.Enum<CollShape>(),
            (n, t, nul, shape, coll) =>
                new MemberSpec($"M{n}", t, nul, shape, coll, NestedRef: null));

    /// <summary>Mirrored pair: dest graph is the source graph with kinds independently re-rolled —
    /// the exact cell family the surface-matrix bugs (A11-F1/F2: [MapTo]×struct) came from.</summary>
    public static Gen<GraphSpec> MirroredPair(int maxNodes = 4, int maxMembers = 6) =>
        from n in Gen.Int[1, maxNodes]
        from nodes in Member.Array[1, maxMembers]
            .SelectMany(ms => Gen.Enum<TypeKind>().Select(k => (k, ms))).Array[n, n]
        select Assemble(nodes);

    private static GraphSpec Assemble(((TypeKind k, MemberSpec[] ms))[] raw)
    {
        // SEAM: wire NestedRef edges acyclically (index i may reference j > i only), dedupe member
        // names, forbid required+CtorParam on the same member, forbid BaseRef on structs — the
        // validity rules ARE the grammar; every rule here is one false-positive class removed.
        var nodes = raw.Select((r, i) => new NodeSpec($"S{i}", r.k, r.ms, BaseRef: null)).ToList();
        var dest  = nodes.Select(s => s with { Name = "D" + s.Name[1..] }).ToList();
        return new GraphSpec(nodes.Concat(dest).ToList(), nodes[0].Name, dest[0].Name);
    }
}
```

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// TypeGraphs/TypeGraphRenderer.cs — render a GraphSpec to compilable C# + the mapper declaration.

public static class TypeGraphRenderer
{
    public static string Render(GraphSpec g)
    {
        var sb = new System.Text.StringBuilder("using DwarfMapper;\nnamespace T;\n");
        foreach (var n in g.Nodes) RenderNode(sb, n);          // SEAM: kind/shape/coll emission table
        sb.Append($$"""
            [DwarfMapper]
            public partial class M
            {
                public partial {{g.RootDest}} Map({{g.RootSource}} s);
            }
            """);
        return sb.ToString();
    }
}
```

Red-when: n/a (infrastructure) — but add the scan-test companion: the descriptor's `TypeKind`/`MemberShape`
enums must cover every kind the surface matrix enumerates, ratcheted, so the generator's blind spots are
declared rather than silent (the YARPGen "generator bias caps yield" lesson as a test).

---

## [R22-01] differential: reflection-oracle over generated graphs (the Csmith move)

Where: `tests/DwarfMapper.CompilerTests/DifferentialOracleTests.cs`.
Why:   McKeeman's differential principle solves the oracle problem: two independent implementations, any
       disagreement is a bug, nobody needs to know the "right" answer. The existing DifferentialTests
       compare against Mapperly/AutoMapper on *hand-picked* shapes; this entry makes the reference
       implementation trivial (reflection copier) and the shapes *generated*, which is where Csmith/YARPGen
       got their ~325/~220 compiler bugs. The [MapTo]×struct bug (`if (source is null)` emitted for a
       value type) would have fallen out of the must-compile leg on the first struct-kind roll.

Proposed code:

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using CsCheck;
using System;
using System.Linq;
using Xunit;
using DwarfMapper.CompilerTests.TypeGraphs;

namespace DwarfMapper.CompilerTests;

public sealed class DifferentialOracleTests
{
    [Fact]
    public void Generated_graphs_compile_and_agree_with_the_reflection_oracle()
    {
        TypeGraphGen.MirroredPair().Sample(g =>
        {
            var src = TypeGraphRenderer.Render(g);

            // Leg 1 — the compiler invariant: generated code MUST compile or refuse loudly.
            var diags  = GeneratorTestHarness.Run(src).Item1;                    // SEAM: harness ref
            var errors = GeneratorTestHarness.RunAndGetCompilationErrors(src);
            var refused = diags.Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            if (refused) return;            // loud refusal is a VALID outcome; silence + CS errors is not
            Assert.True(errors.Count == 0,
                $"seed-replayable miscompilation: generator was silent but output has "
                + $"[{string.Join(",", errors.Select(e => e.Id).Distinct())}]\n{src}");

            // Leg 2 — the oracle: DwarfMapper's map vs a trivially-correct reflective copy.
            var (asm, _) = GeneratorTestHarness.EmitAssembly(src);
            var instance = ReflectionOracle.Populate(asm!, "T." + g.RootSource, seedFromCsCheck: true);
            var actual   = GeneratorTestHarness.InvokeMap(asm!, "T.M", "Map", instance); // SEAM
            var expected = ReflectionOracle.Map(instance, asm!.GetType("T." + g.RootDest)!);
            var diff     = GraphDeepCompare.Diff(expected, actual);   // member-path diff, [RoundTrip] style
            Assert.True(diff.Count == 0, "oracle disagreement at: " + string.Join("; ", diff));
        }, iter: 1_000);                    // nightly leg raises to 10_000
    }
}
```

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// ReflectionOracle.cs — deliberately naive: same-name public members, recursive on NestedRef types,
// element-wise on collections. ~60 lines. Its ONLY virtue is being obviously correct; keep it stupid.
// Where DwarfMapper's *documented* semantics diverge from naive copy (NullCollections, MapNullSkip),
// the oracle consults the same option — one switch per documented option, no cleverness.
```

Fix implied: none in src today; every disagreement is either a generator bug (file it, minimize via
CsCheck shrink, pin as a corpus row) or an *undocumented semantic* (document it, teach the oracle — which
is itself a spec-completeness ratchet).
Red-when: any silent miscompilation into invalid C#; any silent semantic disagreement with the naive copy
not covered by a documented option.

---

## [R22-02] metamorphic: formal relations over the same corpus (the EMI move)

Where: `tests/DwarfMapper.CompilerTests/MetamorphicTests.cs`.
Why:   EMI's insight (Le/Afshari/Su, PLDI 2014, 147 GCC/LLVM bugs in 11 months): assert *relations between
       outputs* instead of absolute outputs — oracle-free. The mapper's relations are sharper than a C
       compiler's because the semantics are simpler: reordering members, adding unmapped members, and
       representation changes must be invisible. These directly target the two most plausible mapper
       failure modes — ordering assumptions and cross-member coupling.

Proposed code:

```csharp
// SPDX-License-Identifier: GPL-2.0-only
using CsCheck; using System.Linq; using Xunit;
using DwarfMapper.CompilerTests.TypeGraphs;

namespace DwarfMapper.CompilerTests;

public sealed class MetamorphicTests
{
    private static string MapResultFingerprint(GraphSpec g)
    {
        var src = TypeGraphRenderer.Render(g);
        var (asm, errs) = GeneratorTestHarness.EmitAssembly(src);
        if (asm is null) return "REFUSED:" + string.Join(",", errs.Select(e => e.Id).Distinct());
        var s = ReflectionOracle.Populate(asm, "T." + g.RootSource, seedFromCsCheck: true);
        var d = GeneratorTestHarness.InvokeMap(asm, "T.M", "Map", s);
        return GraphDeepCompare.Fingerprint(d);     // stable member-path/value hash — order-independent
    }

    [Fact] // MR-1: member declaration order is semantics-free (the round-10 determinism claim, runtime leg)
    public void Reordering_members_preserves_the_mapping() =>
        TypeGraphGen.MirroredPair().Sample(g =>
        {
            var shuffled = g with { Nodes = g.Nodes.Select(n =>
                n with { Members = n.Members.Reverse().ToList() }).ToList() };
            Assert.Equal(MapResultFingerprint(g), MapResultFingerprint(shuffled));
        }, iter: 500);

    [Fact] // MR-2: adding an unmapped/ignored member is behaviour-neutral (EMI dead-code insertion)
    public void Adding_an_ignored_member_changes_nothing_else() =>
        TypeGraphGen.MirroredPair().Sample(g =>
        {
            var fat = InjectUnmappedMember(g);      // SEAM: extra source member absent from every dest
            Assert.Equal(MapResultFingerprint(g), MapResultFingerprint(fat));
        }, iter: 500);

    [Fact] // MR-3: representation invariance — the [MapTo]×struct bug class, generalized
    public void Class_record_recordstruct_shapes_map_identically() =>
        TypeGraphGen.MirroredPair(maxNodes: 2).Sample(g =>
        {
            var results = new[] { TypeKind.Class, TypeKind.Record, TypeKind.RecordStruct }
                .Select(k => MapResultFingerprint(ReKind(g, k)))        // SEAM: guard struct+BaseRef
                .Distinct().ToList();
            Assert.True(results.Count == 1,
                "representation changed the mapping: " + string.Join(" | ", results));
        }, iter: 300);
}
```

Red-when: any ordering dependence, any cross-member coupling through an unmapped member, any kind-specific
divergence (the exact A11-F1/F2 family) — all without a single hand-written expected value.

---

## [R22-03] incremental: cacheability contract on every pipeline stage

Where: extend the existing `IncrementalCachingTests.cs` (landed with the conformance work).
Why:   The one compiler-testing concern with NO classic-compiler analogue: an incremental generator that
       accidentally captures `Compilation`/`ISymbol` in its pipeline silently destroys IDE caching —
       correct output, ruined editor. The existing test covers the stages of its era; every attribute
       added since (the [ProvidesMap]/[RestatesBase]/EnumStringSource wave, the surface-matrix fixes) is
       a new chance to capture a symbol.

Proposed code (delta):

```csharp
// SPDX-License-Identifier: GPL-2.0-only
[Fact]
public void Every_tracked_step_is_cached_on_an_irrelevant_edit()
{
    var driver = CSharpGeneratorDriver.Create(
        new[] { new DwarfMapperGenerator().AsSourceGenerator() },
        driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
                                                  trackIncrementalGeneratorSteps: true));
    var comp1 = Harness.Compile(RichCorpusSource);              // SEAM: reuse the corpus rich model
    driver = (CSharpGeneratorDriver)driver.RunGenerators(comp1);

    // Irrelevant edit: a new syntax tree that touches no mapper — everything must come back Cached.
    var comp2 = comp1.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated {}"));
    var run2  = driver.RunGenerators(comp2).GetRunResult();

    var notCached =
        from r in run2.Results
        from step in r.TrackedSteps                              // ALL steps, not an allowlist —
        from ex in step.Value                                    // a new stage is covered on arrival
        from output in ex.Outputs
        where output.Reason is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)
        select $"{step.Key}: {output.Reason}";
    Assert.Empty(notCached);
}
```

Red-when: any pipeline stage — present or future — re-executes on an edit that cannot affect it, i.e. the
moment someone threads a symbol into the model. This is the entry that guards consumers' IDE, which no
output-correctness test can see.

---

## [R22-04] gates: mutation score as per-subsystem ratchet

Where: `.github/workflows/mutation.yml` + the three existing `stryker-config*.json`.
Why:   Stryker exits non-zero below `thresholds.break` — a ratchet against tests that execute code without
       noticing breakage. It finds weak tests, not product bugs; schedule it, never per-PR (10–60 min class).

Proposed change (config, kernel-patch style):

```diff
 // stryker-config.json (generator core — held to the highest bar)
 {
   "stryker-config": {
-    "thresholds": { "high": 80, "low": 60, "break": 0 }
+    // break starts a few points UNDER the last measured score (green on day one), then ratchets up
+    // in deliberate commits, exactly like the fixture-adoption baseline. Never set above measured.
+    "thresholds": { "high": 85, "low": 70, "break": 66 }   // SEAM: 66 = measured-4, re-measure first
   }
 }
```

```yaml
# .github/workflows/mutation.yml — nightly, per-subsystem, artifacts the html report
on: { schedule: [{ cron: "0 2 * * *" }], workflow_dispatch: {} }
permissions: { contents: read }
jobs:
  mutate:
    strategy: { matrix: { cfg: [stryker-config.json, stryker-config.runtime.json] } }
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@<pinned-sha>
      - uses: actions/setup-dotnet@<pinned-sha>
      - run: dotnet tool restore && dotnet stryker -f ${{ matrix.cfg }}
      - uses: actions/upload-artifact@<pinned-sha>
        if: always()
        with: { name: mutation-${{ matrix.cfg }}, path: StrykerOutput/**/reports }
```

Red-when: a subsystem's tests stop noticing injected defects — the property line coverage cannot express.

---

## [R22-05] fuzz: coverage-guided type-graph exploration (adopt at saturation, not before)

Where: `tests/DwarfMapper.CompilerTests.Fuzz/` (separate project; SharpFuzz instruments the generator DLL).
Why:   Fuzzlyn proved the shape for .NET ("found many thousands of programs producing deviating behavior…
       bugs in Roslyn itself"); SharpFuzz supplies AFL/libFuzzer edge coverage for managed assemblies.
       Trigger discipline from YARPGen: adopt only when R22-01's *random* sampling stops finding bugs —
       before that point, coverage guidance buys nothing random search doesn't.

Proposed code:

```csharp
// SPDX-License-Identifier: GPL-2.0-only
// Program.cs — libFuzzer entry: fuzzer bytes -> deterministic GraphSpec -> the R22-01 legs.
using SharpFuzz;

public static class Program
{
    public static void Main() =>
        Fuzzer.LibFuzzer.Run(span =>
        {
            var g = GraphSpecCodec.Decode(span);        // SEAM: total function — EVERY byte string maps
            if (g is null) return;                      // to a valid-or-null spec, never to invalid C#
            DifferentialLegs.MustCompileOrRefuse(g);    // leg 1 from R22-01
            DifferentialLegs.AgreesWithOracle(g);       // leg 2 — throws on disagreement = fuzz finding
        });
}
// Seed corpus: serialize every GraphSpec the surface matrix + corpus tests already enumerate — the
// hand-built matrix becomes the fuzzer's starting population, which is the correct succession story.
```

Red-when: the generator has branches no hand-enumerated cell reaches — the exact "unpopulated cell" class
the changelog keeps finding by hand, searched for automatically.

---

Landing order restated: R22-00 → 01 → 02 → 03 in one arc (03 is independent and can land any day);
04 is config-only whenever a measured score exists; 05 waits for 01's saturation signal. Every entry names
its red condition; per the house rule, each should land with its own sabotage demonstration (the round-13
torture precedent) before its green is trusted.
