// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Gallery;

/// <summary>
///     Where an example sits in the learning progression. An enum, not a string, so ordering and grouping are
///     the compiler's problem rather than a convention's.
/// </summary>
public enum Tier
{
    Basics,
    Configuration,
    FrontDoors,
    Advanced,
    Testing
}

/// <summary>
///     Declares a Gallery example. Reflected over by DwarfMapper.DocTooling to build the runner order and the
///     generated index table, so that neither is a hand-maintained list.
///     <para>
///         Deliberately declared here in the sample and not in the DwarfMapper package: documentation
///         infrastructure must not enlarge the public API surface consumers depend on.
///     </para>
/// </summary>
/// <remarks>
///     <c>Inherited = false</c> is load-bearing — an inherited [DocExample] would report one example twice. A
///     Rider/ReSharper full cleanup is known to strip it; re-check after any cleanup run.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DocExampleAttribute(int ordinal, Tier tier, string title) : Attribute
{
    /// <summary>Position in the progression. Binds this example to its <c>NN_*.cs</c> file.</summary>
    public int Ordinal { get; } = ordinal;

    /// <summary>Which group of the generated index this example appears under.</summary>
    public Tier Tier { get; } = tier;

    /// <summary>Short title, rendered as the index row's heading.</summary>
    public string Title { get; } = title;

    /// <summary>One clause describing what the example demonstrates; the index's "Shows" column.</summary>
    public string Shows { get; set; } = "";
}
