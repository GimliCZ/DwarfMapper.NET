# Security Policy

## Supported versions
DwarfMapper is pre-1.0. Security fixes are applied to the default branch (`master`) and the latest tagged release.

## Reporting a vulnerability
Please report security issues privately via GitHub Security Advisories
("Report a vulnerability" on the repository Security tab). Do **not** open a
public issue for undisclosed vulnerabilities.

We aim to acknowledge reports within 72 hours and to provide a remediation
timeline within 7 days.

## Scope
DwarfMapper is a compile-time source generator. It performs no reflection and
no runtime code generation. Reports of interest include: generated code that
is not memory-safe, an analyzer that fails to block an unprovable `unsafe`
blit, or any supply-chain concern in the build/release pipeline.

## Build integrity
Releases are deterministic, ship a CycloneDX SBOM, and are produced from the
audited GitHub Actions workflow in `.github/workflows/`.
