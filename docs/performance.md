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
| `ClientBenchmarks.EvaluateYieldingWithDiscardingLoggerAsync` | 12.235 us | 5.56 KB | — |

Measured on a 12th Gen Intel Core i9-12900HK, Windows 11 (10.0.26200.9457), .NET SDK 10.0.401 with runtime 10.0.12,
with `--job short`, so the means are indicative only. BenchmarkDotNet prints Allocated in KB (1 KB = 1024 B) to two
decimals, so each figure is good to about 5 B.

Without a logger, or with one whose levels are all disabled, each operation returns the unlogged call itself. So
`EvaluateAsync` and `TypedEvaluateAsync` run the same code as before logging, and the AOT smoke gates hold them to their
existing budgets with `NullLoggerFactory` and with an every-level-filtered `LoggerFactory`; they measure 4312 B and
3368 B, inside the unchanged 5120 B and 4224 B.

The discarding logger is enabled at every level and writes nothing, so every event, timestamp and logging wrapper runs.
- In the benchmark, it adds 0 B to `EvaluateAsync` and 0 B to `TypedEvaluateAsync`, to the precision of the table. The
  means differ by less than the error bars.
- Under published win-x64 AOT, the smoke gates measure 4312 B and 3368 B per call with the discarding logger, the same as
  without it. Their budgets, 4800 B and 3712 B, are those measurements plus about 10%, rounded up to the next 64 B.
- The events pass struct state straight to the logger, so what logging adds is the logging wrappers' state machines when a
  call does not complete synchronously.

The canned handler completes synchronously, so the rows above never run those state machines, and neither does the AOT
gate. The `Yielding` rows use a handler that awaits `Task.Yield()` before answering, so the call genuinely completes
asynchronously. There the discarding logger adds about 0.47 KB (5.56 KB against 5.09 KB, roughly 480 B) per call, which is
the wrappers' async state machines and is the cost a real network call pays. The two yielding means, 7.994 us and
12.235 us, are dominated by the thread pool hand-off and their error bars (11.7 us and 115.8 us) are wider than the
difference, so they show no logging time cost either way; only their allocation figures are reliable.
