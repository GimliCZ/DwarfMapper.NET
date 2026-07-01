# Design: `MapConfig<S,T>` — type-safe, refactor-safe member-selector configuration

- **Date:** 2026-07-01
- **Status:** Approved (design); ready for implementation planning
- **Component:** `DwarfMapper` (runtime attributes/API) + `DwarfMapper.Generator` (Roslyn source generator)

## 1. Problem

DwarfMapper's per-pair member configuration is authored with **strings**:

```csharp
[MapProperty<DiscordEmbedNotificationConfig, DiscordEmbedNotificationConfigDocument>("Author.Name", "AuthorName")]
[MapProperty<ChatSessionDocument, ChatHistorySummary>(nameof(ChatSessionDocument.MessagesCount), nameof(ChatHistorySummary.NumberOfMessages))]
[MapProperty<S,T>(nameof(S.X), nameof(T.Y), Use = nameof(Conv))]
```

This is unsafe and verbose:

- Raw string paths (`"Author.Name"`) have **no compile-time checking** — a typo or rename silently mis-maps or drops config.
- `nameof(...)` is safer but still **repeats the type** already named in the generic args, and the member reference is only checked in isolation (nothing verifies `S.X` and `T.Y` have compatible types until deep in the generator).
- Method references (`Use`/`When`/factory) are `nameof` strings resolved by the generator, not the compiler.

**Why not just put lambdas in the attribute?** C# attribute arguments must be compile-time constants; **lambdas are not legal attribute arguments** (`[MapProperty(t => t.X)]` does not compile). AutoMapper only offers `d => d.X` because its config is a *runtime* fluent API; Mapperly (an attribute-based source generator like DwarfMapper) has the exact same string limitation. The type system can encode a member's *type* but never its *identity* (name), so member selection cannot be expressed through generics alone.

**Key realization:** a source generator never *executes* user code, but it can *read the syntax* of a lambda. A member-selector lambda living in a **method body** (not an attribute) can be walked by Roslyn — `t => t.AuthorName` is a `MemberAccessExpressionSyntax` whose name is literally `"AuthorName"`. No execution; pure tree-walking, plus semantic-model verification.

## 2. Goals / Non-goals

**Goals**
- A type-safe, refactor-safe way to author **every string-bearing member operation**, replacing both member-name strings and `nameof` method strings.
- Zero new runtime cost, reflection-free, AOT-safe: the config method is **read at compile time, never executed**.
- "Never silent": any selector or reference the generator cannot understand is a **diagnostic**, never an ignored mapping.
- Full parity with the attribute front-end: identical generated code / runtime behavior.

**Non-goals (deferred)**
- **Inline converter/factory/predicate lambda bodies** (e.g. `.Map(t=>t.X, s=>s.Y, v => v.Trim())`, `.Construct(s => new T(...))`). v1 accepts **method groups only**. Emitting arbitrary lambda bodies (captures, closures, ref-safety) is materially larger scope and is a future iteration.
- Replacing structural/type-level attributes that are already string-free (see §4).
- Class-level options (`[DwarfMapper(EnumStrategy=…)]`), map declaration (`[GenerateMap<S,T>]`), and ambient/cross-assembly attributes — not member config.

## 3. Config surface: a convention method matched by parameter type

The selector lambdas live in a method the generator matches by its **single parameter type** `MapConfig<S,T>`. No attribute, no magic string; the S→T pair comes from the generic args; the method name and accessibility are arbitrary; the method is **never called**.

```csharp
[DwarfMapper]
public partial class Mapper
{
    // Read syntactically by the generator. Never executed. Recommended: private static.
    private static void Cfg(MapConfig<ChatSession, ChatHistorySummary> c) => c
        .Map(t => t.NumberOfMessages, s => s.MessagesCount)
        .Map(t => t.AuthorName,       s => s.Author.Name);   // dotted source path = flatten

    public partial ChatHistorySummary Map(ChatSession s);
}
```

- A block body is equally valid: `{ c.Map(...); c.Ignore(...); }`.
- The pair is matched **wherever it is mapped** — a top-level `[GenerateMap]` pair or an auto-synthesized nested/collection-element pair — exactly like the pair-scoped attributes (`MapPropertyAttribute<S,T>`).

## 4. Coverage matrix (every capability accounted for)

**Fluent surface — the 9 string-bearing member operations:**

| Capability (attribute) | Fluent equivalent | Strings removed |
|---|---|---|
| `[MapProperty<S,T>("A.B","X")]` | `.Map(t => t.X, s => s.A.B)` | member names + flatten path |
| `… Use = nameof(Conv)` | `.Map(t => t.X, s => s.Y, Conv)` | member names + `nameof` converter |
| `… When = nameof(Pred)` | `.MapWhen(t => t.X, s => s.Y, Pred)` | member names + `nameof` predicate |
| `… NullSubstitute = v` | `.MapOr(t => t.X, s => s.Y, v)` | member names |
| `[MapIgnore<T>("X")]` | `.Ignore(t => t.X)` | member name |
| `[MapIgnoreSource("Y")]` | `.IgnoreSource(s => s.Y)` | member name |
| `[MapValue<T>("X", 42)]` | `.Value(t => t.X, 42)` | member name |
| `[MapValue<T>("X"){Use=nameof(F)}]` | `.Value(t => t.X, F)` | member name + `nameof` |
| `[MapConstructor<S,T>(nameof(Make))]` | `.Construct(Make)` | `nameof` factory |

**Stays an attribute — already string-free or not member config:**

- `[MapDerivedType<Cat,CatDto>]`, `[GenerateMap<S,T>]`, `[GenerateWrapperMap]` — pure generics, no strings.
- `[BeforeMap]` / `[AfterMap]` — matched by **signature**, no strings; method markers, not pair config.
- `[Flatten]`, `[FlattenGraph]`, `[Reinterpret]`, `[AutoNest]`, `[RoundTrip]`, `[ReverseMap]` — structural/type-level, no member names.
- `[DwarfMapper(EnumStrategy=…)]` and other class options — enums, no strings.

## 5. Runtime API (`DwarfMapper.MapConfig<TSource, TTarget>`)

A compile-time-only configuration surface. Its methods exist so the selector lambdas **type-check** (that is what makes them safe); they are never invoked by generated code. Fluent (each returns the same instance). No public constructor is required — the generator matches the *parameter type*; user code never constructs it.

```csharp
namespace DwarfMapper;

public sealed class MapConfig<TSource, TTarget>
{
    // rename / flatten (source selector may be a dotted member-access chain)
    public MapConfig<TSource, TTarget> Map<TMember>(
        Func<TTarget, TMember> target, Func<TSource, TMember> source);

    // with converter method group: srcMember -> tgtMember
    public MapConfig<TSource, TTarget> Map<TSrcMember, TTgtMember>(
        Func<TTarget, TTgtMember> target, Func<TSource, TSrcMember> source,
        Func<TSrcMember, TTgtMember> convert);

    // conditional assignment (predicate over the source)
    public MapConfig<TSource, TTarget> MapWhen<TMember>(
        Func<TTarget, TMember> target, Func<TSource, TMember> source,
        Func<TSource, bool> when);

    // null-substitute: emitted as `source ?? fallback`
    public MapConfig<TSource, TTarget> MapOr<TMember>(
        Func<TTarget, TMember> target, Func<TSource, TMember> source, TMember fallback);

    // suppress completeness (DWARF001) for a destination member
    public MapConfig<TSource, TTarget> Ignore<TMember>(Func<TTarget, TMember> target);

    // suppress source-coverage suggestion (DWARF039) for a source member
    public MapConfig<TSource, TTarget> IgnoreSource<TMember>(Func<TSource, TMember> source);

    // constant value for a source-less destination member
    public MapConfig<TSource, TTarget> Value<TMember>(Func<TTarget, TMember> target, TMember value);

    // computed value via a parameterless method group
    public MapConfig<TSource, TTarget> Value<TMember>(Func<TTarget, TMember> target, Func<TMember> compute);

    // factory construction: TSource -> TTarget method group
    public MapConfig<TSource, TTarget> Construct(Func<TSource, TTarget> factory);
}
```

**Type-safety bonus (free, from the compiler):** the shared `TMember` on `Map`/`MapWhen`/`MapOr` forces the source and target member types to match; a mismatched pair fails to compile before the generator runs. The generator does not need to re-check it.

**Signature details to finalize at implementation (not design-blocking):**
- `MapOr` must express "possibly-null source member, non-null fallback". For a value-type member this is `Func<TSource, TMember?>` (i.e. `Nullable<TMember>`); for a reference-type member it is `Func<TSource, TMember>` with a nullable annotation. C# cannot express both under one unconstrained `TMember`, so `MapOr` will ship as **two overloads** — `where TMember : struct` and `where TMember : class` — or the reference-type case will simply reuse `Map` + a fallback. Pick during implementation; behavior (emit `source ?? fallback`) is identical.
- The two `Value` overloads (constant vs `Func<TMember>` compute) are disambiguated by the compiler at the call site and by the generator syntactically (literal/const expression → constant; method group → compute). If a member type is itself a delegate, prefer the constant overload and document the edge.

## 6. Generator mechanics — a new front-end over the **same IR**

`MapConfig` produces the **exact internal structures the attributes already produce** (`PairProp`, `PairIgnore`, `PairConstructor`, `PairValue`/`MapValue` entries, source-ignores). Everything downstream — member resolution, conversions, completeness, cycle handling, snapshots — runs unchanged. `MapConfig` is purely a new way to *author* the same intermediate representation.

1. **Match.** Scan the `[DwarfMapper]` class's members for a method with exactly one parameter whose type is `DwarfMapper.MapConfig<S,T>` (resolved via the compilation: compare `ConstructedFrom` to the `MapConfig<,>` symbol; fall back to full-name + arity). Extract `S`, `T` from the type args.
2. **Read (syntax, not execution).** From the method's `DeclaringSyntaxReferences`, get the body (expression-bodied or block). Walk the fluent chain; each call is an `InvocationExpressionSyntax` whose expression is a `MemberAccessExpressionSyntax` (`.Map`, `.Ignore`, …). Dispatch on the invoked method name.
3. **Parse arguments.**
   - **member selector** `p => p.M1.M2…Mn` → strip the lambda parameter → path `"M1.M2…Mn"`.
   - **method group** `Conv` (bare identifier or `Type.Member`/`this.Member`) → simple name `"Conv"`.
   - **constant** literal/`const` → value (for `Value` / `MapOr` fallback).
4. **Emit IR.** Build `PairProp { Source, Target, SrcMember, TgtMember, Use, When, HasNullSub, NullSub }`, `PairIgnore`, `PairConstructor`, `MapValue`, and source-ignore entries, then merge into the same `pairProps` / `pairIgnores` / … lists that `ReadPairMapProperties` etc. populate.

### 6.1 Selector grammar (validated)
Valid selector: `p => p.M1.M2…Mn`, where `p` is the lambda's single parameter and each `.Mi` is a property or field access. Yields the dotted path.

Rejected (→ diagnostic, never silently ignored): method calls, indexers, casts, null-conditional `?.`, binary/conditional expressions, a root other than the lambda parameter, or anything that is not a pure member-access chain.

### 6.2 Method-group grammar (validated)
Valid: a bare identifier or `Type.Member` / `this.Member` naming a method. The simple name feeds the existing `Use`/`When`/factory resolution, so signature validation is identical to the attribute path. Rejected: inline lambda or explicit delegate creation (→ diagnostic guiding to a named method).

## 7. Diagnostics ("never silent")

- **DWARF068** *(new, Error)* — unsupported `MapConfig` expression: a selector that is not a member-access chain, or a method reference that is not a method group.
- **DWARF069** *(new, Error)* — conflicting configuration: the same destination member is configured by **both** an attribute and `MapConfig` (or twice within `MapConfig`). Explicit conflict rather than silent precedence.
- **Reused:** DWARF041 (`Use`/`Value` compute invalid), DWARF050 (`When` predicate invalid), DWARF059 (factory invalid), DWARF056 (`MapConfig<S,T>` for a pair that is never mapped), DWARF049 (null-substitute not assignable), DWARF040/042 (`Value` constant/target invalid).

Diagnostic locations point at the offending `.Map(...)` invocation / lambda in the config method.

## 8. Coexistence, precedence, and the never-called method

- Attributes and `MapConfig` both feed the same lists and may be **mixed per-member**; only a genuine same-member collision errors (DWARF069). Mixing on the same pair is allowed but discouraged (documented).
- The config method is dead code by design. Recommended pattern: `private static` — private methods do not trigger the default "unused" warnings. Documented; no generator magic. (`CA1811` "avoid uncalled private code", off by default, is the only analyzer that would flag it; note in docs.)
- If a user does invoke the config method at runtime, it is a harmless no-op (fluent methods return `this`).

## 9. Self-validation / meta-test impact

Adding `MapConfig<,>` as a public type and DWARF068/069:
- **T2** (every public attribute in `FeatureInteractionCompileMatrix` or exempt): `MapConfig` is a *type*, not an attribute; add to `MatrixExemptAttributes` or the matrix as appropriate, or extend the T2 scope note.
- **Scan1a/b/c, Scan1f, Scan2, Scan3:** register DWARF068/069 descriptors, `AnalyzerReleases.Unshipped.md` rows (Error severity), pipeline references, and test references. Next free id after the current DWARF067 is **DWARF068** (then DWARF069).

## 10. Testing strategy (proves parity, not merely "it runs")

1. **Differential runtime oracle (primary).** For a representative set of shapes (rename, flatten, converter, predicate, null-substitute, ignore, value, factory), build two mappers — one authored with attributes, one with `MapConfig` — and assert **byte-identical output** over shared `ObjectFactory(seed)` graphs via the existing cross-type comparer. This directly proves the front-end has parity.
2. **Generator unit tests.** Selector parsing (rename / flatten / deep nested path), method-group extraction, block vs expression body, and each new diagnostic (DWARF068 unsupported selector, DWARF068 non-method-group ref, DWARF069 conflict, DWARF056 unmapped pair).
3. **Snapshot.** `MapConfig` and the equivalent attributes produce equivalent generated code.
4. **Full suite green** (currently 3,589 self-tests) after each increment, and the FusedChat consumer re-verified against the new library.

## 11. Scope boundary (v1)

In: the 9 member operations in §4 via method-group references; convention config method; the four diagnostics; parity tests.

Out (future): inline converter/factory/predicate lambda bodies; a fluent surface for structural/type-level attributes; multiple config methods contributing to one pair (v1: at most one `MapConfig<S,T>` method per pair, else DWARF069).
