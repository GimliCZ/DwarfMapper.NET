# DwarfMapper threat model

Read by Anthropic's OSS Scanner before it audits the image `Dockerfile` (next to this file) builds. The invariants
DwarfMapper claims, and the test holding each one, are in [SECURITY.md](../SECURITY.md) under *Trust model*; this
file says where to look, how to exercise the code, and how to rate what turns up.

## What this project does and where untrusted input enters

DwarfMapper is a compile-time object mapper for .NET. `src/DwarfMapper.Generator` is a Roslyn incremental source
generator: it runs inside the consumer's C# compiler, reads the consumer's own types and mapping attributes, and
writes ordinary C# mapping methods into the consumer's assembly. `src/DwarfMapper` is the small runtime those methods
call - the depth bound, reference tracking, the cross-assembly registry - plus the attributes.

Untrusted input reaches it in two places, and they are not equal:

1. **At run time - the one that matters.** A consumer maps data it does not control: a deserialized request body into
   a domain object, a database row into a DTO. The emitted code and the runtime then walk an attacker-shaped object
   graph: any depth, cycles, shared references, any enum value whether declared or not, null wherever the type allows
   it, any string, any collection size. Treat every VALUE as hostile. The SHAPE - the types - is fixed at compile time
   by the consumer.
2. **At compile time - lower.** The generator reads the consumer's source and referenced assemblies. Whoever writes
   that source already controls the build, so it is not an attack surface in itself. What does matter is that, for
   ANY legal C# input, the generator never emits code that is wrong in a way the consumer cannot see: a byte copy
   between types that are not layout-identical, a binding to a member C# accessibility would refuse, a value written
   to a member other than the one the mapping declares.

## Components that matter most / least

Most:

- What the generator emits for the run-time path, and the pipeline that decides it
  (`src/DwarfMapper.Generator/Pipeline`, `Core`, `Collections`). Emitted code lives in the consumer's assembly, so a
  flaw there ships into every consumer.
- The byte-level paths: span and array copies through `MemoryMarshal.Cast`, emitted only where the element pair was
  proven layout-identical (`[Reinterpret]` is the one user override, and it is still width-checked), and
  `[MapDenseEnumKeys]`, which uses an enum key as the index into an `[InlineArray]`.
- Recursion and references: the depth bound (`DwarfRefContext`, `MaxDepth`, `DwarfMappingDepthException`) and
  `ReferenceHandling.Preserve`.
- The run-time registry, `DwarfMapperRegistry`.

Less: `src/DwarfMapper.CodeFixes` (IDE code fixes, compile time only) and `src/DwarfMapper.Testing` (a test-time
toolkit for consumers, not for production code).

Out of scope - none of it ships: `tests/`, `samples/`, `benchmarks/`, `conformance/`, `scripts/`, `Issues/`, `docs/`
and `src/DwarfMapper.DocTooling`.

## How to exercise it

The image holds a Release build of the whole solution and has no network. `NuGetAudit` is off for every build in it -
`DWARFMAPPER_OFFLINE_IMAGE=true` is the one exception `Directory.Build.props` makes - because the audit already ran,
online, when the image was built. New projects and test files restore and build offline.

- The push-time suite CI runs:
  `dotnet test DwarfMapper.NET.sln -c Release --no-build --filter "Category!=SurfaceMatrix&Category!=Perf"`.
- What the generator emits for a shape: a test in `tests/DwarfMapper.Generator.Tests` calling
  `GeneratorTestHarness.Run(source, NullableContextOptions.Enable)` returns the diagnostics and the generated source.
- Emitted code run against hostile values: a mapper and a test in `tests/DwarfMapper.IntegrationTests`, which compiles
  with the real generator. `SecurityRegressionRuntimeTests` is where security regressions live, and
  `DeepRecursionPocTests` shows the depth bound holding at 30,000 levels on a thread with a 1 MB stack.
- `tests/DwarfMapper.Generator.Tests/Framework/GoldenCorpus.cs` holds more than a thousand mapping shapes with pinned
  output, and `EmittedIlIsVerifiableTests` runs ilverify over its feature cases.
- `src/DwarfMapper.Testing` has seeded fixture builders and a property-based fuzzer that produce values for any type.

## How you rate severity

- **Critical:** emitted code reads or writes out of bounds, or reinterprets bytes it was not proven to, for some input
  VALUE, under a mapping that compiles without an error.
- **High:** an uncatchable crash (a stack overflow) or unbounded memory or CPU from mapping attacker-controlled values
  under default options; emitted code that reaches a member C# accessibility would refuse; a value written to a
  different member or key than the mapping declares - a dense enum key landing in another key's slot, say.
- **Medium:** a crash or resource exhaustion that needs a non-default option the consumer chose, such as a raised
  `MaxDepth`; an exception escaping where the documentation promises none; a silently wrong value with no memory-safety
  or trust-boundary component.
- **Low:** a generator crash, hang or wrong diagnostic on legal input - a build failure, on the developer's own source.

Not a vulnerability: anything that needs the attacker to write the consumer's source or to load an assembly into the
process - SECURITY.md explains why the registry's first-wins rule is accepted on exactly that basis - and anything
SECURITY.md documents as deliberate.

## Reports and patches

- One report per root cause: one emission path gets one report, however many shapes reach it.
- Every fix here ships with a regression test, named for the finding, that fails before the fix and passes after it,
  in the same commit. A candidate patch in that shape is one we can merge.
- A proof of concept as a test - `GeneratorTestHarness` for what is emitted, `tests/DwarfMapper.IntegrationTests` for
  what it does at run time - is preferred over a standalone program.

## Anything to leave alone

- The AutoMapper 14.0.0 advisory (NU1903, GHSA-rvv3-g6hj-g44x). AutoMapper is referenced only by
  `tests/DwarfMapper.DifferentialTests` and `benchmarks/`, as a comparison target, and does not ship; upgrading it is
  licence-blocked (15 and later are RPL-1.5). DwarfMapper's own answer to that advisory's failure class is
  `DeepRecursionPocTests`.
