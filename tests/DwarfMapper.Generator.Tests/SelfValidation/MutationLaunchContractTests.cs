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
    }
}
