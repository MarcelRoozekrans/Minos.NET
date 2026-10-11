---
id: pipeline
title: The client pipeline
sidebar_position: 7
description: The stages every call goes through (telemetry, logging and retries), building your own pipeline, writing a stage, turning the standard pipeline off, stages with dependency injection, and upgrading from 0.7 and 0.8.
---

# The client pipeline

Every evaluation goes through a **pipeline**: a chain of stages around a transport that sends the request. Each stage
is an `IDecisionClient` that wraps the one inside it. It can act before the call, after it, or both, and then passes
the call on. Telemetry, logging and retries are stages like this, and you can add your own, such as an audit record or
a cache.

This page shows the standard pipeline that every `SystemOneClient` runs, how to build your own, how to write a stage,
how to find a stage again, and how stages fit with dependency injection. It ends with what changed for code written
against 0.7.

## What every call goes through

`IDecisionClient` has one call: `EvaluateAsync(DecisionRequest, CancellationToken)`. A `DecisionRequest` holds the
question set's `Definition`, the `State`, an optional `Model` and the `RetryAttempt`. It returns a `Result` of a
`DecisionResponse` or a [`DecisionError`](client-and-errors.md#errors). The typed `EvaluateAsync<T>` calls and the
[built question set](question-sets-at-run-time.md) calls are extension methods that build a `DecisionRequest` and make
that one call, so they go through the pipeline too.

A `SystemOneClient` builds the standard pipeline for you, so a client you create is already traced and retried, and
logged when you give it a logger factory.

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
    // A SystemOneClient already runs the standard pipeline: one span and one set of metrics per call, one log record
    // when it was given a logger factory, and up to two retries of a transient failure. There is nothing to set up.
    public static async Task<bool> IsUrgentAsync(
        HttpClient http, string apiKey, string message, CancellationToken cancellationToken)
    {
        using var client = new SystemOneClient(http, new SystemOneClientOptions { ApiKey = apiKey });

        // The typed call builds a DecisionRequest from PipelineCheck.Definition and the message, and sends it through
        // the pipeline.
        var result = await client.EvaluateAsync<PipelineCheck>(message, cancellationToken);
        return result.IsSuccess && result.Value.IsUrgent.Value;
    }
}
```
<!-- endSnippet -->

The standard stages run in this order, outermost first:

| Stage | Class | What it does |
| --- | --- | --- |
| Telemetry | `OpenTelemetryDecisionClient` | Opens one span and records one set of metrics for the whole call. |
| Logging | `LoggingDecisionClient` | Logs the outcome once: event 1001, 1002 or 1006. Only when the client has a logger factory. |
| Retries | `RetryingDecisionClient` | Retries a transient failure with a growing wait, and logs event 1003 for each retried attempt. |
| Transport | internal | Sends one attempt over HTTP and reads the reply. |

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

Set `SystemOneClientOptions.UseStandardPipeline` to `false`, and the client is the bare transport: one attempt per call,
with no span, no log and no retry. Then build the pipeline you want around it. `AsBuilder()` starts a
`DecisionClientBuilder` from any client, each `Use…` call adds a stage, and `Build()` returns the outermost one.

<!-- snippet: Pipeline_Custom -->
```cs
// The standard stages, in the standard order, with four retries and an audit stage of your own.
public static IDecisionClient Build(HttpClient http, string apiKey, ILoggerFactory loggerFactory, IAuditLog log)
{
    // With the standard pipeline off, the client is the bare transport: one attempt per call, and no span or log.
    var transport = new SystemOneClient(
        http, new SystemOneClientOptions { ApiKey = apiKey, UseStandardPipeline = false });

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
  `SystemOneClient` offers from its options, and `configure` changes them. It logs event 1003 through the
  `ILoggerFactory` of the services passed to `Build`, and waits on their `TimeProvider`, so `Build()` without services,
  as above, retries without logging 1003.
- **`Build(services)`** creates the stages, inner first. Pass an `IServiceProvider` when a stage needs services.

The retry stage takes a `DecisionRetryOptions`:

| Property | Default | What it does |
| --- | --- | --- |
| `MaxRetries` | 2 | How many times a failed call is tried again. 0 turns retries off. |
| `InitialBackoff` | 500 ms | The first wait. It doubles for each further retry. |
| `MaxRetryDelay` | 30 seconds | The longest wait, for the backoff and for a server's `Retry-After` alike. |
| `Jitter` | `true` | Adds a random extra to each backoff wait, so many clients do not retry in step. |
| `ShouldRetry` | `IsTransient` | Which failures to retry. `DecisionRetryOptions.IsTransient` accepts the kinds [Retries](client-and-errors.md#retries) lists. |

A `Disposed` failure is never retried, whatever `ShouldRetry` says, and no retry starts after the stage is disposed.

Two things to keep in mind when you turn the standard pipeline off:

- **The raw System One calls do not retry either.** They are still traced, and logged when the client has a logger
  factory, but a failed `EvaluateAsync(SystemOneRequest)` or `ListModelsAsync` is returned after one attempt.
- **Do not add a second retry stage over the standard pipeline.** With `UseStandardPipeline` left on, the client
  already retries, and a `UseRetries()` around it multiplies the attempts.

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
result for you, and returns `null` when nothing in the pipeline is a `T`.

<!-- snippet: Pipeline_GetService -->
```cs
public static async Task<string> DescribeAsync(IDecisionClient client, CancellationToken cancellationToken)
{
    // The provider, the endpoint and the default model, which the SystemOneClient at the bottom provides.
    var metadata = client.GetService<DecisionClientMetadata>();

    // A stage, found by its type: null when the pipeline has none, as with UseStandardPipeline off.
    var retries = client.GetService<RetryingDecisionClient>();

    // The raw System One calls are on the concrete SystemOneClient, not on IDecisionClient.
    var models = await client.GetService<SystemOneClient>()!.ListModelsAsync(cancellationToken);

    var listed = models.IsSuccess ? $"{models.Value.Models.Count} models" : models.Error.Kind.ToString();
    return $"{metadata?.ProviderName} at {metadata?.Endpoint}, retries {(retries is null ? "off" : "on")}, {listed}";
}
```
<!-- endSnippet -->

A `SystemOneClient` answers with:

- **itself**, for `SystemOneClient` or `IDecisionClient`. This is how code that holds an `IDecisionClient` reaches the
  raw System One calls, which are not on the interface.
- **a copy of its retry settings**, for `DecisionRetryOptions`. `UseRetries()` starts from these.
- **its `DecisionClientMetadata`**: the `ProviderName`, which is `typesafe`, `openrouter`, `vllm-sr` or
  `custom` (an `OpenAIDecisionClient` reports `openai`), the `Endpoint` it sends to, and the `DefaultModel` from its options. The telemetry and logging stages read
  it.
- **a stage of its standard pipeline**, such as `RetryingDecisionClient`, when there is one.

A [fake `IDecisionClient`](testing-your-code.md#way-one-a-fake-idecisionclient) can return `null` for everything. The
stages then report the provider and the model as `unknown`.

## With dependency injection

`AddSystemOneClient` returns a `SystemOneClientServiceBuilder`. It is a `DecisionClientServiceBuilder`, which is a
`DecisionClientBuilder` for the registered client with the registration's `Name`, or `null` for the default client.
`SystemOneClientServiceBuilder` adds `HttpClient`, the `IHttpClientBuilder` of the client's `HttpClient`. The neutral
`AddDecisionClient(factory)` returns the `DecisionClientServiceBuilder` and has no `HttpClient`.

<!-- snippet: Pipeline_DI -->
```cs
public static void AddAuditedDecision(IServiceCollection services, IConfiguration configuration)
{
    services.AddTransient<TraceHeaderHandler>();
    var decision = services.AddSystemOneClient(configuration.GetSection("Minos"));

    // Handlers go on the HttpClient builder, and see every attempt, retries included.
    decision.HttpClient.AddHttpMessageHandler<TraceHeaderHandler>();

    // Stages wrap the registered client, outside its standard pipeline, and can read the container's services.
    decision.Use((inner, provider) => new AuditStage(inner, provider.GetRequiredService<IAuditLog>()));
}
```
<!-- endSnippet -->

- **Keep the result of `AddSystemOneClient`.** `Use` and the `Use…` extensions return the base
  `DecisionClientBuilder`, which has no `HttpClient`, so reach `HttpClient` from the registration's own builder, as the
  snippet does.
- **Stages wrap the registered `SystemOneClient`,** which still runs its standard pipeline. So a stage you add sits
  outside the span and the retries, and sees one call and its final result.
- **The pipeline is built once,** when the container first resolves the client, and `Build` gets the container. So
  `UseLogging()` takes the container's `ILoggerFactory`, and `UseRetries()` its `ILoggerFactory` and `TimeProvider`.
- **A repeat call for the same name returns the same builder,** so stages from either call land on one pipeline.
- **With `UseStandardPipeline` off,** for example from configuration, the registered client is the bare transport. Add
  the stages you want, and `UseRetries()` starts from the registration's own retry settings, configuration included.
- **Your own `IDecisionClient` still wins.** If you registered one first, `AddSystemOneClient` does not replace it, and
  the builder's stages are not used.

[Dependency injection](dependency-injection.md) covers the rest of the registration.

## Upgrading from 0.8

The System One client moved out of `Minos.NET` into its own package, `Minos.NET.SystemOne`, so that `Minos.NET` and
`Minos.NET.DependencyInjection` know no provider. Types were renamed with it, and the registration split in two. The
compiler finds every one of these. Typed calls, `[Questions]` records and fakes of `IDecisionClient` compile unchanged.

| In 0.8 | After |
| --- | --- |
| The System One client is in `Minos.NET` | It is in `Minos.NET.SystemOne`, which brings `Minos.NET`. Install it with `dotnet add package Minos.NET.SystemOne` |
| `DecisionClient`, `DecisionClientOptions`, `DecisionProvider` and `DecisionDefaults`, in the `Minos` namespace | `SystemOneClient`, `SystemOneClientOptions`, `SystemOneProvider` and `SystemOneDefaults`, in `Minos.SystemOne` |
| `SystemOneRequest`, `SystemOneResponse`, `ModelList`, `ModelCard` and the question and answer classes, in `Minos` | The same names, in `Minos.SystemOne`. Add `using Minos.SystemOne;` |
| `AddDecisionClient()`, with options, a configuration section or a name | `AddSystemOneClient()`, with the same six overloads. `AddDecisionClient` now takes only a factory |
| `DecisionClientServiceBuilder.HttpClient` | `SystemOneClientServiceBuilder.HttpClient`. `DecisionClientServiceBuilder` is the provider-neutral base, without `HttpClient` |
| `Model` is a `string` that defaults to `jev-latest` | `Model` is a `string?` that defaults to `null`, which takes the preset's default: `jev-latest` for TypeSafe and OpenRouter. The new presets have none, so the options need a `Model` for them |
| The raw calls log the provider as `TypeSafe` or `OpenRouter` in events 1001, 1004 and 1005 | They log its telemetry name, `typesafe` or `openrouter`, as the typed calls do. The new presets log `vllm-sr` and `custom` |
| `ListModelsAsync` on a provider without model listing returns `Unsupported` with "Model listing is only available on TypeSafe's API; OpenRouter has its own Models API." | The message is "Model listing is not available on {provider}.", for every provider whose `ModelListing` capability is off |
| A Choice question with more than 255 options is sent, with only the MIN005 warning at build time | On TypeSafe and OpenRouter it returns `Unsupported` with MIN203 before sending. `Capabilities.MaxChoiceOptions` can raise the limit |
| `Minos.NET.DependencyInjection` brings `Microsoft.Extensions.Http` and `Microsoft.Extensions.Options.ConfigurationExtensions` | It no longer does. `Minos.NET.SystemOne` brings them, so code that used them without installing `Minos.NET.SystemOne` adds the package references itself |

`SystemOneClientOptions` also gains `Path` and `Capabilities`, and `SystemOneProvider` gains
`VllmSemanticRouter` and `Custom`, all described on the
[client page](client-and-errors.md#providers). A set that breaks a provider's
[capabilities](client-and-errors.md#capabilities) returns an `Unsupported` failure, with `Failures` filled in, before
any request is sent.

## Upgrading from 0.7

0.8 makes `IDecisionClient` one neutral call, which breaks code that implements the interface or uses what
`AddDecisionClient` returned. Code that only calls the typed overloads compiles unchanged. The 0.8 names in this table
are the ones of that release; [Upgrading from 0.8](#upgrading-from-08) has the later renames.

| In 0.7 | From 0.8 |
| --- | --- |
| `client.EvaluateAsync(SystemOneRequest)` and `client.ListModelsAsync()` on an `IDecisionClient` | `client.GetService<DecisionClient>()!.EvaluateAsync(...)` and `.ListModelsAsync(...)`, or hold a `DecisionClient` |
| `client.EvaluateAsync<T>(state)`, a default interface method or a `DecisionClient` member | The same call, now an extension method in `DecisionClientExtensions`, in the `Minos` namespace |
| A fake implements `EvaluateAsync(SystemOneRequest)` and `ListModelsAsync` | A fake implements `EvaluateAsync(DecisionRequest)`, `GetService` and `Dispose`, and returns a `DecisionResponse` built with its public constructor |
| `AddDecisionClient` returns `IHttpClientBuilder` | It returns a builder whose `HttpClient` property is the old builder; [0.9](#upgrading-from-08) renamed it to `AddSystemOneClient` and `SystemOneClientServiceBuilder` |
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
order the set declares them. The `DecisionResponse` constructor checks them against the definition and throws
`ArgumentException` when they do not match. [Testing your code](testing-your-code.md#way-one-a-fake-idecisionclient)
has a fuller fake. A mock works the same way: set up `EvaluateAsync(DecisionRequest, CancellationToken)`, because the
typed calls are extension methods, and a mocking library cannot intercept those.

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
