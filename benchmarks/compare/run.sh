#!/usr/bin/env bash
# Runs the client comparison: builds and starts the mock, runs the .NET, Node and Python harnesses one after another,
# then stops the mock and merges the results. The twin of run.ps1, for CI and Linux.
#
# Usage: run.sh [--smoke] [--machine <name>] [--project <name>] [--port <n>] [--mock-cores <list>] [--client-cores <list>]
#
# The mock and the harnesses run on separate cores. On a hybrid CPU, read from /sys/devices/cpu_core and cpu_atom on
# Linux, the harnesses get the performance cores and the mock the efficiency cores; otherwise the mock gets the lower
# half and the harnesses the upper half. --mock-cores and --client-cores, lists such as 12-19 or 0-3,8, override the
# split. The mock and the .NET harness pin themselves; Node and Python are pinned with taskset, and run unpinned with a
# warning when it is missing. Results go to results/<machine>/, and the merged table to results/<machine>/comparison.md.
# Exits with 1 if a build or a harness failed, and with 2 on a wrong command line.
set -euo pipefail

usage='Usage: run.sh [--smoke] [--machine <name>] [--project <name>] [--port <n>] [--mock-cores <list>] [--client-cores <list>]'

die_usage() {
  echo "$1" >&2
  echo "$usage" >&2
  exit 2
}

# --- Command line ---------------------------------------------------------------------------------------------------

smoke=false
machine="$(hostname)"
project='ZeroAlloc.Jev'
port=5005
mock_cores_arg=''
client_cores_arg=''
while [ $# -gt 0 ]; do
  case "$1" in
    --smoke) smoke=true; shift; continue ;;
    --machine | --project | --port | --mock-cores | --client-cores) ;;
    *) die_usage "Unknown argument: $1" ;;
  esac
  [ $# -ge 2 ] || die_usage "$1 needs a value."
  case "$1" in
    --machine) machine="$2" ;;
    --project) project="$2" ;;
    --port)
      [[ "$2" =~ ^[0-9]+$ ]] && [ "$2" -ge 1 ] && [ "$2" -le 65535 ] || die_usage '--port must be a number from 1 to 65535.'
      port="$2" ;;
    --mock-cores) mock_cores_arg="$2" ;;
    --client-cores) client_cores_arg="$2" ;;
  esac
  shift 2
done

trim() { local s="$1"; s="${s#"${s%%[![:space:]]*}"}"; printf '%s' "${s%"${s##*[![:space:]]}"}"; }
machine="$(trim "$machine")"
project="$(trim "$project")"
[ -n "$machine" ] || die_usage '--machine must not be blank.'
[ -n "$project" ] || die_usage '--project must not be blank.'

# --- Core lists -----------------------------------------------------------------------------------------------------

core_count="$(nproc)"
[ "$core_count" -le 64 ] || core_count=64
[ "$core_count" -ge 2 ] || die_usage 'The mock and the clients need separate cores, and this machine has one.'

# Expands a list such as 0-3,8 into sorted, distinct core indexes, one per line.
expand_cores() {
  local text="$1" name="$2" part first last core
  [[ "$text" =~ ^[0-9]+(-[0-9]+)?(,[0-9]+(-[0-9]+)?)*$ ]] || die_usage "$name must be a core list such as 0-3,8, not '$text'."
  {
    IFS=',' read -ra parts <<< "$text"
    for part in "${parts[@]}"; do
      first="${part%-*}"
      last="${part#*-}"
      [ "$((10#$last))" -ge "$((10#$first))" ] || die_usage "$name has a descending range: $part."
      for ((core = 10#$first; core <= 10#$last; core++)); do echo "$core"; done
    done
  } | sort -n -u
}

# Checks the expanded cores exist on this machine.
check_cores() {
  local name="$1" highest
  highest="$(tail -n 1 <<< "$2")"
  [ "$highest" -lt "$core_count" ] || die_usage "$name names core $highest, but this machine has cores 0 to $((core_count - 1))."
}

# Writes sorted core indexes, one per line, as a canonical list such as 0-3,8, the form the .NET harness writes.
describe_cores() {
  local out='' start='' prev='' core
  while read -r core; do
    [ -n "$core" ] || continue
    if [ -n "$prev" ] && [ "$core" -eq $((prev + 1)) ]; then
      prev="$core"
      continue
    fi
    if [ -n "$start" ]; then
      out+="${out:+,}$start"
      [ "$prev" -eq "$start" ] || out+="-$prev"
    fi
    start="$core"
    prev="$core"
  done
  if [ -n "$start" ]; then
    out+="${out:+,}$start"
    [ "$prev" -eq "$start" ] || out+="-$prev"
  fi
  printf '%s' "$out"
}

all_cores="$(seq 0 $((core_count - 1)))"
without() { grep -vxF -f <(printf '%s\n' "$2") <<< "$1" || true; }

if [ -n "$mock_cores_arg" ] || [ -n "$client_cores_arg" ]; then
  split='override'
  if [ -n "$mock_cores_arg" ]; then
    mock_cores="$(expand_cores "$mock_cores_arg" --mock-cores)" || exit 2
    check_cores --mock-cores "$mock_cores"
  fi
  if [ -n "$client_cores_arg" ]; then
    client_cores="$(expand_cores "$client_cores_arg" --client-cores)" || exit 2
    check_cores --client-cores "$client_cores"
  fi
  [ -n "$mock_cores_arg" ] || mock_cores="$(without "$all_cores" "$client_cores")"
  [ -n "$client_cores_arg" ] || client_cores="$(without "$all_cores" "$mock_cores")"
elif [ -r /sys/devices/cpu_core/cpus ] && [ -r /sys/devices/cpu_atom/cpus ]; then
  split='hybrid: performance cores for the clients, efficiency cores for the mock'
  client_cores="$(expand_cores "$(trim "$(cat /sys/devices/cpu_core/cpus)")" cpu_core)"
  mock_cores="$(expand_cores "$(trim "$(cat /sys/devices/cpu_atom/cpus)")" cpu_atom)"
else
  if [ "$(uname -s)" = Linux ]; then
    split='even: one core type'
  else
    split='even: the core types are unknown here; on a hybrid CPU pass --mock-cores and --client-cores'
  fi
  half=$((core_count / 2))
  mock_cores="$(seq 0 $((half - 1)))"
  client_cores="$(seq "$half" $((core_count - 1)))"
fi

[ -n "$mock_cores" ] && [ -n "$client_cores" ] \
  || die_usage 'The mock and the clients each need at least one core; pass --mock-cores and --client-cores.'
[ -z "$(grep -xF -f <(printf '%s\n' "$mock_cores") <<< "$client_cores" || true)" ] || die_usage '--mock-cores and --client-cores overlap.'

mock_list="$(describe_cores <<< "$mock_cores")"
client_list="$(describe_cores <<< "$client_cores")"
echo "Cores ($split): mock $mock_list, clients $client_list"

# --- Paths ----------------------------------------------------------------------------------------------------------

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
mock_project="$root/benchmarks/ZeroAlloc.Jev.Benchmarks.Mock/ZeroAlloc.Jev.Benchmarks.Mock.csproj"
mock_dll="$root/benchmarks/ZeroAlloc.Jev.Benchmarks.Mock/bin/Release/net10.0/ZeroAlloc.Jev.Benchmarks.Mock.dll"
compare_project="$root/benchmarks/ZeroAlloc.Jev.Benchmarks.Compare/ZeroAlloc.Jev.Benchmarks.Compare.csproj"
js_dir="$root/benchmarks/compare-js"
py_dir="$root/benchmarks/compare-py"
safe_machine="$(printf '%s' "$machine" | sed 's/[^A-Za-z0-9._-]/-/g')"
out_dir="$here/results/$safe_machine"
base_url="http://127.0.0.1:$port"

venv_python() {
  if [ -x "$py_dir/.venv/bin/python" ]; then echo "$py_dir/.venv/bin/python"; else echo "$py_dir/.venv/Scripts/python.exe"; fi
}

step() { echo "==> $1"; }

# --- Build and set up -----------------------------------------------------------------------------------------------

step 'Build the mock'
dotnet build "$mock_project" -c Release --nologo -v quiet || exit 1
step 'Build the .NET harness'
dotnet build "$compare_project" -c Release --nologo -v quiet || exit 1
step 'Install the Node harness'
(cd "$js_dir" && npm ci --no-audit --no-fund) || exit 1
if [ ! -x "$py_dir/.venv/bin/python" ] && [ ! -x "$py_dir/.venv/Scripts/python.exe" ]; then
  python=''
  for candidate in python3 python; do
    if command -v "$candidate" > /dev/null && "$candidate" -c 'import sys; sys.exit(sys.version_info < (3, 10))' 2> /dev/null; then
      python="$candidate"
      break
    fi
  done
  [ -n "$python" ] || { echo 'Python 3.10 or later is needed.' >&2; exit 1; }
  step 'Create the Python venv'
  "$python" -m venv "$py_dir/.venv" || exit 1
fi
py="$(venv_python)"
step 'Install the Python harness'
"$py" -m pip install --quiet --disable-pip-version-check -r "$py_dir/requirements.txt" || exit 1

# A previous run's files would be merged as if they were this run's.
mkdir -p "$out_dir"
rm -f "$out_dir"/*.json

# --- Run ------------------------------------------------------------------------------------------------------------

mock_pid=''
mock_errors="$(mktemp)"

# Stops the mock by closing its stdin, kills it if it does not exit, and removes the temporary file. Runs on every exit.
cleanup() {
  if [ -n "$mock_pid" ]; then
    # No redirection on this exec: it would apply to the shell for good.
    if [ -n "${mock_in:-}" ]; then exec {mock_in}>&- {mock_out}<&-; fi
    for _ in $(seq 1 100); do
      kill -0 "$mock_pid" 2> /dev/null || break
      sleep 0.1
    done
    if kill -0 "$mock_pid" 2> /dev/null; then
      echo 'The mock did not stop after its stdin closed; killing it.' >&2
      kill -9 "$mock_pid" 2> /dev/null || true
    fi
    # bash may already have reaped an exited coprocess, and then wait has no status to give.
    if wait "$mock_pid" 2> /dev/null; then status=0; else status=$?; fi
    if [ "$status" -eq 127 ]; then echo '==> The mock stopped'; else echo "==> The mock stopped (exit code $status)"; fi
    mock_pid=''
  fi
  rm -f "$mock_errors"
}
trap cleanup EXIT
trap 'exit 1' INT TERM

# The mock pins itself with --cores. Its stdin stays open, through the coprocess pipe, until cleanup closes it.
step "Start the mock on $base_url"
coproc MOCK { exec dotnet "$mock_dll" --port "$port" --cores "$mock_list" 2> "$mock_errors"; }
mock_pid="$MOCK_PID"
# Keep the pipe in fds of our own, because bash unsets MOCK when the coprocess exits, and close the originals so that
# closing mock_in is the only open write end.
exec {mock_in}>&"${MOCK[1]}" {mock_out}<&"${MOCK[0]}"
eval "exec ${MOCK[1]}>&- ${MOCK[0]}<&-"
if ! read -r -t 60 -u "$mock_out" line || [ "${line%$'\r'}" != ready ]; then
  echo "The mock did not start: $(cat "$mock_errors" 2> /dev/null || true)" >&2
  exit 1
fi

pin=()
recorded=()
if command -v taskset > /dev/null; then
  pin=(taskset -c "$client_list")
  recorded=(--cores "$client_list")
else
  echo 'warning: taskset is missing: Node and Python run unpinned.' >&2
fi

common=(--base-url "$base_url" --out "$out_dir" --machine "$machine" --mock-cores "$mock_list")
if [ "$smoke" = true ]; then common+=(--smoke); fi

failed=()
run_harness() {
  local name="$1"
  shift
  step "Run the $name harness"
  if ! "$@"; then
    echo "The $name harness failed." >&2
    failed+=("$name")
  fi
}

run_harness .NET dotnet run --no-build -c Release --project "$compare_project" -- "${common[@]}" --cores "$client_list"
run_harness Node "${pin[@]}" node "$js_dir/bench.mjs" "${common[@]}" "${recorded[@]}"
run_harness Python "${pin[@]}" "$py" "$py_dir/bench.py" "${common[@]}" "${recorded[@]}"

cleanup

if [ ${#failed[@]} -gt 0 ]; then
  echo "Failed: ${failed[*]}. The results in $out_dir are incomplete." >&2
  exit 1
fi

# --- Merge ----------------------------------------------------------------------------------------------------------

"$py" "$here/merge.py" "$out_dir"/*.json --project "$project" > "$out_dir/comparison.md" || { echo 'merge.py failed.' >&2; exit 1; }
echo
cat "$out_dir/comparison.md"
echo
echo "Wrote $out_dir/comparison.md"
