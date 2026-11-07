#!/usr/bin/env bash
# Tests the check step of find-decisions-package.sh against local assemblies.
# Needs the pinned upstream build: run tools/upstream/build-decisions.sh first.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$root/tools/upstream/find-decisions-package.sh"
pathfile="$root/artifacts/upstream/abstractions.path"
[ -f "$pathfile" ] || { echo "test: run tools/upstream/build-decisions.sh first" >&2; exit 2; }
upstream="$(cat "$pathfile")"
export MSBUILDDISABLENODEREUSE=1

failures=0
# expect <name> <expected exit code> <args...>
expect() {
  local name="$1" want="$2"; shift 2
  local got=0
  "$script" --check "$@" >/dev/null 2>&1 || got=$?
  if [ "$got" -eq "$want" ]; then echo "ok   $name"; else echo "FAIL $name: want exit $want, got $got"; failures=$((failures + 1)); fi
}

# Pinned upstream build: has the public type.
expect "public IDecisionClient is found" 0 "$upstream"
# Same assembly, a type that exists but is internal: must not count.
expect "internal type does not count" 1 "$upstream" Microsoft.Shared.Diagnostics.Throw
# Same assembly, the right name in the wrong namespace: must not count.
expect "other namespace does not count" 1 "$upstream" Other.Namespace.IDecisionClient
# An assembly without the type.
runtime_dir="$(dirname "$(dotnet --list-runtimes | grep 'Microsoft.NETCore.App' | tail -1 | sed -E 's/^[^ ]+ ([^ ]+) \[(.*)\]$/\2\/\1\/x/')")"
expect "assembly without the type" 1 "$runtime_dir/System.Private.CoreLib.dll"
# Unreadable input is an error, never "none".
expect "missing file is an error" 2 "$root/artifacts/does-not-exist.dll"

[ "$failures" -eq 0 ] && echo "all passed"
exit "$failures"
