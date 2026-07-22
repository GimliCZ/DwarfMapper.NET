// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Registry;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>One generator under test: how to build it and which steps must stay cacheable.</summary>
internal sealed record GeneratorUnderTest(
    string Name,
    Func<IIncrementalGenerator> Create,
    IReadOnlyList<string> TrackingNames);

/// <summary>
///     Every generator the framework covers. A ratchet asserts this list matches the
///     <see cref="IIncrementalGenerator" /> types actually present in <c>src/</c>, so a new generator cannot
///     ship without cacheability and golden coverage — which is how MapToGenerator ended up with neither.
/// </summary>
internal static class GeneratorRegistry
{
    public static IReadOnlyList<GeneratorUnderTest> All { get; } = new[]
    {
        new GeneratorUnderTest("DwarfGenerator", () => new DwarfGenerator(), DwarfGenerator.AllStepNames),
        new GeneratorUnderTest("MapToGenerator", () => new MapToGenerator(), MapToGenerator.AllStepNames),
    };
}
