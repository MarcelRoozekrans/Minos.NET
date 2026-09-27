# Performance

`benchmarks/ZeroAlloc.Jev.Benchmarks` measures the client's hot paths with BenchmarkDotNet: the
generated `[JevQuestions]` `Parse` method and the `JevAnswerReader` primitives it is built from
(`ParseBenchmarks`), and `JevClient.EvaluateAsync` / `ListModelsAsync` against an in-memory
`HttpMessageHandler` (`ClientBenchmarks`).

Run it locally with:

```
dotnet run -c Release --project benchmarks/ZeroAlloc.Jev.Benchmarks -- --filter '*'
```

or trigger the manual **Benchmarks** GitHub Actions workflow's `full` job, which uploads the
`BenchmarkDotNet.Artifacts` results.

## Baseline

To be recorded from the first full run.
