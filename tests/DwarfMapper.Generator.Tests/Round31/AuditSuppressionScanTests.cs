// SPDX-License-Identifier: GPL-2.0-only
using System;
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
            var offenders = Directory.EnumerateFiles(RepoPaths.Root, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".csproj", StringComparison.Ordinal) || p.EndsWith(".props", StringComparison.Ordinal)
                            || p.EndsWith(".targets", StringComparison.Ordinal))
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(p => NoWarnNuAudit.IsMatch(File.ReadAllText(p)))
                .Select(p => Path.GetRelativePath(RepoPaths.Root, p))
                .ToList();
            Assert.True(offenders.Count == 0,
                "NU1901-NU1904 in <NoWarn> hides every future advisory of that severity. Suppress one advisory with " +
                "<NuGetAuditSuppress Include=\"https://github.com/advisories/GHSA-...\" /> instead:\n  " + string.Join("\n  ", offenders));
        }
    }
}
