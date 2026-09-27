// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DwarfMapper.Generator.Tests.Contracts;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Asserts every file under <c>Coverage/</c> states WHY it exists: a <c>// Covers: Type.Method — reason</c>
    ///     line near the top, naming the production method its tests exercise and the branch or condition it
    ///     targets. Without this, a coverage file is a name and a green checkmark — nothing says what it would
    ///     miss if deleted, which is the same vacuity shape the rest of this repo's scans exist to catch one
    ///     level up.
    /// </summary>
    public sealed class CoverageProvenanceScanTests
    {
        private const string CoversPrefix = "// Covers:";
        private const int HeaderWindow = 15;

        private static string CoverageDir => Path.Combine(RepoPaths.Tests, "DwarfMapper.Generator.Tests", "Coverage");

        private static List<string> CoverageFiles => RepoPaths.SourceFiles(CoverageDir).ToList();

        [Fact]
        public void Every_coverage_file_states_a_Covers_line_near_its_top()
        {
            var files = CoverageFiles;

            // Non-vacuity: a mistyped or moved directory would enumerate to nothing and this scan would pass
            // while checking zero files — the exact failure mode RepoPaths.cs's own remarks describe.
            Assert.True(files.Count >= 100,
                $"only {files.Count} file(s) found under Coverage/ — expected at least 100. The directory may " + "have moved, or RepoPaths.Tests no longer resolves where this scan expects.");

            var offenders = files
                .Where(p => !File.ReadLines(p).Take(HeaderWindow).Any(l => l.StartsWith(CoversPrefix, StringComparison.Ordinal)))
                .Select(p => Path.GetRelativePath(RepoPaths.Root, p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{offenders.Count} Coverage file(s) have no '// Covers: Type.Method — reason' line in their " + $"first {HeaderWindow} lines, so nothing says what production branch they exercise:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void No_Covers_line_is_left_as_a_TODO()
        {
            var files = CoverageFiles;

            var offenders = files
                .Where(p => File.ReadLines(p).Take(HeaderWindow)
                    .Any(l => l.StartsWith(CoversPrefix, StringComparison.Ordinal) && l.Contains("TODO(opus)", StringComparison.Ordinal)))
                .Select(p => Path.GetRelativePath(RepoPaths.Root, p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{offenders.Count} Coverage file(s) still carry a placeholder '// Covers: TODO(opus)' line — " + "the production method/branch was never pinned down:\n  " + string.Join("\n  ", offenders));
        }
    }
}
