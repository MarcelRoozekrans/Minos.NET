---
id: time-budget
title: Setting the time budget
sidebar_position: 3
description: Give escalation a time budget that covers the local call and the hosted call, and choose it from your own latencies.
---

# Setting the time budget

Escalation asks a second call of a stronger decider. That call needs time. This page shows how the stage decides there
is time for it, what happened when the budget was too small, and how to choose one.

[The pipeline guide](../pipeline.md#escalating-unsure-questions) lists every option of the stage. This page is about
the two time options.

## What the time check does

The stage works to a deadline. It is the `Deadline` of the request, an absolute time. A request with no `Deadline` gets
the stage's start plus `Budget`. With neither, there is no deadline and the check never stops a call.

The check runs once the first decider has answered. If some questions are unsure, the stage looks at the time left
before the deadline:

- With at least `ExpectedLatency` left, it asks the stronger decider the unsure questions.
- With less left, it does not call. The unsure questions keep the first answer, and their route says `NoTimeLeft`.

A hosted call that is still running at the deadline is cancelled. It counts as a failed target, and the unsure
questions keep the first answer. The deadline does not cut the first decider's own call: the caller's cancellation token
governs that call. So the first call spends the budget, and the hosted call gets what it leaves.

<!-- snippet: Escalation_Budget -->
```cs
// Each call gets 15 seconds from the moment the stage starts, and the hosted call needs about 300 ms of it.
public static IServiceCollection AddTriage(IServiceCollection services, string localKey, string hostedKey)
{
    services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
    services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
        .UseEscalation("hosted", options =>
        {
            options.PrimaryName = "local";
            options.Budget = TimeSpan.FromSeconds(15);
            options.ExpectedLatency = TimeSpan.FromMilliseconds(300);
        });
    return services;
}

// A host that took 2 seconds to prepare the message passes an absolute Deadline, so the budget counts from when the
// work began. The questions that found no time for the hosted call are the ones routed NoTimeLeft.
public static async Task<IReadOnlyList<string>> KeysWithoutTimeAsync(
    IDecisionClient client, QuestionSetDefinition definition, string message, DateTimeOffset startedAt, CancellationToken cancellationToken)
{
    var request = new DecisionRequest(definition, DecisionContent.FromString(message)) { Deadline = startedAt + TimeSpan.FromSeconds(15) };
    var result = await client.EvaluateAsync(request, cancellationToken);
    if (result.IsFailure)
    {
        return [];
    }

    return result.Value.Path!.Routes.Where(route => route.Reason == RouteReason.NoTimeLeft).Select(route => route.Key).ToList();
}
```
<!-- endSnippet -->

## The first triage run: a budget the local call used up

The triage run sent 400 tickets through a local model, with escalation to a hosted one, as
[the thresholds page](thresholds.md) describes. Its first attempt used the default budget of 5 seconds. The local call
took a median of 5.5 s and p95 6.1 s on the CPU-only runner, so it used up the budget before the stage looked at the
time. All 106 questions below their threshold ended as `NoTimeLeft`. There was no target call, and nothing was billed.

The second run used a budget of 15 seconds and an expected latency of 300 ms. The local call took a median of 5282.9 ms,
and the hosted call a median of 102.3 ms. The same 106 questions were escalated, in 104 target calls. That is 106
questions over 104 tickets, since a ticket with two unsure questions needs one call.

The first run is quoted from [the triage report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77),
which does not commit its records. The second run's figures are in the same report.

## Choose a budget

Add the two calls, and leave a margin:

1. **The local call.** Read its median and p95 from your own records, not from this page. Record the decisions of the
   local model alone, as [Recording decisions](../recording.md) describes, and read `latencyMs` from each record. The
   latency of a call includes its retries.
2. **The hosted call.** Add what the target usually takes. That is also the value for `ExpectedLatency`, which the stage
   compares with the time left.
3. **A margin.** Start from the p95 of the local call, not the median, so a slow local call does not use the whole
   budget.

For the triage run, the local p95 was about 6 s and the hosted call about 0.3 s, so 15 seconds left a wide margin. A
budget of 5 seconds left none.

The budget leaves out time the host spent before the stage started, such as preparing a ticket. If your app has a
deadline of its own, set `Deadline` on the request when the work begins, as the snippet shows. Then the budget counts
from there, and not from the stage.

## When escalation pays off

Escalation needs a hosted call that is faster than the local one, or a local call that leaves room in the budget. On CPU
hardware, where the local call took over 5 seconds, that means a faster local server or a larger budget. With a budget
the local call fills, the stronger decider is never asked, and the cheap decider's unsure answers stand.

## Next

- [The pipeline](../pipeline.md#escalating-unsure-questions): every option of the escalation stage.
- [Sizing the thresholds](thresholds.md): the certainty below which a question is escalated.
- [Recording decisions](../recording.md): read the latency of each call from your own records.
- [The Banking77 triage report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77): the run behind the figures.
