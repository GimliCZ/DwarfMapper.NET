// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     One cell of the surface matrix a divergence covers, in the theory's own key.
///     <para>
///         The key is all five components on purpose. An entry keyed on
///         <c>(element, endpoint)</c> alone would excuse every AXIS and every SITE of that element there —
///         including a case that has not been written yet — which is an allowlist wearing a ratchet's
///         clothes. Naming the axis and the site means an entry covers exactly the cells someone measured.
///     </para>
/// </summary>
/// <param name="UsageName">The element as written in source, e.g. <c>MapProperty</c>.</param>
/// <param name="Arity">Generic arity, because five elements share a usage name with a generic twin.</param>
/// <param name="Axis">The case label — <c>ctor(1)</c>, <c>×2</c>, <c>Use="probe"</c>.</param>
/// <param name="Site">The declaration site the attribute was written at.</param>
/// <param name="Endpoints">The endpoints where THAT case is silent; one flag per covered cell.</param>
internal sealed record DivergentCell(
    string UsageName,
    int Arity,
    string Axis,
    AttributeTargets Site,
    SurfaceEndpoints Endpoints);

/// <summary>
///     One FINDING: a defect in the generator, the cells that prove it, and why a caller is entitled to
///     expect otherwise.
///     <para>
///         One entry per finding rather than per cell, deliberately. 93 per-cell rows would be an
///         inventory of a red build; eighteen findings are eighteen things a maintainer can pick up
///         and fix, each of which retires its whole group at once. That is not a claim about the grouping:
///         one arity check closed four of the original twenty-three in a single change, forty-nine cells at
///         once, and one reader taught to a second call site closed a fifth, twenty cells more.
///     </para>
/// </summary>
/// <param name="Why">
///     Why this is a divergence and not a structural limit, stated in terms of what a caller who wrote the
///     directive reasonably expects — followed by the measured reading that proves it. "The generator does
///     not do this" is not a reason; it is the observation the reason has to account for.
/// </param>
/// <param name="Section">Where the finding is written up, as <c>path#anchor</c>.</param>
/// <param name="Cells">Every cell this finding covers. Exact: see <see cref="DivergentCell" />.</param>
/// <param name="OptionName">
///     The <c>[DwarfMapper]</c> option this finding is about, when it is about one. Set for exactly the two
///     findings the OPTION matrix can also see, which consult this store by option name; null for the
///     findings only the surface matrix reaches.
/// </param>
internal sealed record Divergence(
    string Why,
    string Section,
    IReadOnlyList<DivergentCell> Cells,
    string? OptionName = null);

/// <summary>
///     Endpoint divergences that are known, recorded, and not yet fixed.
///     <para>
///         A ratchet, not an allowance. Any silent cell NOT named here fails the build, and any entry here
///         that stops being silent must be removed — so the list can only shrink, and it cannot quietly
///         re-permit a divergence that comes back. The difference between the two is the whole point and it
///         is enforced, not asserted: <c>SurfaceParityTests.Every_declared_divergence_is_still_a_divergence</c>
///         re-measures every cell named below and fails when one starts working. A row that merely permitted
///         a cell to fail would go on passing forever after the fix.
///     </para>
///     <para>
///         Kept in one place because three things consume it: the surface parity theory and the option parity
///         theory, both of which would otherwise fail the build, and the generated support matrix, which
///         renders these cells as <c>SILENT</c> so a reader sees the gap rather than a blank. Two copies would
///         drift, and a stale copy is how a fixed gap silently keeps its exemption.
///     </para>
///     <para>
///         <b>What does NOT belong here.</b> Three populations look like candidates and are not. A cell the
///         instrument never asked a question about is counted by
///         <c>The_cells_that_pose_no_question_are_declared_and_counted</c> — recording it here would ratify a
///         bug the generator never committed. A cell whose endpoint has no such declaration site is counted by
///         <c>The_cells_with_no_declaration_site_are_counted_by_cause</c>. And generated code that does not
///         COMPILE is not a silent divergence at all — it is a louder defect wearing the wrong label, counted
///         by <c>The_cells_the_compiler_rejects_are_counted</c>, which prints the CS ids for exactly that
///         reason.
///     </para>
/// </summary>
internal static class DeclaredDivergences
{
    private const string Findings = "Issues/round20/SURFACE-MATRIX-FINDINGS.md";

    /// <summary>
    ///     The findings, keyed by the id their write-up carries. 13 findings over 76 cells, every one
    ///     measured by <c>SurfaceParityTests</c> rather than reasoned about.
    ///     <para>
    ///         The maintainer's ruling that produced this list: record the divergences now, fix them
    ///         separately. Closing twenty-three generator gaps is its own body of work, and holding the
    ///         surface architecture unmerged until it is done would leave the matrix red — which is the state
    ///         in which nobody reads it.
    ///     </para>
    ///     <para>
    ///         The dominant shape when this file was written: thirteen of the eighteen findings then recorded
    ///         had a cell at <c>SpanMap</c> or <c>AsyncStream</c>, the ELEMENT-WISE endpoints. Those two map
    ///         the element pair through an auto-synthesized mapper, and a directive attached to the mapping
    ///         method does not reach it. Same root cause as the DWARF077 explicit-only gap, which was closed
    ///         for <c>AutoMatchMembers</c> alone. Whoever fixes it properly retires most of this file in one
    ///         change — and the two that have been closed so far both went the OTHER way, by refusing the
    ///         unreachable form as DWARF090 with the pair-scoped remedy named in the message (<c>D1</c>/<c>D2</c>,
    ///         then <c>D6</c>, which is why <c>D6</c> is no longer one of the thirteen).
    ///     </para>
    /// </summary>
    public static readonly Dictionary<string, Divergence> Reasons = new(StringComparer.Ordinal)
    {
        ["MaxDepth"] = new(
            "honoured at CreateMap and UpdateInto, silent at the span and async-stream endpoints. Found only "
            + "once a RECURSIVE fixture existed — a fixed-depth chain never exercises a depth BUDGET, so the "
            + "row read 'not probed' and claimed nothing. The element pair's depth guard comes from the "
            + "auto-synthesized mapper rather than the method model, so adding MaxDepth to the span/async "
            + "models (done, for consistency with their siblings) changes no output. Lower severity than it "
            + "sounds: the default bound of 64 still applies, so this is a tighter bound being ignored, not "
            + "unguarded recursion",
            Findings + "#MaxDepth",
            [new DivergentCell("DwarfMapper", 0, "MaxDepth=1", AttributeTargets.Class,
                SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream)],
            OptionName: "MaxDepth"),

        ["NullCollections"] = new(
            "honoured everywhere except Projection, which reads the option nowhere and always emits "
            + "`src.Items == null ? null : ...` — i.e. AsNull. Under the DEFAULT (AsEmpty) the runtime "
            + "produces an EMPTY collection, so .Map and .Project answer the same input differently and a "
            + "caller doing dto.Items.Length gets an NRE on the projection path only.\n\n"
            + "NOT fixed, and deliberately so — this one is a DESIGN DECISION, not a threading oversight "
            + "like the other ten. A refusal was implemented and reverted: it broke seven existing tests, "
            + "including ProjectionDeepTests.Projection_nullable_collection_member_gets_source_null_guard, "
            + "which asserts that ternary ON PURPOSE because Enumerable.Select(null!, ...) throws at query "
            + "evaluation time, and the ProjectionMatrixSafeTests capability tests. Refusing would mean "
            + "'you cannot project a nullable collection under default options', a capability regression "
            + "far larger than the divergence it closes.\n\n"
            + "Three candidate resolutions, for a maintainer to choose:\n"
            + "  (a) honour AsEmpty by emitting `== null ? new List<T>() : ...` — fixes it properly, but "
            + "needs someone to confirm the provider translates a constructed empty collection inside an "
            + "expression tree; failing inside a query at runtime is worse than the current divergence;\n"
            + "  (b) refuse with DWARF028 unless NullCollections = AsNull — loud and correct, but breaks the "
            + "default path for every nullable source collection;\n"
            + "  (c) declare projection's collection null-semantics to be AsNull by nature and document it, "
            + "leaving the code alone.\n\n"
            + "Pinned by the generated matrix rendering this cell SILENT, so it cannot be forgotten",
            Findings + "#NullCollections",
            [
                new DivergentCell("DwarfMapper", 0, "NullCollections=NullCollectionStrategy.AsNull",
                    AttributeTargets.Class, SurfaceEndpoints.Projection),
                new DivergentCell("DwarfMapperDefaults", 0, "NullCollections=NullCollectionStrategy.AsNull",
                    AttributeTargets.Assembly, SurfaceEndpoints.Projection)
            ],
            OptionName: "NullCollections"),

        // D1 and D2 lived here and are FIXED, not deleted for convenience. They were one shape — an UNSCOPED
        // member directive ([MapIgnore("X")], [MapProperty("X", "Y")]) written on a mapping method or its
        // class, which the element-wise endpoints drop because they resolve no members themselves: they map
        // the element pair through a mapper synthesized per (source, target) and shared by every route to it,
        // so only a directive that NAMES the pair can configure it. Closed by generalizing the DWARF077
        // explicit-only check into one gate both endpoints call, which now reports DWARF090 naming the
        // pair-scoped replacement text — [MapIgnore<TTarget>], [MapProperty<TSource, TTarget>] — forms
        // measured Honoured at both endpoints. Eight cells went Silent → Refused, which is where two of the
        // findings and eight of the cells below went. Propagation was considered and rejected for the reason
        // DWARF077 already gives: one method's unscoped directive would silently re-configure a nested
        // mapping another method owns.
        //
        // D16 was here too and is FIXED, by a DIFFERENT change: it was never propagation. CollectHooks
        // accepted the partial MAPPING METHOD as a hook whenever its signature fitted, and the emitted
        // update-into body therefore ended in `Update(s, d);` — unconditional recursion the matrix scored as
        // Honoured. A partial method with no implementing part has no body at all, so it is now refused as
        // DWARF091 before its signature is considered. See Issues/round20/SURFACE-MATRIX-FINDINGS.md.

        // D3, D4, D5 and D21 lived here and are FIXED, not deleted for convenience: all four were one shape —
        // a caller reaching for the wrong overload of a directive and the build saying nothing. D4's mirror at
        // the registry now reports DWARFR04 (the descriptor existed and checked a different arity); the other
        // three report DWARF088. Forty-nine cells went Silent → Refused, which is why the two ceilings below
        // dropped by exactly that many. See Issues/round20/SURFACE-MATRIX-FINDINGS.md for the resolutions.

        // D6 and D7 were the exact complement of each other and both are now CLOSED. One root cause: THREE
        // readers of one option, each seeing a different part of it. The method endpoints read
        // `ReadMapNullSkip(method) ?? classDefault` and never consulted the pair-scoped form; the [GenerateMap]
        // and auto-synthesized pairs consulted the pair-scoped form and had no method to read; projection was
        // handed the bare class value and saw neither. Folded into one MapperExtractor.ResolveNullSkip,
        // most-specific-wins (method, then pair, then the mapper/assembly policy), which every front door now
        // calls — including, as of this commit, the projection one, the fourth and last call site.
        //
        // Final readings, all measured: [MapNullSkip<Src, Dst>] Honoured at CreateMap, UpdateInto, SpanMap,
        // AsyncStream and CoLocatedHost; the method form Honoured at CreateMap and UpdateInto and refused
        // element-wise as DWARF090 (an element pair's mapper is shared by every route to it, so only a
        // pair-scoped directive can configure it), with the pair-scoped remedy the message names; and BOTH
        // forms refused at Projection as DWARF028 (behind CS8795) — the refusal the class-level
        // SkipNullSourceMembers has always got there, which the two scoped forms simply never reached.
        //
        // The Projection line was built and reverted once, at A6, and the reason it was reverted was an
        // instrument defect rather than a fact about the option: DWARF028 is an Error, a blocking error
        // suppresses emission, the partial projection method is left unimplemented, and SurfaceProbe read the
        // resulting CS8795 as "the compiler rejected the placement" — so landing it read as three cells moving
        // INTO the population the parity theory judges by nothing (NotCompilableCellCeiling 99 -> 102). R4 is
        // fixed; the same three cells now read Refused and NotCompilable did not move at all (10, unchanged).
        //
        // Neither form was ever "wrong": both declare AppliesTo = All and their XML docs describe one option
        // written at two scopes. The implementation had three partial readers of it, and now has one.

        // D8 closed 2026-08-17 (task A9a), and its evidence was PARTLY FALSE — the second entry in two tasks
        // to be measured wrong, after D10. It claimed the directive "acts at CreateMap … in BOTH the open and
        // the generic form". Measured before anything was changed: the OPEN form's two axes read
        // NotCompilable (CS8795) at CreateMap, because the flat DTO pair declares no hierarchy and the sampled
        // arguments were `typeof(Dst), typeof(Dst)` — a type not assignable to the method's source parameter,
        // hence DWARF035, an Error. The open form was not acting anywhere; it was being refused as nonsense.
        // The new `polymorphic-hierarchy` fixture poses the question, and against it the open form IS Honoured
        // at CreateMap (measured: NotCompilable -> Honoured, one cell out of NotCompilableCellCeiling's
        // population, 99 -> 98).
        //
        // All SIXTEEN cells now read Refused (DWARF092, a Warning): the gate A9a hoisted for D11 gained a
        // [MapDerivedType] arm, reading through ReadDerivedTypeAttributes — the create-map branch's own reader
        // — which now also carries WHICH of the two forms was written, so the message quotes back the syntax
        // the caller typed.
        //
        // One limitation is recorded rather than papered over: SurfaceCatalog.Render spells arity 2 as
        // <Src, Dst> for every element, so the GENERIC form's cells register the base as an arm of itself.
        // That measures "read at CreateMap, at no other endpoint", which is what its four cells claimed; it
        // does not measure polymorphic dispatch. Stated at the fixture and at the attribute.

        // D9 closed 2026-08-18 (task A12). [MapValue("Name", …)] assigns a constant to a destination member
        // and the projection resolver did not read the directive at all. The SpanMap and AsyncStream cells
        // closed first, as DWARF090 with the pair-scoped [MapValue<TTarget>] remedy measured Honoured at both
        // before the message prescribed it; the four Projection cells closed here.
        //
        // The threading was built and MEASURED at A8 and then reverted, for a bookkeeping reason rather than
        // a design one: DWARF042 (neither constant nor Use=) and DWARF041 (Use= naming no provider) are
        // Errors, a blocking error suppresses emission, and before R4 the resulting CS8795 read as
        // NotCompilable — so one cell closed and three moved into the population the parity theory judges by
        // nothing (99 -> 102). R4 is fixed. Final readings, all four Refused:
        //
        //   ctor(2)     => DWARF064 (Info)                      — the constant IS assigned, shadow reported
        //   ctor(1)     => DWARF042, DWARF064 (behind CS8795)
        //   Use="probe" => DWARF028, DWARF064 (behind CS8795)
        //   ×2          => DWARF042, DWARF064 (behind CS8795)
        //
        // Use= is the one part a query provider cannot take — it would have to call back into managed code
        // from inside an expression tree — so it is refused as DWARF028 rather than emitted, which is the
        // treatment [MapProperty(Use=)] already gets at this endpoint. Everything else about the directive is
        // translatable: a constant becomes a literal in the SELECT, and it reads nothing from the destination,
        // so the object-initializer argument that makes SkipNullSourceMembers untranslatable here (D6/D7)
        // never reached it. NOT structurally inapplicable, and the entry always said so.
        //
        // The validation is the create map's own, HOISTED rather than copied: TryValidateMapValueTarget is
        // one statement of the sequence (collision, ignore, constructor parameter, dotted path, unwritable
        // target, and the DWARF064 shadow report) that both resolvers call. A second copy bolted onto the
        // projection path would have closed the finding and left the shape.

        // D11 closed 2026-08-17 (task A9a). [FlattenGraph("Root", "Flat")] was honoured at CreateMap and
        // silent at the other four, where the destination collection was filled by ordinary direct mapping
        // instead — a walked graph on one overload of a mapper and a shallow copy on the next four. All
        // EIGHT cells are now Refused (DWARF092, a Warning, so no CS8795 cascade follows): the new
        // MapperExtractor.ReportCreateMapOnlyDirectives gate is called from the update-into, projection,
        // span-map and async-stream branches and reports every application the create map would have read,
        // through ReadFlattenGraphAttributes — the create map's own reader — rather than a second parse.
        //
        // The entry's evidence held exactly as filed; it is the one of A9a's three that did. Its own note
        // about the ×2 CreateMap cell being N4 rather than a silence is also still true: that cell reads
        // NotCompilable (CS8795 behind DWARF087) and is untouched by this commit.

        // D12 closed 2026-08-17 (task A9b). [Reinterpret("Data")] forces a blit the automatic layout proof
        // declines to make on its own, and both element-wise cells now read Refused (DWARF090) — the gate for
        // exactly this shape, a member directive a span or async-stream map cannot apply, with [MapIgnore],
        // [MapProperty], [MapNullSkip], [MapValue] and [Flatten] already on it. Read through
        // ReadReinterpretMembers, the reader both the create-map and the update-into branches resolve with.
        //
        // It is the FIRST arm of that gate with no pair-scoped twin, so its message ends differently: the
        // remedy is a DECLARED create map rather than a re-scoped attribute, measured before it was
        // prescribed — `d[__i] = Map(s[__i]);` and `Data = __DwarfBlit_…(s.Data)` (MemoryMarshal.Cast), where
        // the same fixture without it goes through a per-element numeric conversion helper. Same reading at
        // the async stream (`yield return Map(…)`).
        //
        // ONE CLAIM IN THE ENTRY WAS FALSE: "acts at CreateMap, UpdateInto and Projection". It does not act at
        // Projection. Only two branches call ReadReinterpretMembers, and neither is the projection one.
        // Measured, before anything was changed, against reinterpretable-array-member:
        //
        //   Reinterpret | ctor(1) | Method | Projection => UnhonouredButLoud
        //
        // — with and without the directive the run is byte-identical and carries the same DWARF028 ("narrowing
        // numeric conversion is not SQL-translatable"), which the int[] -> uint[] pair earns on its own. The
        // Projection cell was never one of this finding's, and it is left where it is: it is in
        // UnhonouredButLoudCellCeiling's population (re-measured 14, unchanged), and a diagnostic prescribed
        // for a cell nobody measured as a divergence is how a message comes to claim an endpoint it has not
        // been run against. The message therefore names the create map and the update-into and stops there.

        // D13 closed 2026-08-17 (task A9a), the third directive on the same DWARF092 gate. All four cells
        // read Refused. Its evidence held on the substance — the four silences are real, measured — and was
        // imprecise on the mechanism, which is worth stating because the entry's own sentence was the source:
        // [ReverseMap] does NOT "ask for the inverse mapping to be GENERATED", so nobody discovers "a method
        // that was never generated". It makes a SEPARATELY DECLARED inverse partial inherit the forward
        // method's simple renames with their ends swapped; the caller declares the inverse themselves, and a
        // missing one is DWARF052. Measured: with [ReverseMap] on a forward create map and an inverse create
        // map declared, the inverse emits `A = d.B` and the pair compiles; without it the same pair is
        // DWARF001 (Error). On an update-into with an inverse UPDATE declared, the renames are not inherited
        // and the pair is DWARF001 — the silence, exactly.
        //
        // Its CreateMap cell is NOT part of what closed. It reads NotCompilable (CS8795) because the endpoint
        // templates declare exactly ONE mapping method, so no inverse can exist there and DWARF052 (an Error)
        // always fires. That is a limitation of the templates, adjacent to A11's, and no fixture can lift it:
        // a fixture supplies TYPES, not a second method.

        // D14 closed 2026-08-17 (task A9b). [MapCollectionKey("Items", "Id")] acted at UpdateInto and was
        // silent at CreateMap, Projection, SpanMap and AsyncStream. All EIGHT of those cells are now Refused
        // (DWARF092, a Warning, so no CS8795 cascade follows): the create-map-only gate is generalized into
        // MapperExtractor.ReportDirectivesNotReadHere, called from ALL FIVE branches, where each arm names its
        // own HOME endpoint and is skipped there. [MapCollectionKey]'s home is the update-into; the
        // create-map-only trio's is the create map. Read through the new ReadCollectionKeys, hoisted out of
        // ApplyCollectionKeyUpserts so the refusal names exactly the applications the upsert path would act on.
        //
        // ITS EVIDENCE WAS FALSE, and in the same way D10's and D8's were — the fixture, not the generator.
        // The re-measurement note above this entry said "against a keyed element fixture it acts at
        // UpdateInto". It did not. keyed-collection-elements declared List<Item> on the source and
        // List<ItemDto> on the destination, and the v1 upsert requires the SAME element type, so at the one
        // endpoint this directive exists for the cell read NotCompilable (CS8795 behind DWARF074, "requires
        // the same element type on source and destination (v1)"). The fixture could not pose its question at
        // all. With one element type on both sides the upsert IS emitted — a Dictionary<int,int> index over
        // the existing d.Items, TryGetValue, replace-or-Add — and BOTH UpdateInto cells moved out of the
        // NotCompilable population: 98 -> 96.
        //
        // Also corrected, at DWARF074's descriptor: its comment listed "not an update-into method" as a case
        // it covered, and no call site ever implemented it — ApplyCollectionKeyUpserts is reached from the
        // update-into branch alone. That case is now DWARF092's, and could never have been DWARF074's once it
        // was written: DWARF074 is an Error, and an error suppresses the class's emission.

        // D15 closed 2026-08-17 (task A9b) as the new DWARF093, a Warning. All TEN cells read Refused.
        //
        // THE ENTRY'S MECHANISM WAS WRONG, and the wrong part is the sentence the fork rested on: "REFUSED at
        // CoLocatedHost with DWARF067, so the generator does read it and does have an opinion about where it
        // is valid." DWARF067 is an opinion about the WRAPPER TYPE — "the wrapper must be a generic type with
        // exactly one type parameter" — and has nothing to say about placement. It fired at CoLocatedHost only
        // because that template declares [GenerateMap<Src, Dst>] and SurfaceCatalog samples a Type argument as
        // typeof(Dst), which is not a single-parameter generic. At the five mapper endpoints the templates
        // declare a partial mapping METHOD and no [GenerateMap] at all, and ExpandWrapperMaps returned early
        // on the empty pair list — before the wrapper was validated, before anything. The silence was the
        // early return, not an opinion.
        //
        // THE FORK, and the answer: EMIT the wrapper map at the mapper endpoints, or REFUSE there too.
        // Refused, argued from what [GenerateWrapperMap] MEANS rather than from what is easier. It is defined
        // relative to [GenerateMap] — its own documentation opens "for every [GenerateMap<A, B>] declared on
        // the same [DwarfMapper] class" — and ExpandWrapperMaps is literally an append to that pair list. A
        // pair declared as a partial mapping method is a different mechanism with a different signature, and
        // four of the five endpoints are not create maps at all: an update-into, a projection, a span map and
        // an async-stream map have no W<A> -> W<B> shape to synthesize, so expanding them would hand the
        // caller a create map they never asked for. Emitting would have been feature work at ONE endpoint and
        // would still have left the other four needing this refusal.
        //
        // Reported BEFORE the wrapper's shape is validated, deliberately: with no pairs to expand even a
        // well-formed Envelope<T> expands nothing, so DWARF067 would send the caller to fix something that
        // changes no output — and it is an Error, which would have put these ten cells into
        // NotCompilableCellCeiling's population rather than out of the divergence one.
        //
        // Remedy MEASURED, including its sharp edge: [GenerateMap<Src, Dst>] beside [GenerateWrapperMap] emits
        // `Envelope<Dst> Map(Envelope<Src>)`, clean at an update-into — but [GenerateMap] also emits its own
        // `Dst Map(Src)`, so on a class that already declares `partial Dst Map(Src s)` over the same pair the
        // combination is CS0111. The message says so rather than sending a create-map caller into a
        // duplicate-member error. (That CS0111 arriving with no DWARF diagnostic of its own is filed as B27.)

        ["D17"] = new(
            "[assembly: DwarfMapperDefaults(RegisterCollectionShapes = false)] withholds the collection-shape "
            + "rows from the AMBIENT REGISTRY. Measured silent at Registry — and the ORIGINAL reason given for "
            + "the cell was wrong: the entry said the [MapTo] front door 'is the endpoint whose entire output "
            + "is registry rows', which is a pun on the word registry. Re-measured for A5: that front door "
            + "emits an extension class and NOTHING ELSE — no [assembly: DwarfProvidesMap], no "
            + "DwarfMapperRegistry.Register call, and therefore no collection shapes for this option to "
            + "withhold. So the cell does not close by plumbing, as D18 and D19 did: honouring it means giving "
            + "[MapTo] maps ambient registration first, which is a feature with manifest, DWARF061 and "
            + "DWARF063 consequences, and refusing it means complaining at every [MapTo] type in an assembly "
            + "whose house style was set for its [DwarfMapper] classes. It stays recorded rather than "
            + "reclassified: turning it into a StructurallyInapplicable row would RAISE that ceiling, and "
            + "'there is nothing here to configure' is a maintainer's call, not a way past this measurement. "
            + "(The same option's silence at UpdateInto, Projection, SpanMap and AsyncStream is NOT part of "
            + "this finding and is not a divergence: none of those four produces a registerable delegate at "
            + "all, which is recorded as a shape in StructurallyInapplicable.)",
            Findings + "#D17",
            [
                new DivergentCell("DwarfMapperDefaults", 0, "RegisterCollectionShapes=false",
                    AttributeTargets.Assembly, SurfaceEndpoints.Registry)
            ])

        // D18 and D19 were here — [assembly: DwarfMapperOptions(PublicExtensions = true)] overridden by the
        // registry's own accessibility choice, and [assembly: DwarfMapperDefaults(AutoMatchMembers = false)]
        // dropped by it. One root cause: MapToGenerator read no assembly-level configuration at all. Closed by
        // hoisting that lookup into AssemblyConfiguration, which all three front doors now call — the class
        // model for its defaults layer, the aggregate emitter for PublicExtensions, and the registry for both.
        // D19 refuses the by-name wire as the new DWARFR10; D18 made the registry's extension class internal
        // unless the assembly opts in, which is what the option's own documentation always said it was.
        //
        // D10 was here — [Flatten] silent at Projection, SpanMap and AsyncStream. Closed, and the record it
        // leaves behind is a correction rather than a fix report: the finding's own re-measurement note
        // claimed the directive was "honoured at CreateMap and UpdateInto" against a fixture with a real
        // nested member. It was not. Against `nested-pair` the flatten resolved its root, found leaf X, and
        // matched it to no destination member — that fixture's Dst still declares the NESTED member, so
        // there was nothing for a leaf to be pulled up into — and emitted BYTE-IDENTICAL output at all five
        // endpoints. The two cells that read Refused there read so because of DWARF044, a nullable-hop
        // WARNING about a hop nobody took. The finding asserted a divergence between endpoints that were
        // doing the same nothing.
        //
        // The fixture demand is now "flattenable-nested-member" (a struct root and a destination carrying
        // the LEAF), against which the directive genuinely acts, and the closure is a fix in two halves:
        // ResolveFlattenInfos is one walk both resolvers call, so Projection honours the flatten
        // (`__s.Child.X`, which a query provider translates) instead of discarding it; and the element-wise
        // endpoints refuse it as DWARF090, naming the dotted [MapProperty<Src, Dst>] remedy that was
        // measured Honoured there first. Final reading: Honoured / Honoured / Honoured / Refused / Refused
        // for ctor(1), and Refused (DWARF017, ambiguous flatten) / DWARF090 for ×2. No cell silent, none
        // moved into a population judged by nothing.
        //
        // D20 was here — the co-located host read no member-level directive, twenty cells across the
        // Property and Field sites. Closed by teaching MapperExtractor's co-located path to read those
        // forms off the host's own members, through the one MemberDirectives parser the [MapTo] registry
        // already used. D21 was here too; see the note where D3/D4/D5 were.
    };

    // `MapperEndpoints` — the five endpoints declared by a partial method on a [DwarfMapper] class — was here,
    // and it is gone for the reason `ElementWiseAndMore` below is: its last user did. D15 was the only entry
    // whose cells spanned all five, and task A9b closed it as DWARF093. Not kept "in case": an unused constant
    // in this file reads as a shape somebody is still recording, and the next entry that needs it can say so.

    // `ElementWiseAndMore` — every mapper endpoint but the create map, the shape of a directive that acts
    // only there — was here, and it is gone because its last user did. D8, D11 and D13 were the three
    // findings of that shape and task A9a closed all three on one gate (DWARF092). The constant is not kept
    // "in case": a shared name with no user is how the next entry of a different shape gets written against
    // it by accident.

    /// <summary>
    ///     The finding covering one cell, or <c>null</c>. Every component of the key must match: an entry
    ///     covers the cells someone measured and named, never a family of cells that happen to share an
    ///     element.
    ///     <para>
    ///         <c>Single</c>, not <c>First</c>. Two findings over one cell would hand the cell to whichever
    ///         the dictionary happened to order first, so deleting the other would leave it still excused —
    ///         an entry that has quietly stopped being load-bearing. <c>SurfaceParityTests</c> asserts the
    ///         same invariant separately; this states it at the point of use rather than trusting that gate
    ///         to have run, for the same reason <see cref="SurfaceCatalog.ClaimFor" /> does.
    ///     </para>
    /// </summary>
    public static KeyValuePair<string, Divergence>? For(string usageName, int arity, string axis,
        AttributeTargets site, SurfaceEndpoints endpoint)
    {
        var matches = Reasons
            .Where(entry => entry.Value.Cells.Any(cell =>
                string.Equals(cell.UsageName, usageName, StringComparison.Ordinal)
                && cell.Arity == arity
                && string.Equals(cell.Axis, axis, StringComparison.Ordinal)
                && cell.Site == site
                && (cell.Endpoints & endpoint) != 0))
            .ToList();

        return matches.Count == 0 ? null : matches.Single();
    }

    /// <summary>Every cell every finding declares, one flag expanded per row, for the ratchets and the gates.</summary>
    public static IEnumerable<(string Id, Divergence Divergence, DivergentCell Cell, SurfaceEndpoints Endpoint)>
        AllDeclaredCells()
    {
        foreach (var (id, divergence) in Reasons)
        foreach (var cell in divergence.Cells)
        foreach (var endpoint in Enum.GetValues<SurfaceEndpoints>())
            if (int.PopCount((int)endpoint) == 1 && (cell.Endpoints & endpoint) != 0)
                yield return (id, divergence, cell, endpoint);
    }

    /// <summary>
    ///     Whether some finding is about this <c>[DwarfMapper]</c> option. Consulted by the OPTION matrix,
    ///     which is keyed by option name rather than by cell, and which therefore sees only the subset of
    ///     findings that carry one.
    /// </summary>
    public static bool CoversOption(string optionName) =>
        Reasons.Values.Any(d => string.Equals(d.OptionName, optionName, StringComparison.Ordinal));

    /// <summary>Every option name some finding is about.</summary>
    public static IEnumerable<string> DeclaredOptions =>
        Reasons.Values.Where(d => d.OptionName is not null).Select(d => d.OptionName!);

    /// <summary>
    ///     Cells where the option CANNOT apply because the endpoint has no such surface — a different claim
    ///     from <see cref="Reasons" />, which is "it should apply and does not".
    ///     <para>
    ///         Shared with the generated matrix and with the SURFACE matrix, so all three render these as
    ///         structural rather than as a divergence. Keeping them apart matters: "there is nothing here to
    ///         configure" and "your configuration was discarded" look identical in the output and mean
    ///         opposite things.
    ///     </para>
    ///     <para>
    ///         Keyed by option name and endpoint with no element, because the reason is about the ENDPOINT's
    ///         shape and holds wherever the option is written: an update has no <c>source.ToTarget()</c> to
    ///         suppress whether the suppression was asked for on the mapper class or on the assembly. That is
    ///         why the surface matrix can consult it for <c>[DwarfMapperDefaults]</c> too, and it is also why
    ///         the surface matrix cannot express these as an <c>AppliesTo</c> narrowing: the claim belongs to
    ///         ONE OPTION of an option bag, and <c>AppliesTo</c> is per element.
    ///     </para>
    /// </summary>
    public static readonly Dictionary<(string Option, Endpoint Endpoint), string> StructurallyInapplicable =
        new()
        {
            // The convenience extension is the create-shaped `source.ToTarget()`. Only a create map produces
            // one, so on every other endpoint there is nothing for GenerateExtensions to suppress.
            [("GenerateExtensions", Endpoint.UpdateInto)] =
                "an update has no source.ToTarget() form to suppress",
            [("GenerateExtensions", Endpoint.Projection)] =
                "projection emits no convenience extension",
            [("GenerateExtensions", Endpoint.SpanMap)] =
                "the extension is generated per mapper, not per span overload",
            [("GenerateExtensions", Endpoint.AsyncStream)] =
                "the extension is generated per mapper, not per stream overload",

            // RegisterCollectionShapes governs what goes into the AMBIENT REGISTRY, not how any endpoint maps.
            // It is the same shape of option as GenerateExtensions above: it adds or withholds registration
            // rows, and only a create map produces a (source)->(dest) delegate the registry can hold. Update-
            // into mutates an existing instance (two arguments), projection emits an expression tree, and span
            // maps take a ref struct — none of the three is registerable at all, so there is nothing here for
            // the option to add or withhold.
            [("RegisterCollectionShapes", Endpoint.UpdateInto)] =
                "update-into is not ambient-registerable (two arguments, mutates an existing instance), so it "
                + "has no registration rows for this option to add",
            [("RegisterCollectionShapes", Endpoint.Projection)] =
                "projection emits an expression tree, not a runtime delegate the registry can hold",
            [("RegisterCollectionShapes", Endpoint.SpanMap)] =
                "Span<T> is a ref struct and cannot be boxed through the registry's Func<object, object>",
            [("RegisterCollectionShapes", Endpoint.AsyncStream)] =
                "an async-stream map is already a sequence map; wrapping it in another collection shape is not "
                + "a shape any call site asks for"
        };
}
