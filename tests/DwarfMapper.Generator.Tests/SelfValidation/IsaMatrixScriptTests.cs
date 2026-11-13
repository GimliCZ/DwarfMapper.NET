// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>scripts/isa-matrix.ps1</c> when its one Release build fails. The build's output went to <c>Out-Null</c>, so
    ///     the script said only that the build had failed, never why: on 2026-10-27, the first time the script ran to
    ///     check round 34's CI job, two analyzer errors in an edited test file read as "the Release build failed before
    ///     any configuration ran" and nothing else. Driven through the real script, with a <c>dotnet</c> function that
    ///     fails the build as MSBuild does (a function outranks the executable of the same name).
    /// </summary>
    public sealed class IsaMatrixScriptTests
    {
        [Fact]
        public void A_failed_build_shows_its_errors_before_the_script_stops()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-isa-matrix-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    # One canonical MSBuild error line, then a failed exit.
                    function dotnet { 'C:\src\Planted.cs(3,7): error CS0246: planted build error'; $global:LASTEXITCODE = 1 }
                    Show 'failed-build' {
                        $isa = Join-Path (Split-Path -Parent $GateChecks) 'isa-matrix.ps1'
                        # Streamed into a list, so what the script printed survives the throw that ends it.
                        $printed = [System.Collections.Generic.List[string]]::new()
                        try {
                            & $isa 6>&1 | ForEach-Object { $printed.Add("$_") }
                            $outcome = 'finished'
                        } catch {
                            $outcome = 'threw: ' + $_.Exception.Message
                        }
                        'error shown: ' + [bool]($printed -match 'planted build error') + ' -> ' + $outcome
                    }
                    """,
                    new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 1, "the ISA matrix's build-failure report");

                Assert.Equal("OK error shown: True -> threw: isa-matrix: the Release build failed before any configuration ran",
                    cases["failed-build"]);
            }
            finally
            {
                if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
            }
        }
    }
}
