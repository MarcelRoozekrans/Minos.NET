---
id: client-and-errors
title: The client and its errors
sidebar_position: 5
description: Create and configure a JevClient, understand its retries and time-outs, handle every kind of JevError, and use the raw request API.
---

# The client and its errors

Every page so far has called `EvaluateAsync` on an `IJevClient`. This page is about that client: how to create one, what
its options do, what happens when a call fails, and how to read the failure. It also covers the raw request API and
listing models.

The failure side matters because a call to a remote model fails in ordinary ways. The network drops, the key is wrong,
the service is busy. Jev reports every such failure as a value, a `JevError`, so a failure of the call itself arrives as
a value to check, not as an exception to catch.

## Creating a client

`JevClient` is the class that implements `IJevClient`. It is thread-safe and meant to live for the whole program: create
one, share it, and dispose it when the program stops. Creating a client per call wastes connections.

<!-- snippet: ClientAndErrors_Constructors -->
```cs
using Microsoft.Extensions.Logging;
using ZeroAlloc.Jev;

public static class ClientConstructors
{
    // The client creates its own HttpClient, and disposing the client disposes it.
    public static JevClient Owned(string apiKey)
        => new(new JevClientOptions { ApiKey = apiKey });

    // You pass an HttpClient you manage, for example one from IHttpClientFactory. The client never disposes it.
    public static JevClient Borrowed(HttpClient http, string apiKey)
        => new(http, new JevClientOptions { ApiKey = apiKey });

    // Either form also takes an ILoggerFactory, and then logs each operation and each retried attempt.
    public static JevClient Logged(string apiKey, ILoggerFactory loggers)
        => new(new JevClientOptions { ApiKey = apiKey }, loggers);
}
```
<!-- endSnippet -->

There are two families of constructor.

- **The client owns its `HttpClient`.** `new JevClient(options)`, or `new JevClient()` for all defaults. The client
  creates the `HttpClient`, applies the options to it, and disposes it in `Dispose`.
- **You lend an `HttpClient`.** `new JevClient(http, options)`. The client uses it and never disposes it, so whoever
  created it still owns it. This is how a client over `IHttpClientFactory` works, and it is what a test does to put a
  fake handler under the client. Two things follow. The `HttpClient`'s own `BaseAddress` wins over
  `JevClientOptions.BaseAddress` when it is set, and it must end in `/`. And its own `Timeout` applies, not
  `JevClientOptions.Timeout`, unless you set it up with `JevClient.ConfigureHttpClient`, which the
  [dependency injection](dependency-injection.md#using-jev-without-the-package) page shows.

Both families take a `null` options object to mean "all defaults and environment variables", and both have an overload
that also takes an `ILoggerFactory`. With one, the client logs each operation and each retried attempt. What it logs
never contains the state, the questions, the answers, the API key or a header value.

A client throws instead of returning a failure only for mistakes in the calling code. A missing API key, an invalid
option or a `null` request throws when you create the client or make the call. Calling a disposed client throws
`ObjectDisposedException`. Cancelling the `CancellationToken` you passed throws `OperationCanceledException`, because
you asked for it. Everything else, including every network and service failure, comes back as a `JevError`.

## Options

`JevClientOptions` has nine properties, and every one is optional.

| Property | Default | Valid values | What it does |
| --- | --- | --- | --- |
| `Provider` | `JevProvider.TypeSafe` | `TypeSafe` or `OpenRouter` | Where requests go. |
| `ApiKey` | none | not blank, no control characters | The key sent as a bearer token. When unset, the client reads `TYPESAFE_API_KEY`, or `OPENROUTER_API_KEY` for OpenRouter. |
| `BaseAddress` | the provider's address | absolute `http` or `https` URI, no query or fragment | The API root, for a proxy or a test server. When unset, the client reads `TYPESAFE_BASE_URL` for TypeSafe only, then uses `https://api.typesafe.ai/` or `https://openrouter.ai/api/`. |
| `Model` | `jev-latest` | not blank | The model that typed evaluation asks, as a versioned id such as `jev-1.13.0` or an alias. |
| `Timeout` | 60 seconds | positive, or `Timeout.InfiniteTimeSpan`, and at most about 24.8 days | How long one attempt may take. |
| `MaxRetries` | 2 | 0 to 10 | How many times a failed call is tried again. 0 turns retries off. |
| `InitialBackoff` | 500 ms | positive, and at most about 24.8 days | The first wait between attempts. It doubles for each further retry. |
| `MaxRetryDelay` | 30 seconds | at least `InitialBackoff`, and at most about 24.8 days | The longest wait between attempts, for the backoff and for a server's `Retry-After` alike. |
| `Jitter` | `true` | `true` or `false` | Adds a random extra of up to 50 percent to each backoff wait, so many clients do not retry in step. |

Each option you set beats the environment. The order is: the option, then the environment variable, then the provider's
default. A blank API key counts as unset. `TYPESAFE_BASE_URL`
never applies to OpenRouter, so an OpenRouter key is never sent to a TypeSafe proxy.

The first snippet spells out every option that has a default, with its default value. You rarely write all of them. It
is here so the table above has code to match.

<!-- snippet: ClientAndErrors_Options -->
```cs
// Every option set to the value it has when you leave it out, apart from the API key, which has no default:
// without one, the client reads TYPESAFE_API_KEY, or OPENROUTER_API_KEY for OpenRouter.
// BaseAddress is also left out: it defaults to the provider's own address.
public static JevClientOptions SpelledOut(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        Provider = JevProvider.TypeSafe,
        Model = "jev-latest",
        Timeout = TimeSpan.FromSeconds(60),
        MaxRetries = 2,
        InitialBackoff = TimeSpan.FromMilliseconds(500),
        MaxRetryDelay = TimeSpan.FromSeconds(30),
        Jitter = true,
    };
```
<!-- endSnippet -->

A `SystemOneRequest` names its own model, so `Model` applies to typed evaluation and to question sets only.

### Checking options early

The constructors check the options, and so does `Validate()`. It runs exactly the check a constructor runs, including
the API key and base address environment variables, but creates no client. Use it in a health check, or to fail fast on
start-up.

<!-- snippet: ClientAndErrors_Validate -->
```cs
// Validate runs the check the constructors run, without creating a client. It throws ArgumentException for a value
// that is out of range, and InvalidOperationException when no API key can be found.
public static string? Problem(JevClientOptions options)
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

Jev retries a failed call for you. These failures are retried:

- rate limiting, HTTP 429;
- overload, HTTP 503 and 529;
- any other server error, HTTP 5xx;
- request time-out, HTTP 408;
- a network failure, such as a refused connection;
- a time-out of the client's own.

Everything else is reported at once: a rejected key (401, 403), an invalid request (400, 422), and any other status.
Trying those again would give the same answer.

The wait before the first retry is `InitialBackoff`, and it doubles each time, up to `MaxRetryDelay`. With the defaults
the waits are about 0.5 s, then 1 s, and a call makes at most three attempts. With `Jitter` on, each wait gains a random
extra of up to half its length, and a wait never exceeds `MaxRetryDelay`. Each retry also sends the attempt number in an
`X-TypeSafe-Retry-Count` header, as TypeSafe's own SDKs do. The first attempt does not send it.

<!-- snippet: ClientAndErrors_Retries -->
```cs
// Four retries, waiting about 1 s, 2 s, 4 s and then 8 s, each wait at most 20 s.
public static JevClientOptions Patient(string apiKey)
    => new()
    {
        ApiKey = apiKey,
        MaxRetries = 4,
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxRetryDelay = TimeSpan.FromSeconds(20),
    };

// No retries: a failed call is reported at once. Use this where a duplicate, billed request is worse than a failure.
public static JevClientOptions NoRetries(string apiKey)
    => new() { ApiKey = apiKey, MaxRetries = 0 };
```
<!-- endSnippet -->

### Retry-After

A busy service usually says how long to wait. Jev reads two headers: `retry-after-ms`, in milliseconds, and the standard
`Retry-After`, either as a number of seconds or as an HTTP date. When both are present, `retry-after-ms` wins. The wait
replaces the backoff for the next attempt and is honoured exactly, but never beyond `MaxRetryDelay`. A server that asks
for an hour is waited for `MaxRetryDelay`, 30 seconds by default, and then asked again. The value is also available to
you, as `JevError.RetryAfter`, on a failure that comes back.

### The cost of retrying

Retrying can be billed twice. When a time-out, a network failure or a 5xx happens after the request reached the server,
the server may already have processed it, and it may charge for it. Retrying then sends a second request that is
processed, and charged, again. If a duplicate charge matters more to you than resilience, set `MaxRetries = 0`.

If you route the client through a handler that retries by itself, such as a standard resilience handler, set
`MaxRetries = 0` too. Otherwise the two sets of retries multiply: three attempts of Jev's inside each of three attempts
of the handler is nine requests.

## Time-outs

`Timeout` bounds one attempt, not the whole call. A call with retries can take up to `(MaxRetries + 1) times Timeout`
plus the waits between attempts. Its time-out applies to a client that owns its `HttpClient`, and to one you set up with
`JevClient.ConfigureHttpClient`, as `AddJevClient` does. An `HttpClient` you pass without that keeps its own `Timeout`,
which is 100 seconds unless you changed it.

<!-- snippet: ClientAndErrors_Timeouts -->
```cs
// Each attempt may take 10 s, and there is one retry. The worst case for one call is therefore two attempts of
// 10 s each, plus one wait of at most MaxRetryDelay between them.
public static JevClientOptions Strict(string apiKey)
    => new() { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(10), MaxRetries = 1 };
```
<!-- endSnippet -->

An attempt that takes longer than `Timeout` fails with `JevErrorKind.Timeout`, and is retried like any transient
failure. Cancelling your own `CancellationToken` is different: it stops the call at once and throws, with no retry.

## Errors

A failed call returns a `JevError`. Its `Kind` says what went wrong, and the other members add detail when there is any.

| Member | Holds |
| --- | --- |
| `Kind` | A `JevErrorKind`: the cause, listed below. |
| `Message` | A short description in English. The response body is in `Detail`, not here. |
| `StatusCode` | The HTTP status, or `null` when no response arrived. |
| `RetryAfter` | How long the service asked you to wait, or `null`. |
| `Detail` | The error response body as a `JsonElement`, when it is JSON, for example the field a 422 rejected. |
| `Exception` | The exception behind a network, time-out or unreadable-response failure. |
| `Failures` | For `InvalidQuestions` only, the rules a question set breaks. Empty for every other kind. |

`ToString()` gives a one-line form, such as `Overloaded (503): The API returned HTTP 503.`

### The kinds

| `JevErrorKind` | When | Retried | What to do |
| --- | --- | --- | --- |
| `Unauthorized` | HTTP 401 or 403: the key is missing, wrong or lacks access. | No | Fix the key. Retrying cannot help. |
| `Validation` | HTTP 400 or 422: the request was rejected. `Detail` usually names the field. | No | Fix the request. This is a bug in the questions or the state. |
| `RateLimited` | HTTP 429. | Yes | Slow down. It comes back only after the retries ran out, and `RetryAfter` says how long to wait. |
| `Overloaded` | HTTP 503 or 529. | Yes | Try again later, or fall back. |
| `Server` | Any other HTTP 5xx. | Yes | Treat it as an outage. |
| `Http` | Any other unsuccessful status, such as 404, or 408. | Only 408 | Report it. The status is in `StatusCode`. |
| `Network` | No response: a DNS, connection or TLS failure. | Yes | Check connectivity. `Exception` holds the cause. |
| `Timeout` | An attempt exceeded `Timeout`. | Yes | Raise `Timeout`, or ask fewer questions. |
| `InvalidResponse` | A successful response could not be read as the expected JSON, or an answer was missing. | No | Report it. The service replied with something this library does not understand. |
| `Unsupported` | The operation is not available on the provider, such as listing models on OpenRouter. | No | Do not call it on that provider. No request was sent. |
| `InvalidQuestions` | A question set built at run time breaks the API's rules. Only a failed `Build()` returns it, and no request is sent. | No | Fix the set. `Failures` lists each rule. |

The "Retried" column describes what the client does before it returns the error. A `RateLimited`, `Overloaded`,
`Server`, `Network` or `Timeout` error that reaches you has already been retried `MaxRetries` times.

### Handling a failure

A `switch` over `Kind` turns each failure into an action. It needs a catch-all arm, because later versions can add
kinds.

<!-- snippet: ClientAndErrors_Failures -->
```cs
// Every failure of a call comes back as a JevError. Kind says what to do about it. The other members
// add detail when there is some: StatusCode, RetryAfter, Detail and Exception.
public static string Describe(JevError error) => error.Kind switch
{
    JevErrorKind.Unauthorized => "Jev rejected the API key. Check the key and what it may access.",
    JevErrorKind.Validation => $"Jev rejected the request: {error.Detail?.GetRawText() ?? error.Message}",
    JevErrorKind.RateLimited or JevErrorKind.Overloaded when error.RetryAfter is { } wait
        => $"Jev is busy. It asks for {(int)wait.TotalMilliseconds} ms before the next call.",
    JevErrorKind.RateLimited or JevErrorKind.Overloaded => "Jev is busy. Try again later.",
    JevErrorKind.Server or JevErrorKind.Http => $"Jev failed with HTTP {error.StatusCode}: {error.Message}",
    JevErrorKind.Network or JevErrorKind.Timeout => $"Jev could not be reached: {error.Exception?.Message ?? error.Message}",
    JevErrorKind.InvalidResponse => $"Jev replied with something unreadable: {error.Message}",
    JevErrorKind.Unsupported => $"The provider cannot do that: {error.Message}",
    JevErrorKind.InvalidQuestions => $"The question set is invalid, {error.Failures.Count} rules broken.",

    // A kind added in a later version still produces a useful message.
    _ => error.ToString(),
};
```
<!-- endSnippet -->

The same `JevError` comes back from every evaluate call: typed, built at run time or raw. A fake `IJevClient` in a test
can return one too.

## The raw request API

Typed evaluation and question sets cover most uses. Underneath them is the raw API, the shape of the HTTP request: a
state, a model and a dictionary of questions with ids you choose. You rarely need it, but it is there when you want the
exact wire form, or are building your own layer.

<!-- snippet: ClientAndErrors_RawRequest -->
```cs
// The raw API names its own model and questions, with ids you choose. Use it when the questions are not known at
// compile time and the question set builder does not fit. Typed evaluation is shorter wherever it can be used.
public static async Task<string> UrgencyAsync(IJevClient jev, string message, CancellationToken ct)
{
    var result = await jev.EvaluateAsync(
        new SystemOneRequest
        {
            State = message,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        },
        ct);

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

A `SystemOneRequest` has a required `State`, which is a `JevContent` (text converts to one, as
[typed evaluation](typed-evaluation.md#jevcontent) describes), a `Model` that defaults to `jev-latest`, and required
`Questions`. The response is a `SystemOneResponse`: the versioned `Model` that answered, `Answers` keyed by the ids you
sent, and `Usage` with the input and output token counts. Only input tokens are billed. On OpenRouter the response also
carries the generation `Id` and the upstream `Provider`, and `Usage` carries the `Cost` in US dollars.

Each answer is a `NoulAnswer`, a `ChoiceAnswer` or a `ScoreAnswer`, so you pattern-match to read it. The typed forms do
that matching for you, which is the reason to prefer them. The raw API leaves the API's rules for questions to you,
where the generator, the analyzers and the question set builder check them for you. A question the service rejects comes
back as a `Validation` error.

## Listing models

`ListModelsAsync` returns the models and aliases your account can name in a `SystemOneRequest`: a `ModelList` of
`ModelCard`s, each with a `Name`, a `Description` and a `ReleaseDate`.

<!-- snippet: ClientAndErrors_Models -->
```cs
public static async Task<string> ModelsAsync(IJevClient jev, CancellationToken ct)
{
    var result = await jev.ListModelsAsync(ct);
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

Listing models is available on TypeSafe's API only. On OpenRouter the call returns an `Unsupported` error and sends no
request, because OpenRouter has its own models API.

## Next

- [Dependency injection](dependency-injection.md): register the client in a .NET host, with several clients, bound from
  configuration and checked at start-up.
- [Typed evaluation](typed-evaluation.md): the typed calls that return these errors.
- [Performance](performance.md): what a call costs, measured.
