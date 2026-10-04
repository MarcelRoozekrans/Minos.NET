# Python comparison harness

Measures TypeSafe's official Python SDK, `typesafe-sdk` 0.7.2, against the shared local mock. It follows the .NET
harness in `benchmarks/ZeroAlloc.Jev.Benchmarks.Compare` and the JS harness in `benchmarks/compare-js`: the same
workload (`benchmarks/compare/workload`), the same latency and throughput method, and the same result format. It needs
Python 3.10 or later.

```sh
python -m venv .venv
.venv/bin/pip install -r requirements.txt     # on Windows: .venv\Scripts\pip
.venv/bin/python bench.py --base-url http://127.0.0.1:5005 --out ../../results [--smoke] [--machine <name>]
```

`requirements.txt` pins `typesafe-sdk` and every transitive dependency to an exact version, from a `pip freeze` of a
clean virtual environment.

Start the mock first: `dotnet run -c Release --project benchmarks/ZeroAlloc.Jev.Benchmarks.Mock`. It stops when its
stdin closes, so keep stdin open when you start it from a script.

It writes `py-<machine>.json`. The machine name defaults to the one the .NET harness uses: on Windows the NetBIOS
name, upper case and cut to 15 characters. The runner always passes `--machine`, so the file names match.

## Method

- The dummy key `benchmark-dummy-key` and `retry=RetryPolicy(max_retries=0)`: one attempt per call. Each mode uses one
  long-lived client, closed when its runs end. The SDK's default HTTP transport is kept on purpose, because that is
  what users get.
- A start-up call asserts every expected answer; the harness exits with code 1 if one differs.
- Latency: the synchronous `TypeSafeClient`, 200 warm-up calls, then 2000 timed calls, one at a time, each timed with
  `time.perf_counter_ns`. Mean, p50 and p99 by nearest rank. A smoke run uses 10 and 20.
- Throughput: the `AsyncTypeSafeClient` with 16 concurrent `asyncio` tasks on one event loop, a 2 s warm-up and a 10 s
  measured window (0.5 s and 1 s for a smoke run). A call in flight when the time is up completes and counts.
- Each window is bracketed by reads of `GET /count`; the count must rise by exactly the calls made, else the harness
  exits with code 1.
- `mockCeilingPerSecond` and `allocatedBytesPerCall` are `null`, and so is `cores`: the runner pins Python from outside.

## Why the Python throughput is lower

Development measurements on one machine, not results. The Python client is bound by one CPU-bound interpreter thread:
it used 93 to 99 percent of one core whether 1, 16 or 64 tasks ran, and 1 task reached 1,163/s against 1,686/s for 16
and 1,562/s for 64. The mock held exactly 16 client connections for 16 tasks, all reused, so the SDK does not open
connections per call. Raw `httpx2` calls, the HTTP library the SDK is built on, with a hand-made JSON body and
response parsing, reached 2,086/s at 16 tasks, so the HTTP stack sets the ceiling and the SDK's request building and
response validation cost roughly 20 to 25 percent on top. The raw `httpx2` comparison was an ad-hoc script and is not committed.
