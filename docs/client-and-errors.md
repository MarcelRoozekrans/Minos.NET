---
id: client-and-errors
title: The client and its errors
sidebar_position: 5
description: Create and configure a SystemOneClient or an OpenAIDecisionClient, compare the providers, send images, understand retries and time-outs, handle every kind of DecisionError, and use the raw request API.
---

# The client and its errors

Every page so far has called `EvaluateAsync` on an `IDecisionClient`. This page is about that client: how to create one, what
its options do, which providers it reaches and how they differ, what happens when a call fails, and how to read the
failure. It also covers OpenAI's client, images, the raw request API and listing models.

The failure side matters because a call to a remote model fails in ordinary ways. The network drops, the key is wrong,
the service is busy. Minos reports every such failure as a value, a `DecisionError`, so a failure of the call itself
arrives as a value to check, not as an exception to catch.

## Creating a client

`SystemOneClient` is the class that implements `IDecisionClient`. It is thread-safe and meant to live for the whole
program: create one, share it, and dispose it when the program stops. Creating a client per call wastes connections.

<!-- snippet: ClientAndErrors_Constructors -->
```cs
using Microsoft.Extensions.Logging;
using Minos;
using Minos.SystemOne;

public static class ClientConstructors
{
    // The client creates its own HttpClient, and disposing the client disposes it.
    public static SystemOneClient Owned(string apiKey)
        => new(new SystemOneClientOptions { ApiKey = apiKey });

    // You pass an HttpClient you manage, for example one from IHttpClientFactory. The client never disposes it.
    public static SystemOneClient Borrowed(HttpClient http, string apiKey)
        => new(http, new SystemOneClientOptions { ApiKey = apiKey });

    // Either form also takes an ILoggerFactory, and then logs each operation and each retried attempt.
    public static SystemOneClient Logged(string apiKey, ILoggerFactory loggers)
        => new(new SystemOneClientOptions { ApiKey = apiKey }, loggers);
}
```
<!-- endSnippet -->

There are two families of constructor.

- **The client owns its `HttpClient`.** `new SystemOneClient(options)`, or `new SystemOneClient()` for all defaults. The
  client creates the `HttpClient`, applies the options to it, and disposes it in `Dispose`.
- **You lend an `HttpClient`.** `new SystemOneClient(http, options)`. The client uses it and never disposes it, so
  whoever created it still owns it. This is how a client over `IHttpClientFactory` works, and it is what a test does to
  put a fake handler under the client. Two things follow. The `HttpClient`'s own `BaseAddress` wins over
  `SystemOneClientOptions.BaseAddress` when it is set, and it must end in `/`. And its own `Timeout` applies, not
  `SystemOneClientOptions.Timeout`, unless you set it up with `SystemOneClient.ConfigureHttpClient`, which the
  [dependency injection](dependency-injection.md#using-minos-without-the-package) page shows.

Both families take a `null` options object to mean "all defaults and environment variables", and both have an overload
that also takes an `ILoggerFactory`. With one, the client logs each operation and each retried attempt, as [Logging,
traces and metrics](observability.md#logging) describes. What it logs never contains the state, the questions, the
answers, the API key or a header value.

Two calls with bare `null` literals do not compile (CS0121), because more than one constructor accepts them.
`new SystemOneClient(null)` matches both `(SystemOneClientOptions?)` and `(HttpClient)`.
`new SystemOneClient(null, null)` matches both `(SystemOneClientOptions?, ILoggerFactory?)` and
`(HttpClient, SystemOneClientOptions?)`; before the logging overloads it compiled and always threw
`ArgumentNullException` for the missing `HttpClient`. For all defaults and environment variables, write
`new SystemOneClient()`. Any other call resolves once its `null` has a type, for example
`new SystemOneClient((SystemOneClientOptions?)null)`.

A client throws instead of returning a failure only for mistakes in the calling code. A missing API key, an invalid
option or a `null` request throws when you create the client or make the call. Calling a disposed client throws
`ObjectDisposedException`. Cancelling the `CancellationToken` you passed throws `OperationCanceledException`, because
you asked for it. Everything else, including every network and service failure, comes back as a `DecisionError`.

### Disposing a client

`Dispose` decides what happens to a call by when the call started:

- **A call started after `Dispose`** throws `ObjectDisposedException`, because calling a disposed client is a mistake in
  the calling code.
- **A call already in flight** over an `HttpClient` the client created is torn down with that `HttpClient`. It returns
  a `DecisionError` of kind `Disposed`, not `Timeout` or `Network`, so you can tell it apart from a real failure. A real
  time-out that was mapped before `Dispose` set its flag keeps `Timeout` when no retry is left.
- **A call already in flight over an `HttpClient` you lent** is not torn down, because the client never disposes that
  `HttpClient`. The attempt in flight keeps its own result.
- **A disposed client never retries**, whichever kind of `HttpClient` it uses. A retry that would start after `Dispose`
  is not sent, and the call returns `Disposed`.

## Options

`SystemOneClientOptions` has twelve properties, and every one is optional.

| Property | Default | Valid values | What it does |
| --- | --- | --- | --- |
| `Provider` | `SystemOneProvider.TypeSafe` | `TypeSafe`, `OpenRouter`, `VllmSemanticRouter` or `Custom` | Where requests go. The [providers](#providers) section lists what each one sets. |
| `ApiKey` | none | any text without control characters; blank counts as unset | The key sent as a bearer token. When unset, the client reads `TYPESAFE_API_KEY` for TypeSafe and `OPENROUTER_API_KEY` for OpenRouter. `VllmSemanticRouter` and `Custom` read no variable and send no key unless this is set. |
| `BaseAddress` | the provider's address | absolute `http` or `https` URI, no query or fragment | The API root, for a proxy, a test server or a server you run. When unset, the client reads `TYPESAFE_BASE_URL` for TypeSafe only, then uses the provider's address. `VllmSemanticRouter` and `Custom` have none, so it is required for them. |
| `Model` | the preset's default: `jev-latest` for TypeSafe and OpenRouter; required for the others | not blank | The model that typed evaluation and built question sets ask: an alias such as `jev-latest` (or TypeSafe's `jev-preview`), or a versioned id such as `jev-1.13.0`. Surrounding whitespace is trimmed. See [Listing models](#listing-models). |
| `Path` | `v1/systemone` | relative, no leading `/`, no query, fragment or `..`; `Custom` and `VllmSemanticRouter` only | The request path under `BaseAddress`. Surrounding whitespace is trimmed, and blank counts as unset. Rejected for a hosted provider, so a key cannot be sent to another path on a host you did not choose. |
| `Capabilities` | the preset's | see [Capabilities](#capabilities) | What the provider accepts, checked before a request is sent. Each property you leave unset keeps the preset's value. |
| `Timeout` | 60 seconds | positive, or `Timeout.InfiniteTimeSpan`, and at most about 24.8 days | How long one attempt may take. |
| `MaxRetries` | 2 | 0 to 10 | How many times a failed call is tried again. 0 turns retries off. |
| `InitialBackoff` | 500 ms | positive, and at most about 24.8 days | The first wait between attempts. It doubles for each further retry. |
| `MaxRetryDelay` | 30 seconds | at least `InitialBackoff`, and at most about 24.8 days | The longest wait between attempts, for the backoff and for a server's `Retry-After` alike. |
| `Jitter` | `true` | `true` or `false` | Adds a random extra of up to 50 percent to each backoff wait, so many clients do not retry in step. |
| `UseStandardPipeline` | `true` | `true` or `false` | Whether a `DecisionRequest` call runs through the standard [pipeline](pipeline.md) of telemetry, logging and retries. `false` leaves one attempt per call, for a pipeline of your own. |

Each option you set beats the environment. The order is: the option, then the environment variable, then the provider's
default. A blank API key counts as unset. `TYPESAFE_BASE_URL` applies to TypeSafe only, so an OpenRouter
key is never sent to a TypeSafe proxy.

The first snippet spells out the options that have a fixed default, with that value. You rarely write all of them. It
is here so the table above has code to match.

<!-- snippet: ClientAndErrors_Options -->
```cs
// Every option set to the value it has when you leave it out, apart from the API key, which has no default:
// without one, the client reads TYPESAFE_API_KEY, or OPENROUTER_API_KEY for OpenRouter. Custom and
// VllmSemanticRouter read no key from the environment and send none unless ApiKey is set.
// BaseAddress, Model, Path and Capabilities are also left out: the provider decides them, and the model is
// jev-latest for TypeSafe.
public static SystemOneClientOptions SpelledOut(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        Provider = SystemOneProvider.TypeSafe,
        Timeout = TimeSpan.FromSeconds(60),
        MaxRetries = 2,
        InitialBackoff = TimeSpan.FromMilliseconds(500),
        MaxRetryDelay = TimeSpan.FromSeconds(30),
        Jitter = true,
        UseStandardPipeline = true,
    };
```
<!-- endSnippet -->

A `SystemOneRequest` names its own model, so `Model` applies to typed evaluation and to question sets only.

### SystemOneDefaults

`SystemOneDefaults` is the static class that holds the values the client falls back on. Use it instead of typing the
same text yourself, for example to send `SystemOneDefaults.Model` explicitly or to name an environment variable in a
message.

| Member | Value | What it is |
| --- | --- | --- |
| `ApiKeyEnvironmentVariable` | `TYPESAFE_API_KEY` | The variable a TypeSafe key is read from. |
| `OpenRouterApiKeyEnvironmentVariable` | `OPENROUTER_API_KEY` | The variable an OpenRouter key is read from. |
| `BaseAddressEnvironmentVariable` | `TYPESAFE_BASE_URL` | The variable that overrides the base address, for TypeSafe only. |
| `Model` | `jev-latest` | The model alias used when a request does not name one. A property, so a changed default is not compiled into your code. |
| `TypeSafeBaseAddress` | `https://api.typesafe.ai/` | The root address of TypeSafe's API. A property. |
| `OpenRouterBaseAddress` | `https://openrouter.ai/api/` | The root address of OpenRouter's System One API. A property. |

### Checking options early

The constructors check the options, and so does `Validate()`. It runs exactly the check a constructor runs, including
the API key and base address environment variables, but creates no client. Use it in a health check, or to fail fast on
start-up.

<!-- snippet: ClientAndErrors_Validate -->
```cs
// Validate runs the check the constructors run, without creating a client. It throws ArgumentException for a value
// that is out of range, and InvalidOperationException when no API key can be found.
public static string? Problem(SystemOneClientOptions options)
{
    try
    {
        options.Validate();
        return null;
    }
    catch (ArgumentException exception)
    {
        return exception.Message;
    }
    catch (InvalidOperationException exception)
    {
        return exception.Message;
    }
}
```
<!-- endSnippet -->

An invalid option throws `ArgumentException`. A missing API key, or an invalid key or base address environment variable,
throws `InvalidOperationException`. The message says which option or variable is wrong. If a value could pass here and
fail in the constructor, it can only be because an environment variable changed in between.

## Providers

`Provider` picks a preset. A preset sets the base address, the request path, where the key comes from, the default
model and the [capabilities](#capabilities). Every preset speaks the same `/v1/systemone` protocol.

OpenAI's Decisions API speaks a protocol of its own, `/v1/decisions`. Its client, `OpenAIDecisionClient`, is in a
separate package, `Minos.NET.OpenAI`, and [OpenAI](#openai) below describes it. [Provider
differences](#provider-differences) compares the presets and OpenAI side by side.

### The presets

| Provider | Base address | Path | Key | Default model |
| --- | --- | --- | --- | --- |
| `TypeSafe` | `https://api.typesafe.ai/` | `v1/systemone` | required, from `TYPESAFE_API_KEY` | `jev-latest` |
| `OpenRouter` | `https://openrouter.ai/api/` | `v1/systemone` | required, from `OPENROUTER_API_KEY` | `jev-latest` |
| `VllmSemanticRouter` | your `BaseAddress`, required | `v1/systemone`, or your `Path` | none, unless `ApiKey` is set | none |
| `Custom` | your `BaseAddress`, required | `v1/systemone`, or your `Path` | none, unless `ApiKey` is set | none |

A preset with no default model fails when the client is created, or when the host starts, until you set `Model`. A
Providers page is planned. Until it exists, the tables on this page are the reference.

OpenRouter routes to models other than Jev, such as Cloudflare's Clef and OpenAI's Luna, by their model id.
[Provider differences](#provider-differences) shows how.

### Custom servers

`Custom` and `VllmSemanticRouter` are for a server you run, such as a vLLM Semantic Router, or any other server that
speaks `/v1/systemone`. They are the same apart from the name that telemetry reports. The server's address and the
model are yours to give.

<!-- snippet: ClientAndErrors_Custom -->
```cs
// A server you run. Custom has no default address or model, so both are set. The path is optional and defaults to
// v1/systemone. No key is read from the environment, and none is sent unless ApiKey is set.
public static SystemOneClientOptions Custom()
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

### OpenAI

> **Beta.** OpenAI's Decisions API has been in public beta since 2026-10-06. `Minos.NET.OpenAI` is built from the
> examples in OpenAI's [Decisions guide](https://developers.openai.com/api/docs/guides/decisions), which documents no
> formal schema, error body, limits or usage object. It has not yet run against the live API, and the format may change
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
| `Timeout`, `MaxRetries`, `InitialBackoff`, `MaxRetryDelay`, `Jitter`, `UseStandardPipeline` | as on `SystemOneClientOptions` | The same meaning, defaults and valid values as in [Options](#options). |

`OpenAIDefaults` holds the fallbacks: `ApiKeyEnvironmentVariable`, `BaseAddressEnvironmentVariable`, `Model` and
`BaseAddress`. The client has the same constructors as `SystemOneClient`, with an `HttpClient` it owns or one you lend,
and with or without an `ILoggerFactory`. `Validate()` and `OpenAIDecisionClient.ConfigureHttpClient` work as they do
there. It retries the same failures, without the `X-TypeSafe-Retry-Count` header, which is TypeSafe's. It reports the
provider as `openai` in its logs, spans and metrics. It has no raw request API and no `ListModelsAsync`.

In a host, `AddOpenAIDecisionClient` registers it, with the same six overloads as `AddSystemOneClient`. It returns an
`OpenAIDecisionClientServiceBuilder`, whose `HttpClient` property is the builder of the client's `HttpClient`. A name
holds one provider, so registering the same name with both throws `InvalidOperationException`.

<!-- snippet: DependencyInjection_OpenAI -->
```cs
// TypeSafe as the default client, and OpenAI beside it under the key "openai". Each key comes from configuration;
// without one, the OpenAI client reads OPENAI_API_KEY, and the host fails at start-up when there is none.
public static OpenAIDecisionClientServiceBuilder AddBothProviders(IHostApplicationBuilder builder)
{
    builder.Services.AddSystemOneClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    return builder.Services.AddOpenAIDecisionClient(
        "openai", options => options.ApiKey = builder.Configuration["OpenAI:ApiKey"]);
}
```
<!-- endSnippet -->

#### The mapping

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

#### What OpenAI cannot express

Two parts of a question set have no place in OpenAI's request. The client does not drop them silently: it returns
`Unsupported` before sending, with one `QuestionFailure` per question and rule, after any capability failures. It is
not retried.

| Rule | When | What to do instead |
| --- | --- | --- |
| `MIN204` | A Noul has `WhenTrue` or `WhenFalse`. A predicate takes only instructions. | Say what a yes and a no mean in the instructions, such as "Does the sender need an answer today, rather than this week?" |
| `MIN205` | A criterion of a Choice option or a Score level has `Examples` or `NotFor`. A description is one string. | Write them into the criterion's text, such as "Payments, invoices and refunds, such as a double charge. Not pricing questions." |

A set meant for System One and OpenAI alike leaves out `WhenTrue`, `WhenFalse`, `Examples` and `NotFor`.

#### Refusals

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

### Provider differences

What changes when you choose or switch a provider. The four presets speak System One through `SystemOneClient`, and
OpenAI speaks its Decisions API through `OpenAIDecisionClient`. Each cell is the default; [capabilities](#capabilities)
you set change the kinds, the limits, images and model listing.

| Difference | `TypeSafe` | `OpenRouter` | `VllmSemanticRouter` | `Custom` | `OpenAI` |
| --- | --- | --- | --- | --- | --- |
| Wire format | System One: `POST v1/systemone`, a `questions` object keyed by id, an `answers` object | System One, as TypeSafe | System One, at `v1/systemone` or your `Path` | System One, at `v1/systemone` or your `Path` | Decisions: `POST v1/decisions`, a `questions` array and an `answers` array, matched by `name` |
| Question kinds and their wire names | `noul`, `choice`, `score`; all three by default, MIN201 for one you turn off | `noul`, `choice`, `score`; all three by default, MIN201 for one you turn off | `noul`, `choice`, `score`; all three by default, MIN201 for one you turn off | `noul`, `choice`, `score`; all three by default, MIN201 for one you turn off | `predicate` for a Noul, `choice`, `score`; all three by default, MIN201 for one you turn off |
| Noul `WhenTrue` and `WhenFalse` | Sent | Sent | Sent | Sent | MIN204, not sent |
| Criterion `Examples` and `NotFor` | Sent, in a criterion object | Sent, in a criterion object | Sent, in a criterion object | Sent, in a criterion object | MIN205, not sent |
| JSON instructions, criteria and state | Sent as JSON | Sent as JSON | Sent as JSON | Sent as JSON | Sent as the JSON's raw text, in a string |
| Limits | 255 options per Choice, MIN203; no question limit | 255 options per Choice, MIN203; no question limit | None published | None published | None published |
| Images | Off: Jev reads text only, MIN206 while off. In `state`, as `image_url` parts, once switched on; data URLs only | Off: switch on with `Images = true` for a model that reads images, MIN206 while off. In `state`, as `image_url` parts; data URLs only | Off, MIN206 while off. In `state`, as `image_url` parts, once switched on; data URLs only | Off, MIN206 while off. In `state`, as `image_url` parts, once switched on; data URLs only | On. In `input`, as `input_image` parts; data URLs only |
| Refusals | None in the protocol | None in the protocol | None in the protocol | None in the protocol | `Refused`, one failure per refused question, rule `refusal` |
| Noul confidence, usage and cost | A Noul has a probability and no confidence. `usage` has the input and output tokens | A Noul has a probability and no confidence. `usage` has the tokens and `cost` in US dollars; the response has a generation `id` and the upstream `provider` | A Noul has a probability and no confidence. `usage` as the server sends it | A Noul has a probability and no confidence. `usage` as the server sends it | A Noul has a probability and no confidence. Usage is not documented: tokens and `cost` are read when present, and `Usage` is `null` unless both token counts are |
| Model listing | `ListModelsAsync` | `Unsupported`; OpenRouter has its own models API | `Unsupported`, unless `ModelListing = true` | `Unsupported`, unless `ModelListing = true` | No `ListModelsAsync` |
| Auth | Required: `ApiKey` or `TYPESAFE_API_KEY` | Required: `ApiKey` or `OPENROUTER_API_KEY` | Optional: `ApiKey` only | Optional: `ApiKey` only | Required: `ApiKey` or `OPENAI_API_KEY` |
| Status | Tested live | Tested live, with images and Clef | WireMock tests | WireMock tests, and a local server in CI | Public beta since 2026-10-06; WireMock tests built from the guide's examples, not tested live |

Choice and Score answers carry a confidence on every provider. A limit you set in `Capabilities` applies on every
provider: MIN202 for more questions than `MaxQuestions`, MIN203 for more options than `MaxChoiceOptions`. A request with
images to a provider whose `Images` is off returns MIN206 without sending. [Images](#images) has the details.

#### Notes from the probes

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

#### Clef and Luna through OpenRouter

OpenRouter serves Cloudflare's Clef and OpenAI's Luna in the System One format, so the OpenRouter preset reaches them:
set `Model` to the model's OpenRouter id. The client then speaks System One, and the OpenRouter column above applies,
not the OpenAI one: `WhenTrue`, `Examples` and JSON are sent, and images stay off until you switch them on. The live
tests run `cloudflare/clef-flash` on text. A Luna test runs only when `MINOS_LIVE_LUNA=1` is set, because the
maintainer's account allows only zero-data-retention endpoints.

<!-- snippet: ClientAndErrors_OpenRouterModels -->
```cs
// OpenRouter serves Clef and Luna in the System One format, so the OpenRouter preset reaches them by model id.
// The request is System One: the OpenRouter column of the provider differences applies, not the OpenAI one.
public static SystemOneClientOptions Clef()
    => new() { Provider = SystemOneProvider.OpenRouter, Model = "cloudflare/clef-flash" };

// Luna through OpenRouter needs an account that allows endpoints which retain data; see the notes above.
public static SystemOneClientOptions Luna()
    => new() { Provider = SystemOneProvider.OpenRouter, Model = "openai/gpt-6-luna-decisions" };
```
<!-- endSnippet -->

## Capabilities

A provider can accept less than the protocol allows. A server you run, for example, may answer one kind of question
only. Minos checks a question set against what the provider accepts before it sends the request. A set the provider
would refuse comes back as a failure at once, with no request, no retry and no cost.

### The defaults

Each preset, and OpenAI, has defaults for the five capabilities. "None" means no limit.

| Provider | Question kinds | Most questions | Most Choice options | Images | Model listing |
| --- | --- | --- | --- | --- | --- |
| `TypeSafe` | every kind | none | 255 | no | yes |
| `OpenRouter` | every kind | none | 255 | no | no |
| `VllmSemanticRouter` | every kind | none | none | no | no |
| `Custom` | every kind | none | none | no | no |
| `OpenAI` | every kind | none | none | yes | no |

`Images` says whether the provider takes [images](#images). A request with images to a provider whose `Images` is off
returns `Unsupported` with `MIN206`, and nothing is sent.

OpenAI's `ModelListing` is always off: `OpenAIDecisionClient` has no model listing, so it ignores
`Capabilities.ModelListing`, and `GetService<DecisionCapabilities>()` reports `false` whatever you set.

### Overriding them

`SystemOneClientOptions.Capabilities` and `OpenAIDecisionClientOptions.Capabilities` are a `DecisionCapabilities`,
with one nullable property for each capability. A property you set wins, and one you leave `null` keeps the default.
This server answers Noul questions only:

<!-- snippet: ClientAndErrors_Capabilities -->
```cs
// A server that only answers Noul questions. Each property left unset keeps the Custom preset's default, so
// this changes the accepted kinds and nothing else.
public static SystemOneClientOptions NoulOnly()
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
| `MIN206` | The request has images and `Images` is off. | none | `Custom does not support images.` |

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
    var images = capabilities.Images == true ? "on" : "off";
    var listing = capabilities.ModelListing == true ? "on" : "off";
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

OpenAI takes images by default. Every System One preset has `Images` off: TypeSafe's Jev reads text only, and a server
of your own may not read images. A request with images to a provider whose `Images` is off returns `Unsupported` with
`MIN206` before anything is sent, and it is not retried.

OpenRouter routes to text and image models alike, so images are off for the preset as well. Switch them on for a model
that reads images:

<!-- snippet: ClientAndErrors_OpenRouterImages -->
```cs
// OpenRouter mixes text and image models, so images are off for the preset. Switch them on for a model that reads
// images. Perplexity's decider read our test image correctly on 2026-10-11.
public static SystemOneClientOptions PerplexityWithImages()
    => new()
    {
        Provider = SystemOneProvider.OpenRouter,
        Model = "perplexity/pplx-decider-v1.1-27b",
        Capabilities = new DecisionCapabilities { Images = true },
    };
```
<!-- endSnippet -->

As of 2026-10-11, `perplexity/pplx-decider-v1.1-27b` read our test image correctly. `cloudflare/clef` and
`cloudflare/clef-flash` take images and answered wrongly, and `typesafe/jev-1.13` is text-only and ignores an image
without an error. [Notes from the probes](#notes-from-the-probes) has the details. A `Custom` or `VllmSemanticRouter`
server that reads images is switched on the same way, with `Capabilities.Images = true`.

### On the wire

Without images, both protocols send exactly what they send for text alone. With images:

- **System One** sends `state` as an array: first `{"type":"text","text":…}`, holding the text or the JSON state's raw
  text, then one `{"type":"image_url","image_url":{"url":"data:…"}}` per image, in order.
- **OpenAI** sends `input` as one user message: an `input_text` part with the state, then one `input_image` part per
  image, as [the mapping](#the-mapping) shows.

## Retries

Minos retries a failed call for you. For a typed, built-set or `DecisionRequest` call, the retries are the retry stage
of the client's [pipeline](pipeline.md), `RetryingDecisionClient`, which the client builds from the options below. The
raw System One calls retry with the same settings. These failures are retried:

- rate limiting, HTTP 429;
- overload, HTTP 503 and 529;
- any other server error, HTTP 5xx;
- request time-out, HTTP 408;
- a network failure, such as a refused connection;
- a time-out of the client's own.

Everything else is reported at once: a rejected key (401, 403), an invalid request (400, 422), and any other status.
Trying those again would give the same answer.

With `UseStandardPipeline` set to `false`, nothing is retried, the raw calls included, until you add a retry stage
yourself with `UseRetries()`. [Building your own pipeline](pipeline.md#building-your-own-pipeline) shows how, and how
to retry other kinds of failure with `DecisionRetryOptions.ShouldRetry`.

The wait before the first retry is `InitialBackoff`, and it doubles each time, up to `MaxRetryDelay`. With the defaults
the waits are about 0.5 s, then 1 s, and a call makes at most three attempts. With `Jitter` on, each wait gains a random
extra of up to half its length, and a wait never exceeds `MaxRetryDelay`. Each retry also sends the attempt number in an
`X-TypeSafe-Retry-Count` header, as TypeSafe's own SDKs do. The first attempt does not send it.

<!-- snippet: ClientAndErrors_Retries -->
```cs
// Four retries, waiting about 1 s, 2 s, 4 s and then 8 s, each wait at most 20 s.
public static SystemOneClientOptions Patient(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        MaxRetries = 4,
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(20),
    };

// No retries: a failed call is reported at once. Use this where a duplicate, billed request is worse than a failure.
public static SystemOneClientOptions NoRetries(string apiKey)
    => new() { ApiKey = apiKey, MaxRetries = 0 };
```
<!-- endSnippet -->

### Retry-After

A busy service usually says how long to wait. Minos reads two headers: `retry-after-ms`, in milliseconds, and the
standard `Retry-After`, either as a number of seconds or as an HTTP date. When both are present, `retry-after-ms` wins,
provided it is a non-negative number. An invalid `retry-after-ms` is ignored and `Retry-After` is used. The wait
replaces the backoff for the next attempt and is waited for as asked, never beyond `MaxRetryDelay`. A server that asks
for an hour is waited for `MaxRetryDelay`, 30 seconds by default, and then asked again. The value is also available to
you, as `DecisionError.RetryAfter`, on a failure that comes back.

TypeSafe's [API reference](https://docs.typesafe.ai/api) asks clients to back off exponentially on a 429 or 529, and
does not say whether those responses carry either header; its official Python SDK reads both. This library's own live
runs have not yet met a 429 or 529, so as of October 2026 whether TypeSafe sends a wait is unconfirmed. Either way the
client behaves correctly: it waits as asked when a header is present, and backs off when none is.

### The cost of retrying

Retrying can be billed twice. When a time-out, a network failure or a 5xx happens after the request reached the server,
the server may already have processed it, and it may charge for it. Retrying then sends a second request that is
processed, and charged, again. If a duplicate charge matters more to you than resilience, set `MaxRetries = 0`.

If you route the client through a handler that retries by itself, such as a standard resilience handler, set
`MaxRetries = 0` too. Otherwise the two sets of retries multiply: three attempts of the client inside each of three
attempts of the handler is nine requests.

## Time-outs

`Timeout` bounds one attempt, not the whole call. A call with retries can take up to `(MaxRetries + 1) times Timeout`
plus the waits between attempts. Its time-out applies to a client that owns its `HttpClient`, and to one you set up with
`SystemOneClient.ConfigureHttpClient`, as `AddSystemOneClient` does. An `HttpClient` you pass without that keeps its own
`Timeout`, which is 100 seconds unless you changed it.

<!-- snippet: ClientAndErrors_Timeouts -->
```cs
// Each attempt may take 10 s, and there is one retry. The worst case for one call is therefore two attempts of
// 10 s each, plus one wait of at most MaxRetryDelay between them.
public static SystemOneClientOptions Strict(string apiKey)
    => new() { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(10), MaxRetries = 1 };
```
<!-- endSnippet -->

An attempt that takes longer than `Timeout` fails with `DecisionErrorKind.Timeout`, and is retried like any transient
failure. Cancelling your own `CancellationToken` is different: it stops the call at once and throws, with no retry.

## Errors

A failed call returns a `DecisionError`. Its `Kind` says what went wrong, and the other members add detail when there is any.

You can build one yourself, for a test fake, with `new DecisionError(kind, message)`. `StatusCode`, `RetryAfter`, `Detail`,
`RequestId` and `Exception` are `init` properties, so set only the ones that apply with an object initializer.

| Member | Holds |
| --- | --- |
| `Kind` | A `DecisionErrorKind`: the cause, listed below. |
| `Message` | A short description in English. The response body is in `Detail`, not here. |
| `StatusCode` | The HTTP status, or `null` when no response arrived. |
| `RetryAfter` | How long the service asked you to wait, or `null`. |
| `Detail` | The error response body as a `JsonElement`, when it is JSON, for example the field a 422 rejected. |
| `RequestId` | The provider's id for the failed request, from the error response's headers, or `null`. See [Request ids](#request-ids). |
| `Exception` | The exception behind a network, time-out or unreadable-response failure. |
| `Failures` | For `InvalidQuestions` and for an `Unsupported` capability check, the rules a question set breaks. For `Refused`, the refused questions. Empty for every other kind. |

`Detail` holds whatever JSON the service sent, and its shape depends on the error. TypeSafe answers a 422 with a list
of problems, each with the path of the field (`loc`) and what is wrong with it (`msg`). This is a real one, for a
request with no questions:

```json
{"detail":[{"type":"too_short","loc":["body","questions"],"msg":"Dictionary should have at least 1 item after validation, not 0","input":{},"ctx":{"field_type":"Dictionary","min_length":1,"actual_length":0}}]}
```

A 401, by contrast, carries one object: `{"detail":{"error_type":"authentication_error","message":"…"}}`, and
OpenRouter's errors use its own shape, `{"error":{"message":"…","code":400}}`. OpenAI's are assumed to be
`{"error":{"message":"…","type":"…","param":"…","code":"…"}}`. Check the shape before reading it:

<!-- snippet: ClientAndErrors_ValidationProblems -->
```cs
// TypeSafe answers a 422 with a list of problems. Each one says where it is, as a path such as body.questions,
// and what is wrong. Other errors carry a different body, and a problem may be malformed, so check each shape.
public static IReadOnlyList<string> List(DecisionError error)
{
    var problems = new List<string>();
    if (error is not { Kind: DecisionErrorKind.Validation, Detail: { ValueKind: JsonValueKind.Object } body }
        || !body.TryGetProperty("detail", out var detail)
        || detail.ValueKind != JsonValueKind.Array)
    {
        return problems;
    }

    foreach (var problem in detail.EnumerateArray())
    {
        // Skip an entry that is not shaped like a problem rather than fail while handling an error.
        if (problem.ValueKind != JsonValueKind.Object
            || !problem.TryGetProperty("loc", out var loc) || loc.ValueKind != JsonValueKind.Array
            || !problem.TryGetProperty("msg", out var msg) || msg.ValueKind != JsonValueKind.String)
        {
            continue;
        }

        var path = new List<string>();
        foreach (var part in loc.EnumerateArray())
        {
            path.Add(part.ToString());
        }

        problems.Add($"{string.Join('.', path)}: {msg.GetString()}");
    }

    return problems;
}
```
<!-- endSnippet -->

`ToString()` gives a one-line form, such as `Overloaded (503): The API returned HTTP 503.`, with the request id when
there is one: `Unauthorized (401, request req_…): The API returned HTTP 401.`

### Request ids

`RequestId` is the provider's id for the request that failed. Quote it when you ask the provider about a failure. The
client reads it from the error response's headers, trying three names in this order and taking the first one present:

| Header | Sent by | Example value |
| --- | --- | --- |
| `x-request-id` | OpenAI | `req_` and 32 hex digits |
| `x-typesafe-request-id` | TypeSafe | `req_` and 32 hex digits |
| `request-id` | No provider we know of sends it on an error yet. OpenRouter lists it in `Access-Control-Expose-Headers`, but its 401 carries none. | |

`RequestId` is `null` when the error response carries none of the three, as OpenRouter's 401 does, and for every failure
without an HTTP error response: a network failure, a time-out, a disposal, a success body that could not be read and a
refusal. All three providers also send Cloudflare's `CF-RAY` header, which names the CDN hop rather than your request, so
it is not read. Neither is a body field, such as OpenRouter's generation `id`. These headers were checked on 2026-10-11 with
an invalid key against each endpoint.

Log event 1002 and the evaluation's span carry the id too, as `RequestId` and `minos.request_id`; see
[Logging, traces and metrics](observability.md).

### The kinds

The values start at 1, so `default(DecisionErrorKind)` is no kind.

| `DecisionErrorKind` | When | Retried | What to do |
| --- | --- | --- | --- |
| `Unauthorized` | HTTP 401 or 403: the key is missing, wrong or lacks access. | No | Fix the key. Retrying cannot help. |
| `Validation` | HTTP 400 or 422: the request was rejected. `Detail` usually names the field. | No | Fix the request. This is a bug in the questions or the state. |
| `RateLimited` | HTTP 429. | Yes | Slow down. It comes back only after the retries ran out, and `RetryAfter` says how long to wait. |
| `Overloaded` | HTTP 503 or 529. | Yes | Try again later, or fall back. |
| `Server` | Any other HTTP 5xx. | Yes | Treat it as an outage. |
| `Http` | Any other unsuccessful status, such as 404, or 408. | Only 408 | Report it. The status is in `StatusCode`. |
| `Network` | No response: a DNS, connection or TLS failure. | Yes | Check connectivity. `Exception` holds the cause. |
| `Timeout` | An attempt exceeded `Timeout`. | Yes | Raise `Timeout`, or ask fewer questions. |
| `InvalidResponse` | A successful response could not be read as the expected JSON, or an answer was missing. `StatusCode` is then 200, or the other 2xx status that arrived. | No | Report it. The service replied with something this library does not understand. |
| `Unsupported` | The provider does not offer the operation, such as listing models on OpenRouter, or does not accept the request, such as a Choice question on a Noul-only server, images on a text-only one, or a Noul's `WhenTrue` on OpenAI. See [Capabilities](#capabilities). | No | Do not call it on that provider, or change the set. No request was sent. |
| `InvalidQuestions` | A question set built at run time breaks the API's rules. Only a failed `Build()` returns it, and no request is sent. | No | Fix the set. `Failures` lists each rule. |
| `Disposed` | The client was disposed while the call was in flight, which tore its request down or kept a due retry from being sent. | No | Stop. The client is gone. A call started after disposal throws `ObjectDisposedException` instead. |
| `Refused` | The model refused to answer one or more questions. `Failures` names each one, with rule `refusal`. OpenAI's Decisions API reports refusals; System One providers do not. | No | Rephrase the state or the questions. Retrying the same request gets the same refusal. |

The "Retried" column describes what the client does before it returns the error. A `RateLimited`, `Overloaded`,
`Server`, `Network` or `Timeout` error, or an `Http` error for a 408, that reaches you has already been retried
`MaxRetries` times.

### Handling a failure

A `switch` over `Kind` turns each failure into an action. It needs a catch-all arm, because later versions can add
kinds.

<!-- snippet: ClientAndErrors_Failures -->
```cs
// Every failure of a call comes back as a DecisionError. Kind says what to do about it. The other members
// add detail when there is some: StatusCode, RetryAfter, Detail and Exception.
public static string Describe(DecisionError error) => error.Kind switch
{
    DecisionErrorKind.Unauthorized => "The service rejected the API key. Check the key and what it may access.",
    DecisionErrorKind.Validation => $"The service rejected the request: {error.Detail?.GetRawText() ?? error.Message}",
    DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded when error.RetryAfter is { } wait
        => $"The service is busy. It asks for {(int)wait.TotalMilliseconds} ms before the next call.",
    DecisionErrorKind.RateLimited or DecisionErrorKind.Overloaded => "The service is busy. Try again later.",
    DecisionErrorKind.Server or DecisionErrorKind.Http => $"The service failed with HTTP {error.StatusCode}: {error.Message}",
    DecisionErrorKind.Network or DecisionErrorKind.Timeout => $"The service could not be reached: {error.Exception?.Message ?? error.Message}",
    DecisionErrorKind.InvalidResponse => $"The service replied with something unreadable: {error.Message}",
    DecisionErrorKind.Unsupported => $"The provider cannot do that: {error.Message}",
    DecisionErrorKind.InvalidQuestions => $"The question set is invalid, {error.Failures.Count} rules broken.",
    DecisionErrorKind.Disposed => "The client was disposed while the call was running.",
    DecisionErrorKind.Refused => $"{error.Message} Rephrase the questions or the state.",

    // A kind added in a later version still produces a useful message.
    _ => error.ToString(),
};
```
<!-- endSnippet -->

The same `DecisionError` comes back from every evaluate call: typed, built at run time or raw. A
[fake `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) in a test can return one too.

## The raw request API

[Typed evaluation](typed-evaluation.md) and [question sets built at run time](question-sets-at-run-time.md) cover
most uses. Underneath them is the raw API, the shape of the HTTP request: a state, a model and a dictionary of questions
with ids you choose. You rarely need it, but it is there when you want the exact wire form, or are building your own
layer.

The raw calls, `EvaluateAsync(SystemOneRequest)` and `ListModelsAsync`, are members of `SystemOneClient`, not of
`IDecisionClient`. Code that holds an `IDecisionClient`, such as a class that takes one from dependency injection,
reaches them with `client.GetService<SystemOneClient>()`, which finds the `SystemOneClient` under any stages wrapped
around it and returns `null` when there is none. [Finding a stage or the
transport](pipeline.md#finding-a-stage-or-the-transport) has an example. The raw calls do not run through the
pipeline's stages: they have their own span and log, and retry with the client's settings.

<!-- snippet: ClientAndErrors_RawRequest -->
```cs
// The raw API names its own model and questions, with ids you choose. Use it when the questions are not known at
// compile time and the question set builder does not fit. Typed evaluation is shorter wherever it can be used.
public static async Task<string> UrgencyAsync(
    SystemOneClient client, string message, CancellationToken cancellationToken)
{
    var result = await client.EvaluateAsync(
        new SystemOneRequest
        {
            State = message,
            Questions = new Dictionary<string, Question>
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        },
        cancellationToken);

    if (result.IsFailure)
    {
        return ClientFailures.Describe(result.Error);
    }

    // Answers are keyed by the ids from the request, and each one is a NoulAnswer, ChoiceAnswer or ScoreAnswer.
    var response = result.Value;
    return response.Answers["is_urgent"] is NoulAnswer urgent
        ? $"{response.Model}: {urgent.Noul * 100:0} %, {response.Usage.InputTokens} input tokens"
        : "Unexpected answer type.";
}
```
<!-- endSnippet -->

A `SystemOneRequest` has a required `State`, which is a `DecisionContent` (text converts to one, as
[typed evaluation](typed-evaluation.md#decisioncontent) describes), a `Model` that defaults to `jev-latest`, and required
`Questions`. The response is a `SystemOneResponse`: the versioned `Model` that answered, `Answers` keyed by the ids you
sent, and `Usage` with the input and output token counts. Only input tokens are billed. On OpenRouter the response also
carries the generation `Id` and the upstream `Provider`, and `Usage` carries the `Cost` in US dollars.

Each answer is a `NoulAnswer`, a `ChoiceAnswer` or a `ScoreAnswer`, so you pattern-match to read it. The typed forms do
that matching for you, which is the reason to prefer them. The raw API leaves the API's rules for questions to you,
where the generator, the [analyzers](diagnostics.md) and the
[question set builder](question-sets-at-run-time.md) check them for you. A question the service rejects comes back as a
`Validation` error.

### When your code has its own Question or Answer

The raw API's base types are `Minos.SystemOne.Question` and `Minos.SystemOne.Answer`. If your application has a
`Question` or `Answer` of its own, a file that imports both namespaces cannot name either type bare: the compiler
reports CS0104, an ambiguous reference. A `using` alias settles it, because an alias outranks a namespace import. Alias
your own type to the short name, and give Minos's type a name of its own.

<!-- snippet: ClientAndErrors_NameClash -->
```cs
using Minos.SystemOne;
using Shop.Surveys;

// Shop.Surveys and Minos.SystemOne both have a Question, so a bare Question in this file would be ambiguous: error
// CS0104. An alias outranks a using of a whole namespace, so these two settle it. Question is your own, MinosQuestion
// is Minos's.
using Question = Shop.Surveys.Question;
using MinosQuestion = Minos.SystemOne.Question;

public static class SurveyRequests
{
    // Each survey question becomes a yes/no question in a raw request, under the survey question's own key.
    public static SystemOneRequest ToRequest(Survey survey, string state)
    {
        var questions = new Dictionary<string, MinosQuestion>(StringComparer.Ordinal);
        foreach (Question question in survey.Questions)
        {
            questions[question.Key] = new NoulQuestion { Instructions = question.Text };
        }

        return new SystemOneRequest { State = state, Questions = questions };
    }
}
```
<!-- endSnippet -->

Code in a namespace under `Minos.SystemOne` never sees the clash: it finds Minos's types first.

## Listing models

`ListModelsAsync` returns the models and aliases your account can name in a `SystemOneRequest`: a `ModelList` of
`ModelCard`s, each with a `Name`, a `Description` and a `ReleaseDate`.

<!-- snippet: ClientAndErrors_Models -->
```cs
public static async Task<string> ModelsAsync(SystemOneClient client, CancellationToken cancellationToken)
{
    var result = await client.ListModelsAsync(cancellationToken);
    if (result.IsFailure)
    {
        return ClientFailures.Describe(result.Error);
    }

    var names = new List<string>();
    foreach (var model in result.Value.Models)
    {
        names.Add($"{model.Name} ({model.ReleaseDate})");
    }

    return string.Join(", ", names);
}
```
<!-- endSnippet -->

Listing models follows the `ModelListing` [capability](#capabilities), which is on for `TypeSafe` only. Where it is
off, the call returns an `Unsupported` error and sends no request. Its message names the provider, such as "Model
listing is not available on OpenRouter." OpenRouter has its own models API. A `Custom` server's `GET v1/models` need not
answer in TypeSafe's shape, so listing is off there too; a `Custom` server that does answer in that shape can switch the
call on with `Capabilities.ModelListing = true`.

As of October 2026, TypeSafe lists only the aliases, `jev-latest` for the newest stable release and `jev-preview` for
the newest release of any kind. A versioned id such as `jev-1.13.0` is accepted as a `Model` too, listed or not, as
TypeSafe's [models page](https://docs.typesafe.ai/models) describes. Whichever you send, the response's `Model` names
the versioned model that answered, so log it if you need to know which release produced a result.

Through OpenRouter, `jev-latest` works and the response reports OpenRouter's own model id, such as
`typesafe/jev-1.13-20260917`. As of October 2026 OpenRouter does not offer `jev-preview`: a request for it fails with
`Validation` and HTTP 400, and OpenRouter's body says the model `typesafe/jev-preview` does not exist.

## Next

- [Dependency injection](dependency-injection.md): register the client in a .NET host, with several clients, bound from
  configuration and checked at start-up.
- [Typed evaluation](typed-evaluation.md): the typed calls that return these errors.
- [Performance](performance.md): what a call costs, measured.
