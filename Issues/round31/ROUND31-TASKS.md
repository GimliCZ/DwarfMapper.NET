# DwarfMapper.NET — Round 31 task list (executable)

Source: `POST-ROUND30-IMPROVEMENT-RESEARCH.md` (same folder). This file turns every item of that list into a task
with exact steps, acceptance tests, audit commands and stop conditions.

Roles:
- **[S] Sonnet** — executes mechanical tasks exactly as written. Never improvises design. When a STOP condition fires,
  stops and hands the task to Opus with the evidence.
- **[O] Opus** — owns design decisions, reviews every `OPUS-REVIEW` gate, and executes tasks marked [O].
- **[H] Human (Gimli)** — account/settings steps no model can do (nuget.org policy, GitHub environments).

Audit tool: `round31-audit.sh` (same folder). `./round31-audit.sh static` checks every task's code-level
completion without building; `./round31-audit.sh tests` runs the round-31 acceptance tests.

### Verification of the prepared tests (run on `feat/round30` @ 3fa3fcb before handing over)
The test code in T01, T02, T03 and T10 was copied verbatim from this file into the repo, compiled under the repo's
strict analyzers (0 warnings, 0 errors) and run:

| task | test | result on round 30 | meaning |
|---|---|---|---|
| T01 | `AuditSuppressionScanTests` | **red** — lists exactly the 3 csproj files | correct red |
| T02 | `Null_guards_in_a_projection_tree_use_reference_equality_not_a_user_operator` | **red** — `(__s.Ship == null)` calls `op_Equality` | correct red |
| T02 | `A_nested_type_with_two_equality_operators_still_compiles` | **red** — CS0034 "Operator '==' is ambiguous on operands of type 'V' and '<null>'" | **new defect found while verifying**: generated code that does not compile, with no diagnostic |
| T03 | `DeepRecursionPocTests` (6 rows) | **green** 6/6 | guard: the CVE-2026-32933 shape already ends in `DwarfMappingDepthException` in both modes |
| T10 | `The_projection_tree_is_built_once`, `The_tree_lives_in_a_static_readonly_field` | **red** — different instances; no static field | correct red |

The scratch copies were deleted afterwards; the tree is clean. Tests for the other tasks are specified but not
pre-run — Sonnet's G3 red–green run is their verification.

---

## PART 1 — Context for Opus (read before assigning or reviewing anything)

### 1.1 Repository facts (verified on `feat/round30` @ 3fa3fcb, 2026-09-22)
- Round 30 is not yet merged into `master` (4b18afd). **All tasks start from master after the round-30 merge.**
  Re-run T00 on the merged master; if any file:line anchor below moved, Sonnet re-locates it by the quoted code text,
  never by line number alone.
- SDK pinned exactly: `global.json` = 10.0.101, `rollForward: disable`. Install with
  `dotnet-install.sh --version 10.0.101`. Newer SDKs are rejected by design.
- `TreatWarningsAsErrors=true` everywhere; NuGetAudit `all/low` fails restore on advisories; lock files + locked-mode
  restore in CI. Strict analyzers: every new file needs `// SPDX-License-Identifier: GPL-2.0-only` as line 1; string
  comparisons need `StringComparison`; formatting needs `CultureInfo.InvariantCulture`; `object.GetType()` is banned in
  `src/` (RS0030) except at the documented registry site.
- Public API frozen by PublicAPI analyzers: any new public member in `src/DwarfMapper` or `src/DwarfMapper.Testing`
  must be added to that project's `PublicAPI.Unshipped.txt`. The exact line is printed by the RS0016 build error —
  copy it verbatim.
- **Byte-identity lock already exists**: `tests/DwarfMapper.Generator.Tests/Golden/GoldenCorpusTests.cs`
  (`Generated_output_matches_the_golden_manifest`, 1,014 cases → `Golden/output-manifest.txt`). Regenerate only
  locally with `DWARF_GOLDEN_UPDATE=1`; CI refuses. Verify snapshots: 85 `*.verified.txt` files (Verify library).
  Any task that changes emitted code changes these; the diff is an OPUS-REVIEW gate.
- Test lanes: CI default runs `--filter "Category!=SurfaceMatrix"`; the surface matrix (854 cells) is its own job.
  New performance tests in this round use `[Trait("Category", "Perf")]` and T19 excludes that category from the
  default lane.
- Diagnostics: highest id is DWARF111. New ids are allocated sequentially from DWARF112 by Opus only. Every live id
  needs a NegativeCases file `tests/DwarfMapper.NegativeCases/Cases/DWARFnnn_<Name>.cs` with `// EXPECT:` and
  `// EXPECT-MESSAGE` headers (copy the format of `DWARF095_PairScopedIgnoreNoMatch.cs`), a row in
  `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`, and a `docs/diagnostics.md` entry.
- Test helpers: `GeneratorTestHarness.Run(src)` → `(Diagnostics, GeneratedSource)`;
  `GeneratorTestHarness.RunAndGetCompilationErrors(src)`; `GeneratorTestHarness.EmitAssembly(src)` →
  `(Assembly? Assembly, ImmutableArray<Diagnostic> Errors)`. Repo paths:
  `DwarfMapper.Generator.Tests.Contracts.RepoPaths.Root / .Src / .Tests / .GeneratorSrcDir`.
- Registry: `DwarfMapperRegistry` (static, add-only, first-wins + ambiguity marking). Exact pairs in a
  `ConcurrentDictionary` keyed by `record struct Key(Type, Type)`; interface-keyed pairs in a copy-on-grow array
  `_interfaceMaps` under `InterfaceMapsGate`. Every mapped pair self-registers from a generated
  `[ModuleInitializer] internal static void __Register()`, including six collection shapes keyed on `IEnumerable<S>`.
- Facade: `DwarfMapperFacade.Map<TDestination>(object)` → registry by runtime type;
  `Map<TSource,TDestination>(TSource)` → exact static pair first (documented semantics), else runtime path;
  update: `Map<TSource,TDestination>(src, dest)` → registry update table by declared types.
- Projection: a method taking `IQueryable<S>`; body is an inline `Queryable.Select(q, __s => …)`
  (`MapEmitter.cs` near the comment `if (method.IsProjection)`); nested collections are
  `global::System.Linq.Enumerable.Select<…>(…)` inside the tree. `MapMethodModel.ExtraParameters` (EquatableArray,
  `.Count`) lists extra method parameters.

### 1.2 Invariants no task may break
1. Loud, never silent: a change may add diagnostics or exceptions; it may never turn a reported problem into silence.
2. Emitted bytes change only in tasks that say so, and only in the way they say. Everything else is byte-identical
   against the golden manifest and Verify snapshots.
3. Registry semantics: first-wins, ambiguity marked and reported, add-only. Caches may only store what the
   registry would answer *for the same inputs*, and must be invalidated by registry version.
4. `Map<TSource,TDestination>` dispatches on the **static** pair when one exists.
5. Projection trees (Case 1) must stay provider-translatable: no calls into generated mapper methods inside a tree.
6. NativeAOT/trim safety: no new reflection in `src/`.

### 1.3 Traps found while preparing this list (read before reviewing the named tasks)
- **T02**: `is null` cannot be used — pattern matching is illegal in expression trees. Cast to `object` instead.
  Value-typed operands (`Nullable<T>`) keep `== null`.
- **T12**: only the *exact* pair may be cached per `<TSource,TDestination>`. The fallback path resolves by the
  source's **runtime** type, so caching it per static type would return a wrong mapper for a different derived
  instance. The slot caches "exact delegate or known-absent", nothing else.
- **T12**: the registry version must be incremented **after** the table write; readers read the version **before**
  the lookup. Reverse order can cache a stale answer forever.
- **T13**: the in-memory twin must be generated from the *same lambda text* as the tree, not from `Map`. Map and
  projection semantics may differ by design (OptionGaps). T15 is where Opus replaces pieces with general mapping,
  only where semantic equality is proven.
- **T14**: a validation root cannot call `internal` mapper methods of other assemblies. Dispatcher arms must go
  through a public per-pair slot, not direct calls. Two interface sources for one destination cannot be ordered at
  compile time (a runtime type may implement both) — those destinations must keep a runtime ambiguity check or
  route to the registry.
- **T11**: batch registration must reproduce sequential `Register` semantics exactly, including ambiguity for
  duplicates *inside* one batch.
- **Measurement honesty**: benchmark numbers in the research file come from a single-core container with models of
  the registry loop. Treat ratios as evidence, absolute times as indicative; T19's perf tests re-measure in-repo.

### 1.4 Opus decisions required before Sonnet starts the dependent task
| decision | needed by | recommendation from the research |
|---|---|---|
| Parameter classification for `ExtractionContext` (context vs per-call) — confirm Sonnet's table | T08 | context = anything decided once per mapper extraction |
| Eager typed per-pair binding vs lazy slot | T12, T14 | lazy slot first (no startup cost); eager only if a startup measurement shows it is free |
| Interface-ambiguity handling in root dispatchers | T14 | runtime check in the interface section, identical to registry behaviour |
| Name of exposed projection expression + collision id | T16 | `{MethodName}Expression`, collision → next free DWARF id |
| CS8795: throwing stubs vs current suppression | T27 | decide deliberately; stubs change the "refusal ⇒ genLen 0" invariant |
| Union metadata shape in .NET 11 | T24 | confirm from .NET 11 docs before detection code is written |
| Roslyn packaging for C# 15 `closed` | T25 | versioned analyzer folders rather than raising the 5.0.0 floor |

### 1.5 Escalation protocol (Sonnet → Opus)
Sonnet stops and posts: task id, step number, the STOP condition that fired, the command run, and its output
(trimmed to the relevant lines). Sonnet does not attempt a workaround.

---

## PART 2 — Global procedures for Sonnet (apply to every task)

**G1 Custody.** Before any baseline or measurement: `git status --porcelain` must print nothing except files this
task created. Unknown files → STOP (a leftover from an earlier session once produced a false bug report).

**G2 Build and test inside time limits.** If commands are capped (e.g. 300 s), run long work in the background and
poll:
```bash
export PATH=$HOME/dotnet:$PATH
nohup dotnet build -c Release --nologo -v q > /tmp/build.log 2>&1 &
# poll until finished:
sleep 60; pgrep -f "dotnet build" >/dev/null && echo running || grep -E "error|Error\(s\)|Warning\(s\)" /tmp/build.log | sort -u
nohup dotnet test -c Release --no-build --nologo --filter "Category!=SurfaceMatrix&Category!=Perf" > /tmp/test.log 2>&1 &
# poll, then:
grep -E "Passed!|Failed!|\[FAIL\]" /tmp/test.log
```
Single project: add the `.csproj` path. Single test class: `--filter "FullyQualifiedName~Round31.<ClassName>"`.

**G3 Red–green for every new test.** Order is fixed: write the test → run it → it must FAIL for the reason stated
(read the failure message) → implement → run → PASS. A new test that passes before the fix is not accepted unless
the task marks it "guard" (a regression guard that cannot be red today). Record both runs in the task log.

**G4 Emission changes.** If the task changes generated code: run
`DWARF_GOLDEN_UPDATE=1 dotnet test tests/DwarfMapper.Generator.Tests -c Release --no-build --filter "FullyQualifiedName~GoldenCorpusTests"`,
then `git diff --stat tests/DwarfMapper.Generator.Tests/Golden/output-manifest.txt` and list the changed case ids.
For Verify snapshots, inspect each `*.received.txt` against its `*.verified.txt`; accept only diffs that consist
solely of the change the task describes. Anything else → STOP. The list of changed cases goes to OPUS-REVIEW.

**G5 Files.** New test files go under `tests/<Project>/Round31/` with namespace `<ProjectNamespace>.Round31`, so the
audit filter `FullyQualifiedName~.Round31.` selects them. Line 1 of every new file is the SPDX header.

**G6 Commits.** One task per commit. Subject: `<type>(round31/<task-id>): <what>`, e.g.
`fix(round31/T02): cast reference-typed projection null guards to object`. Body: the red and green test runs (G3)
and, for emission changes, the changed golden case ids.

**G7 Probes.** Throwaway probe files are named `ZZ*.cs` and deleted before commit. `round31-audit.sh static` fails
if any `ZZ*.cs` remains.

---

## PART 3 — Tasks in execution order

### T00 [S] Baseline
1. Fresh clone; checkout merged master (or `feat/round30` if not merged yet — note which in the log).
2. Install SDK 10.0.101 (G2), build, full test run with the default filter. Record passed/failed per assembly.
3. Run `./round31-audit.sh baseline` — it writes `$HOME/.round31-baseline` (wide-signature count, `#pragma warning
   disable` count in `src/`, `NU190x` NoWarn count). All later audits compare against it.
4. Run `./round31-audit.sh static` — every task must report `TODO` (proves the audit itself works).
**STOP if** the build or any test fails on the untouched tree.

### T01 [S] Advisory-scoped audit suppression (research A2)
Files: `tests/DwarfMapper.DifferentialTests/DwarfMapper.DifferentialTests.csproj`,
`tests/DwarfMapper.ConsumerTests/CleanCorpus/DwarfMapper.ConsumerTests.CleanCorpus.csproj`,
`benchmarks/DwarfMapper.Benchmarks/DwarfMapper.Benchmarks.csproj`.
1. Write the scan test first (G3) — `tests/DwarfMapper.Generator.Tests/Round31/AuditSuppressionScanTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    public sealed class AuditSuppressionScanTests
    {
        private static readonly Regex NoWarnNuAudit = new(@"<NoWarn>[^<]*\bNU190[1-4]\b", RegexOptions.CultureInvariant);

        [Fact]
        public void No_project_silences_NuGet_audit_severities_wholesale()
        {
            var offenders = Directory.EnumerateFiles(RepoPaths.Root, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".props", StringComparison.Ordinal)
                            || p.EndsWith(".targets", StringComparison.Ordinal))
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(p => NoWarnNuAudit.IsMatch(File.ReadAllText(p)))
                .Select(p => Path.GetRelativePath(RepoPaths.Root, p))
                .ToList();
            Assert.True(offenders.Count == 0,
                "NU1901-NU1904 in <NoWarn> hides every future advisory of that severity. Suppress one advisory with " +
                "<NuGetAuditSuppress Include=\"https://github.com/advisories/GHSA-...\" /> instead:\n  " + string.Join("\n  ", offenders));
        }
    }
}
```
2. Run → must FAIL listing the three files.
3. In each of the three files: delete the line `<NoWarn>$(NoWarn);NU1903</NoWarn>`; keep the explanatory comment
   above it; add after the PropertyGroup that contained it:
```xml
<ItemGroup>
    <!-- AutoMapper 14.0.0 (last MIT release, benchmark/differential only): CVE-2026-32933 uncontrolled recursion. -->
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-rvv3-g6hj-g44x" />
</ItemGroup>
```
4. Restore + build the three projects (G2) → must succeed. Scan test → PASS.
Audit: `./round31-audit.sh static` → T01 `DONE`. `dotnet list tests/DwarfMapper.DifferentialTests package --vulnerable`
still lists the advisory (it is suppressed, not fixed — expected).
**STOP if** restore fails with an NU190x naming a *different* advisory — that is exactly what the old NoWarn was hiding.

### T02 [S] Projection null guards must not call user operators (research A1) — OPUS-REVIEW of golden diff
File: `src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs`.
1. Write the test first — `tests/DwarfMapper.Generator.Tests/Round31/ProjectionNullGuardTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    public sealed class ProjectionNullGuardTests
    {
        private const string RecordNested = """
            #nullable enable
            using System.Linq;
            using DwarfMapper;
            namespace T02;
            public record Addr(string City);
            public class S { public int Id { get; set; } public Addr? Ship { get; set; } }
            public class AddrDto { public string City { get; set; } = ""; }
            public class D { public int Id { get; set; } public AddrDto? Ship { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
            """;

        [Fact]
        public void Null_guards_in_a_projection_tree_use_reference_equality_not_a_user_operator()
        {
            var tree = ProjectionTree(RecordNested, "T02");
            var offenders = new NullGuardFinder();
            offenders.Visit(tree);
            Assert.True(offenders.UserOperatorGuards.Count == 0,
                "null guard calls a user-defined operator: " + string.Join(", ", offenders.UserOperatorGuards));
        }

        [Fact] // RED today, measured on round 30: the generated projection fails with CS0034 "Operator '==' is ambiguous
               // on operands of type 'V' and '<null>'" — generated code that does not compile, with no diagnostic.
        public void A_nested_type_with_two_equality_operators_still_compiles()
        {
            const string src = """
                #nullable enable
                using System.Linq;
                using DwarfMapper;
                namespace T02b;
                public class V
                {
                    public int X { get; set; }
                    public static bool operator ==(V? a, V? b) => ReferenceEquals(a, b);
                    public static bool operator !=(V? a, V? b) => !(a == b);
                    public static bool operator ==(V? a, string? b) => false;
                    public static bool operator !=(V? a, string? b) => true;
                    public override bool Equals(object? o) => ReferenceEquals(this, o);
                    public override int GetHashCode() => 0;
                }
                public class VDto { public int X { get; set; } }
                public class S { public V? Inner { get; set; } }
                public class D { public VDto? Inner { get; set; } }
                [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
                """;
            Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        }

        internal static LambdaExpression ProjectionTree(string source, string ns) => ProjectionTrees(source, ns, 1)[0];

        /// <summary>Emits ONE assembly and calls its Project method <paramref name="calls"/> times.</summary>
        internal static List<LambdaExpression> ProjectionTrees(string source, string ns, int calls)
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(source);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));
            var s = asm!.GetType(ns + ".S", throwOnError: true)!;
            var m = asm.GetType(ns + ".M", throwOnError: true)!;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(s))!;
            var queryable = typeof(Queryable).GetMethods()
                .First(x => x.Name == nameof(Queryable.AsQueryable) && x.IsGenericMethod)
                .MakeGenericMethod(s).Invoke(null, new object[] { list })!;
            var project = m.GetMethod("Project")!;
            var instance = project.IsStatic ? null : Activator.CreateInstance(m);
            var trees = new List<LambdaExpression>();
            for (var i = 0; i < calls; i++)
            {
                var result = (IQueryable)project.Invoke(instance, new[] { queryable })!;
                var select = (MethodCallExpression)result.Expression;
                trees.Add((LambdaExpression)((UnaryExpression)select.Arguments[1]).Operand);
            }
            return trees;
        }

        private sealed class NullGuardFinder : ExpressionVisitor
        {
            public List<string> UserOperatorGuards { get; } = new();
            protected override Expression VisitBinary(BinaryExpression node)
            {
                bool comparesNull = node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual
                    && (IsNull(node.Left) || IsNull(node.Right));
                if (comparesNull && node.Method is not null) UserOperatorGuards.Add(node.ToString());
                return base.VisitBinary(node);
            }
            private static bool IsNull(Expression e) => e is ConstantExpression { Value: null }
                || e is UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression { Value: null } };
        }
    }
}
```
2. Run the class → both tests must FAIL: the first names `(__s.Ship == null)` as a user-operator guard, the second
   reports CS0034 (measured on round 30 — a real defect: generated code that does not compile, silently).
3. In `MapperExtractor.Projection.cs`, add near the other private static helpers:
```csharp
/// <summary>
///     Operand for a null guard inside a projection tree. Reference-typed operands are cast to object so the guard is
///     plain reference equality — never a call to a user-defined operator== (records synthesize one), which a query
///     provider may not translate and which is ambiguous when a type declares several. Value-typed operands
///     (Nullable&lt;T&gt;) keep `== null`, which is already a HasValue test. `is null` is not an option: pattern
///     matching is illegal in expression trees.
/// </summary>
private static string NullGuardOperand(string expr, ITypeSymbol type)
    => type.IsReferenceType ? "(object)(" + expr + ")" : expr;
```
4. Find every emitted null guard: `grep -n '== null ?' src/DwarfMapper.Generator/Pipeline/MapperExtractor.Projection.cs`
   (4 sites on round 30). For each site: identify the `ITypeSymbol` of the expression being compared (a local or
   parameter in the enclosing method that holds the source member/collection type). Replace `{srcExpr} == null` with
   `{NullGuardOperand(srcExpr, <thatSymbol>)} == null`. **STOP if** no symbol for the compared expression is in scope
   at a site.
5. Update the doc example in `src/DwarfMapper.Generator/Model/ProjectionMemberMap.cs` (line with
   `__s.Inner == null ?`) to `(object)(__s.Inner) == null ?`.
6. Build; the test class → PASS. Full Generator.Tests run → only golden/snapshot tests may fail. Apply G4.
**OPUS-REVIEW:** every changed golden case must be a projection, and every diff hunk must be exactly the inserted
`(object)(…)`. Opus also checks the EF/SQLite projection rows (if T17 has built the rig) translate unchanged.

### T03 [S] Pin the AutoMapper CVE-2026-32933 proof of concept (research A3)
1. Test — `tests/DwarfMapper.Generator.Tests/Round31/DeepRecursionPocTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     AutoMapper CVE-2026-32933 (GHSA-rvv3-g6hj-g44x): ~25-30k nesting levels exhaust the stack and kill the
    ///     process. DwarfMapper's answer is a default MaxDepth with a catchable exception. This pins it for the
    ///     advisory's own shape and for collection- and dictionary-routed recursion, in both reference modes.
    /// </summary>
    public sealed class DeepRecursionPocTests
    {
        private const int Levels = 30_000; // the advisory's PoC depth

        public static TheoryData<string, string> Shapes() => new()
        {
            { "Self", "None" }, { "Self", "Preserve" },
            { "List", "None" }, { "List", "Preserve" },
            { "Dict", "None" }, { "Dict", "Preserve" },
        };

        [Theory]
        [MemberData(nameof(Shapes))]
        public void A_30000_level_graph_ends_in_a_catchable_depth_exception_not_a_dead_process(string shape, string mode)
        {
            var ns = "T03" + shape + mode;
            var member = shape switch
            {
                "Self" => "public Node? Next { get; set; }",
                "List" => "public System.Collections.Generic.List<Node> Next { get; set; } = new();",
                _ => "public System.Collections.Generic.Dictionary<string, Node> Next { get; set; } = new();",
            };
            var dtoMember = member.Replace("Node", "NodeDto", StringComparison.Ordinal);
            var src = $$"""
                #nullable enable
                using DwarfMapper;
                namespace {{ns}};
                public class Node { public int V { get; set; } {{member}} }
                public class NodeDto { public int V { get; set; } {{dtoMember}} }
                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.{{mode}})]
                public partial class M { public partial NodeDto Map(Node n); }
                """;
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));

            var nodeT = asm!.GetType(ns + ".Node", true)!;
            var next = nodeT.GetProperty("Next")!;
            object root = Activator.CreateInstance(nodeT)!, cur = root;
            for (var i = 0; i < Levels; i++)
            {
                var child = Activator.CreateInstance(nodeT)!;
                switch (shape)
                {
                    case "Self": next.SetValue(cur, child); break;
                    case "List": ((IList)next.GetValue(cur)!).Add(child); break;
                    default: ((IDictionary)next.GetValue(cur)!).Add("k", child); break;
                }
                cur = child;
            }

            var mapperT = asm.GetType(ns + ".M", true)!;
            var map = mapperT.GetMethod("Map")!;
            Exception? thrown = null;
            // Default 1 MB stack, like a request thread. A stack overflow here kills the test host: that is the red.
            var t = new Thread(() =>
            {
                try { map.Invoke(map.IsStatic ? null : Activator.CreateInstance(mapperT), new[] { root }); }
#pragma warning disable CA1031 // the assertion is on WHICH exception escaped
                catch (TargetInvocationException e) { thrown = e.InnerException; }
                catch (Exception e) { thrown = e; }
#pragma warning restore CA1031
            }, maxStackSize: 1024 * 1024);
            t.Start();
            t.Join();

            Assert.NotNull(thrown);
            Assert.Equal("DwarfMapper.DwarfMappingDepthException", thrown!.GetType().FullName);
        }
    }
}
```
   This is a **guard** test (G3): expected green before any code change. If any row fails or the host crashes,
   **STOP** — that is a new finding for Opus, not something to fix mechanically.
2. Docs (only after the test is green): in `SECURITY.md`'s claim register add a row: claim "Deeply nested or
   self-referential graphs end in a catchable `DwarfMappingDepthException` at `MaxDepth` (default 64); they cannot
   exhaust the stack (the failure class of CVE-2026-32933 in AutoMapper)"; evidence
   `tests/DwarfMapper.Generator.Tests/Round31/DeepRecursionPocTests.cs`. In `docs/MIGRATION.md` add a short
   "Coming from AutoMapper" paragraph with the same claim and the advisory link
   `https://github.com/advisories/GHSA-rvv3-g6hj-g44x`.

### T04 [S] Strengthen two LocationInfo tests (research D1)
File: `tests/DwarfMapper.Generator.Tests/Coverage/LocationInfoCoverageTests.cs`. `LocationInfo` is
`record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)`.
1. In `From_location_at_end_of_file`, after `Assert.NotNull(info);` add:
```csharp
Assert.Equal("EndOfFile.cs", info!.FilePath);
Assert.Equal(source.Length - 1, info.TextSpan.Start);
Assert.Equal(source.Length, info.TextSpan.End);
Assert.Equal(0, info.LineSpan.Start.Line);
Assert.Equal(source.Length - 1, info.LineSpan.Start.Character);
```
2. In `From_location_with_zero_length_span_does_not_throw`, replace the body after `BuildLocation(...)` with:
```csharp
var info = LocationInfo.From(location);
Assert.NotNull(info);
Assert.Equal(0, info!.TextSpan.Length);
Assert.Equal(0, info.LineSpan.Start.Character);
```
3. Sabotage check (G3 for a strengthened test): temporarily change `LocationInfo.From` to pass
   `new TextSpan(0, 0)` instead of `location.SourceSpan`; the end-of-file test must FAIL; revert; PASS.
**STOP if** `BuildLocation`'s signature differs from `(source, path, start, length)`.

### T05 [S] Duplicated coverage sources (research D2)
The audit finds three coverage-file sources that appear verbatim in other tests (measured on round 30):
| coverage file:line | also in |
|---|---|
| `Coverage/ResolveMembersDirectiveCoverageTests.cs:34` | `IgnoreConflictDirectiveNameTests.cs:49` |
| `Coverage/PairScopedIgnoreReaderCoverageTests.cs:11` | `UnscopedIgnoreNoMatchTests.cs:132` |
| `Coverage/AssemblyNameNamespaceCoverageTests.cs:20` | six framework/golden tests (the shared minimal "Sample" mapper) |
For each pair:
1. Read both tests. If they assert the **same property** of the same source (same diagnostic id / same generated
   fragment / same runtime value) → delete the coverage-file test (the other file is the older home).
2. If the source is reused on purpose and the tests assert **different** things (e.g. the third row varies the
   assembly name, not the source) → keep both and put `// shared-fixture: <what this test varies>` on the line
   directly above the coverage file's literal. The audit ignores literals marked this way.
3. Record which branch each pair took in the task log.
Audit: `./round31-audit.sh static` → T05 `DONE` (no unmarked duplicates).

### T06 [S] Confirm the byte-identity lock covers the refactor (research B1, part 1)
No new test: the golden manifest is the lock. Steps: run `GoldenCorpusTests` (must PASS on the untouched tree) and
confirm the case count printed in `Golden/output-manifest.txt` line 1 is ≥ 1,000. Record the count. Every T08
commit must leave this test green **without** `DWARF_GOLDEN_UPDATE`.

### T07 [S] Parameter-ceiling ratchet (research B1, part 2)
1. Generate the legacy table from the current tree:
```bash
python3 - <<'PY'
import re,glob
rows=[]
for p in glob.glob('src/DwarfMapper.Generator/Pipeline/**/*.cs',recursive=True):
    s=open(p).read()
    for m in re.finditer(r'(?:private|internal) static [^(\n]+ (\w+)\(([^)]*)\)',s,re.S):
        n=m.group(2).count(',')+1 if m.group(2).strip() else 0
        if n>6: rows.append((p.split('/')[-1],m.group(1),n))
for f,name,n in sorted(rows): print(f'            ["{f}::{name}"] = {n},')
PY
```
2. Create `tests/DwarfMapper.Generator.Tests/Round31/ResolverParameterCeilingTests.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    public sealed class ResolverParameterCeilingTests
    {
        private const int Ceiling = 6;

        // Measured on the pre-refactor tree. Rows only ever go DOWN (and disappear) as T08 migrates methods.
        private static readonly Dictionary<string, int> LegacyAllowance = new()
        {
            // paste the script output here
        };

        [Fact]
        public void No_pipeline_method_exceeds_its_parameter_allowance()
        {
            var offenders = new List<string>();
            var dir = Path.Combine(RepoPaths.GeneratorSrcDir, "Pipeline");
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
                foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var key = Path.GetFileName(file) + "::" + m.Identifier.Text;
                    var allowed = LegacyAllowance.TryGetValue(key, out var a) ? a : Ceiling;
                    var count = m.ParameterList.Parameters.Count;
                    if (count > allowed) offenders.Add($"{key} = {count} > {allowed}");
                }
            }
            Assert.True(offenders.Count == 0,
                "Thread shared state through ExtractionContext instead of adding parameters:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Legacy_allowances_are_not_stale()
        {
            // A row whose method now has <= Ceiling parameters (or no longer exists) must be deleted.
            var dir = Path.Combine(RepoPaths.GeneratorSrcDir, "Pipeline");
            var actual = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetRoot()
                    .DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Select(m => (Key: Path.GetFileName(f) + "::" + m.Identifier.Text, Count: m.ParameterList.Parameters.Count)))
                .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Max(x => x.Count));
            var stale = LegacyAllowance.Where(kv => !actual.TryGetValue(kv.Key, out var c) || c <= Ceiling || c < kv.Value)
                .Select(kv => kv.Key).ToList();
            Assert.True(stale.Count == 0, "Lower or delete these allowance rows: " + string.Join(", ", stale));
        }
    }
}
```
3. Both tests must PASS on the untouched tree (this is a ratchet; its red is demonstrated by adding a dummy 7th
   parameter to one small pipeline method in a scratch edit → FAIL → revert).
**STOP if** the script's row count differs from the research figure (47) by more than a few — report the number.

### T08 [S + O gate] ExtractionContext (research B1, part 3)
1. From T07's rows, list every parameter of every row method (name + type). Classify each:
   - **context** if its name is one of: `compilation`, `allowNonPublic`, `autoNest`, `explicitOnly`,
     `ignoreObsolete`, `caseInsensitive`, `implicitConversions`, `nullCollections`, `enumStrategy`,
     `referenceHandling`, `maxDepth`, `defaults`, `options` — **or** every call site passes an identifier with the
     same name as the parameter (pure pass-through; check with `grep -n "<Method>(" -r src/DwarfMapper.Generator`);
   - **per-call** otherwise;
   - **unsure** if the caller computes or modifies the value before passing it.
   Post the table. **O gate:** Opus confirms or corrects it before step 2.
2. Create `src/DwarfMapper.Generator/Pipeline/ExtractionContext.cs`: `internal sealed record ExtractionContext(...)`
   with one positional property per confirmed context parameter (same types; PascalCase names). Construct it in
   exactly one place — where `ExtractCore` reads the mapper's attribute model.
3. Migrate one method per commit, leaf-first (a method that calls none of the other row methods goes first):
   replace its context parameters with `ExtractionContext ctx`, rewrite uses `x` → `ctx.X`, update all call sites,
   build, run `GoldenCorpusTests` + the snapshot tests (must be green without regeneration), lower/delete its row in
   `LegacyAllowance`, commit.
4. Repeat until no row method has a context parameter left.
**STOP if** a golden or snapshot changes (a behaviour change slipped in), or a context parameter is reassigned
inside a method body.
Audit: wide-signature count from `./round31-audit.sh static` strictly decreases per commit.

### T09 [S] Registry collection registrations: pre-size + concrete fast path (research P1) — emission change
File: `src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs`, the loop `foreach (var r in collectionRegs)` that
emits `DwarfMapperRegistry.Register(typeof(IEnumerable<S>), typeof(<Dest>), static __s => { var __r = new List<…>(); … })`.
1. Test first — `tests/DwarfMapper.Generator.Tests/Round31/RegistryCollectionShapeTests.cs`:
   - emission assertions on `GeneratorTestHarness.Run(src).GeneratedSource` for a one-pair mapper: the generated
     text contains `TryGetNonEnumeratedCount` **and** `CollectionsMarshal.AsSpan` inside the collection
     registrations (both FAIL before the change);
   - behaviour: `EmitAssembly`, create the mapper type (triggers its module initializer), then for each source
     shape — `S[]`, `List<S>`, `HashSet<S>`, a lazy iterator (`Enumerable.Range(0,5).Select(...)` built via
     reflection over `S`), empty `List<S>` — call `DwarfMapperRegistry.Map(source, typeof(List<D>))` and
     `DwarfMapperRegistry.Map(source, typeof(D[]))` and assert element count and each element's mapped value.
     (Behaviour rows are guards: green before and after.)
2. Change the emitted lambda to this shape (placeholders: `{S}` element source FQN, `{D}` element destination FQN,
   `{MAP(x)}` the existing `r.Field.r.Method(x)` call **with the existing `RegistryNullGuard(...)` suffix unchanged**):
```csharp
static __s =>
{
    if (__s is {S}[] __a)
    {
        var __r = /* AsArray: */ new {D}[__a.Length];            /* List: */ // new List<{D}>(__a.Length);
        for (var __i = 0; __i < __a.Length; __i++) __r[__i] = {MAP(__a[__i])};   // List: __r.Add({MAP(__a[__i])});
        return __r;
    }
    if (__s is global::System.Collections.Generic.List<{S}> __l)
    {
        var __sp = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(__l);
        /* same two variants over __sp */
        return __r;
    }
    var __e = (global::System.Collections.Generic.IEnumerable<{S}>)__s;
    var __b = global::System.Linq.Enumerable.TryGetNonEnumeratedCount(__e, out var __n)
        ? new global::System.Collections.Generic.List<{D}>(__n)
        : new global::System.Collections.Generic.List<{D}>();
    foreach (var __x in __e) __b.Add({MAP(__x)});
    return /* AsArray: */ __b.ToArray();   /* List: */ // __b;
}
```
3. Build, tests PASS, apply G4 (every changed golden case must contain collection registrations).
**OPUS-REVIEW:** golden diff; confirm no Preserve/reference-tracking code was touched (the registry lambdas have
none today — verify that is still true).

### T10 [S] Hoist projection trees into static fields (research P5a) — emission change
File: `src/DwarfMapper.Generator/Pipeline/MapEmitter.cs`, block starting `if (method.IsProjection)`.
1. Test first — `tests/DwarfMapper.Generator.Tests/Round31/ProjectionHoistingTests.cs` (verified to compile and to
   fail for the right reason on round 30):
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    public sealed class ProjectionHoistingTests
    {
        private const string Src = """
            #nullable enable
            using System.Linq;
            using DwarfMapper;
            namespace T10;
            public class S { public int Id { get; set; } }
            public class D { public int Id { get; set; } }
            [DwarfMapper] public partial class M { public partial IQueryable<D> Project(IQueryable<S> q); }
            """;

        [Fact] // RED today: every Project() call builds a new tree
        public void The_projection_tree_is_built_once()
        {
            var trees = ProjectionNullGuardTests.ProjectionTrees(Src, "T10", calls: 2); // one assembly, two calls
            Assert.Same(trees[0], trees[1]);
        }

        [Fact] // RED today
        public void The_tree_lives_in_a_static_readonly_field()
        {
            var generated = GeneratorTestHarness.Run(Src).GeneratedSource;
            Assert.Contains("private static readonly global::System.Linq.Expressions.Expression<global::System.Func<", generated, System.StringComparison.Ordinal);
            Assert.Contains("__dwarf_proj_", generated, System.StringComparison.Ordinal);
        }
    }
}
```
2. Refactor the emitter so the lambda text (everything after `__s => ` up to the `)` that closes `Select`) is written
   into a separate `StringBuilder lambda` instead of `sb`. **STOP if** the lambda is written by helper methods that
   receive `sb` in more than three places — hand the refactor to Opus.
3. If `method.ExtraParameters.Count == 0`: emit, immediately before the method declaration,
   `private static readonly global::System.Linq.Expressions.Expression<global::System.Func<{Src}, {Dst}>> __dwarf_proj_{MethodName}_{Ordinal} = __s => {lambda};`
   where `{Ordinal}` is the method's index in the mapper's method list (overload-safe); the method body becomes
   `return global::System.Linq.Queryable.Select({param}, __dwarf_proj_{MethodName}_{Ordinal});`.
   Otherwise (extra parameters captured): keep today's inline emission unchanged.
4. Build, tests PASS, G4.
**OPUS-REVIEW:** golden diff limited to projection methods; confirm constructor projections (leading empty
`TargetName`) are hoisted correctly.

### T11 [S + O review] Batch registration (research P6)
Files: `src/DwarfMapper/DwarfMapperRegistry.cs`, `src/DwarfMapper/PublicAPI.Unshipped.txt`,
`src/DwarfMapper.Generator/Pipeline/AggregateEmitter.cs` (the `__Register()` emission).
1. Tests first — `tests/DwarfMapper.IntegrationTests/Round31/RegisterManyTests.cs` (use the round-13 torture file's
   `Mint<T>` idea: private marker classes per test so keys never collide with other tests):
   - `A_batch_equals_sequential_registration`: batch with entries A, B, A(dup) → `IsAmbiguous(A)` true, `TryGet(A)`
     returns the **first** A delegate, B provided and not ambiguous;
   - `Interface_keyed_entries_in_a_batch_resolve_like_sequential_ones`: `IEnumerable<Marker>`-keyed entries resolve
     for a `List<Marker>` source through `Map`;
   - `Registering_3000_interface_entries_allocates_linearly`: build the 3,000-entry array first, then measure
     `GC.GetAllocatedBytesForCurrentThread()` around one `RegisterMany` call; assert < 1 MB. (FAILS if implemented
     as a loop over `Register`.)
2. Add to the registry:
```csharp
/// <summary>Registers many maps at once with the exact semantics of calling Register for each entry in order.
/// Generated module initializers use it so interface-keyed entries grow the lock-free snapshot once per assembly.</summary>
public static void RegisterMany(ReadOnlySpan<(Type Source, Type Destination, Func<object, object> Map)> maps)
```
   Implementation: for each non-interface entry, run exactly the code `Register` runs today (extract it into a
   private method and call it from both). For interface entries: under `InterfaceMapsGate`, apply today's
   per-entry ambiguity logic against `_interfaceMaps` **plus the entries already accepted from this batch**, collect
   accepted entries in a local list, then publish one grown array (`current` + list) with a single `Volatile.Write`.
3. Build → RS0016 names the missing public API line; paste it into `PublicAPI.Unshipped.txt`.
4. Generator: in `__Register()`, collect every registration it currently emits as separate `Register(...)` calls into
   one array literal `new (global::System.Type, global::System.Type, global::System.Func<object, object>)[] { … }`
   and emit a single `global::DwarfMapper.DwarfMapperRegistry.RegisterMany(<array>);`. Keep entry order identical.
   (Update registrations stay on their own path — do not touch them in this task.)
5. Tests PASS; round-13 torture tests PASS; G4.
**OPUS-REVIEW:** concurrency — a reader must never observe a half-published batch; ambiguity across two batches from
two assemblies equals sequential behaviour.

### T12 [S per O spec] Exact-pair slot for the generic facade (research P2a, P4 update)
Files: `src/DwarfMapper/DwarfMapperRegistry.cs`, `src/DwarfMapper/IDwarfMapper.cs`, new
`src/DwarfMapper/ExactPairSlot.cs` (internal — no public API change).
1. Tests first — `tests/DwarfMapper.IntegrationTests/Round31/ExactPairSlotTests.cs`, with private nested types
   `SlotBase`, `SlotDerived : SlotBase`, `SlotDst(string Tag)`:
   - `Static_pair_wins_over_runtime_type` (guard): register `SlotBase→SlotDst` as "base" and `SlotDerived→SlotDst` as
     "derived"; `DwarfMapperFacade.Instance.Map<SlotBase, SlotDst>(new SlotDerived())` returns "base";
   - `An_exact_pair_registered_after_first_use_is_picked_up`: with only `SlotBase→SlotDst` registered, call
     `Map<SlotDerived, SlotDst>(new SlotDerived())` → "base" (runtime path); then register `SlotDerived→SlotDst` as
     "exact"; call again → must be "exact" (FAILS if the slot never invalidates);
   - `Concurrent_register_and_map_never_returns_a_wrong_delegate`: reuse the torture `RunAll` pattern — 8 readers
     calling `Map<…>` while a writer registers; every result's tag ∈ {"base","exact"} and, per reader, never goes
     from "exact" back to "base".
2. Registry: add `private static int _version;` and
   `internal static int Version => Volatile.Read(ref _version);`. In `Register`, `RegisterUpdate` and `RegisterMany`,
   call `Interlocked.Increment(ref _version)` **after** the table write. Add
   `internal static bool TryGetUpdate(Type s, Type d, out Action<object, object>? map)` if no such accessor exists.
3. `ExactPairSlot.cs`:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Threading;

namespace DwarfMapper
{
    /// <summary>
    ///     Per-(TSource, TDestination) cache of the EXACT registered pair only. The runtime-type fallback is never
    ///     cached here: it depends on the source instance's runtime type, not on TSource. Entries are tagged with the
    ///     registry version read BEFORE the lookup; any later registration invalidates them.
    /// </summary>
    internal static class ExactPairSlot<TSource, TDestination>
    {
        private sealed class Entry
        {
            public Entry(Func<object, object>? map, int version) { Map = map; Version = version; }
            public Func<object, object>? Map { get; }
            public int Version { get; }
        }

        private static Entry? _entry;

        public static Func<object, object>? Get()
        {
            var version = DwarfMapperRegistry.Version;           // read BEFORE the lookup
            var e = Volatile.Read(ref _entry);
            if (e is not null && e.Version == version) return e.Map;
            DwarfMapperRegistry.TryGet(typeof(TSource), typeof(TDestination), out var map);
            Volatile.Write(ref _entry, new Entry(map, version));
            return map;
        }
    }
}
```
   Mirror it as `ExactUpdateSlot<TSource, TDestination>` over `TryGetUpdate` with `Action<object, object>`.
4. Facade: `Map<TSource,TDestination>(TSource)` becomes
   `var map = ExactPairSlot<TSource, TDestination>.Get(); if (map is not null) return (TDestination)map(source!); return (TDestination)DwarfMapperRegistry.Map(source!, typeof(TDestination));`
   The update overload uses `ExactUpdateSlot`, falling back to today's `DwarfMapperRegistry.Update(...)` call when
   null.
5. Tests PASS; torture PASS.
**OPUS-REVIEW:** the ordering in step 2 and step 3's comment are the correctness argument — verify both.

### T13 [S] In-memory route for projections, text twin (research P5, Case 2 part 1) — emission change
Depends on T10 (the lambda text is available as a string).
1. Tests first — `tests/DwarfMapper.Generator.Tests/Round31/InMemoryProjectionRoutingTests.cs` plus a helper
   `TreeOnlyQueryable.cs` in the same folder:
```csharp
// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>An in-memory IQueryable that is NOT an EnumerableQuery, so it always takes the expression-tree path.
    /// Used as the oracle for tree-vs-routed parity.</summary>
    internal sealed class TreeOnlyQueryable<T> : IQueryable<T>, IQueryProvider
    {
        public TreeOnlyQueryable(IEnumerable<T> source) => Expression = source.AsQueryable().Expression;
        private TreeOnlyQueryable(Expression expression) => Expression = expression;
        public Type ElementType => typeof(T);
        public Expression Expression { get; }
        public IQueryProvider Provider => this;
        public IEnumerator<T> GetEnumerator() => new EnumerableQuery<T>(Expression).AsEnumerable().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public IQueryable CreateQuery(Expression e) => throw new NotSupportedException();
        public IQueryable<TE> CreateQuery<TE>(Expression e) => new EnumerableQuery<TE>(e);
        public object? Execute(Expression e) => throw new NotSupportedException();
        public TR Execute<TR>(Expression e) => ((IQueryProvider)new EnumerableQuery<T>(Expression)).Execute<TR>(e);
    }
}
```
   Tests (build the source list and the `TreeOnlyQueryable<S>` via reflection over the emitted `S`):
   - `A_list_backed_queryable_takes_the_compiled_path`: `Project(list.AsQueryable()).Expression` is a
     `ConstantExpression` (FAILS today: it is the `Select` call);
   - `Tree_and_compiled_paths_return_equal_results` — Theory over sources: flat members; nested object present and
     null; nested collection present and empty; constructor projection; enum member. Compare by projecting each
     result element to a string of its members (reflection) — tree result via `TreeOnlyQueryable`, routed result via
     `list.AsQueryable()`.
2. In `MapEmitter.cs`, only when `method.ExtraParameters.Count == 0`, emit next to the T10 field:
   `private static readonly global::System.Func<{Src}, {Dst}> __dwarf_projfn_{MethodName}_{Ordinal} = __s => {lambda};`
   (the **same** lambda text) and, as the first statement of the method body:
```csharp
if ({param} is global::System.Linq.EnumerableQuery<{Src}>)
    return global::System.Linq.Queryable.AsQueryable(
        global::System.Linq.Enumerable.Select((global::System.Collections.Generic.IEnumerable<{Src}>){param}, __dwarf_projfn_{MethodName}_{Ordinal}));
```
3. Tests PASS, G4.
**STOP if** any parity row differs — that is a semantic difference between compiled and tree evaluation; Opus
decides.
**OPUS-REVIEW:** golden diff; Case 1 purity (T13 adds no method call inside any tree).

### T14 [O design → S implementation] Static per-destination dispatch at the validation root (research P2b)
Opus writes the spec, answering: (a) how the root enumerates pairs of referenced assemblies (today
`[assembly: DwarfProvidesMap]` via `AmbientValidator`); (b) the per-pair slot the arms call (public,
`[EditorBrowsable(Never)]`, e.g. a public face of T12's slot) — arms may not call other assemblies' internal
mappers; (c) arm ordering: most-derived class first by inheritance depth, then interfaces; (d) destinations with ≥2
interface sources: runtime ambiguity check identical to the registry, or route to the registry; (e) conflicts
visible at compile time → next free DWARF id(s); (f) `Map(object, Type)`: `FrozenDictionary<Type, …>` built once
by the root; (g) `_` arm → registry (plugins, late loads).
Sonnet then implements in this order, one commit each: public dispatch slot `Dispatch<TDestination>` in the runtime
(+ PublicAPI line) → facade reads it, falls back to registry when null → root emission of dispatchers + binding in
the root's `__Register()` → diagnostics + NegativeCases files → tests:
- parity: for every (runtime type, destination) in a corpus of pairs, dispatcher result equals registry result
  (include a proxy-like subclass declared in the test and an interface-only source);
- conflict: two providing assemblies for one pair → build error at the root (NegativeCases);
- residue: a pair registered at runtime for a type the root never saw maps through `_`;
- perf (Category=Perf): 500-pair cost ≤ 1.2x 10-pair cost.

### T15 [O] Case 2 nested selects → general mapping (research P5, part 2)
Where projection and map semantics are provably equal for a nested pair, replace the twin's
`Enumerable.Select(...)` with the general collection mapping (pre-size, blits, SIMD). Proof obligation per option:
list every option in `OptionGaps.KnownSilent`; a nested pair is eligible only if none of those options is set to a
value that differs between endpoints. T13's parity tests must stay green; add rows for blittable/enum element
collections.

### T16 [O decision → S] Expose the projection expression (research P5d)
After Opus fixes the name (`{MethodName}Expression` recommended) and allocates a collision id: emit
`public static global::System.Linq.Expressions.Expression<global::System.Func<{Src}, {Dst}>> {Name} => __dwarf_proj_…;`
for closure-free projections; report the id when the name collides with a user member. Tests: the property returns
the same instance on every access; composes with `Where`/`OrderBy` in LINQ-to-objects and, once T17's rig exists, in
the EF/SQLite rows; collision case in NegativeCases.

### T17 [O/H] EF precompiled-query experiment (research P5c)
1. Create `samples/EfPrecompileProbe` (net10.0, EF Core 10 SQLite, `Microsoft.EntityFrameworkCore.Tasks`,
   `InterceptorsNamespaces` including `Microsoft.EntityFrameworkCore.GeneratedInterceptors`).
2. Three queries: plain `db.Orders.Select(o => new OrderDto{…}).ToListAsync()` (control),
   `mapper.Project(db.Orders).ToListAsync()`, `db.Orders.Select(OrderMapper.ProjectExpression).ToListAsync()` (after T16).
3. `dotnet ef dbcontext optimize --precompile-queries --nativeaot`; list which queries got generated interceptors.
4. Decide: for each DwarfMapper shape that is skipped, add a DWARF warning when the consumer references EF Core and
   sets `PublishAot`, with a NegativeCases file; document the precompilable shape in the README.

### T18 [O/H] NativeAOT size experiment (research P7)
Publish one sample with `PublishAot=true` before and after a prototype where the root registers only destinations
with `IDwarfMapper.Map` call sites (from `AmbientRequiresCollector`). Record binary sizes. Proceed only if the
difference is material.

### T19 [S writes, O decides] Perf lane and benchmarks (research P2, P4, P5, P6, P8)
1. In `.github/workflows/ci.yml` change the default test filter to `Category!=SurfaceMatrix&Category!=Perf` and add
   a scheduled job (nightly, `workflow_dispatch`) running `--filter "Category=Perf"`.
2. Add BenchmarkDotNet benchmarks in `benchmarks/DwarfMapper.Benchmarks` for: facade `Map<TS,TD>` vs direct call;
   facade collection source at 10/100/500 pairs; `Project()` inline vs hoisted; in-memory `Project()` routed vs
   tree; startup `__Register()` time and allocation at 100/500/1,000 pairs; and the three P8 ideas
   (AggressiveInlining on small nested mappers, pre-sized Preserve identity map, SkipLocalsInit) as A/B pairs.
3. Post results; Opus decides which P8 items become tasks (threshold: ≥1.3x or ≥20 % allocation, consistently).

### T20 [S] zizmor + Harden-Runner (research A5)
1. Resolve pinned SHAs (never tags):
   `git ls-remote https://github.com/step-security/harden-runner 'refs/tags/v2*'` → highest `v2.x.y`, use the peeled
   `^{}` SHA if present; same for `github/codeql-action` (`refs/tags/v3*`, for `upload-sarif`).
   zizmor version: `curl -s https://pypi.org/pypi/zizmor/json | python3 -c "import sys,json;print(json.load(sys.stdin)['info']['version'])"`.
2. New `.github/workflows/actions-security.yml`: triggers `push`, `pull_request`; `permissions: contents: read`;
   job `zizmor` with `permissions: { contents: read, security-events: write }`, steps: harden-runner (audit),
   checkout (existing pinned SHA from ci.yml), `pipx run zizmor==<version> --format sarif . > zizmor.sarif || true`,
   upload-sarif (pinned). Reporting mode: the job does not fail on findings.
3. Add as the **first step of every job in every workflow**:
```yaml
      - uses: step-security/harden-runner@<sha> # v2.x.y
        with:
          egress-policy: audit
```
4. Run `zizmor` locally (`pipx run zizmor==<version> .`) and paste the findings into the task log for Opus.
Audit: every `uses:` in `.github/workflows` is pinned to a 40-hex SHA; harden-runner is step 1 of every job.

### T21 [H → S → O review] NuGet Trusted Publishing (research A4)
[H] On nuget.org create a Trusted Publishing policy: owner = package owner, repository `GimliCZ/DwarfMapper.NET`,
workflow `release.yml`, environment `release`. In GitHub create environment `release` with required reviewers
(keeps the deliberate manual gate) and secret `NUGET_USER` (nuget.org profile name, not e-mail).
[S] In `release.yml` add a `publish` job: `needs:` the existing build/attest job, `environment: release`,
`permissions: { contents: read, id-token: write }`, harden-runner first, download the built `.nupkg` artifacts,
`NuGet/login@<sha>` (resolve like T20) with `user: ${{ secrets.NUGET_USER }}`, then
`dotnet nuget push *.nupkg --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json`.
Update `docs/RELEASING.md`: publishing is the `publish` job approved in the `release` environment; no API key is
stored anywhere.
[O] Review before the first tag.

### T22 [S] .NET 11 SDK forward-compatibility leg (research C1)
Add job `sdk-next` to `ci.yml`: `schedule` weekly + `workflow_dispatch`; `continue-on-error: true` until .NET 11 GA
(2026-11-10). Steps: harden-runner, checkout, setup-dotnet (pinned SHA) with `dotnet-version: 11.0.x` and
`dotnet-quality: preview`, then `dotnet new globaljson --sdk-version "$(dotnet --version)" --force` (overrides the pin
in the CI workspace only), build with `-p:TreatWarningsAsErrors=false` and record the warning count, run tests with
the default filter. Post the first run's result for Opus.

### T23 [S probe → O] Nullability attributes (research A6)
1. Test — `tests/DwarfMapper.Generator.Tests/Round31/NullabilityAttributeProbeTests.cs`, two cases, each asserting
   `DWARF070` (nullable source into non-nullable target) is reported:
   - source `[System.Diagnostics.CodeAnalysis.MaybeNull] public string Name { get; set; } = "";` → destination
     `public string Name { get; set; } = "";`
   - source `public string? Name { get; set; }` → destination
     `[System.Diagnostics.CodeAnalysis.DisallowNull] public string? Name { get; set; }`
   (both sources with `#nullable enable`).
2. Run. If both PASS → A6 is falsified; keep the tests as guards; done. If either FAILS → **STOP**: A6 confirmed;
   Opus designs the change to the member nullability model (including attributes on members from referenced
   assemblies).

### T24 [O research → S] C# 15 union types refused loudly (research C3)
[O] Confirm from .NET 11 documentation how a union is represented in metadata (attribute and/or interface full
names). [S] Add detection by those full names in the member/type classification; report a new DWARF id (allocated by
Opus) "union types are not mapped yet"; NegativeCases file using a hand-declared union shape in plain C# (declare the
attribute/interface in the test source if the compiler lets user code declare them); AnalyzerReleases row; docs.
Deadline: before 2026-11-10.

### T25 [O] C# 15 closed hierarchies (research C2)
Design inference of `[MapDerivedType]` arms from a `closed` base plus a missing-arm DWARF error. Includes the Roslyn
packaging decision (the API needs a newer Microsoft.CodeAnalysis than the 5.0.0 floor): versioned analyzer folders
(`analyzers/dotnet/roslyn5.0/…` + a newer folder) with a CI leg per folder.

### T26 [O] Interceptors prototype (research C4)
Only after T14 and T19: prototype rewriting `IDwarfMapper.Map<TS,TD>` call sites in the consumer compilation to the
generated method; gate on the T19 benchmark showing a remaining gap worth closing.

### T27 [O] CS8795 companion errors (research B2)
Decide: keep generation suppression (current invariant) or emit per-method throwing stubs
(`=> throw new DwarfMappingNotGeneratedException("DWARFnnn: …")`) so only DWARF errors show and clean siblings stay
green. If stubs: new runtime exception type (public API), every refusal id pinned with "stub throws with this id",
and the tests pinning `genLen == 0` rewritten deliberately.

### T28 [S, optional] Decouple assertions from generated local names (research D3)
1. Add to the test project (next to `GeneratorAssert`):
   `internal static string NormalizeLocals(string text) => Regex.Replace(text, @"\b__[A-Za-z_][A-Za-z0-9_]*\b", "__L", RegexOptions.CultureInvariant);`
2. List targets: `grep -rn 'Assert\.\(Contains\|DoesNotContain\)("[^"]*__' tests/DwarfMapper.Generator.Tests/Coverage`
3. For each: `Assert.Contains("X", generated, …)` → `Assert.Contains(NormalizeLocals("X"), NormalizeLocals(generated), …)`.
4. All tests PASS; the grep in step 2 returns nothing un-normalized.

### T29 [S] Coverage provenance headers (research D4)
1. For each file in `tests/DwarfMapper.Generator.Tests/Coverage/`: read its class `<summary>`; add after the SPDX
   line `// Covers: <Type>.<Method> — <branch or reason, from the summary>`. If the summary does not name a method
   or branch, write `// Covers: TODO(opus)` and list the file for Opus.
2. Test — `tests/DwarfMapper.Generator.Tests/Round31/CoverageProvenanceScanTests.cs`: every `.cs` file in that folder
   has a line starting `// Covers:` within its first 15 lines and none contains `TODO(opus)` (the second assertion
   stays red until Opus fills them — expected).

### T30 [S → O review] "Coming from AutoMapper" guide (research C5)
`docs/MIGRATION-from-AutoMapper.md`: AutoMapper went commercial from v15 (2025-07-02); the last MIT release 14.x
carries CVE-2026-32933 with no patch; concept mapping table (Profile/CreateMap → `[DwarfMapper]` partial methods,
`ForMember` → `[MapProperty]`, `Ignore` → `[MapIgnore]`, `ProjectTo` → projection methods, `MaxDepth` → default 64
with catchable exception); the loud-refusal difference with two examples from NegativeCases. Link it from README.
[O] fact-check every claim against its source before merge.

### T99 [S] Final gate
1. `./round31-audit.sh static` — every executed task `DONE`, no probes, no new `#pragma warning disable` in `src/`
   beyond baseline, no NU190x NoWarn, all workflow actions SHA-pinned.
2. Full build + default-filter test run (G2) → 0 failures; `./round31-audit.sh tests` → all round-31 tests pass
   except those explicitly expected red (T29 `TODO(opus)` until filled).
3. `GoldenCorpusTests` green without `DWARF_GOLDEN_UPDATE`; every golden change is in a commit that names its task.
4. CHANGELOG `[Unreleased]`: one line per landed task, in the repo's existing style.
