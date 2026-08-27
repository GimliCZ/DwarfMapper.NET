// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     The mutation-scope figures in <c>Issues/round27/AUDIT-mutation-scope.md</c> are RE-MEASURED here,
    ///     so the document cannot drift away from the configs it describes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         WHY THIS EXISTS. That audit's whole purpose is to stop anyone overstating how much of the
    ///         product mutation testing covers — and it was itself overstating it. Its table credited
    ///         <c>DwarfMapper.Generator</c> with 10 files and 3,849 mutated lines (13.6 %) when
    ///         <c>stryker-config.json</c> has named the same FOUR files since the day it was created; the real
    ///         share is 3.6 %, and the headline was inflated from 9.4 % to 15.3 % along with it.
    ///     </para>
    ///     <para>
    ///         The correction is only worth anything if it cannot happen again, and the original number was
    ///         produced the same way the correction was: by someone measuring once, by hand, with a script
    ///         that lived nowhere. So the measurement lives here now, in the repository, in the language of
    ///         the project, run by every build — which is the only form of a measured claim that stays true.
    ///     </para>
    ///     <para>
    ///         TOLERANCES, and why they differ. File counts are asserted EXACTLY: they move only when a glob
    ///         or a file is added, which is a deliberate act that should be reflected in the document in the
    ///         same commit. Percentage shares are asserted to within one percentage point: ordinary code churn
    ///         moves them by hundredths, so an exact pin would fail on every unrelated edit and be deleted
    ///         within a week — while the error this test exists to catch was TEN points wide.
    ///     </para>
    /// </remarks>
    public class MutationScopeScanTests
    {
        private const string AuditPath = "Issues/round27/AUDIT-mutation-scope.md";
        private const double ShareTolerancePercentagePoints = 1.0;

        private sealed record Measured(int Files, int FilesInLeg, int Lines, int LinesInLeg)
        {
            public double Share => Lines == 0 ? 0 : 100.0 * LinesInLeg / Lines;
        }

        private static IEnumerable<string> SourceFiles(string dir)
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                           .Select(p => p.Replace('\\', '/'))
                           .Where(p => !p.Contains("/obj/", StringComparison.Ordinal)
                                       && !p.Contains("/bin/", StringComparison.Ordinal))
                : [];
        }

        /// <summary>
        ///     Every <c>mutate</c> glob aimed at one project, unioned across configs.
        /// </summary>
        /// <remarks>
        ///     Unioned, not replaced, and that is not hypothetical bookkeeping: two configs target
        ///     <c>DwarfMapper.Generator.csproj</c> — the original four-file leg and the round-27 extracted
        ///     pipeline leg. A measurement that keyed configs by project name would silently keep whichever it
        ///     read last and under-report the assembly, which is exactly the class of error this file exists
        ///     to prevent.
        /// </remarks>
        private static List<string> GlobsFor(string projectFileName)
        {
            var globs = new List<string>();
            foreach (var config in Directory.EnumerateFiles(RepoPaths.Root, "stryker-config*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(config));
                if (!doc.RootElement.TryGetProperty("stryker-config", out var section)) continue;
                if (!section.TryGetProperty("project", out var project)) continue;
                if (!string.Equals(project.GetString(), projectFileName, StringComparison.Ordinal)) continue;
                if (!section.TryGetProperty("mutate", out var mutate)) continue;

                globs.AddRange(mutate.EnumerateArray()
                                     .Select(g => g.GetString()!)
                                     .Select(g => g.StartsWith("**/", StringComparison.Ordinal) ? g[3..] : g));
            }

            return globs;
        }

        private static Measured Measure(string assembly)
        {
            var dir = Path.Combine(RepoPaths.Root, "src", assembly).Replace('\\', '/');
            var globs = GlobsFor(assembly + ".csproj");

            int files = 0, inLeg = 0, lines = 0, linesInLeg = 0;
            foreach (var file in SourceFiles(dir))
            {
                var count = File.ReadAllLines(file).Length;
                files++;
                lines += count;

                var relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                if (globs.Any(g => relative.EndsWith(g, StringComparison.Ordinal)))
                {
                    inLeg++;
                    linesInLeg += count;
                }
            }

            return new Measured(files, inLeg, lines, linesInLeg);
        }

        private static Dictionary<string, int[]> AuditRows()
        {
            var rows = new Dictionary<string, int[]>(StringComparer.Ordinal);
            var shares = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(Path.Combine(RepoPaths.Root, AuditPath)))
            {
                if (!line.StartsWith('|')) continue;

                var cells = line.Split('|');
                if (cells.Length < 7) continue;

                static string Clean(string cell)
                {
                    return cell.Replace("*", string.Empty, StringComparison.Ordinal)
                               .Replace("`", string.Empty, StringComparison.Ordinal)
                               .Replace(",", string.Empty, StringComparison.Ordinal)
                               .Replace("%", string.Empty, StringComparison.Ordinal)
                               .Trim();
                }

                var name = Clean(cells[1]);
                var backtick = Regex.Match(cells[1], "`([^`]+)`");
                if (backtick.Success) name = backtick.Groups[1].Value;
                if (name.Contains(' ', StringComparison.Ordinal) && !string.Equals(name, "all", StringComparison.Ordinal))
                {
                    name = name.Split(' ')[0];
                }

                if (!int.TryParse(Clean(cells[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var files))
                {
                    continue;
                }

                if (!int.TryParse(Clean(cells[3]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var inLeg)
                    || !int.TryParse(Clean(cells[4]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lines)
                    || !int.TryParse(Clean(cells[5]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mut)
                    || !double.TryParse(Clean(cells[6]), NumberStyles.Float, CultureInfo.InvariantCulture, out var share))
                {
                    continue;
                }

                rows[name] = [files, inLeg, lines, mut];
                shares[name] = share;
            }

            Assert.True(rows.Count > 0,
                $"{AuditPath}: no measurable table row parsed — the audit's table changed shape, so the " +
                "figures it publishes are no longer checked against the configs at all.");

            foreach (var (name, share) in shares)
            {
                rows[name] = [.. rows[name], (int)Math.Round(share * 100)];
            }

            return rows;
        }

        [Fact]
        public void The_audit_reports_the_share_the_configs_actually_cover()
        {
            var rows = AuditRows();
            var problems = new List<string>();
            int totalFiles = 0, totalInLeg = 0, totalLines = 0, totalLinesInLeg = 0;

            foreach (var (name, stated) in rows.Where(r => !string.Equals(r.Key, "all", StringComparison.Ordinal)))
            {
                var m = Measure(name);
                totalFiles += m.Files;
                totalInLeg += m.FilesInLeg;
                totalLines += m.Lines;
                totalLinesInLeg += m.LinesInLeg;

                if (m.Files != stated[0])
                {
                    problems.Add($"{name}: audit says {stated[0]} file(s), measured {m.Files}");
                }

                if (m.FilesInLeg != stated[1])
                {
                    problems.Add($"{name}: audit says {stated[1]} file(s) in a leg, measured {m.FilesInLeg}");
                }

                var statedShare = stated[4] / 100.0;
                if (Math.Abs(m.Share - statedShare) > ShareTolerancePercentagePoints)
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{name}: audit says {statedShare:F1} %, measured {m.Share:F1} %"));
                }
            }

            if (rows.TryGetValue("all", out var all))
            {
                if (totalFiles != all[0])
                {
                    problems.Add($"all: audit says {all[0]} file(s), measured {totalFiles}");
                }

                if (totalInLeg != all[1])
                {
                    problems.Add($"all: audit says {all[1]} file(s) in a leg, measured {totalInLeg}");
                }

                var overall = 100.0 * totalLinesInLeg / totalLines;
                if (Math.Abs(overall - (all[4] / 100.0)) > ShareTolerancePercentagePoints)
                {
                    problems.Add(string.Create(CultureInfo.InvariantCulture,
                        $"all: audit says {all[4] / 100.0:F1} %, measured {overall:F1} %"));
                }
            }

            Assert.True(problems.Count == 0,
                $"{AuditPath} no longer describes the Stryker configs it claims to measure. This document " +
                "exists so that nobody states a mutation score as if it covered the product, and a stale " +
                "table defeats that more completely than no table would — the previous version overstated " +
                "the generator by nearly four times. Re-measure and update the table in the same commit as " +
                "whatever moved it.\n  " + string.Join("\n  ", problems));
        }
    }
}
