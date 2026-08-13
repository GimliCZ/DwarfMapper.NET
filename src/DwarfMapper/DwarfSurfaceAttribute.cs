// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     What kind of surface an element is, and therefore what must be proved about it.
///     <para>
///         There is deliberately no <c>Exempt</c> member. Every category below carries a DIFFERENT mandatory
///         obligation; classifying an element redirects its proof rather than waiving it. This replaces six
///         independent allowlist dictionaries, each of which was one person typing a reason once.
///     </para>
/// </summary>
internal enum SurfaceCategory
{
    /// <summary>
    ///     Hand-written by consumers and changes emitted code. Obligation: the executed cross-product (every
    ///     cell honoured, refused, or declared inapplicable), plus at least one consumer-assembly use and one
    ///     runnable-sample use.
    /// </summary>
    ConsumerDirective,

    /// <summary>
    ///     Emitted BY the generator onto the assembly; never hand-written (DWARF086 refuses hand-written use).
    ///     Obligation: produced by a generator run in a test AND consumed by the reading side.
    /// </summary>
    GeneratorEmitted,

    /// <summary>
    ///     Its whole observable effect is a build failure, so a passing sample cannot contain it. Obligation:
    ///     a NegativeCases row pinning the id AND its remedy wording, plus a positive row proving the
    ///     non-failing path compiles.
    /// </summary>
    BuildFailureOnly,

    /// <summary>
    ///     Changes the SHAPE or accessibility of generated code rather than runtime behaviour. Obligation: a
    ///     structural assertion over the generated text at every claimed endpoint.
    /// </summary>
    EmissionShape,

    /// <summary>
    ///     Only observable across an assembly boundary. Obligation: exercised by a multi-assembly consumer
    ///     fixture; single-assembly cells are inapplicable by category, not by allowlist.
    /// </summary>
    CrossAssembly,

    /// <summary>
    ///     Part of the testing surface consumers use to write their own tests. Obligation: consumer-shaped use
    ///     plus a DwarfMapper.Testing.Tests contract row.
    /// </summary>
    TestingOnly
}

/// <summary>
///     The mapping shapes a surface element can reach. Member names are kept identical to
///     <c>DwarfMapper.Generator.Tests.Contracts.Endpoint</c>; a test asserts the bijection, because two enums
///     that drift apart would silently repoint every claim at the wrong endpoint.
/// </summary>
[Flags]
internal enum SurfaceEndpoints
{
    None = 0,
    CreateMap = 1,
    UpdateInto = 2,
    Projection = 4,
    SpanMap = 8,
    AsyncStream = 16,
    Registry = 32,
    CoLocatedHost = 64,

    /// <summary>
    ///     Every endpoint. This is the DEFAULT on purpose. Over-claiming fails (a claimed endpoint must be
    ///     honoured or refused) and under-claiming fails too (an unclaimed endpoint must be silent or
    ///     uncompilable), so there is no value of <see cref="DwarfSurfaceAttribute.AppliesTo" /> that passes
    ///     vacuously — a wrong default is always caught rather than quietly ratified.
    /// </summary>
    All = CreateMap | UpdateInto | Projection | SpanMap | AsyncStream | Registry | CoLocatedHost
}

/// <summary>
///     Declares what must be proved about a public surface element, at the element's own declaration.
///     <para>
///         Two facts about a surface element cannot be reflected: what SHAPE makes it observable, and which
///         endpoints it legitimately does not reach. Both used to live in hand-kept dictionaries in the test
///         project, where nothing forced a new element to acquire an entry — an unprobed element read as "not
///         probed" and claimed nothing. Here they sit next to the code they describe and are verified in both
///         directions.
///     </para>
///     <para>
///         Internal, and read only by the test projects via <c>[InternalsVisibleTo]</c>. Nothing in the shipped
///         runtime reads it: this is metadata, and the package's zero-reflection, trim- and AOT-safe guarantees
///         are unaffected.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface
                | AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
internal sealed class DwarfSurfaceAttribute : Attribute
{
    public DwarfSurfaceAttribute(SurfaceCategory category) => Category = category;

    /// <summary>What kind of surface this is, and therefore what must be proved about it.</summary>
    public SurfaceCategory Category { get; }

    /// <summary>
    ///     The endpoints this element CLAIMS to affect. Verified in both directions by
    ///     <c>SurfaceParityTests</c>: a claimed endpoint where the element does nothing observable fails, and
    ///     an unclaimed endpoint where it changes the output fails too.
    /// </summary>
    public SurfaceEndpoints AppliesTo { get; set; } = SurfaceEndpoints.All;

    /// <summary>
    ///     Names the test-side fixture whose type shape makes this element observable — e.g. an enum pair with
    ///     divergent member order, or a self-referencing graph. Bound dynamically: the test project must
    ///     contain exactly one <c>[SurfaceProbe]</c> fixture with this key, and every fixture must be claimed
    ///     by at least one element. Null means the default flat DTO pair suffices.
    /// </summary>
    public string? ProbeKey { get; set; }
}
