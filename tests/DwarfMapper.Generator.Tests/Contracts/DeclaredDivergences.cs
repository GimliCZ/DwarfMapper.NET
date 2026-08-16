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
///         One entry per finding rather than per cell, deliberately. 162 per-cell rows would be an
///         inventory of a red build; twenty-three findings are twenty-three things a maintainer can pick up
///         and fix, each of which retires its whole group at once.
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
    ///     The findings, keyed by the id their write-up carries. 23 findings over 162 cells, every one
    ///     measured by <c>SurfaceParityTests</c> rather than reasoned about.
    ///     <para>
    ///         The maintainer's ruling that produced this list: record the divergences now, fix them
    ///         separately. Closing twenty-three generator gaps is its own body of work, and holding the
    ///         surface architecture unmerged until it is done would leave the matrix red — which is the state
    ///         in which nobody reads it.
    ///     </para>
    ///     <para>
    ///         The dominant shape, seventeen of the twenty-three: the ELEMENT-WISE endpoints. <c>SpanMap</c>
    ///         and <c>AsyncStream</c> map the element pair through an auto-synthesized mapper, and a directive
    ///         attached to the mapping method does not reach it. That is the same root cause as the DWARF077
    ///         explicit-only gap, which was closed for <c>AutoMatchMembers</c> alone; the matrix now shows it
    ///         across ten more attributes. Whoever fixes it properly retires most of this file in one change.
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

        ["D1"] = new(
            "A caller who writes [MapIgnore(\"Id\")] on the mapping method has excluded that member from THAT "
            + "mapper. The same text on the same method is honoured at CreateMap, UpdateInto and Projection "
            + "and changes nothing at the two element-wise endpoints, so one mapper drops the member on three "
            + "of its overloads and copies it on the other two — the caller cannot have meant that, and "
            + "nothing tells them. Measured: byte-identical output and no diagnostic at SpanMap and "
            + "AsyncStream, from both the method and the class site.",
            Findings + "#D1",
            [
                new DivergentCell("MapIgnore", 0, "ctor(1)", AttributeTargets.Method,
                    SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream),
                new DivergentCell("MapIgnore", 0, "ctor(1)", AttributeTargets.Class,
                    SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream)
            ]),

        ["D2"] = new(
            "[MapProperty(\"Id\", \"Name\")] on a mapping method is REFUSED with DWARF038 at CreateMap and "
            + "UpdateInto — the generator has an opinion about this directive and states it. At SpanMap and "
            + "AsyncStream the identical text on the identical method raises nothing and changes nothing: the "
            + "diagnostic that protects three overloads is simply absent from the other two, so a caller who "
            + "fixed their create map still ships the same mistake on the span path.",
            Findings + "#D2",
            [
                new DivergentCell("MapProperty", 0, "ctor(2)", AttributeTargets.Method,
                    SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream),
                new DivergentCell("MapProperty", 0, "×2", AttributeTargets.Method,
                    SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream)
            ]),

        ["D3"] = new(
            "The sharpest finding on the surface. [MapProperty(\"Id\", Use = \"probe\")] on a mapping method "
            + "names a converter that does not exist, and compiles to byte-identical output with no "
            + "diagnostic at every one of the five mapper endpoints — as do When, NullSubstitute and "
            + "StringFormat. A caller has named a converter, a predicate, a null substitute and a format "
            + "string, and the whole named-argument payload is discarded in silence. That it is a divergence "
            + "and not a structural limit is settled by the generator's own code: the pair-scoped "
            + "MapProperty<S,T> form raises DWARF014 / DWARF049 / DWARF050 for these exact named arguments, "
            + "so the refusals exist and this path never reaches them.",
            Findings + "#D3",
            [
                new DivergentCell("MapProperty", 0, "Use=\"probe\"", AttributeTargets.Method, MapperEndpoints),
                new DivergentCell("MapProperty", 0, "When=\"probe\"", AttributeTargets.Method, MapperEndpoints),
                new DivergentCell("MapProperty", 0, "NullSubstitute=\"probe\"", AttributeTargets.Method,
                    MapperEndpoints),
                new DivergentCell("MapProperty", 0, "StringFormat=\"probe\"", AttributeTargets.Method,
                    MapperEndpoints)
            ]),

        ["D4"] = new(
            "The two-argument [MapProperty(\"Id\", \"Name\")] is the METHOD form; on a source member at the "
            + "[MapTo] registry the form is one argument, naming the destination the annotated member "
            + "supplies. Writing the method form there is precisely the misuse "
            + "RegistryDiagnostics.MapPropertyArity was written for — the descriptor exists, and measured, it "
            + "does not fire: the cell is silent from both the Property and the Field site. A caller who used "
            + "the wrong overload gets a mapping that binds nothing and a build that says so nowhere.",
            Findings + "#D4",
            [
                new DivergentCell("MapProperty", 0, "ctor(2)", AttributeTargets.Property,
                    SurfaceEndpoints.Registry),
                new DivergentCell("MapProperty", 0, "ctor(2)", AttributeTargets.Field, SurfaceEndpoints.Registry)
            ]),

        ["D5"] = new(
            "The no-target [MapIgnore] is the REGISTRY form: the annotated member is the thing ignored. "
            + "Written on a mapping method or a mapper class it names nothing at all, and the class model "
            + "performs no arity check — so a caller who believes they have excluded a member has excluded "
            + "nothing and is told nothing. The mirror misuse at the registry has a descriptor (see D4), "
            + "which is what makes the absence here a gap rather than a shape. Measured silent at all five "
            + "mapper endpoints from the method site, and at those five plus CoLocatedHost from the class "
            + "site, in both the single and the doubled form.",
            Findings + "#D5",
            [
                new DivergentCell("MapIgnore", 0, "ctor(0)", AttributeTargets.Method, MapperEndpoints),
                new DivergentCell("MapIgnore", 0, "×2", AttributeTargets.Method, MapperEndpoints),
                new DivergentCell("MapIgnore", 0, "ctor(0)", AttributeTargets.Class,
                    MapperEndpoints | SurfaceEndpoints.CoLocatedHost),
                new DivergentCell("MapIgnore", 0, "×2", AttributeTargets.Class,
                    MapperEndpoints | SurfaceEndpoints.CoLocatedHost)
            ]),

        ["D6"] = new(
            "[MapNullSkip(true)] on a mapping method is honoured at CreateMap and UpdateInto and silent at "
            + "Projection, SpanMap and AsyncStream. The option decides whether a null source member "
            + "overwrites the destination; a caller who has asked for null-skipping on a mapper gets it on "
            + "two overloads and the opposite behaviour on three, from one declaration. Its own pair-scoped "
            + "twin proves the endpoints are reachable — see D7, which is this finding inverted.",
            Findings + "#D6",
            [
                new DivergentCell("MapNullSkip", 0, "ctor(1)", AttributeTargets.Method,
                    SurfaceEndpoints.Projection | SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream)
            ]),

        ["D7"] = new(
            "[MapNullSkip<Src, Dst>(true)] on the mapper class is honoured at SpanMap, AsyncStream and "
            + "CoLocatedHost and silent at CreateMap, UpdateInto and Projection — the exact complement of D6. "
            + "The two forms are documented as the same option written at two scopes, so between them a "
            + "caller can reach every endpoint and with either one alone reaches roughly half, silently. "
            + "\"Pair-scoped attributes do not reach method-declared pairs\" is not the explanation: "
            + "MapProperty<S,T>, MapValue<T>, MapIgnore<T> and MapConstructor<S,T> all act at the method "
            + "endpoints in the same run.",
            Findings + "#D7",
            [
                new DivergentCell("MapNullSkip", 2, "ctor(1)", AttributeTargets.Class,
                    SurfaceEndpoints.CreateMap | SurfaceEndpoints.UpdateInto | SurfaceEndpoints.Projection),
                new DivergentCell("MapNullSkip", 2, "×2", AttributeTargets.Class,
                    SurfaceEndpoints.CreateMap | SurfaceEndpoints.UpdateInto | SurfaceEndpoints.Projection)
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
            "[MapValue(\"Name\", …)] assigns a constant to a destination member. Honoured at CreateMap and "
            + "UpdateInto, silent at Projection, SpanMap and AsyncStream: the same mapper produces the "
            + "constant on two overloads and the auto-matched source value — or the type default — on three. "
            + "Re-measured for this record: the reading originally filed for D9 was a REFUSAL of a nonsense "
            + "argument (a string constant assigned to an int), which decided nothing; with an argument that "
            + "names a real member of the fixture the directive is genuinely honoured at Create/Update, and "
            + "the silence at the other three is a genuine divergence.",
            Findings + "#D9",
            [
                new DivergentCell("MapValue", 0, "ctor(1)", AttributeTargets.Method, ProjectionAndElementWise),
                new DivergentCell("MapValue", 0, "ctor(2)", AttributeTargets.Method, ProjectionAndElementWise),
                new DivergentCell("MapValue", 0, "Use=\"probe\"", AttributeTargets.Method,
                    ProjectionAndElementWise),
                new DivergentCell("MapValue", 0, "×2", AttributeTargets.Method, ProjectionAndElementWise)
            ]),

        ["D10"] = new(
            "[Flatten(\"Child\")] pulls a nested member's members up into the destination. Honoured at "
            + "CreateMap and UpdateInto, silent at Projection, SpanMap and AsyncStream, where the flattened "
            + "destination members are simply left at their defaults. Re-measured for this record: the "
            + "originally filed evidence named a SCALAR member, so \"acts at Create/Update (blocking)\" was a "
            + "refusal of nonsense; against a fixture with a real nested member the directive is honoured "
            + "there and the three silences stand.",
            Findings + "#D10",
            [
                new DivergentCell("Flatten", 0, "ctor(1)", AttributeTargets.Method, ProjectionAndElementWise),
                new DivergentCell("Flatten", 0, "×2", AttributeTargets.Method, ProjectionAndElementWise)
            ]),

        ["D11"] = new(
            "[FlattenGraph(\"Root\", \"Flat\")] walks a recursive source navigation into a flat destination "
            + "collection. Honoured at CreateMap and silent at UpdateInto, Projection, SpanMap and "
            + "AsyncStream — where the destination collection is filled by the ordinary direct mapping "
            + "instead, so the same declaration produces a walked graph on one overload and a shallow copy on "
            + "four. This finding was proposed for WITHDRAWAL and the withdrawal was wrong: the fixture's own "
            + "baseline did not compile, so the eight cells read UnhonouredButLoud and decided nothing. With "
            + "a baseline that compiles they are silent, and the finding stands — a fixture that cannot "
            + "compile without the element under test can never show that element doing nothing. NOTE: the "
            + "×2 case at CreateMap is NOT part of this finding; two identical directives make the generator "
            + "emit duplicate member initialization (CS1912), which is a defect in the OUTPUT and is recorded "
            + "as N4 rather than as a silence.",
            Findings + "#D11",
            [
                new DivergentCell("FlattenGraph", 0, "ctor(2)", AttributeTargets.Method, ElementWiseAndMore),
                new DivergentCell("FlattenGraph", 0, "×2", AttributeTargets.Method, ElementWiseAndMore)
            ]),

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

        ["D16"] = new(
            "[AfterMap] on the mapping method is honoured at UpdateInto and blocks the build at CreateMap, "
            + "Projection and AsyncStream — four endpoints where the caller learns something. At SpanMap "
            + "alone it compiles and the hook is never called, so a post-mapping fixup a caller relies on "
            + "runs for every element of an async stream and for none of a span.",
            Findings + "#D16",
            [
                new DivergentCell("AfterMap", 0, "ctor(0)", AttributeTargets.Method, SurfaceEndpoints.SpanMap)
            ]),

        ["D17"] = new(
            "[assembly: DwarfMapperDefaults(RegisterCollectionShapes = false)] withholds the collection-shape "
            + "rows from the AMBIENT REGISTRY, and the [MapTo] registry front door is the endpoint whose "
            + "entire output is registry rows — so it is the one endpoint where the option is most clearly "
            + "about something that exists there. Measured silent at Registry. (The same option's silence at "
            + "UpdateInto, Projection, SpanMap and AsyncStream is NOT part of this finding and is not a "
            + "divergence: none of those four produces a registerable delegate at all, which is recorded as a "
            + "shape in StructurallyInapplicable.)",
            Findings + "#D17",
            [
                new DivergentCell("DwarfMapperDefaults", 0, "RegisterCollectionShapes=false",
                    AttributeTargets.Assembly, SurfaceEndpoints.Registry)
            ]),

        ["D18"] = new(
            "[assembly: DwarfMapperOptions(PublicExtensions = true)] decides the accessibility of the "
            + "generated convenience extensions. The registry DOES emit an extension class, with a "
            + "public/internal choice of its own, and ignores this option — so a caller who set the assembly "
            + "default gets it honoured for their [DwarfMapper] classes and quietly overridden for their "
            + "[MapTo] types. This is the one endpoint deliberately kept in the element's claim when the "
            + "other four were narrowed away as shapes, precisely because there IS an extension here to "
            + "decide about.",
            Findings + "#D18",
            [
                new DivergentCell("DwarfMapperOptions", 0, "PublicExtensions=true", AttributeTargets.Assembly,
                    SurfaceEndpoints.Registry)
            ]),

        ["D19"] = new(
            "[assembly: DwarfMapperDefaults(AutoMatchMembers = false)] is a TRUST BOUNDARY: it says nothing "
            + "is mapped unless the caller said so. The mapper-level [DwarfMapper(AutoMatchMembers = false)] "
            + "acts at all five method endpoints, and the assembly-level form is honoured everywhere else and "
            + "dropped by the [MapTo] registry — so an assembly that has switched auto-matching off still has "
            + "every registry map auto-matching, silently. Half a trust boundary is worse than none, because "
            + "the developer believes they have one; this is the same shape as the DWARF077 gap and it is a "
            + "different endpoint.",
            Findings + "#D19",
            [
                new DivergentCell("DwarfMapperDefaults", 0, "AutoMatchMembers=false", AttributeTargets.Assembly,
                    SurfaceEndpoints.Registry)
            ]),

        ["D20"] = new(
            "At the co-located host the mapping is declared BY the annotated type — [GenerateMap<Src, Dst>] "
            + "sits on Dst — so a member of that type is part of the declaration, which is exactly why "
            + "MapProperty's own [DwarfSurfaceSite] keeps CoLocatedHost claimed for the member sites while "
            + "dropping the five mapper endpoints, where the DTOs are ordinary types the consumer may not "
            + "own. Measured: every member-level [MapProperty] and [MapIgnore] case, on both the Property and "
            + "the Field site, is byte-identical and silent there. One root cause and therefore one fix: "
            + "[GenerateMap<S,T>] is extracted by MapperExtractor, which reads these attributes off the class "
            + "or the method symbol only, and MapToGenerator's registry path is the sole reader of the "
            + "member-level forms.",
            Findings + "#D20",
            [
                new DivergentCell("MapProperty", 0, "ctor(1)", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapProperty", 0, "ctor(1)", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapProperty", 0, "ctor(2)", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapProperty", 0, "ctor(2)", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapProperty", 0, "×2", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapProperty", 0, "×2", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapProperty", 0, "Use=\"probe\"", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapProperty", 0, "Use=\"probe\"", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapProperty", 0, "When=\"probe\"", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapProperty", 0, "When=\"probe\"", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapProperty", 0, "NullSubstitute=\"probe\"", AttributeTargets.Property,
                    CoLocated),
                new DivergentCell("MapProperty", 0, "NullSubstitute=\"probe\"", AttributeTargets.Field,
                    CoLocated),
                new DivergentCell("MapProperty", 0, "StringFormat=\"probe\"", AttributeTargets.Property,
                    CoLocated),
                new DivergentCell("MapProperty", 0, "StringFormat=\"probe\"", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapIgnore", 0, "ctor(0)", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapIgnore", 0, "ctor(0)", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapIgnore", 0, "ctor(1)", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapIgnore", 0, "ctor(1)", AttributeTargets.Field, CoLocated),
                new DivergentCell("MapIgnore", 0, "×2", AttributeTargets.Property, CoLocated),
                new DivergentCell("MapIgnore", 0, "×2", AttributeTargets.Field, CoLocated)
            ]),

        ["D21"] = new(
            "The one-argument [MapProperty(\"Id\")] is the MEMBER-placement form, as its own summary states; "
            + "the documented method form takes two arguments. Written on a mapping method it resolves to "
            + "Source == Target, which is the identity binding auto-matching already produces — so the caller "
            + "used the wrong overload and the build accepts it at all five mapper endpoints without a word. "
            + "The reason this is a defect whichever way the generator reads it: if the directive is "
            + "discarded, a caller's explicit binding evaporated; if it is honoured, it was honoured as a "
            + "no-op the caller cannot have wanted. Refusal is the right answer either way, and the class "
            + "model has no arity check to give it — the same missing check as D5, and the mirror of the one "
            + "the registry has and does not fire (D4). Closure is observable only as a refusal, since "
            + "honouring it is byte-identical by construction.\n\n"
            + "Rejected route, so the next person does not retry it: giving this case "
            + "MapperOptions = \"AutoMatchMembers = false\" would make honouring visible, but it breaches two "
            + "shrink-only ceilings — the member-site cells at CoLocatedHost go Unasked because that template "
            + "carries no mapper class, and the Create/Update baselines stop compiling and land in "
            + "UnhonouredButLoud, which is the verdict-swallowing trap D11 was rescued from.",
            Findings + "#D21",
            [
                new DivergentCell("MapProperty", 0, "ctor(1)", AttributeTargets.Method, MapperEndpoints)
            ])
    };

    /// <summary>The five endpoints declared by a partial method on a <c>[DwarfMapper]</c> class.</summary>
    private const SurfaceEndpoints MapperEndpoints =
        SurfaceEndpoints.CreateMap | SurfaceEndpoints.UpdateInto | SurfaceEndpoints.Projection
        | SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream;

    /// <summary>Every mapper endpoint but the create map — the shape of a directive that acts only there.</summary>
    private const SurfaceEndpoints ElementWiseAndMore =
        SurfaceEndpoints.UpdateInto | SurfaceEndpoints.Projection | SurfaceEndpoints.SpanMap
        | SurfaceEndpoints.AsyncStream;

    /// <summary>Projection plus the two element-wise endpoints — a directive that acts on create and update.</summary>
    private const SurfaceEndpoints ProjectionAndElementWise =
        SurfaceEndpoints.Projection | SurfaceEndpoints.SpanMap | SurfaceEndpoints.AsyncStream;

    /// <summary>Spelled out so the twenty D20 cells read as a list of sites rather than of endpoints.</summary>
    private const SurfaceEndpoints CoLocated = SurfaceEndpoints.CoLocatedHost;

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
