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

        // D6 and D7 were the exact complement of each other and both are NARROWED, not deleted: six of their
        // nine cells closed and three did not. One root cause for the six — THREE readers of one option, each
        // seeing a different part of it. The method endpoints read `ReadMapNullSkip(method) ?? classDefault`
        // and never consulted the pair-scoped form; the [GenerateMap] and auto-synthesized pairs consulted the
        // pair-scoped form and had no method to read; projection was handed the bare class value and saw
        // neither. Closed by folding all three into one MapperExtractor.ResolveNullSkip, most-specific-wins
        // (method, then pair, then the mapper/assembly policy), which every front door now calls — so
        // [MapNullSkip<Src, Dst>] is measured Honoured at CreateMap and UpdateInto (4 cells), and the method
        // form, which an element pair's shared mapper structurally cannot see, is refused element-wise as
        // DWARF090 with the pair-scoped remedy the message names (2 cells). Neither form was "wrong": both
        // declare AppliesTo = All and their XML docs describe one option at two scopes. The implementation had
        // three partial readers of it.
        //
        // What is left below is Projection, in both forms, and it is NOT a plumbing gap. Threading the
        // resolved value into ResolveProjectionMembers is one line and it was tried: the resolver already
        // refuses an untranslatable null-skip per affected member with DWARF028 — exactly what the class-level
        // option gets there — but DWARF028 is an ERROR, a blocking error suppresses emission, and all three
        // cells then read NotCompilable (CS8795, the R4 ordering defect) rather than Refused. That RAISES
        // NotCompilableCellCeiling 99 → 102 and closes a cell by relocating it into the population the parity
        // theory judges by nothing. Recorded here instead, because what it waits on is a decision: "do not
        // overwrite the destination's current value" has no referent in an object initializer that CONSTRUCTS
        // the destination, and omitting the member unconditionally is a different mapping.

        ["D6"] = new(
            "[MapNullSkip(true)] on a mapping method is honoured at CreateMap and UpdateInto, refused "
            + "element-wise as DWARF090, and SILENT at Projection. The option decides whether a null source "
            + "member overwrites the destination, and projection answers that question differently from .Map "
            + "on the same mapper without saying so. Re-measured after the reader unification: the SpanMap and "
            + "AsyncStream cells this finding also covered are now Refused and have been removed from it; the "
            + "Projection cell is what remains, and it remains because the honest refusal there is a blocking "
            + "DWARF028 whose CS8795 cascade would move the cell into the NotCompilable population rather than "
            + "out of it. Its class-scoped twin has the identical silence — see D7.",
            Findings + "#D6",
            [
                new DivergentCell("MapNullSkip", 0, "ctor(1)", AttributeTargets.Method,
                    SurfaceEndpoints.Projection)
            ]),

        ["D7"] = new(
            "[MapNullSkip<Src, Dst>(true)] on the mapper class is honoured at CreateMap, UpdateInto, SpanMap, "
            + "AsyncStream and CoLocatedHost, and SILENT at Projection. This finding was the exact complement "
            + "of D6 across six endpoints and is now the same single cell as D6, from the other scope: the two "
            + "forms are documented as one option written at two scopes and they now resolve through one "
            + "reader, so the only endpoint either fails to reach is the one whose translator cannot express "
            + "the option at all. \"Pair-scoped attributes do not reach method-declared pairs\" was never the "
            + "explanation — MapProperty<S,T>, MapValue<T>, MapIgnore<T> and MapConstructor<S,T> all act at "
            + "the method endpoints in the same run — and the four cells that claim proved wrong are removed "
            + "from this entry rather than re-argued.",
            Findings + "#D7",
            [
                new DivergentCell("MapNullSkip", 2, "ctor(1)", AttributeTargets.Class,
                    SurfaceEndpoints.Projection),
                new DivergentCell("MapNullSkip", 2, "×2", AttributeTargets.Class, SurfaceEndpoints.Projection)
            ]),

        ["D8"] = new(
            "[MapDerivedType] declares how a polymorphic source is dispatched. It acts at CreateMap and is "
            + "silent at UpdateInto, Projection, SpanMap and AsyncStream, in BOTH the open and the generic "
            + "form. A caller who has declared the derived-type mapping has declared it for the mapper, not "
            + "for one overload of it; on the other four the derived instance is mapped as its base and the "
            + "extra members are dropped without a word.",
            Findings + "#D8",
            [
                new DivergentCell("MapDerivedType", 0, "ctor(2)", AttributeTargets.Method, ElementWiseAndMore),
                new DivergentCell("MapDerivedType", 0, "×2", AttributeTargets.Method, ElementWiseAndMore),
                new DivergentCell("MapDerivedType", 2, "ctor(0)", AttributeTargets.Method, ElementWiseAndMore),
                new DivergentCell("MapDerivedType", 2, "×2", AttributeTargets.Method, ElementWiseAndMore)
            ]),

        ["D9"] = new(
            "[MapValue(\"Name\", …)] assigns a constant to a destination member, and Projection does not read "
            + "the directive at all. NARROWED to Projection: the SpanMap and AsyncStream cells this finding "
            + "also covered are now Refused as DWARF090, whose remedy — the pair-scoped [MapValue<TTarget>] — "
            + "was measured Honoured at both of those endpoints before the message prescribed it. What "
            + "remains is Projection, and it remains for a BOOKKEEPING reason that is worth stating rather "
            + "than dressing up. Threading [MapValue] into ResolveProjectionMembers was built and measured, "
            + "not argued: the ctor(2) cell — a constant silently not applied, the genuinely dangerous one — "
            + "does close (Refused, DWARF064), and the three malformed applications earn DWARF042/DWARF041, "
            + "which are Errors, so a blocking error suppresses emission and their cells read CS8795. "
            + "Measured NotCompilableCellCeiling 99 -> 102. That is the recorded R4 ordering defect and not "
            + "anything about this directive: the identical three renderings ALREADY read NotCompilable at "
            + "CreateMap and UpdateInto, which also corrects this entry's earlier claim that all four axes "
            + "are honoured there — only ctor(2) is (Refused, DWARF064 Info, output differing by the "
            + "assigned constant). NOT structurally inapplicable: the measurement proves a threaded "
            + "[MapValue] produces the right expression, and a constant assignment reads nothing from the "
            + "destination, so the object-initializer reasoning recorded for [MapNullSkip] does not reach "
            + "it. The one argument that closes this is waiting on R4.",
            Findings + "#D9",
            [
                new DivergentCell("MapValue", 0, "ctor(1)", AttributeTargets.Method, SurfaceEndpoints.Projection),
                new DivergentCell("MapValue", 0, "ctor(2)", AttributeTargets.Method, SurfaceEndpoints.Projection),
                new DivergentCell("MapValue", 0, "Use=\"probe\"", AttributeTargets.Method,
                    SurfaceEndpoints.Projection),
                new DivergentCell("MapValue", 0, "×2", AttributeTargets.Method, SurfaceEndpoints.Projection)
            ]),

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

        ["D12"] = new(
            "[Reinterpret(\"Data\")] forces a blit the automatic layout proof declines to make on its own. It "
            + "acts at CreateMap, UpdateInto and Projection and is silent at SpanMap and AsyncStream — the "
            + "two endpoints whose whole purpose is bulk element throughput, and therefore the two where a "
            + "caller reaching for a forced blit most expects it to apply. Re-measured for this record: the "
            + "originally filed evidence pointed the directive at a scalar; against an unmanaged array pair "
            + "it acts at three endpoints and the two silences stand.",
            Findings + "#D12",
            [
                new DivergentCell("Reinterpret", 0, "ctor(1)", AttributeTargets.Method,
                    SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream),
                new DivergentCell("Reinterpret", 0, "×2", AttributeTargets.Method,
                    SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream)
            ]),

        ["D13"] = new(
            "[ReverseMap] asks for the inverse mapping to be generated alongside the declared one. It acts at "
            + "CreateMap and is silent at UpdateInto, Projection, SpanMap and AsyncStream: a caller who "
            + "wrote it on an update or a projection gets no inverse and no explanation, and discovers the "
            + "absence at the call site of a method that was never generated.",
            Findings + "#D13",
            [
                new DivergentCell("ReverseMap", 0, "ctor(0)", AttributeTargets.Method, ElementWiseAndMore)
            ]),

        ["D14"] = new(
            "[MapCollectionKey(\"Items\", \"Id\")] declares the key by which an existing destination "
            + "collection is MERGED rather than rebuilt. It acts at UpdateInto — the endpoint it is chiefly "
            + "for — and is silent at CreateMap, Projection, SpanMap and AsyncStream. Re-measured for this "
            + "record: the originally filed evidence named members the fixture's `int` element type did not "
            + "have, so the directive could only ever apply to nothing; against a keyed element fixture it "
            + "acts at UpdateInto and the four silences stand.",
            Findings + "#D14",
            [
                new DivergentCell("MapCollectionKey", 0, "ctor(2)", AttributeTargets.Method,
                    SurfaceEndpoints.CreateMap | SurfaceEndpoints.Projection | SurfaceEndpoints.SpanMap
                    | SurfaceEndpoints.AsyncStream),
                new DivergentCell("MapCollectionKey", 0, "×2", AttributeTargets.Method,
                    SurfaceEndpoints.CreateMap | SurfaceEndpoints.Projection | SurfaceEndpoints.SpanMap
                    | SurfaceEndpoints.AsyncStream)
            ]),

        ["D15"] = new(
            "[GenerateWrapperMap(typeof(Dst))] on a mapper class is REFUSED at CoLocatedHost with DWARF067, "
            + "so the generator does read it and does have an opinion about where it is valid. On a "
            + "[DwarfMapper] class at any of the five mapper endpoints it produces nothing and says nothing: "
            + "neither the wrapper map the caller asked for nor the refusal the co-located host would have "
            + "given them. Whichever of the two answers is right, silence is not it.",
            Findings + "#D15",
            [
                new DivergentCell("GenerateWrapperMap", 0, "ctor(1)", AttributeTargets.Class, MapperEndpoints),
                new DivergentCell("GenerateWrapperMap", 0, "×2", AttributeTargets.Class, MapperEndpoints)
            ]),

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

    /// <summary>The five endpoints declared by a partial method on a <c>[DwarfMapper]</c> class.</summary>
    private const SurfaceEndpoints MapperEndpoints =
        SurfaceEndpoints.CreateMap | SurfaceEndpoints.UpdateInto | SurfaceEndpoints.Projection
        | SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream;

    /// <summary>Every mapper endpoint but the create map — the shape of a directive that acts only there.</summary>
    private const SurfaceEndpoints ElementWiseAndMore =
        SurfaceEndpoints.UpdateInto | SurfaceEndpoints.Projection | SurfaceEndpoints.SpanMap
        | SurfaceEndpoints.AsyncStream;

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
