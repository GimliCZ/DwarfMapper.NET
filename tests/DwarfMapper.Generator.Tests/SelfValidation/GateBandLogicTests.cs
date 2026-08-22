// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.Text;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     Proves the R2 mandatory-raise band logic in <c>scripts/gate-checks.ps1</c> is non-vacuous, in BOTH
///     failure directions (research §3, invariant R2): below the floor fails as a regression, a full
///     quantum above the floor fails with the raise-the-floor message, and inside the band passes.
///     <para>
///         The functions are PowerShell because the gates are (<c>housekeeping.ps1</c> dot-sources the
///         file), so the only honest test drives the REAL functions through <c>pwsh</c> against fake
///         inputs — a C# re-implementation would prove a copy, not the gate. All seven scenarios run in
///         ONE <c>pwsh</c> spawn (a battery script printing one structured line per case) to keep the
///         fast-tier cost to a single process start. The fake Stryker reports and the battery script are
///         written to a temp directory only (ARCH-06: registered in <see cref="RepoWriteGuardTests" />).
///     </para>
///     <para>
///         Prerequisite: <c>pwsh</c> on PATH — the same prerequisite <c>housekeeping.ps1</c> itself has
///         (its shebang is <c>/usr/bin/env pwsh</c>; the H8 note tracks documenting it). A machine that
///         cannot run the gates cannot verify them either, so absence is a loud failure, not a skip.
///     </para>
/// </summary>
public class GateBandLogicTests
{
    /// <summary>
    ///     The battery's `break`: the generator leg's real floor, so the fake-report arithmetic mirrors a
    ///     real report. One quantum is 1 point — the gate's own precision (`break` is an integer).
    /// </summary>
    private const int FakeBreak = 71;

    [Fact]
    public void The_band_functions_fail_below_the_floor_fail_a_quantum_above_it_and_pass_within_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-gateband-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Fake Stryker reports against break 71 (the generator leg's real floor, so the arithmetic
            // mirrors a real report): 140/201 = 69.65 % is below; 143 Killed + 1 Timeout = 144/201 =
            // 71.64 % is the measured-at-floor-set value (also proves Timeout counts as detected and
            // Ignored/CompileError never enter the denominator); 146/201 = 72.63 % floors to 72 = break+1.
            WriteTemp(Path.Combine(dir, "report-below.json"), FakeReport(killed: 140, timeout: 0, survived: 50, noCoverage: 11));
            WriteTemp(Path.Combine(dir, "report-within.json"), FakeReport(killed: 143, timeout: 1, survived: 46, noCoverage: 11));
            WriteTemp(Path.Combine(dir, "report-above.json"), FakeReport(killed: 146, timeout: 0, survived: 44, noCoverage: 11));

            // The battery: every scenario prints exactly one "CASE <name>: <OK result|THREW message>" line.
            // Coverage floors are one-decimal (quantum 1.0 pp): floor 78.3 makes 79.2 the top of the band
            // and 79.3 the first raise-demanding value.
            WriteTemp(Path.Combine(dir, "battery.ps1"), """
                param([string]$GateChecks, [string]$WorkDir)
                $ErrorActionPreference = 'Stop'
                . $GateChecks
                function Show([string]$name, [scriptblock]$body) {
                    try {
                        $r = & $body
                        if ($null -eq $r) { $r = 'PASS' }
                        Write-Output "CASE ${name}: OK ${r}"
                    } catch {
                        Write-Output "CASE ${name}: THREW $($_.Exception.Message)"
                    }
                }
                Show 'cov-below'  { Test-CoverageWithinBand -AssemblyName 'Fake' -Measured 77.2 -Floor 78.3 }
                Show 'cov-within' { Test-CoverageWithinBand -AssemblyName 'Fake' -Measured 78.9 -Floor 78.3 }
                Show 'cov-edge'   { Test-CoverageWithinBand -AssemblyName 'Fake' -Measured 79.2 -Floor 78.3 }
                Show 'cov-above'  { Test-CoverageWithinBand -AssemblyName 'Fake' -Measured 79.3 -Floor 78.3 }
                Show 'mut-below'  { Assert-MutationScoreWithinBand -Leg 'fake' -ReportPath (Join-Path $WorkDir 'report-below.json') -Break 71 }
                Show 'mut-within' { Assert-MutationScoreWithinBand -Leg 'fake' -ReportPath (Join-Path $WorkDir 'report-within.json') -Break 71 }
                Show 'mut-above'  { Assert-MutationScoreWithinBand -Leg 'fake' -ReportPath (Join-Path $WorkDir 'report-above.json') -Break 71 }
                """);

            var cases = RunBattery(dir);

            // Below the floor: both gates fail, naming the direction.
            Assert.Contains("fell below", cases["cov-below"], StringComparison.Ordinal);
            Assert.StartsWith("THREW", cases["mut-below"], StringComparison.Ordinal);
            Assert.Contains("below break " + FakeBreak, cases["mut-below"], StringComparison.Ordinal);

            // Inside the band (including its top edge): both gates pass.
            Assert.Equal("OK PASS", cases["cov-within"]);
            Assert.Equal("OK PASS", cases["cov-edge"]);
            Assert.StartsWith("OK", cases["mut-within"], StringComparison.Ordinal);

            // A full quantum above the floor: both gates fail DEMANDING the raise, with the R2 phrase.
            const string raisePhrase = "raise the floor to the measured value in this commit";
            Assert.Contains(raisePhrase, cases["cov-above"], StringComparison.Ordinal);
            Assert.StartsWith("THREW", cases["mut-above"], StringComparison.Ordinal);
            Assert.Contains(raisePhrase, cases["mut-above"], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    ///     Runs the battery script through <c>pwsh</c> against the REAL <c>scripts/gate-checks.ps1</c> and
    ///     returns the parsed CASE lines. Hard-bounded wait (H7: no unbounded loop — a hung pwsh is killed
    ///     and named, never idled on).
    /// </summary>
    private static Dictionary<string, string> RunBattery(string dir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(dir, "battery.ps1"));
        psi.ArgumentList.Add("-GateChecks");
        psi.ArgumentList.Add(Path.Combine(RepoPaths.Root, "scripts", "gate-checks.ps1"));
        psi.ArgumentList.Add("-WorkDir");
        psi.ArgumentList.Add(dir);

        using var process = new Process();
        process.StartInfo = psi;
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Assert.Fail("pwsh (PowerShell 7) is not on PATH — the R2 gate-band functions cannot be "
                        + "verified on this machine, and housekeeping.ps1 itself needs the same "
                        + $"prerequisite (H8). Underlying error: {e.Message}");
        }

        // Async reads BEFORE the bounded wait, so neither a full pipe nor a hung pwsh can defeat the
        // 120 s ceiling (H7: the wait is the termination bound; Kill closes the streams and completes
        // both reads either way).
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The gate-band battery did not finish within 120 s — killed. A band check must "
                        + "be milliseconds; a hang here means gate-checks.ps1 gained blocking behaviour.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        Assert.True(process.ExitCode == 0,
            $"The gate-band battery exited {process.ExitCode} — the harness itself failed rather than a "
            + $"scenario (every scenario catches its own throw).\nstdout:\n{stdout}\nstderr:\n{stderr}");

        var cases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("CASE ", StringComparison.Ordinal)) continue;
            var colon = trimmed.IndexOf(": ", StringComparison.Ordinal);
            cases[trimmed[5..colon]] = trimmed[(colon + 2)..];
        }

        Assert.True(cases.Count == 7,
            $"Expected 7 CASE lines from the battery, got {cases.Count} — a scenario vanished, which is "
            + $"exactly the vacuity this test exists to prevent.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        return cases;
    }

    /// <summary>
    ///     A minimal Stryker-shaped report: the band check counts <c>"status": "…"</c> strings the same
    ///     way <c>Assert-MutantsWereTested</c> does, so only the status entries matter. Ignored and
    ///     CompileError rows are present to prove they never enter the denominator (ruling (b): the H7
    ///     progress guard's mutants report Ignored).
    /// </summary>
    private static string FakeReport(int killed, int timeout, int survived, int noCoverage)
    {
        var sb = new StringBuilder();
        sb.Append("{\"schemaVersion\":\"1\",\"files\":{\"Fake.cs\":{\"mutants\":[");
        var first = true;
        void Append(string status, int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"status\":\"").Append(status).Append("\"}");
            }
        }

        Append("Killed", killed);
        Append("Timeout", timeout);
        Append("Survived", survived);
        Append("NoCoverage", noCoverage);
        Append("Ignored", 7);
        Append("CompileError", 3);
        sb.Append("]}}}");
        return sb.ToString();
    }

    /// <summary>Temp-directory-only write; the single registered raw-write call site of this file.</summary>
    private static void WriteTemp(string path, string content) => File.WriteAllText(path, content);
}
