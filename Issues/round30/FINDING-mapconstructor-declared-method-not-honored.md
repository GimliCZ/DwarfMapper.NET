<!-- SPDX-License-Identifier: GPL-2.0-only -->

# `[MapConstructor<S,T>]` beside a declared `partial` method: silently ignored, and DWARF056's text is wrong when it fires

**Filed 2026-09-10**, a probe run during the DWARF108/DWARF109 coverage arc (see
[FINDING-dwarf108-dwarf109-coverage-sweep.md](FINDING-dwarf108-dwarf109-coverage-sweep.md)), not fixed. This
is exactly the shape the first draft of `EmitMethodBranchCoverageTests`' factory-path test tried — before
discovering it silently fell through to ordinary constructor-argument selection and rewriting the test around
a `[GenerateMap]`-declared pair instead, which the factory wiring DOES reach.

## The asymmetry

`[MapProperty<TSource,TTarget>]` (pair-scoped, class-level, no method needed) is read for a pair **two ways**:
the `[GenerateMap<S,T>]`/nested-synthesis loops (`MapperExtractor.Phases.cs`, the original 2026-06-27 design
in `d846350`), **and** a declared `partial` method for the same pair (`MatchPairProps`, `MapperExtractor.Phases.cs:1791`,
added later — the comment there says "Pair-scoped class-level config ([MapProperty<S,T>]) also applies to a
DECLARED partial method for the same pair; method-level config wins, pair-scoped fills the gaps").

`[MapConstructor<TSource,TTarget>]` was never given that second extension. All three of its readers
(`AnyPairConstructor` in the blit gate, the `[GenerateMap]` pair's own construction at
`MapperExtractor.Phases.cs:3336-3359`, and the nested-pair construction at `MapperExtractor.Phases.cs:3616-3634`)
are gated on `decls.GenPairs.Exists(...)` — a pair some `[GenerateMap<S,T>]` declares. A pair that exists only
as a declared `partial` method (the ordinary, most common way to declare a mapper method) never reaches any
of them.

## What actually happens (verified empirically)

```csharp
public class Src { public string Name { get; set; } = ""; }
public class Dst
{
    public Dst(string name) { Name = name; }
    public string Name { get; }
}
[DwarfMapper]
[MapConstructor<Src, Dst>(nameof(Create))]
public partial class M
{
    public partial Dst Map(Src s);
    private static Dst Create(Src s) => new Dst(s.Name);
}
```

Generates cleanly, calling `ConstructorSelector.Select` and using `Dst`'s own constructor — `Create` is never
called, and the mapper works correctly (the constructor happens to satisfy the same shape the factory would
have). But it fires:

```
warning DWARF056: [MapConstructor<Demo.Src, Demo.Dst>("Create")] matches no [GenerateMap<Demo.Src, Demo.Dst>] pair
```

**This message is wrong for this exact case.** The `(Src, Dst)` pair is not unmapped — it is mapped, by the
declared `Map` method, correctly. DWARF056's text ("matches no `[GenerateMap<...>]` pair") tells the user
their fix is to add a `[GenerateMap<Src, Dst>]` attribute, which is not what's needed and not even necessary
for the mapper to work — only for the factory attribute to be honored. A user who trusts the diagnostic and
adds `[GenerateMap<Src, Dst>]` next to the existing declared method would additionally have to worry about
`DWARF060`-style same-source-multi-target collision (a declared method AND a `[GenerateMap]` for the identical
pair) — untested territory this finding did not explore further.

## Why this is a finding, not a fix (yet)

Two legitimate directions, and picking one is a product decision, not a coverage-sweep byproduct:

1. **Extend `[MapConstructor]` to declared methods**, mirroring `[MapProperty]`'s `MatchPairProps` precedent —
   probably the more consistent, more expected behavior (a user writing `[MapConstructor<S,T>]` beside a
   `partial Dst Map(Src s)` almost certainly means it to apply there).
2. **Leave `[MapConstructor]` scoped to `[GenerateMap]` pairs deliberately**, and fix only DWARF056's message
   to say the true thing when a declared method exists for the pair — something like "matches no
   `[GenerateMap<S,T>]` pair and no declared method reads pair-scoped `[MapConstructor]` — construction for
   an existing declared method is chosen by `ConstructorSelector`, not by this attribute."

`git log -S PairConstructors` shows three touching commits (`d846350` introducing it scoped to
`[GenerateMap]`/nested pairs by original design, `afce186` and `4f8d013` pure refactors, `180510e` closing a
different Preserve/SetNull-mode bypass) — nothing records a deliberate ruling that declared methods should
stay excluded. It reads as an oversight (option 1 never landed after `MatchPairProps` set the precedent for
`[MapProperty]`), not a considered decision, but that is a read, not a git-log-verified ruling — flagging
this explicitly rather than asserting it as fact.

## Recommendation for whoever picks this up

Start with option 1 (extend, matching `[MapProperty]`'s precedent) unless a ruling search turns up a reason
not to; if kept scoped, DWARF056's message must stop claiming "matches no pair" when a declared method for
the exact pair exists. Either way, `DWARF059` (invalid factory) and `DWARF056`'s NegativeCases fixtures
should grow a declared-method case once the direction is chosen.
