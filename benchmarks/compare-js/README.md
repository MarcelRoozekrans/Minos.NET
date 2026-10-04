# JS comparison harness

Measures TypeSafe's official JS SDK, `@typesafe-ai/sdk` 0.6.0, against the shared local mock. It follows the .NET
harness in `benchmarks/ZeroAlloc.Jev.Benchmarks.Compare`: the same workload (`benchmarks/compare/workload`), the same
latency and throughput method, and the same result format.

```sh
npm ci
node bench.mjs --base-url http://127.0.0.1:5005 --out ../../results [--smoke] [--machine <name>]
```

Start the mock first: `dotnet run -c Release --project benchmarks/ZeroAlloc.Jev.Benchmarks.Mock`. It stops when its
stdin closes, so keep stdin open when you start it from a script.

It writes `js-<machine>.json`. The machine name defaults to the one the .NET harness uses: on Windows the NetBIOS
name, upper case and cut to 15 characters. The runner always passes `--machine`, so the file names match.

## Method

- One long-lived `TypeSafeClient` with the dummy key `benchmark-dummy-key` and `retry: { maxRetries: 0 }`: one attempt
  per call. The SDK accepts a `fetch` option, but the harness keeps the default global `fetch` on purpose, because that is what
  users get.
- A start-up call asserts every expected answer; the harness exits with code 1 if one differs.
- Latency: 200 warm-up calls, then 2000 timed calls, one at a time, each timed with `process.hrtime.bigint()`. Mean,
  p50 and p99 by nearest rank. A smoke run uses 10 and 20.
- Throughput: 16 concurrent promise loops, a 2 s warm-up and a 10 s measured window (0.5 s and 1 s for a smoke run).
  A call in flight when the time is up completes and counts.
- Each window is bracketed by reads of `GET /count`; the count must rise by exactly the calls made, else the harness
  exits with code 1.
- `mockCeilingPerSecond` and `allocatedBytesPerCall` are `null`, and so is `cores`: the runner pins Node from outside.

## Why the JS throughput is lower

Development measurements on one machine, not results. The JS client is bound by one CPU-bound JS thread: it used about
125 to 140 percent of a core whether 1, 16 or 64 workers ran, and 1 worker reached 4,289/s against 4,940/s for 16. The
same requests reached about 4.9k/s through the SDK, 8.5k/s with raw global `fetch` and 20.4k/s with `node:http`
keep-alive. The SDK does extra work per call: an `AbortController` plus a timer, two header merges, and buffering the
body before parsing it.
