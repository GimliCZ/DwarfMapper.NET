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
    /// <summary>The DTO pair every endpoint maps between. Deliberately trivial — the matrix varies the
    /// ATTRIBUTE and the ENDPOINT, so the types must contribute no complications of their own.</summary>
    private const string Types = """
        public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
        """;

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
        var onClass = string.IsNullOrEmpty(classAttribute) ? dwarf : dwarf + "\n" + classAttribute;

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

    /// <summary>All endpoints, so the matrix and its growth ratchet iterate one list.</summary>
    public static IReadOnlyList<Endpoint> All { get; } = Enum.GetValues<Endpoint>();

    /// <summary>
    ///     Places <paramref name="rendered" /> at the declaration site <paramref name="site" /> for this
    ///     endpoint, or returns null when the endpoint has no such site at all — a co-located host has no
    ///     mapping METHOD to annotate, and the registry front door has no mapper CLASS. Null is a distinct
    ///     answer from "the attribute did nothing": one means there was no cell, the other means the cell was
    ///     empty.
    /// </summary>
    public static string? BuildAt(Endpoint endpoint, AttributeTargets site, string rendered,
        string? types = null)
    {
        ArgumentNullException.ThrowIfNull(rendered);

        return site switch
        {
            AttributeTargets.Class when endpoint is Endpoint.Registry
                => null, // the registry has no mapper class; intent lives on the source type
            AttributeTargets.Class
                => Build(endpoint, classAttribute: rendered, types: types),
            AttributeTargets.Method when endpoint is Endpoint.Registry or Endpoint.CoLocatedHost
                => null, // neither endpoint declares a mapping method to annotate
            AttributeTargets.Method
                => Build(endpoint, memberAttribute: rendered, types: types),
            AttributeTargets.Property or AttributeTargets.Field
                => Build(endpoint, memberAttribute: rendered, types: types),
            AttributeTargets.Assembly
                => InsertAssemblyAttribute(Build(endpoint, types: types), rendered),
            AttributeTargets.Struct or AttributeTargets.Constructor
                => null, // no fixture in the endpoint set declares one; add a shape before claiming the site
            _ => null
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
}
