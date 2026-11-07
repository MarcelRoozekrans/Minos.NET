---
id: chat-model-target
title: A chat model as the target
sidebar_position: 6
description: Use any chat client as the stronger decider, and read from ProbabilitySource where its probabilities came from before you trust an escalated answer.
---

# A chat model as the target

The target of an escalation is any `IDecisionClient`. A chat client is one more: wrap it in a `ChatDecisionClient` and
pass it to `UseEscalation`. The questions the primary was unsure of are then asked of a chat model, and the rest of the
pipeline does not change.

The chat model gives its probabilities in a different way from a decision model, and that matters for how far you trust
an escalated answer. This page shows where they come from and how to read it.

<!-- snippet: Escalation_ChatTarget -->
```cs
// chat is any IChatClient. The unsure questions of primary are asked again of it, under the name "chat". The merged
// response reports the weakest source among all its answers, so Verbalized means at least one answer is the model's
// own estimate. Disposing client disposes primary, and target is disposed here too. It leaves chat open.
public static async Task<ProbabilitySource?> SourceAsync(
    IChatClient chat, IDecisionClient primary, QuestionSetDefinition definition, string message, CancellationToken cancellationToken)
{
    using var target = new ChatDecisionClient(chat);
    using var client = primary.AsBuilder().UseEscalation(target, options => options.TargetName = "chat").Build();
    var result = await client.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(message)), cancellationToken);
    return result.IsFailure ? null : result.Value.ProbabilitySource;
}
```
<!-- endSnippet -->

## Where the probabilities come from

Every answer carries a `ProbabilitySource`:

- `Model`: the model reported the probabilities itself. Every other Minos client reports this.
- `Logprobs`: the probabilities come from the logprobs of the answer token. They are measured from the model's output.
- `Verbalized`: the model stated the probabilities in its text.

A `ChatDecisionClient` uses the `Auto` mode unless you set another. It asks for logprobs first and asks again,
verbalized, for a question whose reply has none. [The probability modes](../chat-clients.md#probability-modes) list all
three. [The source section](../chat-clients.md#where-the-probabilities-came-from) says how the sources combine in one
response. The source is chosen for each question, so one answer of a set can be `Logprobs` and another `Verbalized`.

## Why a verbalized answer is weaker evidence

A `Verbalized` probability is the model's own claim, written as text. Nothing measured it, and it is often too sure. A
`Logprobs` probability is read from the output distribution, and a `Model` probability comes from a decision model that
was built to report one. So a `Verbalized` 0.9 is a weaker 0.9.

This matters in two places:

- **A threshold.** The escalation stage compares a certainty with a threshold: the confidence of the picked option for a
  Choice or a Score, and the larger of the probability and its complement for a Noul. It does not look at
  `ProbabilitySource`. If a chat client is the primary, a verbalized 0.9 passes a threshold the same as a measured one.
  [The chat clients guide](../chat-clients.md#confidence) says not to reuse a threshold tuned on a decision model.
- **A calibration run.** A threshold is sized from a report on your own labelled data, as [the thresholds
  page](thresholds.md) shows. Run the chat model through the same labelled data. A verbalized confidence needs its own
  numbers, because its 0.9 does not mean what a logprob 0.9 means.

## A reasoning model gives no logprobs

A reasoning model thinks before it answers, so it returns no logprobs, and some reject the logprob request outright. In
`Auto` that is a wasted call before every verbalized one, and the answers are `Verbalized`. Set the mode to
`Verbalized` for such a model, and for any chat client that wraps an SDK without logprobs: pass
`new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Verbalized }` as the second argument. [The reasoning
models section](../chat-clients.md#reasoning-models) has the details.

## The escalated answer wins, even when it is less sure

The stage does not compare the two answers. For a question it escalated, the target's answer takes the primary's place,
with the target's probabilities and the target's `ProbabilitySource`. It wins when it is less confident than the
primary was, and when it is the weaker kind of evidence. A question the primary was sure of keeps the primary's answer
and its source.

So read `ProbabilitySource` before you trust an escalated answer. The response's value is the weakest among all its
answers, so it tells you that some answer was verbalized, not which one. To find the escalated questions, read the route
of each question in `Path.Routes`: a reason of `Escalated` means the target answered it. Then read the source of that
answer. [The fallback page](fallback.md#reading-the-path) shows the path.

## Next

- [The chat clients guide](../chat-clients.md#probability-modes): the modes, the logprob and verbalized calls, and the cache.
- [Sizing the thresholds](thresholds.md): size a threshold from your own data, then check it for the chat model.
- [Composing the stages and keeping data private](composing-and-privacy.md): the chat provider sees the state too.
