// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.Xml.Linq;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     No two project files write the same <c>&lt;AssemblyName&gt;</c> (round 35 T23). CycloneDX - the sbom job's tool,
    ///     6.2.0 and 6.3.1 alike - reads each project file as XML, names a project by the AssemblyName written under
    ///     <c>/Project/PropertyGroup</c> (else by its file name), and keys a project's references by that name, imports and
    ///     conditions unread. The Roslyn 5.12 generator project wrote the 5.0 one's name, and the job died on
    ///     <c>DwarfMapper.csproj</c>: "An item with the same key has already been added. Key: DwarfMapper.Generator"
    ///     (2026-11-15, run 43673564878). Both builds keep that assembly name - the coverage floors and Codecov merge them
    ///     under it - so the 5.12 project sets it from an imported file, where CycloneDX does not look, and this test reads
    ///     the project files the way CycloneDX does.
    /// </summary>
    public sealed class ProjectFileAssemblyNameTests
    {
        [Fact]
        public void No_two_project_files_write_the_same_assembly_name()
        {
            var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var projects = TrackedProjectFiles();
            foreach (var relative in projects)
            {
                var project = XDocument.Load(Path.Combine(RepoPaths.Root, relative));
                var written = project.Root?
                    .Elements().Where(e => e.Name.LocalName == "PropertyGroup")
                    .Elements().FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value.Trim();
                var fileName = Path.GetFileNameWithoutExtension(relative);
                var name = string.IsNullOrEmpty(written) ? fileName : written.Replace("$(MSBuildProjectName)", fileName, StringComparison.Ordinal);
                if (!names.TryGetValue(name, out var list))
                {
                    names[name] = list = [];
                }

                list.Add(relative);
            }

            // Every project in the repository, so the scan cannot pass by reading nothing.
            Assert.True(projects.Count >= 20, $"Only {projects.Count} project files found.");
            var duplicates = names.Where(n => n.Value.Count > 1).Select(n => n.Key + ": " + string.Join(", ", n.Value)).ToList();
            Assert.True(duplicates.Count == 0,
                "Project files that write the same <AssemblyName> - CycloneDX keys project references by it and stops on " +
                "the duplicate. Set it from an imported file instead (see src/DwarfMapper.Generator.Roslyn512):\n  " +
                string.Join("\n  ", duplicates));
        }

        private static List<string> TrackedProjectFiles()
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = RepoPaths.Root
            };
            psi.ArgumentList.Add("ls-files");
            psi.ArgumentList.Add("*.csproj");
            using var git = Process.Start(psi)!;
            var output = git.StandardOutput.ReadToEnd();
            Assert.True(git.WaitForExit(60_000), "git ls-files did not finish within 60 s.");
            Assert.Equal(0, git.ExitCode);
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }
}
