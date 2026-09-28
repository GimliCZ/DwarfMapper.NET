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

> **Every live diagnostic id is announced in `CHANGELOG.md`.** The file mandates an entry for every new
> diagnostic id, and the release workflow publishes its section verbatim as the GitHub Release body. The
> seventy-six ids that predated the file were written up on 2026-08-21 as the "initial diagnostic surface"
> block under `### Added` (task `D-e` in [`Issues/round20/TASKS.md`](../Issues/round20/TASKS.md)), and the
> frozen `PredatesTheChangelog` exemption baseline was deleted with them — so the `Scan9` gate in
> `tests/DwarfMapper.Generator.Tests/SelfValidation/AssemblyScanTests.cs` now fails the build for **any**
> live id with no entry, new or old. A new diagnostic therefore cannot reach a tag unannounced.

No secrets or keys to configure — the keyless signature uses the workflow's OIDC token, which
GitHub mints automatically (the workflow already requests `id-token: write` + `attestations: write`).

```bash
# 1. Pick the version (keep README 'Status' in sync).
# 2. Tag and push — the pipeline does the rest.
git tag v1.0.2-rc.1
git push origin v1.0.2-rc.1
# 3. (Manual, when ready) publish to nuget.org — push the SAME signed packages the Release carries
#    (don't re-pack locally; a local build can differ byte-for-byte and fail attestation):
#    gh release download v1.0.2-rc.1 -p '*.nupkg' -p '*.snupkg'
#    dotnet nuget push '*.nupkg' -s https://api.nuget.org/v3/index.json -k <API_KEY>
```

### Publishing to nuget.org: Trusted Publishing (round 31 T21)

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

The version flows from the tag (`vX.Y.Z` → `X.Y.Z`) into `-p:Version=` for both build and pack.
Local default (no tag) is `1.0.2-rc.1`, set in [`Directory.Build.props`](../Directory.Build.props).

> **Want a NuGet-native author signature too?** That requires an X.509 code-signing certificate and
> a stored private key, which this project intentionally avoids. If a downstream consumer ever mandates
> one, the lowest-friction option is Azure **Trusted Signing** (Microsoft-identity-backed, short-lived
> certs, no key for you to hold) wired into the release job — the rest of the pipeline is unaffected.
