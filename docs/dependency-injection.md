---
id: dependency-injection
title: Dependency injection
sidebar_position: 7
description: Register IDecisionClient in a .NET host with AddSystemOneDecisionClient, key several clients, bind options from configuration, and check them at start-up.
---

# Dependency injection

In an application built on the .NET generic host, such as an ASP.NET Core site or a worker service, you do not create
the client yourself. You register it once, and the container hands it to every class that asks for an `IDecisionClient`.
The registering is done by two packages. `Minos.NET.SystemOne` has `AddSystemOneDecisionClient`, which registers a
`SystemOneDecisionClient` over `IHttpClientFactory`, reads its options from code or from configuration, and checks them
when the host starts. It depends on `Minos.NET.DependencyInjection`, the provider-neutral part, so one install covers
both:

```shell
dotnet add package Minos.NET.SystemOne
```

The extension methods live in the `Microsoft.Extensions.DependencyInjection` namespace, so no extra `using` is needed
beyond the one you already have for the container. [OpenAI](#openai) registers OpenAI's client from
`Minos.NET.OpenAI` the same way, and [the neutral `AddDecisionClient`](#the-neutral-adddecisionclient) is for any other
client.

## Register a client

A class asks for `IDecisionClient` in its constructor. Here is a small one that judges a message as urgent or not.

<!-- snippet: DependencyInjection_Consumer -->
```cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minos;
using Minos.SystemOne;

// One yes/no question, so the example stays short.
[Questions]
public partial record InboxCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

// A class asks for IDecisionClient in its constructor, and the container supplies the shared client.
public sealed class InboxTriage(IDecisionClient client)
{
    public async Task<string> TriageAsync(string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<InboxCheck>(message, cancellationToken);
        if (result.IsFailure)
        {
            return $"The call failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent" : "can wait";
    }
}
```
<!-- endSnippet -->

`AddSystemOneDecisionClient` registers the client. The overload with an `Action<SystemOneDecisionClientOptions>` sets
the options in code. Here the key comes from configuration, such as user secrets, so it is never written into the
program.

<!-- snippet: DependencyInjection_Register -->
```cs
// The key is read from configuration, such as user secrets, and never written into the code.
public static SystemOneDecisionClientServiceBuilder AddDecision(IHostApplicationBuilder builder)
{
    builder.Services.AddSingleton<InboxTriage>();
    return builder.Services.AddSystemOneDecisionClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
}
```
<!-- endSnippet -->

After that, the container builds an `InboxTriage` with the shared client whenever one is needed, and calls
`TriageAsync` as before.

`AddSystemOneDecisionClient` returns a `SystemOneDecisionClientServiceBuilder`, the registration's pipeline builder. Its
`HttpClient` property is the `IHttpClientBuilder` of the client's `HttpClient`, which is where handlers are added, as
[below](#the-httpclient-from-the-factory) shows. Its `Use` methods add stages around the client, as [The client
pipeline](pipeline.md#with-dependency-injection) shows.

## The six overloads

`AddSystemOneDecisionClient` has six overloads. Three register the default client, and three register a client under a
name.

| Overload | Registers | Options come from |
| --- | --- | --- |
| `AddSystemOneDecisionClient()` | the default client | the defaults and the environment variables |
| `AddSystemOneDecisionClient(Action<SystemOneDecisionClientOptions>)` | the default client | your delegate |
| `AddSystemOneDecisionClient(IConfiguration)` | the default client | a configuration section |
| `AddSystemOneDecisionClient(string name)` | a keyed client | the defaults and the environment variables |
| `AddSystemOneDecisionClient(string name, Action<SystemOneDecisionClientOptions>)` | a keyed client | your delegate |
| `AddSystemOneDecisionClient(string name, IConfiguration)` | a keyed client | a configuration section |

"The defaults and the environment variables" are the ones from [the client page](client-and-errors.md#options): the key
comes from `TYPESAFE_API_KEY`, the base address from `TYPESAFE_BASE_URL` when it is set, and everything else has its
default.

<!-- snippet: DependencyInjection_FromEnvironment -->
```cs
// With no options at all, the key comes from TYPESAFE_API_KEY, the base address from TYPESAFE_BASE_URL when set,
// and everything else from the defaults.
public static void AddFromEnvironment(IServiceCollection services)
{
    services.AddSystemOneDecisionClient();
    services.AddSystemOneDecisionClient("backup");
}
```
<!-- endSnippet -->

The `name` must not be empty. It is both the service key and the name of the options.

## Several clients

An application sometimes needs more than one client, for example one for TypeSafe and one for a server you run, or two
keys with different retry settings. Register each under a name, and ask for each by that name with
`[FromKeyedServices]`. Each client takes its own provider, so the two here need nothing in common.

<!-- snippet: DependencyInjection_Keyed -->
```cs
public static void AddKeyedClients(IHostApplicationBuilder builder)
{
    builder.Services.AddSystemOneDecisionClient("typesafe", options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    builder.Services.AddSystemOneDecisionClient("local", options =>
    {
        // A server you run: Custom has no default address or model, and sends no key unless ApiKey is set.
        options.Provider = SystemOneProvider.Custom;
        options.BaseAddress = new Uri("http://localhost:8080/");
        options.Model = "local-model";
    });
    builder.Services.AddSingleton<InboxRouter>();
}
```
<!-- endSnippet -->

<!-- snippet: DependencyInjection_KeyedConsumer -->
```cs
// A keyed client is asked for by its key.
public sealed class InboxRouter([FromKeyedServices("local")] IDecisionClient client)
{
    public async Task<string> TriageAsync(string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<InboxCheck>(message, cancellationToken);
        if (result.IsFailure)
        {
            return $"The call failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent, via the local server" : "can wait, via the local server";
    }
}
```
<!-- endSnippet -->

A keyed client is a separate singleton with its own options and its own `HttpClient`. A name you register does not
register the default client, so `IDecisionClient` without a key still fails to resolve unless you also called one of the
default overloads.

## Options from configuration

Instead of a delegate, you can bind a client's options from configuration, such as `appsettings.json`. The keys are the
`SystemOneDecisionClientOptions` property names, and every key is optional.

```json
{
  "Minos": {
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

Pass the section to `AddSystemOneDecisionClient`. The first call binds `Minos` to the default client, and the second
binds `OpenRouter` to a client keyed `openrouter`.

<!-- snippet: DependencyInjection_Configuration -->
```cs
// Each client reads its own section.
public static void AddFromConfiguration(IHostApplicationBuilder builder)
{
    builder.Services.AddSystemOneDecisionClient(builder.Configuration.GetSection("Minos"));
    builder.Services.AddSystemOneDecisionClient("openrouter", builder.Configuration.GetSection("OpenRouter"));
}
```
<!-- endSnippet -->

The keys for the other providers are `Path` and `Capabilities`, with the capabilities in a section of their
own. `QuestionKinds` is a comma-separated list. A capability you leave out keeps the [preset's
default](providers.md#the-defaults).

```json
{
  "Local": {
    "Provider": "Custom",
    "BaseAddress": "http://localhost:8080/",
    "Path": "api/v1/systemone",
    "Model": "local-model",
    "Capabilities": {
      "QuestionKinds": "Noul, Choice",
      "MaxQuestions": 16
    }
  }
}
```

<!-- snippet: DependencyInjection_ConfigurationProviders -->
```cs
// Provider, Path and Capabilities bind like any other option.
public static void AddProvidersFromConfiguration(IHostApplicationBuilder builder)
{
    builder.Services.AddSystemOneDecisionClient("local", builder.Configuration.GetSection("Local"));
}
```
<!-- endSnippet -->

`Local` sends no key at all.

Things to know when you bind:

- **Time spans** are written `hh:mm:ss`, with fractions of a second after a dot, as `InitialBackoff` shows.
- **Keep the key out of `appsettings.json`.** Leave `ApiKey` unset to use the provider's variable, such as
  `TYPESAFE_API_KEY`, or supply it from user secrets or an environment variable. The setting is `Minos:ApiKey`, and as
  an environment variable the colon becomes a double underscore, `Minos__ApiKey`.
- **A delegate registered later overrides bound values.** Calling `AddSystemOneDecisionClient(options => ...)` after
  binding changes only the properties it sets.
- **A value the binder cannot convert**, such as `"MaxRetries": "abc"`, fails with the binder's
  `InvalidOperationException`, not with an `OptionsValidationException`. A value that converts but is out of range, such
  as `"MaxRetries": 11`, is checked like any other, as the next section says.
- **Binding uses no reflection,** so it works under [Native AOT](native-aot.md) without any setup.
- **Changes are not picked up.** The client is a singleton that reads its options once, so a change to the configuration
  after it is built has no effect.

```shell
dotnet user-secrets set "Minos:ApiKey" "your-key"
```

## Checking the options at start-up

Each registration checks its options when the host starts, so a wrong value stops the program at launch instead of on
the first request. It runs `SystemOneDecisionClientOptions.Validate()`, the same check the client's constructor runs,
and the failure is an `OptionsValidationException` that carries the core's own message. That covers a missing API key,
an invalid base address, `MaxRetries` outside 0 to 10, a backoff out of range, a blank model, and an invalid `Timeout`.
It covers the provider settings too: a provider with no model, a missing `BaseAddress` for `Custom`, and a
`Capabilities` override that allows no question kind.

<!-- snippet: DependencyInjection_Startup -->
```cs
// Invalid options fail here, when the host starts, not on the first request.
public static async Task<string> TryStartAsync(IHost host)
{
    try
    {
        await host.StartAsync();
        return "started";
    }
    catch (OptionsValidationException exception)
    {
        return $"Minos is misconfigured: {string.Join(' ', exception.Failures)}";
    }
}
```
<!-- endSnippet -->

Without a host, with a plain `BuildServiceProvider()`, there is no start-up step, so the same exception comes when the
client is first resolved.

Some things still need valid options, an API key included:

- **Creating the client's named `HttpClient` from `IHttpClientFactory` yourself.** It reads the same options, so a call
  such as `CreateClient("Minos.NET")` fails with the same exception when they are invalid.
- **A test host that replaces the client.** If the application registers its own `IDecisionClient` before
  `AddSystemOneDecisionClient`, the application's client wins, but the options are still validated. A test that swaps
  the client needs a placeholder key, for example `AddSystemOneDecisionClient(options => options.ApiKey = "test")`, or a
  `Minos:ApiKey` setting.

## What gets registered

- **One singleton per registration.** It is a `SystemOneDecisionClient`, wrapped in any stages you added to the
  registration's builder, and is built once, when the container first resolves it. The client reads its named options
  then. The default client reads the default options, and a keyed client reads the options named by its key. The client
  logs through the container's `ILoggerFactory`, as [Logging, traces and metrics](observability.md#logging) describes.
- **A repeat call for the same name** adds its delegate, and the delegates run in order. It returns the same builder,
  so stages from either call land on one pipeline, and it does not register a second client.
- **Your own registration wins.** `AddSystemOneDecisionClient` registers the client with `TryAdd`, so an
  `IDecisionClient` you registered first stays. `AddSystemOneDecisionClient` still sets up the named `HttpClient` and
  the options in that case.
- **Disposal.** The container disposes the client with the provider. The `HttpClient` belongs to the factory and is left
  alone.

## The HttpClient from the factory

The client's `HttpClient` comes from `IHttpClientFactory`. It is named `Minos.NET`, or `Minos.NET:` followed by
the key for a keyed client. You rarely need the name, except to change what is behind it in a test.

- **Its handler.** The primary handler is a `SocketsHttpHandler` that recycles its connections every two minutes, so a
  change in DNS is picked up. The factory never rotates it, because the singleton keeps its `HttpClient` for life.
- **Its settings.** `SystemOneDecisionClient.ConfigureHttpClient` gives the `HttpClient` its base address, the
  per-attempt `Timeout` and the `Minos.NET` User-Agent.
- **Its handlers.** Add your own through the `HttpClient` property of the builder `AddSystemOneDecisionClient` returns.
  A handler sees every request and every retry. `ConfigureHttpClientDefaults` adds a handler to every `HttpClient` the
  factory makes, the Minos clients included, whether you call it before or after `AddSystemOneDecisionClient`.

<!-- snippet: DependencyInjection_Handlers -->
```cs
// A handler of your own sees every request the client sends, and every retry.
public sealed class TraceHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Trace", "inbox");
        return base.SendAsync(request, cancellationToken);
    }
}

public static class HandlerRegistration
{
    // The HttpClient property of the builder AddSystemOneDecisionClient returns is the client's HttpClient builder, so handlers
    // are added the usual way.
    public static IHttpClientBuilder AddTracedDecision(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        return services
            .AddSystemOneDecisionClient(options => options.ApiKey = apiKey)
            .HttpClient.AddHttpMessageHandler<TraceHeaderHandler>();
    }

    // ConfigureHttpClientDefaults adds a handler to every HttpClient the factory makes, the Minos clients included.
    public static void AddTracedEverywhere(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        services.AddSystemOneDecisionClient(options => options.ApiKey = apiKey);
    }

    // To keep a defaults handler off the Minos client, clear the handlers of its builder. This also clears any you
    // added through that builder, so add those inside the delegate, after the Clear.
    public static IHttpClientBuilder AddDecisionWithoutDefaultHandlers(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        return services
            .AddSystemOneDecisionClient(options => options.ApiKey = apiKey)
            .HttpClient.ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
    }

    // When a handler of yours retries, such as a standard resilience handler, turn the client's own retries off,
    // so the two do not multiply.
    public static void AddWithOwnRetries(IServiceCollection services, string apiKey)
        => services.AddSystemOneDecisionClient(options =>
        {
            options.ApiKey = apiKey;
            options.MaxRetries = 0;
        });
}
```
<!-- endSnippet -->

### Host-wide defaults

`ConfigureHttpClientDefaults` is how a host applies one setup to every `HttpClient`, and it reaches the Minos clients
too. Two things in it do not:

- **A primary handler set in the defaults is replaced.** The Minos client always uses its own `SocketsHttpHandler`,
  whether the defaults are registered before or after `AddSystemOneDecisionClient`. A defaults primary handler never
  sees the client's requests.
- **The defaults' loggers are removed** from the Minos clients, in either order, as the next section says.

The usual cause of trouble is a defaults handler that retries, most often Aspire ServiceDefaults'
`AddStandardResilienceHandler()`. Its retries multiply with the client's own, so set `MaxRetries = 0`, as the snippet
does and as [the cost of retrying](client-and-errors.md#the-cost-of-retrying) explains. It has time-outs of its own too,
and the advice is to keep them at or above `Timeout`. That last point is advice, not something the tests here check.

To keep a defaults handler off the Minos client, clear the additional handlers of its builder, as the last method in the
snippet does. This also removes every handler you added yourself through that builder, so add yours in the same call
with `ConfigureAdditionalHttpMessageHandlers`, after the `Clear()`, if you want them.

### The factory's request logs

The factory has request logs of its own, with the categories `System.Net.Http.HttpClient.*`. They are turned off for
the Minos clients. The client already logs each operation and each retried attempt, in the `Minos.DecisionClient`
category, and the factory's logging handlers add work to every request. Call `AddDefaultLogger()` on the `HttpClient`
property of the builder `AddSystemOneDecisionClient` returns to bring the factory's logs back.

Without a logging provider, or with the Minos log levels turned off, the client logs nothing. [Logging, traces and
metrics](observability.md#logging) lists the events and their levels.

## OpenAI

`Minos.NET.OpenAI` has `AddOpenAIDecisionClient`, which registers an `OpenAIDecisionClient` for OpenAI's Decisions API.
It works as `AddSystemOneDecisionClient` does: the same six overloads, default and keyed, options from code or
configuration, checked at start-up, over a named `HttpClient` from `IHttpClientFactory`. It returns an
`OpenAIDecisionClientServiceBuilder`, whose `HttpClient` property is the builder of that `HttpClient`.

```shell
dotnet add package Minos.NET.OpenAI
```

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

- **The key is required.** Without `ApiKey` the client reads `OPENAI_API_KEY`, and the host fails at start-up when
  neither is set. `BaseAddress` falls back on `OPENAI_BASE_URL`, then `https://api.openai.com/v1/`. It is the
  versioned API root, as in OpenAI's official SDKs: requests go to `decisions` under it.
- **Configuration binds the same way.** The keys are the property names of `OpenAIDecisionClientOptions`, such as
  `ApiKey`, `Model` and `Capabilities:QuestionKinds`.
- **A name holds one provider.** Registering the same name with `AddSystemOneDecisionClient` and
  `AddOpenAIDecisionClient` throws `InvalidOperationException`. Use different names, as the snippet does, to have both.
- **Its provider name is `openai`**, in its logs, spans and metrics.

[OpenAI](providers.md#openai) has the options, what OpenAI cannot express, and refusals.

## The neutral AddDecisionClient

`AddDecisionClient` is in `Minos.NET.DependencyInjection`, and it knows no provider. It takes a factory that makes the
`IDecisionClient`, and the container calls it once, when it first resolves the client. Use it for a client of your
own, a fake in a test host, or a client from another protocol package. It has two overloads,
`AddDecisionClient(factory)` for the default client and `AddDecisionClient(name, factory)` for a keyed one.

<!-- snippet: DependencyInjection_Factory -->
```cs
// AddDecisionClient takes a factory and knows no provider. Use it for a client of your own, or one from another
// protocol package. It returns the neutral DecisionClientServiceBuilder: Use stages, but no HttpClient property.
public static DecisionClientServiceBuilder AddOwnClient(IServiceCollection services)
    => services.AddDecisionClient(provider => new AlwaysUrgent());
```
<!-- endSnippet -->

It returns a `DecisionClientServiceBuilder`, which has `Use` and the `Use…` extensions for
[stages](pipeline.md#with-dependency-injection) and a `Name`, and nothing else. It has no `HttpClient` property,
because the factory may make a client with no `HttpClient`. That property, the named `HttpClient` from
`IHttpClientFactory`, the options and the start-up check belong to `AddSystemOneDecisionClient`, whose
`SystemOneDecisionClientServiceBuilder` derives from `DecisionClientServiceBuilder` and adds `HttpClient`.

A name holds one provider's registration. Registering the same name with `AddSystemOneDecisionClient` and with
`AddDecisionClient` throws `InvalidOperationException`, so two registrations cannot disagree about what the name is. A
repeat call of the same kind returns the first call's builder.

## Using Minos without the package

`AddSystemOneDecisionClient` is a convenience. `SystemOneDecisionClient.ConfigureHttpClient(httpClient, options)`
configures any `HttpClient` the way the client configures its own: it applies the per-attempt `Timeout`, the base
address when the `HttpClient` has none, and the User-Agent. It needs no API key and leaves the handler alone. Call it
before the client sends a request, for instance on a named client of your own.

<!-- snippet: DependencyInjection_WithoutPackage -->
```cs
// A named HttpClient of your own, set up the way AddSystemOneDecisionClient sets up its client.
public static void AddMinosHttpClient(IServiceCollection services, SystemOneDecisionClientOptions options)
    => services.AddHttpClient("minos")
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
        .ConfigureHttpClient(http => SystemOneDecisionClient.ConfigureHttpClient(http, options));

public static SystemOneDecisionClient Create(IHttpClientFactory factory, SystemOneDecisionClientOptions options)
    => new(factory.CreateClient("minos"), options);
```
<!-- endSnippet -->

The `SystemOneDecisionClient` that `Create` makes still needs an API key. `ConfigureHttpClient` sets up the `HttpClient`
only, so the key comes from `options.ApiKey` or the environment variable, as for any client.

The `SocketsHttpHandler` and the infinite handler lifetime do for this client what the package does for its own: a
long-lived `SystemOneDecisionClient` keeps one handler for life, so the connections recycle themselves and the factory
does not rotate the handler.

## Next

- [The client pipeline](pipeline.md): the stages around a registered client, and how to add your own.
- [Logging, traces and metrics](observability.md): what a registered client reports about each call.
- [Client and errors](client-and-errors.md): the options a registration sets, and every `DecisionError`.
- [Typed evaluation](typed-evaluation.md): the calls a resolved client makes.
- [Performance](performance.md): what a call costs, measured, including through dependency injection.
