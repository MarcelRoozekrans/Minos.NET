# Client comparison runner

`run.ps1` and `run.sh` run the client comparison end to end:

1. They build the mock and the .NET harness in Release, run `npm ci` for the Node harness, and create or refresh the
   Python harness's venv.
2. They start the mock with its stdin held open and wait for it to print `ready`.
3. They run the .NET, Node and Python harnesses one at a time, never in parallel.
4. They stop the mock by closing its stdin, and kill it if it doesn't exit. This happens on every exit, including when
   a harness fails.
5. They merge the result files into a Markdown table with `merge.py`.

`run.ps1` is the Windows path. `run.sh` is its twin, for CI and Linux. Both take the same options.

## Prerequisites

- PowerShell 7 or later, for `run.ps1`; bash, for `run.sh`
- the .NET 10 SDK
- Node 20 or later, with npm
- Python 3.10 or later, as `python` or `python3`
- on Linux, `taskset` (from util-linux), to pin Node and Python

## Running

```sh
pwsh benchmarks/compare/run.ps1 --smoke                 # a quick check; the numbers mean nothing
pwsh benchmarks/compare/run.ps1 --machine local         # a full run; keep the machine idle
benchmarks/compare/run.sh --smoke                       # the same from bash
```

| Option | Default | Meaning |
|---|---|---|
| `--smoke` | off | A short run that checks every harness works. |
| `--machine <name>` | this computer's name, as .NET reports it | The machine name in the results and their folder. |
| `--project <name>` | `ZeroAlloc.Jev` | The project under test: it names the .NET projects and marks the project's rows in the table. |
| `--bench-root <dir>` | this repository's `benchmarks` | The folder holding the .NET projects, `compare-js` and `compare-py`. |
| `--results <dir>` | `results` next to the scripts | Where results go, in a `<machine>` subfolder. |
| `--port <n>` | `5005` | The mock's port. |
| `--mock-cores <list>` | see below | The mock's cores, such as `12-19` or `0-3,8`. |
| `--client-cores <list>` | see below | The harnesses' cores. Given one of the two, the other set gets the remaining cores. |
| `--help` | | Prints the options. |

Exit codes:
- 0: success.
- 1: a build or a harness failed, or the mock didn't start. A taken port is named as such.
- 2: a wrong command line or an impossible core split.

### Results layout

```
<results>/
  <machine>/
    dotnet-<machine>.json    the .NET harness: five clients, the mock ceiling, bytes per call
    js-<machine>.json        the Node harness
    py-<machine>.json        the Python harness
    comparison.md            merge.py's table
    BenchmarkDotNet.Artifacts/
```

Each run first deletes the old `*.json` files in its `<machine>` folder, so a failed harness can't leave a stale file
behind to be merged. The per-machine folders are git-ignored. The published runs are checked in at the top of
`results/`, such as `results/ci.json`.

To merge by hand, run `python benchmarks/compare/merge.py results/<machine>/*.json --project ZeroAlloc.Jev`.

### Publishing a run

`docs/performance.md` publishes the table of one run, kept in `results/ci.json`. That file is one published run: a
`run` object with the run's `url` and `commit`, and a `files` list holding each harness's result file whole, under its
file name. `merge.py` writes it with `--save`, and reads it back like the files it came from:

```sh
python benchmarks/compare/merge.py dotnet-ci.json js-ci.json py-ci.json --project ZeroAlloc.Jev   --run-url https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/runs/<id> --commit <sha>   --save benchmarks/compare/results/ci.json
python benchmarks/compare/merge.py benchmarks/compare/results/ci.json --project ZeroAlloc.Jev
```

| Option | Meaning |
|---|---|
| `--run-url <url>` | The run's URL, printed under the machine line. It overrides the URL a published run holds. |
| `--commit <sha>` | The commit the run measured, printed in short under the machine line. It overrides a published run's. |
| `--save <path>` | Also writes the inputs as one published run at `<path>`. |

The printed table goes on the page between `<!-- comparison: benchmarks/compare/results/ci.json -->` and
`<!-- endComparison -->`. A docs test runs `merge.py` on the JSON and fails when the page's table differs, or when the
published run's figures under "Across runs" do.

## The core split

The mock and the harnesses run on separate cores, so the mock's work doesn't take CPU from the clients being measured.
`cores.py` makes the split, and both runners call it:

- **Hybrid CPU:** the harnesses get the fastest core type and the mock gets the others.
  - On Windows, `run.ps1` reads each core's EfficiencyClass through `GetLogicalProcessorInformationEx`.
  - On Linux, `cores.py` reads `/sys/devices/cpu_core/cpus` and `/sys/devices/cpu_atom/cpus`. Without those, it reads
    `/sys/devices/system/cpu/cpu<n>/cpu_capacity`, and differing capacities count as different types.
- **One core type, or types unknown:** the mock gets the lower half and the harnesses the upper half. The runner prints
  which case applied. Under Git Bash on Windows the types are unknown, so on a hybrid CPU pass the core sets or use
  `run.ps1`.
- **Overrides:** `--mock-cores` and `--client-cores`.

The mock and the .NET harness pin themselves with `--cores`. Node and Python are pinned from outside:
- On Windows, `run.ps1` sets its own affinity before starting them, so they inherit it. This also covers the venv
  launcher's child interpreter.
- On Linux, the runners use `taskset -c`. Without `taskset`, Node and Python run unpinned and the runner warns.

Every result file records the mock's cores as `machine.mockCores`, and its harness's cores as `machine.cores`, or
`null` when it wasn't pinned. The merged machine line shows both.

The mock ceiling, `machine.mockCeilingPerSecond`, is measured under the same split. It is the best of the raw .NET
client at 16, 32 and 64 workers.

## The process warm-up

The .NET harness measures five clients in one process, one after another. Before it measures any of them, it warms up
every client: the checked warm-up calls, then a throughput warm-up at 16 workers, on an instance it then disposes. Each
client is then measured warm, wherever it stands in the order.

Without it, the first client's latency loop ran while the process was still cold: tiered JIT had not yet recompiled the
shared `HttpClient`, socket and System.Text.Json code or the client's own, and the thread pool had not grown. Whichever
client came first read as slower than it is. On 2026-10-04 that was ZeroAlloc.Jev, at about twice the raw client's
latency on CI, a gap that vanished when the order was reversed or the process was warmed first. Node and Python run
one client per process, so the order can't favour one of them.

The figures are therefore steady-state, measured in a warm process. They don't show a client's first-call cost: its
construction and the JIT compilation of its code, generated code included.

## Tests

`merge.py` and `cores.py` have pytest tests in `tests/`:

```sh
python -m venv .venv-test
.venv-test/bin/pip install -r benchmarks/compare/requirements-test.txt      # on Windows: .venv-test\Scripts\pip
.venv-test/bin/python -m pytest benchmarks/compare/tests
```

## Reusing the scripts for another project

The scripts depend on the layout, not on this project:

```
<bench-root>/
  <Project>.Benchmarks.Mock/<Project>.Benchmarks.Mock.csproj          prints ready, stops when stdin closes, takes --port and --cores
  <Project>.Benchmarks.Compare/<Project>.Benchmarks.Compare.csproj    takes --base-url --out --machine --smoke --cores --mock-cores
  compare-js/bench.mjs, package-lock.json                               takes the same options, minus pinning
  compare-py/bench.py, requirements.txt
```

Every harness writes `<harness>-<machine>.json` in the shared result format. Point the scripts at another project
with `--project`, `--bench-root` and `--results`:

```sh
pwsh benchmarks/compare/run.ps1 --project ZeroAlloc.Other --bench-root ../Other/benchmarks --results ../Other/results
```

The mock should exit with code 3 when its port is taken, so the runners can name the cause.
