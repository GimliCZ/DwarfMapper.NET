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
    /// <summary>
    ///     The suffix every hint name this generator hands to <c>AddSource</c> ends with, and therefore the
    ///     suffix Roslyn puts on the <see cref="SyntaxTree.FilePath" /> of the tree it parses from that source.
    /// </summary>
    public const string GeneratedFileSuffix = ".g.cs";

    /// <summary>Adds a generated source file with line endings normalised to LF.</summary>
    public static void AddNormalizedSource(this SourceProductionContext spc, string hintName, string source)
    {
        spc.AddSource(hintName, source.Replace("\r\n", "\n"));
    }

    /// <summary>
    ///     Whether <paramref name="tree" /> is one the generator authored rather than one the user wrote.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Needed by DWARF086, which refuses a hand-written manifest attribute and must not refuse the
    ///         generator's own emission of the same attribute. The discriminator is the file-path suffix:
    ///         every emission funnels through <see cref="AddNormalizedSource" /> under a
    ///         <c>*<see cref="GeneratedFileSuffix" /></c> hint name, and Roslyn derives the generated tree's
    ///         path from that hint name — so the suffix is present whether the driver keeps the tree in memory
    ///         or <c>EmitCompilerGeneratedFiles</c> writes it to <c>obj/</c>.
    ///     </para>
    ///     <para>
    ///         "The tree has no on-disk path" is the tempting alternative and it is wrong: both test harnesses
    ///         parse the USER's source with <c>CSharpSyntaxTree.ParseText(source)</c>, which yields an empty
    ///         <see cref="SyntaxTree.FilePath" />, so a path-emptiness test classifies hand-written source as
    ///         generated and the refusal never fires where it is measured.
    ///     </para>
    ///     <para>
    ///         A <c>null</c> tree — an attribute with no syntax reference — is treated as generator-authored,
    ///         because it cannot have been written in this compilation's source at all: it came from metadata.
    ///     </para>
    ///     <para>
    ///         <b>The collision case, stated rather than left to be rediscovered (B14).</b> The suffix is not
    ///         exclusive to THIS generator: any other source generator in the consumer's compilation that
    ///         emits under a <c>*<see cref="GeneratedFileSuffix" /></c> hint name, and any <c>.g.cs</c> file a
    ///         consumer checks in by hand, produces a tree this predicate calls generator-authored. A manifest
    ///         attribute written there is therefore exempted from <c>DWARF086</c> and the refusal never fires.
    ///     </para>
    ///     <para>
    ///         That is accepted, and the direction is why: the failure is PERMISSIVE-ONLY. The collision can
    ///         only ever WITHHOLD a diagnostic from source that would otherwise have been refused — it can
    ///         never redden a consumer build that should have been green, and it cannot make the generator
    ///         emit anything different. Tightening it would mean asking Roslyn which generator produced a
    ///         tree, which the <c>SyntaxTree</c> a <c>SyntaxReference</c> hands back does not carry; the
    ///         alternative discriminators were examined and are worse (see the path-emptiness paragraph
    ///         above). Recorded here, at the method that decides it, because an undocumented false negative
    ///         is indistinguishable from an oversight to whoever reads this next.
    ///     </para>
    /// </remarks>
    public static bool IsGeneratorAuthored(SyntaxTree? tree)
    {
        return tree is null
               || tree.FilePath.EndsWith(GeneratedFileSuffix, StringComparison.Ordinal);
    }
}
