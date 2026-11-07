---
id: fallback
title: Falling back to another provider
sidebar_position: 4
description: Keep answering when the cheap decider is down, by asking a cheaper hosted decider and then a stronger one, and read from the path which client answered.
---

# Falling back to another provider

Escalation is for doubt: the first decider answered, and was not sure. Fallback is for failure: the first decider did not
answer at all. This page shows a chain that starts local, tries a cheaper hosted decider, and ends with a stronger one,
and how to read which of them answered.

[The pipeline guide](../pipeline.md#falling-back-to-another-provider) shows how to register the chain and lists its
rules. This page does not repeat the registration. It is about how the chain behaves around escalation.

## Fallback is for failure, escalation is for doubt

The two stages answer different questions.

- **Escalation** looks at an answer that arrived. A question below its threshold goes to the stronger decider.
- **Fallback** looks at a call that failed. A call that fails with a provider error goes to the next client in the chain.

A confident wrong answer never falls back. It is an answer, so the first client's response is returned untouched, with
no path added. Only a better threshold or a stronger decider catches it, as [the thresholds page](thresholds.md)
describes.

## A chain from local to stronger

A common chain is the local model first, then a cheaper hosted decider, then a stronger hosted one. The local model
answers almost every request. When it is down or overloaded, the cheaper hosted decider answers. When that fails too, the
stronger one does.

List the steps in the order you want them asked. Each step has a name and an `ExpectedLatency`. The first client, the
one the stage wraps, is step 0 and is named by `PrimaryName`, which is `"primary"` unless you set it.

Every step receives the full state of the request. So list only providers that may see that state.

## When a step is skipped

A step is skipped for lack of time. This needs a deadline: the `Deadline` of the request, or `Budget` counted from when
the stage starts. With neither, no step is ever skipped, and a call that hangs is left to the caller's cancellation
token.

With a deadline, the stage checks the time left before it calls a step:

- With at least the step's `ExpectedLatency` left, it calls the step.
- With less left, or with the deadline already passed, it skips the step. The call list records it as
  `NotCalledNoTimeLeft`.

Step 0 is different. It is always called, even when the deadline has passed. In that case its call is left to the
caller's token.

The stage also cuts a slow call. A call is cut at the deadline minus the smallest `ExpectedLatency` of the steps after
it, so that one of them still fits. When no step is left after it, the call is cut at the deadline. A cut call fails
with a `Timeout` error, and that goes through `ShouldFallBack` like any other failure.

[The time budget page](time-budget.md) shows how to choose a budget. For a chain, the same advice holds: add the calls
you expect to make, and leave a margin.

## When the chain stops

The chain stops at the first client that answers. It also stops when a failure should not move on:

- By default, `ShouldFallBack` is `FallbackOptions.IsProviderFailure`. It moves on for provider errors such as
  `Overloaded`, `RateLimited`, `Server`, `Network` and `Timeout`. It does not move on for a `Validation` error, since
  the next provider would reject the request too, or for a refusal. Set `ShouldFallBack` to decide otherwise.
- A `Disposed` failure always stops the chain, whatever `ShouldFallBack` says. The app is shutting down. The stage does
  not even ask `ShouldFallBack` about it.

A failure that stops the chain is returned as it is. When every step has failed or been skipped, the error of the last
step called is returned, and the response has no path. The caller's own cancellation stops the chain and throws an
`OperationCanceledException`.

## Reading the path

A response that the first client answered has no `Path`. Read `Path` as nullable.

When a later step answered, the path says so in two places. This holds when the answering step has no routing stage of
its own; the note after the snippet covers a step that has one.

- Every route has the reason `FellBack`, and `AnsweredBy` is the name of the answering step.
- `Path.Calls` lists each client tried or skipped, in order, and the last entry is the one that answered. Each call has a
  `Status`: `Answered` for the call that returned an answer, `Failed` for one that failed, with its `ErrorKind`, and
  `NotCalledNoTimeLeft` for a step the stage skipped.

<!-- snippet: Escalation_FallbackPath -->
```cs
// A response the first client answered has no Path, so there is nothing to list. After a fallback, Calls holds one
// entry per client tried or skipped, in order, and the last one is the client that answered.
public static async Task<IReadOnlyList<string>> CallsAsync(
    IDecisionClient client, QuestionSetDefinition definition, string message, CancellationToken cancellationToken)
{
    var result = await client.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(message)), cancellationToken);
    if (result.IsFailure)
    {
        return [];
    }

    var calls = result.Value.Path?.Calls ?? [];
    return calls.Select(call => $"{call.ClientName}: {call.Status}").ToList();
}
```
<!-- endSnippet -->

A step that has a routing stage of its own, say an escalation to a stronger client, keeps its routes: a question it
escalated still reads `Escalated`, and its calls follow the failed ones, so the last entry is still its last call.

When fallback sits inside escalation, `Path.Calls` is one flat list of every client tried. A question the escalation
stage finds confident keeps the fallback route, except `FellBack`, which reads `Confident`. Who answered is still in
`AnsweredBy`, and that a fallback happened is in the calls.

## Keyed clients with dependency injection

With `Minos.NET.DependencyInjection`, a `KeyedFallbackStep` names a client that `AddDecisionClient` registered under a
key. The key is also the step's name in the path. The container owns those clients, so the stage does not dispose them.
[The pipeline guide](../pipeline.md#falling-back-to-another-provider) has the registration.

## Next

- [The pipeline](../pipeline.md#falling-back-to-another-provider): how to register the chain, and its rules.
- [Setting the time budget](time-budget.md): choose the budget that bounds the chain.
- [Sizing the thresholds](thresholds.md): catch the confident wrong answer that fallback cannot.
