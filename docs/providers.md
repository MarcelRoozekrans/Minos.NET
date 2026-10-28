---
id: providers
title: Providers
sidebar_position: 6
description: The presets, servers you run and OpenAI, what each provider accepts and how they differ, images, switching providers by configuration, and running a decision server on your own machine.
---

# Providers

Minos reaches several providers through one interface, `IDecisionClient`. `SystemOneDecisionClient` reaches TypeSafe,
OpenRouter and any server that speaks the System One protocol, through a preset. `OpenAIDecisionClient` reaches
OpenAI's Decisions API. `ChatDecisionClient` asks a chat model through any Microsoft.Extensions.AI `IChatClient`.
This page lists them and what sets them apart. [The client and its errors](client-and-errors.md)
covers what every client shares: its options, retries, time-outs and errors.

`Provider` picks a preset. A preset sets the base address, the request path, where the key comes from, the default
model and the [capabilities](#capabilities). Every preset speaks the same `/v1/systemone` protocol.

OpenAI's Decisions API speaks a protocol of its own, `/v1/decisions`. Its client, `OpenAIDecisionClient`, is in a
separate package, `Minos.NET.OpenAI`, and [OpenAI](#openai) below describes it. A chat model is not a decision
model, and [Chat clients](#chat-clients) below introduces the bridge to it. [At a glance](#at-a-glance) compares the
presets, OpenAI and the chat client side by side.

## At a glance

Each column is a provider: the four `SystemOneProvider` presets, which `SystemOneDecisionClient` reaches over System
One, OpenAI, which `OpenAIDecisionClient` reaches over its Decisions API, and a chat model, which `ChatDecisionClient`
asks through an `IChatClient`. The first five rows are the [capabilities](#capabilities). Each of their cells is the
default, which you can change, and "none" means no limit is published or set.

| What | `TypeSafe` | `OpenRouter` | `VllmSemanticRouter` | `Custom` | `OpenAI` | Chat client |
| --- | --- | --- | --- | --- | --- | --- |
| Question kinds | every kind | every kind | every kind | every kind | every kind | every kind |
| Most questions | none | none | none | none | none | none |
| Most Choice options | 255 | 255 | none | none | none | 26 |
| Images | no | no | no | no | yes | no |
| Model listing | yes | no | no | no | no | no |
| Wire format | System One: `POST v1/systemone`, a `questions` object keyed by id, an `answers` object | System One, as TypeSafe | System One, at `v1/systemone` or your `Path` | System One, at `v1/systemone` or your `Path` | Decisions: `POST v1/decisions`, a `questions` array and an `answers` array, matched by `name` | Chat calls through the `IChatClient`: one call per question, answered with one label, read from its logprobs or from a stated JSON object |
| Question wire names | `noul`, `choice`, `score` | `noul`, `choice`, `score` | `noul`, `choice`, `score` | `noul`, `choice`, `score` | `predicate` for a Noul, `choice`, `score` | None: options and levels are labelled `A` to `Z`, and a Noul is `A` for yes and `B` for no |
| Noul `WhenTrue` and `WhenFalse` | Sent | Sent | Sent | Sent | MIN204, not sent | Sent, in the prompt |
| Criterion `Examples` and `NotFor` | Sent, in a criterion object | Sent, in a criterion object | Sent, in a criterion object | Sent, in a criterion object | MIN205, not sent | Sent, in the prompt |
| JSON instructions, criteria and state | Sent as JSON | Sent as JSON | Sent as JSON | Sent as JSON | Sent as the JSON's raw text, in a string | Sent as the JSON's raw text, in the prompt |
| Images on the wire | In `state`, as `image_url` parts; data URLs only | In `state`, as `image_url` parts; data URLs only | In `state`, as `image_url` parts; data URLs only | In `state`, as `image_url` parts; data URLs only | In `input`, as `input_image` parts; data URLs only | In the user message, as data parts |
| Refusals | None in the protocol | None in the protocol | None in the protocol | None in the protocol | `Refused`, one failure per refused question, rule `refusal` | None: a reply with no usable answer is `InvalidResponse` |
| Noul confidence, usage and cost | A Noul has a probability and no confidence. `usage` has the input and output tokens | A Noul has a probability and no confidence. `usage` has the tokens and `cost` in US dollars; the response has a generation `id` and the upstream `provider` | A Noul has a probability and no confidence. `usage` as the server sends it | A Noul has a probability and no confidence. `usage` as the server sends it | A Noul has a probability and no confidence. Usage is not documented: tokens and `cost` are read when present, and `Usage` is `null` only when none of the three was sent | A Noul has a probability and no confidence. A Choice's or Score's confidence is the picked option's probability, not a calibrated one. Usage sums the tokens of every call; there is no cost |
| Auth | Required: `ApiKey` or `TYPESAFE_API_KEY` | Required: `ApiKey` or `OPENROUTER_API_KEY` | Optional: `ApiKey` only | Optional: `ApiKey` only | Required: `ApiKey` or `OPENAI_API_KEY` | The chat client's own |
| Status | Tested live | Tested live, with images and Clef | WireMock tests | WireMock tests, and a local server in CI | Public beta since 2026-10-06; WireMock tests built from the guide's examples, not tested live | Unit tests, and a conformance fixture built from them; not yet tested live |

A set that breaks a capability comes back as `Unsupported` before anything is sent: MIN201 for a question kind the
provider does not take, MIN202 for more questions than `MaxQuestions`, MIN203 for more options than `MaxChoiceOptions`,
and MIN206 for images while `SupportsImages` is off. TypeSafe's Jev reads text only, and OpenRouter mixes text and image
models, so images are off for both until you switch them on; [Images](#images) says for which models. OpenRouter has its
own models API, so model listing is off for it. A `Custom` or `VllmSemanticRouter` server switches listing on with
`SupportsModelListing = true`, and `OpenAIDecisionClient` has no `ListModelsAsync`. The chat client labels options `A`
to `Z`, so it takes at most 26, and has no model listing either. Choice and Score answers carry a confidence on every
provider; the chat client's is the picked option's probability, not a calibrated one. A limit that reads "none" for
`VllmSemanticRouter`, `Custom` or OpenAI means the provider publishes none, not that it is known to be unlimited. A
limit you set in `Capabilities` applies on every provider. The capabilities you set change the kinds, the limits, the
images and, for the System One presets, the model listing the client checks. While model listing is off, a model-listing
call returns the `Unsupported` kind.

## The presets

| Provider | Base address | Path | Key | Default model |
| --- | --- | --- | --- | --- |
| `TypeSafe` | `https://api.typesafe.ai/` | `v1/systemone` | required, from `TYPESAFE_API_KEY` | `jev-latest` |
| `OpenRouter` | `https://openrouter.ai/api/` | `v1/systemone` | required, from `OPENROUTER_API_KEY` | `jev-latest` |
| `VllmSemanticRouter` | your `BaseAddress`, required | `v1/systemone`, or your `Path` | none, unless `ApiKey` is set | none |
| `Custom` | your `BaseAddress`, required | `v1/systemone`, or your `Path` | none, unless `ApiKey` is set | none |

A preset with no default model fails when the client is created, or when the host starts, until you set `Model`.

OpenRouter routes to models other than Jev, such as Cloudflare's Clef and OpenAI's Luna, by their model id.
[Provider differences](#provider-differences) shows how.

## Custom servers

`Custom` and `VllmSemanticRouter` are for a server you run, such as a vLLM Semantic Router, or any other server that
speaks `/v1/systemone`. They are the same apart from the name that telemetry reports. The server's address and the
model are yours to give.

<!-- snippet: ClientAndErrors_Custom -->
```cs
// A server you run. Custom has no default address or model, so both are set. The path is optional and defaults to
// v1/systemone. No key is read from the environment, and none is sent unless ApiKey is set.
public static SystemOneDecisionClientOptions Custom()
    => new()
    {
        Provider = SystemOneProvider.Custom,
        BaseAddress = new Uri("http://localhost:8080/"),
        Path = "api/v1/systemone",
        Model = "local-model",
    };
```
<!-- endSnippet -->

- **`BaseAddress` and `Model` are required.** Neither has a default.
- **`Path`** is relative to `BaseAddress` and defaults to `v1/systemone`. It must not start with `/`, and it must not
  contain a query, a fragment or a `..` segment. Only these two providers accept it.
- **`Custom` and `VllmSemanticRouter` never read a key from the environment.** A key in `TYPESAFE_API_KEY` or
  `OPENROUTER_API_KEY` is never sent to a server you name. Without `ApiKey` the client sends no `Authorization` header
  at all; with it, a bearer token.
- **`TYPESAFE_BASE_URL` does not apply** to them either.

## OpenAI

> **Beta.** OpenAI's Decisions API has been in public beta since 2026-10-06. `Minos.NET.OpenAI` is built from the
> examples in OpenAI's [Decisions guide](https://developers.openai.com/api/docs/guides/decisions), which documents no
> formal schema, error body, limits or usage object. It is not yet tested against the live API, and the format may change
> during the beta.

`OpenAIDecisionClient` sends a question set to `POST /v1/decisions`, the `decisions` path under its versioned base
address. It is in its own package and the `Minos.OpenAI` namespace. Like `Minos.NET.SystemOne`, the package brings
`Minos.NET` and `Minos.NET.DependencyInjection`.

```shell
dotnet add package Minos.NET.OpenAI
```

<!-- snippet: ClientAndErrors_OpenAI -->
```cs
using Minos;
using Minos.OpenAI;

public static class OpenAIClients
{
    // The key is required. Without ApiKey the client reads OPENAI_API_KEY, and without a key it throws when it is
    // created. The model defaults to gpt-6-luna and the address to https://api.openai.com/v1/.
    public static OpenAIDecisionClient Create(string apiKey)
        => new(new OpenAIDecisionClientOptions { ApiKey = apiKey });

    // The same typed call as on any IDecisionClient. The set is checked against what OpenAI can express before
    // anything is sent.
    public static async Task<string> TriageAsync(
        OpenAIDecisionClient client, string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<InboxCheck>(message, cancellationToken);
        return result.IsFailure
            ? ClientFailures.Describe(result.Error)
            : result.Value.IsUrgent.Value ? "urgent" : "can wait";
    }
}
```
<!-- endSnippet -->

`OpenAIDecisionClientOptions` has ten properties, and every one is optional.

| Property | Default | What it does |
| --- | --- | --- |
| `ApiKey` | none; the client reads `OPENAI_API_KEY` | The key sent as a bearer token. A key is required: without one the client throws `InvalidOperationException` when it is created, or the host fails at start-up. |
| `BaseAddress` | `OPENAI_BASE_URL`, then `https://api.openai.com/v1/` | The versioned API root, for a proxy or a test server. Requests go to `decisions` under it. As in OpenAI's official SDKs, the root includes `/v1`, so a proxy is set as `https://proxy.example/v1`, and `OPENAI_BASE_URL` set for another OpenAI tool works unchanged. |
| `Model` | `gpt-6-luna` | The model a request without its own asks. `gpt-6-luna` is the only model OpenAI's guide names. |
| `Capabilities` | OpenAI's, see [The defaults](#the-defaults) | What the API accepts, checked before a request is sent. |
| `Timeout`, `MaxRetries`, `InitialBackoff`, `MaxRetryDelay`, `Jitter`, `UseStandardPipeline` | as on `SystemOneDecisionClientOptions` | The same meaning, defaults and valid values as in [Options](client-and-errors.md#options). |

`OpenAIDefaults` holds the fallbacks: `ApiKeyEnvironmentVariable`, `BaseAddressEnvironmentVariable`, `Model` and
`BaseAddress`. The client has the same constructors as `SystemOneDecisionClient`, with an `HttpClient` it owns or one
you lend, and with or without an `ILoggerFactory`. `Validate()` and `OpenAIDecisionClient.ConfigureHttpClient` work as
they do there. It retries the same failures, without the `X-TypeSafe-Retry-Count` header, which is TypeSafe's. It
reports the provider as `openai` in its logs, spans and metrics. It has no raw request API and no `ListModelsAsync`.

In a host, `AddOpenAIDecisionClient` registers it, with the same six overloads as `AddSystemOneDecisionClient`. It
returns an `OpenAIDecisionClientServiceBuilder`, whose `HttpClient` property is the builder of the client's
`HttpClient`. A name holds one provider, so registering the same name with both throws `InvalidOperationException`.

<!-- snippet: DependencyInjection_OpenAI -->
```cs
// TypeSafe as the default client, and OpenAI beside it under the key "openai". Each key comes from configuration;
// without one, the OpenAI client reads OPENAI_API_KEY, and the host fails at start-up when there is none.
public static OpenAIDecisionClientServiceBuilder AddBothProviders(IHostApplicationBuilder builder)
{
    builder.Services.AddSystemOneDecisionClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    return builder.Services.AddOpenAIDecisionClient(
        "openai", options => options.ApiKey = builder.Configuration["OpenAI:ApiKey"]);
}
```
<!-- endSnippet -->

### The mapping

A question set is the same on both protocols. OpenAI's request and answer have their own shape:

| Minos | OpenAI request | OpenAI answer, read into Minos |
| --- | --- | --- |
| Noul | `{"type":"predicate","name","instructions"}` | `probability` is the Noul probability. |
| Choice | `{"type":"choice","name","instructions","choices":[{"value","description"}]}`, with the option's key as `value`. An option with no criterion has no `description`. | `choice` is the chosen key, `probabilities` the probability of each option, and `confidence` passes through. |
| Score | `{"type":"score","name","instructions","levels":[{"label","description"}]}`, lowest level first, labelled `"0"`, `"1"` and so on. | `score` is the probability-weighted mean level, `probabilities` the probability of each level, and `confidence` passes through. |
| Text state, no images | `"input":"<text>"` | none |
| JSON state, no images | `"input":"<the JSON's raw text>"` | none |
| State with images | `"input":[{"role":"user","content":[{"type":"input_text","text":…},{"type":"input_image","image_url":"data:…"},…]}]` | none |

JSON instructions and JSON criteria are sent as their raw JSON text, because OpenAI's fields are strings. An answer is
matched to its question by `name`. A missing answer, a second answer for the same question, or an answer whose `type`
does not fit the question is `InvalidResponse`. An answer for a name the set does not have is skipped. `model` and `id`
are read when present.

OpenAI's error body is assumed to be its standard `{"error":{"message":"…","type":"…","param":"…","code":"…"}}`,
because the guide documents none for this endpoint. Its `message` becomes `DecisionError.Message`, and the whole body is
in `Detail`. The kind follows the HTTP status, as for System One, so a body of another shape still maps.

### What OpenAI cannot express

Two parts of a question set have no place in OpenAI's request. The client does not drop them silently: it returns
`Unsupported` before sending, with one `QuestionFailure` per question and rule, after any capability failures. It is
not retried.

| Rule | When | What to do instead |
| --- | --- | --- |
| `MIN204` | A Noul has `WhenTrue` or `WhenFalse`. A predicate takes only instructions. | Say what a yes and a no mean in the instructions, such as "Does the sender need an answer today, rather than this week?" |
| `MIN205` | A criterion of a Choice option or a Score level has `Examples` or `NotFor`. A description is one string. | Write them into the criterion's text, such as "Payments, invoices and refunds, such as a double charge. Not pricing questions." |

A set meant for System One and OpenAI alike leaves out `WhenTrue`, `WhenFalse`, `Examples` and `NotFor`.

### Refusals

OpenAI's model can refuse a question; System One providers report no refusals. A refusal fails the whole call with
`DecisionErrorKind.Refused`, status 200, and a message such as `The model refused to answer: is_urgent.` The answers to
the other questions are discarded, because a typed set needs every answer. `Failures` has one `QuestionFailure` per
refused question, with rule `refusal` and the question's key.

<!-- snippet: ClientAndErrors_Refused -->
```cs
// A refusal fails the whole call: the answers to the other questions are discarded, because a typed set needs every
// answer. Failures names each refused question, with rule "refusal". Retrying sends the same request and gets the
// same refusal, so change the state or the questions instead.
public static IReadOnlyList<string> RefusedQuestions(DecisionError error)
{
    var keys = new List<string>();
    if (error.Kind != DecisionErrorKind.Refused)
    {
        return keys;
    }

    foreach (var failure in error.Failures)
    {
        if (failure.QuestionKey is { } key)
        {
            keys.Add(key);
        }
    }

    return keys;
}
```
<!-- endSnippet -->

`Refused` is not retried: the same request gets the same refusal.

## Chat clients

`ChatDecisionClient`, in the `Minos.NET.Extensions.AI` package, asks a chat model instead of a decision model, through
any Microsoft.Extensions.AI `IChatClient`: OpenAI, OpenRouter, Azure, Ollama or another. Each question is one chat
call, and the probabilities come from the model's logprobs where it returns them, or from an estimate the model states
as JSON. Every answer says which, in its `ProbabilitySource`. [Chat clients](chat-clients.md) describes the bridge, its
probability modes, what its confidence means, and its errors.

## Provider differences

The [matrix at the top of this page](#at-a-glance) shows what changes when you choose or switch a provider. These
notes add what a table cannot hold: what live probes found, and how OpenRouter reaches models other than Jev.

### Notes from the probes

Live probes through OpenRouter found these on 2026-10-11. Models and routing change, so read them as a dated record,
not a guarantee.

- **OpenRouter lists 19 decision models** at `GET https://openrouter.ai/api/v1/models?output_modalities=decisions`. Of
  them, `openai/gpt-6-luna-decisions`, `cloudflare/clef`, `cloudflare/clef-flash`, `cloudflare/clef-omni` and
  `perplexity/pplx-decider-v1.1-27b` list image input, and `typesafe/jev-1.13` lists text only.
- **Clef's image answers were wrong.** Asked about a solid red 128×128 PNG, `cloudflare/clef` and
  `cloudflare/clef-flash` billed the image tokens and answered blue. `perplexity/pplx-decider-v1.1-27b` answered red,
  with 0.9994. On text, `cloudflare/clef-flash` answered a three-question request with HTTP 200, `usage.cost`, an `id`
  and a `provider`.
- **`typesafe/jev-1.13` accepted an image silently.** It is a text-only model, and it took a request with an image
  without an error. This is why images are off for the OpenRouter preset until you switch them on for a model that
  reads them.
- **Zero data retention blocks Luna and clef-omni.** On an OpenRouter account that allows only zero-data-retention
  endpoints, `openai/gpt-6-luna-decisions` and `cloudflare/clef-omni` return HTTP 404 with
  `zdr-violation-by-account`, which Minos reports as `Http`. The setting is at
  [openrouter.ai/settings/privacy](https://openrouter.ai/settings/privacy).

### Clef and Luna through OpenRouter

OpenRouter serves Cloudflare's Clef and OpenAI's Luna in the System One format, so the OpenRouter preset reaches them:
set `Model` to the model's OpenRouter id. The client then speaks System One, and the OpenRouter column above applies,
not the OpenAI one: `WhenTrue`, `Examples` and JSON are sent, and images stay off until you switch them on. The live
tests run `cloudflare/clef-flash` on text. A Luna test runs only when `MINOS_LIVE_LUNA=1` is set, because the
maintainer's account allows only zero-data-retention endpoints.

<!-- snippet: ClientAndErrors_OpenRouterModels -->
```cs
// OpenRouter serves Clef and Luna in the System One format, so the OpenRouter preset reaches them by model id.
// The request is System One: the OpenRouter column of the provider matrix applies, not the OpenAI one.
public static SystemOneDecisionClientOptions Clef()
    => new() { Provider = SystemOneProvider.OpenRouter, Model = "cloudflare/clef-flash" };

// Luna through OpenRouter needs an account that allows endpoints which retain data; see the notes above.
public static SystemOneDecisionClientOptions Luna()
    => new() { Provider = SystemOneProvider.OpenRouter, Model = "openai/gpt-6-luna-decisions" };
```
<!-- endSnippet -->

### Privacy: zero data retention

By default, which OpenRouter endpoints may serve your requests is set by your account, at
[openrouter.ai/settings/privacy](https://openrouter.ai/settings/privacy). `ProviderPreferences` lets the client require it
on every request instead, so a misconfigured account or key cannot send your text to a provider that keeps it.

- `ZeroDataRetention` sends `"zdr": true`: only zero-data-retention endpoints may serve the request.
- `DenyDataCollection` sends `"data_collection": "deny"`: providers that collect user data are refused.

Both go in OpenRouter's `provider` object, on every call: typed, built sets and raw `SystemOneRequest` calls alike.

<!-- snippet: ClientAndErrors_OpenRouterPrivacy -->
```cs
// Every request asks OpenRouter for zero-data-retention endpoints. When none can serve it, the call fails instead of
// going to a provider that keeps the text.
public static SystemOneDecisionClientOptions ZeroDataRetention()
    => new()
    {
        Provider = SystemOneProvider.OpenRouter,
        ProviderPreferences = new OpenRouterProviderPreferences { ZeroDataRetention = true },
    };
```
<!-- endSnippet -->

From configuration:

```json
{
  "Minos": {
    "Provider": "OpenRouter",
    "ProviderPreferences": { "ZeroDataRetention": true }
  }
}
```

- **OpenRouter only.** With any other `Provider`, a preference that is on makes the client throw
  `ArgumentException` when it is built, and makes options validation fail at startup. A guarantee that would silently do
  nothing is refused.
- **Your account setting still applies.** The stricter of the two wins.
- **When no endpoint qualifies**, OpenRouter refuses the request. You get a `DecisionError` with OpenRouter's status code
  and its body in `Detail`, as for the `zdr-violation-by-account` refusals in the notes above, and the call is not
  retried.
- **What the live tests verified**, on 2026-10-16 in
  [live run 38857122239](https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/38857122239):
  - OpenRouter's `/v1/systemone` reads the `provider` object. The same request was answered without it, and refused
    with HTTP 404 when its `provider` object allowed no endpoint. OpenRouter named the routing step that refused it.
  - Jev, served as `typesafe/jev-1.14-20261014`, answered with `ZeroDataRetention`, on a raw call and a typed call.
  - Jev answered with `DenyDataCollection` too.
- **What they could not show:** the test account already allows only zero-data-retention endpoints. A `zdr: true`
  request therefore succeeds whether or not OpenRouter enforces that one flag. The proof is that the endpoint reads the
  object; `zdr` is one of its documented fields.

## Capabilities

A provider can accept less than the protocol allows. A server you run, for example, may answer one kind of question
only. Minos checks a question set against what the provider accepts before it sends the request. A set the provider
would refuse comes back as a failure at once, with no request, no retry and no cost.

### The defaults

The first five rows of the [matrix](#at-a-glance) are each provider's defaults for the five capabilities. "None"
means no limit is published or set.

`SupportsImages` says whether the provider takes [images](#images). A request with images to a provider whose `SupportsImages` is off
returns `Unsupported` with `MIN206`, and nothing is sent.

OpenAI's `SupportsModelListing` is always off: `OpenAIDecisionClient` has no model listing, so it ignores
`Capabilities.SupportsModelListing`, and `GetService<DecisionCapabilities>()` reports `false` whatever you set.

### Overriding them

`SystemOneDecisionClientOptions.Capabilities` and `OpenAIDecisionClientOptions.Capabilities` are a
`DecisionCapabilities`, with one nullable property for each capability. A property you set wins, and one you leave
`null` keeps the default. This server answers Noul questions only:

<!-- snippet: ClientAndErrors_Capabilities -->
```cs
// A server that only answers Noul questions. Each property left unset keeps the Custom preset's default, so
// this changes the accepted kinds and nothing else.
public static SystemOneDecisionClientOptions NoulOnly()
    => new()
    {
        Provider = SystemOneProvider.Custom,
        BaseAddress = new Uri("http://localhost:8080/"),
        Model = "noul-small",
        Capabilities = new DecisionCapabilities { QuestionKinds = QuestionKinds.Noul },
    };
```
<!-- endSnippet -->

The `QuestionKinds` property is a flags enum of the same name, with `Noul`, `Choice` and `Score`, and `All` for the three.
In configuration it binds from a comma-separated list, such as `"Noul, Choice"`, as
[dependency injection](dependency-injection.md#options-from-configuration) shows. An override must make sense: at least
one question kind, no flag that does not exist, `MaxQuestions` of at least 1, and `MaxChoiceOptions` of at least 2.
Otherwise the options fail when the client is created or the host starts, like any invalid option.

### The Unsupported error

A typed call, or a call with a [question set built at run time](question-sets-at-run-time.md), checks the set before it
is sent. A set that breaks a capability returns a `DecisionError` of kind `Unsupported`, and `Failures` lists every
problem it found. Each is a `QuestionFailure` with a rule id, the key of the question and a message:

| Rule | Failure | `QuestionKey` | Message, for `Custom` or `OpenAI` |
| --- | --- | --- | --- |
| `MIN201` | The set has a question of a kind the provider does not accept. | the question | `Custom does not support Choice questions.` |
| `MIN202` | The set has more questions than `MaxQuestions`. | none | `Custom accepts at most 1 question per request; the set has 2.` |
| `MIN203` | A Choice question has more options than `MaxChoiceOptions`. | the question | `Custom accepts at most 2 options per Choice question; 'team' has 3.` |
| `MIN204` | A Noul has `WhenTrue` or `WhenFalse`, on OpenAI. | the question | `OpenAI does not support WhenTrue or WhenFalse on a Noul; put the meaning in the instructions.` |
| `MIN205` | A criterion has `Examples` or `NotFor`, on OpenAI. | the question | `OpenAI does not support Examples or NotFor on a criterion; describe them in its text.` |
| `MIN206` | The request has images and `SupportsImages` is off. | none | `Custom does not support images.` |

The error's `Message` is the first failure's. `MIN202` comes first, then each question's `MIN201` and `MIN203` in the
order it is sent, then `MIN206`, then OpenAI's `MIN204` and `MIN205` in question order. The MIN2 series is for these
provider rules, and MIN201 to MIN203 keep their meaning, so you can match on them. The rules
MIN001 to MIN006 are the API's own rules, MIN101 to MIN108 are about what the generator can turn into code, and
[diagnostics](diagnostics.md) lists them.

<!-- snippet: ClientAndErrors_Unsupported -->
```cs
// An Unsupported error from a capability check lists every problem in Failures: the rule id, the question it
// is about and a message. The set was not sent.
public static IReadOnlyList<string> Problems(DecisionError error)
{
    var problems = new List<string>();
    if (error.Kind != DecisionErrorKind.Unsupported)
    {
        return problems;
    }

    foreach (var failure in error.Failures)
    {
        problems.Add($"{failure.Rule} {failure.QuestionKey}: {failure.Message}");
    }

    return problems;
}
```
<!-- endSnippet -->

`Unsupported` is not retried, and no request was sent. The raw `EvaluateAsync(SystemOneRequest)` call is not checked: it
sends what you built, and the provider's own answer comes back.

### Reading the resolved capabilities

`GetService<DecisionCapabilities>()` returns what a client resolved, with every property set: the preset's or OpenAI's
defaults and your overrides together. Both clients answer it, and a client that reports none, such as a fake, returns
`null`. It is a new instance each time, so changing it changes nothing.

<!-- snippet: ClientAndErrors_ResolvedCapabilities -->
```cs
// The capabilities a client resolved: the preset's or OpenAI's defaults with your overrides applied, every property
// set.
// GetService returns a new instance each time, so changing it changes nothing in the client.
public static string Describe(IDecisionClient client)
{
    var capabilities = client.GetService<DecisionCapabilities>();
    if (capabilities is null)
    {
        return "This client does not report its capabilities.";
    }

    var questions = capabilities.MaxQuestions is { } most ? $"at most {most} questions" : "no question limit";
    var images = capabilities.SupportsImages == true ? "on" : "off";
    var listing = capabilities.SupportsModelListing == true ? "on" : "off";
    return $"{capabilities.QuestionKinds}, {questions}, images {images}, model listing {listing}";
}
```
<!-- endSnippet -->

### What is not a capability

The size of the state is not checked. A model has a context size, and Minos has no capability for it: a state that is
too long for the server is the server's to refuse, and its refusal comes back as a `DecisionError` like any other.

## Images

A decision can be about images as well as text: the photo in a return request, the screenshot in a bug report. Images
go beside the state, not inside it, so a text state, a JSON state and a typed `TState` all carry them the same way. The
question set does not change.

<!-- snippet: ClientAndErrors_ImageQuestions -->
```cs
// An ordinary question set. The images go with the state at call time, so the same set works with or without them.
[Questions]
public partial record ReturnPhotoCheck
{
    [Noul("Does the product in the photo look damaged?")]
    public partial Noul IsDamaged { get; }
}
```
<!-- endSnippet -->

<!-- snippet: ClientAndErrors_Images -->
```cs
// Each image is a DecisionImage: bytes with their media type, or a data URL. PNG, JPEG, WebP and GIF are accepted,
// and anything else throws ArgumentException. FromBytes copies the bytes, so the buffer can be reused at once.
public static async Task<string> CheckReturnAsync(
    IDecisionClient client, ReadOnlyMemory<byte> photo, string screenshotDataUrl, CancellationToken cancellationToken)
{
    IReadOnlyList<DecisionImage> images =
    [
        DecisionImage.FromBytes(photo, "image/jpeg"),
        DecisionImage.FromDataUrl(screenshotDataUrl),
    ];

    // The overload with images takes them after the state. A provider without image support returns Unsupported
    // with MIN206, and nothing is sent.
    var result = await client.EvaluateAsync<ReturnPhotoCheck>(
        "A return request for a kettle.", images, cancellationToken);
    return result.IsFailure
        ? ClientFailures.Describe(result.Error)
        : result.Value.IsDamaged.Value ? "damaged" : "not damaged";
}
```
<!-- endSnippet -->

- **`DecisionImage` is one image:** its bytes and its media type. `FromBytes(data, mediaType)` copies the bytes.
  `FromDataUrl(url)` takes a `data:<type>;base64,<data>` URL and decodes it once to check it. Both accept
  `image/png`, `image/jpeg`, `image/webp` and `image/gif`, and throw `ArgumentException` for another type, for empty
  data or for data that is not base64.
- **Data URLs only.** Both protocols take images inline, as base64 data URLs, so there is no way to pass a remote URL
  or a file id.
- **Every evaluate overload has a twin with images.** It takes an `IReadOnlyList<DecisionImage>` after the state: for a
  text, JSON, UTF-8 JSON or `TState` state, and for a [question set built at run time](question-sets-at-run-time.md).
  A `DecisionRequest` you build yourself takes them in its `Images` property. The raw
  `EvaluateAsync(SystemOneRequest)` call takes none.
- **Size and count are not checked.** No provider publishes limits that hold reliably. A server that refuses an image
  returns its refusal as a `DecisionError`, like any other.

### Which providers take images

OpenAI takes images by default. Every System One preset has `SupportsImages` off: TypeSafe's Jev reads text only, and a server
of your own may not read images. A request with images to a provider whose `SupportsImages` is off returns `Unsupported` with
`MIN206` before anything is sent, and it is not retried.

OpenRouter routes to text and image models alike, so images are off for the preset as well. Switch them on for a model
that reads images:

<!-- snippet: ClientAndErrors_OpenRouterImages -->
```cs
// OpenRouter mixes text and image models, so images are off for the preset. Switch them on for a model that reads
// images. Perplexity's decider read our test image correctly on 2026-10-11.
public static SystemOneDecisionClientOptions PerplexityWithImages()
    => new()
    {
        Provider = SystemOneProvider.OpenRouter,
        Model = "perplexity/pplx-decider-v1.1-27b",
        Capabilities = new DecisionCapabilities { SupportsImages = true },
    };
```
<!-- endSnippet -->

As of 2026-10-11, `perplexity/pplx-decider-v1.1-27b` read our test image correctly. `cloudflare/clef` and
`cloudflare/clef-flash` take images and answered wrongly, and `typesafe/jev-1.13` is text-only and ignores an image
without an error. [Notes from the probes](#notes-from-the-probes) has the details. A `Custom` or `VllmSemanticRouter`
server that reads images is switched on the same way, with `Capabilities.SupportsImages = true`.

### On the wire

Without images, both protocols send exactly what they send for text alone. With images:

- **System One** sends `state` as an array: first `{"type":"text","text":…}`, holding the text or the JSON state's raw
  text, then one `{"type":"image_url","image_url":{"url":"data:…"}}` per image, in order.
- **OpenAI** sends `input` as one user message: an `input_text` part with the state, then one `input_image` part per
  image, as [the mapping](#the-mapping) shows.

## Switching providers

An app that names its provider in configuration switches with no code change. The
[provider-switch sample](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.ProviderSwitch)
keeps one section per provider under `Minos:Providers`, and `Minos:Active` names the one to use. This is its
`appsettings.json`:

```json
{
  "Minos": {
    "Active": "TypeSafe",
    "Providers": {
      "TypeSafe": { "Provider": "TypeSafe", "MaxRetries": 2 },
      "OpenRouter": { "Provider": "OpenRouter", "MaxRetries": 2 },
      "Local": {
        "Provider": "Custom",
        "BaseAddress": "http://localhost:8000/",
        "Model": "Mapika/decider-2b-GGUF:Q4_K_M",
        "MaxRetries": 2
      }
    }
  }
}
```

No key is in the file. TypeSafe's comes from `TYPESAFE_API_KEY` and OpenRouter's from `OPENROUTER_API_KEY`, as for any
preset, and the local server needs none. The sections differ only in their provider settings, `Provider`,
`BaseAddress` and `Model`; everything else, such as `MaxRetries`, is the same in each.

A program can bind the active section and nothing else. This is a simpler version of what the sample does; the
sample's own code also lists the configured providers:

<!-- snippet: Providers_ActiveProvider -->
```cs
// Minos:Active names one section under Minos:Providers, and the client is bound from that section alone. No other
// code depends on the provider.
public static IConfigurationSection ActiveSection(IConfiguration configuration)
{
    var active = configuration["Minos:Active"];
    if (string.IsNullOrWhiteSpace(active))
    {
        throw new InvalidOperationException("Set Minos:Active to the provider to use.");
    }

    var section = configuration.GetSection("Minos:Providers").GetSection(active);
    return section.Exists()
        ? section
        : throw new InvalidOperationException($"Minos:Providers has no section named {active}.");
}

public static SystemOneDecisionClientServiceBuilder AddActiveProvider(IHostApplicationBuilder builder)
    => builder.Services.AddSystemOneDecisionClient(ActiveSection(builder.Configuration));
```
<!-- endSnippet -->

`Minos:Active` is set in the file. The environment variable `Minos__Active` overrides it, as it overrides any setting
in a .NET host, and so does the sample's `--provider` argument:

```shell
dotnet run --project samples/Minos.NET.Samples.ProviderSwitch -- --provider local
```

Each provider's answers are recorded in a file of its own: `recordings/typesafe.json`, `recordings/openrouter.json` and
`recordings/local.json`. TypeSafe and OpenRouter receive byte-identical requests, because both ask `jev-latest`, so one
file could not hold both.

The sample's recorded answers, side by side, each ticket's team and confidence:

| Ticket | TypeSafe | OpenRouter | Local |
| --- | --- | --- | --- |
| s01 | billing 1.00 | billing 1.00 | billing 0.99 |
| s02 | technical 1.00 | technical 1.00 | technical 0.97 |
| s03 | account 1.00 | account 1.00 | account 0.98 |
| s04 | sales 1.00 | sales 1.00 | sales 0.95 |
| s05 | technical 1.00 | technical 1.00 | technical 0.97 |
| s06 | account 1.00 | account 1.00 | account 0.94 |
| s07 | sales 1.00 | sales 1.00 | sales 0.80 |
| s08 | billing 1.00 | billing 1.00 | billing 0.94 |
| Model | `jev-1.13.0` | `typesafe/jev-1.13-20260917` | `Mapika/decider-2b-GGUF:Q4_K_M` |

All three providers send every ticket to the same team. They differ in how sure they are. TypeSafe and OpenRouter ask
the same model and answer with a confidence of 1.00 for every ticket. The small local model is less sure, from 0.94 to
0.99 on most tickets and 0.80 on s07, so a confidence gate tuned on one provider needs a second look before another
one takes its place.

To use several providers at once in one app, register each under a key, as
[several clients](dependency-injection.md#several-clients) shows.

## Running a local server

`Custom` reaches any server that speaks `/v1/systemone`, and a small one runs on an ordinary machine. The Local
systemone workflow runs ollajev 0.4.0 with the decision model `Mapika/decider-2b-GGUF:Q4_K_M` on a CI runner with no
GPU, with these commands:

```shell
pip install --extra-index-url https://download.pytorch.org/whl/cpu ollajev==0.4.0
ollajev pull Mapika/decider-2b-GGUF:Q4_K_M
OLLAJEV_DEVICE=cpu ollajev serve Mapika/decider-2b-GGUF:Q4_K_M --port 8000 --no-browser
```

The model is a 1.27 GB GGUF file under the Apache-2.0 licence, run by llama.cpp, so no code from its repository runs.
Never pass `--trust`, which lets a model run code of its own. The server is ready when
`GET http://127.0.0.1:8000/v1/models` answers. On the runner that took about 8 seconds, most of it loading the model.

The `Local` section above points the `Custom` preset at it: `Provider` is `Custom`, `BaseAddress` is
`http://localhost:8000/` and `Model` is `Mapika/decider-2b-GGUF:Q4_K_M`. No key is sent. The server answers all three
question kinds, and reports probabilities to four decimals.

## Adding OpenAI

OpenAI's client is a type of its own, `OpenAIDecisionClient`, so its section is bound with `AddOpenAIDecisionClient`
instead of `AddSystemOneDecisionClient`. Add a section for it:

```json
{
  "Minos": {
    "Active": "OpenAI",
    "Providers": {
      "OpenAI": { "Model": "gpt-6-luna", "MaxRetries": 2 }
    }
  }
}
```

Then pick the registration by the section's name:

<!-- snippet: Providers_AddOpenAI -->
```cs
// OpenAI has a client type of its own, so its section is bound with AddOpenAIDecisionClient. The code that asks for
// IDecisionClient does not change. Without an ApiKey in the section, the client reads OPENAI_API_KEY.
public static void AddActiveClient(IHostApplicationBuilder builder)
{
    var section = ActiveSection(builder.Configuration);
    if (string.Equals(section.Key, "OpenAI", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddOpenAIDecisionClient(section);
    }
    else
    {
        builder.Services.AddSystemOneDecisionClient(section);
    }
}
```
<!-- endSnippet -->

The code that asks for an `IDecisionClient` does not change. The key comes from `OPENAI_API_KEY`, and
[OpenAI](#openai) above lists what OpenAI cannot express. The provider-switch sample has no OpenAI section, because
there is no OpenAI key to record its answers with yet;
[issue #161](https://github.com/MarcelRoozekrans/Minos.NET/issues/161) tracks that.

## Next

- [Chat clients](chat-clients.md): ask any Microsoft.Extensions.AI chat client, and what its probabilities mean.
- [Choosing a provider and a threshold](choosing-a-provider.md): how the providers compare on a labelled sample, and
  how to pick a threshold.
- [The client and its errors](client-and-errors.md): the options every client shares, retries, time-outs and errors.
- [Dependency injection](dependency-injection.md#several-clients): register several providers side by side, each
  under a key.
- [Samples](samples.md): runnable programs, one of them against three providers by configuration only.
