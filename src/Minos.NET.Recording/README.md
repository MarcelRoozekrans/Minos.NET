# Minos.NET.Recording

A decision recorder for [Minos.NET](https://www.nuget.org/packages/Minos.NET): one pipeline stage writes a record of every decision, with its question set, state, answers and usage, to a JSONL file or a sink of your own. Outcomes are attached to a record later by its decision id, and records are erased by a key you choose. The stage never delays or fails a decision, runs your redaction before anything is kept, and never puts the state, an id or a key in a log, a trace or a metric. Native AOT-compatible.

```
dotnet add package Minos.NET.Recording
```

```csharp
using Minos;
using Minos.Recording;

await using var sink = new JsonlDecisionRecordSink("decisions.jsonl");
await using var recorder = new DecisionRecorder(sink);

// Outermost, so one decision is one record.
using var recorded = client.AsBuilder().UseRecording(recorder).Build();

using (var scope = DecisionRecording.Begin(erasureKey: customerId))
{
    var answers = await recorded.EvaluateAsync<Triage>(message);
    await recorder.AddOutcomeAsync(
        scope.DecisionId!,
        DecisionOutcome.Signal("mailer", DateTimeOffset.UtcNow, "complained"));
}

// Completes after the sink has erased the customer's records, or throws RecordingException.
await recorder.EraseAsync(customerId);

await foreach (var decision in DecisionRecordFile.ReadAsync("decisions.jsonl", CancellationToken.None))
{
    Console.WriteLine($"{decision.Record.DecisionId}: {decision.Outcomes.Count} outcomes");
}
```

Documentation: [Recording decisions](https://marcelroozekrans.github.io/Minos.NET/recording). Source and issues: [github.com/MarcelRoozekrans/Minos.NET](https://github.com/MarcelRoozekrans/Minos.NET). Licensed under the MIT license.
