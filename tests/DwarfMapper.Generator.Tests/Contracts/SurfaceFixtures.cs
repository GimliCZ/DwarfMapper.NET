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
}
