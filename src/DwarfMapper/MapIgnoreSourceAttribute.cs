// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics.CodeAnalysis;

namespace DwarfMapper;

/// <summary>
///     Excludes a <b>source</b> member from source-coverage checking. Required to silence the
///     <c>DWARF039</c> suggestion for a source member intentionally read by no destination, when the mapper
///     uses <c>[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]</c>. The source-side mirror of
///     <see cref="MapIgnoreAttribute" />.
/// </summary>
[DwarfSurface(SurfaceCategory.ConsumerDirective, ProbeKey = "unconsumed-source-member")]
// `Extra` is the fixture's UNCONSUMED member — the only one whose coverage warning there is anything to
// silence. The sampled "Id" named a member every destination already reads, so the directive asked the
// generator to suppress a warning that was never going to be raised.
// And the suggestion only exists under RequiredMapping = Both; with the default strategy the directive has
// nothing to silence and correctly changes nothing, which read as a divergence the generator never committed.
[DwarfSurfaceProbe(constructorArity: 1, Arguments = "{Extra}",
    MapperOptions = "RequiredMapping = RequiredMappingStrategy.Both")]
[ExcludeFromCodeCoverage(Justification = "compile-time-only attribute, consumed by the generator (round-22 P4's one "
    + "sanctioned category): read from the semantic model at build time; no runtime code path constructs or "
    + "executes it. Issues/round22/RESEARCH-97-PERCENT-GATES.md §2.2.")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class MapIgnoreSourceAttribute : Attribute
{
    /// <summary>Initialises a new instance of <see cref="MapIgnoreSourceAttribute" />.</summary>
    /// <param name="source">Name of the source member to exclude from coverage checking.</param>
    public MapIgnoreSourceAttribute(string source)
    {
        Source = source;
    }

    /// <summary>Name of the source member to exclude from coverage checking.</summary>
    public string Source { get; }
}
