---
id: dependency-injection
title: Dependency injection
sidebar_position: 6
description: Register IJevClient in a .NET host with AddJevClient, key several clients, bind options from configuration, and check them at start-up.
---

# Dependency injection

In an application built on the .NET generic host, such as an ASP.NET Core site or a worker service, you do not create
the client yourself. You register it once, and the container hands it to every class that asks for an `IJevClient`. The
`ZeroAlloc.Jev.DependencyInjection` package does the registering. It sets up the client over `IHttpClientFactory`, reads
its options from code or from configuration, and checks them when the host starts.

```shell
dotnet add package ZeroAlloc.Jev.DependencyInjection
```

The extension methods live in the `Microsoft.Extensions.DependencyInjection` namespace, so no extra `using` is needed
beyond the one you already have for the container.

## Register a client

A class asks for `IJevClient` in its constructor. Here is a small one that judges a message as urgent or not.

<!-- snippet: DependencyInjection_Consumer -->
```cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ZeroAlloc.Jev;

// One yes/no question, so the example stays short.
[JevQuestions]
public partial record InboxCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

// A class asks for IJevClient in its constructor, and the container supplies the shared client.
public sealed class InboxTriage(IJevClient jev)
{
    public async Task<string> TriageAsync(string message, CancellationToken cancellationToken)
    {
        var result = await jev.EvaluateAsync<InboxCheck>(message, cancellationToken);
        if (result.IsFailure)
        {
            return $"Jev failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent" : "can wait";
    }
}
```
<!-- endSnippet -->

`AddJevClient` registers the client. The overload with an `Action<JevClientOptions>` sets the options in code. Here the
key comes from configuration, such as user secrets, so it is never written into the program.

<!-- snippet: DependencyInjection_Register -->
```cs
// The key is read from configuration, such as user secrets, and never written into the code.
public static IHttpClientBuilder AddJev(IHostApplicationBuilder builder)
{
    builder.Services.AddSingleton<InboxTriage>();
    return builder.Services.AddJevClient(options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
}
```
<!-- endSnippet -->

After that, the container builds an `InboxTriage` with the shared client whenever one is needed, and calls
`TriageAsync` as before. `AddJevClient` returns the `IHttpClientBuilder` of the client's `HttpClient`, which is where
handlers are added. [Below](#the-httpclient-from-the-factory) covers that.

## The six overloads

`AddJevClient` has six overloads. Three register the default client, and three register a client under a name.

| Overload | Registers | Options come from |
| --- | --- | --- |
| `AddJevClient()` | the default client | the defaults and the environment variables |
| `AddJevClient(Action<JevClientOptions>)` | the default client | your delegate |
| `AddJevClient(IConfiguration)` | the default client | a configuration section |
| `AddJevClient(string name)` | a keyed client | the defaults and the environment variables |
| `AddJevClient(string name, Action<JevClientOptions>)` | a keyed client | your delegate |
| `AddJevClient(string name, IConfiguration)` | a keyed client | a configuration section |

"The defaults and the environment variables" are the ones from [the client page](client-and-errors.md#options): the key
comes from `TYPESAFE_API_KEY`, the base address from `TYPESAFE_BASE_URL` when it is set, and everything else has its
default.

<!-- snippet: DependencyInjection_FromEnvironment -->
```cs
// With no options at all, the key comes from TYPESAFE_API_KEY, the base address from TYPESAFE_BASE_URL when set,
// and everything else from the defaults.
public static void AddFromEnvironment(IServiceCollection services)
{
    services.AddJevClient();
    services.AddJevClient("backup");
}
```
<!-- endSnippet -->

The `name` must not be empty. It is both the service key and the name of the options.

## Several clients

An application sometimes needs more than one client, for example one for TypeSafe and one for OpenRouter, or two keys
with different retry settings. Register each under a name, and ask for each by that name with `[FromKeyedServices]`.

<!-- snippet: DependencyInjection_Keyed -->
```cs
public static void AddKeyedJev(IHostApplicationBuilder builder)
{
    builder.Services.AddJevClient("typesafe", options => options.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    builder.Services.AddJevClient("openrouter", options =>
    {
        options.Provider = JevProvider.OpenRouter;
        options.ApiKey = builder.Configuration["OpenRouter:ApiKey"];
    });
    builder.Services.AddSingleton<InboxRouter>();
}
```
<!-- endSnippet -->

<!-- snippet: DependencyInjection_KeyedConsumer -->
```cs
// A keyed client is asked for by its key.
public sealed class InboxRouter([FromKeyedServices("openrouter")] IJevClient jev)
{
    public async Task<string> TriageAsync(string message, CancellationToken cancellationToken)
    {
        var result = await jev.EvaluateAsync<InboxCheck>(message, cancellationToken);
        if (result.IsFailure)
        {
            return $"Jev failed, {result.Error.Kind}";
        }

        return result.Value.IsUrgent.Value ? "urgent, via OpenRouter" : "can wait, via OpenRouter";
    }
}
```
<!-- endSnippet -->

A keyed client is a separate singleton with its own options and its own `HttpClient`. A name you register does not
register the default client, so `IJevClient` without a key still fails to resolve unless you also called one of the
default overloads.

## Options from configuration

Instead of a delegate, you can bind a client's options from configuration, such as `appsettings.json`. The keys are the
`JevClientOptions` property names, and every key is optional.

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

Pass the section to `AddJevClient`. The first call binds `Jev` to the default client, and the second binds `OpenRouter`
to a client keyed `openrouter`.

<!-- snippet: DependencyInjection_Configuration -->
```cs
// Each client reads its own section.
public static void AddFromConfiguration(IHostApplicationBuilder builder)
{
    builder.Services.AddJevClient(builder.Configuration.GetSection("Jev"));
    builder.Services.AddJevClient("openrouter", builder.Configuration.GetSection("OpenRouter"));
}
```
<!-- endSnippet -->

Things to know when you bind:

- **Time spans** are written `hh:mm:ss`, with fractions of a second after a dot, as `InitialBackoff` shows.
- **Keep the key out of `appsettings.json`.** Leave `ApiKey` unset to use `TYPESAFE_API_KEY` or `OPENROUTER_API_KEY`, or
  supply it from user secrets or an environment variable. The setting is `Jev:ApiKey`, and as an environment variable
  the colon becomes a double underscore, `Jev__ApiKey`.
- **A delegate registered later overrides bound values.** Calling `AddJevClient(options => ...)` after binding changes
  only the properties it sets.
- **A value the binder cannot convert**, such as `"MaxRetries": "abc"`, fails with the binder's
  `InvalidOperationException`, not with an `OptionsValidationException`. A value that converts but is out of range, such
  as `"MaxRetries": 11`, is checked like any other, as the next section says.
- **Binding uses no reflection,** so it works under [Native AOT](native-aot.md) without any setup.
- **Changes are not picked up.** The client is a singleton that reads its options once, so a change to the configuration
  after it is built has no effect.

```shell
dotnet user-secrets set "Jev:ApiKey" "your-key"
```

## Checking the options at start-up

Each registration checks its options when the host starts, so a wrong value stops the program at launch instead of on
the first request. It runs `JevClientOptions.Validate()`, the same check the client's constructor runs, and the failure
is an `OptionsValidationException` that carries the core's own message. That covers a missing API key, an invalid base
address, `MaxRetries` outside 0 to 10, a backoff out of range, a blank model, and an invalid `Timeout`.

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
        return $"Jev is misconfigured: {string.Join(' ', exception.Failures)}";
    }
}
```
<!-- endSnippet -->

Without a host, with a plain `BuildServiceProvider()`, there is no start-up step, so the same exception comes when the
client is first resolved.

Some things still need valid options, an API key included:

- **Creating Jev's named `HttpClient` from `IHttpClientFactory` yourself.** It reads the same options, so a call such as
  `CreateClient("ZeroAlloc.Jev")` fails with the same exception when they are invalid.
- **A test host that replaces the client.** If the application registers its own `IJevClient` before `AddJevClient`, the
  application's client wins, but the options are still validated. A test that swaps the client needs a placeholder key,
  for example `AddJevClient(options => options.ApiKey = "test")`, or a `Jev:ApiKey` setting.

## What gets registered

- **One singleton per registration.** It reads its named options once, when the container first builds it. The default
  client reads the default options, and a keyed client reads the options named by its key. The client logs through the
  container's `ILoggerFactory`, as [Logging, traces and metrics](observability.md#logging) describes.
- **A repeat call for the same name** adds its delegate, and the delegates run in order. It does not register a second
  client.
- **Your own registration wins.** `AddJevClient` registers the client with `TryAdd`, so an `IJevClient` you registered
  first stays. `AddJevClient` still sets up the named `HttpClient` and the options in that case.
- **Disposal.** The container disposes the client with the provider. The `HttpClient` belongs to the factory and is left
  alone.

## The HttpClient from the factory

The client's `HttpClient` comes from `IHttpClientFactory`. It is named `ZeroAlloc.Jev`, or `ZeroAlloc.Jev:` followed by
the key for a keyed client. You rarely need the name, except to change what is behind it in a test.

- **Its handler.** The primary handler is a `SocketsHttpHandler` that recycles its connections every two minutes, so a
  change in DNS is picked up. The factory never rotates it, because the singleton keeps its `HttpClient` for life.
- **Its settings.** `JevClient.ConfigureHttpClient` gives the `HttpClient` its base address, the per-attempt `Timeout`
  and the `ZeroAlloc.Jev` User-Agent.
- **Its handlers.** Add your own through the builder `AddJevClient` returns. A handler sees every request and every
  retry. `ConfigureHttpClientDefaults` adds a handler to every `HttpClient` the factory makes, and Jev's included,
  whether you call it before or after `AddJevClient`.

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
    // AddJevClient returns the builder of the client's HttpClient, so handlers are added the usual way.
    public static IHttpClientBuilder AddTracedJev(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        return services
            .AddJevClient(options => options.ApiKey = apiKey)
            .AddHttpMessageHandler<TraceHeaderHandler>();
    }

    // ConfigureHttpClientDefaults adds a handler to every HttpClient the factory makes, Jev's included.
    public static void AddTracedEverywhere(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        services.AddJevClient(options => options.ApiKey = apiKey);
    }

    // To keep a defaults handler off Jev's client, clear the handlers of its builder. This also clears any you added
    // through that builder, so add those inside the delegate, after the Clear.
    public static IHttpClientBuilder AddJevWithoutDefaultHandlers(IServiceCollection services, string apiKey)
    {
        services.AddTransient<TraceHeaderHandler>();
        services.ConfigureHttpClientDefaults(defaults => defaults.AddHttpMessageHandler<TraceHeaderHandler>());
        return services
            .AddJevClient(options => options.ApiKey = apiKey)
            .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
    }

    // When a handler of yours retries, such as a standard resilience handler, turn Jev's own retries off,
    // so the two do not multiply.
    public static void AddWithOwnRetries(IServiceCollection services, string apiKey)
        => services.AddJevClient(options =>
        {
            options.ApiKey = apiKey;
            options.MaxRetries = 0;
        });
}
```
<!-- endSnippet -->

### Host-wide defaults

`ConfigureHttpClientDefaults` is how a host applies one setup to every `HttpClient`, and it reaches Jev's clients too.
Two things in it do not:

- **A primary handler set in the defaults is replaced.** Jev's client always uses its own `SocketsHttpHandler`, whether
  the defaults are registered before or after `AddJevClient`. A defaults primary handler never sees Jev's requests.
- **The defaults' loggers are removed** from Jev's clients, in either order, as the next section says.

The usual cause of trouble is a defaults handler that retries, most often Aspire ServiceDefaults'
`AddStandardResilienceHandler()`. Its retries multiply with Jev's, so set `MaxRetries = 0`, as the snippet does and as
[the cost of retrying](client-and-errors.md#the-cost-of-retrying) explains. It has time-outs of its own too, and the
advice is to keep them at or above `Timeout`. That last point is advice, not something the tests here check.

To keep a defaults handler off Jev's client, clear the additional handlers of its builder, as the last method in the
snippet does. This also removes every handler you added yourself through that builder, so add yours in the same call
with `ConfigureAdditionalHttpMessageHandlers`, after the `Clear()`, if you want them.

### The factory's request logs

The factory has request logs of its own, with the categories `System.Net.Http.HttpClient.*`. They are turned off for
Jev's clients. Jev's client already logs each operation and each retried attempt, in the `ZeroAlloc.Jev.JevClient`
category, and the factory's logging handlers add work to every request. Call `AddDefaultLogger()` on the builder
`AddJevClient` returns to bring the factory's logs back.

Without a logging provider, or with Jev's log levels turned off, the client logs nothing. [Logging, traces and
metrics](observability.md#logging) lists the events and their levels.

## Using Jev without the package

The package is a convenience. `JevClient.ConfigureHttpClient(httpClient, options)` configures any `HttpClient` the way
the client configures its own: it applies the per-attempt `Timeout`, the base address when the `HttpClient` has none,
and the User-Agent. It needs no API key and leaves the handler alone. Call it before the client sends a request, for
instance on a named client of your own.

<!-- snippet: DependencyInjection_WithoutPackage -->
```cs
// A named HttpClient of your own, set up the way AddJevClient sets up its client.
public static void AddJevHttpClient(IServiceCollection services, JevClientOptions options)
    => services.AddHttpClient("jev")
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
        .ConfigureHttpClient(http => JevClient.ConfigureHttpClient(http, options));

public static JevClient Create(IHttpClientFactory factory, JevClientOptions options)
    => new(factory.CreateClient("jev"), options);
```
<!-- endSnippet -->

The `JevClient` that `Create` makes still needs an API key. `ConfigureHttpClient` sets up the `HttpClient` only, so the
key comes from `options.ApiKey` or the environment variable, as for any client.

The `SocketsHttpHandler` and the infinite handler lifetime do for this client what the package does for its own: a
long-lived `JevClient` keeps one handler for life, so the connections recycle themselves and the factory does not rotate
the handler.

## Next

- [Logging, traces and metrics](observability.md): what a registered client reports about each call.
- [Client and errors](client-and-errors.md): the options a registration sets, and every `JevError`.
- [Typed evaluation](typed-evaluation.md): the calls a resolved client makes.
- [Performance](performance.md): what a call costs, measured, including through dependency injection.
