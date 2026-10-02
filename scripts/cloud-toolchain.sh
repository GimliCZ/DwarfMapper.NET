#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-2.0-only
#
# Install the toolchain this repository pins, in a fresh Linux session (round 32 T14). Usage, from the repository:
#
#     eval "$(scripts/cloud-toolchain.sh)"
#
# Progress goes to stderr. stdout carries only the `export` lines, so `eval` picks up the environment.
#
# What it installs, each at the version the repository pins and never another:
#
#   * The .NET SDK named in global.json (rollForward: disable). The first route is dotnet-install.sh. Where a
#     network policy blocks that script's hosts (builds.dotnet.microsoft.com, ci.dot.net), the SDK comes from
#     Microsoft's own image on mcr.microsoft.com (`dotnet/sdk:<version>-noble-amd64`) instead. Each layer is
#     checked against the manifest's SHA-256, and only /usr/share/dotnet is taken. It is the same Microsoft
#     build, from the other channel Microsoft publishes it on. If neither route yields the pinned version, the
#     script exits 1 and says so: protocol section 8 forbids substituting another SDK.
#   * dotnet-ilverify, at the version .github/workflows/ci.yml installs. EmittedIlIsVerifiableTests fails
#     without it.
#   * PowerShell 7, only when `pwsh` is missing, from packages.microsoft.com. The PwshBattery tests and
#     scripts/housekeeping.ps1 need it. The repository pins no PowerShell version (CI uses the runner's), so
#     apt takes the feed's current one.
#   * Full git history, when the clone is shallow. Protocol G7's history search silently misses decisions in a
#     shallow clone.
#
# Writes outside the repository are limited to the toolchain directories (DOTNET_INSTALL_DIR, default
# $HOME/dotnet; ~/.dotnet/tools), the apt-installed PowerShell, and a scratch directory deleted on exit.

set -euo pipefail

say() { printf '[cloud-toolchain] %s\n' "$*" >&2; }
die() { say "FAILED: $*"; exit 1; }

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
py="$(command -v python3 || command -v python || true)"
[ -n "$py" ] || die "python3 or python is required to read global.json"

sdk_version="$("$py" -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$repo/global.json")"
ilverify_version="$(grep -oE 'dotnet-ilverify --version [0-9][0-9.]*' "$repo/.github/workflows/ci.yml" | head -1 | awk '{print $3}')"
[ -n "$ilverify_version" ] || die "could not read the ilverify version from .github/workflows/ci.yml"
dotnet_dir="${DOTNET_INSTALL_DIR:-$HOME/dotnet}"
tools_dir="$HOME/.dotnet/tools"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

has_sdk() { [ -x "$dotnet_dir/dotnet" ] && "$dotnet_dir/dotnet" --list-sdks 2>/dev/null | grep -q "^$sdk_version "; }

install_sdk_from_mcr() {
  local arch tag reg manifest
  arch="$(uname -m)"
  [ "$arch" = "x86_64" ] || { say "the mcr.microsoft.com route covers x86_64 only (this is $arch)"; return 1; }
  tag="$sdk_version-noble-amd64"
  reg="https://mcr.microsoft.com/v2/dotnet/sdk"
  manifest="$scratch/manifest.json"
  curl -fsSL -m 60 -o "$manifest" \
    -H 'Accept: application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json' \
    "$reg/manifests/$tag" || { say "no manifest for dotnet/sdk:$tag on mcr.microsoft.com"; return 1; }
  mkdir -p "$scratch/root"
  # Layers are applied in manifest order, so a later layer's file wins. The image adds files and does not delete
  # them, so no whiteout handling is needed for the dotnet tree.
  local digest blob
  for digest in $("$py" -c 'import json,sys; [print(l["digest"]) for l in json.load(open(sys.argv[1]))["layers"]]' "$manifest"); do
    blob="$scratch/${digest#sha256:}.tgz"
    curl -fsSL -m 900 -o "$blob" "$reg/blobs/$digest" || { say "could not download layer $digest"; return 1; }
    # sha256sum reports a mismatch on stdout; send it to stderr, which is where progress goes.
    echo "${digest#sha256:}  $blob" | sha256sum -c --quiet - >&2 || { say "layer $digest failed its SHA-256 check"; return 1; }
    # Not `grep -q`: it exits at the first match, tar dies of SIGPIPE, and under pipefail the layer is skipped.
    if tar -tzf "$blob" | grep '^usr/share/dotnet/' >/dev/null; then
      tar -xzf "$blob" -C "$scratch/root" usr/share/dotnet
    fi
    rm -f "$blob"
  done
  [ -d "$scratch/root/usr/share/dotnet" ] || { say "the image carried no /usr/share/dotnet"; return 1; }
  mkdir -p "$dotnet_dir"
  cp -a "$scratch/root/usr/share/dotnet/." "$dotnet_dir/"
}

# ── .NET SDK ──────────────────────────────────────────────────────────────────────────────────────────────
if has_sdk; then
  say "SDK $sdk_version already in $dotnet_dir"
else
  say "installing SDK $sdk_version into $dotnet_dir"
  if curl -fsSL -m 60 -o "$scratch/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh \
     && bash "$scratch/dotnet-install.sh" --version "$sdk_version" --install-dir "$dotnet_dir" >&2 \
     && has_sdk; then
    say "SDK $sdk_version installed by dotnet-install.sh"
  else
    say "dotnet-install.sh could not install $sdk_version here; trying Microsoft's SDK image on mcr.microsoft.com"
    install_sdk_from_mcr || die "the pinned SDK $sdk_version is unavailable by either route; no other version is substituted"
    has_sdk || die "the image did not yield SDK $sdk_version"
    say "SDK $sdk_version installed from mcr.microsoft.com/dotnet/sdk:$sdk_version-noble-amd64"
  fi
fi

export DOTNET_ROOT="$dotnet_dir" PATH="$dotnet_dir:$tools_dir:$PATH"
export DOTNET_CLI_UI_LANGUAGE=en DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

# ── dotnet-ilverify ───────────────────────────────────────────────────────────────────────────────────────
# `grep` without -q, for the reason given at the layer check above.
if dotnet tool list --global 2>/dev/null | grep -E "^dotnet-ilverify[[:space:]]+${ilverify_version}[[:space:]]" >/dev/null; then
  say "dotnet-ilverify $ilverify_version already installed"
else
  say "installing dotnet-ilverify $ilverify_version"
  (cd "$scratch" && dotnet tool install --global dotnet-ilverify --version "$ilverify_version" >&2) \
    || die "dotnet-ilverify $ilverify_version could not be installed"
fi

# ── PowerShell 7 ──────────────────────────────────────────────────────────────────────────────────────────
if command -v pwsh >/dev/null 2>&1; then
  say "pwsh already present: $(pwsh --version)"
else
  if [ "$(id -u)" -ne 0 ] || ! command -v apt-get >/dev/null 2>&1; then
    die "pwsh is missing and this is not a root apt system; install PowerShell 7 by hand"
  fi
  os_id="$(sed -n 's/^ID=//p' /etc/os-release | tr -d '"')"
  os_version="$(sed -n 's/^VERSION_ID=//p' /etc/os-release | tr -d '"')"
  say "installing PowerShell from packages.microsoft.com ($os_id $os_version)"
  curl -fsSL -m 120 -o "$scratch/packages-microsoft-prod.deb" \
    "https://packages.microsoft.com/config/$os_id/$os_version/packages-microsoft-prod.deb" \
    || die "could not fetch packages-microsoft-prod.deb for $os_id $os_version"
  dpkg -i "$scratch/packages-microsoft-prod.deb" >&2
  if ! { apt-get update -qq >&2 && apt-get install -y -qq powershell >&2; }; then
    die "apt could not install powershell"
  fi
fi

# ── git history ───────────────────────────────────────────────────────────────────────────────────────────
if [ "$(git -C "$repo" rev-parse --is-shallow-repository 2>/dev/null)" = "true" ]; then
  say "unshallowing the clone (protocol G7)"
  git -C "$repo" fetch --unshallow >&2 || die "git fetch --unshallow failed"
fi

say "ready: SDK $(cd "$repo" && dotnet --version), ilverify $ilverify_version, $(pwsh --version)"
printf 'export DOTNET_ROOT=%q\n' "$dotnet_dir"
# The trailing $PATH is escaped on purpose: it expands when the caller evals this line, not here.
printf '%s\n' "export PATH=$(printf '%q' "$dotnet_dir"):$(printf '%q' "$tools_dir"):\"\$PATH\""
printf 'export DOTNET_CLI_UI_LANGUAGE=en DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1\n'
