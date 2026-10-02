// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Linq;
using DwarfMapper.Generator.Tests.Contracts;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round32
{
    /// <summary>
    ///     A throwaway probe never reaches the tree. Probes are named <c>ZZ*.cs</c> and deleted before the commit
    ///     (round 31 G7, protocol G5).
    ///     <para>
    ///         Until round 32 the only check was a line in <c>round31-audit.sh</c>, and that script never exits
    ///         non-zero for a TODO. A probe left in <c>tests/</c> or <c>src/</c> compiles into the build like any
    ///         other file, so it could change what the suite measures without anyone seeing it (round 32 finding,
    ///         item 4).
    ///     </para>
    /// </summary>
    public sealed class ProbeFileScanTests
    {
        [Fact]
        public void No_throwaway_probe_file_is_left_in_a_project_tree()
        {
            var roots = new[] { RepoPaths.Src, RepoPaths.Tests, RepoPaths.Samples, Path.Combine(RepoPaths.Root, "benchmarks") };
            var files = roots.SelectMany(RepoPaths.SourceFiles).ToList();

            // Non-vacuity: the scan must be looking at the repository's sources, not at an empty directory.
            Assert.True(files.Count > 500, $"the scan saw only {files.Count} .cs files under src/, tests/, samples/ and benchmarks/.");

            var probes = files
                .Where(p => Path.GetFileName(p).StartsWith("ZZ", StringComparison.Ordinal))
                .Select(p => Path.GetRelativePath(RepoPaths.Root, p))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.True(probes.Count == 0,
                "Throwaway probe file(s) left in a project tree. A ZZ*.cs probe compiles into the build like any other " +
                "file, so delete it before committing (protocol G5):\n  " + string.Join("\n  ", probes));
        }
    }
}
