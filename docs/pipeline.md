---
id: pipeline
title: The client pipeline
sidebar_position: 8
description: The stages every call goes through (telemetry, logging and retries), building your own pipeline, caching decisions, limiting the call rate, the stage order, writing a stage, turning the standard pipeline off, stages with dependency injection, and upgrading to 1.0 and from 0.7 and 0.8.
---

# The client pipeline

Every evaluation goes through a **pipeline**: a chain of stages around a transport that sends the request. Each stage
is an `IDecisionClient` that wraps the one inside it. It can act before the call, after it, or both, and then passes
the call on. Telemetry, logging and retries are stages like this. Two packages add a cache stage and a rate-limiting
stage, and you can write your own, such as an audit record.

This page shows the standard pipeline that every `SystemOneDecisionClient` runs, how to build your own, how to cache
decisions and limit the call rate, the order to put stages in, how to write a stage, how to find a stage again, and how
stages fit with dependency injection. It ends with what changed for code written against 0.7 and 0.8.

## What every call goes through

`IDecisionClient` has one call: `EvaluateAsync(DecisionRequest, CancellationToken)`. A `DecisionRequest` holds the
question set's `Definition`, the `State`, an optional `Model` and the `RetryAttempt`. It returns a `Result` of a
`DecisionResponse` or a [`DecisionError`](client-and-errors.md#errors). The typed `EvaluateAsync<T>` calls and the
[built question set](question-sets-at-run-time.md) calls are extension methods that build a `DecisionRequest` and make
that one call, so they go through the pipeline too.

A `SystemOneDecisionClient` builds the standard pipeline for you, so a client you create is already traced and retried,
and logged when you give it a logger factory.

<!-- snippet: Pipeline_Default -->
```cs
using Minos;
using Minos.SystemOne;

// One yes/no question, so the examples stay short.
[Questions]
public partial record PipelineCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

public static class DefaultPipeline
{
    // A SystemOneDecisionClient already runs the standard pipeline: one span and one set of metrics per call, one log record
    // when it was given a logger factory, and up to two retries of a transient failure. There is nothing to set up.
    public static async Task<bool> IsUrgentAsync(
        HttpClient http, string apiKey, string message, CancellationToken cancellationToken)
    {
        using var client = new SystemOneDecisionClient(http, new SystemOneDecisionClientOptions { ApiKey = apiKey });

        // The typed call builds a DecisionRequest from PipelineCheck.Definition and the message, and sends it through
        // the pipeline.
        var result = await client.EvaluateAsync<PipelineCheck>(message, cancellationToken);
        return result.IsSuccess && result.Value.IsUrgent.Value;
    }
}
```
<!-- endSnippet -->

The standard stages run in this order, outermost first. The last two rows are add-on stages that are not in the
standard pipeline; you place them yourself, as [Stage order](#stage-order) describes.

| Stage | Class | What it does |
| --- | --- | --- |
| Telemetry | `OpenTelemetryDecisionClient` | Opens one span and records one set of metrics for the whole call. |
| Logging | `LoggingDecisionClient` | Logs the outcome once: event 1001, 1002 or 1006. Only when the client has a logger factory. |
| Retries | `RetryingDecisionClient` | Retries a transient failure with a growing wait, and logs event 1003 for each retried attempt. |
| Transport | internal | Sends one attempt over HTTP and reads the reply. |
| Caching | `CachingDecisionClient` | Answers a repeated decision from memory or a distributed cache. Add-on package `Minos.NET.Caching`, added with `UseCaching()`. |
| Rate limiting | `RateLimitingDecisionClient` | Takes a permit from a `RateLimiter` before each call. Add-on package `Minos.NET.RateLimiting`, added with `UseRateLimiting()`. |

The order is what makes the signals line up:

- **Telemetry is outermost**, so one span covers every attempt. Its duration is the time the caller waited, retries
  and waits included. A call that fails on every attempt is still one span.
- **Logging sits inside the span**, so the outcome is logged once per call, while the span is open.
- **Retries sit inside both**, so the span and the outcome log see the final result, not each attempt. The retry stage
  sends each retry as a copy of the request with `RetryAttempt` set, and the transport turns that into the
  `X-TypeSafe-Retry-Count` header.
- **The transport is innermost.** It sends one attempt and returns every pooled buffer before it completes.

[Logging, traces and metrics](observability.md) lists what each stage emits. With nothing listening and every log
level off, the stages add nothing to a call that completes synchronously.
[Performance](performance.md#phase-63--the-client-pipeline) has the measurements.

The raw System One calls, `EvaluateAsync(SystemOneRequest)` and `ListModelsAsync`, are not `DecisionRequest` calls,
so they do not run through the stages. They have their own span and log, and retry with the same settings.
[The client page](client-and-errors.md#the-raw-request-api) covers them.

## Building your own pipeline

Set `SystemOneDecisionClientOptions.UseStandardPipeline` to `false`, and the client is the bare transport: one attempt
per call, with no span, no log and no retry. Then build the pipeline you want around it. `AsBuilder()` starts a
`DecisionClientBuilder` from any client, each `Use…` call adds a stage, and `Build()` returns the outermost one.

<!-- snippet: Pipeline_Custom -->
```cs
// The standard stages, in the standard order, with four retries and an audit stage of your own.
public static IDecisionClient Build(HttpClient http, string apiKey, ILoggerFactory loggerFactory, IAuditLog log)
{
    // With the standard pipeline off, the client is the bare transport: one attempt per call, and no span or log.
    var transport = new SystemOneDecisionClient(
        http, new SystemOneDecisionClientOptions { ApiKey = apiKey, UseStandardPipeline = false });

    // The first Use is the outermost stage. The audit stage is the innermost, so it records every attempt.
    return transport.AsBuilder()
        .UseOpenTelemetry()
        .UseLogging(loggerFactory)
        .UseRetries(options => options.MaxRetries = 4)
        .Use(inner => new AuditStage(inner, log))
        .Build();
}
```
<!-- endSnippet -->

- **The first `Use` is the outermost stage.** A call passes through the stages in the order you added them, then
  reaches the client you started from.
- **`UseOpenTelemetry()`** adds the telemetry stage. It reads the provider, the endpoint and the default model from
  the client inside it, as `DecisionClientMetadata`.
- **`UseLogging(loggerFactory)`** adds the logging stage. Without a factory, it uses the `ILoggerFactory` of the
  services passed to `Build`. With neither, it adds nothing.
- **`UseRetries(configure)`** adds the retry stage. Its settings start from the inner client's own, which a
  `SystemOneDecisionClient` offers from its options, and `configure` changes them. It logs event 1003 through the
  `ILoggerFactory` of the services passed to `Build`, and waits on their `TimeProvider`, so `Build()` without services,
  as above, retries without logging 1003.
- **`Build(services)`** creates the stages, inner first. Pass an `IServiceProvider` when a stage needs services.

Each standard stage can also be created directly, such as `new RetryingDecisionClient(inner, options, logger,
timeProvider)`. A stage created this way starts from the library defaults, not from the inner client's settings, as
`UseRetries()` does. `new DecisionClientBuilder(services => …)` starts a builder whose inner client is created at
`Build`, from the services passed to it.

The retry stage takes a `DecisionRetryOptions`:

| Property | Default | Valid values | What it does |
| --- | --- | --- | --- |
| `MaxRetries` | 2 | 0 to 10 | How many times a failed call is tried again. 0 turns retries off. |
| `InitialBackoff` | 500 ms | Positive, at most `int.MaxValue` ms | The first wait. It doubles for each further retry. |
| `MaxRetryDelay` | 30 seconds | At least `InitialBackoff`, at most `int.MaxValue` ms | The longest wait, for the backoff and for a server's `Retry-After` alike. |
| `Jitter` | `true` | `true` or `false` | Adds a random extra to each backoff wait, so many clients do not retry in step. |
| `ShouldRetry` | `IsTransient` | Not `null` | Which failures to retry. `DecisionRetryOptions.IsTransient` accepts the kinds [Retries](client-and-errors.md#retries) lists. |

The settings are checked when the stage is created, so a value out of range throws `ArgumentException` from `Build`,
not from `UseRetries`. The stage copies them, so changing the options afterwards has no effect.

A `Disposed` failure is never retried, whatever `ShouldRetry` says, and no retry starts after the stage is disposed.

Two things to keep in mind when you turn the standard pipeline off:

- **The raw System One calls do not retry either.** They are still traced, and logged when the client has a logger
  factory, but a failed `EvaluateAsync(SystemOneRequest)` or `ListModelsAsync` is returned after one attempt.
- **Do not add a second retry stage over the standard pipeline.** With `UseStandardPipeline` left on, the client
  already retries, and a `UseRetries()` around it multiplies the attempts.

## Caching decisions

`Minos.NET.Caching` adds `UseCaching()`, a stage that answers a repeated decision from two levels: an in-process
`IMemoryCache` first, then Redis or any other `IDistributedCache` the app registers. Without a distributed cache, it
caches in process only. The stage is `CachingDecisionClient`; create it with `new` to wrap a client without a builder, or find it
in a pipeline with `GetService<CachingDecisionClient>()`.

<!-- snippet: Pipeline_Caching -->
```cs
// Answers repeated decisions from memory, and from Redis or any IDistributedCache the app registers.
public static IServiceCollection AddCachedDecisions(IServiceCollection services, string apiKey)
{
    services.AddMemoryCache();
    services.AddSystemOneDecisionClient(options => options.ApiKey = apiKey)
        .UseCaching(configure: cache => cache.Expiration = TimeSpan.FromHours(1));
    return services;
}
```
<!-- endSnippet -->

- **Register an in-process cache.** `services.AddMemoryCache()` registers one. `Build` throws when there is none in the
  services and none is passed to `UseCaching`. A distributed cache in the services is used when one is registered,
  and the stage logs through the services' `ILoggerFactory`, when there is one.
- **A repeat is the same definition, state, images and model, to the same provider and endpoint.** The model is the
  request's `Model`, or the client's default model when the request names none. Two clients for different providers or
  models never share an answer, even in one Redis instance. The retry attempt is not part of the key.
- **Only successes are stored,** in process and then in the distributed cache. An error or a refusal passes through,
  and the next call asks the provider again.
- **A hit is marked.** `DecisionResponse.IsCached` is `true`. The response keeps the model, usage and cost of the call
  that produced it, so filter on `minos.cache.hit` when you count spend.
- **A hit is traced and logged only when telemetry and logging sit outside the cache stage.** Then the span and the
  duration, token and confidence metrics carry `minos.cache.hit`, and log event 1001's `Cached` field is `True`. That
  takes the standard pipeline off and the [stage order](#stage-order) below. With a client's standard pipeline on, its
  telemetry and logging run inside the cache stage, so a hit makes no span, no metric and no log event.
- **An in-process hit reads nothing.** It returns the stored response and completes at once. A hit in the distributed
  cache is read from compact JSON, with no reflection, and kept in process for the next call.
- **Concurrent misses are not merged.** Two identical calls that both miss each ask the provider.
- **A miss waits for the distributed write.** The answer is written to the distributed cache before the call returns,
  and the write is not cancelled with the call, so a slow distributed cache delays every miss.
- **A shared layer needs a real `IDistributedCache`,** such as Redis. `AddDistributedMemoryCache()` registers a
  `MemoryDistributedCache`, which only keeps a second copy in the same process: harmless, but of no use.
- **A failing distributed cache never fails a call.** A read that throws is a miss, logged as a Warning, event 1007. A
  write that throws leaves the answer out of the distributed cache, logged as event 1008; the answer is still returned
  and kept in process. Cancelling the call still throws `OperationCanceledException`.
- **`DecisionCacheOptions`** sets `Expiration`, 5 minutes by default, for both levels; `LocalCacheExpiration`, to keep
  the in-process copy for a different time (neither may exceed 365 days, and a longer value throws
  `ArgumentOutOfRangeException` when it is set); `KeyPrefix`, `minos:` by default, to keep apps that share a cache
  apart; and `UseDistributedCache`, on by default, which you turn off when the app's `IDistributedCache` serves
  something else, such as session state. Passing a distributed cache to `UseCaching` with it off throws at `Build`.
  Each in-process entry has a `Size` of 1, so an `IMemoryCache` with a `SizeLimit` counts entries.

## Limiting the call rate

`Minos.NET.RateLimiting` adds `UseRateLimiting(limiter)`, a stage that takes one permit from a
[`RateLimiter`](https://learn.microsoft.com/dotnet/api/system.threading.ratelimiting.ratelimiter) before each call
is sent. The stage is `RateLimitingDecisionClient`; create it with `new` to wrap a client without a builder, or find it
in a pipeline with `GetService<RateLimitingDecisionClient>()`.

<!-- snippet: Pipeline_RateLimiting -->
```cs
// At most 10 calls a second, shared by every client given the same limiter. A call waits up to 20 deep in the
// queue; beyond that it fails with RateLimited and nothing is sent.
public static readonly RateLimiter SharedLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit = 10,
    TokensPerPeriod = 10,
    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
    QueueLimit = 20,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
});

public static IDecisionClient Build(SystemOneDecisionClient client) => client.AsBuilder().UseRateLimiting(SharedLimiter).Build();
```
<!-- endSnippet -->

- **You own the limiter.** Any `RateLimiter` works: a token bucket, a fixed or sliding window, or a concurrency limiter.
  Share one across clients to limit an account, not a client. The stage never disposes it. Dispose the limiter only after
  the clients that use it: a call on a disposed limiter throws `ObjectDisposedException`.
- **A call waits as the limiter says.** With a queue, a call waits for its permit; cancelling it while it waits throws
  `OperationCanceledException`. A concurrency limiter counts calls in flight, because the permit is held until the call
  completes.
- **A rejected call is not sent.** It returns a `RateLimited` error whose `StatusCode` is null, which tells it apart from
  a server's 429, and whose `RetryAfter` is the limiter's, when it gives one.

### Stage order

The first `Use` is the outermost stage. A client's standard pipeline runs inside every stage you add, so its retries
take no extra permits. When you build the pipeline yourself, use this order:

<!-- snippet: Pipeline_StageOrder -->
```cs
// The recommended order for a pipeline you build yourself: a cache hit takes no permit, and every attempt takes one.
public static IDecisionClient Build(HttpClient http, string apiKey, RateLimiter limiter, IServiceProvider services)
{
    var transport = new SystemOneDecisionClient(http, new SystemOneDecisionClientOptions { ApiKey = apiKey, UseStandardPipeline = false });
    return transport.AsBuilder()
        .UseOpenTelemetry()
        .UseCaching()
        .UseRetries()
        .UseRateLimiting(limiter)
        .Build(services);
}
```
<!-- endSnippet -->

1. telemetry, so the span covers the whole call;
2. caching, so a hit takes no permit;
3. retries, outside the limiter, so a local rejection is retried after its `RetryAfter`;
4. rate limiting, so every attempt takes a permit;
5. the transport.

## Writing a stage

A stage derives from `DelegatingDecisionClient` and overrides `EvaluateAsync`. `base.EvaluateAsync` calls the client
inside, which the stage can also reach as `InnerClient`. This one records every result in an audit log.

<!-- snippet: Pipeline_Stage -->
```cs
using Minos;
using ZeroAlloc.Results;

// Where the audit records go. An application would write them to a database or a log of its own.
public interface IAuditLog
{
    void Record(QuestionSetDefinition definition, Result<DecisionResponse, DecisionError> result);
}

// A stage derives from DelegatingDecisionClient and overrides EvaluateAsync. base.EvaluateAsync calls the client inside.
public sealed class AuditStage(IDecisionClient inner, IAuditLog log) : DelegatingDecisionClient(inner)
{
    public override async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(
        DecisionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await base.EvaluateAsync(request, cancellationToken);
        log.Record(request.Definition, result);
        return result;
    }
}
```
<!-- endSnippet -->

`DelegatingDecisionClient` does the rest:

- **`GetService`** returns the stage itself when it is of the type asked for, and asks the client inside otherwise.
- **`Dispose`** disposes the client inside. Override `Dispose(bool)` to release something of the stage's own, and call
  the base.
- **A stage that overrides nothing costs nothing.** The base returns the inner call's own task, and the
  `PassThroughStage` allocation gate holds it to 0 B.

A stage sees only `DecisionRequest`, `DecisionResponse` and `DecisionError`, never HTTP, so the same stage works over
any transport. Where you add it decides what it sees. Added after `UseRetries`, as in the custom pipeline above, it
sees every attempt, and a retry has `RetryAttempt` above 0. Added before `UseRetries`, it sees one call and its final
result.

The `DecisionResponse` a stage sees carries, beside the answers, `Model`, the model that answered; `Usage`, the tokens and
cost the provider reported, or `null` when it reported none; `Id`, the provider's response id; `UpstreamProvider`, the host
an aggregator such as OpenRouter routed the call to; and `IsCached`, which a cache stage sets on a response it replays.
A stage that rebuilds a response can set `Id`, `UpstreamProvider` and `IsCached` with an object initializer.

An `async` override allocates a state machine when the inner call completes asynchronously, as a real network call
does. That is usually fine. The library's own stages avoid it: each checks whether the inner call has already
completed, with `IsCompletedSuccessfully`, and only awaits when it has not.

For a one-off stage, pass a delegate to `Use` instead of writing a class. It receives the request, the client inside
and the cancellation token.

<!-- snippet: Pipeline_Delegate -->
```cs
// A one-off stage from a delegate: it times each call and passes the result on unchanged.
public static IDecisionClient Timed(IDecisionClient client, Action<TimeSpan> record)
    => client.AsBuilder()
        .Use(async (request, inner, cancellationToken) =>
        {
            var started = Stopwatch.GetTimestamp();
            var result = await inner.EvaluateAsync(request, cancellationToken);
            record(Stopwatch.GetElapsedTime(started));
            return result;
        })
        .Build();
```
<!-- endSnippet -->

## Finding a stage or the transport

`GetService` looks a type up along the pipeline, from the outside in. The typed `GetService<T>()` extension casts the
result for you, and returns `null` when nothing in the pipeline is a `T`. `GetRequiredService<T>()` does the same and
throws an `InvalidOperationException` naming the type instead, for a service the code cannot work without.

<!-- snippet: Pipeline_GetService -->
```cs
public static async Task<string> DescribeAsync(IDecisionClient client, CancellationToken cancellationToken)
{
    // The provider, the endpoint and the default model, which the SystemOneDecisionClient at the bottom provides.
    var metadata = client.GetService<DecisionClientMetadata>();

    // A stage, found by its type: null when the pipeline has none, as with UseStandardPipeline off.
    var retries = client.GetService<RetryingDecisionClient>();

    // The raw System One calls are on the concrete SystemOneDecisionClient, not on IDecisionClient.
    var models = await client.GetRequiredService<SystemOneDecisionClient>().ListModelsAsync(cancellationToken);

    var listed = models.IsSuccess ? $"{models.Value.Models.Count} models" : models.Error.Kind.ToString();
    return $"{metadata?.ProviderName} at {metadata?.Endpoint}, retries {(retries is null ? "off" : "on")}, {listed}";
}
```
<!-- endSnippet -->

A `SystemOneDecisionClient` answers with:

- **itself**, for `SystemOneDecisionClient` or `IDecisionClient`. This is how code that holds an `IDecisionClient`
  reaches the raw System One calls, which are not on the interface.
- **a copy of its retry settings**, for `DecisionRetryOptions`. `UseRetries()` starts from these.
- **its `DecisionClientMetadata`**: the `ProviderName`, which is `typesafe`, `openrouter`, `vllm-sr` or
  `custom` (an `OpenAIDecisionClient` reports `openai`), the `Endpoint` it sends to (the base address plus the path,
  such as `https://api.typesafe.ai/v1/systemone`), and the `DefaultModel` from its options. The telemetry and logging
  stages read it.
- **a stage of its standard pipeline**, such as `RetryingDecisionClient`, when there is one.

A [fake `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) can return `null` for everything. The
stages then report the provider and the model as `unknown`.

## With dependency injection

`AddSystemOneDecisionClient` returns a `SystemOneDecisionClientServiceBuilder`. It is a `DecisionClientServiceBuilder`,
which is a `DecisionClientBuilder` for the registered client with the registration's `Name`, or `null` for the default
client. `SystemOneDecisionClientServiceBuilder` adds `HttpClient`, the `IHttpClientBuilder` of the client's
`HttpClient`. The neutral `AddDecisionClient(factory)` returns the `DecisionClientServiceBuilder` and has no
`HttpClient`.

<!-- snippet: Pipeline_DI -->
```cs
public static void AddAuditedDecision(IServiceCollection services, IConfiguration configuration)
{
    services.AddTransient<TraceHeaderHandler>();
    var decision = services.AddSystemOneDecisionClient(configuration.GetSection("Minos"));

    // Handlers go on the HttpClient builder, and see every attempt, retries included.
    decision.HttpClient.AddHttpMessageHandler<TraceHeaderHandler>();

    // Stages wrap the registered client, outside its standard pipeline, and can read the container's services.
    decision.Use((inner, provider) => new AuditStage(inner, provider.GetRequiredService<IAuditLog>()));
}
```
<!-- endSnippet -->

- **Keep the result of `AddSystemOneDecisionClient`.** `Use` and the `Use…` extensions return the base
  `DecisionClientBuilder`, which has no `HttpClient`, so reach `HttpClient` from the registration's own builder, as the
  snippet does.
- **Stages wrap the registered `SystemOneDecisionClient`,** which still runs its standard pipeline. So a stage you add
  sits outside the span and the retries, and sees one call and its final result.
- **The pipeline is built once,** when the container first resolves the client, and `Build` gets the container. So
  `UseLogging()` takes the container's `ILoggerFactory`, and `UseRetries()` its `ILoggerFactory` and `TimeProvider`.
- **A repeat call for the same name returns the same builder,** so stages from either call land on one pipeline.
- **With `UseStandardPipeline` off,** for example from configuration, the registered client is the bare transport. Add
  the stages you want, and `UseRetries()` starts from the registration's own retry settings, configuration included.
- **Your own `IDecisionClient` still wins.** If you registered one first, `AddSystemOneDecisionClient` does not replace
  it, and the builder's stages are not used.

[Dependency injection](dependency-injection.md) covers the rest of the registration.

## Upgrading to 1.0

1.0 freezes the public API. The last 0.x release renamed and tightened it, and kept no obsolete aliases. The compiler
finds every rename and removal in the first table. The second list is the behaviour that changed without a compile
error, so read it before you deploy. The left column holds the names of 0.12 and earlier.

| In 0.12 | In 1.0 |
| --- | --- |
| `SystemOneClient`, `SystemOneClientOptions`, `SystemOneClientServiceBuilder` and `AddSystemOneClient`, all six overloads | `SystemOneDecisionClient`, `SystemOneDecisionClientOptions`, `SystemOneDecisionClientServiceBuilder` and `AddSystemOneDecisionClient`. Configuration keys bind property names and do not change |
| `[Criteria("…")]` on a question property | `[Criterion("…")]` |
| The `Answers` class | `QuestionSetAnswers` |
| `QuestionAnswer.Value` | `Probability` for a Noul answer and `Expected` for a Score answer. The `Noul` and `Score` factories name their parameters `probability` and `expected` |
| `DecisionResponse.Provider` | `DecisionResponse.UpstreamProvider` |
| `SystemOneResponse.Provider` | `SystemOneResponse.UpstreamProvider`. The wire name is unchanged |
| `new DecisionRequest(Definition: …, State: …)` with named arguments | `new DecisionRequest(definition: …, state: …)` |
| `DecisionContent.IsString` | Removed. Call `TryGetString(out var text)`, which also gives the text |
| `DecisionCapabilities.Images` and `ModelListing` | `SupportsImages` and `SupportsModelListing`. The [configuration](providers.md#capabilities) keys change too: `Capabilities:Images` is `Capabilities:SupportsImages`, and `Capabilities:ModelListing` is `Capabilities:SupportsModelListing` |
| The constructors of `Choice<T>`, `Score<T>`, `KeyedChoice` and `KeyedScore` | Internal. Test a helper through a [fake `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) that returns the answers |
| `DecisionUsage.InputTokens` and `OutputTokens` are `int` | `int?`. A count the provider did not report is `null` |
| `SystemOneDefaults.ApiKeyEnvironmentVariable` and `BaseAddressEnvironmentVariable` | `TypeSafeApiKeyEnvironmentVariable` and `TypeSafeBaseAddressEnvironmentVariable`. The variable names are unchanged |
| `CachingDecisionClient`, `DecisionCacheOptions`, `UseCaching`, `RateLimitingDecisionClient` and `UseRateLimiting`, in `Minos.Caching` and `Minos.RateLimiting` | The same names, in `Minos`. Remove the `using Minos.Caching;` and `using Minos.RateLimiting;` lines |

What the compiler does not find:

- **Recompile anything built against `ListModelsAsync`.** The optional token became an overload pair,
  `ListModelsAsync()` and `ListModelsAsync(CancellationToken)`. The call `ListModelsAsync(cancellationToken: token)` still
  compiles.
- **The cache keys change, so a distributed cache starts cold.** `DecisionClientMetadata.Endpoint` is now the base address
  plus the path the client posts to, and the key holds it. A `KeyPrefix` set per client to keep two paths apart, the
  workaround for issue 175, is no longer needed. See [Caching decisions](#caching-decisions).
- **A malformed raw `SystemOneRequest` throws `ArgumentException` before it is sent.** It used to come back as
  `InvalidResponse`. See [the raw request API](client-and-errors.md#the-raw-request-api).
- **A raw `SystemOneRequest` with no `Model` sends the client's configured model,** not `jev-latest`.
- **`DecisionResponse.Usage` is set when any count or the cost is reported.** A count the provider left out is `null`,
  and a raw `SystemOneResponse` whose `usage` holds only some counts now succeeds.
- **A registered client's built-in retries wait on the container's `TimeProvider`,** for the OpenAI and the System One
  clients alike. Register a `FakeTimeProvider` and a test no longer waits in real time.
- **`Expiration` and `LocalCacheExpiration` above 365 days throw `ArgumentOutOfRangeException`** when they are set.
- **A disposed caching or rate-limiting stage throws `ObjectDisposedException`** from `EvaluateAsync`.
- **New, and on by default: `DecisionCacheOptions.UseDistributedCache`.** Turn it off when the app's `IDistributedCache`
  serves something else, such as session state. See [Caching decisions](#caching-decisions).

## Upgrading from 0.8

The System One client moved out of `Minos.NET` into its own package, `Minos.NET.SystemOne`, so that `Minos.NET` and
`Minos.NET.DependencyInjection` know no provider. Types were renamed with it, and the registration split in two. The
compiler finds every one of these. Typed calls, `[Questions]` records and fakes of `IDecisionClient` compile unchanged.
The After column uses current names; [Upgrading to 1.0](#upgrading-to-10) has the renames since.

| In 0.8 | After |
| --- | --- |
| The System One client is in `Minos.NET` | It is in `Minos.NET.SystemOne`, which brings `Minos.NET`. Install it with `dotnet add package Minos.NET.SystemOne` |
| `DecisionClient`, `DecisionClientOptions`, `DecisionProvider` and `DecisionDefaults`, in the `Minos` namespace | `SystemOneDecisionClient`, `SystemOneDecisionClientOptions`, `SystemOneProvider` and `SystemOneDefaults`, in `Minos.SystemOne` |
| `SystemOneRequest`, `SystemOneResponse`, `ModelList`, `ModelCard` and the question and answer classes, in `Minos` | The same names, in `Minos.SystemOne`. Add `using Minos.SystemOne;` |
| `AddDecisionClient()`, with options, a configuration section or a name | `AddSystemOneDecisionClient()`, with the same six overloads. `AddDecisionClient` now takes only a factory |
| `DecisionClientServiceBuilder.HttpClient` | `SystemOneDecisionClientServiceBuilder.HttpClient`. `DecisionClientServiceBuilder` is the provider-neutral base, without `HttpClient` |
| `Model` is a `string` that defaults to `jev-latest` | `Model` is a `string?` that defaults to `null`, which takes the preset's default: `jev-latest` for TypeSafe and OpenRouter. The new presets have none, so the options need a `Model` for them |
| The raw calls log the provider as `TypeSafe` or `OpenRouter` in events 1001, 1004 and 1005 | They log its telemetry name, `typesafe` or `openrouter`, as the typed calls do. The new presets log `vllm-sr` and `custom` |
| `ListModelsAsync` on a provider without model listing returns `Unsupported` with "Model listing is only available on TypeSafe's API; OpenRouter has its own Models API." | The message is "Model listing is not available on {provider}.", for every provider whose `SupportsModelListing` capability is off |
| A Choice question with more than 255 options is sent, with only the MIN005 warning at build time | On TypeSafe and OpenRouter it returns `Unsupported` with MIN203 before sending. `Capabilities.MaxChoiceOptions` can raise the limit |
| `Minos.NET.DependencyInjection` brings `Microsoft.Extensions.Http` and `Microsoft.Extensions.Options.ConfigurationExtensions` | It no longer does. `Minos.NET.SystemOne` brings them, so code that used them without installing `Minos.NET.SystemOne` adds the package references itself |

`SystemOneDecisionClientOptions` also gains `Path` and `Capabilities`, and `SystemOneProvider` gains
`VllmSemanticRouter` and `Custom`, all described on the
[Providers](providers.md#the-presets) page. A set that breaks a provider's
[capabilities](providers.md#capabilities) returns an `Unsupported` failure, with `Failures` filled in, before
any request is sent.

## Upgrading from 0.7

0.8 makes `IDecisionClient` one neutral call, which breaks code that implements the interface or uses what
`AddDecisionClient` returned. Code that only calls the typed overloads compiles unchanged. The names in the 0.7 column
are the ones of that release, and the replacement column uses current names, such as `SystemOneDecisionClient` where 0.8
said `DecisionClient`; [Upgrading from 0.8](#upgrading-from-08) and
[Upgrading to 1.0](#upgrading-to-10) have the later renames.

| In 0.7 | From 0.8 |
| --- | --- |
| `client.EvaluateAsync(SystemOneRequest)` and `client.ListModelsAsync()` on an `IDecisionClient` | `client.GetRequiredService<SystemOneDecisionClient>().EvaluateAsync(...)` and `.ListModelsAsync(...)`, or hold a `SystemOneDecisionClient` |
| `client.EvaluateAsync<T>(state)`, a default interface method or a `DecisionClient` member | The same call, now an extension method in `DecisionClientExtensions`, in the `Minos` namespace |
| A fake implements `EvaluateAsync(SystemOneRequest)` and `ListModelsAsync` | A fake implements `EvaluateAsync(DecisionRequest)`, `GetService` and `Dispose`, and returns a `DecisionResponse` built with its public constructor |
| `AddDecisionClient` returns `IHttpClientBuilder` | It returns a builder whose `HttpClient` property is the old builder; `AddSystemOneDecisionClient` now returns `SystemOneDecisionClientServiceBuilder`; [0.9](#upgrading-from-08) split it out of `AddDecisionClient` |
| `IDecisionClient` is not disposable | `IDecisionClient` derives from `IDisposable` |
| `EvaluateUtf8Async<T>` writes the state into the request before it returns | It copies the bytes once when you call it, the state's length plus 24 B, and a retry sends that copy, so you may still reuse the buffer as soon as the call returns |

A fake is now three members. This one answers a set of one Noul question:

<!-- snippet: Pipeline_Fake -->
```cs
using Minos;
using ZeroAlloc.Results;

// What a fake implements: one EvaluateAsync over a DecisionRequest, GetService and Dispose.
public sealed class AlwaysUrgent : IDecisionClient
{
    // One Noul answer of 0.9, for a set of one Noul question such as PipelineCheck.
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(request.Definition, [QuestionAnswer.Noul(0.9)])));

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
```
<!-- endSnippet -->

`QuestionAnswer.Noul`, `QuestionAnswer.Choice` and `QuestionAnswer.Score` make the answers, one per question in the
order the set declares them. A Noul answer holds its `Probability` and a Score answer its `Expected` level. The
`DecisionResponse` constructor checks them against the definition and throws `ArgumentException` when they do not match.
[Testing your code](testing-your-code.md#way-one-a-fake-idecisionclient) has a fuller fake. A mock works the same way:
set up `EvaluateAsync(DecisionRequest, CancellationToken)`, because the typed calls are extension methods, and a mocking
library cannot intercept those.

Two observability values change, because the pipeline sees only a `DecisionRequest`:

- **`minos.operation` and the log's `{Operation}` are `evaluate-set`** for every typed and built-set call. They were
  `evaluate-typed` and `evaluate-built-set`. The raw calls keep `evaluate` and `list-models`.
- **Event 1001 logs `{Provider}` as the metadata's provider name**, such as `typesafe`, where it logged `TypeSafe`.
  The raw calls logged `TypeSafe` or `OpenRouter` in 0.8; [later versions](#upgrading-from-08) log the same name as the
  typed calls.

The span of a typed or built-set call also gains `gen_ai.response.id` and `minos.usage.cost` when the provider reports
them, which only the raw call recorded before.

## Next

- [Logging, traces and metrics](observability.md): what each stage emits.
- [Testing your code](testing-your-code.md): fakes and canned replies for code that calls Jev.
- [Performance](performance.md#phase-63--the-client-pipeline): what the pipeline costs per call.
