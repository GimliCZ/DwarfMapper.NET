<!-- SPDX-License-Identifier: GPL-2.0-only -->
# Releasing & verifying DwarfMapper.NET

This document describes how a release is produced and — more importantly for consumers —
how to **verify** one. It is part of the project's Cyber Resilience Act (CRA) supply-chain
posture: every released artifact is reproducible, has a machine-readable SBOM, carries a
SHA-256 control hash, and is signed **keylessly** through the project's GitHub identity.

## Signing model: fingerprint + git identity, no stored key

DwarfMapper deliberately keeps **no private signing key** anywhere — nothing to store, leak,
or rotate. Authenticity rests on two things that travel with every release:

1. **The artifact fingerprint** — the SHA-256 hash of each `.nupkg`/`.snupkg`, published as `SHA256SUMS`.
2. **The git identity** — a [SLSA build-provenance](https://slsa.dev/) attestation produced by
   `actions/attest-build-provenance`, signed keyless via GitHub's OIDC identity through
   [Sigstore](https://www.sigstore.dev/) and recorded in a public transparency log. The signature is
   cryptographically bound to **this repository and the exact release workflow run** — the identity is
   the trust anchor, not a long-lived certificate.

Together they answer both questions a consumer cares about: *is this byte-for-byte the artifact that
was built?* (fingerprint) and *was it built by the real DwarfMapper repo?* (identity).

## What a release contains

A version tag (`vX.Y.Z`) triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml),
which attaches the following to a GitHub Release:

| Artifact | Purpose |
|---|---|
| `DwarfMapper.X.Y.Z.nupkg` + `.snupkg` | **all-in-one** package (attributes + bundled generator + code fixes) — the only reference a normal consumer needs — plus symbols |
| `DwarfMapper.Testing.X.Y.Z.nupkg` + `.snupkg` | testing toolkit + symbols |
| `bom.xml` (in `sbom/`) | CycloneDX SBOM for the whole solution |
| `SHA256SUMS` | SHA-256 fingerprint of every `.nupkg`/`.snupkg` |
| build-provenance attestation | keyless Sigstore signature bound to the GitHub identity |

Pushing to nuget.org is **never unattended**. It is either the `publish` job in `release.yml` — NuGet Trusted
Publishing, approved by a human in the `release` environment (see *Publishing to nuget.org* below) — or, until
that is switched on, a manual push. (When published to nuget.org, packages additionally receive nuget.org's own
*repository* signature.)

## Consumer-side verification

**Which versions can be verified.** Everything below applies to a release cut from a tag by the workflow, from
1.1.0-rc.1 on. The three earlier release candidates on nuget.org (1.0.0-rc.1, 1.0.1-rc.1, 1.0.2-rc.1) were pushed by
hand before this pipeline existed: they have no tag, no GitHub Release, no SBOM and no provenance attestation, so
they cannot be verified this way. Prefer a tagged version.

### 1. Fingerprint (content hash)
```bash
sha256sum -c SHA256SUMS
```

### 2. Git identity (provenance)
```bash
gh attestation verify DwarfMapper.X.Y.Z.nupkg --repo GimliCZ/DwarfMapper.NET
```
This checks that the package's digest matches an attestation signed by this repository's GitHub
identity. To pin the exact workflow as well:
```bash
gh attestation verify DwarfMapper.X.Y.Z.nupkg \
  --repo GimliCZ/DwarfMapper.NET \
  --signer-workflow GimliCZ/DwarfMapper.NET/.github/workflows/release.yml
```

### 3. SBOM
The CycloneDX `bom.xml` enumerates every component and license. Feed it to your own
vulnerability/compliance tooling (e.g. `cyclonedx`, Dependency-Track, `grype`).

## Maintainer: cutting a release

No secrets or keys to configure — the keyless signature uses the workflow's OIDC token, which
GitHub mints automatically (the workflow already requests `id-token: write` + `attestations: write`).

**Before the tag**, on `master`, with a green full CI run:

1. **Version.** `<Version>` in [`Directory.Build.props`](../Directory.Build.props) already names the release being
   cut (it is bumped right after each publish, step 7). The tag overrides it anyway, but a local pack must not carry
   the number of a package already on nuget.org.
2. **CHANGELOG.** Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` and open a fresh, empty `## [Unreleased]`
   above it. The release job publishes that section as the GitHub Release body and **fails the release** if the
   section is missing, empty, or over 125,000 characters. Every live diagnostic id must appear somewhere in the
   file (a test enforces it), so a new diagnostic cannot reach a tag unannounced.
3. **Analyzer release tracking.** New and changed diagnostic ids are listed in
   `src/DwarfMapper.Generator/AnalyzerReleases.Unshipped.md`. While every release is a prerelease they stay there;
   at the first stable release, move the table into `AnalyzerReleases.Shipped.md` under `## Release X.Y.Z`.
4. **Packages in the release.** The release job packs the solution and the GitHub Release carries **every**
   packable project: `DwarfMapper` and `DwarfMapper.Testing`. Confirm both are meant to go out; a package that
   should not, gets `IsPackable=false` before the tag. (`DwarfMapper.Testing` is published for the first time with
   1.1.0-rc.1.)

**Tag and publish:**

```bash
# 5. Tag and push — the pipeline builds, tests, packs, attests and creates the GitHub Release.
git tag vX.Y.Z
git push origin vX.Y.Z
# 6. Publish to nuget.org: approve the `publish` job (Trusted Publishing, below) or, until that is switched on,
#    push the SAME signed packages the Release carries (don't re-pack locally; a local build can differ
#    byte-for-byte and fail attestation):
#    gh release download vX.Y.Z -p '*.nupkg' -p '*.snupkg'
#    dotnet nuget push '*.nupkg' -s https://api.nuget.org/v3/index.json -k <API_KEY>
```

**After publishing:**

7. Bump `<Version>` in `Directory.Build.props` to the next planned version, and move
   `PackageValidationBaselineVersion` in `src/DwarfMapper/DwarfMapper.csproj` to the version just published (package
   validation compares every pack against it). Add a baseline to `DwarfMapper.Testing` once it has a published
   version to compare against. Regenerate the lock files (`dotnet restore DwarfMapper.NET.sln --force-evaluate`):
   they record the project version.

The three hand-pushed 1.0.x release candidates cannot be verified (see *Consumer-side verification*); consider
deprecating them on nuget.org in favour of the first tagged release.

### Publishing to nuget.org: Trusted Publishing

The `publish` job pushes the **same** packages the GitHub Release carries (handed over as a workflow artifact,
never rebuilt) with **no API key stored anywhere**: `NuGet/login` exchanges the job's GitHub OIDC token for a
short-lived, single-use nuget.org key, and nuget.org accepts it only for the repository, workflow file and
environment its Trusted Publishing policy names. The deliberate manual gate stays — it moves from "a maintainer
runs `dotnet nuget push`" to "a maintainer approves the `publish` job in the `release` environment".

The job is **off until you switch it on**, because GitHub silently auto-creates an environment a job names —
*without* protection rules — and an unreviewed publish on every tag is exactly what this must not become. One-time
setup, in this order:

1. **nuget.org** → your account → *Trusted Publishing* → add a policy: owner = the package owner, repository
   `GimliCZ/DwarfMapper.NET`, workflow file `release.yml`, environment `release`.
2. **GitHub** → *Settings → Environments* → create `release` and add **required reviewers**.
3. In that environment, add the secret `NUGET_USER` = your nuget.org **profile name** (not your e-mail).
4. *Settings → Variables → Actions* → add the repository variable `NUGET_TRUSTED_PUBLISHING` = `true`.
5. Delete any long-lived nuget.org API key used for manual pushes.

From then on every tag stops at the `publish` job until a reviewer approves it. Until step 4, the job is skipped
and the manual push above remains the path.

The version flows from the tag (`vX.Y.Z` → `X.Y.Z`) into `-p:Version=` for both build and pack. Without a tag
the version is the one in [`Directory.Build.props`](../Directory.Build.props): the next release, never a published
one.

> **Want a NuGet-native author signature too?** That requires an X.509 code-signing certificate and
> a stored private key, which this project intentionally avoids. If a downstream consumer ever mandates
> one, the lowest-friction option is Azure **Trusted Signing** (Microsoft-identity-backed, short-lived
> certs, no key for you to hold) wired into the release job — the rest of the pipeline is unaffected.
