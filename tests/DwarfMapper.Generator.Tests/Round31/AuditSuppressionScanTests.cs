// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    public sealed class AuditSuppressionScanTests
    {
        private static readonly Regex NoWarnNuAudit = new(@"<NoWarn>[^<]*\bNU190[1-4]\b", RegexOptions.CultureInvariant);

        [Fact]
        public void No_project_silences_NuGet_audit_severities_wholesale()
        {
            var offenders = Offenders(RepoPaths.Root);
            Assert.True(offenders.Count == 0,
                "NU1901-NU1904 in <NoWarn> hides every future advisory of that severity. Suppress one advisory with " +
                "<NuGetAuditSuppress Include=\"https://github.com/advisories/GHSA-...\" /> instead:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        ///     Regression for a false red seen on 2026-09-27: agent worktrees live under <c>.claude/worktrees/</c>
        ///     INSIDE the repository, and one cut from pre-round-31 master still carried the NoWarn T01 removed. A
        ///     project file in another checkout describes another tree. The planted root offender proves the scan
        ///     still sees this one.
        /// </summary>
        [Fact]
        public void A_project_file_in_a_nested_checkout_is_not_this_trees_offender()
        {
            var root = Directory.CreateTempSubdirectory("dwarf-audit-scan-").FullName;
            try
            {
                const string noWarn = "<Project><PropertyGroup><NoWarn>$(NoWarn);NU1903</NoWarn></PropertyGroup></Project>";
                WriteTemp(root, Path.Combine(".claude", "worktrees", "agent", "src", "Old.csproj"), noWarn);
                WriteTemp(root, Path.Combine(".git", "modules", "Sub.csproj"), noWarn);
                WriteTemp(root, "Clean.csproj", "<Project />");

                Assert.Empty(Offenders(root));

                WriteTemp(root, "Real.csproj", noWarn);
                Assert.Equal(["Real.csproj"], Offenders(root));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>The one raw write in this file: fixtures in a temp directory only (RepoWriteGuardTests registers it).</summary>
        private static void WriteTemp(string root, string relative, string text)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        private static List<string> Offenders(string root)
        {
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".props", StringComparison.Ordinal)
                            || p.EndsWith(".targets", StringComparison.Ordinal))
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                // .claude/ and .git/ hold OTHER checkouts (agent worktrees live under .claude/worktrees/), so a
                // project file there describes a different tree.
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !p.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(p => NoWarnNuAudit.IsMatch(File.ReadAllText(p)))
                .Select(p => Path.GetRelativePath(root, p))
                .ToList();
        }
    }
}
