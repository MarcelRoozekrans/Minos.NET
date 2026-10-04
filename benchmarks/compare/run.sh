#!/usr/bin/env bash
# Runs the client comparison: builds and starts the mock, runs the .NET harness and whichever of the Node and Python
# harnesses exist under the bench root, one after another, then stops the mock and merges the results. The twin of
# run.ps1, for CI and Linux. See README.md next to this script, and run it with --help for the options.
set -euo pipefail

usage='Usage: run.sh [options]

  --smoke                 A short run that checks every harness works; its numbers mean nothing.
  --machine <name>        The machine name in the results and their folder. Default: the host name; on Windows
                          shells, upper case and cut to 15 characters, as .NET reports it.
  --project <name>        The project under test. It names the .NET projects <name>.Benchmarks.Mock and
                          <name>.Benchmarks.Compare under the bench root, and marks the project'"'"'s rows in the table.
                          Default: ZeroAlloc.Jev.
  --bench-root <dir>      The folder holding those two projects, which are required, and compare-js and compare-py,
                          which are run when present. Default: the benchmarks folder of this repository.
  --results <dir>         Where results go, in a <machine> subfolder. Default: results next to this script.
  --port <n>              The mock'"'"'s port. Default: 5005.
  --mock-cores <list>     The mock'"'"'s cores, such as 12-19 or 0-3,8. Default: see the core split in README.md.
  --client-cores <list>   The harnesses'"'"' cores. Given one of the two, the other set gets the remaining cores.
  --help                  Prints this text.

Exits with 0 on success, 1 if a build or a harness failed or the mock did not start, and 2 on a wrong command line.'

die_usage() {
  echo "$1" >&2
  echo "$usage" >&2
  exit 2
}

trim() { local s="$1"; s="${s#"${s%%[![:space:]]*}"}"; printf '%s' "${s%"${s##*[![:space:]]}"}"; }

# The default machine name matches the harnesses' and run.ps1's: .NET's Environment.MachineName, which on Windows is the
# NetBIOS name, upper case and at most 15 characters.
default_machine() {
  local name
  name="$(hostname)"
  case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) name="$(printf '%s' "$name" | tr '[:lower:]' '[:upper:]')"; name="${name:0:15}" ;;
  esac
  printf '%s' "$name"
}

# --- Command line ---------------------------------------------------------------------------------------------------

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
smoke=false
machine="$(default_machine)"
project='ZeroAlloc.Jev'
bench_root="$here/.."
results_root="$here/results"
port=5005
core_args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --smoke) smoke=true; shift; continue ;;
    --help) echo "$usage"; exit 0 ;;
    --machine | --project | --bench-root | --results | --port | --mock-cores | --client-cores) ;;
    *) die_usage "Unknown argument: $1" ;;
  esac
  [ $# -ge 2 ] || die_usage "$1 needs a value."
  case "$1" in
    --machine) machine="$2" ;;
    --project) project="$2" ;;
    --bench-root) bench_root="$2" ;;
    --results) results_root="$2" ;;
    --port)
      [[ "$2" =~ ^[0-9]+$ ]] && [ "$2" -ge 1 ] && [ "$2" -le 65535 ] || die_usage '--port must be a number from 1 to 65535.'
      port="$2" ;;
    --mock-cores | --client-cores) core_args+=("$1" "$2") ;;
  esac
  shift 2
done

machine="$(trim "$machine")"
project="$(trim "$project")"
[ -n "$machine" ] || die_usage '--machine must not be blank.'
[ -n "$project" ] || die_usage '--project must not be blank.'
[ -d "$bench_root" ] || die_usage "Not found: $bench_root. Check --bench-root."
bench_root="$(cd "$bench_root" && pwd)"
mkdir -p "$results_root"
results_root="$(cd "$results_root" && pwd)"

# --- Paths ----------------------------------------------------------------------------------------------------------

mock_name="$project.Benchmarks.Mock"
compare_name="$project.Benchmarks.Compare"
mock_project="$bench_root/$mock_name/$mock_name.csproj"
compare_project="$bench_root/$compare_name/$compare_name.csproj"
js_dir="$bench_root/compare-js"
py_dir="$bench_root/compare-py"
# The mock and the .NET harness are the comparison; the Node and Python harnesses are optional, since most projects have
# no JS or Python counterpart to compare against.
for path in "$mock_project" "$compare_project"; do
  [ -f "$path" ] || die_usage "Not found: $path. Check --project and --bench-root."
done
has_js=false
has_py=false
if [ -f "$js_dir/bench.mjs" ]; then has_js=true; else echo "Skipping the Node harness: no $js_dir/bench.mjs."; fi
if [ -f "$py_dir/bench.py" ]; then has_py=true; else echo "Skipping the Python harness: no $py_dir/bench.py."; fi
safe_machine="$(printf '%s' "$machine" | sed 's/[^A-Za-z0-9._-]/-/g')"
out_dir="$results_root/$safe_machine"
base_url="http://127.0.0.1:$port"

# --- Python, for the core split, the harness venv and the merge -----------------------------------------------------

python=''
for candidate in python3 python; do
  if command -v "$candidate" > /dev/null && "$candidate" -c 'import sys; sys.exit(sys.version_info < (3, 10))' 2> /dev/null; then
    python="$candidate"
    break
  fi
done
[ -n "$python" ] || { echo 'Python 3.10 or later is needed.' >&2; exit 1; }

# --- Core split -----------------------------------------------------------------------------------------------------

# cores.py reads the core types and each physical core's threads from sysfs on Linux; Windows shells have neither, so
# the split there is even over logical cores unless --mock-cores or --client-cores says otherwise.
split_output="$("$python" "$here/cores.py" --count "$(nproc)" "${core_args[@]}")" \
  || die_usage 'The core split failed; see the message above.'
split_value() { sed -n "s/^$1=//p" <<< "$split_output" | tr -d '\r'; }
mock_list="$(split_value mock)"
client_list="$(split_value clients)"
split="$(split_value split)"
siblings="$(split_value siblings)"
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    if [ ${#core_args[@]} -eq 0 ]; then
      split+='; the core types and SMT siblings are unknown here, so pass --mock-cores and --client-cores, or use run.ps1'
    fi ;;
esac
echo "Cores ($split): mock $mock_list, clients $client_list; physical cores $siblings"

venv_python() {
  if [ -x "$py_dir/.venv/bin/python" ]; then echo "$py_dir/.venv/bin/python"; else echo "$py_dir/.venv/Scripts/python.exe"; fi
}

step() { echo "==> $1"; }

# --- Build and set up -----------------------------------------------------------------------------------------------

step 'Build the mock'
dotnet build "$mock_project" -c Release --nologo -v quiet || exit 1
step 'Build the .NET harness'
dotnet build "$compare_project" -c Release --nologo -v quiet || exit 1
if [ "$has_js" = true ]; then
  step 'Install the Node harness'
  (cd "$js_dir" && npm ci --no-audit --no-fund) || exit 1
fi
if [ "$has_py" = true ]; then
  if [ ! -x "$py_dir/.venv/bin/python" ] && [ ! -x "$py_dir/.venv/Scripts/python.exe" ]; then
    step 'Create the Python venv'
    "$python" -m venv "$py_dir/.venv" || exit 1
  fi
  py="$(venv_python)"
  step 'Install the Python harness'
  "$py" -m pip install --quiet --disable-pip-version-check -r "$py_dir/requirements.txt" || exit 1
fi

# The mock's own build output, whatever framework it targets.
mapfile -t mock_dlls < <(find "$bench_root/$mock_name/bin/Release" -name "$mock_name.runtimeconfig.json" 2> /dev/null \
  | sed "s/\.runtimeconfig\.json\$/.dll/")
if [ ${#mock_dlls[@]} -ne 1 ]; then
  echo "Expected one built $mock_name.dll under bin/Release, found ${#mock_dlls[@]}." >&2
  exit 1
fi
mock_dll="${mock_dlls[0]}"

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
  if kill -0 "$mock_pid" 2> /dev/null; then
    echo 'The mock did not start: it did not print ready within 60 s.' >&2
  elif grep -q "^Port $port is already in use" "$mock_errors" 2> /dev/null; then
    # The mock exits with 3 and this line when the port is taken.
    echo "The mock did not start: port $port is already in use. Stop whatever holds it, or pass --port." >&2
  else
    echo "The mock did not start: $(cat "$mock_errors" 2> /dev/null || true)" >&2
  fi
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
if [ "$has_js" = true ]; then run_harness Node "${pin[@]}" node "$js_dir/bench.mjs" "${common[@]}" "${recorded[@]}"; fi
if [ "$has_py" = true ]; then run_harness Python "${pin[@]}" "$py" "$py_dir/bench.py" "${common[@]}" "${recorded[@]}"; fi

cleanup

if [ ${#failed[@]} -gt 0 ]; then
  echo "Failed: ${failed[*]}. The results in $out_dir are incomplete." >&2
  exit 1
fi

# --- Merge ----------------------------------------------------------------------------------------------------------

"$python" "$here/merge.py" "$out_dir"/*.json --project "$project" > "$out_dir/comparison.md" || { echo 'merge.py failed.' >&2; exit 1; }
echo
cat "$out_dir/comparison.md"
echo
echo "Wrote $out_dir/comparison.md"
