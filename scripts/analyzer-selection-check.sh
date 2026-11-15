#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
#
# Which build of the generator does a consumer of the PACKED package load?
#
#   scripts/analyzer-selection-check.sh <pack-dir> <expected-folder>      e.g. "$RUNNER_TEMP/pack" roslyn5.0
#
# Round 35 T23. The package carries the generator twice, built against Roslyn 5.0 and against Roslyn 5.12, in
# analyzers/dotnet/roslyn5.0/cs and analyzers/dotnet/roslyn5.12/cs. The SDK picks between them (dotnet/sdk,
# ResolvePackageAssets.cs, AnalyzerResolver): among Roslyn-versioned folders it takes the highest version not above
# the compiler's own CompilerApiVersion, and it takes every analyzer file OUTSIDE a versioned folder whatever else
# the package holds. So the two ways this can go wrong are both visible from a consumer: the wrong build loads, or
# two builds load at once (every mapper generated twice).
#
# The projects inside this repository reference the generator through ProjectReference and never see the package,
# so this script builds a consumer that does: a throwaway project outside the working tree, restored from
# <pack-dir> alone, built with the SDK this repository resolves (its global.json, or whatever the job installed when
# a job removed it). It prints every Analyzer item after the SDK resolved them and passes only when exactly one
# DwarfMapper.Generator.dll is listed, from analyzers/dotnet/<expected-folder>/cs, exactly one
# DwarfMapper.CodeFixes.dll is listed, and the consumer's one generated map runs.

set -euo pipefail

if [ $# -ne 2 ]; then
    echo "usage: $0 <pack-dir> <expected-folder: roslyn5.0 | roslyn5.12>" >&2
    exit 2
fi

repo=$(cd "$(dirname "$0")/.." && pwd)
pack_dir=$(cd "$1" && pwd)
expected=$2

pkg=$(find "$pack_dir" -maxdepth 1 -name 'DwarfMapper.[0-9]*.nupkg' ! -name '*.symbols.nupkg' | head -n 1)
if [ -z "$pkg" ]; then
    echo "::error::no DwarfMapper.<version>.nupkg in $pack_dir" >&2
    exit 1
fi
version=$(basename "$pkg" .nupkg)
version=${version#DwarfMapper.}

# The SDK the repository resolves, pinned for the consumer too: a directory outside the working tree has no
# global.json, and a runner image carries other SDKs that would otherwise win. The consumer targets that SDK's own
# framework (net10.0 under SDK 10, net11.0 under SDK 11), whose reference pack ships inside the SDK, so the restore
# needs no source but the pack directory.
sdk=$(cd "$repo" && dotnet --version)
tfm="net${sdk%%.*}.0"

work=$(mktemp -d "${RUNNER_TEMP:-/tmp}/dwarf-selection.XXXXXX")
trap 'rm -rf "$work"' EXIT

cat > "$work/global.json" <<EOF
{ "sdk": { "version": "$sdk", "rollForward": "disable" } }
EOF

# NuGetAudit is off for this throwaway consumer only: its single source is a local folder, which carries no
# vulnerability data, and what it proves is analyzer selection. The repository's own restores keep the audit.
cat > "$work/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$tfm</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RestoreSources>$pack_dir</RestoreSources>
    <RestorePackagesPath>$work/packages</RestorePackagesPath>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DwarfMapper" Version="$version" />
  </ItemGroup>
  <Target Name="DwarfPrintAnalyzers" AfterTargets="ResolveLockFileAnalyzers">
    <Message Importance="high" Text="DWARF-COMPILER-API: \$(CompilerApiVersion)" />
    <Message Importance="high" Text="DWARF-ANALYZER: %(Analyzer.Identity)" />
  </Target>
</Project>
EOF

cat > "$work/Program.cs" <<'EOF'
using DwarfMapper;

namespace Consumer;

public class Src { public int Id { get; set; } public string Name { get; set; } = ""; }

public class Dst { public int Id { get; set; } public string Name { get; set; } = ""; }

[DwarfMapper]
public partial class M { public partial Dst Map(Src s); }

public static class Program
{
    public static int Main()
    {
        var d = new M().Map(new Src { Id = 7, Name = "seven" });
        System.Console.WriteLine($"DWARF-CONSUMER: {d.Id} {d.Name}");
        return d.Id == 7 && d.Name == "seven" ? 0 : 1;
    }
}
EOF

echo "SDK $sdk ($tfm), package DwarfMapper $version, expecting the $expected build"
if ! (cd "$work" && dotnet build -c Release -nologo > build.log 2>&1); then
    cat "$work/build.log"
    echo "::error::the consumer of the packed package did not build" >&2
    exit 1
fi

tr '\\' '/' < "$work/build.log" | grep -o 'DWARF-[A-Z-]*: .*' | sort -u > "$work/analyzers.txt"
grep 'DWARF-COMPILER-API' "$work/analyzers.txt" || true
grep -E 'DwarfMapper\.(Generator|CodeFixes)\.dll' "$work/analyzers.txt" || true

generators=$(grep -c 'DwarfMapper\.Generator\.dll' "$work/analyzers.txt" || true)
codefixes=$(grep -c 'DwarfMapper\.CodeFixes\.dll' "$work/analyzers.txt" || true)
if [ "$generators" -ne 1 ]; then
    echo "::error::the consumer loaded $generators copies of DwarfMapper.Generator.dll, expected exactly one" >&2
    exit 1
fi
if ! grep 'DwarfMapper\.Generator\.dll' "$work/analyzers.txt" | grep -q "/analyzers/dotnet/$expected/cs/DwarfMapper\.Generator\.dll"; then
    echo "::error::the consumer loaded the generator from the wrong folder; expected analyzers/dotnet/$expected/cs" >&2
    exit 1
fi
if [ "$codefixes" -ne 1 ]; then
    echo "::error::the consumer loaded $codefixes copies of DwarfMapper.CodeFixes.dll, expected exactly one" >&2
    exit 1
fi

run=$(cd "$work" && dotnet run -c Release --no-build)
echo "$run"
if [ "$run" != "DWARF-CONSUMER: 7 seven" ]; then
    echo "::error::the consumer's generated map did not run as expected" >&2
    exit 1
fi
echo "PASSED: one generator, from analyzers/dotnet/$expected/cs, and it maps"
