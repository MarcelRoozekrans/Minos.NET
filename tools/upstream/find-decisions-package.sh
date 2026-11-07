#!/usr/bin/env bash
# Gate for the decision adapter package: looks on nuget.org for a
# Microsoft.Extensions.AI.Abstractions version newer than 10.11.0 (stable or
# preview) whose assemblies export the public type IDecisionClient.
#
#   find-decisions-package.sh
#       Prints the first (lowest) such version and exits 0. Prints nothing and
#       exits 1 when no version qualifies. Exits 2 on a network or tool error,
#       so an outage never reads as "none".
#   find-decisions-package.sh --check <assembly.dll> [full.type.Name]
#       Checks one local assembly. Exits 0 when it has the public type, 1 when
#       it does not, 2 on an error. Used by the test.
set -euo pipefail

readonly package_id="microsoft.extensions.ai.abstractions"
readonly dll_name="Microsoft.Extensions.AI.Abstractions.dll"
readonly default_type="Microsoft.Extensions.AI.IDecisionClient"
readonly baseline="10.11.0"
# FIND_DECISIONS_FEED is a test hook: a file:// or http base laid out like the flat container.
readonly feed="${FIND_DECISIONS_FEED:-https://api.nuget.org/v3-flatcontainer/$package_id}"

err() { echo "find-decisions-package: $*" >&2; exit 2; }

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export MSBUILDDISABLENODEREUSE=1 DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

# has_type <dll> <type>: 0 public type present, 1 absent. Only a definite FOUND or ABSENT
# line from a clean run is an answer; a build, restore or run failure ends the script with
# exit 2, so an outage can never read as "absent".
has_type() {
  local rc=0 out errfile errtext
  errfile="$(mktemp)"
  out="$(cd "$root" && dotnet run "${FIND_DECISIONS_HASTYPE:-tools/upstream/HasType.cs}" -- "$1" "$2" 2>"$errfile")" || rc=$?
  errtext="$(tail -5 "$errfile")"
  rm -f "$errfile"
  [ "$rc" -eq 0 ] || err "type check failed on $1 (exit $rc)
stderr: $errtext
stdout: $(printf '%s\n' "$out" | tail -5)"
  case "$(printf '%s\n' "$out" | tail -1)" in
    FOUND) return 0 ;;
    ABSENT) return 1 ;;
    *) err "type check gave no answer on $1
stderr: $errtext
stdout: $(printf '%s\n' "$out" | tail -5)" ;;
  esac
}

if [ "${1:-}" = "--check" ]; then
  [ -n "${2:-}" ] || err "--check needs an assembly path"
  command -v dotnet >/dev/null || err "dotnet is required"
  [ -f "$2" ] || err "assembly not found: $2"
  has_type "$2" "${3:-$default_type}"
  exit $?
fi

for tool in curl jq unzip dotnet sort; do
  command -v "$tool" >/dev/null || err "$tool is required"
done

# Strict "newer than baseline" on the numeric core. A prerelease of the baseline
# core (10.11.0-preview) sorts below 10.11.0, so it is excluded with it, and any
# prerelease of a higher core (10.12.0-preview.1) is included. The numeric core
# is safe for sort -V; prerelease tags never reach it. Feed order is semver order.
is_newer() {
  local core="${1%%[-+]*}"
  [ "$core" != "$baseline" ] && [ "$(printf '%s\n%s\n' "$core" "$baseline" | sort -V | tail -1)" = "$core" ]
}

[ -z "${FIND_DECISIONS_SOURCE_ONLY:-}" ] || return 0 2>/dev/null || { echo "FIND_DECISIONS_SOURCE_ONLY is only for sourcing the script in tests" >&2; exit 2; }

index="$(curl -fsS --retry 3 --retry-delay 2 --max-time 60 "$feed/index.json")" \
  || err "cannot read the version list from $feed/index.json"
versions="$(printf '%s' "$index" | jq -er '.versions[]')" || err "unexpected version list format"

work="$(mktemp -d "$root/artifacts/find-decisions.XXXXXX" 2>/dev/null)" \
  || { mkdir -p "$root/artifacts" && work="$(mktemp -d "$root/artifacts/find-decisions.XXXXXX")"; } \
  || err "cannot create a temp directory under artifacts"
trap 'rm -rf "$work"' EXIT

while IFS= read -r version; do
  is_newer "$version" || continue
  lower="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
  dir="$work/$lower"
  mkdir -p "$dir"
  curl -fsS --retry 3 --retry-delay 2 --max-time 300 -o "$dir/pkg.nupkg" \
    "$feed/$lower/$package_id.$lower.nupkg" || err "cannot download $version"
  # Extract only the library assemblies, one list entry per line.
  listing="$(unzip -Z1 "$dir/pkg.nupkg")" || err "$version is not a readable package"
  entries="$(printf '%s\n' "$listing" | grep -E "^lib/[^/]+/$dll_name\$" || true)"
  [ -n "$entries" ] || err "$version has no lib/*/$dll_name"
  while IFS= read -r entry; do
    mkdir -p "$dir/$(dirname "$entry")"
    unzip -q -o -j "$dir/pkg.nupkg" "$entry" -d "$dir/$(dirname "$entry")" || err "cannot extract $entry from $version"
    if has_type "$dir/$entry" "$default_type"; then
      echo "$version"
      exit 0
    fi
  done <<< "$entries"
done <<< "$versions"

exit 1
