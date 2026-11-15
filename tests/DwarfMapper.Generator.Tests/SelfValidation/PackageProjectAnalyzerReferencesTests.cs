// SPDX-License-Identifier: GPL-2.0-only

using System.Xml.Linq;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>src/DwarfMapper/DwarfMapper.csproj</c> references the analyzer projects - both generator builds and the code
    ///     fixes - only while packing (round 35 T23). Both generator builds write <c>DwarfMapper.Generator.dll</c>, and a
    ///     build that passes ONE global <c>OutDir</c> to the whole graph puts them in one folder, where the last one written
    ///     wins: BenchmarkDotNet's child build does exactly that, and the nightly benchmark smoke failed with CS9057 - the
    ///     Roslyn 5.12 build, loaded by SDK 10's compiler through the benchmark's reference to the 5.0 build (measured
    ///     2026-11-15, <c>housekeeping.ps1 -Nightly</c>). The package project needs neither build to compile, and loads
    ///     neither as an analyzer; it needs them built when it packs, and the SDK marks a pack with <c>_IsPacking</c>.
    /// </summary>
    public sealed class PackageProjectAnalyzerReferencesTests
    {
        private const string PackingOnly = "'$(_IsPacking)' == 'true'";

        [Fact]
        public void The_package_project_references_the_analyzer_projects_only_while_packing()
        {
            var path = Path.Combine(RepoPaths.Root, "src", "DwarfMapper", "DwarfMapper.csproj");
            var project = XDocument.Load(path);
            var analyzerReferences = project.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Where(e => ((string?)e.Attribute("Include") ?? "").Contains("DwarfMapper.Generator", StringComparison.Ordinal) ||
                            ((string?)e.Attribute("Include") ?? "").Contains("DwarfMapper.CodeFixes", StringComparison.Ordinal))
                .ToList();

            // Both generator builds and the code fixes: a reference that vanished would make this test vacuous.
            Assert.Equal(3, analyzerReferences.Count);
            var unconditioned = analyzerReferences
                .Where(e => !IsPackingOnly(e) && !IsPackingOnly(e.Parent!))
                .Select(e => (string?)e.Attribute("Include"))
                .ToList();
            Assert.True(unconditioned.Count == 0,
                "src/DwarfMapper/DwarfMapper.csproj references these analyzer projects outside a pack (condition them, or " +
                "their ItemGroup, on " + PackingOnly + "): " + string.Join(", ", unconditioned) + ". A build with one " +
                "global OutDir writes both generator builds' DwarfMapper.Generator.dll into one folder, and the last one " +
                "written is what every analyzer reference to either build then loads.");
        }

        private static bool IsPackingOnly(XElement element)
        {
            return string.Equals(((string?)element.Attribute("Condition"))?.Trim(), PackingOnly, StringComparison.Ordinal);
        }
    }
}
