---
id: chat-clients
title: Chat clients
sidebar_position: 7
description: Ask Minos questions over any Microsoft.Extensions.AI chat client, how the bridge gets each answer's probabilities from logprobs or a verbalized estimate, what its confidence means, its capabilities and its errors.
---

# Chat clients

`Minos.NET.Extensions.AI` turns any Microsoft.Extensions.AI `IChatClient` into an `IDecisionClient`. OpenAI,
OpenRouter, Azure, Ollama or any other chat model then answers the same Noul, Choice and Score questions as a decision
model does, with the same typed question sets, pipeline stages, caching and recording.

A chat model is not a decision model. It does not return calibrated probabilities for each option. The bridge gets the
best numbers it can, and every answer says how they were made, so a model's own guess never passes for a measured
distribution.

```shell
dotnet add package Minos.NET.Extensions.AI
```

The package brings `Minos.NET` and `Minos.NET.DependencyInjection`. Its types are in the `Minos.Extensions.AI`
namespace, and `AddChatDecisionClient` is in `Microsoft.Extensions.DependencyInjection`.

## Ask a chat client

`AsDecisionClient` wraps a chat client in a `ChatDecisionClient`. For OpenAI or OpenRouter, the chat client is the
OpenAI SDK's `ChatClient` wrapped with `AsIChatClient()` from `Microsoft.Extensions.AI.OpenAI`; for OpenRouter, set its
endpoint to `https://openrouter.ai/api/v1`.

<!-- snippet: ChatClients_AsDecisionClient -->
```cs
using Microsoft.Extensions.AI;
using Minos;
using Minos.Extensions.AI;

public enum ChatTicketTeam
{
    [Criterion("Invoices, payments, refunds and charges")]
    Billing,

    [Criterion("Errors, crashes and how a feature works")]
    Technical,
}

[Questions]
public partial record ChatTicketCheck
{
    [Noul("Does the customer ask for money back?")]
    public partial Noul WantsRefund { get; }

    [Choice("Which team should handle this ticket?")]
    public partial Choice<ChatTicketTeam> Team { get; }
}

public static class ChatDecisions
{
    // chat is any IChatClient: OpenAI, OpenRouter, Azure, Ollama or another. The decision client does not dispose it,
    // so the app can keep using it for chat.
    public static async Task<string> TriageAsync(IChatClient chat, string ticket, CancellationToken cancellationToken)
    {
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Auto });
        var result = await client.EvaluateAsync<ChatTicketCheck>(ticket, cancellationToken);
        return result.IsFailure
            ? $"{result.Error.Kind}: {result.Error.Message}"
            : $"team: {result.Value.Team.Value}, from {result.Value.Team.ProbabilitySource}";
    }
}
```
<!-- endSnippet -->

`ChatDecisionClientOptions` has four properties, all optional:

- **`ProbabilityMode`** says how the probabilities are made. It defaults to `Auto`; see [below](#probability-modes).
- **`Model`** is the model for requests that name none. Without it, the chat client's own default model is used.
  Surrounding whitespace is trimmed, and a blank value throws `ArgumentException`.
- **`Capabilities`** limits what the bridge accepts; see [Capabilities](#capabilities).
- **`MaxParallelCalls`** is the most chat calls one request has in flight at once. Without it, there is no limit. A
  value below 1 throws `ArgumentException`; see [How a question is asked](#how-a-question-is-asked).

The client reads the options once, when it is created.

## How a question is asked

Each question is one chat call, two when `Auto` falls back. The calls of a request run at once, unless
`MaxParallelCalls` is set. The answers come back in the question set's order, whatever order the calls finish in.

A question set of many questions sends many calls at once, and a provider with a low rate limit may answer some of them
with HTTP 429. Set `MaxParallelCalls` to cap the calls in flight. The other questions wait for a free call, and are not
asked at all when a question fails first. The limit is per request: two requests that run together may each have that
many calls in flight. A question that `Auto` asks again keeps its place, so it never has two calls in flight.

The call has a system message, which tells the model it is a classifier, and one user message. That message holds the
question, its options or levels labelled `A`, `B`, `C` and on, each with its criterion, its examples and what it is not
for, then the state, then any images. A Noul's options are `A` for yes and `B` for no, with its `WhenTrue` and
`WhenFalse` when it has them. JSON instructions, criteria and state are sent as their raw text.

`DecisionResponse.Usage` sums the tokens of every call. `DecisionResponse.Model` is the first model the chat responses
name, in question order. When none names one, it is the request's `Model`, then `ChatDecisionClientOptions.Model`.

## Probability modes

| Mode | What it does |
| --- | --- |
| `Auto` | The default. Asks for logprobs first. When a reply has none, or no option label among them, or the provider rejects the logprob request with HTTP 400 or 422, that question is asked once more, verbalized. |
| `Logprobs` | Asks for logprobs only. A reply without them fails with `Unsupported`, and a rejected request with its own error; it is never asked again. |
| `Verbalized` | Asks the model to state each option's probability. It never asks for logprobs. |

`Auto` decides for each question and each call. It keeps nothing between calls, so one odd reply does not move every
later call onto the verbalized path. The price is an extra call per question for a model that never returns logprobs.

### Logprobs

The logprob call asks for one output token at temperature 0, with the top 20 logprobs. The model is told to answer
with one label, so the first token is the answer, and its top logprobs give a probability to each label.

- A token counts for a label when, trimmed of whitespace, it is that label. Several tokens for one label, such as `A`
  and ` A`, are added up.
- A label that is not among the top 20 gets a share of what is left of 1. Then the probabilities are normalized.
- No label among the top 20 means the reply has no usable answer.

The bridge reads the logprobs from the OpenAI SDK's `ChatCompletion` under the chat response. That covers OpenAI, and
OpenRouter or any other OpenAI-compatible server through the OpenAI client. A chat client that wraps another SDK gives
no logprobs, so it takes the verbalized path.

### Verbalized

The verbalized call asks for a JSON object with one number for each label, and passes a JSON schema as the response
format. It sends no temperature, so the model uses its own. The bridge normalizes the numbers so they sum to 1. A reply
that is not that JSON, leaves out a label, or states a negative value, a value that is not a finite number, or only
zeros, is an `InvalidResponse`.

A verbalized probability is the model's own estimate, written as text. It is not measured from the model's output
distribution, and it is often too sure. Treat it as a weaker signal than logprobs.

### Reasoning models

A reasoning model thinks before it answers, so its first output token is not the answer, and it returns no logprobs.
Some, such as OpenAI's, reject the logprob request outright with HTTP 400, because they take neither logprobs nor a
temperature other than their own. In `Auto`, a logprob request rejected with HTTP 400 or 422 is asked once more,
verbalized, and that call decides: its answer, or its error when it fails too. Other failures, such as 401, 429 or a
5xx, are returned as they are. In `Logprobs` mode the rejection is returned as a `Validation` error.

Either way, `Auto` costs every question a wasted logprob call before the verbalized one. Set `ProbabilityMode` to
`Verbalized` for such a model, and for any chat client that gives no logprobs. The verbalized call sends no
temperature, so a reasoning model accepts it.

## Where the probabilities came from

Every answer carries a `ProbabilitySource`:

| Source | Meaning |
| --- | --- |
| `Model` | The model reported the probabilities itself. Every other Minos client reports this. |
| `Logprobs` | The probabilities come from the logprobs of the answer token. |
| `Verbalized` | The model stated the probabilities in its text. |

The typed answers `Noul`, `Choice<T>` and `Score<T>`, the keyed `KeyedChoice` and `KeyedScore`, and `QuestionAnswer`
all have a `ProbabilitySource` property. In `Auto`, one question of a set can come from logprobs and another be
verbalized, so `DecisionResponse.ProbabilitySource` is the weakest source among the answers: `Verbalized`, then
`Logprobs`, then `Model`.

<!-- snippet: ChatClients_ReadSource -->
```cs
public static class TicketRouting
{
    // A verbalized probability is the model's own estimate, not a measured one, so such an answer never acts alone.
    public static bool CanRouteAlone(Choice<ChatTicketTeam> team, double threshold)
        => team.ProbabilitySource != ProbabilitySource.Verbalized && team.Confidence >= threshold;
}
```
<!-- endSnippet -->

The source stays with the answer. A cached answer keeps it, and a [recorded decision](recording.md#the-jsonl-format)
writes it for each answer.

## Confidence

For a Choice or a Score, the bridge's confidence is the probability of the picked option. A tie picks the option that
comes first. A Noul has its probability and no confidence, as on every provider.

This confidence is **not** TypeSafe's calibrated confidence. TypeSafe's Jev returns a confidence trained to say how far
to trust the answer, as [Confidence is not probability](question-types.md#confidence-is-not-probability) explains. The
bridge has no such number, so it reports the picked option's probability, from logprobs or from the model's own
estimate. Do not reuse a threshold tuned on a decision model. Measure the chat model on your own data first, as
[Labelled datasets](datasets.md) shows: `DatasetRunner` runs a dataset through any `IDecisionClient`.

## Registration

`AddChatDecisionClient` registers a `ChatDecisionClient` over the `IChatClient` in the container. Register a chat
client too, for example with `AddChatClient`. The registration returns the pipeline builder, so stages chain on it
as they do after `AddSystemOneDecisionClient`.

<!-- snippet: ChatClients_Register -->
```cs
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Minos;
using Minos.Extensions.AI;
using Minos.Recording;

public static class ChatDecisionServices
{
    public static IServiceCollection AddChatDecisions(this IServiceCollection services, IChatClient chat, string recordPath)
    {
        // A chat client too: the decision client resolves IChatClient from the container when it is first resolved.
        services.AddChatClient(chat);

        // The recorder, as on the recording page. Its records keep each answer's probability source.
        services.AddSingleton(_ => new JsonlDecisionRecordSink(recordPath));
        services.AddSingleton(provider => new DecisionRecorder(provider.GetRequiredService<JsonlDecisionRecordSink>()));
        services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // A reasoning model gives no logprobs, or rejects the request for them, so Verbalized skips the call Auto spends on it.
        services.AddChatDecisionClient(options => options.ProbabilityMode = ProbabilityMode.Verbalized)
            .UseRecording();
        return services;
    }
}
```
<!-- endSnippet -->

There are four overloads:

- `AddChatDecisionClient()` and `AddChatDecisionClient(configure)` register the default `IDecisionClient`.
- `AddChatDecisionClient(name)` and `AddChatDecisionClient(name, configure)` register a client keyed by `name`, over
  the chat client registered under the same key, for example with `AddKeyedChatClient`.

Each registration gets a new `ChatDecisionClientOptions` for `configure` to fill in. The decision client is created
when the container first resolves it. Resolving it throws `InvalidOperationException` when no chat client is
registered under its key.

The bridge has no standard pipeline: it does not retry, log or trace by itself. Add `UseRetries()`, `UseLogging()` and
`UseOpenTelemetry()` as you need them; [The client pipeline](pipeline.md) describes the stages.

## Ownership

The bridge does not dispose the chat client. Disposing a `ChatDecisionClient` only stops it from taking new calls,
which then throw `ObjectDisposedException`. The chat client stays usable, so it can be shared with the rest of the app,
and whoever created it, the app or the container, disposes it.

## Capabilities

The bridge checks a question set against its capabilities before it sends anything, as every provider does.

| Capability | Default |
| --- | --- |
| Question kinds | every kind |
| Most questions | none |
| Most Choice options | 26 |
| Images | no |
| Model listing | no |

- **Options are labelled `A` to `Z`**, so a Choice can have at most 26 options. Setting `MaxChoiceOptions` above 26
  throws `ArgumentException` when the client is created. A Score with more than 26 levels returns `Unsupported` with
  MIN203.
- **Images are off.** Switch them on with `Capabilities = new DecisionCapabilities { SupportsImages = true }` only when
  the chat model reads images. Each image is sent as a data part of the user message.
- **Model listing is always off.** The bridge has no model listing, so it ignores `SupportsModelListing`, and
  `GetService<DecisionCapabilities>()` reports `false` whatever you set.

`GetService<DecisionClientMetadata>()` gives the chat client's provider name, endpoint and default model, with
`ChatDecisionClientOptions.Model` in place of the model when it is set. The provider name is `unknown` when the chat
client gives none. Any other service is asked of the chat client.

## Errors

A failure comes back as a `DecisionError`, as on every provider.
[The client and its errors](client-and-errors.md#errors) describes them.

- **`Validation`:** the request has no questions, and nothing is sent. It is also a provider's HTTP 400 or 422; in
  `Auto` that comes from the verbalized call, since a rejected logprob request is asked again.
- **`Unsupported`:** the set breaks a [capability](#capabilities), and nothing is sent. In `Logprobs` mode it is also a
  reply without logprobs.
- **`InvalidResponse`:** no option label among the top logprobs in `Logprobs` mode, or a verbalized reply that is not
  usable.
- **An exception from the chat client** is returned as an error when Minos knows it. The OpenAI SDK's
  `ClientResultException` maps by its HTTP status, as the HTTP clients map it, with `RetryAfter` and `RequestId` read
  from its headers. A `ClientResultException` without a response is `Network`, or `Timeout` when its inner exception
  is a time-out. An `HttpRequestException` is `Network`, and a `TimeoutException` is `Timeout`. Any other exception
  propagates, so a bug does not pass as a provider failure.

The error's message is the bridge's own, and never quotes the state or the model's reply. The provider's exception is
in `DecisionError.Exception`, and a JSON error body in `DecisionError.Detail`.

The first question that fails fails the whole request, and the other calls are cancelled. When you cancel the call
yourself, it throws `OperationCanceledException` instead of returning an error.

## Next

- [Providers](providers.md#at-a-glance): how the chat bridge compares with the decision providers.
- [Conformance](conformance.md#the-chat-completions-wire): how the suite checks a chat-completions endpoint.
- [Recording decisions](recording.md): keep a record of every decision, each answer with its source.
- [Samples](samples.md): the chat-client sample, which runs offline with a scripted chat client.
