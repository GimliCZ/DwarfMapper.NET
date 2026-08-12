<!-- SPDX-License-Identifier: GPL-2.0-only -->
# DwarfMapper.NegativeCases

**What the build must refuse, and what it must say while refusing.**

The companion to `tests/DwarfMapper.ConsumerTests`: that project asserts what a consumer's program *does* at
run time across assembly boundaries; this one asserts what the build *rejects*. Between them they cover the
two things a snapshot cannot — behaviour and refusal.

## Why message text, not just ids

Round 18 — a migration of ~300 AutoMapper maps onto DwarfMapper — produced three separate tasks whose entire
content was *"the id was right and the message did not help"*:

- `DWARF076` documented a suppression that does not work.
- `CS9035` fired against generated code, saying nothing about the `[MapIgnore]` that caused it.
- `DwarfMapMissingException` named an unpronounceable compiler-generated iterator type.

A diagnostic's id tells the reader which rule they hit. Its **message** is the only part that tells them what
to write instead — and, outside the individual diagnostic suites, it had no test at all. So cases assert the
rendered message, not the format string.

## The case format

One file per refusal. Each is a real, deliberately red `.cs` source — removed from `Compile` and carried as an
embedded resource, so it can be opened, edited, and pasted straight into a scratch project to reproduce the
diagnostic by hand. The header declares what the build must say about it:

```csharp
// CASE: one-line title
// WHY:  why this shape is red, in the reader's terms
// EXPECT: DWARF007, DWARF078
// EXPECT-MESSAGE DWARF078: CS8795
// EXPECT-CS: CS8795
```

- **`EXPECT` is an exact set.** A case that starts provoking an extra id has changed meaning, and that change
  being invisible is the reason this project exists. It also keeps each case at the shortest source that
  produces exactly its set. Most error cases legitimately declare two ids: the error itself and `DWARF078`,
  the signpost for the `CS8795` cascade it causes.
- **`EXPECT-MESSAGE` asserts the rendered message**, repeatable per id.
- **`EXPECT-CS` asserts the compiler errors the emission produces.** A DwarfMapper diagnostic that *explains*
  a compiler error (`DWARF078` → `CS8795`, `DWARF079` → `CS9035`) is only correct while that error is still
  what the consumer actually sees. If the cascade changes shape, the explanation becomes misinformation, and
  no other test in the repository would notice.
- **`WHY` is mandatory.** A case nobody can explain is a case nobody can maintain.

Naming: `DWARFnnn_ShortName.cs`, or `CLEAN_ShortName.cs` for a shape that must stay silent. At least one clean
case is load-bearing: a suite that can only prove diagnostics *fire* would go on passing while a widened
trigger filled every consumer's build with noise.

## The ratchet

`DiagnosticCoverageRatchetTests` is forward-looking. Demanding a case for all 78 pre-existing ids would have
been a mechanical translation of tests that already exist; what was missing is a rule for the *next* one. So
the ids that predate this project (2026-08-12) are listed in `PredatesThisProject`, and a descriptor that is
neither listed there nor backed by a case file fails the build.

Adding an id to that list is how you opt out — deliberately, in a diff a reviewer will see and ask about. The
list is also allowed to **shrink**, and should: every entry removed is a diagnostic that gained an executable
statement of the shape that triggers it and the message a consumer will read. Removing an id from the list is
part of the same commit that adds its case.

## Why its own driver

`CaseDriver` deliberately does not reuse `GeneratorTestHarness` from `DwarfMapper.Generator.Tests`. Two
reasons: this project must stay able to say "the shipped generator refuses this shape" without inheriting
whatever accommodations the main harness has grown, and a Round-18 audit found two defects that self-review
had passed, both of the form *"a check that read a subset of the inputs the real path reads"*. A second,
differently-built instrument is the cheapest defence against that.

It also takes its references from the runtime's trusted-platform-assemblies list rather than from whatever is
loaded into the test AppDomain — otherwise a case could pass because an earlier test happened to load
`System.ComponentModel`, and the suite would be order-dependent.
