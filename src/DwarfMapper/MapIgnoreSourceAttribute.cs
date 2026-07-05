// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Excludes a <b>source</b> member from source-coverage checking. Required to silence the
///     <c>DWARF039</c> suggestion for a source member intentionally read by no destination, when the mapper
///     uses <c>[DwarfMapper(RequiredMapping = RequiredMappingStrategy.Both)]</c>. The source-side mirror of
///     <see cref="MapIgnoreAttribute" />.
/// </summary>
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
