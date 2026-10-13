---
id: client-and-errors
title: The client and its errors
sidebar_position: 5
description: Create and configure a SystemOneDecisionClient or an OpenAIDecisionClient, understand retries and time-outs, handle every kind of DecisionError, and use the raw request API.
---

# The client and its errors

Every page so far has called `EvaluateAsync` on an `IDecisionClient`. This page is about that client: how to create one, what
its options do, what happens when a call fails, and how to read the failure. It also covers the raw request API and
listing models.

Which providers a client reaches, what each one accepts and how they differ is on a page of its own,
[Providers](providers.md): the presets, servers you run, OpenAI, capabilities and images.

The failure side matters because a call to a remote model fails in ordinary ways. The network drops, the key is wrong,
the service is busy. Minos reports every such failure as a value, a `DecisionError`, so a failure of the call itself
arrives as a value to check, not as an exception to catch.

## Creating a client

`SystemOneDecisionClient` is the class that implements `IDecisionClient`. It is thread-safe and meant to live for the
whole program: create one, share it, and dispose it when the program stops. Creating a client per call wastes
connections.

<!-- snippet: ClientAndErrors_Constructors -->
```cs
using Microsoft.Extensions.Logging;
using Minos;
using Minos.SystemOne;

public static class ClientConstructors
{
    // The client creates its own HttpClient, and disposing the client disposes it.
    public static SystemOneDecisionClient Owned(string apiKey)
        => new(new SystemOneDecisionClientOptions { ApiKey = apiKey });

    // You pass an HttpClient you manage, for example one from IHttpClientFactory. The client never disposes it.
    public static SystemOneDecisionClient Borrowed(HttpClient http, string apiKey)
        => new(http, new SystemOneDecisionClientOptions { ApiKey = apiKey });

    // Either form also takes an ILoggerFactory, and then logs each operation and each retried attempt.
    public static SystemOneDecisionClient Logged(string apiKey, ILoggerFactory loggers)
        => new(new SystemOneDecisionClientOptions { ApiKey = apiKey }, loggers);
}
```
<!-- endSnippet -->

There are two families of constructor.

- **The client owns its `HttpClient`.** `new SystemOneDecisionClient(options)`, or `new SystemOneDecisionClient()` for
  all defaults. The client creates the `HttpClient`, applies the options to it, and disposes it in `Dispose`.
- **You lend an `HttpClient`.** `new SystemOneDecisionClient(http, options)`. The client uses it and never disposes it,
  so whoever created it still owns it. This is how a client over `IHttpClientFactory` works, and it is what a test does
  to put a fake handler under the client. Two things follow. The `HttpClient`'s own `BaseAddress` wins over
  `SystemOneDecisionClientOptions.BaseAddress` when it is set, and it must end in `/`. And its own `Timeout` applies,
  not `SystemOneDecisionClientOptions.Timeout`, unless you set it up with `SystemOneDecisionClient.ConfigureHttpClient`,
  which the [dependency injection](dependency-injection.md#using-minos-without-the-package) page shows.

Both families take a `null` options object to mean "all defaults and environment variables", and both have an overload
that also takes an `ILoggerFactory`. With one, the client logs each operation and each retried attempt, as [Logging,
traces and metrics](observability.md#logging) describes. What it logs never contains the state, the questions, the
answers, the API key or a header value.

Two calls with bare `null` literals do not compile (CS0121), because more than one constructor accepts them.
`new SystemOneDecisionClient(null)` matches both `(SystemOneDecisionClientOptions?)` and `(HttpClient)`.
`new SystemOneDecisionClient(null, null)` matches both `(SystemOneDecisionClientOptions?, ILoggerFactory?)` and
`(HttpClient, SystemOneDecisionClientOptions?)`; before the logging overloads it compiled and always threw
`ArgumentNullException` for the missing `HttpClient`. For all defaults and environment variables, write
`new SystemOneDecisionClient()`. Any other call resolves once its `null` has a type, for example
`new SystemOneDecisionClient((SystemOneDecisionClientOptions?)null)`.

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

`SystemOneDecisionClientOptions` has twelve properties, and every one is optional.

| Property | Default | Valid values | What it does |
| --- | --- | --- | --- |
| `Provider` | `SystemOneProvider.TypeSafe` | `TypeSafe`, `OpenRouter`, `VllmSemanticRouter` or `Custom` | Where requests go. The [Providers](providers.md#the-presets) page lists what each one sets. |
| `ApiKey` | none | any text without control characters; blank counts as unset | The key sent as a bearer token. When unset, the client reads `TYPESAFE_API_KEY` for TypeSafe and `OPENROUTER_API_KEY` for OpenRouter. `VllmSemanticRouter` and `Custom` read no variable and send no key unless this is set. |
| `BaseAddress` | the provider's address | absolute `http` or `https` URI, no query or fragment | The API root, for a proxy, a test server or a server you run. When unset, the client reads `TYPESAFE_BASE_URL` for TypeSafe only, then uses the provider's address. `VllmSemanticRouter` and `Custom` have none, so it is required for them. |
| `Model` | the preset's default: `jev-latest` for TypeSafe and OpenRouter; required for the others | not blank | The model that typed evaluation, built question sets and a raw `SystemOneRequest` with no model ask: an alias such as `jev-latest` (or TypeSafe's `jev-preview`), or a versioned id such as `jev-1.13.0`. Surrounding whitespace is trimmed. See [Listing models](#listing-models). |
| `Path` | `v1/systemone` | relative, no leading `/`, no query, fragment or `..`; `Custom` and `VllmSemanticRouter` only | The request path under `BaseAddress`. Surrounding whitespace is trimmed, and blank counts as unset. Rejected for a hosted provider, so a key cannot be sent to another path on a host you did not choose. |
| `Capabilities` | the preset's | see [Capabilities](providers.md#capabilities) | What the provider accepts, checked before a request is sent. Each property you leave unset keeps the preset's value. |
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
public static SystemOneDecisionClientOptions SpelledOut(string apiKey)
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

A `SystemOneRequest` that names no model of its own is sent with this one, so `Model` applies to every call of the client.

### SystemOneDefaults

`SystemOneDefaults` is the static class that holds the values the client falls back on. Use it instead of typing the
same text yourself, for example to send `SystemOneDefaults.Model` explicitly or to name an environment variable in a
message.

| Member | Value | What it is |
| --- | --- | --- |
| `TypeSafeApiKeyEnvironmentVariable` | `TYPESAFE_API_KEY` | The variable a TypeSafe key is read from. |
| `OpenRouterApiKeyEnvironmentVariable` | `OPENROUTER_API_KEY` | The variable an OpenRouter key is read from. |
| `TypeSafeBaseAddressEnvironmentVariable` | `TYPESAFE_BASE_URL` | The variable that overrides TypeSafe's base address; no other provider reads it. |
| `Model` | `jev-latest` | The model alias that the TypeSafe and OpenRouter presets use when no model is set. A property, so a changed default is not compiled into your code. |
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
public static string? Problem(SystemOneDecisionClientOptions options)
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

A call the client's own [rate limiter](pipeline.md#limiting-the-call-rate) rejects is not retried either. The client's
retries run inside the rate-limiting stage, so they never see a local rejection, which returns at once. Only a retry
stage added outside the limiter retries it, as the [stage order](pipeline.md#stage-order) shows.

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
public static SystemOneDecisionClientOptions Patient(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        MaxRetries = 4,
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(20),
    };

// No retries: a failed call is reported at once. Use this where a duplicate, billed request is worse than a failure.
public static SystemOneDecisionClientOptions NoRetries(string apiKey)
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
`SystemOneDecisionClient.ConfigureHttpClient`, as `AddSystemOneDecisionClient` does. An `HttpClient` you pass without
that keeps its own `Timeout`, which is 100 seconds unless you changed it.

<!-- snippet: ClientAndErrors_Timeouts -->
```cs
// Each attempt may take 10 s, and there is one retry. The worst case for one call is therefore two attempts of
// 10 s each, plus one wait of at most MaxRetryDelay between them.
public static SystemOneDecisionClientOptions Strict(string apiKey)
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
| `request-id` | None known | none known |

No provider we know of sends `request-id` on an error yet. OpenRouter lists it in `Access-Control-Expose-Headers`, but its
401 carries none.

`RequestId` is `null` when the error response carries none of the three, as OpenRouter's 401 does, and for every failure
without an HTTP error response: a network failure, a time-out, a disposal, a success body that could not be read and a
refusal. All three providers also send Cloudflare's `CF-RAY` header, which names the CDN hop rather than your request, so
it is not read. Neither is a body field, such as OpenRouter's generation `id`. These headers were checked on 2026-10-11 with
an invalid key against each endpoint, and the [conformance suite](conformance.md) checks them again on every recording.

Log event 1002 and the evaluation's span carry the id too, as `RequestId` and `minos.request_id`; see
[Logging, traces and metrics](observability.md).

### The kinds

The values start at 1, so `default(DecisionErrorKind)` is no kind.

| `DecisionErrorKind` | When | Retried | What to do |
| --- | --- | --- | --- |
| `Unauthorized` | HTTP 401 or 403: the key is missing, wrong or lacks access. | No | Fix the key. Retrying cannot help. |
| `Validation` | HTTP 400 or 422: the request was rejected. `Detail` usually names the field. | No | Fix the request. This is a bug in the questions or the state. |
| `RateLimited` | HTTP 429, or the client's own [rate limiter](pipeline.md#limiting-the-call-rate), with no `StatusCode`. | Only a 429 | Slow down. A 429 comes back only after the retries ran out. A local rejection comes back at once, and only a retry stage outside the limiter retries it. `RetryAfter` says how long to wait. |
| `Overloaded` | HTTP 503 or 529. | Yes | Try again later, or fall back. |
| `Server` | Any other HTTP 5xx. | Yes | Treat it as an outage. |
| `Http` | Any other unsuccessful status, such as 404, or 408. | Only 408 | Report it. The status is in `StatusCode`. |
| `Network` | No response: a DNS, connection or TLS failure. | Yes | Check connectivity. `Exception` holds the cause. |
| `Timeout` | An attempt exceeded `Timeout`. | Yes | Raise `Timeout`, or ask fewer questions. |
| `InvalidResponse` | A successful response could not be read as the expected JSON, or an answer was missing. `StatusCode` is then 200, or the other 2xx status that arrived. | No | Report it. The service replied with something this library does not understand. |
| `Unsupported` | The provider does not offer the operation, such as listing models on OpenRouter, or does not accept the request, such as a Choice question on a Noul-only server, images on a text-only one, or a Noul's `WhenTrue` on OpenAI. See [Capabilities](providers.md#capabilities). | No | Do not call it on that provider, or change the set. No request was sent. |
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

The raw calls, `EvaluateAsync(SystemOneRequest)` and `ListModelsAsync`, are members of `SystemOneDecisionClient`, not of
`IDecisionClient`. Code that holds an `IDecisionClient`, such as a class that takes one from dependency injection,
reaches them with `client.GetService<SystemOneDecisionClient>()`, which finds the `SystemOneDecisionClient` under any
stages wrapped around it and returns `null` when there is none; `client.GetRequiredService<SystemOneDecisionClient>()`
throws instead. [Finding a stage or the transport](pipeline.md#finding-a-stage-or-the-transport) has an example. The raw
calls do not run through the pipeline's stages: they have their own span and log, and retry with the client's settings.

<!-- snippet: ClientAndErrors_RawRequest -->
```cs
// The raw API names its own questions, with ids you choose, and may name a model. Use it when the questions are not known at
// compile time and the question set builder does not fit. Typed evaluation is shorter wherever it can be used.
public static async Task<string> UrgencyAsync(
    SystemOneDecisionClient client, string message, CancellationToken cancellationToken)
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
        ? $"{response.Model}: {urgent.Noul * 100:0} %, {response.Usage.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "an unknown number of"} input tokens"
        : "Unexpected answer type.";
}
```
<!-- endSnippet -->

A `SystemOneRequest` has a required `State`, which is a `DecisionContent` (text converts to one, as
[typed evaluation](typed-evaluation.md#decisioncontent) describes), an optional `Model` and required `Questions`. A `Model`
you leave `null` is the client's `Model` option, or the provider's default; a blank one throws. The response is a `SystemOneResponse`: the versioned `Model` that answered, `Answers` keyed by the ids you
sent, and `Usage` with the input and output token counts; a count the server left out is `null`. On OpenRouter the
response also carries the generation `Id` and the `UpstreamProvider` OpenRouter routed the request to, and `Usage` carries
the `Cost` in US dollars.

Each answer is a `NoulAnswer`, a `ChoiceAnswer` or a `ScoreAnswer`, so you pattern-match to read it. The typed forms do
that matching for you, which is the reason to prefer them. The raw API leaves the API's rules for questions to you,
where the generator, the [analyzers](diagnostics.md) and the
[question set builder](question-sets-at-run-time.md) check them for you. A question the service rejects comes back as a
`Validation` error. A request that is malformed in your own code, which is a blank `Model`, a default `State`, null
`Questions`, a null question or a question whose `Instructions` is the default value, throws an `ArgumentException` before
anything is sent.

### The question types

A raw request takes three kinds of question. Each has `Instructions`, what the model should evaluate, and a `Criteria`
that gives the model more to go on. They differ in what `Criteria` holds and in the answer that comes back.

| Type | What `Criteria` holds | Answer |
| --- | --- | --- |
| `NoulQuestion` | Optional. A `NoulCriteria` with `WhenTrue` and `WhenFalse`, each a description of what a yes or a no means, or `null`. | `NoulAnswer` |
| `ChoiceQuestion` | Required. A dictionary that maps each option to a description, or to `null` when the option needs none. | `ChoiceAnswer` |
| `ScoreQuestion` | Required. A list of level descriptions, lowest first. | `ScoreAnswer` |

This request asks a `ChoiceQuestion` and reads the `Choice` and the `Confidence` of the `ChoiceAnswer`.

<!-- snippet: ClientAndErrors_RawChoice -->
```cs
// A ChoiceQuestion maps each option to a description, or to null when the option needs none. Its answer is a
// ChoiceAnswer: the highest-probability option and how certain the model is of it.
public static async Task<string> RouteAsync(
    SystemOneDecisionClient client, string message, CancellationToken cancellationToken)
{
    var result = await client.EvaluateAsync(
        new SystemOneRequest
        {
            State = message,
            Questions = new Dictionary<string, Question>
            {
                ["team"] = new ChoiceQuestion
                {
                    Instructions = "Which team should handle this?",
                    Criteria = new Dictionary<string, DecisionContent?>
                    {
                        ["billing"] = "Payments, invoicing, refunds",
                        ["technical"] = "Bugs, outages, integrations",
                        ["other"] = null,
                    },
                },
            },
        },
        cancellationToken);

    if (result.IsFailure)
    {
        return ClientFailures.Describe(result.Error);
    }

    return result.Value.Answers["team"] is ChoiceAnswer team
        ? $"{team.Choice} ({team.Confidence * 100:0} % sure)"
        : "Unexpected answer type.";
}
```
<!-- endSnippet -->

### The answer types

An answer is a `NoulAnswer`, a `ChoiceAnswer` or a `ScoreAnswer`, under the id of its question. Where an answer
has `Probabilities`, they sum to 1.

| Type | Members |
| --- | --- |
| `NoulAnswer` | `Noul`: the answer on a scale from 0 (no) to 1 (yes). |
| `ChoiceAnswer` | `Choice`: the highest-probability option. `Probabilities`: every option mapped to its probability. `Confidence`: how certain the model is, from 0 to 1. |
| `ScoreAnswer` | `Score`: the probability-weighted level, which can land between levels. `Legend`: each level index, as a string, mapped back to its description. `Probabilities`: each level index mapped to its probability. `Confidence`: how certain the model is, from 0 to 1. |

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
`ModelCard`s, each with a `Name`, a `Description` and a `ReleaseDate`. Like the other calls, it comes as a pair:
`ListModelsAsync()` and `ListModelsAsync(CancellationToken)`.

<!-- snippet: ClientAndErrors_Models -->
```cs
public static async Task<string> ModelsAsync(SystemOneDecisionClient client, CancellationToken cancellationToken)
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

Listing models follows the `SupportsModelListing` [capability](providers.md#capabilities), which is on for `TypeSafe` only. Where it is
off, the call returns an `Unsupported` error and sends no request. Its message names the provider, such as "Model
listing is not available on OpenRouter." OpenRouter has its own models API. A `Custom` server's `GET v1/models` need not
answer in TypeSafe's shape, so listing is off there too; a `Custom` server that does answer in that shape can switch the
call on with `Capabilities.SupportsModelListing = true`.

As of October 2026, TypeSafe lists only the aliases, `jev-latest` for the newest stable release and `jev-preview` for
the newest release of any kind. A versioned id such as `jev-1.13.0` is accepted as a `Model` too, listed or not, as
TypeSafe's [models page](https://docs.typesafe.ai/models) describes. Whichever you send, the response's `Model` names
the versioned model that answered, so log it if you need to know which release produced a result.

Through OpenRouter, `jev-latest` works and the response reports OpenRouter's own model id, such as
`typesafe/jev-1.13-20260917`. As of October 2026 OpenRouter does not offer `jev-preview`: a request for it fails with
`Validation` and HTTP 400, and OpenRouter's body says the model `typesafe/jev-preview` does not exist.

## Next

- [Providers](providers.md): the presets, OpenAI, what each provider accepts, and images.
- [Dependency injection](dependency-injection.md): register the client in a .NET host, with several clients, bound from
  configuration and checked at start-up.
- [Typed evaluation](typed-evaluation.md): the typed calls that return these errors.
- [Performance](performance.md): what a call costs, measured.
