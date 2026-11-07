---
id: composing-and-privacy
title: Composing the stages and keeping data private
sidebar_position: 5
description: Put escalation outside fallback, check that every provider in the chain may see the state, and know what a record keeps for a path that crossed providers.
---

# Composing the stages and keeping data private

Escalation, fallback and recording each work alone. This page covers the two things that matter when you use them
together: the order they sit in, and which providers see your data.

## Why escalation sits outside fallback

Put `UseEscalation` outside `UseFallback`. The first `Use` is the outermost stage, so list escalation first.

- **Fallback makes each tier reliable.** If the local model is down, the chain asks the next client, so the local tier
  still answers.
- **Escalation then moves questions between tiers.** It looks at the answer that came back, from whichever client gave
  it, and sends the unsure questions to the stronger decider.

With this order, the path is one flat list. `Path.Calls` holds every client tried, the failed and skipped ones included.
A question the escalation stage finds confident keeps the route the fallback stage gave it, except `FellBack`, which
reads `Confident`. A question it escalates reads `Escalated`. [The fallback page](fallback.md#reading-the-path) shows how
to read it.

<!-- snippet: Escalation_Compose -->
```cs
// The first Use is the outermost stage. Recording sees one decision per call, escalation moves doubtful questions to
// "hosted", and fallback keeps the local model reliable by asking "backup" when it fails. Both "hosted" and "backup"
// may be sent the state, so register only providers that may see it. The recorder is registered as the recording page
// shows.
public static IServiceCollection AddComposedDecisions(IServiceCollection services, string localKey, string hostedKey, string backupKey)
{
    services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
    services.AddSystemOneDecisionClient("backup", options => options.ApiKey = backupKey);
    services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
        .UseRecording()
        .UseEscalation("hosted", options => options.PrimaryName = "local")
        .UseFallback(new KeyedFallbackStep("backup", TimeSpan.FromMilliseconds(300)));
    return services;
}
```
<!-- endSnippet -->

[The pipeline guide](../pipeline.md#building-your-own-pipeline) has the full order and why each stage sits where it
does. For this recipe, the stages that matter are in this order, outermost first:

1. recording, so one decision is one record;
2. telemetry, so the span covers the whole call (not in the snippet);
3. escalation, so a stronger client is asked only for the questions the first was unsure of;
4. fallback, so the next client is asked only when the first fails;
5. caching, so a hit takes no permit;
6. retries, outside the limiter, so a local rejection is retried after its `RetryAfter`;
7. rate limiting, so every attempt takes a permit.

The snippet adds the first, third and fourth stages to a client from dependency injection. It adds no telemetry stage.
Each client's standard pipeline gives it a span of its own, and the escalation stage records none. One span over the
whole call, with the escalation and fallback tags, needs `UseOpenTelemetry` placed outside escalation, as [the stage
order](../pipeline.md#stage-order) shows. The standard pipeline also already holds retries, and it runs inside every
stage you add.

## Every client you add may see the state

A question below its threshold is sent to the target with the state of the request, and its images. So is a fallback
step: when it is called, it receives the full state. Redaction does not change that. [The step by
step table](../running-locally.md#what-stays-private-step-by-step) says it for recording: redaction runs before a record
is kept and changes only what is recorded, and the provider still gets the state whole.

So a target and a fallback step must meet the same data rules as the primary. If the state may go only to a provider
with zero data retention, or must not leave the machine, then every client in the chain must be one that may see it. A
local primary does not make the chain local. The escalated questions leave the machine for the target.

Check this when you add a client to the chain, not when you review the first week of traffic.

## What a record keeps for a path that crossed providers

With recording outermost, a decision that crossed providers is still one record. [The escalation
path](../recording.md#the-escalation-path) section of the recording guide lists what it holds for the path:

- for each answer, the name of the client that answered and the reason, with the certainties;
- for each call, the client name, the model, the usage, whether it was cached, and, for a call that did not answer, its
  status and error kind.

The path holds client names and reasons, never the state. The state is in the record, as for any decision, kept the way
your redaction leaves it. A call on the path does not repeat it.

## Erasing a path

The path is part of its record, so it is erased when the record is. `EraseDecisionAsync` with the decision id erases that
one decision, and `EraseAsync` with the erasure key erases every record under it. [Erasure](../recording.md#erasure)
describes both. A record that crossed providers needs no extra step.

The JSONL sink's erasure is a tombstone: the erased state stays in the file. For production, use a sink over your own
store that deletes. An export is a copy that a later erasure does not reach, so delete earlier exports after an erasure
and export again.

Erasure does not reach the providers either. The target and any fallback step that was called were sent the state, and what
they keep is set by the rules of each account.

## Next

- [The pipeline](../pipeline.md#building-your-own-pipeline): the full stage order.
- [What stays private, step by step](../running-locally.md#what-stays-private-step-by-step): who sees the state at each step.
- [Recording decisions](../recording.md#the-escalation-path): what a record keeps for a path, and how to erase it.
- [Falling back to another provider](fallback.md): the chain this page puts escalation around.
