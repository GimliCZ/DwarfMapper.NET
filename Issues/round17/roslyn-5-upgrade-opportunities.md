# Roslyn 4.14 → 5.0: what the bump actually unlocked, measured

**Status: analysis. Nothing here is a defect.** One actionable item (a diagnostic message), one regression-test
gap now closed, and a list of things deliberately *not* worth doing.

## Method

Release notes for a compiler API delta are hard to find and easy to misremember, and this repository does not
run on recall. The `microsoft.codeanalysis.common` and `microsoft.codeanalysis.csharp` **4.14.0, 5.0.0 and
5.6.0** `netstandard2.0` reference assemblies — the exact ones the generator compiles against — were already
in the local NuGet cache. A throwaway `MetadataLoadContext` dumper wrote each version's public type and member
surface to a text file; the files were diffed. The dumper was deleted afterwards.

Comparing *member names* (signature stripped) is the sound part of that instrument. See **Instrument limits**
at the end for what it cannot see.

## Result: 101 added, 36 removed — and it is almost all one feature

The 36 removals are a rename: `ExtensionDeclarationSyntax` → `ExtensionBlockDeclarationSyntax`. About 60 of
the 101 additions are that same syntax type re-appearing under its new name. What is left:

| New in 5.0 | Why it exists |
|---|---|
| `INamedTypeSymbol.IsExtension`, `.ExtensionParameter`, `.ExtensionGroupingName`, `.ExtensionMarkerName` | the symbol model for C# 14 extension blocks |
| `IMethodSymbol.AssociatedExtensionImplementation` | ditto |
| `IMethodSymbol.IsIterator` | general |
| 18 × `WellKnownMemberNames.*AssignmentOperatorName`, `SyntaxFacts.IsOverloadableCompoundAssignmentOperator` | user-defined compound assignment |
| `LanguageVersion.CSharp14`, `RuntimeCapability.RuntimeAsyncMethods` | version/capability probes |
| `CommandLineResource`, `EmitDifferenceOptions.EmitFieldRva` | compiler host / edit-and-continue — not applicable |

**Nothing was added to the incremental-generator pipeline.** No new `SyntaxValueProvider` members, nothing on
`IncrementalGeneratorInitializationContext`. Our pipeline is already at the current API ceiling:
`ForAttributeWithMetadataName` + `WithTrackingName` everywhere, with a single `CreateSyntaxProvider` for
ambient-facade *call-site* detection, where no attribute exists to match on and no newer API helps.

Two candidates checked because they would have been the interesting answers — both negative:

- **Interceptors.** `GetInterceptableLocation`, `GetInterceptorMethod`, `GetInterceptsLocationAttributeSyntax`
  and `InterceptableLocation` are **byte-identical across 4.14, 5.0 and 5.6**, and carry no `[Experimental]`
  in any of them. (The attribute detector works: it found 6 `[Experimental]` and 60 `[Obsolete]` members
  elsewhere in the 5.0 surface.) Interceptors were not unlocked by this bump. The language feature remains
  gated behind `<Features>InterceptorsNamespaces=…`, which is a separate question from API availability.
- **`AddEmbeddedAttributeDefinition`** is a **4.14** API, and is not applicable here regardless: our attributes
  ship in the `DwarfMapper` runtime package rather than being generated per-project, so the marker-attribute
  collision problem it solves does not arise.

### One constraint the bump created

`IMethodSymbol.ReduceExtensionMember` and `IPropertySymbol.ReduceExtensionMember` are **5.6-only**. Full
extension-member reduction is *not* available at the 5.0 floor declared in `README.md`. Reaching for it means
raising the floor again, which re-runs the CS9057 story recorded in the ISSUE-038 / Roslyn-bump commit.

## The finding that matters is not the API — it is the input

The generator's contract is over *consumer* code. Every new language version is a new set of shapes it has
never been asked about, and before this round the corpus contained **zero** instances of `field`-backed
properties, partial constructors, extension blocks, or user-defined compound assignment. "We still work on
.NET 10" was an assumption.

Probed, and the good news is that it is mostly true:

| Consumer shape | Behaviour | Emitted |
|---|---|---|
| `field`-backed property (source) | ✅ correct | `Name = s.Name` |
| `field`-backed property (destination) | ✅ correct | `Name = s.Name` |
| extension block in the compilation | ✅ no interference | built-in `__DwarfMap_FmtToStr_int_…` |
| `partial` constructor on the destination | ✅ correct | `new D(id: s.Id)` |
| user-defined `operator +=` on a mapped type | ✅ not mistaken for a conversion | `V = s.V` |
| **`Use = nameof(Ext.Spell)` naming an extension member** | ⚠️ `DWARF014` | — |

All six are now pinned in `tests/DwarfMapper.Generator.Tests/CSharp14ConsumerShapeTests.cs`. Five are
**regression guards** for behaviour that already worked; the sixth is pinned as *observed*.

### The one actionable item

`Use=` pointing at a C# 14 extension member is refused with **DWARF014 — "Conversion method not found."**

The refusal is loud and safe: no silent wrong mapping, which is the contract that matters. But the *reason* is
wrong. The method is plainly there, the user is looking straight at it, and they are told it does not exist.
That is precisely the failure mode the projection resolver already fixed once — DWARF028 replaced a generic
"no matching source member" with a message naming the real reason, because the generic text sends the reader
hunting for something that is present.

Roslyn 5.0 supplies exactly what a better answer needs: `INamedTypeSymbol.IsExtension` and `.ExtensionParameter`
on the containing type identify an extension block, and its members' shapes are readable from there.

Two candidate resolutions, and the choice is a design decision rather than a defect fix:

1. **Explain.** Keep refusing, but detect the extension member and say so: "`Spell` is an extension member;
   `Use=` resolves methods declared on the mapper or as ordinary statics." Cheap, honest, no new capability.
2. **Resolve.** Teach converter discovery to bind extension members whose receiver matches the source type.
   Larger: it widens the `Use=` contract, it interacts with `allMethods`/`autoCandidates` discovery, and full
   reduction support (`ReduceExtensionMember`) sits above the declared 5.0 floor.

Candidate 1 is the conservative reading and matches the DWARF028 precedent. Candidate 2 is the one a user who
has adopted C# 14 would actually want. Left open deliberately.

## What is NOT worth doing, and why

**C# 14 in the generator's own source.** Available already — `LangVersion=latest` plus the SDK pin gave us
C# 14 before the package bump. The bump unlocked *API*, not *language*; these are independent axes and
conflating them is the easy mistake here. Adoption should be targeted (e.g. `field` where a property exists
only to null-guard, null-conditional assignment where the code already reads `if (x is not null) x.Y = …`),
never a restyling pass — that would violate the house rule about matching surrounding code, and it would churn
a diff for no behavioural gain.

**C# 14 in the *emitted* code.** Expensive by construction: every Verify snapshot is invalidated, the
byte-determinism guarantee is touched, and the whole-solution gate re-runs. Worse, a consumer on `net10.0` may
still set `LangVersion` below 14, so emitting C# 14 constructs is a compatibility decision, not a cleanup.
Recommendation: do not.

**Span conversions in generator source.** The generator is `netstandard2.0` by hard requirement of the Roslyn
host and stays that way; first-class span conversions would need `System.Memory`, which is not referenced.

## Instrument limits

- The dumper compares **member names** soundly. Its **signature** comparison is swamped by assembly-version
  strings baked into reflection type names (`Version=4.14.0.0` → `5.0.0.0`), which is why the raw line diff
  reads 1353/1282 while the real delta is 101/36. Do not quote the raw numbers.
- Reflection type names **do not carry nullability annotations**, so the dumper cannot see a `string` →
  `string?` change. That check was done by the compiler instead: warnings-as-errors on a full solution build
  found exactly one such change (`SymbolDisplay.FormatPrimitive`), which is complete coverage for APIs we
  actually call — an annotation change on an API we never touch cannot affect us.
- The consumer-shape probe covers six shapes. C# 14 also shipped `nameof` over unbound generics, modifiers on
  simple lambda parameters, and partial events; none of them can appear as a *mapped member* or as a converter
  signature, which is why they were not probed. If that reasoning is wrong for partial events, that is the
  gap to look at next.
