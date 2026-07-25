# [ISSUE-040] `MemberFacts.Readable` ignores accessor accessibility for interface sources → CS0122

| | |
|---|---|
| **Severity** | Medium |
| **Type** | Loud codegen gap (legal input → raw compile error) |
| **Component** | Generator / Core (shared by BOTH engines) |
| **Finding ID** | R6-3 |
| **Affects** | `src/DwarfMapper.Generator/Core/MemberFacts.cs` |
| **Status** | **CONFIRMED and FIXED** — commit `d71de5a` |

## Where
`MemberFacts.Readable`, the interface branch, gated only on `p.GetMethod is not null` (properties) and
`!f.IsImplicitlyDeclared` (fields), while the class branch 12 lines below gated on
`AccessorUsable(p.GetMethod, compilation, allowNonPublic)` / `FieldUsable(...)`.

## What
For an interface source type, `Readable` yielded **every** non-static property with a getter, regardless of the
accessor's declared accessibility and regardless of `allowNonPublic`. Since C# 8 interfaces may declare
`private`/`internal`/`protected` default members, this enumerated members the generated mapper cannot reference.
The two branches of the same method disagreed about the same question — the exact engine divergence the shared
`MemberFacts` extraction was meant to end, reintroduced within one method, and amplified because both engines
share this enumeration.

## Verification (independent, this repo)
- **Original audit repro (external round 6):** interface with `private int Secret => 42;` +
  `internal int Hidden => 7;`, no `AllowNonPublic` → `errors=[CS0122]`, `s.Secret`/`s.Hidden` both emitted.
- **This repo, at the unit level:** `MemberFacts.Readable(iface)` yielded `Secret`/`Hidden`. The regression test
  `Interface_readable_excludes_non_public_default_interface_members` was watched failing before the fix and
  passing after.

Note: my own prior 5-agent audit marked `MemberFacts` SOUND and missed this; the external round caught it. Both
audits were kept because the disagreement is exactly the kind of thing to verify against the code, not trust.

## Fix (applied)
Routed BOTH branches through one local `Classify(ISymbol)` function that applies the accessor-usability gate, so
the interface and class paths cannot answer "is this readable?" differently again. `Writable` has no interface
branch and was unaffected. Golden manifest unmoved (no corpus case had non-public interface DIMs); 0 warnings
solution-wide; full suite green.

## Regression tests (added)
`InterfaceSourceAccessibilityTests` — unit tests on `MemberFacts.Readable` directly (independent of downstream
emission/completeness): the negative asserts non-public DIMs are excluded; the paired positive asserts an
`internal` DIM IS enumerated with `AllowNonPublic` + same assembly (so the fix is the accessor gate, not a
blanket exclusion).
