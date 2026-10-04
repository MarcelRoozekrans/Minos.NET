---
id: native-aot
title: Native AOT and allocations
sidebar_position: 8
description: What it means that the Jev client is Native AOT compatible, the one reflection it uses, and the allocation budgets that guard it.
---

# Native AOT and allocations

Native AOT compiles a .NET program ahead of time into a native executable. There is no just-in-time compiler at run
time, and the compiler removes code that it cannot see being used, which is called trimming. The result starts fast and
is small, but a library has to cooperate. Code that finds types or members by name at run time, which is reflection,
can lose the very things it looks for.

`ZeroAlloc.Jev` is written for this. This page says what that covers, where it is checked, and what the client
allocates, because a program that avoids the just-in-time compiler usually cares about memory as well.

## What is Native AOT compatible

- **The client.** `ZeroAlloc.Jev` sets `IsAotCompatible`, which turns on the trimming and Native AOT analyzers for the
  library's own build. Sending requests, mapping every failure to a `JevError`, retries, logging and telemetry all run
  under Native AOT.
- **The generated question sets.** The `[JevQuestions]` generator writes the question JSON and the answer parsing as
  ordinary C# at compile time. The set uses no reflection at run time, and neither does reading an answer.
- **Dependency injection.** `ZeroAlloc.Jev.DependencyInjection` is Native AOT compatible too. It binds options from
  configuration with the configuration binding source generator, so binding uses no reflection and needs nothing set up
  in your program. [Dependency injection](dependency-injection.md#options-from-configuration) covers the options.

## The one use of reflection

A question set [built at run time](question-sets-at-run-time.md) can take a Choice or a Score from an enum. To read the
enum's members, the builder reads its public fields. This is the only reflection in the library, and it is declared to
the trimmer with `DynamicallyAccessedMembers`, so the trimmer keeps those fields and the code is safe under trimming and
Native AOT. You do not need to do anything for it.

There is one case that does ask something of you. A method of your own that passes its own generic parameter on to
`Choice<T>`, `Score<T>` or `JevAnswers.Get` needs the same annotation on that parameter,
`[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]`.
[Question sets at run time](question-sets-at-run-time.md#build-once-and-share) says where.

## State types need JSON metadata

A typed [state](typed-evaluation.md#the-state), such as a support ticket record, is written as JSON through
`System.Text.Json`. Under Native AOT that needs source-generated metadata, which a `JsonSerializerContext` gives. Pass
the context's `JsonTypeInfo` to the `EvaluateAsync<T, TState>` overload, and nothing is found by reflection. A state
you pass as a string, a `JsonElement` or UTF-8 JSON needs no metadata at all, and `JevContent.FromValue` takes the same
`JsonTypeInfo`.

## Publishing your own application

Add `PublishAot` to the project that you publish, and publish for a runtime identifier:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

```shell
dotnet publish -c Release -r linux-x64
```

Publishing for Native AOT needs the native toolchain of the platform. Microsoft's
[Native AOT deployment guide](https://learn.microsoft.com/dotnet/core/deploying/native-aot/) lists it. A publish that
reports trimming or AOT warnings, whose codes start with `IL2` or `IL3`, is telling you that something in the program
will not survive, and the warning names it.

## Where it is checked

The repository holds a smoke application, `samples/ZeroAlloc.Jev.AotSmoke`. The `aot-smoke` job of the CI workflow
publishes it with `PublishAot` and runs the native executable. The project treats every warning as an error, including
the whole `IL2xxx` and `IL3xxx` range, so a trimming or AOT warning anywhere in the client fails the build. The program
exits with a failure if any check fails.

The checks run the real client over a canned HTTP handler. They cover:

- raw, typed and built-set evaluation, and a typed state;
- failures: a rejected request, an unreadable response, an overloaded service that is retried, and the unsupported
  model listing on OpenRouter;
- logging through a real `LoggerFactory`, and the telemetry spans and metrics through real listeners;
- registering clients with [dependency injection](dependency-injection.md), and binding their options from
  configuration;
- the allocation budgets in the next section.

## What the client allocates

An allocation is memory the garbage collector must later reclaim. A client that allocates little causes few collections,
which keeps the pauses short in a program that makes many calls. Jev is built to allocate little. The typed and
built-set calls write the request into a pooled buffer and read the response from one, and reading an answer creates no
object.

- **Reading an answer allocates nothing.** `Noul`, `Choice<T>` and `Score<T>` are read as structs, and so are the
  `Probabilities`, the confidence helpers and `JevAnswers.Get` for a [set built at run
  time](question-sets-at-run-time.md).
- **Parsing a typed answer set allocates the result.** That is the result record, plus the shared buffer that holds the
  probabilities of its answers, and nothing else.
- **A whole call allocates a few kilobytes.** Over the canned handler, a typed call measures 3368 B under Native AOT,
  and [Performance](performance.md) has the measurements for the other paths.
- **Building a question set allocates the set.** It measures 6592 B, so build it once and share it, as the
  [run-time page](question-sets-at-run-time.md) advises.
- **Logging and telemetry add nothing to synchronous calls until something listens.** With no logger, or every level
  off, a call allocates nothing extra. With nothing listening to the source or the meter, telemetry adds nothing to the
  calls that complete synchronously, and 211 B, measured under the JIT, to a typed or built-set call that completes
  asynchronously. [Logging, traces and metrics](observability.md#the-cost-of-logging) says what each adds when it is on.

These claims are enforced, not only measured. The smoke application runs each path below repeatedly, mostly under
`AllocationGate` from the ZeroAlloc.TestHelpers package, and fails if the path allocates more than its budget. The calls
run over a canned in-memory handler, so the budgets measure Jev's own work and not the network.

### The allocation budgets

A budget is set a little above what the path measures under Native AOT, usually the measurement plus about 10 percent,
rounded up to the next 64 bytes. Where the measurement is zero the budget is zero, and the first gates have looser
budgets that [Performance](performance.md) notes. A gate that fails fails the `aot-smoke` job, so an allocation
regression cannot reach a release unnoticed.

| Gate | What it measures | Budget in bytes |
| --- | --- | --- |
| `ReadNoul` | Reading a `Noul` answer. | 0 |
| `ReadChoice` | Reading a `Choice<T>` answer. | 0 |
| `ReadScore` | Reading a `Score<T>` answer. | 0 |
| `JevAnswersGet` | Reading answers of a built set through its handles. | 0 |
| `PatternHelpers` | The confidence and normalization helpers of the patterns. | 0 |
| `NoulEquals` | Comparing two `Noul` answers, directly and through `EqualityComparer<Noul>.Default`. | 0 |
| `GeneratedParse` | Parsing a typed set of three answers. | 192 |
| `EvaluateRoundTrip` | A raw `EvaluateAsync` call. | 5120 |
| `TypedEvaluateRoundTrip` | A typed `EvaluateAsync<T>` call. | 4224 |
| `EvaluateBuiltSetRoundTrip` | An `EvaluateAsync` call over a built set. | 4736 |
| `BuildQuestionSet` | Building a question set. | 7296 |
| `ContentFromValue` | `JevContent.FromValue`. | 320 |
| `ContentFromUtf8Json` | `JevContent.FromUtf8Json`. | 320 |
| `EvaluateRoundTripWithNullLoggerFactory` | A raw call with a logger factory that logs nothing. | 5120 |
| `TypedEvaluateRoundTripWithEveryLevelFiltered` | A typed call with every log level filtered out. | 4224 |
| `EvaluateRoundTripWithDiscardingLogger` | A raw call with every log level on. | 4800 |
| `TypedEvaluateRoundTripWithDiscardingLogger` | A typed call with every log level on. | 3712 |
| `EvaluateRoundTripThroughDependencyInjection` | A raw call through a client resolved from the container. | 4864 |
| `EvaluateRoundTripWhileListening` | A raw call with a span and metric listener attached. | 6272 |
| `TypedEvaluateRoundTripWhileListening` | A typed call with the listeners attached. | 5440 |
| `EvaluateBuiltSetRoundTripWhileListening` | A built-set call with the listeners attached. | 5760 |
| `EvaluateRoundTripThroughBoundConfiguration` | A raw call through a client bound from configuration, equal to a hand-built client's own measurement. | same as the hand-built client |
| `DisabledLoggerAddsNothingWhereAnEnabledOneDoes` | Asynchronous calls with no factory, a null factory and an enabled logger. Checks the disabled ones add nothing. | no byte budget |
| `TelemetryOffAsynchronousTypedEvaluation` | A typed call that completes asynchronously, with nothing listening. The median of five runs. | 5056 |

The two logging rows with a logger that does nothing keep the budgets of the calls without one, because the client takes
the unlogged path. The two rows with every level on are higher, and the two listening rows show what a span and the
metrics cost. Each budget has a section in [Performance](performance.md) that explains the measurement behind it. The
phases there are [3.1 for logging](performance.md#phase-31--logging), [3.2 for
telemetry](performance.md#phase-32--telemetry) and [3.3 for dependency injection](performance.md#phase-33--di-package).

A call that completes asynchronously, as every real network call does, pays for a state machine that a synchronous call
does not. The canned handler completes synchronously, so most gates do not see that cost, and the last gate in the table
exists to measure it.

## Next

- [Diagnostics](diagnostics.md): the compile-time rules for question sets.
- [Logging, traces and metrics](observability.md): what the client reports about each call.
- [Performance](performance.md): the benchmarks and the measurements behind every budget.
