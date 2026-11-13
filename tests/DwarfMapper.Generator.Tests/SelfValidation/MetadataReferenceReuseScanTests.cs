// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Tests.Contracts;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Only <c>GeneratorTestHarness.ReferenceTo</c> creates a metadata reference from an assembly file.</b>
    ///     <para>
    ///         <see cref="MetadataReference.CreateFromFile(string, MetadataReferenceProperties, DocumentationProvider)" />
    ///         copies the whole image into native memory, which is freed only when the reference is finalized. About
    ///         twenty helpers in this project built their references inline on every compile, from the corlib alone or
    ///         from every assembly the host had loaded. On Linux, in Stryker's serial mode, the test host reached 5-7 GB
    ///         while the GC's committed heap stayed under 0.41 GB: glibc arenas holding image copies that the GC had no
    ///         reason to finalize yet (round 34 A1). That host is what left the 16 GB mutation runners with 0.4-0.6 GB.
    ///         The harness creates each file's reference once per host and shares it.
    ///     </para>
    /// </summary>
    public sealed class MetadataReferenceReuseScanTests
    {
        // Built from the API's own names, so this file does not match itself.
        private const string Needle = nameof(MetadataReference) + "." + nameof(MetadataReference.CreateFromFile) + "(";

        [Fact]
        public void Only_the_harness_creates_a_metadata_reference_from_a_file()
        {
            var project = Path.Combine(RepoPaths.Tests, "DwarfMapper.Generator.Tests");
            var files = RepoPaths.SourceFiles(project).ToList();
            Assert.True(files.Count >= 100, $"found only {files.Count} source file(s) under {project}; this scan is reading the wrong thing.");

            // Non-vacuity: the one call that should exist is found, so a needle that stopped matching cannot pass.
            var harness = Path.Combine(project, "GeneratorTestHarness.cs");
            Assert.Single(CallSites(harness));

            var offenders = files
                .Where(f => !string.Equals(f, harness, StringComparison.OrdinalIgnoreCase))
                .SelectMany(f => CallSites(f).Select(line => Path.GetRelativePath(project, f).Replace('\\', '/') + ":" + line))
                .ToList();

            Assert.True(offenders.Count == 0,
                $"{offenders.Count} call(s) outside the harness create a metadata reference from a file, each a fresh native " +
                "copy of the whole image on every call: [" + string.Join(", ", offenders) + "]. Use " +
                "GeneratorTestHarness.ReferenceTo(...), or GeneratorTestHarness.LoadedAssemblyReferences() for the set of " +
                "every assembly the host has loaded.");
        }

        /// <summary>The 1-based lines that call the API; documentation comments name it without calling it.</summary>
        private static List<int> CallSites(string file)
        {
            return File.ReadLines(file)
                .Select((text, index) => (Text: text, Line: index + 1))
                .Where(x => x.Text.Contains(Needle, StringComparison.Ordinal) &&
                            !x.Text.TrimStart().StartsWith("///", StringComparison.Ordinal))
                .Select(x => x.Line)
                .ToList();
        }
    }
}
