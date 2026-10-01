# Performance

`benchmarks/ZeroAlloc.Jev.Benchmarks` measures the client's hot paths with BenchmarkDotNet: the
generated `[JevQuestions]` `Parse` method and the `JevAnswerReader` primitives it is built from
(`ParseBenchmarks`), `JevClient.EvaluateAsync` / `ListModelsAsync` against an in-memory
`HttpMessageHandler` (`ClientBenchmarks`) and question sets built at run time (`QuestionSetBenchmarks`).

Run it locally with:

```
dotnet run -c Release --project benchmarks/ZeroAlloc.Jev.Benchmarks -- --filter '*'
```

or trigger the manual **Benchmarks** GitHub Actions workflow's `full` job, which uploads the
`BenchmarkDotNet.Artifacts` results.

## Baseline

To be recorded from the first full run.

### Phase 2.3 — JevContent factories

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ContentBenchmarks.FromValue` | 538.7 ns | 256 B | 320 B |
| `ContentBenchmarks.FromUtf8Json` | 708.7 ns | 256 B | 320 B |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401, with
`--job short`. Question sets add no runtime cost: `Examples`, `NotFor` and `Json = true` change only the
static `QuestionsUtf8` literal.

The means come from `--job short`, which runs few iterations and leaves wide error bars; treat them as
indicative only. The first full run supersedes them.

The AOT smoke gates measure on their own inputs, not the benchmark's. The `FromValue` gate serializes the
smoke app's state and measures 280 B per call, where the benchmark shows 256 B, so its 320 B budget is that
280 B plus about 10%, rounded up to the next 64 B. The `FromUtf8Json` gate measures the same 256 B as the
benchmark, and the same rounding gives it 320 B too.

### Phase 2.4 — Question sets built at run time

| Benchmark | Mean | Allocated | Budget |
|---|---|---|---|
| `QuestionSetBenchmarks.Build` | 1.903 us | 6728 B | 7296 B (AOT smoke, a different three-question set) |
| `QuestionSetBenchmarks.EvaluateBuiltSet` | 4.234 us | 3808 B | 4736 B (AOT smoke, a different set) |
| `QuestionSetBenchmarks.ParseBuiltTwenty` | 3.087 us | 568 B | 256 B (unit test, a different three-question set) |
| `QuestionSetBenchmarks.ParseGeneratedTwenty` | 2.457 us | 176 B | — |
| `JevAnswers.Get` | — | — | 0 B (AOT smoke) |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only.

A built set finds each answer's question by a linear `ValueTextEquals` scan over its UTF-8 keys, where the generated
parser compiles one `if` chain. At twenty questions the scan costs about 0.63 us more, 3.087 us against 2.457 us, or
26% (1.26 times the generated parse), and allocates 568 B against 176 B mostly for the larger slot array. That is well
inside twice the generated parse, so it does not call for a UTF-8 key map: the scan is short at the sizes a question
set has, and a map, built once at Build, would add a hash per answer for a saving of well under a microsecond.

The budgets come from the AOT smoke app and the unit test, which measure on their own inputs, not the benchmark's:
`Build` measures 6592 B and `EvaluateBuiltSet` 4288 B on published win-x64 AOT, each plus about 10% rounded up to the
next 64 B; the parse measures 216 B, rounded up to 256 B. The parse budget is gated in `tests/ZeroAlloc.Jev.Tests`
under the JIT, since parsing is internal and the AOT smoke app uses only the public API; it allocates only the
`JevAnswers` object, its probability buffer and its slot array.

### Phase 3.1 — Logging

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ClientBenchmarks.EvaluateAsync` | 2.269 us | 4.17 KB | 5120 B |
| `ClientBenchmarks.EvaluateWithDiscardingLoggerAsync` | 2.402 us | 4.17 KB | 4800 B |
| `ClientBenchmarks.TypedEvaluateAsync` | 1.780 us | 3.29 KB | 4224 B |
| `ClientBenchmarks.TypedEvaluateWithDiscardingLoggerAsync` | 1.960 us | 3.29 KB | 3712 B |
| `ClientBenchmarks.EvaluateYieldingAsync` | 7.994 us | 5.09 KB | — |
| `ClientBenchmarks.EvaluateYieldingWithNullLoggerAsync` | 22.79 us | 5.09 KB | — |
| `ClientBenchmarks.EvaluateYieldingWithDiscardingLoggerAsync` | 12.235 us | 5.56 KB | — |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two
decimals, so each figure is good to about 5 B.

Without a logger, or with one whose levels are all disabled, each operation returns the unlogged call itself, so logging
allocates nothing and does only `IsEnabled` checks; with no factory at all, there is no logging decorator either. The AOT smoke gates hold `EvaluateAsync` and `TypedEvaluateAsync` to their
existing budgets with `NullLoggerFactory` and with an every-level-filtered `LoggerFactory`; they measure 4312 B and
3368 B, inside the unchanged 5120 B and 4224 B.

The discarding logger is enabled at every level and writes nothing, so every event, timestamp and logging wrapper runs.
- In the benchmark, it adds 0 B to `EvaluateAsync` and 0 B to `TypedEvaluateAsync`, to the precision of the table. The
  mean gaps, 0.13 us and 0.18 us, are within the noise, less than the larger error bar of each pair. The discarding
  logger's per-call `Interlocked` counter, which the AOT gates read, costs a few ns and no allocation, also inside the
  noise.
- Under published win-x64 AOT, the smoke gates measure 4312 B and 3368 B per call with the discarding logger, the same as
  the disabled-logger measurements of 4312 B and 3368 B. Their budgets, 4800 B and 3712 B, are those measurements plus about 10%, rounded up to the next 64 B.
- The events pass struct state straight to the logger, so what logging adds is the logging wrappers' state machines when a
  call does not complete synchronously.

The canned handler completes synchronously, so the rows above never run those state machines, and neither does the AOT
gate. The `Yielding` rows use a handler that awaits `Task.Yield()` before answering, so the call genuinely completes
asynchronously. There the discarding logger adds about 0.47 KB (5.56 KB against 5.09 KB, roughly 480 B) per call, which is
attributable by elimination: the synchronous rows show the wrapper adds 0 B, so the async delta is the state machines
that only an async completion allocates, and it is the cost a real network call pays. The two yielding means, 7.994 us and
12.235 us, are dominated by the thread pool hand-off and their error bars (11.7 us and 115.8 us) are wider than the
difference, so they show no logging time cost either way; only their allocation figures are reliable.

`EvaluateYieldingWithNullLoggerAsync` runs the same yielding call through `NullLoggerFactory`: it allocates 5.09 KB, the same as the unlogged call, so a disabled logger adds no allocation even where an enabled one adds about 480 B. Its mean comes from a later, noisier run that also re-measured the two rows beside it (26.51 us and 28.30 us, error bars above 200 us), so it says nothing about time. The AOT smoke app makes the same comparison with `GC.GetTotalAllocatedBytes` over 500 awaited calls, the least of three runs: 5254 B with no factory, 5254 B with `NullLoggerFactory` and 5733 B with the discarding logger.

Note, 2026-10-01: since Phase 3.2 that check takes the median of five runs, because yielding runs vary in both directions, and it measures about 5254 B with no factory, 5254 B with `NullLoggerFactory` and 5720-5736 B with the discarding logger.

### Phase 3.2 — Telemetry

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ClientBenchmarks.EvaluateAsync` | 2.530 us | 4.17 KB | 5120 B |
| `TelemetryBenchmarks.EvaluateListeningAsync` | 3.200 us | 5.51 KB | 6272 B |
| `ClientBenchmarks.TypedEvaluateAsync` | 2.082 us | 3.29 KB | 4224 B |
| `TelemetryBenchmarks.TypedEvaluateListeningAsync` | 6.623 us | 4.81 KB | 5440 B |
| `QuestionSetBenchmarks.EvaluateBuiltSet` | 2.024 us | 3.31 KB | — |
| `TelemetryBenchmarks.EvaluateBuiltSetListeningAsync` | 6.457 us | 4.84 KB | — |
| `ClientBenchmarks.ListModelsAsync` | 1.520 us | 3.08 KB | — |
| `TelemetryBenchmarks.ListModelsListeningAsync` | 1.872 us | 4.09 KB | — |
| `ClientBenchmarks.TypedEvaluateYieldingAsync` | 5.170 us | 4.46 KB | 5056 B |
| `TelemetryBenchmarks.TypedEvaluateYieldingListeningAsync` | 21.724 us | 6.23 KB | — |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two
decimals, so each figure is good to about 5 B.
`QuestionSetBenchmarks.EvaluateBuiltSet` and `TelemetryBenchmarks.EvaluateBuiltSetListeningAsync` evaluate the same
three-question triage set, off and listening, so those two rows compare directly. The AOT built-set gates evaluate
`SmokeBuiltSet.Full`, four questions, so they have no row here: `EvaluateBuiltSetRoundTrip` holds 4736 B, and
`EvaluateBuiltSetRoundTripWhileListening` measures 5216 B against 5760 B.

**Telemetry off.** With nothing listening, the generated proxy returns each operation's own task, so it adds nothing.
- The raw evaluation allocates exactly what it did in Phase 3.1: `ClientBenchmarks.EvaluateAsync` matches its Phase 3.1
  row, and the AOT `EvaluateRoundTrip` gate holds 5120 B unchanged. Model listing has no earlier row and no AOT gate; the
  unit gate `OperationsCostTests.NothingListening_ListModels_AddsNothing` shows 0 B through the proxy with nothing listening.
- Typed and built-set calls that complete synchronously allocate exactly what they did in Phase 3.1:
  `ClientBenchmarks.TypedEvaluateAsync` matches its Phase 3.1 row, and under published win-x64 AOT the typed and built-set
  gates measure 3368 B and 3656 B, inside the unchanged 4224 B and 4736 B.
- A typed or built-set call that completes asynchronously, which every real network call does, pays one state machine
  for the unwrap that hands back the answers and returns the response buffer. The unit test measures it at
  211 B under the JIT, against a 344 B limit, the headroom of the tightest existing gate,
  `TypedEvaluateRoundTripWithDiscardingLogger`.
- Under published win-x64 AOT, an asynchronous `EvaluateAsync<T>` with telemetry off allocates 4568 B per call, the median
  of five runs, because yielding runs vary in both directions.

Listening minus off, from the table: the raw evaluation 1.34 KB (5.51 against 4.17), the typed call 1.52 KB (4.81 against 3.29),
the built set 1.53 KB (4.84 against 3.31), model listing 1.01 KB (4.09 against 3.08) and the asynchronous typed call 1.77 KB
(6.23 against 4.46). The mean of `TypedEvaluateYieldingListeningAsync`, 21.724 us with an error of 171 us, is dominated by the thread pool
hand-off and noise, so only its allocation figure is reliable.

**Listening.** Discarding listeners sample every span and enable every instrument. Then the call pays for:
- the `Activity`, and the start tags `TagsAtStart` boxes into one `TagList`;
- the boxed tag and measurement values, and a `TagList` per metric;
- the deferred reads.

The reads re-scan the response once per attribute. The only allocation they make is the response model's string. There is no
cache, so the string is built again for each attribute that reads it: the span tag and the duration and token-histogram
metric tags. That cost is included in the figures below; it is measured, not budgeted at zero. Under
published win-x64 AOT the listening gates measure 5680 B, 4928 B and 5216 B per call.
Their budgets are those measurements plus about 10%, rounded up to the next 64 B.

### Phase 3.3 — DI package

| Benchmark | Mean | Allocated | AOT smoke budget |
|---|---|---|---|
| `ClientBenchmarks.EvaluateAsync` | 18.15 us | 4.17 KB | 5120 B |
| `DependencyInjectionBenchmarks.HandBuiltEvaluateAsync` | 5.453 us | 4.23 KB | — |
| `DependencyInjectionBenchmarks.ResolvedEvaluateAsync` | 5.816 us | 4.23 KB | 4864 B |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11, .NET SDK 10.0.401 with runtime 10.0.12, with `--job short`, so
the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two decimals, so each figure is
good to about 5 B.

**DI adds nothing per call.**
- Both `DependencyInjectionBenchmarks` rows make `ClientBenchmarks.EvaluateAsync`'s call.
- The hand-built client runs over an `HttpClient` that `JevClient.ConfigureHttpClient` configured, as the factory's is.
  So both send the User-Agent header. That header costs 64 B per call over `ClientBenchmarks.EvaluateAsync`, whose
  borrowed client sends none.
- Under published win-x64 AOT, the gate `EvaluateRoundTripThroughDependencyInjectionAgainstHandBuilt` holds the
  resolved client to the hand-built client's own measurement from the same run, 4376 B. The resolved client
  measures 4376 B, within the absolute budget of 4864 B: the measurement plus about 10%, rounded up to the
  next 64 B.
- Registration and the first resolve happen once and are not budgeted.

**The factory's request logging.** `AddHttpClient` gives every named client the factory's logging handlers. These format
the redacted request URI and open a logging scope on every request, before they check whether any logger is enabled.
- With those handlers, the resolved client measured 4720 B against 4376 B per call, under both the JIT and published
  win-x64 AOT. That is 344 B more. This was measured while planning, on 2026-10-01.
- So `AddJevClient` removes them with `RemoveAllLoggers()`. The client still logs each operation and each retried attempt
  itself.
- `AddDefaultLogger()` on the returned builder brings them back, at that cost.
