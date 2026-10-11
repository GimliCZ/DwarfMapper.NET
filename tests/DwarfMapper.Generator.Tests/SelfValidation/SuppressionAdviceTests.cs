// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     User-facing documentation and diagnostic text must not send a reader to <c>.editorconfig</c> to change a
    ///     DWARF diagnostic's severity: DwarfMapper reports at a location the compiler does not tie to a source file, so a
    ///     per-file severity never reaches its diagnostics. The
    ///     project-wide switch that does is a <c>.globalconfig</c> (<c>is_global = true</c>).
    /// </summary>
    /// <remarks>
    ///     Measured 2026-10-11 in a real build on 1.1.0-rc14 and pinned in-process by
    ///     <c>SuppressionPathwayTests</c> (NegativeCases): <c>.editorconfig</c> neither silenced nor escalated
    ///     DWARF070/DWARF076, a <c>.globalconfig</c> did both. <c>docs/diagnostics.md</c> had recommended
    ///     <c>.editorconfig</c> sixteen times, including in its severity table for every id - so a reader who
    ///     followed the docs exactly concluded the generator ignored its own configuration, and reached for a
    ///     project-wide <c>NoWarn</c>. The rule is deliberately blunt: a line that mentions <c>.editorconfig</c>
    ///     must be saying that it does not apply.
    /// </remarks>
    public sealed class SuppressionAdviceTests
    {
        private static readonly Regex Negation = new(@"\bnot\b|n't\b|\bno effect\b|\bnever\b", RegexOptions.IgnoreCase);

        public static IEnumerable<string> UserFacingFiles()
        {
            var root = RepoPaths.Root;
            yield return Path.Combine(root, "README.md");
            foreach (var f in Directory.GetFiles(Path.Combine(root, "docs"), "*.md"))
                // IMPROVEMENT-PLAN.md is a dated record of past plans, not guidance a reader acts on.
                if (!f.EndsWith("IMPROVEMENT-PLAN.md", StringComparison.Ordinal))
                    yield return f;
            foreach (var f in Directory.GetFiles(Path.Combine(root, "docs", "howto"), "*.md"))
                yield return f;
            foreach (var f in Directory.GetFiles(Path.Combine(root, "docs", "generated"), "*.md"))
                yield return f;
            foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "DwarfMapper"), "*.cs"))
                yield return f;
            yield return Path.Combine(RepoPaths.GeneratorSrcDir, "Diagnostics", "DiagnosticDescriptors.cs");
        }

        [Fact]
        public void No_user_facing_text_recommends_editorconfig_for_a_DWARF_severity()
        {
            var offenders = new List<string>();
            foreach (var file in UserFacingFiles())
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].Contains("editorconfig", StringComparison.OrdinalIgnoreCase) && !Negation.IsMatch(lines[i]))
                        offenders.Add(Path.GetRelativePath(RepoPaths.Root, file) + ":" + (i + 1) + "  " + lines[i].Trim());
            }

            Assert.True(offenders.Count == 0,
                "These lines send a reader to .editorconfig, which does not reach a DwarfMapper diagnostic (see " +
                "SuppressionPathwayTests). Name a .globalconfig (is_global = true) for a project-wide severity, or " +
                "the per-site intent statement where one exists:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_scan_reads_the_files_it_claims_to()
        {
            // Non-vacuity: diagnostics.md is the file the rule exists for.
            Assert.Contains(UserFacingFiles(), f => f.EndsWith(Path.Combine("docs", "diagnostics.md"), StringComparison.Ordinal));
        }
    }
}
