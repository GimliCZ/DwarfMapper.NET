// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>
///     The markdown files the pipeline owns. An explicit list, not a glob: <c>docs/superpowers/</c> holds
///     specs and plans whose code blocks are design sketches of code that does not exist yet, and a glob would
///     demand they resolve to real samples.
/// </summary>
public static class DocSet
{
    public static IReadOnlyList<string> All { get; } =
    [
        "README.md",
        "CONTRIBUTING.md",
        "docs/diagnostics.md",
        "docs/options.md",
        "docs/COMPARISON.md",
        "docs/CORRECTNESS.md",
        "docs/MIGRATION.md",
        "docs/howto/README.md",
        "docs/howto/ambient-cross-assembly-maps.md",
        "docs/howto/common-changes.md",
        "docs/howto/deploy-and-optimize.md",
        "docs/howto/migrate-from-automapper.md",
        "docs/howto/migrate-from-handwritten.md",
        "docs/howto/migrate-from-mapperly.md",
        "docs/howto/migrate-from-mapster.md",
        "samples/DwarfMapper.Gallery/README.md"
    ];

    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoLayout.Root, relativePath));
}
