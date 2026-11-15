<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Comparison probes

Round 35 T25: what DwarfMapper, Mapperly and Mapster do with mistakes and with hostile shapes, measured rather than read
off documentation. The versions are the repository's pins (`Directory.Packages.props`); the results and what they mean
are in [`../results/2026-11-15-round35-t25-comparison.md`](../results/2026-11-15-round35-t25-comparison.md).

| Probe | Question | How a cell is read |
|---|---|---|
| [`validation/`](validation) | Eight mapping mistakes: what each library says, and when (build, configuration, run time, never) | one BUILD per mistake and library: the build's DWARF/RMG diagnostics, then the program's output |
| [`security/`](security) | A deep chain, a cycle, re-entry through user code, NativeAOT | one PROCESS per shape and library: a stack overflow ends a process and cannot be caught, so a cell is how the process ended |
| [`enum-gap/`](enum-gap) | Why the by-value enum row read 2.1× in Mapperly's favour on Linux and level on Windows | BenchmarkDotNet over the row's two generated methods, verbatim and with each variant of the argument check |

Run them from a checkout, on the pinned SDK, in the CI image (the AOT step needs clang and zlib1g-dev):

```bash
docker run --rm -v "$PWD:/w" -w /w mcr.microsoft.com/dotnet/sdk:10.0.101 bash -c \
  "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev >/dev/null; \
   bash benchmarks/comparison-probes/validation/run.sh; bash benchmarks/comparison-probes/security/run.sh"
```

The enum-gap probe is a BenchmarkDotNet run, `bash benchmarks/comparison-probes/enum-gap/run.sh` (add `--disasm` for the
JIT's code), in the same image for the Linux figures and on the host for the Windows ones.

None of the three projects is in `DwarfMapper.NET.sln`, and nothing in CI runs them yet; turning the validation and
security cells into tests in `tests/DwarfMapper.DifferentialTests` is the next step the results document proposes.
