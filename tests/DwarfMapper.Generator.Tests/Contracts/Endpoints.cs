// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>
///     The seven shapes through which DwarfMapper exposes a mapping. Each is a distinct code path with its own
///     resolver or emitter, which is precisely why an option can reach one and not another.
/// </summary>
public enum Endpoint
{
    /// <summary><c>partial TTarget Map(TSource)</c> — the ordinary create map.</summary>
    CreateMap,

    /// <summary><c>partial void Update(TSource, TTarget)</c> — map onto an existing instance.</summary>
    UpdateInto,

    /// <summary><c>partial IQueryable&lt;T&gt; Project(IQueryable&lt;S&gt;)</c> — provider-translated.</summary>
    Projection,

    /// <summary><c>partial void Map(ReadOnlySpan&lt;S&gt;, Span&lt;T&gt;)</c> — zero-alloc buffer fill.</summary>
    SpanMap,

    /// <summary><c>partial IAsyncEnumerable&lt;T&gt; Map(IAsyncEnumerable&lt;S&gt;)</c> — streaming.</summary>
    AsyncStream,

    /// <summary><c>[MapTo]</c> on the source type — the registry front door (a SEPARATE generator).</summary>
    Registry,

    /// <summary><c>[GenerateMap&lt;S,T&gt;]</c> on a plain class — the co-located host.</summary>
    CoLocatedHost
}

/// <summary>
///     Builds a compilable mapper for each <see cref="Endpoint" />, so a contract cell is
///     <c>(attribute, endpoint) → expectation</c> and nothing else. Keeping the shapes in one place is the
///     point: the defects this matrix exists to catch were all "the option was tested, the endpoint was
///     tested, the CELL was not", and that gap is only closable if adding an endpoint is a single edit here.
/// </summary>
public static class EndpointSources
{
    /// <summary>
    ///     Marks the point inside a fixture's Src/Dst text where <see cref="SpliceAtSlot" /> splices a
    ///     PROPERTY-site attribute, immediately ahead of a real property. A fixture whose text does not
    ///     contain this marker has no property slot — <see cref="BuildAt" /> returns <c>null</c> (NoSuchSite)
    ///     for that fixture's Property cells rather than placing the attribute somewhere that isn't
    ///     actually a property, which is how <c>(Method, Property/Field)</c> ended up measuring identical
    ///     source under two different labels.
    /// </summary>
    public const string PropertySlotMarker = "/*__PROPERTY_SLOT__*/";

    /// <summary>
    ///     The FIELD twin of <see cref="PropertySlotMarker" />, sitting immediately ahead of a real field.
    ///     <para>
    ///         It exists because one arm handled <c>Property</c> and <c>Field</c> together and discarded the
    ///         site: for the two elements legal on both (<c>MapProperty</c>, <c>MapIgnore</c>) every Field
    ///         cell was byte-identical to its Property cell at all seven endpoints, so a field-only divergence
    ///         was invisible while the matrix read as fully measured — the third instance on this branch of a
    ///         site being CLAIMED but not distinguishable from another. Gap G6.
    ///     </para>
    ///     <para>
    ///         Kept as a second marker rather than a second mechanism: the rules are the ones
    ///         <see cref="PropertySlotMarker" /> already established — a fixture that carries no marker for
    ///         the site under test yields <c>NoSuchSite</c> with a stated cause, never a fall-through to the
    ///         other site's slot.
    ///     </para>
    /// </summary>
    public const string FieldSlotMarker = "/*__FIELD_SLOT__*/";

    /// <summary>
    ///     The STRUCT twin of <see cref="PropertySlotMarker" />, sitting immediately ahead of a real
    ///     <c>struct</c> declaration that is a complete mapping source for the endpoint's <c>Dst</c>.
    ///     <para>
    ///         <c>[MapTo]</c> is legal on <c>Class</c> and on <c>Struct</c>, and its fourteen Struct-site
    ///         cells were unmeasured because no template declared a struct at all — counted honestly under
    ///         <c>no-fixture-declares-one</c>, but counted-and-unmeasured is still unmeasured (gap G5).
    ///     </para>
    ///     <para>
    ///         The struct is a SEPARATE type rather than <c>Src</c> made a value type: turning the pair's
    ///         source into a struct would change what every other cell in the matrix is measured against, so
    ///         the shape that answers one site's question would have silently rewritten a thousand other
    ///         answers. It carries the same three members as <c>Src</c> so <c>[MapTo(typeof(Dst))]</c> is a
    ///         COMPLETE mapping — a struct source missing a destination member would be <c>DWARF001</c>, and
    ///         a cell that can only ever be a refusal cannot show the element doing nothing.
    ///     </para>
    /// </summary>
    public const string StructSlotMarker = "/*__STRUCT_SLOT__*/";

    /// <summary>
    ///     The CONSTRUCTOR twin, sitting immediately ahead of a non-parameterless constructor of the
    ///     destination type — the only site <c>[DwarfMapperConstructor]</c> is legal on, and the other half
    ///     of gap G5's twenty-one unmeasured cells.
    ///     <para>
    ///         Whatever carries it must declare a parameterless constructor TOO. That is the whole question:
    ///         the default selection policy prefers the parameterless constructor and maps by object
    ///         initializer, so the annotated overload being chosen instead is a visible change in the emitted
    ///         text. A destination with only one constructor would be selected anyway and the directive could
    ///         not be told from doing nothing; a destination with two same-arity constructors would make the
    ///         BASELINE a <c>DWARF025</c> error, and a fixture that cannot compile without the element under
    ///         test can never show that element doing nothing.
    ///     </para>
    /// </summary>
    public const string ConstructorSlotMarker = "/*__CONSTRUCTOR_SLOT__*/";

    /// <summary>
    ///     Every site that is answered by SPLICING at a slot, with the marker it splices at and the cause
    ///     label it reports when no slot is present. The ONE list — <see cref="SiteAbsenceReason" />,
    ///     <see cref="BuildAt" /> and <see cref="SpliceAtSlot" /> all read it, so they cannot disagree about
    ///     which slot a site needs. Disagreeing there is precisely how a Field cell came to be answered with
    ///     the property slot, and reaching the Struct and Constructor sites down a second path would have
    ///     been the same defect a third time. Adding a site is one entry here plus a marker in the templates.
    /// </summary>
    private static readonly Dictionary<AttributeTargets, (string Marker, string Cause)> Slots =
        new Dictionary<AttributeTargets, (string Marker, string Cause)>
        {
            [AttributeTargets.Property] = (PropertySlotMarker, "no-property-slot"),
            [AttributeTargets.Field] = (FieldSlotMarker, "no-field-slot"),
            [AttributeTargets.Struct] = (StructSlotMarker, "no-struct-slot"),
            [AttributeTargets.Constructor] = (ConstructorSlotMarker, "no-constructor-slot")
        };

    /// <summary>
    ///     The slot a site splices at, or a throw for a site that splices at none. A closed lookup rather
    ///     than a two-way conditional: the old <c>site == Field ? … : PropertySlotMarker</c> form answered
    ///     "the property slot" for every site it had never heard of, which is the fall-through defect one
    ///     level up from the one it was written to prevent.
    /// </summary>
    public static string SlotMarkerFor(AttributeTargets site) =>
        Slots.TryGetValue(site, out var slot)
            ? slot.Marker
            : throw new ArgumentOutOfRangeException(nameof(site), site,
                "This site is not answered by splicing at a slot; it has no marker.");

    /// <summary>
    ///     The DTO pair every endpoint maps between. Deliberately trivial — the matrix varies the ATTRIBUTE
    ///     and the ENDPOINT, so the types must contribute no complications of their own.
    ///     <para>
    ///         <c>Tag</c> is a FIELD, and the only reason it exists is that a Field-site cell has to land on
    ///         one. It is declared on both sides so the pair still maps completely: a field on <c>Dst</c>
    ///         alone would be <c>DWARF001</c> in every baseline, and a fixture that cannot compile without the
    ///         element under test can never show that element doing nothing.
    ///     </para>
    ///     <para>
    ///         <c>Dst</c>'s two constructors exist for the Constructor site, which has exactly one legal
    ///         occupant (<c>[DwarfMapperConstructor]</c>) and nowhere to sit without them. The parameterless
    ///         one is what the default selection policy already picks, so every other cell in the matrix is
    ///         measured against the object-initializer mapping it was measured against before — verified by
    ///         re-measuring all seven ceilings. See <see cref="ConstructorSlotMarker" /> for why the annotated
    ///         overload has to be the OTHER one.
    ///     </para>
    ///     <para>
    ///         <c>SrcValue</c> is a STRUCT, and exists for the same reason one level along: a Struct-site
    ///         cell has to land on one. It is a third type rather than <c>Src</c> turned into a value type,
    ///         because the pair is what every other cell in the matrix is measured against — see
    ///         <see cref="StructSlotMarker" />. It is unannotated and unreferenced, so it contributes
    ///         nothing to any baseline until a case is spliced onto it.
    ///     </para>
    /// </summary>
    private const string Types = $$"""
        public sealed class Src { public int Id { get; set; } {{PropertySlotMarker}}public string? Name { get; set; } {{FieldSlotMarker}}public string? Tag; }
        public sealed class Dst { public Dst() { } {{ConstructorSlotMarker}}public Dst(int id, string? name, string? tag) { Id = id; Name = name; Tag = tag; } public int Id { get; set; } public string? Name { get; set; } public string? Tag; }
        {{StructSlotMarker}}public struct SrcValue { public int Id { get; set; } public string? Name { get; set; } public string? Tag; }
        """;

    /// <summary>
    ///     The flat pair a case with no <c>ProbeKey</c> is measured against. Exposed so a
    ///     <c>{Member}</c> placeholder in a declared argument list can be checked against the shape actually in
    ///     play — for an undemanding case that shape is this one, and leaving it uncheckable would mean the
    ///     gate covered only the cases that already carry a fixture.
    /// </summary>
    public static string DefaultTypes => Types;

    /// <summary>
    ///     Emits a full compilation unit for <paramref name="endpoint" />, placing
    ///     <paramref name="memberAttribute" /> on the mapping method and <paramref name="classAttribute" /> on
    ///     the mapper class. <see cref="Endpoint.Registry" /> and <see cref="Endpoint.CoLocatedHost" /> declare
    ///     no mapping method, so a non-empty <paramref name="memberAttribute" /> (or <c>extraMembers</c>) is
    ///     refused here — use <see cref="BuildAt" /> with a slot site instead.
    /// </summary>
    public static string Build(Endpoint endpoint, string memberAttribute = "", string classAttribute = "",
        string extraMembers = "", string options = "", string? types = null)
    {
        // Registry and CoLocatedHost declare no mapping method, and their templates therefore consume neither
        // memberAttribute nor extraMembers. Refusing the combination rather than dropping it on the floor:
        // silently discarding a caller's attribute is the exact defect
        // SurfaceProbeTests.BuildAt_never_returns_a_source_that_omits_the_rendered_attribute_text exists to
        // catch, and a caller that means "put this on a member" wants BuildAt's Property/Field site, which
        // routes through the slot markers.
        if (endpoint is Endpoint.Registry or Endpoint.CoLocatedHost
            && !(string.IsNullOrEmpty(memberAttribute) && string.IsNullOrEmpty(extraMembers)))
            throw new ArgumentException(
                $"{endpoint} declares no mapping method, so it can carry neither a method-level attribute nor "
                + "extra members. Use BuildAt with one of the slot sites (Property, Field, Struct, "
                + "Constructor), which splices at that site's marker.",
                string.IsNullOrEmpty(memberAttribute) ? nameof(extraMembers) : nameof(memberAttribute));

        var onMethod = string.IsNullOrEmpty(memberAttribute) ? "" : "    " + memberAttribute + "\n";
        var extras = string.IsNullOrEmpty(extraMembers) ? "" : "\n" + extraMembers + "\n";

        // Options go INSIDE [DwarfMapper(...)], not alongside it. Appending a second [DwarfMapper] would not
        // compile (AllowMultiple = false), so the option family needs its own slot rather than reusing
        // classAttribute, which exists for genuinely separate attributes like [MapIgnore].
        var dwarf = string.IsNullOrEmpty(options) ? "[DwarfMapper]" : $"[DwarfMapper({options})]";

        // A class-site case that IS [DwarfMapper(...)] SUBSTITUTES for the template's own [DwarfMapper]
        // rather than being appended beside it. [DwarfMapper] is AllowMultiple = false, so appending is
        // CS0579 — and the surface matrix then read all nineteen of that element's class-site cases as
        // NotCompilable at every method-based endpoint, i.e. the largest single element on the surface
        // passed the matrix without one of its cells ever being measured. Substituting keeps the mapper
        // class annotated exactly once, so the case under test is the annotation.
        var onClass = string.IsNullOrEmpty(classAttribute) ? dwarf
            : IsDwarfMapperAttribute(classAttribute) ? classAttribute
            : dwarf + "\n" + classAttribute;

        // Some options only become observable against a shape that triggers them (an enum for EnumStrategy, a
        // nested class for AutoNest). A caller may substitute the DTO pair; the default stays deliberately
        // trivial so the ordinary cells vary only the attribute and the endpoint.
        var t = string.IsNullOrEmpty(types) ? Types : types;

        return endpoint switch
        {
            Endpoint.CreateMap => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                {{t}}
                {{onClass}}
                public partial class M
                {
                {{onMethod}}    public partial Dst Map(Src s);{{extras}}
                }
                """,

            Endpoint.UpdateInto => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                {{t}}
                {{onClass}}
                public partial class M
                {
                {{onMethod}}    public partial void Update(Src s, Dst d);{{extras}}
                }
                """,

            Endpoint.Projection => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                {{t}}
                {{onClass}}
                public partial class M
                {
                {{onMethod}}    public partial IQueryable<Dst> Project(IQueryable<Src> q);{{extras}}
                }
                """,

            Endpoint.SpanMap => $$"""
                using System;
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                {{t}}
                {{onClass}}
                public partial class M
                {
                {{onMethod}}    public partial void MapSpan(ReadOnlySpan<Src> s, Span<Dst> d);{{extras}}
                }
                """,

            Endpoint.AsyncStream => $$"""
                using System.Collections.Generic;
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                {{t}}
                {{onClass}}
                public partial class M
                {
                {{onMethod}}    public partial IAsyncEnumerable<Dst> MapStream(IAsyncEnumerable<Src> s);{{extras}}
                }
                """,

            // The registry has no mapper class: intent lives on the SOURCE type, and a member-level attribute
            // goes on the member rather than a method. This asymmetry is exactly why it needs its own row.
            // ALL FOUR slots are declared inline, because this template ignores `types` entirely — the
            // mapping is declared BY the annotated type, so the pair cannot be substituted, and a slot the
            // fixture carries would never arrive here. Dst's two constructors are the shape the constructor
            // slot needs (see ConstructorSlotMarker); the parameterless one is declared first and still wins
            // the default selection policy, so every OTHER registry cell measures what it measured before.
            Endpoint.Registry => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                public sealed class Dst
                {
                    public Dst() { }
                    {{ConstructorSlotMarker}}public Dst(int id, string? name, string? tag) { Id = id; Name = name; Tag = tag; }
                    public int Id { get; set; }
                    public string? Name { get; set; }
                    public string? Tag;
                }

                {{StructSlotMarker}}public struct SrcValue { public int Id { get; set; } public string? Name { get; set; } public string? Tag; }

                [MapTo(typeof(Dst))]
                public sealed class Src
                {
                    public int Id { get; set; }
                    {{PropertySlotMarker}}public string? Name { get; set; }
                    {{FieldSlotMarker}}public string? Tag;
                }
                """,

            // A member-form attribute has no mapping METHOD to land on here (there is none), so — like the
            // registry above — it goes on a member of the generated type, through the same four slots.
            Endpoint.CoLocatedHost => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public int Id { get; set; } public string? Name { get; set; } public string? Tag; }

                {{StructSlotMarker}}public struct SrcValue { public int Id { get; set; } public string? Name { get; set; } public string? Tag; }

                [GenerateMap<Src, Dst>]
                {{(string.IsNullOrEmpty(classAttribute) ? "" : classAttribute + "\n")}}public sealed class Dst
                {
                    public Dst() { }
                    {{ConstructorSlotMarker}}public Dst(int id, string? name, string? tag) { Id = id; Name = name; Tag = tag; }
                    public int Id { get; set; }
                    {{PropertySlotMarker}}public string? Name { get; set; }
                    {{FieldSlotMarker}}public string? Tag;
                }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unhandled endpoint")
        };
    }

    /// <summary>
    ///     Whether <paramref name="rendered" /> is an application of <c>[DwarfMapper]</c> itself — as opposed
    ///     to a differently-named attribute that merely starts with the same letters, such as
    ///     <c>[DwarfMapperOptions]</c>. Matched on the exact bare form or on the open parenthesis that must
    ///     follow the name, so the prefix cannot swallow a longer sibling.
    /// </summary>
    private static bool IsDwarfMapperAttribute(string rendered)
    {
        var text = rendered.Trim();
        return string.Equals(text, "[DwarfMapper]", StringComparison.Ordinal)
               || text.StartsWith("[DwarfMapper(", StringComparison.Ordinal);
    }

    /// <summary>All endpoints, so the matrix and its growth ratchet iterate one list.</summary>
    public static IReadOnlyList<Endpoint> All { get; } = Enum.GetValues<Endpoint>();

    /// <summary>
    ///     WHY this endpoint has no such declaration site, or <c>null</c> when it has one.
    ///     <para>
    ///         The single source of truth for every <c>NoSuchSite</c> verdict — <see cref="BuildAt" /> consults
    ///         it first and never invents a null of its own, so the reason cannot drift from the behaviour it
    ///         describes. It exists because "137 cells have no declaration site" is not a reviewable statement:
    ///         several quite different things were producing that verdict, and the slot-shaped ones are
    ///         limitations of these templates rather than absences in the library. Reported per cause by
    ///         <c>SurfaceParityTests.The_cells_with_no_declaration_site_are_counted_by_cause</c>.
    ///     </para>
    ///     <para>
    ///         Every slot site is answered by ONE question — does the source this endpoint would build, with
    ///         the fixture in play, carry that site's marker? Asked of the BUILT source rather than of the
    ///         fixture text, which is what retires the separate <c>Registry</c>/<c>CoLocatedHost</c> arm that
    ///         used to answer "the site exists" for those two templates by hand: they declare their own DTO
    ///         pair and their own slots, so the built source already says so. Two arms that had to agree
    ///         became one that cannot disagree.
    ///     </para>
    /// </summary>
    /// <param name="types">The fixture in play; the slot it carries decides every slot site.</param>
    public static string? SiteAbsenceReason(Endpoint endpoint, AttributeTargets site, string? types = null)
    {
        if (Slots.TryGetValue(site, out var slot))
            return Build(endpoint, types: types).Contains(slot.Marker, StringComparison.Ordinal)
                ? null
                : $"{slot.Cause}: neither the fixture in play nor the endpoint template carries a "
                  + slot.Marker;

        return site switch
        {
            AttributeTargets.Class when endpoint is Endpoint.Registry
                => "registry-has-no-mapper-class: intent lives on the source type",
            AttributeTargets.Method when endpoint is Endpoint.Registry or Endpoint.CoLocatedHost
                => "no-mapping-method: neither endpoint declares one to annotate",

            AttributeTargets.Class or AttributeTargets.Method or AttributeTargets.Assembly => null,
            _ => $"unmodelled-site: the endpoint templates model no {site} site at all"
        };
    }

    /// <summary>
    ///     Whether this endpoint's template EMBEDS a caller-supplied fixture, or declares its own DTO pair and
    ///     ignores it.
    ///     <para>
    ///         A structural fact about the template, which is what <c>SurfaceProbe</c> needs in order to say
    ///         "the fixture never reached this endpoint". It used to ask whether the built source still
    ///         contained the fixture text verbatim — true only while no case spliced anything INTO that text.
    ///         The moment a slot site splices at a marker inside a fixture, the verbatim text is gone and a
    ///         perfectly-delivered fixture reads as never delivered, turning honest silence into
    ///         <c>Unasked</c>. The question was never about the string.
    ///     </para>
    /// </summary>
    public static bool DeliversFixture(Endpoint endpoint) =>
        endpoint is not (Endpoint.Registry or Endpoint.CoLocatedHost);

    /// <summary>
    ///     Places <paramref name="rendered" /> at the declaration site <paramref name="site" /> for this
    ///     endpoint, or returns null when the endpoint has no such site at all — a co-located host has no
    ///     mapping METHOD to annotate, and the registry front door has no mapper CLASS. Null is a distinct
    ///     answer from "the attribute did nothing": one means there was no cell, the other means the cell was
    ///     empty. Every null comes from <see cref="SiteAbsenceReason" />, so each one carries a stated cause.
    /// </summary>
    public static string? BuildAt(Endpoint endpoint, AttributeTargets site, string rendered,
        string? types = null, string? options = null)
    {
        ArgumentNullException.ThrowIfNull(rendered);

        if (SiteAbsenceReason(endpoint, site, types) is not null) return null;

        // Every slot site down ONE arm, and the arm does not discard the site: Slots decides WHICH marker, so
        // a Property cell lands on a property, a Field cell on a field, a Struct cell on a struct and a
        // Constructor cell on a constructor. Routing a member site to the mapping method would silently
        // measure METHOD-placement semantics under a member label — a real, different code path in the
        // generator (confirmed: MapperExtractor never reads MapIgnore/MapProperty off a Src/Dst member for
        // the class-model endpoints; only MapToGenerator's registry path does) — and routing Field to the
        // property slot measured the property path under a field label, which is the same defect one level
        // down (gap G6). Adding the Struct and Constructor sites beside them rather than through a parallel
        // path is what keeps that from happening a third time (gap G5).
        if (Slots.ContainsKey(site)) return SpliceAtSlot(endpoint, site, rendered, types, options);

        return site switch
        {
            AttributeTargets.Class
                => Build(endpoint, classAttribute: rendered, types: types, options: options ?? ""),
            AttributeTargets.Method
                => Build(endpoint, memberAttribute: rendered, types: types, options: options ?? ""),
            AttributeTargets.Assembly
                => InsertAssemblyAttribute(Build(endpoint, types: types, options: options ?? ""), rendered),
            _ => throw new InvalidOperationException(
                $"{site} at {endpoint} has no absence reason and no template. SiteAbsenceReason and BuildAt "
                + "must agree on which sites exist; one of them gained a case the other did not.")
        };
    }

    /// <summary>
    ///     Rewrites <paramref name="rendered" /> as one or more assembly-targeted attributes and splices them
    ///     between <paramref name="source" />'s <c>using</c> directives and its <c>namespace Demo;</c>
    ///     declaration. A <c>using</c> directive must precede every other element in a compilation unit, so
    ///     prepending the assembly attribute ahead of the source (as a naive concatenation would) is CS1529 —
    ///     every assembly-site cell would read <c>NotCompilable</c> for a broken harness template, not for
    ///     anything the compiler actually has an opinion about regarding the element itself.
    ///     <para>
    ///         <paramref name="rendered" /> may itself be more than one bracketed attribute, "\n"-joined (the
    ///         multiplicity axis renders <c>[Foo(1)]\n[Foo(2)]</c>). Trimming '[' / ']' off the ends of the
    ///         whole joined string — rather than off each line individually — corrupts every attribute but the
    ///         first and last, so each line is rewritten on its own.
    ///     </para>
    ///     <para>
    ///         Also adds <c>using Demo;</c> alongside the source's other <c>using</c> directives. An assembly
    ///         attribute necessarily sits ABOVE <c>namespace Demo;</c>, so a constructor argument like
    ///         <c>typeof(Dst)</c> is otherwise unqualified outside the very namespace <c>Dst</c> is declared
    ///         in — CS0246, not because the placement is illegal, but because the harness's own scoping hides
    ///         a type from an attribute argument that names it.
    ///     </para>
    /// </summary>
    private static string InsertAssemblyAttribute(string source, string rendered)
    {
        var assemblyForm = string.Join("\n", rendered
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => "[assembly: " + line.Trim().Trim('[', ']') + "]"));

        const string marker = "namespace Demo;";
        var idx = source.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException(
                $"Expected \"{marker}\" in the generated source; the assembly-attribute splice point moved.");
        return source[..idx] + "using Demo;\n" + assemblyForm + "\n" + source[idx..];
    }

    /// <summary>
    ///     Splices <paramref name="rendered" /> immediately ahead of the real declaration that
    ///     <paramref name="site" />'s slot marks — a property for <c>Property</c>, a field for <c>Field</c>, a
    ///     struct for <c>Struct</c>, a non-parameterless constructor for <c>Constructor</c>.
    ///     <para>
    ///         The splice happens on the BUILT source rather than on the fixture text, so the two endpoints
    ///         that declare their own DTO pair (<see cref="Endpoint.Registry" />,
    ///         <see cref="Endpoint.CoLocatedHost" />) go through one code path with the five that accept a
    ///         substituted one. The result is byte-identical to splicing into <paramref name="types" /> first,
    ///         because <see cref="Build" /> embeds that text verbatim and the markers appear nowhere else.
    ///     </para>
    /// </summary>
    private static string SpliceAtSlot(Endpoint endpoint, AttributeTargets site, string rendered,
        string? types, string? options)
    {
        var source = Build(endpoint, types: types, options: options ?? "");
        var marker = SlotMarkerFor(site);
        var idx = source.IndexOf(marker, StringComparison.Ordinal);

        // Unreachable: SiteAbsenceReason reports the missing slot for exactly this fixture and site, and
        // BuildAt returns before getting here. Restated at the point of use rather than trusted, because the
        // alternative to throwing is splicing the attribute at the OTHER site's slot — which is how
        // (Method, Property/Field) and then (Property, Field) each measured identical source under two
        // different labels.
        if (idx < 0)
            throw new InvalidOperationException(
                $"SpliceAtSlot reached a {endpoint} source with no {marker} for the {site} site; "
                + "SiteAbsenceReason should have reported the missing slot and BuildAt should have returned "
                + "null.");

        return source[..idx] + rendered + " " + source[(idx + marker.Length)..];
    }
}
