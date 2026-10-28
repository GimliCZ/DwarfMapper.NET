// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Server GC runs where it was measured to pay, and nowhere it costs.</b> Round 34.
    ///     <para>
    ///         The deep tier's and the power set's compilations allocate tens of gigabytes, and workstation GC stops every
    ///         test thread while one thread collects. Measured on Generator.Tests: the deep tier 53.3 to 44.9 s on 12 logical
    ///         CPUs and 80.0 to 68.2 s on 4 (a hosted runner's shape), the power set 6:19 to 5:05 on 12. On 4 CPUs it bought
    ///         nothing measurable on build-test's run, and a serial Stryker session is slower under it (5.5 to 7.0 s, E3).
    ///         So no test project's runtimeconfig turns it on; CI's power-set job sets it, and housekeeping sets it for its
    ///         deep and exhaustion stages and puts it back before the stages that time code or run Stryker.
    ///     </para>
    /// </summary>
    public class ServerGcScopeScanTests
    {
        private static readonly Regex SetsServerGc = new(@"DOTNET_gcServer\s*[:=]\s*['""]?1", RegexOptions.CultureInvariant);

        private static readonly Regex TopLevelKey = new(@"^  (?<key>[A-Za-z0-9_-]+):\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex StageBanner = new(@"(?=Write-Host [""']== )", RegexOptions.CultureInvariant);

        private const string PutBack = "$env:DOTNET_gcServer = $gcServerBefore";

        [Fact]
        public void No_test_project_turns_server_GC_on_in_its_runtimeconfig()
        {
            var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, "tests"), "*.csproj", SearchOption.AllDirectories)
                                 .Concat(new[] { "Directory.Build.props", "Directory.Build.targets" }.Select(f => Path.Combine(RepoPaths.Root, f)))
                                 .Where(File.Exists)
                                 .ToList();

            // Non-vacuity: ten test projects and the two build files.
            Assert.True(files.Count >= 10, $"read only {files.Count} project files.");

            var offenders = files.Where(f => Regex.IsMatch(File.ReadAllText(f), @"<ServerGarbageCollection>\s*true|System\.GC\.Server", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                                 .Select(f => Path.GetRelativePath(RepoPaths.Root, f))
                                 .ToList();

            Assert.True(offenders.Count == 0,
                "server GC in a test project's runtimeconfig reaches every Stryker session, which runs slower under it: [" +
                string.Join(", ", offenders) + "]. Set DOTNET_gcServer for the run that measured a gain instead.");
        }

        [Fact]
        public void In_the_workflows_only_the_power_set_job_sets_server_GC()
        {
            var setting = new List<string>();
            foreach (var workflow in Directory.GetFiles(Path.Combine(RepoPaths.Root, ".github", "workflows"), "*.yml"))
            {
                var text = File.ReadAllText(workflow);
                var keys = TopLevelKey.Matches(text);
                for (var i = 0; i < keys.Count; i++)
                {
                    var end = i + 1 < keys.Count ? keys[i + 1].Index : text.Length;
                    if (SetsServerGc.IsMatch(text[keys[i].Index..end]))
                    {
                        setting.Add(Path.GetFileName(workflow) + ": " + keys[i].Groups["key"].Value);
                    }
                }
            }

            Assert.Equal(new[] { "ci.yml: fuzz-exhaustion" }, setting);
        }

        [Fact]
        public void Housekeeping_sets_server_GC_for_the_deep_and_exhaustion_stages_only_and_puts_it_back()
        {
            var stages = StageBanner.Split(File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "housekeeping.ps1")));
            var setting = stages.Where(s => SetsServerGc.IsMatch(s)).ToList();

            Assert.Equal(2, setting.Count);
            Assert.StartsWith("Write-Host \"== 1/4 ", setting[0], StringComparison.Ordinal);
            Assert.StartsWith("Write-Host \"== 2/4 ", setting[1], StringComparison.Ordinal);

            // Stage 1 runs the fast tier too, where server GC measured no gain: it is set in the -Deep branch only.
            var deepBranch = setting[0].IndexOf("if ($Deep) {", StringComparison.Ordinal);
            var set = SetsServerGc.Match(setting[0]).Index;
            Assert.True(deepBranch >= 0 && deepBranch < set && set < setting[0].IndexOf("\n    }", deepBranch, StringComparison.Ordinal),
                "stage 1 sets server GC outside its -Deep branch.");

            foreach (var stage in setting)
            {
                Assert.True(stage.IndexOf(PutBack, SetsServerGc.Match(stage).Index, StringComparison.Ordinal) > 0,
                    "a housekeeping stage sets server GC and does not put it back before the next stage: " + stage[..60]);
            }
        }
    }
}
