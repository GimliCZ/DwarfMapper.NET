// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>Assert-ZizmorReportClean</c> (<c>scripts/gate-checks.ps1</c>): what fails the <c>actions-security</c> job
    ///     since round 34. Until then the job only reported zizmor's findings, pending the owner's triage of a first
    ///     baseline; every run read for the flip reported none, so any open finding now fails it. A finding suppressed in
    ///     the workflow's own text does not, and neither a missing nor an empty report nor one without results may read
    ///     as "no findings". Driven through the real function against SARIF fixtures, house pattern.
    /// </summary>
    public sealed class ZizmorGateTests
    {
        [Fact]
        public void An_open_finding_fails_the_job_and_no_report_never_reads_as_none()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-zizmor-gate-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    # One gate run over the given report text ($null: no report at all, which is why the parameter is
                    # untyped: a [string] one turns $null into ""). Returns what the gate printed, then whether it passed.
                    function Gate($sarif) {
                        $path = Join-Path $WorkDir 'zizmor.sarif'
                        Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
                        if ($null -ne $sarif) { Set-Content -LiteralPath $path -Value $sarif -NoNewline }
                        $said = [System.Collections.Generic.List[string]]::new()
                        try {
                            Assert-ZizmorReportClean -Path $path | ForEach-Object { $said.Add("$_") }
                            $outcome = 'clean'
                        } catch {
                            $outcome = 'failed: ' + $_.Exception.Message
                        }
                        $(if ($said.Count) { ($said -join ' | ') + ' -> ' } else { '' }) + $outcome
                    }
                    $finding = '{"ruleId":"template-injection","message":{"text":"code injection via template expansion"},' +
                        '"locations":[{"physicalLocation":{"artifactLocation":{"uri":".github/workflows/ci.yml"},"region":{"startLine":42}}}]'
                    Show 'clean' { Gate '{"version":"2.1.0","runs":[{"results":[]}]}' }
                    Show 'one-finding' { Gate ('{"runs":[{"results":[' + $finding + '}]}]}') }
                    Show 'suppressed-in-source' { Gate ('{"runs":[{"results":[' + $finding + ',"suppressions":[{"kind":"inSource"}]}]}]}') }
                    Show 'no-runs' { Gate '{"runs":[]}' }
                    Show 'run-without-results' { Gate '{"runs":[{"tool":{"driver":{"name":"zizmor"}}}]}' }
                    Show 'empty-report' { Gate '' }
                    Show 'no-report' { Gate $null }
                    """,
                    new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 7, "the zizmor gate");

                Assert.Equal("OK zizmor findings: 0 open, 0 suppressed in source -> clean", cases["clean"]);
                // Annotated on its file and line, counted, and failing.
                Assert.Equal("OK ::error file=.github/workflows/ci.yml,line=42::zizmor template-injection: code injection via template expansion" +
                             " | zizmor findings: 1 open, 0 suppressed in source" +
                             " -> failed: zizmor: 1 open finding(s). Fix each one, or suppress it in the workflow's text with" +
                             " '# zizmor: ignore[<rule>]' and the reason beside it.", cases["one-finding"]);
                // A suppression in the workflow's text is reviewed in its diff, so it does not fail the job.
                Assert.Equal("OK zizmor findings: 0 open, 1 suppressed in source -> clean", cases["suppressed-in-source"]);
                // No analysis is not no findings, whichever way the report is missing.
                Assert.Equal("OK failed: zizmor: the report has no runs, so the tool analysed nothing and no finding count means anything.",
                    cases["no-runs"]);
                Assert.Equal("OK failed: zizmor: a run in the report has no results array, which SARIF reserves for a tool that produced none.",
                    cases["run-without-results"]);
                Assert.Equal("OK failed: zizmor: the report is empty, so no finding count means anything.", cases["empty-report"]);
                Assert.Equal("OK failed: zizmor: no report was written, so no finding count means anything.", cases["no-report"]);
            }
            finally
            {
                if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
            }
        }
    }
}
