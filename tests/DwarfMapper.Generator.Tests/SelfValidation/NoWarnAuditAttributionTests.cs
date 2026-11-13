// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>Get-OwnWarningCodes</c> (<c>scripts/gate-checks.ps1</c>): the warning codes a build raised in the one
    ///     project <c>scripts/nowarn-audit.ps1</c> is auditing. The audit clears <c>NoWarn</c> with a GLOBAL property,
    ///     which reaches every referenced project as well, and it used to count every warning in the output. So
    ///     DwarfMapper.Testing's own CA5394 (System.Random in ObjectFactoryV2) was credited to Benchmarks, Conformance
    ///     and Gallery, whose CA5394 suppressed nothing (round 34). MSBuild ends every warning line with the project it
    ///     belongs to; only the audited project's lines count. Driven through the real function, house pattern.
    /// </summary>
    public sealed class NoWarnAuditAttributionTests
    {
        [Fact]
        public void Only_the_warnings_MSBuild_attributes_to_the_audited_project_count()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-nowarn-audit-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    $output = @"
                    C:\r\src\Lib\Lib.cs(3,5): warning CA5394: Random is an insecure random number generator. [C:\r\src\Lib\Lib.csproj]
                    C:\r\app\App\Program.cs(9,1): warning CA1303: Do not pass literals as localized parameters [C:\r\app\App\App.csproj]
                    C:\r\app\App\Program.cs(10,1): warning CA1303: Do not pass literals as localized parameters [C:\r\app\App\App.csproj]
                    CSC : warning DWARF038: an implicit conversion is applied [C:\r\app\App\App.csproj]
                    C:\r\app\App\Program.cs(11,1): error CS0103: an error is not a warning [C:\r\app\App\App.csproj]
                    "@
                    Show 'own' { (@(Get-OwnWarningCodes -BuildOutput $output -Project 'C:\r\app\App\App.csproj') | Sort-Object) -join ',' }
                    Show 'path-case' { (@(Get-OwnWarningCodes -BuildOutput $output -Project 'c:\R\APP\App\app.csproj') | Sort-Object) -join ',' }
                    Show 'referenced' { @(Get-OwnWarningCodes -BuildOutput $output -Project 'C:\r\src\Lib\Lib.csproj') -join ',' }
                    Show 'unbuilt' { @(Get-OwnWarningCodes -BuildOutput $output -Project 'C:\r\elsewhere\None.csproj').Count }
                    """,
                    new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 4, "the NoWarn audit's attribution");

                // App's own two codes, the location-less DWARF038 included; Lib's CA5394 is not App's.
                Assert.Equal("OK CA1303,DWARF038", cases["own"]);
                Assert.Equal("OK CA1303,DWARF038", cases["path-case"]);
                Assert.Equal("OK CA5394", cases["referenced"]);
                Assert.Equal("OK 0", cases["unbuilt"]);
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
