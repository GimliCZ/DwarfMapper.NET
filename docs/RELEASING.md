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

The release is **not** pushed to nuget.org automatically — that step is manual. (When published to
nuget.org, packages additionally receive nuget.org's own *repository* signature.)

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

No secrets or keys to configure — the keyless signature uses the workflow's OIDC token, which
GitHub mints automatically (the workflow already requests `id-token: write` + `attestations: write`).

```bash
# 1. Pick the version (keep README 'Status' in sync).
# 2. Tag and push — the pipeline does the rest.
git tag v1.0.0-rc.1
git push origin v1.0.0-rc.1
# 3. (Manual, when ready) publish to nuget.org — push the SAME signed packages the Release carries
#    (don't re-pack locally; a local build can differ byte-for-byte and fail attestation):
#    gh release download v1.0.0-rc.1 -p '*.nupkg' -p '*.snupkg'
#    dotnet nuget push '*.nupkg' -s https://api.nuget.org/v3/index.json -k <API_KEY>
```

The version flows from the tag (`vX.Y.Z` → `X.Y.Z`) into `-p:Version=` for both build and pack.
Local default (no tag) is `1.0.0-rc.1`, set in [`Directory.Build.props`](../Directory.Build.props).

> **Want a NuGet-native author signature too?** That requires an X.509 code-signing certificate and
> a stored private key, which this project intentionally avoids. If a downstream consumer ever mandates
> one, the lowest-friction option is Azure **Trusted Signing** (Microsoft-identity-backed, short-lived
> certs, no key for you to hold) wired into the release job — the rest of the pipeline is unaffected.
