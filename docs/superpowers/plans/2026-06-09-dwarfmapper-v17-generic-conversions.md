# DwarfMapper.NET — Plan 17: Principled generic-type conversions (generic math + IParsable)

> **For agentic workers:** TDD throughout. Steps use checkbox (`- [ ]`). SPDX header on new files; CPM; warnings-as-errors; warning-clean. New diagnostics → `AnalyzerReleases.Unshipped.md` (none expected here).

**Goal.** Replace DwarfMapper's silent/limited scalar conversions with principled, reflection-free, AOT-safe conversions built on .NET generic-type interfaces — turning the enum-underlying "havoc" (silent narrowing truncation) into **loud, checked** behavior, and generalizing string conversions via `IParsable<T>`.

**Decisions (owner-approved):**
1. Narrowing numeric / enum-underlying conversions → emit **`CreateChecked`** (throws `OverflowException` on loss; succeeds when the value fits). No new compile-time diagnostic.
2. `string → T` conversions → **automatic** when `T : IParsable<T>` (emit `T.Parse(s, InvariantCulture)`; loud on bad input).

**Research basis (authoritative):**
- `static virtual TSelf INumberBase<TSelf>.CreateChecked<TOther>(TOther value) where TOther : INumberBase<TOther>` — throws `OverflowException` when out of representable range. All built-in numerics (`sbyte…ulong`, `nint/nuint`, `Half`, `float`, `double`, `decimal`, `Int128/UInt128`, `BigInteger`) implement `INumberBase`. **Enums do NOT.**
- For floating/decimal → integer, `CreateChecked` truncates the fraction silently (only magnitude overflow throws). ⇒ **Scope auto generic-math to integral↔integral** (and enum-underlying, which is integral). Float/decimal lossy conversions remain explicit-`Use=` only.
- `IParsable<TSelf>.Parse(string, IFormatProvider)` — throws `FormatException`/`OverflowException` on bad input. Implemented by all our basic value types + `string`. Enums are NOT `IParsable` (keep existing by-name string↔enum path).
- All emitted calls (`global::System.Int32.CreateChecked(v)`, `global::System.Guid.Parse(s, …)`) are concrete static-abstract-interface invocations: **reflection-free, trim/AOT-safe** (validated by the AOT gate).

**Builds on:** trunk + the `feat/basic-types-coverage` branch (basic-types fixture, ObjectFactory DTO/TimeSpan, widened fuzzer, enum-underlying FINDING tests). **Branch off `feat/basic-types-coverage`** (so it includes the new coverage) → `feat/v17-generic-conversions`. (If that branch is already folded to trunk by the time you start, branch off trunk.)

---

## New shared helper: `Pipeline/TypeInterfaces.cs`
Symbol-level predicates against the (.NET 10) `Compilation`, used by the resolvers. Pure functions; no state stored in cached models (only the resulting synthesized-method NAME flows into the model).
- [ ] `bool IsIntegral(ITypeSymbol)` — sbyte/byte/short/ushort/int/uint/long/ulong/nint/nuint by `SpecialType` (reuse/move the existing `EnumConverter.IsIntegral`; add nint/nuint).
- [ ] `bool ImplementsIParsable(Compilation c, ITypeSymbol t)` — `t.AllInterfaces.Any(i => i.OriginalDefinition is the type from c.GetTypeByMetadataName("System.IParsable`1") && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], t))`. Guard null (older TFMs).
- [ ] `bool ImplementsIFormattable(ITypeSymbol t)` — `t.AllInterfaces.Any(i => i.ToDisplayString() == "System.IFormattable")`.
- [ ] `bool IsINumberBase(Compilation c, ITypeSymbol t)` — true for the integral set (sufficient for our scope; do not need full interface walk, but may also check `AllInterfaces` against `System.Numerics.INumberBase`1` for robustness).

---

## Task 17.1: Integral↔integral checked numeric conversion
**New `Pipeline/NumericConverter.cs`** + wire into `TryResolveConversion`.

- [ ] **Step 1 (failing tests)** `tests/DwarfMapper.Generator.Tests/NumericConversionTests.cs` + runtime `tests/DwarfMapper.IntegrationTests/NumericConversionRuntimeTests.cs`:
  - `long → int` narrowing: compiles, no error; runtime value within range preserved; **out-of-range throws `OverflowException`** (e.g. `(long)int.MaxValue + 1`).
  - `int → uint`, `int → short`, `ulong → long`: compile + in-range preserved + overflow throws.
  - widening `byte → int`, `int → long`: still resolves via the EXISTING implicit path (direct assign — assert generated does NOT call CreateChecked for these; they must stay zero-cost).
  - `int → string` is NOT this task (it's 17.3) — keep out.
- [ ] **Step 2:** run → FAIL (today `long→int` etc. hit DWARF005 NoImplicitConversion).
- [ ] **Step 3:** `NumericConverter.TryCreate(src, tgt, synthesized)` returns a synthesized method name when **both** `IsIntegral(src) && IsIntegral(tgt)` and there is **no** implicit conversion (narrowing/sign-change). Emit:
  `private static {Fq(tgt)} {name}({Fq(src)} v) => {Fq(tgt)}.CreateChecked(v);`
  (`{Fq(tgt)}` is e.g. `global::System.Int32`, which exposes the static `CreateChecked`.) FNV-1a-hashed name like the other synthesizers.
- [ ] **Step 4:** In `TryResolveConversion`, AFTER the implicit-conversion check (currently line ~457) and the nullable branch, BEFORE the `autoCandidates`/enum fallthrough, try `NumericConverter.TryCreate`; if non-null set `converterMethod` and return true. (Place so a user `Use=` and implicit/identity still win first; and so it never intercepts enum types — enums are not integral `SpecialType`, so `IsIntegral` is false for them: verified safe.)
- [ ] **Step 5:** tests + build 0/0. Commit `feat(gen): integral narrowing via INumberBase.CreateChecked (loud OverflowException, no silent truncation)`.

## Task 17.2: Enum conversions use CreateChecked (fix the silent-truncation havoc)
Edit `Pipeline/EnumConverter.cs`. The by-NAME and enum↔string paths are unchanged (safe). Only the value/numeric casts change.

- [ ] **Step 1 (update the FINDING characterization tests)** — the two tests added on `feat/basic-types-coverage` currently assert SILENT truncation:
  - `FINDING_Enum_long_to_int_narrowing_truncates_silently` (runtime + gen) → rename/retarget to `Enum_long_to_int_narrowing_throws_on_overflow`: in-range enum value maps fine; an enum value > int range now **throws `OverflowException`** at runtime. Update the gen-level test (it asserted no diagnostic — still no diagnostic, but now document the runtime-checked behavior).
  - `FINDING_EnumByValue_long_to_byte_underlying_truncates_silently` → `EnumByValue_long_to_byte_narrowing_throws_on_overflow`: `EnumStrategy.ByValue`, source underlying value 256 → byte target **throws `OverflowException`**.
  - Keep a positive case: in-range ByValue / enum→numeric still maps correctly.
- [ ] **Step 2:** FAIL (today truncates).
- [ ] **Step 3:** Change emission:
  - `AddCast` for **enum→numeric** (`"EnumNum"`): emit `({Fq(tgt)}){Fq(tgt)}.CreateChecked(({underlyingOf src})v)` — i.e. cast enum to its underlying, then `TgtNum.CreateChecked(...)`. (tgt is integral.) Concretely: `global::System.Int32.CreateChecked((global::System.Int64)v)`.
  - `AddCast` for **numeric→enum** (`"NumEnum"`): emit `({Fq(tgtEnum)}){Fq(tgtUnderlying)}.CreateChecked(v)` — checked into the enum's underlying, then cast to the enum.
  - `AddEnumByValue`: emit `({Fq(tgt)}){Fq(tgtUnderlying)}.CreateChecked(({Fq(srcUnderlying)})v)`.
  - Use `enumType.EnumUnderlyingType!` for the underlying `SpecialType`/Fq. Split `AddCast` into the two directions if cleaner (it currently serves both via prefix).
- [ ] **Step 4:** run ALL enum tests (EnumNumericTests, EnumToEnumTests, EnumRuntimeTests, EnumUnderlyingTests) + build. The small-value enums (Color{Red=0,Green=1}) still pass (CreateChecked(0/1) fine). Commit `fix(gen): enum↔numeric and ByValue use CreateChecked — narrowing throws instead of silently truncating`.

## Task 17.3: Automatic `string → T` via IParsable, and `T → string` via IFormattable
**New `Pipeline/ParsableConverter.cs`** + wire in. (Enum↔string stays in EnumConverter.)

- [ ] **Step 1 (failing tests)** `tests/DwarfMapper.Generator.Tests/ParsableConversionTests.cs` + runtime `ParsableConversionRuntimeTests.cs`:
  - `string → int` (no `[MapProperty(Use=)]`) auto-resolves; runtime `"42" → 42`; bad input `"x"` throws (`FormatException`).
  - `string → Guid`, `string → DateTime` (InvariantCulture), `string → bool` auto-resolve + round-trip a known value.
  - `int → string`, `Guid → string`, `DateTime → string` auto-resolve via IFormattable → `ToString(null, InvariantCulture)`; round-trip with `string→T` preserves value.
  - **Precedence:** an explicit `[MapProperty(Use=nameof(custom))]` for a `string→int` member still uses the custom method (the existing MoneyMapper-style test must keep passing).
  - **Enum unaffected:** `string → enum` still maps by NAME (existing behavior), not via IParsable (enums aren't IParsable) — assert an existing enum-string test still green.
- [ ] **Step 2:** FAIL (today string→int etc. → DWARF005 unless Use=).
- [ ] **Step 3:** `ParsableConverter`:
  - `string→T`: when `src.SpecialType == System_String`, `tgt` is NOT an enum, and `ImplementsIParsable(c, tgt)` → synthesize `private static {Fq(tgt)} {name}(string v) => {Fq(tgt)}.Parse(v, global::System.Globalization.CultureInfo.InvariantCulture);`
  - `T→string`: when `tgt.SpecialType == System_String`, `src` is NOT an enum (enum→string handled), `src` is NOT already string, and `ImplementsIFormattable(src)` → synthesize `... string {name}({Fq(src)} v) => v.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture);`
  - Wire into `TryResolveConversion` AFTER numeric (17.1) and AFTER `autoCandidates`, BEFORE `EnumConverter.TryCreate` (so enum↔string still routes through EnumConverter — but note EnumConverter only triggers for enum operands, and ParsableConverter explicitly excludes enums, so order between them is safe either way; keep Parsable before enum for clarity). Identity/implicit/`Use=` still win first.
- [ ] **Step 4:** run FULL solution. Reconcile any test that previously asserted `string↔primitive` is an ERROR (there should be none; if found, that behavior is now intentionally supported — update the test + note it). Build 0/0. Commit `feat(gen): automatic string↔T via IParsable<T>.Parse / IFormattable.ToString (InvariantCulture)`.

## Task 17.4: AOT gate + fuzzer + docs
- [ ] Extend `samples/DwarfMapper.AotSample` with ONE mapper exercising a checked numeric narrowing + a `string↔int` + `string↔Guid` conversion, so the multi-RID `aot-trim-gate` proves the emitted `CreateChecked`/`Parse` calls are trim/AOT-clean. Run `dotnet publish samples/DwarfMapper.AotSample -c Release -r linux-x64 -p:PublishAot=true -warnaserror` locally; must pass with no IL2xxx/IL3xxx.
- [ ] (Optional, if low-effort) Add a small `ConversionFuzz` differential: schema with `string` source members ↔ `int`/`Guid` target members and assert round-trip via the existing `EmitAssembly`. If non-trivial, DEFER and note it (the current identical-shape fuzzer doesn't trigger conversions).
- [ ] README + spec: add a short "Built-in conversions" section — implicit (zero-cost) / integral narrowing (`CreateChecked`, throws on overflow) / `string↔T` (`IParsable`/`IFormattable`, InvariantCulture) / enum (by-name default, by-value `CreateChecked`). Note float/decimal lossy conversions require explicit `[MapProperty(Use=)]`. Commit `docs+test: AOT-gate generic conversions; document built-in conversion matrix`.

---

## Self-Review
- The enum-underlying "havoc" is eliminated: narrowing now throws `OverflowException` (loud) instead of silently truncating — consistent with the "no silent surprises" thesis, without a blanket compile-time ban. ✅
- Generic-math scoped to integral↔integral to avoid introducing silent float/decimal fraction loss; lossy float/decimal stays explicit. ✅
- `string↔T` generalized via `IParsable`/`IFormattable`, InvariantCulture, loud on bad input; enums keep their by-name string path; explicit `Use=` still wins. ✅
- All emitted code is concrete static-abstract-interface calls — reflection-free, AOT/trim-safe, gated. ✅

## Deferred
- Float/decimal lossy auto-conversion (precision-loss policy) — explicit `Use=` for now.
- `ISpanParsable`/`Utf8` fast paths; culture configurability (InvariantCulture fixed).
- A dedicated conversion-dimension fuzzer (current fuzzer is identity-shape).
