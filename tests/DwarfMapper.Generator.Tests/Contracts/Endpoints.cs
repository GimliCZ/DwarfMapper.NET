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
    ///     Marks the point inside a fixture's Src/Dst text where <see cref="BuildAtMember" /> splices a
    ///     Property/Field-site attribute, immediately ahead of a real member. A fixture whose text does not
    ///     contain this marker has no member slot — <see cref="BuildAt" /> returns <c>null</c> (NoSuchSite)
    ///     for that fixture's Property/Field cells rather than placing the attribute somewhere that isn't
    ///     actually a member, which is how <c>(Method, Property/Field)</c> ended up measuring identical
    ///     source under two different labels.
    /// </summary>
    public const string MemberSlotMarker = "/*__MEMBER_SLOT__*/";

    /// <summary>The DTO pair every endpoint maps between. Deliberately trivial — the matrix varies the
    /// ATTRIBUTE and the ENDPOINT, so the types must contribute no complications of their own.</summary>
    private const string Types = $$"""
        public sealed class Src { public int Id { get; set; } {{MemberSlotMarker}}public string? Name { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
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
    ///     <paramref name="memberAttribute" /> on the mapping method (or, for the registry, on the source
    ///     type) and <paramref name="classAttribute" /> on the mapper class.
    /// </summary>
    public static string Build(Endpoint endpoint, string memberAttribute = "", string classAttribute = "",
        string extraMembers = "", string options = "", string? types = null)
    {
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
            Endpoint.Registry => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }

                [MapTo(typeof(Dst))]
                public sealed class Src
                {
                    public int Id { get; set; }
                {{onMethod}}    public string? Name { get; set; }
                }
                """,

            // A member-form attribute has no mapping METHOD to land on here (there is none), so — like the
            // registry above — it goes on a member of the generated type instead of vanishing. Reuses
            // onMethod (computed from memberAttribute) for the same reason Registry does: the name predates
            // this endpoint needing a non-method slot, and renaming it is a larger diff than this fix.
            Endpoint.CoLocatedHost => $$"""
                using System.Linq;
                using DwarfMapper;
                namespace Demo;
                public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }

                [GenerateMap<Src, Dst>]
                {{(string.IsNullOrEmpty(classAttribute) ? "" : classAttribute + "\n")}}public sealed class Dst
                {
                    public int Id { get; set; }
                {{onMethod}}    public string? Name { get; set; }
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
    ///         four quite different things were producing that verdict, and two of them are limitations of
    ///         these templates rather than absences in the library. Reported per cause by
    ///         <c>SurfaceParityTests.The_cells_with_no_declaration_site_are_counted_by_cause</c>.
    ///     </para>
    /// </summary>
    /// <param name="types">The fixture in play; its member slot decides the Property/Field case.</param>
    public static string? SiteAbsenceReason(Endpoint endpoint, AttributeTargets site, string? types = null) =>
        site switch
        {
            AttributeTargets.Class when endpoint is Endpoint.Registry
                => "registry-has-no-mapper-class: intent lives on the source type",
            AttributeTargets.Method when endpoint is Endpoint.Registry or Endpoint.CoLocatedHost
                => "no-mapping-method: neither endpoint declares one to annotate",

            // Registry and CoLocatedHost place the attribute on a real DTO member of their own templates, so
            // the member site exists there regardless of the fixture.
            AttributeTargets.Property or AttributeTargets.Field
                when endpoint is Endpoint.Registry or Endpoint.CoLocatedHost => null,
            AttributeTargets.Property or AttributeTargets.Field
                => (string.IsNullOrEmpty(types) ? Types : types)
                   .Contains(MemberSlotMarker, StringComparison.Ordinal)
                    ? null
                    : "no-member-slot: the fixture in play carries no " + nameof(MemberSlotMarker),

            AttributeTargets.Struct or AttributeTargets.Constructor
                => "no-fixture-declares-one: a TEMPLATE limitation, not a structural absence — a struct "
                   + "fixture and a constructor-bearing fixture could exist and do not",

            AttributeTargets.Class or AttributeTargets.Method or AttributeTargets.Assembly => null,
            _ => $"unmodelled-site: the endpoint templates model no {site} site at all"
        };

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

        return site switch
        {
            AttributeTargets.Class
                => Build(endpoint, classAttribute: rendered, types: types, options: options ?? ""),
            AttributeTargets.Method
                => Build(endpoint, memberAttribute: rendered, types: types, options: options ?? ""),
            // Registry and CoLocatedHost already place memberAttribute on a real DTO member (Registry's own
            // template puts it on Src.Name; CoLocatedHost's puts it on Dst.Name) — that is a genuine member
            // site, not a stand-in for a missing method. Every OTHER endpoint has a mapping method, and
            // routing Property/Field there too would silently measure METHOD-placement semantics under a
            // Property/Field label — a real, different code path in the generator (confirmed: MapperExtractor
            // never reads MapIgnore/MapProperty off a Src/Dst member for the class-model endpoints; only
            // MapToGenerator's registry path does). BuildAtMember places it on an actual member instead; the
            // fixture is guaranteed to carry a slot, because SiteAbsenceReason returned above otherwise.
            AttributeTargets.Property or AttributeTargets.Field
                when endpoint is Endpoint.Registry or Endpoint.CoLocatedHost
                => Build(endpoint, memberAttribute: rendered, types: types, options: options ?? ""),
            AttributeTargets.Property or AttributeTargets.Field
                => BuildAtMember(endpoint, rendered, types, options),
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
    ///     Splices <paramref name="rendered" /> immediately ahead of a real DTO member, for the endpoints
    ///     whose Property/Field site must NOT fall back to the mapping method. Returns <c>null</c> — NoSuchSite
    ///     — when the fixture in play (<paramref name="types" />, or the default <see cref="Types" />) carries
    ///     no <see cref="MemberSlotMarker" />, rather than placing the attribute somewhere that silently
    ///     measures a different code path. Fixture text is arbitrary: a fixture written before this slot
    ///     existed has no marker to find, and that is an honest "no cell here," not a defect to paper over.
    /// </summary>
    private static string BuildAtMember(Endpoint endpoint, string rendered, string? types, string? options)
    {
        var t = string.IsNullOrEmpty(types) ? Types : types;
        var idx = t.IndexOf(MemberSlotMarker, StringComparison.Ordinal);

        // Unreachable: SiteAbsenceReason reports "no-member-slot" for exactly this fixture and BuildAt returns
        // before getting here. Restated at the point of use rather than trusted, because the alternative to
        // throwing is splicing the attribute somewhere that is not a member — which is how (Method,
        // Property/Field) once measured identical source under two different labels.
        if (idx < 0)
            throw new InvalidOperationException(
                $"BuildAtMember reached a fixture with no {nameof(MemberSlotMarker)}; SiteAbsenceReason should "
                + "have reported no-member-slot and BuildAt should have returned null.");

        var withMember = t[..idx] + rendered + " " + t[(idx + MemberSlotMarker.Length)..];
        return Build(endpoint, types: withMember, options: options ?? "");
    }
}
