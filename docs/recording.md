---
id: recording
title: Recording decisions
sidebar_position: 12
description: Keep a record of every decision with Minos.NET.Recording, attach outcomes later, erase by a key you choose, redact the state, and read the JSONL file back.
---

# Recording decisions

`Minos.NET.Recording` keeps a record of every decision your app makes. One pipeline stage hands each decision to a
recorder, and the recorder writes it to a sink: a JSONL file, memory, or a store of your own. Later you attach what
happened, such as the answer a reviewer confirmed, by the decision's id, and you erase the records of a customer by a
key you chose. The records are what you turn into a [labelled dataset](datasets.md), to measure a model on your own
traffic.

The stage never makes a decision slower or makes it fail. It returns the inner client's result unchanged, runs your
redaction before anything is kept, and never puts the state, a decision id or an erasure key in a log, a trace or a
metric. It runs under [Native AOT](native-aot.md).

```shell
dotnet add package Minos.NET.Recording
```

The examples on this page ask these questions:

<!-- snippet: Recording_Questions -->
```cs
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minos;
using Minos.Recording;

public enum SupportLane
{
    [Criterion("Payments, invoices and refunds")]
    Billing,

    [Criterion("Bugs, outages and integrations")]
    Technical,
}

[Questions]
public partial record SupportTriage
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which queue should handle this?")]
    public partial Choice<SupportLane> Queue { get; }
}
```
<!-- endSnippet -->

## Registering it outermost

A `DecisionRecorder` owns a bounded queue and one background writer that calls the sink. Create one for the app, and
add the stage with `UseRecording(recorder)`, the first stage you add, so that it is the outermost:

<!-- snippet: Recording_Register -->
```cs
public static class RecordedApp
{
    public static async Task RunAsync(IDecisionClient client, string path, IServiceProvider services, Func<IDecisionClient, Task> work)
    {
        // The app owns the sink and the recorder. Declared in this order, the recorder is disposed first, and its
        // dispose writes what is still queued to the sink before the sink closes the file.
        await using var sink = new JsonlDecisionRecordSink(path);
        await using var recorder = new DecisionRecorder(sink, new RecordingOptions { Capacity = 1024 }, services.GetService<ILoggerFactory>());

        // The recording stage goes first, so it is outermost: one decision is one record, a cache hit included.
        using var recorded = client.AsBuilder()
            .UseRecording(recorder)
            .UseCaching()
            .UseRetries()
            .Build(services);

        await work(recorded);
    }
}
```
<!-- endSnippet -->

- **Outermost means one record per decision.** A cache hit is recorded, with `IsCached` set, and a call the retry stage
  retried is one record with its final result. A `SystemOneDecisionClient` with its standard pipeline already retries
  inside, so any stage you add to it is outside its retries.
- **Inside a retry stage,** if you put it there anyway, each attempt is recorded under the same decision id with its
  attempt number, the request's `RetryAttempt`. A reader of the file keeps the record with the highest attempt.
- **Every pipeline built with one recorder shares it,** its queue and its one writer, so a sink is never called by two
  writers at once.
- **You own the recorder and the sink.** Disposing a pipeline never disposes the recorder, and the recorder never
  disposes the sink. Dispose the recorder first: its `DisposeAsync` writes what is still queued to the sink, for up to
  `RecordingOptions.FlushTimeout`, 5 seconds by default. Then dispose the sink.
- **The recorder's settings** are in `RecordingOptions`: `Capacity`, the queue's size, 1,024 by default; `FlushTimeout`;
  and `Redact`, described [below](#redaction). The recorder logs through the `ILoggerFactory` you pass and reads the
  time from a `TimeProvider`, `TimeProvider.System` by default.

The stage is `RecordingDecisionClient`. Create it with `new` to wrap a client without a builder. The app reaches the
recorder as an `IDecisionRecorder`, either the instance it created or `client.GetService<IDecisionRecorder>()`, which
finds it through the stage.

### With dependency injection

`UseRecording()` with no recorder takes the `DecisionRecorder` from the services given to `Build`, and `Build` throws
`InvalidOperationException` when there is none. Register the sink and the recorder as singletons:

<!-- snippet: Recording_DependencyInjection -->
```cs
public static class RecordedServices
{
    public static IServiceCollection AddRecordedDecisions(this IServiceCollection services, string apiKey, string path)
    {
        // The container creates the sink before the recorder and disposes them in the reverse order, recorder first.
        services.AddSingleton(_ => new JsonlDecisionRecordSink(path));
        services.AddSingleton(provider => new DecisionRecorder(
            provider.GetRequiredService<JsonlDecisionRecordSink>(),
            new RecordingOptions(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()));
        services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // UseRecording with no recorder takes the DecisionRecorder from the container. A stage added here wraps the
        // registered client and its standard pipeline, so it is outermost already.
        services.AddSystemOneDecisionClient(options => options.ApiKey = apiKey).UseRecording();
        return services;
    }
}
```
<!-- endSnippet -->

The container disposes the recorder before the sink, because it created the recorder after it. Both are disposed
asynchronously, as a host disposes its container; a `ServiceProvider` disposed with `Dispose()` instead throws for a
service that is only `IAsyncDisposable`.

## The scope

A decision's id and its erasure key travel in a scope, so `IDecisionClient` and the typed extensions do not change.
`DecisionRecording.Begin` starts one, and the decision made inside it is recorded with its id and key:

<!-- snippet: Recording_Scope -->
```cs
public static class TicketDecisions
{
    // Begin the scope in the method that awaits the call. The id is the one you give, or one the stage generates.
    public static async Task<(SupportLane? Queue, string? DecisionId)> RouteAsync(
        IDecisionClient client, string customerId, string message, CancellationToken cancellationToken)
    {
        using var scope = DecisionRecording.Begin(erasureKey: customerId);
        var result = await client.EvaluateAsync<SupportTriage>(message, cancellationToken);

        // Keep the id with the ticket, so outcomes and erasures can name this decision later.
        return (result.IsSuccess ? result.Value.Queue.Value : null, scope.DecisionId);
    }
}
```
<!-- endSnippet -->

- **The id** is the one you pass as `decisionId`, or one the stage generates when the call is made, a UUIDv7 string.
  `DecisionRecordingScope.DecisionId` returns it; a generated id is `null` until the decision is made.
- **The erasure key** is optional, and is usually the id of the person the decision was about. A record without one is
  still erased by its decision id.
- **One fresh decision per scope.** A second call in the same scope throws `InvalidOperationException` before the inner
  client is called, so two records can never share an id by mistake. A retry, with `RetryAttempt` above 0, is not a
  second decision. A call whose inner client throws makes no decision: it releases the scope, and the scope can be used
  again.
- **Begin the scope in the method that awaits the call,** or in one that calls that method. A scope flows into the calls
  made inside it, as an `AsyncLocal` does, but a scope begun inside an async helper does not flow back out to the
  helper's caller, so the caller's call would be recorded with an id of its own.
- **Scopes do not nest.** `Begin` throws `InvalidOperationException` while a scope is active in the same flow. Parallel
  flows each see their own scope.
- **With no scope,** the decision is still recorded, with a generated id that you cannot see. That is enough for an app
  that never attaches outcomes.

## Outcomes

An outcome is what became of a decision. Attach it by the decision's id with `AddOutcomeAsync`. A record can have
several, and each has a source, such as `reviewer-ui`, and a time.

<!-- snippet: Recording_Outcomes -->
```cs
public static class TicketOutcomes
{
    // A reviewer moved the ticket: the right answers, which a dataset can use as expected answers.
    public static ValueTask ConfirmAsync(IDecisionRecorder recorder, string decisionId, CancellationToken cancellationToken)
        => recorder.AddOutcomeAsync(
            decisionId,
            DecisionOutcome.Confirmed("reviewer-ui", DateTimeOffset.UtcNow, new Dictionary<string, ConfirmedAnswer>
            {
                ["queue"] = ConfirmedAnswer.ForChoice("technical"),
                ["is_urgent"] = ConfirmedAnswer.ForNoul(true),
            }),
            cancellationToken);

    // Something that happened afterwards, which is never an expected answer.
    public static ValueTask ComplainedAsync(IDecisionRecorder recorder, string decisionId, CancellationToken cancellationToken)
        => recorder.AddOutcomeAsync(
            decisionId,
            DecisionOutcome.Signal("mailer", DateTimeOffset.UtcNow, "complained", value: 1, note: "Wrote back within the hour."),
            cancellationToken);
}
```
<!-- endSnippet -->

- **`DecisionOutcome.Confirmed`** gives the right answer to some of the decision's questions, keyed by question key: a
  `ConfirmedAnswer.ForChoice` with the option key, a `ForScore` with the level, or a `ForNoul` with true or false. It is
  read back as a `ConfirmedOutcome`, and its answers can become the expected answers of a dataset item.
- **`DecisionOutcome.Signal`** is something that happened afterwards, such as `complained` or `node-failed`, with an
  optional number and note. It is read back as a `SignalOutcome`, and it never becomes an expected answer.
- **`AddOutcomeAsync` completes when the outcome is queued,** not when the sink has it. It waits for queue space instead of
  being dropped. `FlushAsync` completes when everything queued before it has been handed to the sink.

## Erasure

`EraseAsync(erasureKey)` erases every record with that key, and `EraseDecisionAsync(decisionId)` erases one decision.
Both erase the outcomes too.

<!-- snippet: Recording_Erasure -->
```cs
public static class CustomerErasure
{
    // Returns whether the sink confirmed the erasure. When it did not, keep the request and try again later.
    public static async Task<bool> ForgetAsync(IDecisionRecorder recorder, string customerId, CancellationToken cancellationToken)
    {
        try
        {
            await recorder.EraseAsync(customerId, cancellationToken);
            return true;
        }
        catch (RecordingException)
        {
            return false;
        }
    }
}
```
<!-- endSnippet -->

- **An erasure completes after the sink has run it,** so a call that returns is a confirmed erasure. It erases what was
  queued before it, since it goes through the same queue as the records.
- **It can fail.** When the sink throws, or the recorder is disposed before the sink ran it, the call throws
  `RecordingException`. Its message holds no key or id, and the sink's exception is its inner exception. Keep the
  request and run it again.
- **It is never dropped.** When the queue is full, the erasure waits for space, up to your cancellation token.
- **An erasure in a file is a line,** appended like any other. A reader applies it to the lines before it, so a record
  written after it with the same key is a new decision and is kept. `DecisionRecordFile.CompactAsync` rewrites the file
  without the erased records; see [the file format](#the-jsonl-format).

## Redaction

`RecordingOptions.Redact` decides what is kept of a decision's state. It runs after the call, on the caller's thread,
before the record is queued, and only changes what is recorded: the provider already saw the state as you sent it.

<!-- snippet: Recording_Redaction -->
```cs
public static partial class StateRedaction
{
    // Text is kept with e-mail addresses masked. Structured state is not kept at all.
    public static RecordingOptions Options() => new()
    {
        Redact = state => state.TryGetString(out var text)
            ? RedactionResult.Keep(Email().Replace(text!, "[email]"))
            : RedactionResult.Drop,
    };

    [GeneratedRegex(@"[^\s@]+@[^\s@]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Email();
}
```
<!-- endSnippet -->

- **`RedactionResult.Keep(state)`** keeps a record with that state, as it was or rewritten.
- **`RedactionResult.Drop`** keeps no record of the decision, counted as `dropped_redaction`.
- **With no redactor,** the state is kept as it was given, text or JSON.
- **A redactor that throws,** or returns `null`, keeps no record. The decision is unaffected, and the failure is logged
  as event 1011 with the exception's type name only, and counted as `redaction_failed`.

Images are never kept: a record holds how many there were and each one's SHA-256.

## The sinks

A sink is where the recorder hands its records, outcomes and erasures. Two ship with the package, and any
`IDecisionRecordSink` works:

- **`JsonlDecisionRecordSink`** appends one line per record, outcome and erasure to a file, in [the format](#the-jsonl-format)
  below. Each line reaches the operating system in one write before the call returns, but is not forced to disk. Other
  processes can read the file while it is written. If the file's last line was cut, by a crash or a failed write, the
  next line starts on a line of its own. On Unix, a file the sink creates has the mode `0600`, readable and writable by
  its owner only, because records hold your state.
- **`InMemoryDecisionRecordSink`** keeps records and their outcomes in memory, readable from `Records`, for tests:

<!-- snippet: Recording_InMemory -->
```cs
public static class RecordingInTests
{
    // In a test, keep the records in memory and flush before reading them.
    public static async Task<RecordedDecision> RecordOneAsync(IDecisionClient fake, CancellationToken cancellationToken)
    {
        var sink = new InMemoryDecisionRecordSink();
        await using var recorder = new DecisionRecorder(sink);
        using var client = fake.AsBuilder().UseRecording(recorder).Build();

        await client.EvaluateAsync<SupportTriage>("The invoice is wrong.", cancellationToken);
        await recorder.FlushAsync(cancellationToken);

        return sink.Records.Single();
    }
}
```
<!-- endSnippet -->

A sink of your own implements `WriteAsync`, `AddOutcomeAsync`, `EraseAsync` and `EraseDecisionAsync`, and keeps this
contract:

- **One caller.** The recorder calls the sink from its one writer, one call at a time and in queue order, so a sink
  written by one recorder needs no locking of its own. Do not call it yourself while a recorder writes to it.
- **Never throw for an orphan.** After a restart, an outcome or an erasure can arrive for a record that was lost. Store
  it or ignore it.
- **A call that throws** is logged as event 1010 with the exception's type name only, counted, and skipped. The writer
  goes on to the next item. A failed erasure also fails the caller's `EraseAsync`.
- **The cancellation token** is cancelled when the recorder's dispose runs out of time.
- **The app disposes the sink,** after the recorder.

## The JSONL format

The file is UTF-8 JSONL, one JSON object per line. Every line has a `kind`, which is `record`, `outcome` or `erasure`,
and a `version`, which is `1`. A version 1 reader skips members it does not know, so members can be added to version 1
without breaking it.

<!-- decision record file -->
```json
{"kind":"record","version":1,"decisionId":"0199f1d2-7c4e-7a10-9b3e-2f6d8c1a4e55","erasureKey":"customer-7","attempt":0,"started":"2026-10-22T09:30:00+00:00","latencyMs":212.5,"provider":"typesafe","endpoint":"https://api.example.com/","defaultModel":"jev-latest","servedModel":"jev-1.14","usage":{"inputTokens":120,"outputTokens":2},"cached":false,"questions":{"is_urgent":{"type":"noul","instructions":"Does this convey urgency?"},"queue":{"type":"choice","instructions":"Which queue should handle this?","criteria":{"billing":"Payments, invoices and refunds","technical":"Bugs, outages and integrations"}}},"state":"The app crashes when I pay.","imageCount":0,"answers":[{"key":"is_urgent","kind":"noul","probability":0.9,"confidence":0.8},{"key":"queue","kind":"choice","chosenIndex":1,"confidence":0.8,"probabilities":[0.2,0.8]}]}
{"kind":"outcome","version":1,"decisionId":"0199f1d2-7c4e-7a10-9b3e-2f6d8c1a4e55","type":"confirmed","source":"reviewer-ui","at":"2026-10-22T09:35:00+00:00","confirmed":{"queue":"technical","is_urgent":true}}
{"kind":"outcome","version":1,"decisionId":"0199f1d2-7c4e-7a10-9b3e-2f6d8c1a4e55","type":"signal","source":"mailer","at":"2026-10-22T10:30:00+00:00","name":"complained","value":1,"note":"Wrote back within the hour."}
{"kind":"record","version":1,"decisionId":"0199f1d2-8d5f-7b21-8c4f-3a7e9d2b5f66","attempt":0,"started":"2026-10-22T09:31:00+00:00","latencyMs":48.25,"provider":"typesafe","endpoint":"https://api.example.com/","defaultModel":"jev-latest","cached":false,"questions":{"is_urgent":{"type":"noul","instructions":"Does this convey urgency?"}},"state":{"subject":"Refund","body":"Where is my refund?"},"imageCount":0,"errorKind":"RateLimited"}
{"kind":"erasure","version":1,"decisionId":"0199f1d2-8d5f-7b21-8c4f-3a7e9d2b5f66"}
```

- **A record** holds the decision id and the erasure key, if any; `attempt`; when the call started and how long it took;
  the inner client's `provider`, `endpoint` and `defaultModel`; the `requestedModel`, the `servedModel`, the
  `responseId` and the `upstreamProvider` when there are any; `usage`, with its tokens and `cost`; whether the answer
  was `cached`; the question set, in the same shape as a [dataset line's `questions`](datasets.md#questions); the
  `state` after redaction, as text or JSON; and `imageCount` with `imageHashes`. On success it has `answers`: the
  kind, the chosen index, the probability, the expected value, the confidence and the probabilities of each. On an error
  it has `errorKind` and nothing of the error's message, since a provider's message can quote the request. A refusal is
  the error kind `Refused`, with the refused question keys in `refusedKeys`.
- **An outcome** has a `type`, `confirmed` or `signal`, its `source` and the time `at`. A confirmed outcome's answers are
  in `confirmed`: an option key for a Choice, a level for a Score, and true or false for a Noul.
- **An erasure** has an `erasureKey` or a `decisionId`.

`DecisionRecordFile.ReadAsync(path, cancellationToken)` reads a file and returns each surviving decision as a
`RecordedDecision`: its `DecisionRecord`, with `RecordedAnswer` values for the answers, and its outcomes in file order.
The example above reads as one decision with two outcomes, since the second record is erased by its id.

<!-- snippet: Recording_ReadFile -->
```cs
public static class RecordFileReading
{
    // Reads every decision that survives its erasures, each with its outcomes. Bad lines are reported, not thrown.
    public static async Task<IReadOnlyList<RecordedDecision>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var problems = new List<DecisionRecordFileProblem>();
        var decisions = new List<RecordedDecision>();
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, cancellationToken))
        {
            decisions.Add(decision);
        }

        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"line {problem.Line}: {problem.Message}");
        }

        return decisions;
    }

    // Rewrites the file without erased records. Run it when no recorder writes to the file.
    public static async Task<int> CompactAsync(string path, CancellationToken cancellationToken)
    {
        var problems = await DecisionRecordFile.CompactAsync(path, cancellationToken);
        return problems.Count;
    }
}
```
<!-- endSnippet -->

- **An erasure applies to the lines before it.** A record that comes after it is a new decision and is kept.
- **Of the records of one decision id, the highest attempt wins,** and the last one of equal attempts. The outcomes of
  every attempt are kept.
- **An outcome with no record before it** is ignored and reported.
- **A bad line is a problem, not an exception.** The overload that takes a collection fills it with
  `DecisionRecordFileProblem` values, each with its line number and a message that never holds an id or any state, and
  the lines around it are still read.
- **`CompactAsync`** rewrites the file with only what a read keeps, and returns its problems as
  `DecisionRecordFileProblems`. It copies each surviving line as it was, so members this reader does not know are kept.
  The new file replaces the old one in one step, after it was flushed to disk, and keeps the old file's permissions.
  **A file it cannot fully read is left untouched,** byte for byte, with a problem that says why: a line of a format
  version or a kind it does not know could hold data that an erasure is meant to remove. Do not compact a file a
  recorder is still writing to.

## The queue and what a crash loses

The decision returns as soon as the inner client has answered. The stage queues the record without waiting, and the
writer hands it to the sink later. That keeps the sink off the decision's path, at a price:

- **A full queue drops records, never outcomes or erasures.** A record that finds the queue full is dropped, counted as
  `dropped_full` and logged as event 1009 with the number dropped. Outcomes, erasures and flushes wait for space instead.
- **A crash loses what is still queued.** A record, an outcome or an erasure is only as safe as the sink once the sink
  has it. An erasure is confirmed when `EraseAsync` returns; re-run one you could not confirm.
- **Dispose drains the queue for at most `FlushTimeout`.** Then it cancels the sink's call and gives up on the rest,
  logged as event 1012 with each kind counted. A record lost then is counted as `dropped_flush_timeout`, and one handed
  over after dispose as `dropped_disposed`.

## What is never recorded in logs or telemetry

The recorder never writes the state, before or after redaction, a decision id, an erasure key or a provider's error
message to a log, a span, a metric tag or an exception message. Its logs carry counts and exception type names only,
in the category `Minos.DecisionClient`:

| Id | Event | Level | When |
| --- | --- | --- | --- |
| 1009 | `RecordsDropped` | Warning | The queue was full, and records were dropped. |
| 1010 | `SinkCallFailed` | Warning | A sink call threw; the item was skipped. |
| 1011 | `RedactionFailed` | Warning | The redactor threw or returned `null`; no record was kept. |
| 1012 | `FlushTimedOut` | Error | Dispose ran out of time; the records, outcomes, erasures and flushes it lost are counted. |
| 1013 | `RecordingFailed` | Warning | The stage could not make a record after the call, for example because a metric listener threw; the decision is unaffected. |

Its instruments are on the meter named `Minos`, the same as the client's, and each tag is a fixed value:

| Instrument | Kind | Tags |
| --- | --- | --- |
| `minos.recording.records` | Counter | `result`: `written`, `dropped_full`, `dropped_redaction`, `redaction_failed`, `sink_failed`, `dropped_disposed` or `dropped_flush_timeout`. Every record ends in exactly one. |
| `minos.recording.outcomes` | Counter | `result`: `written` or `failed`. |
| `minos.recording.erasures` | Counter | `result`: `written` or `failed`. |
| `minos.recording.log_failures` | Observable counter | none: the recorder's log events lost because the logger threw. |
| `minos.recording.record_failures` | Observable counter | none: the decisions the stage could not record. |
| `minos.recording.queue_length` | Observable gauge | none: the items waiting in every recorder's queue. |

The two failure counters read totals kept in the process, so counting a failure never calls a listener and cannot fail
in turn. A logger that throws loses its event but never stops the recorder.
[Logging, traces and metrics](observability.md) covers the rest of what Minos emits.

## Next

- [Labelled datasets](datasets.md): the format the records' question sets share, and how a dataset is run.
- [Logging, traces and metrics](observability.md): the client's own logs, spans and metrics.
- [The client pipeline](pipeline.md): the other stages, and the order they go in.
- [Native AOT and allocations](native-aot.md): what running as a native executable means for the library.
