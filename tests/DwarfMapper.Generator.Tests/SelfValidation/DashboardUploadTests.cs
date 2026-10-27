// SPDX-License-Identifier: GPL-2.0-only

using System.Text;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>Send-DashboardReport</c> (<c>scripts/gate-checks.ps1</c>): what the mutation job's upload does when the
    ///     Stryker dashboard gives no answer about the report. On 2026-10-27 the first nightly on round 33's code
    ///     measured and gated all six legs, and the runtime and codefixes legs still went red: the dashboard answered
    ///     their PUTs with HTTP 504 four minutes later, while the uploads just before and after went through. A 5xx,
    ///     or no connection at all, is now sent once more after a pause; a 4xx is an answer about the request and
    ///     fails at once. Driven through the real function with a scripted transport and clock, house pattern.
    /// </summary>
    public sealed class DashboardUploadTests
    {
        private const string StepName = "Publish the verified report to the Stryker dashboard";

        [Fact]
        public void A_dashboard_that_gave_no_answer_is_asked_once_more_and_a_refusal_is_not()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-dashboard-upload-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    # One upload against a scripted dashboard: each send takes the next status off the list, each pause
                    # is recorded instead of slept. Returns what the transport and the clock saw, then the outcome.
                    function Upload([string[]] $answers) {
                        $queue = [System.Collections.Generic.Queue[string]]::new($answers)
                        $seen = [System.Collections.Generic.List[string]]::new()
                        $send = { param($url, $payload, $response) $seen.Add('send'); $queue.Dequeue() }.GetNewClosure()
                        $pause = { param($seconds) $seen.Add("pause $seconds") }.GetNewClosure()
                        try {
                            $null = Send-DashboardReport -Url 'https://dashboard.invalid/api/reports/p/v?module=m' `
                                -Payload (Join-Path $WorkDir 'payload.json') -Response (Join-Path $WorkDir 'response.json') `
                                -Send $send -Pause $pause
                            $outcome = 'published'
                        } catch {
                            $outcome = 'failed: ' + $_.Exception.Message
                        }
                        ($seen -join ', ') + ' -> ' + $outcome
                    }
                    Show 'ok' { Upload @('200') }
                    Show 'created' { Upload @('201') }
                    Show 'gateway-timeout-then-ok' { Upload @('504', '200') }
                    Show 'no-connection-then-ok' { Upload @('000', '201') }
                    Show 'refused' { Upload @('413') }
                    Show 'down-twice' { Upload @('503', '502') }
                    Show 'no-status' { Upload @('') }
                    """,
                    new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 7, "the dashboard upload's retry");

                Assert.Equal("OK send -> published", cases["ok"]);
                Assert.Equal("OK send -> published", cases["created"]);
                // The two answers that say nothing about the report: one more send, after the pause.
                Assert.Equal("OK send, pause 120, send -> published", cases["gateway-timeout-then-ok"]);
                Assert.Equal("OK send, pause 120, send -> published", cases["no-connection-then-ok"]);
                // A 4xx is the dashboard's answer about the request (key, project name, size), so sending the same
                // request again cannot change it: no pause, no second send.
                Assert.StartsWith("OK send -> failed: ", cases["refused"], StringComparison.Ordinal);
                Assert.Contains("HTTP 413", cases["refused"], StringComparison.Ordinal);
                // One retry, not a loop: a dashboard down for longer still fails the leg, naming the last status.
                Assert.StartsWith("OK send, pause 120, send -> failed: ", cases["down-twice"], StringComparison.Ordinal);
                Assert.Contains("HTTP 502", cases["down-twice"], StringComparison.Ordinal);
                // No status at all means the transport itself did not run, which a retry would only hide.
                Assert.StartsWith("OK send -> failed: ", cases["no-status"], StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        /// <summary>
        ///     The function only helps if the job calls it: the step that sent its own <c>curl</c> PUT is the one
        ///     that failed on the 504s. Comment lines are skipped, so the step may still explain itself.
        /// </summary>
        [Fact]
        public void The_mutation_job_uploads_through_the_function_that_retries()
        {
            var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));
            var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith("- name: " + StepName, StringComparison.Ordinal));
            Assert.True(start >= 0, $"ci.yml has no step named '{StepName}'; this scan is reading the wrong thing.");

            // The step runs until the next line at its own indentation or less: the next step, or a comment above it.
            var indent = lines[start].IndexOf('-', StringComparison.Ordinal);
            var commands = lines.Skip(start + 1)
                                .TakeWhile(l => l.Trim().Length == 0 || l.Length - l.TrimStart().Length > indent)
                                .Where(l => !l.TrimStart().StartsWith('#'))
                                .ToList();
            Assert.True(commands.Count >= 10, $"the '{StepName}' step read as {commands.Count} line(s); this scan is reading the wrong thing.");

            Assert.Contains(commands, l => l.Contains("Send-DashboardReport", StringComparison.Ordinal));
            Assert.DoesNotContain(commands, l => l.Contains("curl", StringComparison.Ordinal));
        }
    }
}
