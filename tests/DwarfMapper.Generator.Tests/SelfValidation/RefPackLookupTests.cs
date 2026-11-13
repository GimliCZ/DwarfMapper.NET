// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>The ILVerify stage finds the net10 reference pack wherever an SDK really keeps it</b> (round 35).
    ///     <para>
    ///         housekeeping's ILVerify stage looked beside the <c>dotnet</c> on <c>PATH</c>, under <c>DOTNET_ROOT</c>
    ///         and in <c>~/.nuget/packages</c>. In Microsoft's SDK container - and with a distribution's packaged SDK -
    ///         <c>dotnet</c> on <c>PATH</c> is a symlink (<c>/usr/bin/dotnet</c> -> <c>/usr/share/dotnet/dotnet</c>) and
    ///         <c>DOTNET_ROOT</c> is unset, so the stage failed before verifying anything: "no net10 ref pack found
    ///         (searched: /usr/bin/packs/...)". Found on 2026-11-11 rehearsing the nightly job in that container; a
    ///         hosted runner sets <c>DOTNET_ROOT</c>, which is why CI never showed it. <c>Get-Net10RefPack</c> in
    ///         <c>scripts/gate-checks.ps1</c> follows the link, and reads a relocated package cache
    ///         (<c>NUGET_PACKAGES</c>) as well.
    ///     </para>
    ///     <para>
    ///         Driven through <c>pwsh</c> (<see cref="PwshBattery" />) over real folders in a temp directory (ARCH-06:
    ///         registered in <see cref="RepoWriteGuardTests" />). The link itself is a stand-in resolver: creating a
    ///         real symlink needs a privilege a Windows developer machine does not have by default.
    ///     </para>
    /// </summary>
    public class RefPackLookupTests
    {
        [Fact]
        public void The_reference_pack_is_found_behind_a_symlinked_dotnet_and_in_a_relocated_cache()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dwarfmapper-refpack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "battery.ps1"),
                    PwshBattery.ScriptPrelude +
                    """
                    function Pack([string]$root, [string[]]$versions) {
                        foreach ($v in $versions) { New-Item -ItemType Directory -Force -Path (Join-Path $root 'packs/Microsoft.NETCore.App.Ref' $v 'ref/net10.0') | Out-Null }
                    }
                    function Cache([string]$root, [string[]]$versions) {
                        foreach ($v in $versions) { New-Item -ItemType Directory -Force -Path (Join-Path $root 'microsoft.netcore.app.ref' $v 'ref/net10.0') | Out-Null }
                    }
                    function Name($pack) { "$(Split-Path -Leaf (Split-Path -Parent $pack.FullName)) $($pack.Name)" }
                    $real = Join-Path $WorkDir 'share'; $bin = Join-Path $WorkDir 'bin'; $root = Join-Path $WorkDir 'root'
                    $cache = Join-Path $WorkDir 'cache'; $empty = Join-Path $WorkDir 'empty'
                    New-Item -ItemType Directory -Force -Path $bin, $empty | Out-Null
                    Pack $real '10.0.1', '10.0.11', '10.0.9', '11.0.0', '10.0.12-rc.1'
                    Pack $root '10.0.3'
                    Cache $cache '10.0.15'
                    $link = { param($p) if ($p -eq (Join-Path $bin 'dotnet')) { Join-Path $real 'dotnet' } else { $p } }
                    $none = { param($p) $p }
                    # The container's shape: dotnet on PATH is a link into the real root, nothing else is set.
                    Show 'behind-link'  { Name (Get-Net10RefPack -DotnetCommand (Join-Path $bin 'dotnet') -UserProfile $empty -ResolveLink $link) }
                    # The same without following it: nothing, and the message names where it looked.
                    Show 'link-unread'  { Name (Get-Net10RefPack -DotnetCommand (Join-Path $bin 'dotnet') -UserProfile $empty -ResolveLink $none) }
                    Show 'dotnet-root'  { Name (Get-Net10RefPack -DotnetRoots $root -UserProfile $empty -ResolveLink $none) }
                    Show 'moved-cache'  { Name (Get-Net10RefPack -NuGetPackages $cache -UserProfile $empty -ResolveLink $none) }
                    # Newest 10.x across every place, by version and not by text; an 11.x pack is not a net10 pack.
                    Show 'newest'       { Name (Get-Net10RefPack -DotnetRoots $root -DotnetCommand (Join-Path $bin 'dotnet') -NuGetPackages $cache -UserProfile $empty -ResolveLink $link) }
                    Show 'nothing'      { Name (Get-Net10RefPack -UserProfile $empty -ResolveLink $none) }
                    """, new UTF8Encoding(false));

                var cases = PwshBattery.Run(dir, 6, "Get-Net10RefPack");

                // 10.0.12-rc.1 outranks 10.0.11 once its suffix is stripped; 10.0.9 does not (a text sort would say so).
                Assert.Equal("OK Microsoft.NETCore.App.Ref 10.0.12-rc.1", cases["behind-link"]);
                Assert.StartsWith("THREW ilverify: no net10 ref pack found (searched: ", cases["link-unread"], StringComparison.Ordinal);
                Assert.Contains("bin", cases["link-unread"], StringComparison.Ordinal);
                Assert.Equal("OK Microsoft.NETCore.App.Ref 10.0.3", cases["dotnet-root"]);
                Assert.Equal("OK microsoft.netcore.app.ref 10.0.15", cases["moved-cache"]);
                Assert.Equal("OK microsoft.netcore.app.ref 10.0.15", cases["newest"]);
                Assert.StartsWith("THREW ilverify: no net10 ref pack found (searched: ", cases["nothing"], StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
