// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>Marks a fixture field as the supply for a <c>[DwarfSurface(ProbeKey = ...)]</c> demand.</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
internal sealed class SurfaceProbeAttribute(string key) : Attribute
{
    public string Key { get; } = key;
}

/// <summary>
///     The one thing that genuinely cannot be derived: the SHAPE that makes a surface element or class-level
///     option observable. No amount of reflection over <c>AutoNest</c> yields "you need a nested class pair
///     here". These are inputs to the experiment, not a description of the API — and a demand whose key has
///     no fixture is reported rather than quietly assumed fine.
///     <para>
///         Every fixture must declare types named <c>Src</c> and <c>Dst</c>, because
///         <c>EndpointSources.Build</c> writes method signatures against those names.
///     </para>
/// </summary>
internal static class SurfaceFixtures
{
    // Every field below is read exclusively through GetFields()/GetValue() in All, not by name from ordinary
    // code — that is the whole point of the [SurfaceProbe] indirection. Static analysis cannot see across a
    // reflection boundary, so CS0414 ("assigned but never used"), CA1802 ("could be const" — it cannot: a
    // const field carries no instance to attribute-scan for, GetValue(null) needs a real static field), CA1823
    // and IDE0051 ("unused private member") all fire on every entry here. Disabled for this block only, with
    // the reflective consumer sitting three lines below as the reason.
#pragma warning disable CS0414, CA1802, CA1823, IDE0051
    [SurfaceProbe("nested-pair")]
    private static readonly string NestedPair = """
        public sealed class Inner { public int X { get; set; } }
        public sealed class InnerDto { public int X { get; set; } }
        public sealed class Src { public int Id { get; set; } public Inner Child { get; set; } = new(); }
        public sealed class Dst { public int Id { get; set; } public InnerDto Child { get; set; } = new(); }
        """;

    [SurfaceProbe("internal-member")]
    private static readonly string InternalMember = """
        public sealed class Src { public int Id { get; set; } internal string? Name { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
        """;

    [SurfaceProbe("snake-case-member")]
    private static readonly string SnakeCaseMember = """
        public sealed class Src { public int Id { get; set; } public string? user_name { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string? UserName { get; set; } }
        """;

    [SurfaceProbe("case-mismatched-member")]
    private static readonly string CaseMismatchedMember = """
        public sealed class Src { public int Id { get; set; } public string? name { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
        """;

    [SurfaceProbe("obsolete-member")]
    private static readonly string ObsoleteMember = """
        public sealed class Src { public int Id { get; set; } [System.Obsolete] public string? Name { get; set; } }
        public sealed class Dst { public int Id { get; set; } [System.Obsolete] public string? Name { get; set; } }
        """;

    [SurfaceProbe("nullable-source-nonnull-target")]
    private static readonly string NullableSourceNonNullTarget = """
        public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string Name { get; set; } = ""; }
        """;

    [SurfaceProbe("nullable-value-to-nonnull")]
    private static readonly string NullableValueToNonNull = """
        public sealed class Src { public int Id { get; set; } public int? Val { get; set; } }
        public sealed class Dst { public int Id { get; set; } public int Val { get; set; } }
        """;

    [SurfaceProbe("unconsumed-source-member")]
    private static readonly string UnconsumedSourceMember = """
        public sealed class Src { public int Id { get; set; } public string? Name { get; set; } public int Extra { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
        """;

    // Two enums whose members are declared in DIFFERENT order, so ByName and ByValue genuinely disagree
    // about the result. Same-order enums would map identically under both strategies and the cell would
    // read "no effect" while the option was working perfectly.
    [SurfaceProbe("divergent-order-enums")]
    private static readonly string DivergentOrderEnums = """
        public enum SrcKind { A, B }
        public enum DstKind { B, A }
        public sealed class Src { public int Id { get; set; } public SrcKind Kind { get; set; } }
        public sealed class Dst { public int Id { get; set; } public DstKind Kind { get; set; } }
        """;

    // An enum member whose [Description] differs from its identifier, mapped to a string — otherwise the
    // two settings describe the same mapping and the option reads as having no effect.
    [SurfaceProbe("described-enum-to-string")]
    private static readonly string DescribedEnumToString = """
        public enum Kind { [System.ComponentModel.Description("in-progress")] InProgress, Done }
        public sealed class Src { public int Id { get; set; } public Kind Kind { get; set; } }
        public sealed class Dst { public int Id { get; set; } public string Kind { get; set; } = ""; }
        """;

    // A NULLABLE collection member: the option decides what a null source collection becomes.
    // DIFFERENT collection types, so the mapper must REBUILD rather than assign the reference across.
    // With List<int> on both sides it is a straight copy and the null policy never comes up, which read
    // as "the option does nothing" when the fixture simply never asked it anything.
    [SurfaceProbe("nullable-collection-rebuild")]
    private static readonly string NullableCollectionRebuild = """
        public sealed class Src { public int Id { get; set; } public System.Collections.Generic.List<int>? Items { get; set; } }
        public sealed class Dst { public int Id { get; set; } public int[]? Items { get; set; } }
        """;

    // Shared by two demands, each carrying its own original reasoning verbatim:
    //
    // OnCycle — A self-referencing graph, so there is a cycle for the policy to have an opinion about.
    //
    // MaxDepth — Nesting deeper than the probe's MaxDepth (see ProbeOverrides), so the budget actually binds.
    // A RECURSIVE graph. A fixed three-level chain does not exercise a depth budget — the generator
    // simply walks it — whereas a self-referencing type forces depth tracking, which is what MaxDepth
    // bounds.
    [SurfaceProbe("recursive-graph")]
    private static readonly string RecursiveGraph = """
        public sealed class Node { public int Id { get; set; } public Node? Next { get; set; } }
        public sealed class NodeDto { public int Id { get; set; } public NodeDto? Next { get; set; } }
        public sealed class Src { public int Id { get; set; } public Node? Root { get; set; } }
        public sealed class Dst { public int Id { get; set; } public NodeDto? Root { get; set; } }
        """;

    // A NARROWING pair. Widening (int->long) is allowed regardless, so it cannot distinguish the option;
    // narrowing is what ImplicitConversions actually gates, by escalating DWARF038 to an error.
    [SurfaceProbe("narrowing-conversion")]
    private static readonly string NarrowingConversion = """
        public sealed class Src { public int Id { get; set; } public long Val { get; set; } }
        public sealed class Dst { public int Id { get; set; } public int Val { get; set; } }
        """;

    [SurfaceProbe("shared-reference-graph")]
    private static readonly string SharedReferenceGraph = """
        public sealed class Inner { public int X { get; set; } }
        public sealed class InnerDto { public int X { get; set; } }
        public sealed class Src { public int Id { get; set; } public Inner? Child { get; set; } }
        public sealed class Dst { public int Id { get; set; } public InnerDto? Child { get; set; } }
        """;

    // A collection whose ELEMENT type carries a key member, which is what a key-based upsert merges on.
    // The nullable-collection-rebuild fixture next door has List<int> elements: they have no members at all,
    // so [MapCollectionKey] could only ever name something that did not exist, and the resulting silence said
    // nothing about upsert support.
    //
    // The SAME element type on both sides, and that is the whole question rather than a tidying choice. The
    // v1 upsert requires it — MapperExtractor.Flatten.cs refuses a differing element type as DWARF074, "member
    // 'Items' requires the same element type on source and destination (v1)" — so the earlier Item/ItemDto
    // pair could only ever be refused. Measured before it was changed: at UpdateInto, the endpoint this
    // directive exists for, the Item/ItemDto fixture read NotCompilable (CS8795 behind that DWARF074), so the
    // finding built on it (D14) asserted "it acts at UpdateInto" against a fixture in which it demonstrably
    // could not. With one element type the upsert IS emitted — a Dictionary<int,int> index over the existing
    // d.Items, TryGetValue, replace-or-Add — which is the reading the four silences are worth stating against.
    // Same shape as D10's and D8's false evidence: the fixture, not the generator.
    [SurfaceProbe("keyed-collection-elements")]
    private static readonly string KeyedCollectionElements = """
        public sealed class Item { public int Id { get; set; } public string? Label { get; set; } }
        public sealed class Src { public int Id { get; set; } public System.Collections.Generic.List<Item> Items { get; set; } = new(); }
        public sealed class Dst { public int Id { get; set; } public System.Collections.Generic.List<Item> Items { get; set; } = new(); }
        """;

    // A RECURSIVE navigation on the source and a FLAT collection on the destination — the two halves a graph
    // flatten needs. The nested-pair fixture has a single non-recursive complex member and no collection
    // anywhere, so [FlattenGraph] had neither a graph to walk nor anywhere to put the result.
    //
    // Src.Flat exists ONLY so the BASELINE compiles. Without it, Dst.Flat has no source member, the baseline
    // is DWARF001 (Error) and emits nothing — so a [FlattenGraph] that did nothing produced byte-identical
    // (empty) output beside an error and read UnhonouredButLoud, which passes a claimed endpoint AND an
    // unclaimed one. The fixture would have swallowed the verdict it was built to produce: a fixture that
    // cannot compile without the element under test can never show that element doing nothing. Here the
    // directive's job is to REDIRECT Dst.Flat's source from the direct collection to a walk of Root's graph,
    // which is a question with a visible answer either way.
    //
    // Contrast the case-mismatched-member / snake-case-member / internal-member fixtures, whose baselines are
    // DWARF001 BY DESIGN: there the error is precisely what CaseInsensitive / NameConvention / AllowNonPublic
    // exist to remove, so the element under test is expected to clear it and the cell reads Honoured.
    [SurfaceProbe("graph-navigation-to-flat-collection")]
    private static readonly string GraphNavigationToFlatCollection = """
        public sealed class Node { public int Id { get; set; } public System.Collections.Generic.List<Node> Children { get; set; } = new(); }
        public sealed class NodeDto { public int Id { get; set; } }
        public sealed class Src { public int Id { get; set; } public Node? Root { get; set; } public System.Collections.Generic.List<Node> Flat { get; set; } = new(); }
        public sealed class Dst { public int Id { get; set; } public System.Collections.Generic.List<NodeDto> Flat { get; set; } = new(); }
        """;

    // A complex source member whose LEAF is what the destination declares. [Flatten] pulls a nested member's
    // members UP, so a destination that still declares the nested member has nothing for it to pull up: against
    // the nested-pair fixture (Dst.Child of type InnerDto) the directive resolved the root, found leaf X,
    // matched it to no destination member, and emitted BYTE-IDENTICAL output at all five endpoints. That
    // reading was filed for four rounds as "honoured at CreateMap and UpdateInto" — it was DWARF044, a
    // nullable-hop WARNING about a hop nobody took, and the cell read Refused because a diagnostic appeared,
    // not because the directive did anything. Correcting it is what closed D10.
    //
    // Baseline is DWARF001 BY DESIGN, the same shape as the case-mismatched-member / snake-case-member /
    // internal-member fixtures: Dst.X has no direct source and the flatten is precisely what supplies it, so
    // the element under test is expected to clear the error and the cell reads Honoured. It cannot be avoided
    // — giving Src its own X makes the direct member win (measured: byte-identical output, because a flatten
    // leaf is consulted only where no direct source matches), which is a fixture that cannot pose the question.
    //
    // The root is a STRUCT deliberately. A reference-typed root earns DWARF044 in the nullable-oblivious test
    // compilation, and a warning added by the case under test short-circuits SurfaceProbe.Classify into Refused
    // before it ever compares output — so the honest Honoured reading would have been masked by an incidental
    // hop warning, which is the very confusion this fixture replaces.
    [SurfaceProbe("flattenable-nested-member")]
    private static readonly string FlattenableNestedMember = """
        public struct Inner { public int X { get; set; } }
        public sealed class Src { public int Id { get; set; } public Inner Child { get; set; } }
        public sealed class Dst { public int Id { get; set; } public int X { get; set; } }
        """;

    // Two UNMANAGED arrays of the same width and different element types. A forced blit is an array→array
    // directive, and the automatic layout proof declines this pair precisely because the element types differ
    // — which is the case [Reinterpret] exists to force. Against the narrowing-conversion fixture it was
    // pointed at a scalar, so the directive could not apply and the cell measured nothing.
    [SurfaceProbe("reinterpretable-array-member")]
    private static readonly string ReinterpretableArrayMember = """
        public sealed class Src { public int Id { get; set; } public int[] Data { get; set; } = System.Array.Empty<int>(); }
        public sealed class Dst { public int Id { get; set; } public uint[] Data { get; set; } = System.Array.Empty<uint>(); }
        """;

    // A real base/derived HIERARCHY on both sides, which is the only shape [MapDerivedType] has anything to
    // say about: it registers a dispatch arm from a DERIVED source type to a DERIVED destination type on a
    // method whose parameter is the base. Against the flat pair the sampled arguments were
    // `typeof(Dst), typeof(Dst)` — a type not assignable to the method's source parameter — so the directive
    // could only ever be DWARF035, and the open form's CreateMap cell read NotCompilable (CS8795): the entry
    // that claimed the directive "acts at CreateMap in BOTH forms" was measurably wrong about the open one.
    // Same shape as D10's false evidence, one finding along.
    //
    // The derived types carry an EXTRA member each, so the arm is observable: without it a derived arm maps
    // exactly the base members and the dispatch is a switch that changes nothing anybody can see.
    //
    // Baseline compiles (Src{Id} -> Dst{Id}, no diagnostics at any of the five endpoints, measured), so a
    // directive that does nothing reads Silent rather than UnhonouredButLoud — the trap D11 had to be
    // rescued from.
    //
    // LIMITATION, stated rather than hidden: the GENERIC form's cells are still `[MapDerivedType<Src, Dst>]`,
    // because SurfaceCatalog.Render spells arity 2 as `<Src, Dst>` for every element and the endpoint
    // templates fix the method signature to `Dst Map(Src s)`. Against this fixture that is the base
    // registered as an arm of itself — a legal, resolvable, and degenerate arm. It measures "the generic form
    // is read at CreateMap and at no other endpoint", which is what its cells claim; it does NOT measure
    // polymorphic dispatch. Widening Render to per-element type arguments is a change to the instrument, not
    // to this finding.
    [SurfaceProbe("polymorphic-hierarchy")]
    private static readonly string PolymorphicHierarchy = """
        public class Src { public int Id { get; set; } }
        public sealed class SrcDerived : Src { public string? Extra { get; set; } }
        public class Dst { public int Id { get; set; } }
        public sealed class DstDerived : Dst { public string? Extra { get; set; } }
        """;
#pragma warning restore CS0414, CA1802, CA1823, IDE0051

    public static IReadOnlyDictionary<string, string> All { get; } =
        typeof(SurfaceFixtures)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Select(f => (Probe: f.GetCustomAttribute<SurfaceProbeAttribute>(), Value: f.GetValue(null)))
            .Where(x => x.Probe is not null)
            .ToDictionary(x => x.Probe!.Key, x => (string)x.Value!, StringComparer.Ordinal);

    /// <summary>The fixture for a key, or null for "the default flat DTO pair is sufficient".</summary>
    public static string? Get(string? key) =>
        key is not null && All.TryGetValue(key, out var text) ? text : null;

    /// <summary>
    ///     The member and type names a fixture declares, PARSED rather than string-matched.
    ///     <para>
    ///         A <c>{Member}</c> placeholder in a <c>[DwarfSurfaceProbe(Arguments = ...)]</c> declaration is
    ///         checked against this, so an argument that stops naming a real member fails a gate instead of
    ///         degrading back into the no-op cell the declaration was written to eliminate. Substring matching
    ///         would accept a name that appears only inside a type argument or a comment; the fixtures are C#
    ///         and the test project already has Roslyn, so the question is answered exactly.
    ///     </para>
    ///     <para>
    ///         Members are gathered across EVERY type the fixture declares, not just <c>Src</c> and
    ///         <c>Dst</c>: <c>[MapCollectionKey("Items", "Id")]</c> names a collection on the root and a key on
    ///         its ELEMENT type, and both halves are equally part of the shape.
    ///     </para>
    /// </summary>
    public static (IReadOnlySet<string> Members, IReadOnlySet<string> Types) DeclaredNames(string fixtureText)
    {
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(fixtureText).GetRoot();

        var members = root.DescendantNodes().SelectMany(n => n switch
            {
                Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax p => [p.Identifier.ValueText],
                Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax f =>
                    f.Declaration.Variables.Select(v => v.Identifier.ValueText),
                _ => Enumerable.Empty<string>()
            })
            .ToHashSet(StringComparer.Ordinal);

        var types = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>()
            .Select(t => t.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        return (members, types);
    }
}
