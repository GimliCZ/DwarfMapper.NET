<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Migration methodics — converting to DwarfMapper

A **feature-by-feature** conversion guide from **AutoMapper 14.0.0**, **Mapster 10.x**, and
**Riok.Mapperly 4.x** to DwarfMapper. Every feature/mechanic of each library has a row: the source API,
the DwarfMapper equivalent, the mechanical before→after, and an honest note where behaviour **diverges** or
is a **deliberate non-goal**.

This complements [`COMPARISON.md`](COMPARISON.md) (capability/perf matrix). The matching **parity test
suite** — `tests/DwarfMapper.IntegrationTests/LibraryParityRuntimeTests.cs` — proves at runtime that each
"YES" row below actually behaves equivalently; each test cites the competitor feature it validates.

> **Licensing note.** AutoMapper is referenced as **14.0.0 only** (the last MIT release) and only
> *conceptually* — no AutoMapper source or tests are reproduced. Mapster and Mapperly are MIT.

---

## 0. The three transformations that cover 90% of any migration

| Migration axiom | Why | Mechanical rule |
|---|---|---|
| **Runtime engine → one `partial class`** | DwarfMapper has no `MapperConfiguration`/`IMapper`/`Profile`/DI; the generated class *is* the configuration. | Delete the config/DI registration. `new MyMapper()` (stateless, allocation-free) or register the concrete type in DI. |
| **`CreateMap`/convention → `[GenerateMap<A,B>]` or a `partial` method** | A mapping pair is declared once, at compile time. | `CreateMap<A,B>()` → `[GenerateMap<A,B>]` on the class, **or** `public partial B Map(A a);`. Call sites drop the generic arg: `mapper.Map<B>(a)` → `mapper.Map(a)`. |
| **Lambdas → named members** | Attributes cannot carry closures. | Every `MapFrom(s=>…)`, `Condition`, `ConvertUsing(s=>…)`, resolver, before/after lambda becomes a **named method** referenced by `Use=`, `When=`, or `[BeforeMap]`/`[AfterMap]`. |

Two consequences worth stating up front:

- **Delete your `AssertConfigurationIsValid()` tests.** Completeness is a non-optional **build error**
  (`DWARF001`) in DwarfMapper — strictly stronger than a runtime/test assertion. You literally cannot
  compile a mapping that drops a destination member (annotate intentional drops with `[MapIgnore]`).
- **POCOs stay attribute-free.** Only the mapper class is annotated — same as all three competitors.

---

## 1. From AutoMapper 14.0.0

AutoMapper is a runtime expression-tree/reflection engine with a fluent builder; DwarfMapper is a
compile-time generator with attribute-only config. Anything requiring runtime reflection, DI-injected
resolvers, ambient `ResolutionContext`, or a fluent runtime builder is a deliberate non-goal — and usually
has a static, compile-checked replacement.

### 1.1 Configuration & registration

| AutoMapper 14 | DwarfMapper | Before → after |
|---|---|---|
| `new MapperConfiguration(cfg=>…)` / `IMapper` | **non-goal** — no runtime config object | Delete it; the generated class is the config. |
| `class P : Profile { CreateMap<A,B>(); }` | `[DwarfMapper] partial class` + `[GenerateMap<A,B>]` | `class P:Profile{CreateMap<A,B>();}` → `[DwarfMapper][GenerateMap<A,B>] partial class P{}` |
| `CreateMap<A,B>()` | `[GenerateMap<A,B>]` or `partial B Map(A)` | find-and-replace; call site `Map<B>(a)`→`Map(a)` |
| `.ReverseMap()` | `[ReverseMap]` + explicit inverse method | see 1.6 |
| `services.AddAutoMapper(asm)` | `new MyMapper()` / register the type | no assembly scan (AOT-safe by design) |
| `CompileMappings()` / `AssertConfigurationIsValid()` | **non-goal / redundant** | delete — the build *is* the compile and completeness check |

### 1.2 Member configuration (`ForMember` family)

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| `.ForMember(d=>d.X, o=>o.MapFrom(s=>s.Y))` (rename) | `[MapProperty(nameof(S.Y), nameof(D.X))]` | direct |
| `.MapFrom(s=>s.A.B)` (nested source) | `[MapProperty("A.B", nameof(D.X))]` | deep source path; nullable interior hop → `DWARF044` |
| `.MapFrom(s=>Compute(s.Y))` (expression) | `[MapProperty(nameof(S.Y), nameof(D.X), Use=nameof(Compute))]` | `Compute(srcMember)→destType` |
| `.Ignore()` | `[MapIgnore(nameof(D.X))]` | **required** in DwarfMapper — unmapped = `DWARF001`, not silent |
| `.MapFrom(_=>"const")` (constant) | `[MapValue(nameof(D.X), "const")]` | type-checked → `DWARF040` |
| `.MapFrom(_=>Compute())` (no-source computed) | `[MapValue(nameof(D.X), Use=nameof(Compute))]` | `Compute` is **parameterless** → `DWARF041` |
| `.NullSubstitute(v)` | `[MapProperty(src, tgt, NullSubstitute=v)]` | emits `src ?? v`; type-checked → `DWARF049` |
| `.Condition(s=>p)` / `.PreCondition(s=>p)` | `[MapProperty(src, tgt, When=nameof(P))]` | `bool P(S)`; member keeps **default** when false → `DWARF050` |
| `.ForSourceMember(s=>s.X, o=>o.DoNotValidate())` | `[MapIgnoreSource("X")]` | only relevant under `RequiredMapping=Both` |
| `.ForPath(d=>d.A.B, …)` | `[MapProperty(src, "A.B")]` | single-level unflatten; deeper → `DWARF045` |
| `.SetMappingOrder(n)` | **non-goal** | deterministic canonical order; use `[AfterMap]` if order mattered |
| `.UseDestinationValue()` / per-member `AllowNull` | **partial** | update-into replaces (not merges); null policy is mapper-wide |

### 1.3 Flattening / unflattening / inclusion

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| Convention flatten `Customer.Name`→`CustomerName` (automatic) | `[Flatten("Customer")]` *(if names align)* or `[MapProperty("Customer.Name","CustomerName")]` | **DIVERGENT**: flattening is **explicit**, not implicit name-splitting. No `GetX()`-method convention — map a method's value via `Use=`. |
| `.IncludeMembers(s=>s.Inner)` | `[Flatten("Inner")]` or per-leaf `[MapProperty("Inner.X","X")]` | multi-source first-match ordering must be explicit |
| Unflatten via `.ReverseMap()` | `[MapProperty("CustomerName","Customer.Name")]` on the inverse | intermediate must be a class w/ public parameterless ctor (`DWARF045`) |

### 1.4 Custom conversion

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| `.ConvertUsing(s=>…)` / `ITypeConverter<S,D>` (type pair) | a `D Convert(S s)` method on the mapper | user methods take precedence over synthesis; no `ResolutionContext` |
| `IValueResolver` / `IMemberValueResolver` (per member) | `[MapProperty(src, tgt, Use=nameof(M))]` | `M` sees the **source member**, not whole source/dest/ctx |
| `IValueConverter<TSrc,TDst>` (reusable per member) | `Use=nameof(M)` | exact match |
| `mapper.Map(src, o=>o.Items["k"]=v)` + `context.Items` | **extra method parameter**: `partial D Map(S s, T k)` | **DIVERGENT**: typed param matched to a dest member by name (`DWARF047`); no dynamic bag, not propagated to nested |
| built-in scalar coercions (often needs config) | **richer & stricter, built-in** | `int↔long` checked, `string↔IParsable` (InvariantCulture), enum↔{enum,string,int}, nullable lift; lossy → `DWARF038` (or error under `ImplicitConversions=false`). **You can often delete explicit converters.** float/decimal→int still needs `Use=` (never silent truncation). |

### 1.5 Hooks & lifecycle

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| `.BeforeMap((s,d)=>…)` | `[BeforeMap] void Hook(S s)` | sees source only (dest not yet built in new-instance path) |
| `.AfterMap((s,d)=>…)` / `IMappingAction` | `[AfterMap] void Hook(S s, T t)` (or `void Hook(T t)`) | ideal for filling `[MapIgnore]`d members |
| `mapper.Map(src, o=>{o.AfterMap(…)})` (per-call) | **non-goal** | lift data into an extra param, or act at the call site |

### 1.6 Reverse / inheritance / polymorphism

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| `.ReverseMap()` | `[ReverseMap]` on forward + explicit inverse `partial A ToA(B)` | inverts **simple renames** only; non-invertible config (`Use=`, dotted, `NullSubstitute`, `When`) → `DWARF051`; missing inverse → `DWARF052` |
| `.Include<DS,DD>()` (base→derived) | `[MapDerivedType<DS,DD>]` on the base method | most-derived-first switch; unregistered runtime type throws |
| `.IncludeBase<…>()` | **partial** | no config-inheritance primitive; restate shared config |
| `.IncludeAllDerived()` | **non-goal** | list each `[MapDerivedType]` arm (no reflection discovery) |
| runtime `Map(obj, srcType, destType)` polymorphism | `[MapDerivedType]` generated type switch | AOT-safe, no reflection |

### 1.7 Construction & update targets

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| ctor/record mapping (automatic) | automatic (records, `record struct`, immutable structs) | every ctor param mandatory (`DWARF024`); deterministic selection (`[DwarfMapperConstructor]`) |
| `.ForCtorParam("p", o=>o.MapFrom(s=>s.Y))` | `[MapProperty(nameof(S.Y), "p")]` | targets ctor param by name; conversions apply |
| `.ConstructUsing(s=>new D(...))` / `IDestinationFactory` | **partial / DIVERGENT** | generator emits the `new D(...)`; custom logic → `Use=` converter or `[AfterMap]` |
| `mapper.Map(src, existingDest)` | `partial void Update(S s, T d)` / `partial T Update(S s, T d)` | identity preserved; nested/collections **replaced, not merged** |

### 1.8 Collections / enums / null / generics

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| list/array/collection mapping | `T[]`, `List<T>`, `HashSet<T>` dests; read-only source shapes accepted | bulk-copy when element type unchanged |
| `AllowNullCollections=true` (default null→empty) | `[DwarfMapper(NullCollections=AsNull)]` (default `AsEmpty`) | mapper-wide; matches AM default |
| dictionary mapping | `Dictionary<K,V>`→`Dictionary<K2,V2>` | **keys and values** both converted; post-conversion key collision overwrites (no throw) |
| enum by value (default); by-name via EnumMapping pkg | **DIVERGENT**: **by-name is the default**; `[DwarfMapper(EnumStrategy=ByValue)]` for AM's default | missing by-name member → build error `DWARF015`; no extra package |
| open generics `CreateMap(typeof(S<>),typeof(D<>))` | **non-goal** | one `[GenerateMap<S<Foo>,D<Foo>>]` per closed type used |

### 1.9 Reference handling / depth / projection / validation / naming

| AutoMapper 14 | DwarfMapper | Note |
|---|---|---|
| `.MaxDepth(n)` (default 64→ here too) | `[DwarfMapper(MaxDepth=n)]` | throws catchable `DwarfMappingDepthException`, never silent SO; applies to direct/list/dict edges |
| `.PreserveReferences()` | `[DwarfMapper(ReferenceHandling=Preserve)]` | full topology (shared/diamond/cycle) reconstructed |
| *(no first-class equivalent)* | `[DwarfMapper(OnCycle=SetNull)]` | **DwarfMapper-only**: cycle→null ≡ `System.Text.Json` `IgnoreCycles` |
| `query.ProjectTo<Dto>(cfg)` | `partial IQueryable<Dto> Project(IQueryable<S>)` | **provably translatable**: direct members, renames, `[MapIgnore]`, enum→int casts, nested objects, collections, and dotted-path flattening; only non-translatable conversions (narrowing/parse/by-name/`Use=`/`HashSet`·dict/reference-handling) → `DWARF028` |
| `.ExplicitExpansion()` / projection params | **non-goal** | define a narrower DTO/`Project` method; parameterize the source query |
| `AssertConfigurationIsValid()` | **build error `DWARF001`** (always on) | `MemberList.Source` ≡ `RequiredMapping=Both`; `MemberList.None` ≡ has no analogue (use `[MapIgnore]`) |
| naming conventions (`SourceMemberNamingConvention` …) | `[DwarfMapper(NameConvention=Flexible)]` | Pascal/camel/snake/UPPER interchangeable; collision → `DWARF048` |
| `RecognizePrefixes/Postfixes`, `ReplaceMemberName` | **non-goal** | explicit `[MapProperty]` per affected member |
| `ShouldMapProperty/Field` predicate | **non-goal** | fixed rule (public instance fields+props); exclude via `[MapIgnore]` |
| case-insensitive matching | `[DwarfMapper(CaseInsensitive=true)]` | ambiguity → `DWARF010` |
| `ResolutionContext.Items`/`State` | **non-goal** | extra typed method parameter |

---

## 2. From Mapster 10.x

Mapster is convention-first and zero-config at the call site (`src.Adapt<Dst>()`), with `TypeAdapterConfig`
for overrides. Migration is mostly **adding explicit declarations** — there are no per-pair config classes
to port for the convention path.

### 2.1 Core

| Mapster | DwarfMapper | Before → after |
|---|---|---|
| `src.Adapt<Dst>()` (zero-config) | `[GenerateMap<Src,Dst>]` + `mapper.Map(src)` | add one attribute line per pair you use |
| `config.NewConfig<S,D>()` / `ForType<S,D>()` | the `[GenerateMap]`/`partial` method | the per-type config root |
| `src.Adapt(existingDst)` | `partial Dst Update(S s, Dst d)` | identity preserved; replaces nested (see note) |
| `config.ForType<S,D>().TwoWays()` | `[ReverseMap]` + explicit inverse | inverts simple renames; rest restated |

### 2.2 Member config

| Mapster | DwarfMapper | Note |
|---|---|---|
| `.Map(d=>d.X, s=>s.Y)` | `[MapProperty(nameof(S.Y), nameof(D.X))]` | direct |
| `.Map(d=>d.X, s=>Expr(s))` | `Use=nameof(Method)` | named method |
| `.Ignore(d=>d.X)` | `[MapIgnore(nameof(D.X))]` | required (completeness gate) |
| `.IgnoreIf((s,d)=>cond, d=>d.X)` | `[MapProperty(src, tgt, When=nameof(P))]` | `When` gates the assignment |
| `.Map(d=>d.X, s=>s.Y, srcCond)` | `When=` | condition on the member |
| `.IgnoreNullValues(true)` | **DIVERGENT** | DwarfMapper update **replaces**; it does not skip null-source members. Use `[MapIgnore]` or restructure. |
| `.AfterMapping((s,d)=>…)` / `.BeforeMapping(…)` | `[AfterMap]` / `[BeforeMap]` | named hook methods |
| `.ConstructUsing(s=>new D(...))` | **DIVERGENT** | generator selects/emits the ctor; custom → `Use=`/`[AfterMap]` |
| `.MapWith(s=>convert(s))` (type pair) | a `D Convert(S)` method on the mapper | user-method precedence |
| `.AddDestinationTransform(...)` | `[AfterMap]` or `Use=` | post-process in a named method |

### 2.3 Naming / depth / refs / enums

| Mapster | DwarfMapper | Note |
|---|---|---|
| `NameMatchingStrategy.Flexible` | `[DwarfMapper(NameConvention=Flexible)]` | snake/camel/Pascal/UPPER interchangeable |
| `NameMatchingStrategy.IgnoreCase` | `[DwarfMapper(CaseInsensitive=true)]` | ordinal-ignore-case; ambiguity → `DWARF010` |
| `.MaxDepth(n)` | `[DwarfMapper(MaxDepth=n)]` | catchable depth exception, never silent SO |
| `.PreserveReference(true)` | `[DwarfMapper(ReferenceHandling=Preserve)]` | full topology |
| unflattening | `[MapProperty(src, "A.B")]` | single-level dotted target |
| enum mapping (by value default) | `[DwarfMapper(EnumStrategy=ByValue)]` for parity | DwarfMapper default is **by name** (`DWARF015` on mismatch) |
| `ProjectToType<Dst>()` (EF) | `partial IQueryable<Dst> Project(IQueryable<S>)` | translatable subset — nested/collection/enum-cast/dotted supported; non-translatable conversions → `DWARF028` |
| `Mapster.Tool` source-gen | built-in (DwarfMapper is always source-gen) | no separate tool/codegen step |

---

## 3. From Riok.Mapperly 4.x

Mapperly is DwarfMapper's closest analogue — also a Roslyn source generator with `[Mapper] partial class`
and `partial Dst Map(Src)` methods. Migration is **nearly mechanical**, often less ceremony.

### 3.1 Core

| Mapperly | DwarfMapper | Before → after |
|---|---|---|
| `[Mapper]` | `[DwarfMapper]` | rename the attribute |
| `partial Dst Map(Src s);` | keep as-is, **or** `[GenerateMap<Src,Dst>]` | identical shape, or collapse to an attribute line |
| `partial void Map(Src s, Dst d);` (existing target) | `partial void Update(Src s, Dst d);` | same shape |
| `[UserMapping]` precedence | user method precedence (automatic) | a `Dst Convert(Src)` method is preferred over synthesis |

### 3.2 Member config

| Mapperly | DwarfMapper | Note |
|---|---|---|
| `[MapProperty(nameof(S.A), nameof(D.B))]` | identical `[MapProperty]` | direct |
| `[MapProperty("A.B.C", "D")]` (deep path) | identical dotted path | `DWARF043`/`DWARF044` for unknown/nullable-hop |
| `[MapPropertyFromSource]` / `[MapValue]` | `[MapValue]` (const + `Use=`) | type-checked `DWARF040–042` |
| `[MapperIgnoreTarget(nameof(D.X))]` | `[MapIgnore(nameof(D.X))]` | required by completeness gate |
| `[MapperIgnoreSource(nameof(S.X))]` | `[MapIgnoreSource("X")]` | with `RequiredMapping=Both` |
| `[MapperIgnoreObsoleteMembers]` | obsolete ctors excluded; otherwise `[MapIgnore]` | per-member explicitness |
| `Use=`/referenced method converter | `[MapProperty(src, tgt, Use=nameof(M))]` | same idea |

### 3.3 Mapper options

| Mapperly | DwarfMapper | Note |
|---|---|---|
| `PropertyNameMappingStrategy.CaseInsensitive` | `[DwarfMapper(CaseInsensitive=true)]` | `DWARF010` on ambiguity |
| `RequiredMappingStrategy.Target/Both` | `[DwarfMapper(RequiredMapping=Target/Both)]` | `DWARF039` (Info) for unconsumed source; `.editorconfig` to escalate |
| `EnumMappingStrategy.ByName/ByValue` + `EnumMappingIgnoreCase` | `[DwarfMapper(EnumStrategy=ByName/ByValue)]` | **default is ByName in both**; mismatch → `DWARF015` |
| strict lossy-conversion diagnostics | `[DwarfMapper(ImplicitConversions=false)]` | flips `DWARF038` suggestions into build errors (Mapperly-style strict) |
| `UseReferenceHandling` | `[DwarfMapper(ReferenceHandling=Preserve)]` | **DwarfMapper reconstructs full topology** (Mapperly's is partial) |
| `[MapDerivedType<DS,DD>]` | `[MapDerivedType<DS,DD>]` | same attribute name |
| `IQueryable` projection | `partial IQueryable<Dst> Project(…)` | translatable subset — nested/collection/enum-cast/dotted supported; non-translatable → `DWARF028` |
| `[MapEnum]`/`[MapEnumValue]` per-value remap | **partial** | differing enum member sets are a build error to resolve explicitly |
| `[UseMapper]`/`[UseStaticMapper]` (compose mappers) | call another mapper from a `Use=` method, or nest types | no first-class mapper-injection attribute |

### 3.4 What you gain moving from Mapperly

`[RoundTrip]` verification, the blittable/SIMD bulk-copy and `Vector.Widen` fast-paths, zero-alloc
`Span<T>` mapping, `IAsyncEnumerable` streaming, `[FlattenGraph]` graph degradation, `OnCycle=SetNull`
cycle-breaking, full-topology `Preserve`, and a *non-optional* completeness build-error gate — none of
which Mapperly offers. See [`COMPARISON.md`](COMPARISON.md#capability-matrix).

---

## 4. Deliberate non-goals (all libraries) — and the idiomatic replacement

| You relied on | Why it's a non-goal | Idiomatic DwarfMapper replacement |
|---|---|---|
| Inline lambdas in config | attributes can't carry closures | named methods (`Use=`, `When=`, hooks) |
| Ambient per-call context (`Items`, `State`) | reflection/dynamic-bag | extra typed method parameter (by-name to a dest member) |
| Open generics / assembly scanning | needs concrete types & reflection (breaks AOT) | one `[GenerateMap<closed>]` per used instantiation |
| Generic mapper **methods** (`Map<T>(…)`) or a generic mapper **class** (`partial class M<T>`) | a source generator cannot emit a body for an unbound type parameter (would not compile) | diagnosed loudly — `DWARF053` (generic method) / `DWARF054` (generic class); declare a closed `[GenerateMap<A,B>]` on a non-generic class |
| Implicit deep flattening by name-splitting | "magic" mislinking risk | explicit `[Flatten]` / dotted `[MapProperty]` |
| Per-call before/after, `SetMappingOrder` | runtime-only | declared `[BeforeMap]`/`[AfterMap]`; deterministic order |
| Deep merge into existing nested objects | ambiguous identity semantics | update **replaces** nested; merge by hand if truly needed |
| Silent float/decimal→int truncation | data-loss footgun | explicit `Use=` converter (opt-in, visible) |
| Turning the completeness check *off* | resilience-first stance | `[MapIgnore]` per intentional drop (auditable) |

Every non-goal is a **conscious resilience/AOT trade**, not a missing feature — each surfaces a diagnostic
or a typed alternative rather than failing silently.
