// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator;

/// <summary>
///     The single funnel every generator output goes through, so emitted source has ONE line-ending convention.
/// </summary>
/// <remarks>
///     Generated files were assembled two different ways: the emitters use <c>StringBuilder.AppendLine</c>, which
///     writes <see cref="System.Environment.NewLine" /> (CRLF on Windows, LF elsewhere), while the synthesized
///     converter/helper bodies are built with hard <c>"\n"</c> literals and then spliced into that same builder.
///     A single generated file therefore contained BOTH conventions, and which mixture you got depended on the
///     build machine's OS — so the same source produced byte-different output on Windows and Linux. That breaks
///     reproducible builds, makes any byte-level comparison of generated output platform-dependent, and shows up
///     as spurious diffs when <c>EmitCompilerGeneratedFiles</c> writes the files to disk.
///     Normalising to LF at the boundary fixes it in one place instead of auditing ~220 AppendLine calls.
/// </remarks>
internal static class GeneratedSourceExtensions
{
    /// <summary>Adds a generated source file with line endings normalised to LF.</summary>
    public static void AddNormalizedSource(this SourceProductionContext spc, string hintName, string source)
    {
        spc.AddSource(hintName, source.Replace("\r\n", "\n"));
    }
}
