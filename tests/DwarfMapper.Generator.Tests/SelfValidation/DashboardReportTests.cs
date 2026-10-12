// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>Write-DashboardReport</c> (<c>scripts/gate-checks.ps1</c>): the payload the mutation job uploads to the
    ///     Stryker dashboard. From the round-31 merge on, the generator and pipeline legs' uploads failed with
    ///     HTTP 413 - their reports had reached ~10.9 MB, ~90 % of it the <c>testFiles</c> list of every test in the
    ///     solution. The function keeps only the tests a mutant references and passes everything else through, so
    ///     the dashboard shows exactly what it did before. Driven through the real function, house pattern.
    /// </summary>
    public sealed class DashboardReportTests
    {
        // t1 kills a mutant, t4 only covers one, t2 and t3 are referenced by nothing; file B ends up empty.
        private const string Fixture = """
            {"schemaVersion":"2","thresholds":{"high":80,"low":60},"projectRoot":"/r",
             "files":{"src/X.cs":{"language":"cs","source":"class X {}","mutants":[
               {"id":"1","mutatorName":"Equality","status":"Killed","killedBy":["t1"],"coveredBy":["t1","t4"],
                "location":{"start":{"line":1,"column":1},"end":{"line":1,"column":2}}},
               {"id":"2","mutatorName":"Block","status":"Ignored","killedBy":[],"coveredBy":[],
                "location":{"start":{"line":1,"column":1},"end":{"line":1,"column":2}}}]}},
             "testFiles":{
               "A.cs":{"source":"","tests":[{"id":"t1","name":"A.kills"},{"id":"t2","name":"A.idle"}]},
               "B.cs":{"source":"","tests":[{"id":"t3","name":"B.idle"}]},
               "C.cs":{"source":"","tests":[{"id":"t4","name":"C.covers"}]}}}
            """;

        [Fact]
        public void Only_the_tests_a_mutant_references_are_uploaded_and_nothing_else_changes()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-dashboard-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "full.json"), Fixture, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "no-tests.json"),
                    """{"schemaVersion":"2","files":{"src/X.cs":{"language":"cs","source":"","mutants":[]}}}""",
                    new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "empty.json"), """{"schemaVersion":"2","files":{}}""",
                    new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    function Read([string]$p) { Get-Content -Raw -LiteralPath $p | ConvertFrom-Json -AsHashtable }
                    Show 'kept' {
                        $null = Write-DashboardReport -Report (Join-Path $WorkDir 'full.json') -Out (Join-Path $WorkDir 'full.out.json')
                        $o = Read (Join-Path $WorkDir 'full.out.json')
                        (@($o.testFiles.Keys | Sort-Object | ForEach-Object { $_ + '=' + (@($o.testFiles[$_].tests | ForEach-Object { $_.id }) -join '+') })) -join ';'
                    }
                    Show 'files-untouched' {
                        $a = Read (Join-Path $WorkDir 'full.json'); $b = Read (Join-Path $WorkDir 'full.out.json')
                        ((ConvertTo-Json $a.files -Depth 50 -Compress) -ceq (ConvertTo-Json $b.files -Depth 50 -Compress)) -and
                        ($b.schemaVersion -eq '2') -and ($b.thresholds.high -eq 80) -and ($b.projectRoot -eq '/r')
                    }
                    Show 'single-test-stays-an-array' {
                        (Get-Content -Raw -LiteralPath (Join-Path $WorkDir 'full.out.json')).Contains('"C.cs":{"source":"","tests":[{')
                    }
                    Show 'no-testFiles' {
                        $null = Write-DashboardReport -Report (Join-Path $WorkDir 'no-tests.json') -Out (Join-Path $WorkDir 'no-tests.out.json')
                        (Read (Join-Path $WorkDir 'no-tests.out.json')).ContainsKey('testFiles')
                    }
                    Show 'empty' { Write-DashboardReport -Report (Join-Path $WorkDir 'empty.json') -Out (Join-Path $WorkDir 'empty.out.json') }
                    """,
                    new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 5, "the dashboard report payload");

                // Exactly the referenced tests: t1 (killedBy) and t4 (coveredBy only); B.cs, whose one test nothing
                // references, is dropped whole rather than left as an empty entry.
                Assert.Equal("OK A.cs=t1;C.cs=t4", cases["kept"]);
                Assert.Equal("OK True", cases["files-untouched"]);
                // One kept test must still serialise as a JSON array - the schema says `tests` is an array, and
                // PowerShell is known to unwrap single-element collections.
                Assert.Equal("OK True", cases["single-test-stays-an-array"]);
                Assert.Equal("OK False", cases["no-testFiles"]);
                Assert.StartsWith("THREW", cases["empty"], StringComparison.Ordinal);
                Assert.Contains("empty population", cases["empty"], StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }
}
