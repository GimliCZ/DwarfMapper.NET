// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Contracts
{
    /// <summary>
    ///     Repository locations, resolved once. Three test files carried a private copy of this walk; a fourth
    ///     copy is how one of them ends up pointed at a directory that has since moved, passing vacuously.
    /// </summary>
    public static class RepoPaths
    {
        public static string Root { get; } = FindRoot();

        public static string Src => Path.Combine(Root, "src");

        public static string Tests => Path.Combine(Root, "tests");

        public static string Samples => Path.Combine(Root, "samples");

        public static string GeneratorSrcDir => Path.Combine(Src, "DwarfMapper.Generator");

        public static string PipelineDir => Path.Combine(GeneratorSrcDir, "Pipeline");

        /// <summary>The four consumer-shaped assemblies — projects that USE DwarfMapper rather than test it.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance",
            "CA1819:Properties should not return arrays",
            Justification = "A small, fixed, read-only set of paths consumed by index — mutability of the backing " + "array is not a real risk in a test-only static catalogue.")]
        public static string[] ConsumerRoots { get; } =
        [
            Path.Combine(Tests, "DwarfMapper.ConsumerTests"),
            Path.Combine(Tests, "DwarfMapper.DifferentialTests"),
            Path.Combine(Tests, "DwarfMapper.NegativeCases"),
            Path.Combine(Tests, "DwarfMapper.IntegrationTests")
        ];

        /// <summary>Every .cs file under <paramref name="dir" />, skipping build output.</summary>
        public static IEnumerable<string> SourceFiles(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return [];
            }

            var sep = Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !p.Contains($"{sep}bin{sep}", StringComparison.Ordinal));
        }

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && dir.GetFiles("DwarfMapper.NET.sln").Length == 0)
                dir = dir.Parent;

            Assert.True(dir is not null, "Could not locate the repository root (DwarfMapper.NET.sln).");
            return dir.FullName;
        }
    }
}
