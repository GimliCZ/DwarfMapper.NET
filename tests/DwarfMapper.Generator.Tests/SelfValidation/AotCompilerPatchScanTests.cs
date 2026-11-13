// SPDX-License-Identifier: GPL-2.0-only

using System.Text.Json;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A project compiled ahead of time locks a patched compiler.</b> Round 34.
    ///     <para>
    ///         <c>PublishAot</c> makes the SDK reference <c>Microsoft.DotNet.ILCompiler</c> at the runtime patch the SDK
    ///         shipped with: 10.0.1 for the pinned 10.0.101. On 2026-11-03 that version drew advisory GHSA-p3jp-9v83-76w9
    ///         (the Brotli decoder's out-of-bounds write, high, fixed in 10.0.14). The audit in Directory.Build.props then
    ///         failed every restore of the solution, as it is there to, and master's nightly was red in 18 of 23 jobs from
    ///         2026-11-04 on. Directory.Build.targets now names the runtime patch such a project is published against
    ///         (<c>RuntimeFrameworkVersion</c>, the SDK's own property, which it takes the compiler pack from), and the
    ///         lock files carry that compiler.
    ///     </para>
    ///     <para>
    ///         What must not happen quietly: a new ahead-of-time project, or a regenerated lock file, resolving the SDK's
    ///         own older compiler again; and the override outliving its reason when the SDK pin itself moves.
    ///     </para>
    /// </summary>
    public class AotCompilerPatchScanTests
    {
        private const string Compiler = "Microsoft.DotNet.ILCompiler";

        /// <summary>The framework the override is written for; a PublishAot project on another one is a new question.</summary>
        private const string Framework = "net10.0";

        /// <summary>The runtime patch Directory.Build.targets gives every PublishAot project on <see cref="Framework" />.</summary>
        private static Version NamedPatch()
        {
            var targets = File.ReadAllText(Path.Combine(RepoPaths.Root, "Directory.Build.targets"));
            var named = Regex.Match(targets,
                @"<PropertyGroup Condition=""'\$\(PublishAot\)' == 'true' and '\$\(TargetFramework\)' == '" + Regex.Escape(Framework) + @"'"">" +
                @"\s*<RuntimeFrameworkVersion>(?<patch>\d+\.\d+\.\d+)</RuntimeFrameworkVersion>\s*</PropertyGroup>",
                RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(10));
            Assert.True(named.Success,
                "Directory.Build.targets no longer gives every PublishAot project on " + Framework + " a " +
                "RuntimeFrameworkVersion: such a project then restores the pinned SDK's own " + Compiler + ", which the " +
                "NuGet audit refuses (GHSA-p3jp-9v83-76w9).");
            return Version.Parse(named.Groups["patch"].Value);
        }

        [Fact]
        public void Every_project_compiled_ahead_of_time_locks_the_compiler_at_the_patch_the_build_names()
        {
            var patch = NamedPatch().ToString();
            var projects = new[] { "src", "samples", "tests", "benchmarks" }
                           .Select(d => Path.Combine(RepoPaths.Root, d))
                           .Where(Directory.Exists)
                           .SelectMany(d => Directory.GetFiles(d, "*.csproj", SearchOption.AllDirectories))
                           .Where(p => !Regex.IsMatch(p, @"[\\/](bin|obj)[\\/]"))
                           .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<PublishAot>\s*true\s*</PublishAot>", RegexOptions.IgnoreCase))
                           .ToList();

            // Non-vacuity: the AOT gate's sample and the AOT benchmark.
            Assert.True(projects.Count >= 2, $"found {projects.Count} PublishAot project(s); the AOT sample and the AOT bench are two.");

            var offenders = new List<string>();
            foreach (var project in projects)
            {
                var name = Path.GetRelativePath(RepoPaths.Root, project).Replace('\\', '/');

                // The override reaches a project on its one framework only; any other needs its own answer first.
                Assert.True(Regex.IsMatch(File.ReadAllText(project), @"<TargetFramework>\s*" + Regex.Escape(Framework) + @"\s*</TargetFramework>"),
                    name + " is compiled ahead of time and does not target exactly " + Framework + ": the runtime patch " +
                    "Directory.Build.targets names does not reach it. Decide which compiler it restores, and extend the " +
                    "override and this test.");
                var lockFile = Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json");
                Assert.True(File.Exists(lockFile), name + " has no packages.lock.json.");

                using var json = JsonDocument.Parse(File.ReadAllText(lockFile));
                var seen = 0;
                foreach (var graph in json.RootElement.GetProperty("dependencies").EnumerateObject())
                foreach (var package in graph.Value.EnumerateObject())
                {
                    // The compiler and its runtime.<rid> pack, in the framework graph and in each runtime's.
                    if (!package.Name.EndsWith(Compiler, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    seen++;
                    var resolved = package.Value.GetProperty("resolved").GetString();
                    if (resolved != patch)
                    {
                        offenders.Add($"{name} [{graph.Name}] {package.Name} {resolved}");
                    }
                }

                Assert.True(seen >= 1, name + "'s lock file names no " + Compiler + " at all: is it still a PublishAot project?");
            }

            Assert.True(offenders.Count == 0,
                "a PublishAot project's lock file resolves a compiler other than the " + patch + " that " +
                "Directory.Build.targets names. Regenerate it (dotnet restore <project> --force-evaluate) and check the " +
                "restore still passes the audit:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        ///     The override exists because the pinned SDK's own runtime is older than the patch. When the pin moves to an
        ///     SDK whose runtime is at least that new, the override is dead weight that would hold the compiler BACK on the
        ///     next move, so it has to go; and when the pin leaves the 10.0 band altogether the question has to be asked
        ///     again rather than answered by a number written for another SDK.
        /// </summary>
        [Fact]
        public void The_override_is_newer_than_the_pinned_SDKs_own_runtime()
        {
            var patch = NamedPatch();

            // ci.yml's roslyn-forward-compat and preview-sdk-canary jobs delete global.json from their working copy to
            // run on a newer SDK, then run this suite. There is no pin to compare there, and the pinned jobs hold the
            // rule. (Found by running the suite without the file before the first push: FileNotFoundException.)
            var pin = Path.Combine(RepoPaths.Root, "global.json");
            if (!File.Exists(pin))
            {
                return;
            }

            using var global = JsonDocument.Parse(File.ReadAllText(pin));
            var sdk = Version.Parse(global.RootElement.GetProperty("sdk").GetProperty("version").GetString()!);

            Assert.True(sdk.Major == patch.Major && sdk.Minor == patch.Minor,
                $"the SDK pin ({sdk}) left the {patch.Major}.{patch.Minor} band the override was written for. Restore a " +
                "PublishAot project without the override, read what compiler the new SDK resolves and whether the audit " +
                "accepts it, then delete the override or rewrite it - and this test with it.");

            // An SDK's patch number carries its runtime's in the last two digits: 10.0.101 ships runtime 10.0.1.
            var sdkRuntime = new Version(sdk.Major, sdk.Minor, sdk.Build % 100);
            Assert.True(patch > sdkRuntime,
                $"the pinned SDK {sdk} ships runtime {sdkRuntime}, which is at least the {patch} the override names: the " +
                "SDK's own compiler is new enough now. Delete the override from Directory.Build.targets, regenerate the " +
                "PublishAot projects' lock files and delete this class.");
        }
    }
}
