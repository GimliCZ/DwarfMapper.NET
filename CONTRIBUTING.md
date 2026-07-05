# Contributing

Thank you for digging in. DwarfMapper is licensed **GPL-2.0-only**.

By submitting a contribution you agree it is licensed under GPL-2.0-only and
that you have the right to submit it (per the Developer Certificate of Origin).
Sign your commits with `git commit -s`.

## Building & testing

You need the **.NET 10 SDK** (`10.0.x`) — the whole solution targets `net10.0` (an older SDK fails with `NETSDK1045`).

```bash
dotnet build DwarfMapper.NET.sln -c Release
dotnet test  DwarfMapper.NET.sln -c Release
```

## Ground rules
- Every source file starts with `// SPDX-License-Identifier: GPL-2.0-only`.
- Builds are warning-clean (`TreatWarningsAsErrors`). Fix analyzer findings;
  do not suppress without a justification comment.
- New mapping behavior requires: a generator snapshot test **and** a runtime
  integration test. A bug fix starts with a failing test.
- The generator must remain reflection-free and AOT/trim-safe; the AOT sample
  must publish clean.
