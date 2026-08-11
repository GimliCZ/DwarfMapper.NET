# Security Policy

## Supported versions
The 1.0 line is in release candidate (`1.0.2-rc.1`); no stable release has been published to nuget.org yet,
and no release has been tagged. Until one is, the default branch (`master`) is the only supported version and
is where security fixes land. Once releases begin, this section will name the supported release line.

(If you are reading this from a package you obtained somewhere other than a future official release, treat it
as unsupported — there is nothing published to compare it against.)

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

## Auditing what the generator actually emits
A source generator writes code into your assembly at compile time. You do not
have to take our word for what that code is — the compiler will write it to disk
for you:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Rebuild, and every file DwarfMapper contributed appears under that path, grouped
by generator. It is ordinary, readable C#: assignments, null checks, and helper
methods. There is no reflection, no runtime emit, no `unsafe` block that the
`[Reinterpret]` analyzer has not proven blittable, and nothing that reaches the
network or the filesystem.

Two things worth knowing when you audit:

- **Output is deterministic.** The same inputs produce byte-identical output,
  including under member reordering, so you can diff two builds and expect
  silence. A diff that is not silent is worth reporting.
- **Generated files are build output, not sources.** Do not add the directory
  to source control or to the compile item group — it is emitted for reading,
  and including it produces duplicate definitions.

If the emitted code and the documented behaviour disagree, that is a report we
want, whether or not it is exploitable.
