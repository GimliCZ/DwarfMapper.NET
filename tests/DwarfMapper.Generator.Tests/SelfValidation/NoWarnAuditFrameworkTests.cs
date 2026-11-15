// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <c>Get-DeclaredFrameworks</c> and <c>Get-FrameworksNewerThanSdk</c> (<c>scripts/gate-checks.ps1</c>): which
    ///     projects <c>scripts/nowarn-audit.ps1</c> can build at all (round 35 T23). The audit builds every project from
    ///     the repository root, under the pinned SDK 10; <c>samples/DwarfMapper.Unions</c> and
    ///     <c>benchmarks/DwarfMapper.UnionBenchmarks</c> target net11.0, fail there with NETSDK1045 before any analyzer
    ///     runs, and every code they declare would read as stale - the nightly deep-test would fail on suppressions that
    ///     are all live. Such a project is skipped there and audited by the net11 job. Driven through the real
    ///     functions, house pattern.
    /// </summary>
    public sealed class NoWarnAuditFrameworkTests
    {
        [Fact]
        public void A_framework_newer_than_the_SDK_is_skipped_and_nothing_else_is()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-nowarn-frameworks-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    $net11 = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework><NoWarn>$(NoWarn);CA1303</NoWarn></PropertyGroup></Project>'
                    $net10 = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
                    $multi = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net10.0; net11.0;netstandard2.0</TargetFrameworks></PropertyGroup></Project>'
                    $legacy = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>netstandard2.0;net48</TargetFrameworks></PropertyGroup></Project>'
                    $inherited = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><NoWarn>CA1707</NoWarn></PropertyGroup></Project>'
                    Show 'declared-multi' { (Get-DeclaredFrameworks -ProjectText $multi) -join ',' }
                    Show 'net11-under-10' { (Get-FrameworksNewerThanSdk -Frameworks (Get-DeclaredFrameworks -ProjectText $net11) -SdkMajor 10) -join ',' }
                    Show 'net11-under-11' { @(Get-FrameworksNewerThanSdk -Frameworks (Get-DeclaredFrameworks -ProjectText $net11) -SdkMajor 11).Count }
                    Show 'net10-under-10' { @(Get-FrameworksNewerThanSdk -Frameworks (Get-DeclaredFrameworks -ProjectText $net10) -SdkMajor 10).Count }
                    Show 'multi-under-10' { (Get-FrameworksNewerThanSdk -Frameworks (Get-DeclaredFrameworks -ProjectText $multi) -SdkMajor 10) -join ',' }
                    Show 'legacy-under-10' { @(Get-FrameworksNewerThanSdk -Frameworks (Get-DeclaredFrameworks -ProjectText $legacy) -SdkMajor 10).Count }
                    Show 'inherited' { @(Get-FrameworksNewerThanSdk -Frameworks (Get-DeclaredFrameworks -ProjectText $inherited) -SdkMajor 10).Count }
                    """,
                    new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 7, "the NoWarn audit's framework skip");

                // Both properties are read, split and trimmed.
                Assert.Equal("OK net10.0,net11.0,netstandard2.0", cases["declared-multi"]);
                // The pinned SDK cannot build net11.0: skipped. The SDK 11 that the net11 job runs can.
                Assert.Equal("OK net11.0", cases["net11-under-10"]);
                Assert.Equal("OK 0", cases["net11-under-11"]);
                Assert.Equal("OK 0", cases["net10-under-10"]);
                // A multi-targeted project names only the framework it cannot build.
                Assert.Equal("OK net11.0", cases["multi-under-10"]);
                // Not netN.M - netstandard, .NET Framework, or no framework of its own - is never newer.
                Assert.Equal("OK 0", cases["legacy-under-10"]);
                Assert.Equal("OK 0", cases["inherited"]);
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
