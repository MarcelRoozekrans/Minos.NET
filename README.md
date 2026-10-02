# ZeroAlloc.Jev

[![CI](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/workflows/ci.yml)

Unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One model: send a `state` and typed questions (Noul, Choice, Score) and get calibrated, typed answers back — directly from TypeSafe or through OpenRouter.

> **Not affiliated with TypeSafe AI.** ZeroAlloc.Jev is a community project in the [ZeroAlloc](https://github.com/ZeroAlloc-Net) family. TypeSafe publishes official SDKs for Python and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

**Status:** early development, not yet published to NuGet. See [the roadmap](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/blob/main/docs/planning/ROADMAP.md).

## Requirements

.NET 10 SDK 10.0.100 or later to use the package. Building this repository needs the SDK version pinned in `global.json` (10.0.401 or later, via `rollForward: latestMinor`). The `[JevQuestions]` generator compiles against Roslyn 5.0, so builds and IDEs must host Roslyn 5.0 or later — Visual Studio 2026 version 18.0 is the first release that qualifies. An older IDE shows generator errors even when `dotnet build` succeeds.

## What works so far

- `JevClient` calls `POST /v1/systemone` and `GET /v1/models` on TypeSafe or OpenRouter and returns `Result<T, JevError>` for every outcome — HTTP errors, network failures, time-outs and unreadable responses included.
- `[JevQuestions]` source generator: declare questions as a C# type; the question JSON is emitted at compile time and answers parse into `Noul`, `Choice<TEnum>` and `Score<TEnum>`.
- Typed evaluation: `EvaluateAsync<T>` and `EvaluateAsync<T, TState>` return the generated type directly, over a raw, pooled-buffer path.
- Question sets built at run time: `JevQuestionSet.CreateBuilder()` adds Noul, Choice and Score questions over an enum or over keys known only at run time, checks them against the same rules as the analyzers, and evaluates them through the same pooled path.
- Analyzers and code fixes: question sets are checked at compile time against the Jev API's rules and the generator's, with code fixes that add a missing `[Criteria]` or `[Level]` description. See [Diagnostics](#diagnostics).
- Structured criteria and instructions: `Examples` / `NotFor` on `[Criteria]` and `[Level]`, and `Json = true` for JSON object or array text, checked at compile time.
- Native AOT: verified by an AOT smoke app in CI. Generated sets use no reflection; enum questions in built sets read the enum's public fields through trim-safe annotations.
- Logging: pass an `ILoggerFactory` for structured `Microsoft.Extensions.Logging` events per operation and per retried attempt, never with request or answer content. See [Logging](#logging).
- Telemetry: OpenTelemetry spans and metrics on the `ZeroAlloc.Jev` source and meter, named per the GenAI conventions, never with request or answer content, and adding no allocation to the raw path or to synchronously completing calls until something listens. See [Telemetry](#telemetry).
- Dependency injection: `ZeroAlloc.Jev.DependencyInjection`'s `services.AddJevClient(...)` registers a singleton `IJevClient` over `IHttpClientFactory`, and `AddJevClient(name, ...)` registers keyed clients, each with its own options and `HttpClient`. See [Dependency injection](#dependency-injection).

## Example

```csharp
using ZeroAlloc.Jev;

using var jev = new JevClient(new JevClientOptions { ApiKey = apiKey }); // or set TYPESAFE_API_KEY

var result = await jev.EvaluateAsync(new SystemOneRequest
{
    State = "Help! My payouts have been failing for 3 days.",
    Questions = new Dictionary<string, JevQuestion>
    {
        ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
    },
});

if (result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer urgent)
{
    Console.WriteLine($"P(urgent) = {urgent.Noul:P0}");
}
else if (result.IsFailure)
{
    Console.WriteLine($"{result.Error.Kind}: {result.Error.Message}");
}
```

For OpenRouter, set `Provider = JevProvider.OpenRouter` and an OpenRouter key (or `OPENROUTER_API_KEY`).

### Retries

Rate limiting (429), overload (503, 529), other server errors (5xx), request time-outs (408), network failures and client time-outs are retried with exponential backoff, honouring the server's `Retry-After`. Defaults follow TypeSafe's official SDKs: 2 retries, a 500 ms initial backoff and jitter; each wait, including a server's `Retry-After`, is capped at 30 s. Tune them with `MaxRetries`, `InitialBackoff`, `MaxRetryDelay` and `Jitter` on `JevClientOptions`. Retrying a time-out, network failure or 5xx can process, and bill, a request twice; set `MaxRetries = 0` where that matters. If you pass your own `HttpClient` that already has a retry handler, such as `AddStandardResilienceHandler`, set `MaxRetries = 0` so retries don't multiply. Each retry sends the attempt number as `X-TypeSafe-Retry-Count`, as TypeSafe's official SDKs do.

### Logging

Pass an `ILoggerFactory` to log through `Microsoft.Extensions.Logging`:

```csharp
using var jev = new JevClient(new JevClientOptions { ApiKey = apiKey }, loggerFactory);
// or over your own HttpClient:
using var jev = new JevClient(httpClient, options, loggerFactory);
```

The client logs in the `ZeroAlloc.Jev.JevClient` category. It logs each operation once, when it completes, each attempt it will retry, and each unexpected exception thrown while the call runs. A `null` factory logs nothing, and so do the four constructors without one. Without a logger, or with every level an operation can emit disabled, such as through `NullLoggerFactory` or a filter, the call runs the unlogged path: no allocation, only `IsEnabled` checks.

| Id | Event | Level | Fields |
|---|---|---|---|
| 1001 | `EvaluationSucceeded` | Debug | `Operation`, `Model`, `Provider`, `QuestionCount`, `DurationMs` |
| 1002 | `EvaluationFailed` | Warning | `Operation`, `Model`, `ErrorKind`, `StatusCode`, `DurationMs`, `ErrorMessage` |
| 1003 | `AttemptRetrying` | Warning | `Attempt`, `ErrorKind`, `StatusCode`, `RetryAfter` |
| 1004 | `ModelsListed` | Debug | `Provider`, `ModelCount`, `DurationMs` |
| 1005 | `ModelsListFailed` | Warning | `Provider`, `ErrorKind`, `StatusCode`, `DurationMs`, `ErrorMessage` |
| 1006 | `UnexpectedException` | Error | `Operation`, and the exception |

- **`Operation`** is `evaluate` for `EvaluateAsync(SystemOneRequest)`, `evaluate-typed` for the typed overloads, `evaluate-built-set` for a built `JevQuestionSet`, and `list-models`.
- **`EvaluationFailed`** covers every failure the call returns, including an `InvalidResponse` from reading typed answers.
- **`AttemptRetrying`** is logged for each failed attempt the client retries. `Attempt` is 1 for the first attempt, and matches the `X-TypeSafe-Retry-Count` of the retry that follows. The last attempt is reported by the operation's failure event instead.
- **`UnexpectedException`** means a programming error. The exception surfaces unchanged. Cancellation you requested is not logged, and neither are the argument, disposed and request-writing checks that throw before the call runs.

**Never logged by the library:** the state, instructions, criteria, answers, the API key, any header value, and `JevError.Detail`, which holds the server's error body. `UnexpectedException` is the exception: it carries the exception as thrown, unchanged, including one from your own `DelegatingHandler`, whose message the library cannot vouch for. `ErrorMessage` is `JevError.Message`, except for two kinds whose message can carry request or response text. For `InvalidResponse`, which can quote the server's answer, it is `The response could not be read.` instead. For `Network`, whose message is the transport's exception text and can echo the request, it is `The request could not be sent.` instead.

`JevError.Message` itself is unchanged: for `Network` and `InvalidResponse` it can carry that text. If you log `result.Error.Message` yourself, be aware of what it can hold.

**Cost:** with no logger or every level disabled, logging adds no allocation, and the existing allocation budgets hold. With logging enabled, it adds no allocation to a call that completes synchronously, and about 480 B to a call that completes asynchronously, which is the logging wrappers' state machines. See [Phase 3.1 — Logging](docs/performance.md#phase-31--logging) for the measurements.

A hand-written `IJevClient` that relies on the default interface methods for typed and built-set evaluation logs nothing. The logging lives in `JevClient`.

With the new overloads, `new JevClient(null, null)` no longer compiles (CS0121), because both two-parameter constructors accept two `null` literals. It always threw `ArgumentNullException` before. For defaults and environment variables, write `new JevClient()`.

### Telemetry

The client emits OpenTelemetry spans and metrics through `System.Diagnostics`, generated at compile time by ZeroAlloc.Telemetry. Subscribe to the `ZeroAlloc.Jev` source and meter:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("ZeroAlloc.Jev"))
    .WithMetrics(metrics => metrics.AddMeter("ZeroAlloc.Jev"));
```

Add `ZeroAlloc.Rest` too to see each HTTP attempt: Jev's span is the parent of every attempt's `ZeroAlloc.Rest` span, so a retried call shows one Jev span over several attempts. The version of both is the package's informational version. Nothing needs configuring on the client.

**Span.** One per operation, of kind `Client`.

| | Evaluation | Model listing |
|---|---|---|
| Name | `evaluate {gen_ai.request.model}` | `list_models` |
| `gen_ai.operation.name` | `evaluate` | `list_models` |
| `gen_ai.provider.name` | `typesafe` or `openrouter` | same |
| `gen_ai.request.model` | the requested model | — |
| `server.address`, `server.port` | the base address | same |
| `jev.operation` | `evaluate`, `evaluate-typed` or `evaluate-built-set` | `list-models` |
| `jev.request.question_count` | the number of questions | — |
| `gen_ai.response.model`, `gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens` | on success | — |
| `gen_ai.response.id`, `jev.usage.cost` | on success, for `EvaluateAsync(SystemOneRequest)`, when OpenRouter reports them | — |
| `error.type` | the `JevErrorKind` name, on failure | same |

The first seven rows are set when the span starts, so a sampler sees them. A failed result marks the span `Error`, with no description: `error.type` carries the kind, and `JevError.Message` can quote the request. A typed answer the question set rejects is an `InvalidResponse` failure, as the caller sees it.

**Metrics.**

| Metric | Kind | Unit | Recorded | Attributes |
|---|---|---|---|---|
| `gen_ai.client.operation.duration` | Histogram | `s` | every call | operation, provider, request model, `server.address`, `server.port`; `error.type` on failure; response model on success. The model-listing duration carries no request or response model |
| `gen_ai.client.inference.operation.input_tokens` | Histogram | `{token}` | evaluation success | operation, provider, request model, response model |
| `gen_ai.client.inference.operation.output_tokens` | Histogram | `{token}` | evaluation success | same |
| `gen_ai.client.inference.usage.input_tokens` | Counter | `{token}` | evaluation success | operation, provider, request model, `gen_ai.token.modality` = `text` |
| `gen_ai.client.inference.usage.output_tokens` | Counter | `{token}` | evaluation success | same |
| `jev.answer.confidence` | Histogram | `1` | one point per Choice or Score answer | operation, provider, request model, `jev.operation` |

The histograms carry the GenAI conventions' bucket boundaries as advice. Confidence uses `0.1` to `0.9` in steps of `0.1`, then `0.95` and `0.99`. Noul answers have no confidence and record no point.

**Custom values.** The GenAI conventions list no operation for evaluating or listing models, and no TypeSafe or OpenRouter provider, and they ask instrumentations to document their own:
- `gen_ai.operation.name` is `evaluate` or `list_models`;
- `gen_ai.provider.name` is `typesafe` or `openrouter`.

**Exceptions.** A thrown exception, cancellation included, marks the span `Error` with the exception's message as its description, and the duration is recorded without `error.type`. That is ZeroAlloc.Telemetry's default exception handling, and it is fixed in ZeroAlloc.Telemetry 1.11.0, with adoption tracked in ZeroAlloc.Jev#85.

**Never emitted:** Jev never puts the state, instructions, criteria, answers or probabilities, the API key, any header value, `JevError.Message` or `JevError.Detail` in a tag, a metric attribute or a span description. The one message that can reach a span is a thrown exception's, which is the runtime's.

**Cost.** With nothing listening, the generated proxy returns each operation's own task.
- The raw evaluation, model listing and every call that completes synchronously allocate nothing extra.
- A typed or built-set call that completes asynchronously, as a real network call does, also allocates one extra state machine of 211 B. It hands back the answers and returns the pooled response buffer.
- While listening, a call pays for the span, its tags and the measurements. The benchmarks measure about 1.0 to 1.8 KB per call, depending on the path. A typed call under Native AOT pays 1560 B, which is 4928 B listening against 3368 B with nothing listening.

See [Phase 3.2 — Telemetry](docs/performance.md#phase-32--telemetry).

A hand-written `IJevClient` that relies on the default interface methods for typed and built-set evaluation emits nothing. The telemetry lives in `JevClient`.

**Stability.** The GenAI semantic conventions are in Development, and the token metric names follow the `semantic-conventions-genai` repository's main branch, which has no release yet. Names may change before this package's 1.0.

### Dependency injection

`ZeroAlloc.Jev.DependencyInjection` registers the client in a .NET host, over `IHttpClientFactory`:

```csharp
builder.Services.AddJevClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);

public sealed class TicketTriage(IJevClient jev)
{
    // ...
}
```

For several providers or configurations, register keyed clients and inject each one with `[FromKeyedServices]`:

```csharp
builder.Services.AddJevClient("typesafe", options => options.ApiKey = typesafeKey);
builder.Services.AddJevClient("openrouter", options =>
{
    options.Provider = JevProvider.OpenRouter;
    options.ApiKey = openRouterKey;
});

public sealed class Router([FromKeyedServices("openrouter")] IJevClient jev)
{
    // ...
}
```

The six `AddJevClient` overloads are `()`, `(Action<JevClientOptions>)`, `(IConfiguration)`, `(string name)`, `(string name, Action<JevClientOptions>)` and `(string name, IConfiguration)`, in the namespace `Microsoft.Extensions.DependencyInjection`.

- **One singleton per registration.** It reads its named `JevClientOptions` once, when first resolved. The default client reads the default name, and a keyed client reads its key. Repeat calls for the same name add their configure delegates in order, and register no second client. Registration uses `TryAdd`: if the app registers its own `IJevClient`, or a keyed one under the same name, before `AddJevClient`, the app's registration wins, and `AddJevClient` configures only the named `HttpClient` and the options. Those options are still validated at startup, so a test host that replaces the client needs a placeholder key, for example `AddJevClient(options => options.ApiKey = "test")`, or a `Jev:ApiKey` setting.
- **The `HttpClient` comes from the factory.** It is named `ZeroAlloc.Jev`, or `ZeroAlloc.Jev:{name}` for a keyed client.
  - Its primary handler is a `SocketsHttpHandler` that recycles connections every 2 minutes, so DNS changes are picked up.
  - The factory never rotates that handler, because the singleton keeps its `HttpClient` for life.
  - `JevClient.ConfigureHttpClient` gives it the base address, the per-attempt `Timeout` and the User-Agent.
- **`AddJevClient` returns the `IHttpClientBuilder`,** which is where you add your own handlers. If one of them retries, see [Retries](#retries).
- **Host-wide defaults apply to Jev's clients too.** Handlers added through `ConfigureHttpClientDefaults` also run on `ZeroAlloc.Jev` and `ZeroAlloc.Jev:{name}`, whether the defaults are registered before or after `AddJevClient`.
  - Aspire ServiceDefaults' `AddStandardResilienceHandler()` is the usual case. Its retries multiply with Jev's, and its per-attempt and total time-outs override `JevClientOptions.Timeout`.
  - To keep a defaults handler off Jev's client, call `AddJevClient(...).ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear())`. It also removes every handler you added yourself through that builder. Otherwise set `MaxRetries = 0`, as [Retries](#retries) describes, and keep the handler's time-outs at or above `Timeout`.
  - A primary handler set in the defaults is replaced by `AddJevClient`'s own `SocketsHttpHandler`, and the defaults' loggers are removed.
- **Logging goes through the host's `ILoggerFactory`.** Without a logging provider, or with Jev's levels disabled, the client logs nothing and allocates nothing for logging.
  - The factory's own request logs are off for Jev's clients, because their handlers allocate on every request even when nothing logs, 344 B per call.
  - The client logs each operation and each retried attempt itself.
  - Call `AddDefaultLogger()` on the returned builder to bring the factory's logs back, at that cost.
- **Telemetry is on.** Subscribe with `AddSource("ZeroAlloc.Jev")` and `AddMeter("ZeroAlloc.Jev")`, as [Telemetry](#telemetry) shows. The package takes no OpenTelemetry dependency.
- **Invalid options fail at startup.** Each registration validates its options with `JevClientOptions.Validate()`, the check the client's constructor runs, and `ValidateOnStart()`. So a generic host fails at `StartAsync`. Without a host, as with a plain `BuildServiceProvider()`, they throw when the client is first resolved. That covers a missing API key, an invalid base address, `MaxRetries` outside 0–10, a back-off out of range, a blank model, or a `Timeout` that is zero, negative other than infinite, or longer than about 24.8 days. The exception is an `OptionsValidationException` whose message is the core's own. `JevClientOptions.Validate()` is public, for health checks or hand-built clients. Jev's named `HttpClient` reads the same options, so creating it from `IHttpClientFactory` directly also needs valid options, an API key included.
- **Disposal.** The container disposes the client with the provider. The `HttpClient` belongs to the factory, and is left alone.

#### Configuration

Bind a client's options from configuration, such as `appsettings.json`:

```json
{
  "Jev": {
    "Provider": "TypeSafe",
    "Model": "jev-latest",
    "Timeout": "00:01:00",
    "MaxRetries": 2,
    "InitialBackoff": "00:00:00.500",
    "MaxRetryDelay": "00:00:30",
    "Jitter": true
  },
  "OpenRouter": {
    "Provider": "OpenRouter",
    "MaxRetries": 0
  }
}
```

```csharp
builder.Services.AddJevClient(builder.Configuration.GetSection("Jev"));
builder.Services.AddJevClient("openrouter", builder.Configuration.GetSection("OpenRouter"));
```

- The keys are the `JevClientOptions` property names: `Provider`, `ApiKey`, `BaseAddress`, `Model`, `Timeout`, `MaxRetries`, `InitialBackoff`, `MaxRetryDelay` and `Jitter`. Time spans use the `hh:mm:ss` form. Every key is optional.
- Keep the API key out of `appsettings.json`. Leave `ApiKey` unset to use `TYPESAFE_API_KEY` or `OPENROUTER_API_KEY`, or supply `Jev:ApiKey` from user secrets or an environment variable such as `Jev__ApiKey`.
- A configure delegate registered later for the same client, such as `AddJevClient(options => ...)`, overrides bound values.
- Bound values are validated at startup like any others. A value the binder cannot convert, such as `"MaxRetries": "abc"`, fails at the same point with an `InvalidOperationException` from the binder, not an `OptionsValidationException`.
- Binding is source-generated, so it uses no reflection and stays Native AOT-clean with nothing to set up.
- Changes to the configuration after the client is built are not picked up. The client is a singleton that reads its options once.

**Cost.** An evaluation through a resolved client allocates no more than the same call on a hand-built client over a configured `HttpClient`, and measures equal to it, so DI adds nothing per call. Under Native AOT both allocate 4376 B per call. Registration and the first resolve happen once. A client bound from configuration costs the same per call: binding and validation run once, when the options are first read, which is at startup under a generic host. See [Phase 3.3 — DI package](docs/performance.md#phase-33--di-package).

**Without the package.** `JevClient.ConfigureHttpClient(httpClient, options)` configures any `HttpClient` the way the client configures its own: it applies the per-attempt `Timeout`, the base address when the `HttpClient` has none, and the User-Agent. It needs no API key and does not touch the handler. Call it before the client sends a request, for instance on a named client of your own:

```csharp
services.AddHttpClient("jev")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
    .ConfigureHttpClient(http => JevClient.ConfigureHttpClient(http, options));

var jev = new JevClient(httpClientFactory.CreateClient("jev"), options);
```

A long-lived `JevClient` keeps one handler for life, so the factory's default 2-minute handler rotation would never reach it and DNS changes would go unseen. The `SocketsHttpHandler` recycles its own connections instead, and the infinite handler lifetime stops the factory rotating it.

## Typed evaluation

Declare the questions as a partial record with `[JevQuestions]`; the generator emits the question JSON at compile time and a `Parse` method that reads the typed answers. Call `EvaluateAsync<T>` with the state, and match on the `Result`:

```csharp
using ZeroAlloc.Jev;

using var jev = new JevClient(new JevClientOptions { ApiKey = apiKey });

var result = await jev.EvaluateAsync<UrgencyCheck>("Help! My payouts have been failing for 3 days.");

Console.WriteLine(result switch
{
    { IsSuccess: true } => $"P(urgent) = {result.Value.IsUrgent.Probability:P0}",
    _ => $"{result.Error.Kind}: {result.Error.Message}",
});

[JevQuestions]
public partial record UrgencyCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}
```

`JevClient` answers `EvaluateAsync<T>` over a raw, pooled-buffer UTF-8 path instead of building `SystemOneRequest` and `SystemOneResponse`; any other `IJevClient` (a hand-written fake, for example) falls back to a compatible, allocating default that parses the same typed answers from the untyped call.

For a typed state instead of plain text, name it on the question set with `State = typeof(...)` and call `EvaluateAsync<T, TState>` with a source-generated `JsonTypeInfo<TState>`:

```csharp
using System.Text.Json.Serialization;

var ticket = new TicketContext("Payouts failing", "Help! My payouts have been failing for 3 days.");
var result = await jev.EvaluateAsync<TicketUrgency, TicketContext>(ticket, TicketContextJsonContext.Default.TicketContext);

public sealed record TicketContext(string Subject, string Body);

[JsonSerializable(typeof(TicketContext))]
internal sealed partial class TicketContextJsonContext : JsonSerializerContext;

[JevQuestions(State = typeof(TicketContext))]
public partial record TicketUrgency
{
    [Noul("Does the ticket convey urgency?")]
    public partial Noul IsUrgent { get; }
}
```

Set `JevClientOptions.Model` to send a model or alias other than `JevDefaults.Model` on typed calls.

### Structured criteria and instructions

`[Criteria]` and `[Level]` can carry `Examples` and `NotFor` alongside the description. With either set and non-empty, the generator sends a criterion object instead of plain text for that option; empty arrays are omitted, and a `null` entry in `Examples` or `NotFor` is left out:

```csharp
public enum Department
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Bugs, outages, integrations", Examples = ["The API returns 500"])]
    Technical,

    [Criteria("Pricing, upgrades, new accounts")]
    Sales,

    Other,
}

public enum Severity
{
    [Level("Cosmetic", NotFor = ["Data loss"])]
    Low,

    [Level("Blocks work")]
    High,
}

[JevQuestions]
public partial record Routing
{
    [Choice("Which team should handle this?")]
    public partial Choice<Department> Department { get; }

    [Score("How severe is this?")]
    public partial Score<Severity> Severity { get; }
}
```

which sends:

```json
{
  "department": {
    "type": "choice",
    "instructions": "Which team should handle this?",
    "criteria": {
      "billing": {
        "description": "Payments, invoicing, refunds",
        "examples": ["I was charged twice"],
        "not_for": ["How much is Pro?"]
      },
      "technical": {
        "description": "Bugs, outages, integrations",
        "examples": ["The API returns 500"]
      },
      "sales": "Pricing, upgrades, new accounts",
      "other": null
    }
  },
  "severity": {
    "type": "score",
    "instructions": "How severe is this?",
    "criteria": [
      { "description": "Cosmetic", "not_for": ["Data loss"] },
      "Blocks work"
    ]
  }
}
```

`description`, `examples` and `not_for` are a ZeroAlloc.Jev convention the model reads from the criterion object; the Jev API itself has no such field.

Instructions or a description can also be marked `Json = true`: the text must be a JSON object or array, and the generator checks and minifies it at compile time, then sends it as structured JSON instead of a string:

```csharp
[JevQuestions]
public partial record DuplicateCheck
{
    [Noul(
        """
        {
          "potential_duplicate": {
            "name": "John Smith",
            "location": "Oakland, California",
            "last_employer": "Google"
          },
          "question": "Is the resume for the same person as `potential_duplicate`?"
        }
        """,
        Json = true)]
    public partial Noul IsDuplicate { get; }
}
```

which sends:

```json
"is_duplicate": {
  "type": "noul",
  "instructions": {
    "potential_duplicate": {
      "name": "John Smith",
      "location": "Oakland, California",
      "last_employer": "Google"
    },
    "question": "Is the resume for the same person as `potential_duplicate`?"
  }
}
```

`Json = true` cannot be combined with `Examples` or `NotFor` on the same attribute (JEV109).

The example mirrors the API reference's static illustration, with the candidate written into the instructions. In real use the candidate varies per request, and generated question JSON, including any `Json = true` text, is fixed at compile time and cannot vary per call. So put the candidate in the `State` type and reference it from the instructions by a backticked member name:

```csharp
public sealed record ResumeReview(Candidate Resume, Candidate PotentialDuplicate);
[Noul("""{ "question": "Is the resume for the same person as `potential_duplicate`?" }""", Json = true)]
```

For the raw `SystemOneRequest` model, build `JevContent` directly for `State`, a question's `Instructions`, or a criterion description: `JevContent.FromValue<T>(value, typeInfo)` serializes a value through its source-generated `JsonTypeInfo<T>`, and `JevContent.FromUtf8Json(utf8Json)` parses UTF-8 JSON already in hand. Both throw `ArgumentException` unless the result is a single JSON string, object or array.

## Question sets built at run time

When the questions, options or keys are only known at run time — products from a database, a tenant's own teams — build the set instead of declaring it.

```csharp
var built = JevQuestionSet.CreateBuilder()
    .Noul("urgent", "Is `message` urgent?", out var urgent,
          c => c.WhenTrue("Explicitly time-sensitive").WhenFalse("No urgency expressed"))
    .Choice<Team>("team", "Which team should handle this?", out var team,
          o => o.Describe(Team.Billing, JevCriterion.Text("Refunds").WithExamples("I was charged twice")))
    .Choice("product", "Which product?", out var product,
          o => { foreach (var p in products) o.Option(p.Key, p.Summary); })
    .Score<Urgency>("urgency", "How urgent?", out var urgency,
          l => l.Level(Urgency.Low, "Can wait").Level(Urgency.Medium, "This week").Level(Urgency.High, "Today"))
    .Build();                                   // Result<JevQuestionSet, JevError>

var result = await client.EvaluateAsync(built.Value, ticketText);   // Result<JevAnswers, JevError>
Choice<Team> t = result.Value.Get(team);
KeyedChoice  p = result.Value.Get(product);
```

Each question method takes the question's wire key, its instructions as `JevContent`, text or JSON, an `out` handle, and optionally a configurator. A configurator works only inside its callback: calling a stored one after its question method returned throws `InvalidOperationException`. `Build()` returns `Result<JevQuestionSet, JevError>`; build once and share the set, which is immutable and thread-safe. Read each answer with `JevAnswers.Get(handle)`: `Noul`, `Choice<T>`, `Score<T>`, `KeyedChoice` or `KeyedScore`. A handle works with answers to a set built by the same builder that contains its question; answers to another builder's set, a `default` handle, or a set built before the question was added throw `ArgumentException`.

`JevCriterion` describes an option or level: a string converts to one, `JevCriterion.Text(…).WithExamples(…).WithNotFor(…)` sends the same criterion object as `[Criteria]`'s `Examples` and `NotFor`, and `JevCriterion.Json(content)` sends JSON.

Enum questions read the enum's public fields, its members, once per enum. This is trim- and Native AOT-safe: the builder's generic parameters are annotated so the trimmer keeps the enum's public fields; a method of yours that passes its own generic parameter on to `Choice<T>`, `Score<T>` or `JevAnswers.Get` needs the same `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]`.

A Choice's options are keyed and ordered as the generator does it: keyed by member name in snake_case, in declaration order, and an alias, a member repeating an earlier member's value, is skipped, so the value is keyed by the first name declared for it.

Attributes on the members, such as `[Criteria(Key = …)]` or `[Level]`, are not read: describe the options with `Describe`, and give a Score's levels with `Level`, in order, lowest first. Every distinct member must be given exactly once; list them in declaration order to match the generator.

`Build()` checks the set against these rules:

| Rule | Outcome |
|---|---|
| JEV001 / JEV002: a Choice or Score with no options or levels | Failure |
| JEV104: an enum Score member not given a level | Failure |
| JEV106: an enum Score member given more than one level | Failure |
| JEV106: a duplicate question key, a duplicate keyed option key, or an empty key | Failure |
| JEV108: JSON instructions or a JSON description nested deeper than 60 levels | Failure |
| JEV003: blank instructions, description or example entry, or JSON exactly `{}` or `[]` | Warning |
| JEV005: a Score outside 2–10 levels, or a Choice over 255 options | Warning |

A failure makes `Build()` return a `JevErrorKind.InvalidQuestions` error whose `JevError.Failures`, a read-only list, lists each broken rule as a `JevQuestionFailure(Rule, QuestionKey, Message)`; warnings are on `JevQuestionSet.Warnings`, also a read-only list. `JevErrorKind.InvalidQuestions` is new in this release: an exhaustive `switch` over `JevErrorKind` needs a case for it.

`IJevClient` evaluates a built set through default interface methods, `EvaluateAsync(JevQuestionSet questionSet, JevContent state)` and an overload that takes a `CancellationToken`, so a hand-written fake that implements only the two abstract members supports it too; `JevClient` uses its raw, pooled-buffer path.

## Patterns

The [pattern guides](docs/patterns/index.md) cover speculative fan-out, confidence routing, composite scoring and intent routing, each with C# that is compiled and tested. Two helpers support them: `ConfidenceThresholds` with `ConfidenceTier` classifies a confidence as Low, Medium or High without allocating, and `Normalized` on Score answers puts any rubric's `Expected` on a 0 to 1 scale.

## Diagnostics

`ZeroAlloc.Jev.Analyzers` ships inside the `ZeroAlloc.Jev` package, next to the `[JevQuestions]` generator, and checks every question set against the Jev API's own rules (JEV001–006) and against what the generator can turn into code (JEV101–109). JEV001 and JEV002 enforce TypeSafe's official SDK schema, which needs at least one option or level. The limits behind JEV005 are only the API sketch's guidance, not a schema limit. NuGet flows a dependency's analyzers to consumers transitively, so the ZeroAlloc.Validation, ZeroAlloc.Pipeline and Microsoft.Extensions.Logging generators reach your build too; they stay inert unless you declare `[Validate]` types or your own `[LoggerMessage]` methods. `Microsoft.Extensions.Logging.Abstractions` 10.0.0 or later is a runtime dependency of the package, new with logging.

A set with any error, JEV001, JEV002 or JEV101–109, is invalid. The generator emits no `QuestionsUtf8`, no `Parse` and no `IJevQuestionSet` for it. It does implement each unimplemented partial question property with a throwing stub, so a command-line build reports the JEV error instead of CS9248, "partial property must have an implementation part". Only those properties are stubbed. An error in your own declaration, such as CS0238 for `sealed` on a property that overrides nothing, still stops a command-line build before the analyzers run and hides their output. The IDE, which runs the analyzers live, still shows them.

| Id | Severity | Meaning |
|----|----------|---------|
| JEV001 | Error | A `Choice` enum has no members. |
| JEV002 | Error | A `Score` enum has no members. |
| JEV003 | Warning | Instructions, a description or an `Examples` / `NotFor` entry is empty or whitespace, or JSON text is `{}` or `[]`. |
| JEV004 | Warning | A backticked name in the instructions matches no public member of the `State` type, by lenient matching; against an array `State`, the element type is checked; for `Json = true` instructions, every string value is checked. |
| JEV005 | Warning | A `Score` enum has fewer than 2 or more than 10 levels, or a `Choice` enum has more than 255 options — the API sketch's guidance, not a hard schema limit. |
| JEV006 | Info | A `Choice` member has no `[Criteria]` description. |
| JEV101 | Error | The type carrying `[JevQuestions]` is not a supported shape: a non-generic, non-abstract, non-static, top-level partial class or record that is not file-local. |
| JEV102 | Error | A question property is not a partial, get-only instance property; is `virtual`, `new`, `sealed` or `override`; or is named `Parse` or `QuestionsUtf8`, which the generator reserves. |
| JEV103 | Error | A question property carries more than one question attribute, or one that doesn't match the property type. |
| JEV104 | Error | A `Score` level has no `[Level]` description. |
| JEV105 | Error | The question set has no constructor callable without arguments, or it has `required` members and that constructor lacks `[SetsRequiredMembers]`. A constructor whose parameters all have defaults is accepted. |
| JEV106 | Error | The same wire key is used more than once in the set. |
| JEV107 | Error | The `State` type is not a class, struct, record or array type. |
| JEV108 | Error | Text marked `Json = true` is not a JSON object or array. |
| JEV109 | Error | `Json = true` is combined with `Examples` or `NotFor`. |

Two code fixes are offered, and both can fix all occurrences in a document, project or solution:
- **JEV006** adds `[Criteria("…")]` to the `Choice` enum member.
- **JEV104** adds `[Level("…")]` to the `Score` enum member.

Each derives the description from the member's own name: for example, `NeedsAttention` becomes `"Needs attention"`.

Suppress a warning or info diagnostic that does not apply, narrowly, with a reason:

```csharp
#pragma warning disable JEV005 // Score enum intentionally has 12 levels for this question
public enum Satisfaction { /* ... */ }
#pragma warning restore JEV005
```

Or per-project or per-file in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.JEV005.severity = none
```

Suppressing JEV001, JEV002 or JEV101–109 hides the error but does not make the set valid. The generator does not read the diagnostics; it applies the same shared rules itself. So a suppressed set still gets no `QuestionsUtf8`, no `Parse` and no `IJevQuestionSet`, and any use of it fails: `EvaluateAsync<T>` does not compile, and the stubbed properties throw. Fix the declaration instead.

## Testing

`dotnet test` runs the unit tests, the generator tests, the analyzer and code-fix tests, the WireMock integration tests, and the pack tests, which pack the library and check the package's contents. A solution-wide `dotnet test` never makes a billed call: the live smoke tests in `tests/ZeroAlloc.Jev.Live.Tests` call the real APIs and only run when `JEV_LIVE=1` is set *and* the provider's key (`TYPESAFE_API_KEY` or `OPENROUTER_API_KEY`) is set; otherwise every one of them reports skipped. To run them locally, opt in explicitly with the key set: `JEV_LIVE=1 dotnet test tests/ZeroAlloc.Jev.Live.Tests`. Each run makes a few small billed evaluations. Override the model with `JEV_LIVE_MODEL`, or `JEV_LIVE_OPENROUTER_MODEL` for OpenRouter. Maintainers can also run them in CI with the manual **Live smoke** workflow, which reads the keys from the `live-api` environment; restrict that environment's deployment branches to `main`.

## License

[MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/blob/main/LICENSE)
