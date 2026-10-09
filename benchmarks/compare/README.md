# Client comparison runner

`run.ps1` and `run.sh` run the client comparison end to end:

1. They build the mock and the .NET harness in Release. When the Node and Python harnesses exist under the bench root,
   they run `npm ci` for the Node harness and create or refresh the Python harness's venv.
2. They start the mock with its stdin held open and wait for it to print `ready`.
3. They run the .NET harness, then the Node and Python harnesses that exist, one at a time, never in parallel. A
   missing Node or Python harness is skipped with a message; a missing mock or .NET harness is an error.
4. They stop the mock by closing its stdin, and kill it if it doesn't exit. This happens on every exit, including when
   a harness fails.
5. They merge the result files into a Markdown table with `merge.py`.

`run.ps1` is the Windows path. `run.sh` is its twin, for CI and Linux. Both take the same options.

## Prerequisites

- PowerShell 7 or later, for `run.ps1`; bash, for `run.sh`
- the .NET 10 SDK
- Python 3.10 or later, as `python` or `python3`, for the core split and the merge, and for the Python harness
- Node 20 or later, with npm, only when the Node harness exists
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
| `--project <name>` | `Minos.NET` | The project under test: it names the .NET projects and marks the project's rows in the table. |
| `--bench-root <dir>` | this repository's `benchmarks` | The folder holding the .NET projects, and `compare-js` and `compare-py` when they exist. |
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
    dotnet-<machine>.json    the .NET harness: five clients, the mock ceiling, bytes per call, the order it measured in
    js-<machine>.json        the Node harness, when it exists
    py-<machine>.json        the Python harness, when it exists
    comparison.md            merge.py's table
    BenchmarkDotNet.Artifacts/
```

Each run first deletes the old `*.json` files in its `<machine>` folder, so a failed harness can't leave a stale file
behind to be merged. The per-machine folders are git-ignored. The published runs are checked in at the top of
`results/`, such as `results/ci-run-1.json`.

To merge by hand, run `python benchmarks/compare/merge.py results/<machine>/*.json --project Minos.NET`.

### Publishing a run

`docs/performance.md` publishes the table of one run, kept in `results/ci-run-1.json`. That file is one published run: a
`run` object with the run's `url` and `commit`, and a `files` list holding each harness's result file whole, under its
file name. `merge.py` writes it with `--save`, and reads it back like the files it came from:

```sh
python benchmarks/compare/merge.py dotnet-ci.json js-ci.json py-ci.json --project Minos.NET \
  --run-url https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/<id> --commit <sha> \
  --save benchmarks/compare/results/ci-run-1.json
python benchmarks/compare/merge.py benchmarks/compare/results/ci-run-1.json --project Minos.NET
```

| Option | Meaning |
|---|---|
| `--run-url <url>` | The run's URL, printed under the machine line. It overrides the URL a published run holds. |
| `--commit <sha>` | The commit the run measured, printed in short under the machine line. It overrides a published run's. |
| `--save <path>` | Also writes the inputs as one published run at `<path>`. |

The printed table goes on the page between `<!-- comparison: benchmarks/compare/results/ci-run-1.json -->` and
`<!-- endComparison -->`.

The published runs were measured before the rename, under the name ZeroAlloc.Jev, so the table on the page now comes
from `--project ZeroAlloc.Jev`:

```sh
python benchmarks/compare/merge.py benchmarks/compare/results/ci-run-1.json --project ZeroAlloc.Jev
```

Several published runs, such as three CI runs saved as `results/ci-run-1.json` to `results/ci-run-3.json`, give the
"Across runs" table: one row per run with its CPU and mock ceiling, then per client the lowest and highest mean latency,
throughput, share of the mock ceiling and bytes per call.

The published runs were measured before the rename, under the name ZeroAlloc.Jev, so the command takes that name:

```sh
python benchmarks/compare/merge.py --across benchmarks/compare/results/ci-run-1.json benchmarks/compare/results/ci-run-2.json benchmarks/compare/results/ci-run-3.json --project ZeroAlloc.Jev
```

That table goes between `<!-- acrossRuns: <file> <file> ... -->` and `<!-- endAcrossRuns -->`, the marker naming the
files in order. A docs test runs `merge.py` on the JSON and fails when either table on the page differs, and fails
when the section's prose states a measured figure of its own instead of leaving it to the tables.

## The core split

The mock and the harnesses run on separate physical cores, so the mock's threads never run on a core the clients
being measured run on. `cores.py` makes the split, and both runners call it. It splits physical cores, not logical
ones: the SMT sibling threads of one core always land on the same side.

- **Physical cores:**
  - On Windows, `run.ps1` reads each core's logical processors from `GetLogicalProcessorInformationEx`'s
    `RelationProcessorCore` entries and passes them as `--siblings`.
  - On Linux, `cores.py` reads `/sys/devices/system/cpu/cpu<n>/topology/thread_siblings_list`.
  - Under Git Bash on Windows neither is available. The split then treats every logical core as its own physical
    core, so on an SMT machine pass the core sets or use `run.ps1`.
- **Hybrid CPU:** the harnesses get the physical cores of the fastest type and the mock gets the others.
  - On Windows, `run.ps1` reads each core's EfficiencyClass from the same entries.
  - On Linux, `cores.py` reads `/sys/devices/cpu_core/cpus` and `/sys/devices/cpu_atom/cpus`. Without those, it reads
    `/sys/devices/system/cpu/cpu<n>/cpu_capacity`, and differing capacities count as different types.
- **One core type, or types unknown:** the mock gets the lower half of the physical cores and the harnesses the upper
  half. The runner prints which case applied and the physical cores it found.
- **Overrides:** `--mock-cores` and `--client-cores`. An override that splits a physical core's threads between the two
  sides is used as given, with a warning.

What pinning guarantees, and what it doesn't:
- The mock's threads and the clients' threads never share a core, or a core's execution units through SMT.
- The two sides still share what the cores share: the last-level cache, where the CPU has one, and the memory
  bandwidth. The mock's work can slow the clients through those, and pinning can't prevent it.

The mock and the .NET harness pin themselves with `--cores`. Node and Python are pinned from outside:
- On Windows, `run.ps1` sets its own affinity before starting them, so they inherit it. This also covers the venv
  launcher's child interpreter.
- On Linux, the runners use `taskset -c`. Without `taskset`, Node and Python run unpinned and the runner warns.

Every result file records the mock's cores as `machine.mockCores`, and its harness's cores as `machine.cores`, or
`null` when it wasn't pinned. The merged machine line shows both.

The mock ceiling, `machine.mockCeilingPerSecond`, is measured under the same split. It is the best rate the raw .NET
client reached at 16, 32 and 64 workers. So it is a lower bound on what the mock can serve: the run doesn't show whether
the mock or the client side was the limit.

## The process warm-up

The .NET harness measures five clients in one process. Before it measures any of them, it warms up every client: the
checked warm-up calls, then a throughput warm-up at 16 workers for 2 s, or 0.5 s in a smoke run. It does this on the
long-lived instance it then measures, so both the process and that instance's connections are warm. Each client is
then measured warm, wherever it stands in the order.

The Node and Python harnesses get the same warm-up before their latency loop: 16 workers for 2 s, or 0.5 s in a smoke
run. Node runs 16 promise loops on its one client. Python runs 16 threads on the synchronous client its latency loop
uses, because the SDK splits sync and async calls between two client classes; the asynchronous client then has its own
2 s throughput warm-up before its measured window, as every client does.

Without it, the first client's latency loop ran while the process was still cold: tiered JIT had not yet recompiled the
shared `HttpClient`, socket and System.Text.Json code or the client's own, and the thread pool had not grown. Whichever
client came first read as slower than it is. On 2026-10-04 that was Minos.NET, at about twice the raw client's
latency on CI, a gap that vanished when the order was reversed or the process was warmed first. Node and Python run
one client per process, so the order can't favour one of them.

## The measurement order

The warm-up removes the cold-process effect, but a fixed order would still tie each client to one place in the run:
the first client measured would always meet the machine's state at the start, and a slow spell would always fall on
the same client. So the .NET harness:
- measures latency in interleaved rounds: 20 rounds in which every client makes 100 timed calls, 2000 per client in
  all, or 4 rounds of 5 in a smoke run. The order rotates by one client each round. Each client's call times are pooled
  over all its rounds, and the mock's count is read around every block, so each client must still have sent exactly
  one request per call;
- runs the throughput windows one client at a time, in an order rotated per run;
- starts both rotations at a random client, or at the one `--rotation <n>` names, and records the order as `order` in
  its result file: `latencyRounds`, `callsPerRound`, `rotation` and the `throughput` order. `merge.py` prints it under
  the machine line.

Node and Python measure one client each, so they have no order to rotate.

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

The runners fit a project that compares clients of a service through a local mock of that service. The mock is the
point: it gives every client the same server, so the figures are the clients' own. `merge.py` and `cores.py` work on
their own for any project.

Another repository needs, under its bench root:

```
<bench-root>/
  <Project>.Benchmarks.Mock/<Project>.Benchmarks.Mock.csproj          required
  <Project>.Benchmarks.Compare/<Project>.Benchmarks.Compare.csproj    required
  compare-js/bench.mjs, package.json, package-lock.json               optional
  compare-py/bench.py, requirements.txt                               optional
```

- **The mock**, a .NET console project. It takes `--port <n>` and `--cores <mask or list>` and pins itself to those
  cores. It prints `ready` on its own line once it listens, and exits when its stdin closes. It exits with code 3 when
  its port is taken, so the runners can name the cause. Every harness reads its served-request count from
  `GET /count`, so it must serve that as a plain number.
- **The .NET harness**, a .NET console project. It takes `--base-url <url> --out <dir> --machine <name> [--smoke]
  --cores <list> --mock-cores <list>`, pins itself to `--cores`, and writes `dotnet-<machine>.json` to `--out` in the
  shared result format, with `mockCeilingPerSecond` set. It exits with 0 on success and non-zero on a failure.
- **A Node harness**, when the project has a JS counterpart: `compare-js/bench.mjs`, installed with `npm ci`. It takes
  the same options as the .NET harness, except that the runner pins it from outside and passes `--cores` only to
  record. It writes `js-<machine>.json`.
- **A Python harness**, when the project has a Python counterpart: `compare-py/bench.py`, installed into
  `compare-py/.venv` from `requirements.txt`. It takes the same options as the Node harness and writes
  `py-<machine>.json`.

The shared result format is the one in `docs/superpowers/plans/2026-10-04-phase-5.2-benchmark-suite.md`: a `machine`
object and a `results` list, one entry per client. A .NET result must have `allocatedBytesPerCall`; the other runtimes
may leave it `null`.

To add clients, give a harness more entries: the .NET harness measures any number of .NET clients in one process. The
runners run only these three harnesses. A harness for another runtime needs a step in both runners, next to the Node
and Python ones, that runs it pinned to the clients' cores. Its file then merges like theirs, because `merge.py` reads
every `*.json` file in the results folder.

Point the scripts at another project with `--project`, `--bench-root` and `--results`:

```sh
pwsh benchmarks/compare/run.ps1 --project ZeroAlloc.Other --bench-root ../Other/benchmarks --results ../Other/results
```
