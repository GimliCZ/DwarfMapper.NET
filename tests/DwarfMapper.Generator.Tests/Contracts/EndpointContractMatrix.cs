// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>What a given attribute does at a given endpoint. There is no fourth option, and that is the point.</summary>
public enum CellStatus
{
    /// <summary>The attribute applies here and its effect is asserted.</summary>
    Honoured,

    /// <summary>Inapplicable, and the generator SAYS SO with a named diagnostic (the "error shape").</summary>
    Refused,

    /// <summary>Structurally meaningless at this endpoint; carries a reason so the claim can be reviewed.</summary>
    NotApplicable
}

/// <summary>One declared expectation for one (attribute usage name, endpoint) pair.</summary>
/// <param name="Usage">Attribute usage name, e.g. <c>MapProperty</c> — the deduplicated form.</param>
/// <param name="Endpoint">Which mapping shape.</param>
/// <param name="Status">Honoured / Refused / NotApplicable.</param>
/// <param name="DiagnosticId">For <see cref="CellStatus.Refused" />, the id that must be reported.</param>
/// <param name="Reason">For <see cref="CellStatus.NotApplicable" />, why.</param>
public sealed record ContractCell(
    string Usage,
    Endpoint Endpoint,
    CellStatus Status,
    string? DiagnosticId = null,
    string? Reason = null);

/// <summary>
///     The attribute × endpoint contract.
///     <para>
///         Every defect this matrix exists to prevent was a cell that was <b>silently ignored</b>:
///         <c>NullSubstitute</c>, <c>When=</c> and <c>SkipNullSourceMembers</c> each bound their rename at the
///         projection endpoint — satisfying the completeness gate — and then dropped the modifier, so
///         <c>.Map</c> and <c>.Project</c> returned different data from one mapper with no diagnostic at all.
///         The option was tested. The endpoint was tested. The cell was not.
///     </para>
///     <para>
///         So <see cref="CellStatus" /> has no "silent" member, and a cell with no declared expectation fails
///         the growth ratchet. An attribute that reaches one endpoint and not another is now a build failure
///         here rather than a data-correctness bug in someone's application.
///     </para>
///     <para>
///         Scope note: usage names are taken from the SAME derivation
///         <see cref="SelfValidation.TestTheTestsScanTests" /> already uses for <c>MatrixExemptAttributes</c>
///         (generic and non-generic forms collapse to one name). Inventing a second notion of "an attribute"
///         is how the two-engine drift started, and is not repeated here.
///     </para>
/// </summary>
public static class EndpointContractMatrix
{
    // Reasons reused across many N/A cells — named so a reader sees the CLASS of exemption, not 60 strings.
    private const string AssemblyScoped =
        "assembly-scoped policy: it configures a whole assembly, not one mapping method, so it has no "
        + "per-endpoint cell (covered by AssemblyDefaultsTests)";

    private const string GeneratorEmitted =
        "emitted BY the generator onto the assembly as a manifest; never hand-written, so it has no endpoint";

    private const string SelectsEndpoint =
        "a front-door marker that SELECTS the endpoint rather than modifying one; it is the row header, not a "
        + "cell value";

    private const string ClassLevelOnly =
        "class-level mapper configuration; its per-endpoint behaviour is expressed by the options it carries "
        + "(NameConvention, NullStrategy, ...) rather than by the marker itself";

    private const string NoMemberConfigOnRegistry =
        "the [MapTo] registry front door is a separate generator with its own DWARFR diagnostics and a "
        + "deliberately smaller feature set; it does not read the class-model mapper attributes";

    private const string CollectionOrGraphOnly =
        "applies to a collection/graph shape this endpoint does not construct";

    /// <summary>
    ///     The declared cells. Only entries that differ from the endpoint-independent default are listed;
    ///     <see cref="For" /> fills the rest, so the table states intent instead of restating boilerplate 182
    ///     times.
    /// </summary>
    private static readonly ContractCell[] Declared =
    [
        // ── [MapProperty] — honoured broadly; its MODIFIERS are the interesting part (see below) ──────────
        new("MapProperty", Endpoint.CreateMap, CellStatus.Honoured),
        new("MapProperty", Endpoint.UpdateInto, CellStatus.Honoured),
        new("MapProperty", Endpoint.Projection, CellStatus.Honoured),
        new("MapProperty", Endpoint.Registry, CellStatus.Honoured),

        // ── [MapValue] — a source-less constant. Verified: works on .Map, DWARF001 on .Project ────────────
        new("MapValue", Endpoint.CreateMap, CellStatus.Honoured),
        new("MapValue", Endpoint.UpdateInto, CellStatus.Honoured),
        new("MapValue", Endpoint.Projection, CellStatus.Refused, "DWARF001",
            "no source member exists, and projection does not receive mapValues"),

        // ── [MapIgnore] / [MapIgnoreSource] — completeness control, meaningful wherever completeness runs ─
        new("MapIgnore", Endpoint.CreateMap, CellStatus.Honoured),
        new("MapIgnore", Endpoint.UpdateInto, CellStatus.Honoured),
        new("MapIgnore", Endpoint.Projection, CellStatus.Honoured),
        new("MapIgnore", Endpoint.Registry, CellStatus.Honoured),

        // ── Hooks — imperative, so untranslatable in an expression tree ───────────────────────────────────
        new("BeforeMap", Endpoint.CreateMap, CellStatus.Honoured),
        new("AfterMap", Endpoint.CreateMap, CellStatus.Honoured),
        new("BeforeMap", Endpoint.UpdateInto, CellStatus.Honoured),
        new("AfterMap", Endpoint.UpdateInto, CellStatus.Honoured),

        // ── [RoundTrip] — needs a forward/back PAIR of create maps ────────────────────────────────────────
        new("RoundTrip", Endpoint.CreateMap, CellStatus.Honoured),
        new("ReverseMap", Endpoint.CreateMap, CellStatus.Honoured)
    ];

    /// <summary>
    ///     Endpoint-independent classification for attributes whose answer is the same everywhere. Keeps the
    ///     explicit table to the cells that carry real information.
    /// </summary>
    private static readonly Dictionary<string, (CellStatus Status, string Reason)> Uniform =
        new(StringComparer.Ordinal)
        {
            ["DwarfMapperOptions"] = (CellStatus.NotApplicable, AssemblyScoped),
            ["DwarfMapperDefaults"] = (CellStatus.NotApplicable, AssemblyScoped),
            ["DwarfMapperValidationRoot"] = (CellStatus.NotApplicable, AssemblyScoped),
            ["UsesMap"] = (CellStatus.NotApplicable, AssemblyScoped),
            ["DwarfProvidesMap"] = (CellStatus.NotApplicable, GeneratorEmitted),
            ["DwarfRequiresMap"] = (CellStatus.NotApplicable, GeneratorEmitted),
            ["DwarfMapper"] = (CellStatus.NotApplicable, ClassLevelOnly),
            ["MapTo"] = (CellStatus.NotApplicable, SelectsEndpoint),
            ["GenerateMap"] = (CellStatus.NotApplicable, SelectsEndpoint),
            ["GenerateWrapperMap"] = (CellStatus.NotApplicable, SelectsEndpoint),
            ["DwarfMapperConstructor"] = (CellStatus.NotApplicable,
                "placed on a TARGET TYPE's constructor, not on a mapping method"),
            ["MapConstructor"] = (CellStatus.NotApplicable,
                "class-level pair-scoped factory selection; not a per-method modifier"),
            ["AutoNest"] = (CellStatus.NotApplicable, ClassLevelOnly),
            ["MapDerivedType"] = (CellStatus.NotApplicable,
                "polymorphic dispatch arm; applies to the base-type method it annotates"),
            ["Flatten"] = (CellStatus.NotApplicable, CollectionOrGraphOnly),
            ["FlattenGraph"] = (CellStatus.NotApplicable, CollectionOrGraphOnly),
            ["Reinterpret"] = (CellStatus.NotApplicable, CollectionOrGraphOnly),
            ["MapCollectionKey"] = (CellStatus.NotApplicable,
                "update-into-only key correlation; refused elsewhere by DWARF074"),
            ["MapIgnoreSource"] = (CellStatus.NotApplicable,
                "source-side completeness mirror, only meaningful under RequiredMapping = Both")
        };

    /// <summary>Attributes this matrix has been taught about — explicitly per-cell, or uniformly.</summary>
    public static IReadOnlyCollection<string> KnownUsages { get; } =
        Declared.Select(c => c.Usage).Concat(Uniform.Keys).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    ///     The declared expectation for one cell, or <c>null</c> when this matrix has never been told about
    ///     <paramref name="usage" /> at all.
    ///     <para>
    ///         Returning null for an unknown attribute is load-bearing. An earlier draft fell back to an
    ///         endpoint-shaped <c>NotApplicable</c> for anything unrecognised, which meant a brand-new public
    ///         attribute was auto-classified with a canned reason and the growth ratchet stayed green —
    ///         verified by adding a probe attribute and watching the suite pass. That is the exact vacuity this
    ///         matrix exists to prevent, so the fallback is now reachable only for KNOWN attributes.
    ///     </para>
    /// </summary>
    public static ContractCell? For(string usage, Endpoint endpoint)
    {
        var explicitCell = Declared.FirstOrDefault(
            c => string.Equals(c.Usage, usage, StringComparison.Ordinal) && c.Endpoint == endpoint);
        if (explicitCell is not null) return explicitCell;

        if (Uniform.TryGetValue(usage, out var uniform))
            return new ContractCell(usage, endpoint, uniform.Status, Reason: uniform.Reason);

        // Known only because some OTHER endpoint declared it explicitly; fall through to the shaped default.
        // Unknown entirely -> null, so the ratchet names it.
        if (!KnownUsages.Contains(usage)) return null;

        // Endpoint-shaped defaults for the member-level attributes not explicitly listed above. Span and
        // async-stream take element-wise shapes that carry no per-member configuration; the registry is a
        // separate generator that does not read class-model attributes; the co-located host has no method to
        // annotate.
        return endpoint switch
        {
            Endpoint.SpanMap => new ContractCell(usage, endpoint, CellStatus.NotApplicable,
                Reason: "span mapping is an element-wise buffer fill with no per-member configuration surface"),
            Endpoint.AsyncStream => new ContractCell(usage, endpoint, CellStatus.NotApplicable,
                Reason: "async streaming maps elements through the element mapper; configuration belongs there"),
            Endpoint.Registry => new ContractCell(usage, endpoint, CellStatus.NotApplicable,
                Reason: NoMemberConfigOnRegistry),
            Endpoint.CoLocatedHost => new ContractCell(usage, endpoint, CellStatus.NotApplicable,
                Reason: "the co-located host declares the pair; member config uses the pair-scoped forms"),
            _ => new ContractCell(usage, endpoint, CellStatus.NotApplicable,
                Reason: "not a modifier of this endpoint")
        };
    }

    /// <summary>
    ///     The per-member <c>[MapProperty]</c> MODIFIERS, which carry their own contract independent of the
    ///     attribute they ride on. This is where three silent divergences lived: each bound the rename, so
    ///     completeness was satisfied, and then the modifier was dropped.
    /// </summary>
    public static readonly (string Modifier, string Syntax, Endpoint Endpoint, CellStatus Status, string? Id)[]
        ModifierCells =
        [
            ("Use", "Use = nameof(Conv)", Endpoint.Projection, CellStatus.Refused, "DWARF028"),
            ("NullSubstitute", "NullSubstitute = \"<sub>\"", Endpoint.Projection, CellStatus.Refused, "DWARF028"),
            ("When", "When = nameof(Never)", Endpoint.Projection, CellStatus.Refused, "DWARF028"),
            ("Use", "Use = nameof(Conv)", Endpoint.CreateMap, CellStatus.Honoured, null),
            ("NullSubstitute", "NullSubstitute = \"<sub>\"", Endpoint.CreateMap, CellStatus.Honoured, null),
            ("When", "When = nameof(Never)", Endpoint.CreateMap, CellStatus.Honoured, null)
        ];
}
