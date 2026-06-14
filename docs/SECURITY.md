<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Security posture & threat model

DwarfMapper is a **compile-time, reflection-free** Roslyn source generator. This document records how it
handles the security/robustness issues that have affected the runtime-reflection mappers, so we don't
repeat them. Each row is cross-checked against a real advisory or documented pitfall of the reference
libraries (AutoMapper / Mapperly / Mapster), studied license-safely (Mapperly/Mapster are MIT; AutoMapper
referenced as 14.0.0 conceptually only).

## How DwarfMapper avoids the known mapper failure modes

| Issue class | Reference-library precedent | DwarfMapper stance |
|---|---|---|
| **Uncontrolled recursion → uncatchable `StackOverflowException` (DoS)** | **AutoMapper 14.0.0 — CVE-2026-32933 / GHSA-rvv3-g6hj-g44x** (CWE-674, CVSS 7.5): a self-referential graph (~25–30k levels) crashes the process; a `StackOverflowException` cannot be caught in modern .NET. The free branch is won't-fix. | **Default-on, catchable depth guard.** Recursion-capable type pairs carry a depth counter; cyclic/over-deep data throws a **catchable** `DwarfMappingDepthException` at `MaxDepth` (default 64, hard cap 1000) — through direct, `List<T>`, *and* `Dictionary<K,V>` edges. `ReferenceHandling=Preserve` reconstructs topology via an identity map; `OnCycle=SetNull` breaks cycles to null. There is **no configuration in which untrusted cyclic input produces an uncatchable StackOverflow.** |
| **Generator-side compile-time StackOverflow on recursive types** | Mapperly #1751/#785 (generator/projection SO on recursive properties). | The generator enforces a **nesting cap (`DWARF031`)** when synthesizing nested mappers, so mutually-recursive / self-referential type graphs terminate generation with a diagnostic rather than hanging or crashing the compiler. Regression-tested. |
| **Silent numeric narrowing / overflow (data-integrity)** | C# default `unchecked` context silently wraps `long→int`, `double→int`, etc. | Every cross-width narrowing emits `T.CreateChecked(...)` → throws **`OverflowException`** (fail-loud), never a silent wrap. Lossless widening is silent; non-lossless conversions also surface `DWARF038`. Verified at runtime (overflow throws inside collections, nested members, ctor params, and dictionary values). |
| **Culture-sensitive parsing footgun** | `Parse`/`ToString` without an `IFormatProvider` pick up ambient `CurrentCulture` — `"1.234"` parses differently under `de-DE` vs `en-US`, silently changing values per deployment/thread. | All generated `Parse`/`ToString` conversions pass **`CultureInfo.InvariantCulture`** (or a user-specified provider) — the generator emits **zero** `CurrentCulture`/`CurrentUICulture` calls. |
| **Reflection / dynamic-code / untrusted-type instantiation** | Runtime mappers compile expression trees and walk types reflectively; mapping to `object`/`dynamic`/polymorphic destinations from untrusted input can instantiate attacker-influenced types. | The shipped generator and runtime library are **reflection-free**: generated code instantiates only statically-known destination types. No `Activator.CreateInstance`, `Type.GetType(string)`, `Expression.Compile`, or `MakeGenericType` anywhere in the production path. This eliminates the type-confusion class structurally (and keeps trim/NativeAOT safety — CI AOT gate). |
| **Vulnerable transitive dependencies** | AutoMapper 14.0.0 carries its CVE in its own engine; supply-chain advisories generally flow through deps. | The runtime library (`DwarfMapper`) has **zero NuGet dependencies**. The generator references only Roslyn (`Microsoft.CodeAnalysis.*`) with `PrivateAssets="all"` — a build-time analyzer, never shipped to consumers. `dotnet list package --vulnerable --include-transitive` reports **no vulnerable packages**. |
| **Trim / NativeAOT unsafety** | AutoMapper (reflection) and Mapster (runtime mode) are not cleanly trim/AOT-safe (`IL2026`/`IL3050`). | Reflection-free generated code is trim- and AOT-safe; a `DwarfMapper.AotSample` + CI gate verifies a `PublishAot` build. |

## Over-posting / mass-assignment guidance (consumer responsibility)

The classic mapper security risk is **mass assignment / over-posting**: mapping an untrusted input object
directly onto a domain entity can set protected fields (`IsAdmin`, `Id`, `Balance`, `RowVersion`). No
mapper makes this "safe by default" — and per the
[OWASP Mass Assignment Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Mass_Assignment_Cheat_Sheet.html),
**the mitigation is exactly a narrow DTO mapped via a mapper**. To use DwarfMapper safely with untrusted
input:

1. **Map untrusted input into a narrow input DTO, never directly onto a domain entity.** If the DTO has no
   `IsAdmin`/`Id` member, there is nothing for an attacker to set — the protected field cannot flow.
2. **Lean on the completeness gate.** DwarfMapper requires every destination member be mapped
   (`DWARF001`); a stray protected field surfaces as a **build error**, not a silent pass. Enable
   `RequiredMapping = Both` to also flag *unused source* members (`DWARF039`) so a too-wide input DTO is
   visible.
3. **For update-into-existing entities**, map only the fields a request is allowed to change — use
   `[MapIgnore]` for protected members so they are never assigned, and prefer explicit `[MapProperty]`
   over broad by-name auto-mapping for trust boundaries.

## Caveat: `DwarfMapper.Testing`

The optional `DwarfMapper.Testing` fixture helper (round-trip / object-factory verification) **does** use
reflection (`Activator.CreateInstance`, `MakeGenericType`) to synthesize random test instances. This is a
**test-time** aid operating over the consumer's *own* compile-time types — it is **not** part of the
production mapping path, carries no untrusted input, and is not referenced by generated mappers. It is the
one component that is not trim/AOT-safe, by design (testing code). The core generator + runtime library
remain fully reflection-free.

## Reporting

Security issues: please open a private advisory on the repository rather than a public issue. The shipped
surface is small (a build-time generator + a dependency-free runtime library), which is itself the primary
mitigation.
