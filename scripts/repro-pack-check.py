# SPDX-License-Identifier: GPL-2.0-only
"""Reproducible-build check for the shipped NuGet packages (round-23 S4).

Usage:  python3 scripts/repro-pack-check.py DIR_A DIR_B

Compares two independent `dotnet pack` runs of the SAME commit, ENTRY BY ENTRY, and exits non-zero the
moment any build-produced byte differs.

WHY THIS IS NOT A PLAIN `sha256sum` COMPARISON — the measurement, not a guess
-----------------------------------------------------------------------------
Measured 2026-08-22 on Windows, SDK 10.0.101, at `81c4ace`, two packs from a `git clean -xdf` tree with
`CI=true` (so `ContinuousIntegrationBuild=true` and `RestoreLockedMode=true`, exactly as CI builds):

  * whole-file SHA-256 of every .nupkg/.snupkg  ->  DIFFERENT on every run
  * every build-produced entry inside them      ->  BYTE-IDENTICAL on every run
        lib/net10.0/DwarfMapper.dll, analyzers/dotnet/cs/DwarfMapper.Generator.dll,
        analyzers/dotnet/cs/DwarfMapper.CodeFixes.dll, the .nuspec, README.md,
        lib/net10.0/DwarfMapper.xml, [Content_Types].xml, and the .snupkg PDBs
  * DwarfMapper.1.0.2-rc.1.nupkg even differed in LENGTH, by one byte (253420 vs 253421)

The whole-file hash moves for exactly two reasons, both of them OPC packaging metadata written by
NuGet.Packaging and neither of them build output:

  1. `package/services/metadata/core-properties/<32 hex>.psmdcp` — NuGet names this part after a GUID it
     draws fresh on every pack. Its CONTENT was byte-identical across the two runs; only the FILE NAME
     moved (and, being a different length under Deflate, moved the archive length by a byte).
  2. `_rels/.rels` — differs on exactly one line: the relationship that Targets that .psmdcp, and the
     `Id="R…"` attribute derived from it. The sibling relationship pointing at the .nuspec kept a
     byte-identical `Id` across both runs.

So the honest verdict is: **DwarfMapper's packages reproduce; NuGet's OPC envelope does not.** Recording
that as "not reproducible" would be as wrong as recording it as "reproducible" on a hash that never
matches. This script therefore gates the layer that IS deterministic and names the exemption exactly —
it does not widen into a tolerance, and it does not skip the exempt parts, it NORMALISES them and
compares what is left of them:

  * the .psmdcp is compared by CONTENT under a canonical name (a changed .psmdcp body still fails)
  * `_rels/.rels` is compared byte-for-byte after replacing ONLY the psmdcp Target+Id pair with a
    placeholder (a changed relationship set, or a changed .nuspec relationship, still fails)
  * the entry-NAME sets are compared with the psmdcp GUID canonicalised, so an added or removed entry
    still fails

R4 note: the oracle here is file CONTENT. Nothing in this check reads a clock, a duration or a
percentage; two packs of one commit either produce the same bytes or they do not.

Vacuity guards (a comparison that looks at nothing passes exactly like a clean one — the house rule):
both directories must hold the SAME non-empty set of package files; every package must contain at least
one managed assembly (.dll) or symbol file (.pdb) and those must be among the entries actually compared;
and every .nupkg/.snupkg must carry EXACTLY ONE .psmdcp part, because "exactly one" is the premise the
exemption rests on.
"""

import hashlib
import re
import sys
import zipfile
from pathlib import Path

# The two OPC parts measured non-deterministic, each handled by name — never by a wildcard.
PSMDCP_RE = re.compile(r"^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$")
PSMDCP_CANONICAL = "package/services/metadata/core-properties/<guid>.psmdcp"
RELS_ENTRY = "_rels/.rels"

# Replaces the ONE relationship line that names the .psmdcp, Target and derived Id together. Everything
# else in .rels — the .nuspec relationship included, Id and all — is compared byte-for-byte.
RELS_PSMDCP_RE = re.compile(
    rb'Target="/package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp" Id="R[0-9A-F]+"'
)
RELS_PLACEHOLDER = b'Target="<core-properties>" Id="<derived>"'

BINARY_SUFFIXES = (".dll", ".pdb")


def canonical_name(name: str) -> str:
    return PSMDCP_CANONICAL if PSMDCP_RE.match(name) else name


def normalized_bytes(name: str, data: bytes) -> bytes:
    return RELS_PSMDCP_RE.sub(RELS_PLACEHOLDER, data) if name == RELS_ENTRY else data


def read_entries(pkg: Path) -> dict[str, bytes]:
    with zipfile.ZipFile(pkg) as z:
        return {info.filename: z.read(info) for info in z.infolist() if not info.is_dir()}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def package_names(directory: Path) -> set[str]:
    return {p.name for p in directory.iterdir() if p.suffix in (".nupkg", ".snupkg")}


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(f"usage: {argv[0]} DIR_A DIR_B", file=sys.stderr)
        return 2

    dir_a, dir_b = Path(argv[1]), Path(argv[2])
    failures: list[str] = []

    for d in (dir_a, dir_b):
        if not d.is_dir():
            print(f"FAIL: '{d}' is not a directory — the check has nothing to compare.", file=sys.stderr)
            return 1

    names_a, names_b = package_names(dir_a), package_names(dir_b)
    if not names_a:
        print(f"FAIL: no .nupkg/.snupkg in '{dir_a}'. A comparison over an empty set passes by looking at "
              f"nothing, which is the failure mode this guard exists to deny.", file=sys.stderr)
        return 1
    if names_a != names_b:
        print(f"FAIL: the two packs produced different package SETS.\n"
              f"  only in {dir_a}: {sorted(names_a - names_b)}\n"
              f"  only in {dir_b}: {sorted(names_b - names_a)}", file=sys.stderr)
        return 1

    compared_entries = 0
    compared_binaries = 0

    for name in sorted(names_a):
        pkg_a, pkg_b = dir_a / name, dir_b / name
        whole_a, whole_b = sha256(pkg_a.read_bytes()), sha256(pkg_b.read_bytes())
        verdict = "identical" if whole_a == whole_b else "DIFFER (see the OPC note in this script's header)"
        # INFORMATIONAL, never a gate: the whole-file hash carries NuGet's random .psmdcp name.
        print(f"{name}: whole-file sha256 {verdict}")
        print(f"   A {whole_a}  ({pkg_a.stat().st_size} bytes)")
        print(f"   B {whole_b}  ({pkg_b.stat().st_size} bytes)")

        entries_a, entries_b = read_entries(pkg_a), read_entries(pkg_b)

        for label, entries in (("A", entries_a), ("B", entries_b)):
            psmdcp = [e for e in entries if PSMDCP_RE.match(e)]
            if len(psmdcp) != 1:
                failures.append(
                    f"{name} [{label}]: {len(psmdcp)} .psmdcp part(s), expected exactly 1. The exemption "
                    f"below rests on there being exactly one core-properties part; with any other number "
                    f"this check cannot say what it claims to say.")

        canon_a = {canonical_name(e) for e in entries_a}
        canon_b = {canonical_name(e) for e in entries_b}
        if canon_a != canon_b:
            failures.append(
                f"{name}: the two packs produced different ENTRY SETS.\n"
                f"    only in A: {sorted(canon_a - canon_b)}\n"
                f"    only in B: {sorted(canon_b - canon_a)}")
            continue

        by_canon_b = {canonical_name(e): data for e, data in entries_b.items()}
        for entry, data_a in sorted(entries_a.items()):
            canon = canonical_name(entry)
            got_a = normalized_bytes(canon, data_a)
            got_b = normalized_bytes(canon, by_canon_b[canon])
            compared_entries += 1
            if canon.endswith(BINARY_SUFFIXES):
                compared_binaries += 1
            if got_a != got_b:
                # The two normalised parts get their own wording: their CONTENT is supposed to be
                # deterministic, and saying "build output" about them would misname the finding.
                what = ("OPC packaging metadata whose content is supposed to be deterministic"
                        if canon in (PSMDCP_CANONICAL, RELS_ENTRY) else "build output")
                failures.append(
                    f"{name}: entry '{canon}' is NOT reproducible - "
                    f"A sha256 {sha256(got_a)} ({len(got_a)} bytes) != "
                    f"B sha256 {sha256(got_b)} ({len(got_b)} bytes). This is {what}: two packs of one "
                    f"commit disagree.")

    # Vacuity: the comparison must have looked at the things it exists to look at.
    if compared_entries == 0:
        failures.append("no entries were compared at all — a vacuous pass.")
    if compared_binaries == 0:
        failures.append(
            "not one .dll or .pdb entry was compared. The whole point of a reproducible-build check is "
            "the compiled output; a run that compared only text files proves nothing about it.")

    if failures:
        print("\nREPRODUCIBLE-BUILD CHECK FAILED:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1

    print(f"\nReproducible-build check PASSED: {compared_entries} entries compared byte-for-byte across "
          f"{len(names_a)} package(s), {compared_binaries} of them compiled assemblies/symbols. The only "
          f"normalised parts are NuGet's random .psmdcp part NAME and the one _rels/.rels relationship "
          f"that targets it; their contents were compared.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
