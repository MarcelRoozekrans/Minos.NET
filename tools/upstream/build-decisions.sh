#!/usr/bin/env bash
# Builds the pinned upstream Microsoft.Extensions.AI.Abstractions locally.
# Output: artifacts/upstream/abstractions.path holds the full DLL path.
set -euo pipefail

fail() { echo "build-decisions: $*" >&2; exit 1; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
pin="$root/tools/upstream/decisions-pin.json"
out="$root/artifacts/upstream"
src="$out/src"

# Drop any stale pointer first so a failed run never leaves the old one.
rm -f "$out/abstractions.path" "$out/abstractions.path.tmp"

command -v jq >/dev/null || fail "jq is required"
command -v git >/dev/null || fail "git is required"
command -v dotnet >/dev/null || fail "dotnet is required"
[ -f "$pin" ] || fail "pin file not found: $pin"

repo="$(jq -er '.repository' "$pin")" || fail "pin has no repository"
commit="$(jq -er '.commit' "$pin")" || fail "pin has no commit"
project="$(jq -er '.project' "$pin")" || fail "pin has no project"
[[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "pin commit is not a full SHA: $commit"

export MSBUILDDISABLENODEREUSE=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

mkdir -p "$src"
[ -d "$src/.git" ] || git -C "$src" init -q || fail "git init failed"
git -C "$src" remote remove origin 2>/dev/null || true
git -C "$src" remote add origin "https://github.com/$repo" || fail "git remote failed"

if [ "$(git -C "$src" rev-parse -q --verify HEAD 2>/dev/null || true)" != "$commit" ]; then
  git -C "$src" fetch -q --depth 1 origin "$commit" || fail "cannot fetch $commit from $repo; the commit may no longer exist upstream because it is a PR head, so bump the pin per tools/upstream/README.md"
  git -C "$src" checkout -q --force FETCH_HEAD || fail "cannot check out $commit"
fi

[ -f "$src/$project" ] || fail "project not found at pinned commit: $project"

# Build the single project directly. The SDK is chosen from the global.json above the
# current directory, so change to the repo root first, whatever the caller's directory:
# that selects this repo's pin, 10.0.403 with rollForward latestMinor, not upstream's
# 10.0.401 pin (dotnet --version differs in the two).
cd "$root" || fail "cannot change to $root"
dotnet build "$src/$project" -c Release -nologo -v quiet \
  -p:TreatWarningsAsErrors=false -p:ContinuousIntegrationBuild=false \
  || fail "dotnet build failed"

name="$(basename "$project" .csproj)"
# The project is multi-targeted; the net10.0 build is the one the adapter uses.
dll="$src/artifacts/bin/$name/Release/net10.0/$name.dll"
[ -f "$dll" ] || fail "built $name.dll not found at $dll"

printf '%s\n' "$dll" > "$out/abstractions.path.tmp"
mv -f "$out/abstractions.path.tmp" "$out/abstractions.path"
echo "build-decisions: $dll"
