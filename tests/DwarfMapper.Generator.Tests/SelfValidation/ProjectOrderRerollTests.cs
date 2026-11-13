// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>A bad project-order draw is rerolled</b> (round 34): <c>Invoke-StrykerRerollingBadOrder</c> in
    ///     <c>scripts/gate-checks.ps1</c> launches Stryker, follows the test hosts of <c>DwarfMapper.CompilerTests</c> and
    ///     <c>DwarfMapper.Generator.Tests</c>, and launches again when CompilerTests runs first - the draw that took the
    ///     same generator leg's mutant phase from 20:55 to 48:29. Discovery hosts are brief (CompilerTests' showed in one
    ///     0.25 s sample) and initial-run hosts long, so a costly host seen before any killer host rerolls at once, and
    ///     otherwise the first host to stay alive <c>-LongSeconds</c> decides. What it must never do: take a process that
    ///     is not a test host, or a project whose name only begins with one of the two, for a host; decide on brief hosts
    ///     alone; judge a launch by a host its killed predecessor left behind; reroll past its last attempt; kill a launch
    ///     that has already started on its mutants; lose the exit code it hands back; or drop a leg because it could not
    ///     read the process table.
    ///     <para>
    ///         Driven through <c>pwsh</c> against the REAL functions (<see cref="PwshBattery" />) with a fake Stryker
    ///         script and a process lister that plays each launch's hosts on a schedule counted in the launcher's own
    ///         polls, so no case's verdict depends on how fast the machine is, in a temp directory (ARCH-06: registered
    ///         in <see cref="RepoWriteGuardTests" />).
    ///     </para>
    /// </summary>
    public class ProjectOrderRerollTests
    {
        [Fact]
        public void The_costly_project_running_first_is_rerolled_however_its_draw_shows()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-reroll-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // A stand-in for `dotnet stryker`: records its launch and arguments, sleeps, marks that it finished, exits.
                // A killed launch never writes its done-N marker, which is how a case tells a reroll from a finish. The
                // plan gives each launch its own sleep ('30000,400 7': launch 1 sleeps 30 s, later ones 0.4 s): a launch
                // the case expects to be killed sleeps far longer than any verdict takes, so it can never finish first,
                // and a kept launch exits soon after its verdict - or before it, which keeps it all the same. A launch
                // that must still be running when the launcher asks whether mutation has begun sleeps 'asked': until
                // the case's stand-in for that question has been called (it leaves asked.txt), not for a guessed time.
                WriteTemp(Path.Combine(dir, "fake-stryker.ps1"),
                    """
                    $n = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'launch-*.txt').Count + 1
                    Set-Content -LiteralPath (Join-Path $PSScriptRoot "launch-$n.txt") -Value ($args -join ' ')
                    $plan = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'plan.txt')).Trim() -split ' '
                    $sleeps = $plan[0] -split ','
                    $sleep = $sleeps[[Math]::Min($n, $sleeps.Count) - 1]
                    if ($sleep -eq 'asked') {
                        for ($i = 0; $i -lt 600 -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'asked.txt')); $i++) { Start-Sleep -Milliseconds 50 }
                    } else {
                        Start-Sleep -Milliseconds ([int]$sleep)
                    }
                    Set-Content -LiteralPath (Join-Path $PSScriptRoot "done-$n.txt") -Value 'done'
                    exit ([int]$plan[1])
                    """);

                WriteTemp(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    $ct = 'DwarfMapper.CompilerTests'; $gt = 'DwarfMapper.Generator.Tests'
                    function HostLine([string]$project) {
                        "dotnet exec --runtimeconfig /w/tests/$project/bin/Debug/net10.0/$project.runtimeconfig.json " +
                        "--depsfile /w/tests/$project/bin/Debug/net10.0/$project.deps.json " +
                        "/root/.nuget/packages/microsoft.testplatform.testhost/17.14.1/lib/net8.0/testhost.dll --port 40001 --role client"
                    }
                    function M([string]$line) {
                        $p = Get-TestHostProject -CommandLine $line -Projects $ct, $gt
                        if ($null -eq $p) { 'none' } else { $p }
                    }
                    # Not 'H': PowerShell resolves an alias before a function, and h is Get-History.
                    function Seen([string]$project, [double]$seconds) { [pscustomobject]@{ Project = $project; Seconds = $seconds } }
                    function V([object[]]$hosts) {
                        $v = Get-ProjectOrderVerdict -Hosts $hosts -CostlyProject $ct -KillerProject $gt -LongSeconds 10
                        if ($null -eq $v) { 'undecided' } else { $v }
                    }
                    # One fake leg. Launch N plays the hosts of $launches[N-1]: 'project,from,to[,id]' entries joined by ';',
                    # listed in that order, where from and to count the launcher's POLLS of that launch (1 is its first,
                    # '*' is for good) and never seconds: a host listed in one poll only is brief however slowly the polls
                    # come, and one listed for good turns long, so no verdict here depends on the machine's speed (in
                    # seconds, a 0.15 s host was missed whenever one poll came late, and the case then kept the bad draw).
                    # An explicit id lets a host outlive the launch that showed it.
                    #   -Mutating and -ProbeThrows stand in for the launcher's question "has this launch started on its
                    # mutants?". -TestBin asks it for real instead: the launcher's OWN default probe runs, from the leg's
                    # folder, over a tests/ tree holding the named files - a test bin as Stryker leaves it.
                    function Leg([string]$name, [string]$plan, [string[]]$launches, [switch]$ListerThrows, [switch]$Mutating,
                                 [switch]$ProbeThrows, [string[]]$TestBin) {
                        $dir = Join-Path $WorkDir $name
                        New-Item -ItemType Directory -Path $dir | Out-Null
                        Copy-Item -LiteralPath (Join-Path $WorkDir 'fake-stryker.ps1') -Destination $dir
                        Set-Content -LiteralPath (Join-Path $dir 'plan.txt') -Value $plan
                        $probe = @{}
                        if ($TestBin) {
                            $bin = Join-Path $dir 'tests' 'DwarfMapper.Generator.Tests' 'bin' 'Debug' 'net10.0'
                            New-Item -ItemType Directory -Path $bin | Out-Null
                            foreach ($file in $TestBin) { Set-Content -LiteralPath (Join-Path $bin $file) -Value '' }
                        } elseif ($ProbeThrows) {
                            $probe['MutationBegan'] = { Set-Content -LiteralPath (Join-Path $dir 'asked.txt') -Value ''; throw 'no file system here' }.GetNewClosure()
                        } else {
                            if ($Mutating) { Set-Content -LiteralPath (Join-Path $dir 'mutating.txt') -Value '' }
                            $probe['MutationBegan'] = {
                                Set-Content -LiteralPath (Join-Path $dir 'asked.txt') -Value ''
                                Test-Path -LiteralPath (Join-Path $dir 'mutating.txt')
                            }.GetNewClosure()
                        }
                        $polls = @{}
                        $lister = if ($ListerThrows) { { throw 'no process table here' } } else {
                            {
                                $n = @(Get-ChildItem -LiteralPath $dir -Filter 'launch-*.txt').Count
                                if ($n -lt 1 -or $n -gt $launches.Count) { return }
                                $polls[$n] = 1 + [int]$polls[$n]
                                $i = 0
                                foreach ($entry in @($launches[$n - 1] -split ';' | Where-Object { $_ })) {
                                    $i++
                                    $f = $entry -split ','
                                    if ($polls[$n] -lt [int]$f[1] -or ($f[2] -ne '*' -and $polls[$n] -gt [int]$f[2])) { continue }
                                    $id = if ($f.Count -gt 3) { [int]$f[3] } else { 1000 * $n + $i }
                                    [pscustomobject]@{ Id = $id; CommandLine = "dotnet exec --runtimeconfig /w/tests/$($f[0])/bin/$($f[0]).runtimeconfig.json /nuget/testhost.dll" }
                                }
                            }.GetNewClosure()
                        }
                        Push-Location -LiteralPath $dir   # the default probe looks under the current folder, as on a runner
                        try {
                            $code = Invoke-StrykerRerollingBadOrder -ConfigFile 'leg.json' -PollMilliseconds 50 -LongSeconds 0.3 -ListProcesses $lister `
                                @probe -Command @('pwsh', '-NoProfile', '-NonInteractive', '-File', (Join-Path $dir 'fake-stryker.ps1')) -InformationVariable said
                        } finally { Pop-Location }
                        $launched = @(Get-ChildItem -LiteralPath $dir -Filter 'launch-*.txt').Count
                        $finished = @(Get-ChildItem -LiteralPath $dir -Filter 'done-*.txt' | ForEach-Object { $_.BaseName.Substring(5) } | Sort-Object) -join ','
                        # WHY a launch was kept, in the launcher's own words: a launch that merely ended before any verdict
                        # is kept too, and must not pass for one kept because it was too late to kill.
                        $why = if (@($said | Where-Object { "$_" -match 'too late to reroll' }).Count) { ' too-late' } else { '' }
                        "exit=$code launches=$launched finished=$finished$why"
                    }
                    Show 'host-costly'       { M (HostLine $ct) }
                    Show 'host-killer'       { M (HostLine $gt) }
                    Show 'not-a-host'        { M 'dotnet build /w/tests/DwarfMapper.CompilerTests/DwarfMapper.CompilerTests.csproj' }
                    Show 'assembly-not-host' { M 'dotnet /w/tests/DwarfMapper.CompilerTests/bin/DwarfMapper.CompilerTests.dll' }
                    Show 'longer-name'       { M (HostLine 'DwarfMapper.CompilerTests.Extra') }
                    Show 'other-host'        { M (HostLine 'DwarfMapper.NegativeCases') }
                    Show 'no-hosts'          { V @() }
                    Show 'costly-seen-first' { V @((Seen $ct 0.2)) }
                    Show 'brief-killer'      { V @((Seen $gt 4)) }
                    Show 'both-brief'        { V @((Seen $gt 4), (Seen $ct 0.3)) }
                    Show 'costly-runs-first' { V @((Seen $gt 4), (Seen $ct 10)) }
                    Show 'killer-runs-first' { V @((Seen $gt 4), (Seen $ct 0.3), (Seen $gt 12)) }
                    Show 'reroll-early'      { Leg 'reroll-early' '30000,400 7' @("$ct,1,1;$gt,1,*", "$gt,1,*;$ct,1,1") }
                    Show 'reroll-late'       { Leg 'reroll-late' '30000,400 7' @("$gt,1,1;$ct,2,*", "$gt,1,*") }
                    Show 'stale-host'        { Leg 'stale-host' '30000,400 7' @("$ct,1,*,4242", "$ct,1,*,4242;$gt,1,*") }
                    Show 'out-of-attempts'   { Leg 'out-of-attempts' '30000,30000,400 7' @("$ct,1,*", "$ct,1,*", "$ct,1,*") }
                    Show 'too-late'          { Leg 'too-late' 'asked,400 7' @("$gt,1,1;$ct,2,*") -Mutating }
                    Show 'cannot-tell'       { Leg 'cannot-tell' 'asked,400 7' @("$ct,1,*") -ProbeThrows }
                    Show 'bin-untouched'     { Leg 'bin-untouched' '30000,400 7' @("$ct,1,*", "$gt,1,*") -TestBin 'DwarfMapper.Generator.dll' }
                    Show 'bin-backed-up'     { Leg 'bin-backed-up' '1500,400 7' @("$ct,1,*") -TestBin 'DwarfMapper.Generator.dll', 'DwarfMapper.Generator.dll.stryker-unchanged' }
                    Show 'unreadable'        { Leg 'unreadable' '300 7' @() -ListerThrows }
                    Show 'exits-first'       { Leg 'exits-first' '0 3' @() }
                    Show 'arguments'         { (Get-Content -Raw -LiteralPath (Join-Path $WorkDir 'reroll-late' 'launch-2.txt')).Trim() }
                    """);

                var cases = PwshBattery.Run(dir, 23, "Invoke-StrykerRerollingBadOrder");

                // A test host of either project, by its exact name - nothing else, and not a longer name.
                Assert.Equal("OK DwarfMapper.CompilerTests", cases["host-costly"]);
                Assert.Equal("OK DwarfMapper.Generator.Tests", cases["host-killer"]);
                Assert.Equal("OK none", cases["not-a-host"]);
                Assert.Equal("OK none", cases["assembly-not-host"]);
                Assert.Equal("OK none", cases["longer-name"]);
                Assert.Equal("OK none", cases["other-host"]);

                // A costly host seen first is the bad draw; after a killer host, only a host alive LongSeconds decides,
                // because the costly project's discovery host may have come and gone between polls.
                Assert.Equal("OK undecided", cases["no-hosts"]);
                Assert.Equal("OK reroll", cases["costly-seen-first"]);
                Assert.Equal("OK undecided", cases["brief-killer"]);
                Assert.Equal("OK undecided", cases["both-brief"]);
                Assert.Equal("OK reroll", cases["costly-runs-first"]);
                Assert.Equal("OK keep", cases["killer-runs-first"]);

                // The bad draw is killed before it finishes whichever way it shows, and the good one runs to the end with
                // its exit code handed back. Early: the costly host is listed once, ahead of a killer host in the same poll
                // (the process table lists the older process first), and the next launch shows the two the other way
                // round, which is not the bad draw. Late: the costly host comes after a brief killer host and stays.
                Assert.Equal("OK exit=7 launches=2 finished=2", cases["reroll-early"]);
                Assert.Equal("OK exit=7 launches=2 finished=2", cases["reroll-late"]);

                // A host the killed launch left alive is not the next launch's: judged by it, launch 2 would reroll too.
                Assert.Equal("OK exit=7 launches=2 finished=2", cases["stale-host"]);

                // Three bad draws in a row: the third is kept, not rerolled again.
                Assert.Equal("OK exit=7 launches=3 finished=3", cases["out-of-attempts"]);

                // A bad draw that shows only once Stryker has started on its mutants is kept: killing it then would
                // leave a test bin without its original assembly, or with a mutated one. So is one it cannot ask about.
                // Both launches stay alive until the question has been asked, and the launcher says why it kept them.
                Assert.Equal("OK exit=7 launches=1 finished=1 too-late", cases["too-late"]);
                Assert.Equal("OK exit=7 launches=1 finished=1 too-late", cases["cannot-tell"]);

                // The question as the launcher really asks it, with nothing injected: a test bin Stryker has not touched
                // lets the bad draw be rerolled, and one holding Stryker's backup of the original assembly
                // (<assembly>.stryker-unchanged, TestProjectsInfo.GetBackupName) does not. Without the first of these the
                // default could answer "begun" to everything - a typo, a wrong folder - and the launcher would never
                // reroll on a runner while every case above stayed green.
                Assert.Equal("OK exit=7 launches=2 finished=2", cases["bin-untouched"]);
                Assert.Equal("OK exit=7 launches=1 finished=1 too-late", cases["bin-backed-up"]);

                // What it cannot read keeps the run, and a Stryker that exits before any host shows is not relaunched.
                Assert.Equal("OK exit=7 launches=1 finished=1", cases["unreadable"]);
                Assert.Equal("OK exit=3 launches=1 finished=1", cases["exits-first"]);

                // Every launch carries the config and stops on a red initial test run (MutationLaunchContractTests).
                Assert.Equal("OK --config-file leg.json --break-on-initial-test-failure", cases["arguments"]);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void WriteTemp(string path, string text)
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}
