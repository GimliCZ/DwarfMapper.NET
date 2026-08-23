<!-- SPDX-License-Identifier: GPL-2.0-only -->

# Round 25 T0-A — local kernel measurements

**Why this file exists.** The round-25 RFC's v2 numbers were measured in a cloud container on hardware this
repository does not have, and the harness it says is "preserved for re-runs" (`/home/claude/bench/Program.cs`)
does not exist here. Every constant round 25 ships had to come from a measurement someone can repeat. This is
that measurement.

## Environment

| | |
|---|---|
| Host | Windows 10.0.19045, 12 logical cores |
| Runtime | .NET 10.0.1, Release, workstation GC |
| Vectors | `Vector.IsHardwareAccelerated = true`, `Vector<byte>.Count = 32` (256-bit) |
| Method | each candidate against its **scalar twin in the same process**, so shared noise cancels; 200 warmup iterations for tier-up, then median of 15 trials of 50 iterations |
| Runs | executed twice; both runs reported below where they differ materially |

Ratios are the finding. Absolute nanoseconds carry machine noise and are not comparable across runs.

## Results

Ratio > 1 means the fast path wins.

| kernel | n=4 | n=8 | n=16 | n=32 | n=64 | n=128 | n=256 | n=1024 | n=16384 | n=65536 |
|---|---|---|---|---|---|---|---|---|---|---|
| A `array → List<T>`, 16-byte struct | 1.08 / 1.00 | 1.48 / 1.55 | 1.83 / 1.82 | 2.22 | 2.66 | 2.86 | 3.61 | **13.47** | 1.23 / 1.19 | **0.93 / 0.92** |
| B `List<T> → array`, 16-byte struct | 0.84 / 1.02 | 1.82 / 1.55 | 1.83 / 1.92 | 2.48 | 2.66 | 3.02 | 10.29 | 8.88 | 1.22 / 1.22 | **1.16 / 0.79** |
| C enum array (T1, as shipped) | 0.76 / 0.82 | 1.19 / 1.60 | 1.89 / 2.35 | 2.53 | 3.33 | 4.36 | 5.44 | 6.89 | 7.91 / 9.25 | 1.39 / 1.49 |
| D `List<T> → List<T>`, 16-byte struct | 0.95 / 0.92 | 1.82 / 1.10 | 2.16 / 2.52 | 2.63 | 2.67 | 3.55 | 3.49 | **12.44** | 1.48 / 1.94 | 1.25 / 1.15 |

## What this changes

**1. The container's `Count >= 32` List guard does not reproduce, and no guard ships.** The RFC recorded
`Add` winning below about 32 elements and specified a threshold expressed as a `Vector<T>.Count` multiple.
Measured here, the crossover is between **n=4 and n=8** — the blit already wins at 8 in every shape — and
below the crossover the penalty is **tens of nanoseconds** against multi-fold gains above it. A runtime
branch to dodge a 20 ns loss, calibrated on a constant that is wrong on at least one machine, is worse than
no branch. Shipping unconditionally, with this measurement as the reason.

**2. The gate must target the IN-CACHE regime, not the large-n one — the plan inherited this backwards.**
The v2 table showed large-n ratios settling at about 2x and the plan therefore specified the gate at "the
large-n regime, ratio >= 1.5x". On this hardware that gate would **fail on green code**: at n=65536 the
struct shapes measure 0.79–1.16x, and A reproduces *below* 1.0 across both runs. At roughly 1 MB of working
set both arms are bandwidth-bound and the copy strategy stops mattering; it can invert. The advantage lives
between about n=16 and n=16384, peaking in the low thousands, which is also where real DTO collections live.
**T4's gate is therefore specified at n≈1024, not at 65536.**

**3. T1's enum blit is confirmed on this hardware** — up to 9.25x at n=16384, and positive from n=8 —
so it earns its place independently of the container's 76x claim, which was measured against a different
scalar baseline and should not be quoted.

## What was deliberately not measured

Array→array struct blit, which has shipped since Plan 15. Benchmarking it would measure the past rather than
inform a decision. See `Issues/round25/TASKS.md`, correction 3.

## Reproducing

The harness is a standalone kernel-isolation console app, not part of the solution: it exists to answer a
question, and keeping it in the build would invite it to rot. The four kernels are reproduced verbatim in
`Issues/round25/TASKS.md` under T0-A, which is enough to rebuild it in a few minutes. The standing,
always-run comparison is the BenchmarkDotNet ratio gate in T4 — that one lives in the repository, because it
has to keep running.
