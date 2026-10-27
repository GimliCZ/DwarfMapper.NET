# SPDX-License-Identifier: GPL-2.0-only
#
# The image Anthropic's OSS Scanner (https://github.com/anthropics/oss-scanner) audits DwarfMapper in. The scanner
# builds this file with the repository as the build context and WITH network access, then runs its agent in the
# result with NONE - so everything a build or a test run needs is fetched here. What the agent should look at, and how
# to rate what it finds, is in threat_model.md next to this file.
#
# Build it locally from a CLEAN CLONE, never from the working tree: `COPY . /src` would carry every bin/, obj/,
# StrykerOutput/ and .claude/worktrees/ into the image, where the root-walking scans would read them.
#
#   git clone <this repository> dm && docker build -f dm/.oss-scanner/Dockerfile -t dwarfmapper-audit dm
#   docker run --rm -it --network none dwarfmapper-audit
#
# Every pin below is held to its source of truth by OssScannerImageTests.

# The SDK global.json pins (rollForward: disable - any other SDK refuses the build outright), held by digest like
# the workflow actions are held by SHA. Docker resolves the digest; the tag is what OssScannerImageTests compares.
FROM mcr.microsoft.com/dotnet/sdk:10.0.101@sha256:5504edd1267dd4deab3443f960cfab219249c8bd935fbcc358f1c24aeae23fe0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH="${PATH}:/root/.dotnet/tools"

# EmittedIlIsVerifiableTests shells out to ilverify: the version ci.yml pins, so the verdict is the same tool's. pwsh,
# which PwshBattery shells out to, already ships in the SDK image.
RUN dotnet tool install --global dotnet-ilverify --version 10.0.11

# scanner contract: the checkout lives inside the image, at /src
COPY . /src
WORKDIR /src

# CI's restore: exactly the committed packages.lock.json graphs, with NuGetAudit - which needs the network, so this is
# where it runs. Then the whole solution, samples included, the way CI builds it.
RUN dotnet restore DwarfMapper.NET.sln --locked-mode
RUN dotnet build DwarfMapper.NET.sln -c Release --no-restore

# Offline from here on, and a restore that is not a no-op - a new project, or a project restored on its own rather
# than through the solution - would try to fetch vulnerability data and fail NU1900 under TreatWarningsAsErrors.
# Directory.Build.props switches the audit off for this image alone, when it declares itself; the audit ran above,
# online. (Neither `ENV NuGetAudit=false` - the props' own property outranks it - nor a Directory.Build.rsp - which
# `dotnet build` does not read - reached every restore; measured.)
ENV DWARFMAPPER_OFFLINE_IMAGE=true

# CI's push-time suite, so a broken image shows in its own build log, with CI's hang bound so a stuck test cannot
# stall the image build. A failing test does not fail the image: the audit is still worth running against a red tree.
# But the agent never reads the build log, so the outcome stays in the image, at /suite-outcome.txt: GREEN, or RED and
# the tests that were already failing. threat_model.md sends the agent there first. Until round 34 a red tree said so
# only in an echo here.
RUN dotnet test DwarfMapper.NET.sln -c Release --no-build --filter "Category!=SurfaceMatrix&Category!=Perf" \
        --blame-hang --blame-hang-timeout 5m > /tmp/suite.log 2>&1; \
    code=$?; cat /tmp/suite.log; \
    { if [ "$code" -eq 0 ]; then echo "GREEN: the push-time suite passed when this image was built."; \
      else echo "RED: the push-time suite failed when this image was built (dotnet test exit $code). Already failing:"; fi; \
      grep -E '^(Passed|Failed)! |^  Failed ' /tmp/suite.log; } > /suite-outcome.txt; \
    cat /suite-outcome.txt
