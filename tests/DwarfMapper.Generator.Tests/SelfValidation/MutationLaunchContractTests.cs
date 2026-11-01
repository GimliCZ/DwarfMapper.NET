// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     ARCHITECTURAL RULE: a mutation leg must be launched through <c>Invoke-StrykerLeg</c>, never as a
    ///     bare <c>dotnet stryker</c>, and the launcher must keep its fuse.
    ///     <para>
    ///         DIAGNOSED 2026-08-26. All three <c>stryker-config*.json</c> files ask for the <c>progress</c>
    ///         reporter, which draws a live bar with ANSI cursor control. Interactively that is fine — six
    ///         legs completed normally on 2026-08-23. From a DETACHED process with redirected stdout there is
    ///         no console to address: Stryker created its output directory and then blocked forever, burning
    ///         0.1 s of CPU across 17 processes in twelve seconds. It was misdiagnosed twice as an IDE file
    ///         lock; the DLLs were free both times.
    ///     </para>
    ///     <para>
    ///         Two things follow, and this rule pins both. The launcher SELECTS the reporter from the
    ///         context, so a redirected run gets reporters that need no cursor. And it fuses the leg
    ///         regardless of cause, because a gate that can hang forever is strictly worse than one that
    ///         fails — a failure is information, a hang is a machine occupied all night with nothing to show.
    ///         Verified after the fix: under redirected stdout the leg reached its initial test run (5,142
    ///         tests) in under a minute, exactly where <c>progress</c> used to stop dead.
    ///     </para>
    /// </summary>
    public class MutationLaunchContractTests
    {
        private static string Gate()
        {
            return File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "gate-checks.ps1"));
        }

        private static string Housekeeping()
        {
            return File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "housekeeping.ps1"));
        }

        [Fact]
        public void No_mutation_leg_is_launched_as_a_bare_dotnet_stryker()
        {
            // The launcher itself is the one legal invocation; every caller must route through it.
            var offenders = new List<string>();
            var lines = Housekeeping().Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith('#')) { continue; }

                if (Regex.IsMatch(line, @"^\s*dotnet\s+stryker\b"))
                {
                    offenders.Add($"housekeeping.ps1:{i + 1}: {line.Trim()}");
                }
            }

            Assert.True(offenders.Count == 0,
                "A mutation leg is launched as a bare `dotnet stryker`. That bypasses Invoke-StrykerLeg, so it "
                + "keeps the `progress` reporter under redirected stdout (which hangs forever, diagnosed "
                + "2026-08-26) and has no fuse to kill it. Route it through Invoke-StrykerLeg:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_leg_passes_a_bounded_timeout_to_the_launcher()
        {
            // Call sites only. A PowerShell COMMENT naming the launcher is prose, not a launch, and reading
            // it as one made this test fail on a sentence explaining why the launcher must not be called
            // directly (2026-09-09) — a check that forbids describing itself. The exclusion is narrow: a
            // line whose first non-space character is `#`. A commented-out launch is likewise not a launch.
            var calls = Housekeeping()
                        .Split('\n')
                        .Where(l => !l.TrimStart().StartsWith('#'))
                        .Select(l => Regex.Match(l, @"Invoke-StrykerLeg(?<args>[^\r\n]*)", RegexOptions.ExplicitCapture))
                        .Where(m => m.Success)
                        .ToList();

            // SIX since round 29 added the testing-toolkit verifier leg. The floor read 3 under a sentence
            // saying "the three mutation legs"; rounds 27 and 29 added legs and moved neither.
            Assert.True(calls.Count >= 6,
                $"expected at least the six mutation legs to call Invoke-StrykerLeg; found {calls.Count}");

            foreach (var c in calls)
            {
                Assert.Contains("-TimeoutMinutes", c.Groups["args"].Value, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_launcher_swaps_the_progress_reporter_out_when_stdout_is_redirected()
        {
            var src = Gate();

            // The honest test for "is there a console": false in a terminal, true under a pipe, a file
            // redirect, or a detached task — precisely the cases where `progress` has nothing to draw on.
            Assert.Contains("[Console]::IsOutputRedirected", src, StringComparison.Ordinal);

            // And it must actually override the reporter rather than merely noticing.
            var launcher = Regex.Match(src,
                @"function Invoke-StrykerLeg \{(?<body>[\s\S]*?)\n\}",
                RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));
            Assert.True(launcher.Success, "Invoke-StrykerLeg not found — this rule is reading the wrong file");

            var body = launcher.Groups["body"].Value;

            // Read the ARGUMENTS, not the prose. An earlier version of this rule scanned the whole body for
            // the word "progress" and failed on the comment explaining why progress is swapped out — a test
            // that forbids naming the bug it guards against is a test that will be deleted rather than fixed.
            var reporters = Regex.Matches(body, @"'--reporter',\s*'(?<name>[a-z]+)'", RegexOptions.ExplicitCapture)
                .Select(m => m.Groups["name"].Value)
                .ToArray();

            Assert.Contains("dots", reporters, StringComparer.Ordinal);
            Assert.DoesNotContain("progress", reporters, StringComparer.Ordinal);

            // The machine-readable report is what the band gate reads afterwards, so losing it would make the
            // leg unauditable even when it completes.
            Assert.Contains("json", reporters, StringComparer.Ordinal);
        }

        [Fact]
        public void The_fuse_kills_the_process_tree_and_says_why()
        {
            var body = Regex.Match(Gate(),
                @"function Invoke-StrykerLeg \{(?<body>[\s\S]*?)\n\}",
                RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)).Groups["body"].Value;

            // A fuse that cannot kill what it timed out on is decoration, so the launcher must hold a handle
            // (Start-Process -PassThru) and wait with a deadline rather than blocking on exit.
            Assert.Contains("-PassThru", body, StringComparison.Ordinal);
            Assert.Contains("WaitForExit(", body, StringComparison.Ordinal);

            // /T kills the whole tree: Stryker spawns test hosts, and killing only the parent orphans them
            // onto the cores the next leg needs.
            Assert.Contains("taskkill", body, StringComparison.Ordinal);
            Assert.Contains("/T", body, StringComparison.Ordinal);

            // The throw must name the leg and the deadline, or the failure is as opaque as the hang was.
            Assert.Contains("HUNG", body, StringComparison.Ordinal);
            Assert.Contains("$TimeoutMinutes", body, StringComparison.Ordinal);
        }

        [Fact]
        public void Stdin_is_an_empty_file_rather_than_the_string_NUL()
        {
            var body = Regex.Match(Gate(),
                @"function Invoke-StrykerLeg \{(?<body>[\s\S]*?)\n\}",
                RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)).Groups["body"].Value;

            // -RedirectStandardInput resolves its argument as a PATH. 'NUL' becomes <repo>/NUL, which does
            // not exist, so Start-Process throws before anything launches and the fuse then fires on a null
            // handle — blaming a timeout for a launch failure. Measured: that is exactly what happened on the
            // first attempt at this fix.
            Assert.Contains("-RedirectStandardInput", body, StringComparison.Ordinal);
            Assert.DoesNotContain("-RedirectStandardInput 'NUL'", body, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Round 33. A test failing Stryker's INITIAL run is not one lost test: it puts every static mutant on
        ///     an explicit list of every other test, and Stryker then held 6.8-8.2 GB instead of ~1.1 GB (measured
        ///     2026-10-26): the likeliest cause of CI's generator and pipeline legs dying for weeks, and the cause of
        ///     the 119 MB report the dashboard refused. So both launch sites stop on such a failure, which also
        ///     prints the failing tests by name, rather than letting the leg degrade.
        /// </summary>
        [Fact]
        public void Every_launch_site_breaks_on_an_initial_test_failure()
        {
            const string Flag = "--break-on-initial-test-failure";

            var body = Regex.Match(Gate(),
                @"function Invoke-StrykerLeg \{(?<body>[\s\S]*?)\n\}",
                RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)).Groups["body"].Value;

            // The ARGUMENT, not the prose: the comment above it names the flag too.
            Assert.Matches(@"(?m)^\s*\$stArgs \+= @\('" + Regex.Escape(Flag) + @"'\)", body);

            var ciLaunches = File.ReadAllLines(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"))
                                 .Where(l => !l.TrimStart().StartsWith('#') && Regex.IsMatch(l, @"\bdotnet\s+stryker\b"))
                                 .ToList();

            // Non-vacuity: a workflow that stopped launching Stryker would pass the check below over nothing.
            Assert.NotEmpty(ciLaunches);
            Assert.All(ciLaunches, l => Assert.Contains(Flag, l, StringComparison.Ordinal));

            // CI's generator-project legs launch through the rerolling launcher, which builds its own arguments: the
            // flag must be one of them, not a word in its comments.
            var reroll = Regex.Match(Gate(),
                @"function Invoke-StrykerRerollingBadOrder \{(?<body>[\s\S]*?)\n\}",
                RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));
            Assert.True(reroll.Success, "Invoke-StrykerRerollingBadOrder not found - this rule is reading the wrong file");
            Assert.Matches(@"@\([^)\r\n]*'" + Regex.Escape(Flag) + @"'[^)\r\n]*\)", reroll.Groups["body"].Value);
        }

        /// <summary>
        ///     Round 34. Stryker 4.16 mutates and compiles every source of the project under test whatever the globs
        ///     say, and drops what they exclude only afterwards: on the generator project that was 2:20 of a four-CPU
        ///     leg spent on mutants no leg tests. So CI's legs on that project first mark the sources their globs do
        ///     not name as generated code (<c>Set-UnmutatedSourcesGenerated</c>, held by
        ///     <see cref="UnmutatedSourcesMarkingTests" />), which Stryker compiles without mutating. Every leg whose
        ///     config mutates DwarfMapper.Generator carries the flag that runs the step, no other leg does (their
        ///     projects' source scans have not been run against a marked tree), and the step runs before Stryker.
        /// </summary>
        [Fact]
        public void The_generator_project_legs_compile_only_what_they_mutate()
        {
            const string Generator = "DwarfMapper.Generator.csproj";
            var ci = File.ReadAllLines(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));
            var legs = MatrixLegs(ci);

            // Non-vacuity: the six legs, two of them (generator, pipeline) on the generator project.
            Assert.True(legs.Count >= 6, $"read {legs.Count} mutation legs from ci.yml's matrix.");
            Assert.True(legs.Count(l => ProjectOf(l.Config) == Generator) >= 2, "fewer than two legs mutate " + Generator + ".");
            Assert.All(legs, l => Assert.True(l.Flags.Contains("compile-only-mutated") == (ProjectOf(l.Config) == Generator),
                $"leg '{l.Leg}' ({l.Config} mutates {ProjectOf(l.Config)}) {(l.Flags.Contains("compile-only-mutated") ? "carries" : "lacks")} " +
                "compile-only-mutated: true; exactly the legs on " + Generator + " carry it."));

            var code = ci.Select((text, index) => (Text: text, Index: index)).Where(l => !l.Text.TrimStart().StartsWith('#')).ToList();
            var marking = code.Single(l => l.Text.Contains("Set-UnmutatedSourcesGenerated -ConfigFile $env:CONFIG", StringComparison.Ordinal)).Index;
            var launch = code.Single(l => Regex.IsMatch(l.Text, @"\bdotnet\s+stryker\b")).Index;
            Assert.True(marking < launch, "the marking step must run before the leg's Stryker launch.");

            var step = code.Last(l => l.Index < marking && Regex.IsMatch(l.Text, @"^\s*-\s+name:")).Index;
            Assert.Contains(code, l => l.Index > step && l.Index < marking && Regex.IsMatch(l.Text, @"^\s+if:\s*matrix\.compile-only-mutated\s*$"));
        }

        /// <summary>
        ///     Round 34. Stryker 4.16 visits the test projects of every run in one order per invocation, drawn from its
        ///     parallel project analysis, and on the generator project the draw that puts CompilerTests ahead of
        ///     Generator.Tests took the same leg's mutant phase from 20:55 to 48:29 (3 of 12 full legs drew it). So CI's
        ///     legs on that project launch through <c>Invoke-StrykerRerollingBadOrder</c> (held by
        ///     <see cref="ProjectOrderRerollTests" />), which launches again on that draw. Every leg whose config mutates
        ///     DwarfMapper.Generator carries the flag and no other leg does (no other leg's project costs were measured),
        ///     and the wrapper and the plain launch share one step, chosen by the flag.
        /// </summary>
        [Fact]
        public void The_generator_project_legs_reroll_a_bad_project_order()
        {
            const string Generator = "DwarfMapper.Generator.csproj";
            const string Flag = "reroll-bad-order";
            var ci = File.ReadAllLines(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));
            var legs = MatrixLegs(ci);

            Assert.True(legs.Count >= 6, $"read {legs.Count} mutation legs from ci.yml's matrix.");
            Assert.All(legs, l => Assert.True(l.Flags.Contains(Flag) == (ProjectOf(l.Config) == Generator),
                $"leg '{l.Leg}' ({l.Config} mutates {ProjectOf(l.Config)}) {(l.Flags.Contains(Flag) ? "carries" : "lacks")} " +
                Flag + ": true; exactly the legs on " + Generator + " carry it."));

            var code = ci.Select((text, index) => (Text: text, Index: index)).Where(l => !l.Text.TrimStart().StartsWith('#')).ToList();
            var wrapper = code.Single(l => l.Text.Contains("Invoke-StrykerRerollingBadOrder -ConfigFile $env:CONFIG", StringComparison.Ordinal)).Index;
            var plain = code.Single(l => Regex.IsMatch(l.Text, @"\bdotnet\s+stryker\b")).Index;
            var step = code.Last(l => l.Index < wrapper && Regex.IsMatch(l.Text, @"^\s*-\s+name:")).Index;
            Assert.True(code.Last(l => l.Index < plain && Regex.IsMatch(l.Text, @"^\s*-\s+name:")).Index == step,
                "the rerolling launch and the plain Stryker launch must sit in the same step.");
            Assert.Contains(code, l => l.Index > step && l.Index < wrapper && Regex.IsMatch(l.Text, @"^\s+REROLL:\s*\$\{\{\s*matrix\." + Flag + @"\s*\}\}\s*$"));
            Assert.Contains(code, l => l.Index > step && l.Index < wrapper && l.Text.Contains("if [ \"$REROLL\" = true ]; then", StringComparison.Ordinal));
        }

        /// <summary>The mutation matrix's legs with their config and the flags each sets to <c>true</c>.</summary>
        private static List<(string Leg, string Config, HashSet<string> Flags)> MatrixLegs(string[] ci)
        {
            var legs = new List<(string Leg, string Config, HashSet<string> Flags)>();
            string? leg = null, config = null;
            var flags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in ci)
            {
                var start = Regex.Match(line, @"^\s*-\s+leg:\s*(?<leg>[\w-]+)", RegexOptions.ExplicitCapture);
                var end = leg is not null && Regex.IsMatch(line, @"^\s+timeout-minutes:");
                if (start.Success || end)
                {
                    if (leg is not null)
                    {
                        legs.Add((leg, config ?? "", flags));
                    }

                    if (end)
                    {
                        break;
                    }

                    (leg, config, flags) = (start.Groups["leg"].Value, null, new HashSet<string>(StringComparer.Ordinal));
                    continue;
                }

                var named = Regex.Match(line, @"^\s+config:\s*(?<config>\S+)", RegexOptions.ExplicitCapture);
                if (leg is not null && named.Success)
                {
                    config = named.Groups["config"].Value;
                }

                var flag = Regex.Match(line, @"^\s+(?<flag>[a-z-]+):\s*true\s*$", RegexOptions.ExplicitCapture);
                if (leg is not null && flag.Success)
                {
                    flags.Add(flag.Groups["flag"].Value);
                }
            }

            return legs;
        }

        private static string ProjectOf(string config)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, config)));
            return json.RootElement.GetProperty("stryker-config").GetProperty("project").GetString() ?? "";
        }
    }
}
