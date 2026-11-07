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

# --- The version loop, against a stub feed (the FIND_DECISIONS_FEED test hook, file:// URLs). ---
tmp="$(mktemp -d "$root/artifacts/test-find.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
id=microsoft.extensions.ai.abstractions
# feed <name> <version>: a feed with one version; the caller fills $tmp/<name>/<version>/<id>.<version>.nupkg.
feed() {
  mkdir -p "$tmp/$1/$2"
  printf '{"versions":["10.11.0","%s"]}' "$2" > "$tmp/$1/index.json"
}
# run_gate <name> <expected exit code> [expected stdout]: runs the gate with the stub feed.
run_gate() {
  local name="$1" want="$2" want_out="${3-}" got=0 out
  out="$(FIND_DECISIONS_FEED="file://$tmp/$name" "$script" 2>/dev/null)" || got=$?
  if [ "$got" -eq "$want" ] && [ "$out" = "$want_out" ]; then echo "ok   gate: $name"; else echo "FAIL gate: $name: want exit $want and '$want_out', got $got and '$out'"; failures=$((failures + 1)); fi
}

mkdir -p "$tmp/pkg/lib/net10.0"
cp "$upstream" "$tmp/pkg/lib/net10.0/Microsoft.Extensions.AI.Abstractions.dll"
feed good 10.12.0
(cd "$tmp/pkg" && python3 -I -m zipfile -c "$tmp/good/10.12.0/$id.10.12.0.nupkg" lib)
run_gate good 0 10.12.0

feed corrupt 10.12.0
echo "not a zip" > "$tmp/corrupt/10.12.0/$id.10.12.0.nupkg"
run_gate corrupt 2

feed nodll 10.12.0
mkdir -p "$tmp/other" && echo x > "$tmp/other/readme.txt"
(cd "$tmp/other" && python3 -I -m zipfile -c "$tmp/nodll/10.12.0/$id.10.12.0.nupkg" readme.txt)
run_gate nodll 2

feed missing 10.12.0
run_gate missing 2

# A newer version that only lacks the type is a plain "none".
feed notype 10.12.0
mkdir -p "$tmp/core/lib/net10.0"
cp "$runtime_dir/System.Private.CoreLib.dll" "$tmp/core/lib/net10.0/Microsoft.Extensions.AI.Abstractions.dll"
(cd "$tmp/core" && python3 -I -m zipfile -c "$tmp/notype/10.12.0/$id.10.12.0.nupkg" lib)
run_gate notype 1

# A failing type-check helper (build error) must be an error, never "absent".
echo 'this is not C#' > "$tmp/Broken.cs"
got=0
FIND_DECISIONS_HASTYPE="$tmp/Broken.cs" FIND_DECISIONS_FEED="file://$tmp/good" "$script" >/dev/null 2>&1 || got=$?
if [ "$got" -eq 2 ]; then echo "ok   gate: failing helper build is an error"; else echo "FAIL gate: failing helper build: want exit 2, got $got"; failures=$((failures + 1)); fi
got=0
FIND_DECISIONS_HASTYPE="$tmp/Broken.cs" "$script" --check "$upstream" >/dev/null 2>&1 || got=$?
if [ "$got" -eq 2 ]; then echo "ok   check: failing helper build is an error"; else echo "FAIL check: failing helper build: want exit 2, got $got"; failures=$((failures + 1)); fi

# --- is_newer, sourced through the FIND_DECISIONS_SOURCE_ONLY test hook. ---
# expect_newer <version> <yes|no>
(
  FIND_DECISIONS_SOURCE_ONLY=1
  # shellcheck disable=SC1090
  source "$script"
  fail=0
  check_newer() {
    local got=no; is_newer "$1" && got=yes
    if [ "$got" = "$2" ]; then echo "ok   is_newer $1 -> $2"; else echo "FAIL is_newer $1: want $2, got $got"; fail=1; fi
  }
  check_newer 10.11.0-preview.1 no
  check_newer 10.11.0 no
  check_newer 10.11.1 yes
  check_newer 10.12.0-preview.1 yes
  check_newer 10.100.0 yes
  check_newer 10.2.0 no
  check_newer 9.9.9 no
  exit "$fail"
) || failures=$((failures + 1))

[ "$failures" -eq 0 ] && echo "all passed"
exit "$failures"
