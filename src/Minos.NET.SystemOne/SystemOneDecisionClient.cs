using System.Buffers;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Minos.SystemOne.Protocols;
using Minos.SystemOne.Serialization;
using Minos.SystemOne.Telemetry;
using Minos.SystemOne.Transport;
using Minos.Transport;
using Minos.Transport.Rest;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace Minos.SystemOne;

/// <summary>
/// Calls a Jev System One API: TypeSafe's, directly or through OpenRouter, a vLLM Semantic Router, or
/// another server that speaks the <c>/v1/systemone</c> protocol. <see cref="SystemOneDecisionClientOptions.Provider"/> picks one.
/// </summary>
/// <remarks>
/// Thread-safe. Create one per application and reuse it; dispose it when the application stops. Pass an
/// <see cref="ILoggerFactory"/> to log each operation, each retried attempt and each unexpected exception; a neutral
/// <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> call is logged only with
/// <see cref="SystemOneDecisionClientOptions.UseStandardPipeline"/>, while the raw System One calls are logged either way. What the
/// library writes never contains the state, questions, answers, API key, a header value or an error response body; the
/// unexpected-exception event carries the exception as thrown, which can include one from your own handler.
/// Spans and metrics come from the Minos ActivitySource and Meter; see the
/// <see href="https://marcelroozekrans.github.io/Minos.NET/observability">observability guide</see>.
/// <para>
/// Each <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> call runs the standard pipeline: one span and one
/// set of metrics over the whole call (<see cref="OpenTelemetryDecisionClient"/>), one outcome log when a logger factory
/// was given (<see cref="LoggingDecisionClient"/>), then retries (<see cref="RetryingDecisionClient"/>) around a transport
/// that sends one attempt. <see cref="SystemOneDecisionClientOptions.UseStandardPipeline"/> set to <see langword="false"/> leaves the
/// transport alone. <see cref="GetService"/> finds each stage.
/// </para>
/// </remarks>
public sealed class SystemOneDecisionClient : IDecisionClient
{
    private static readonly ProductInfoHeaderValue UserAgent = HttpClientSetup.CreateUserAgent(typeof(SystemOneDecisionClient).Assembly);

    private readonly DecisionOperationsInstrumented _operations;
    private readonly HttpClient? _ownedHttpClient;
    private readonly string _providerName;
    private readonly Uri _endpoint;
    private readonly SystemOneProvider _provider;
    private readonly CapabilitySet _capabilities;
    private readonly ILogger? _logger;
    private readonly DecisionClientMetadata _metadata;
    private readonly DecisionRetryOptions _retryOptions;
    private readonly IDecisionClient _pipeline;
    // Volatile: Dispose writes it before it disposes the owned HttpClient, and the error mapper, the transport and the raw
    // calls' retry loop read it on the threads that complete the calls in flight.
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemOneDecisionClient"/> class that creates and owns its
    /// <see cref="HttpClient"/>, using defaults and environment variables.
    /// </summary>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public SystemOneDecisionClient()
        : this((SystemOneDecisionClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SystemOneDecisionClient"/> class that creates and owns its <see cref="HttpClient"/>.</summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public SystemOneDecisionClient(SystemOneDecisionClientOptions? options)
        : this(ResolveSettings(options), httpClient: null, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemOneDecisionClient"/> class over a caller-owned <see cref="HttpClient"/>,
    /// using defaults and environment variables.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="SystemOneDecisionClientOptions.BaseAddress"/> when set, and must end in '/'. It must then be the full API root,
    /// because it replaces <see cref="SystemOneDecisionClientOptions.BaseAddress"/>. When it is <see langword="null"/>, this
    /// constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="httpClient"/> already has an invalid base address.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public SystemOneDecisionClient(HttpClient httpClient)
        : this(httpClient, (SystemOneDecisionClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SystemOneDecisionClient"/> class over a caller-owned <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="SystemOneDecisionClientOptions.BaseAddress"/> when set, and must end in '/'. It must then be the full API root,
    /// because it replaces <see cref="SystemOneDecisionClientOptions.BaseAddress"/>. When it is <see langword="null"/>, this
    /// constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public SystemOneDecisionClient(HttpClient httpClient, SystemOneDecisionClientOptions? options)
        : this(ResolveSettings(httpClient, options), httpClient, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemOneDecisionClient"/> class that creates and owns its <see cref="HttpClient"/>
    /// and logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>Minos.DecisionClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public SystemOneDecisionClient(SystemOneDecisionClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(options),
            httpClient: null,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(DecisionLog.Category))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemOneDecisionClient"/> class over a caller-owned <see cref="HttpClient"/> that
    /// logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="SystemOneDecisionClientOptions.BaseAddress"/> when set, and must end in '/'. It must then be the full API root,
    /// because it replaces <see cref="SystemOneDecisionClientOptions.BaseAddress"/>. When it is <see langword="null"/>, this
    /// constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>Minos.DecisionClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public SystemOneDecisionClient(HttpClient httpClient, SystemOneDecisionClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(httpClient, options),
            httpClient,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(DecisionLog.Category))
    {
    }

    internal SystemOneDecisionClient(SystemOneDecisionClientSettings settings, HttpClient? httpClient, HttpMessageHandler? ownedHandler, TimeProvider time)
        : this(settings, httpClient, ownedHandler, time, ArrayPool<byte>.Shared)
    {
    }

    // pool supplies the typed path's request and response buffers; tests pass a counting pool to check every one is returned.
    internal SystemOneDecisionClient(
        SystemOneDecisionClientSettings settings,
        HttpClient? httpClient,
        HttpMessageHandler? ownedHandler,
        TimeProvider time,
        ArrayPool<byte> pool)
        : this(settings, httpClient, ownedHandler, time, pool, logger: null)
    {
    }

    // logger is null when no factory was given, and then nothing in the pipeline or the operations changes.
    internal SystemOneDecisionClient(
        SystemOneDecisionClientSettings settings,
        HttpClient? httpClient,
        HttpMessageHandler? ownedHandler,
        TimeProvider time,
        ArrayPool<byte> pool,
        ILogger? logger)
    {
        if (httpClient is null)
        {
            httpClient = new HttpClient(ownedHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
            HttpClientSetup.Apply(httpClient, new HttpSettings(settings.BaseAddress, settings.Timeout), UserAgent);
            _ownedHttpClient = httpClient;
        }
        else if (httpClient.BaseAddress is not null)
        {
            HttpClientSetup.CheckBorrowedBaseAddress(httpClient);
        }
        else
        {
            httpClient.BaseAddress = settings.BaseAddress;
        }

        _provider = settings.Provider;
        _capabilities = settings.Capabilities;
        _retryOptions = new DecisionRetryOptions
        {
            MaxRetries = settings.MaxRetries,
            InitialBackoff = settings.InitialBackoff,
            MaxRetryDelay = settings.MaxRetryDelay,
            Jitter = settings.Jitter,
        };
        _providerName = settings.ProviderName;
        _endpoint = httpClient.BaseAddress!;
        _logger = logger;
        // A disposed client never retries. Only an owned HttpClient is disposed with the client, so only an owned one can
        // tear a request down: the mapper reports such an attempt as Disposed, which is never retried. A borrowed
        // HttpClient's attempt in flight keeps its own result, so its mapper never reads the flag.
        Func<bool> disposed = () => _disposed;
        var api = new DecisionApiClient(
            httpClient,
            new SystemTextJsonSerializer(DecisionJsonContext.Default),
            new DecisionRawSerializer(pool),
            new DecisionErrorMapper(time, _ownedHttpClient is null ? null : disposed, SystemOneProtocol.Instance));
        _metadata = new DecisionClientMetadata(_providerName, new Uri(_endpoint, settings.Path), settings.Model);

        // The standard chain, outermost first: one span over every attempt, one outcome log inside it, then retries. Each
        // stage reads the metadata through GetService, which the chain answers from the transport at its bottom.
        var path = settings.Path;
        var authorization = settings.Authorization;
        RawSend send = (body, retryCount, ct) => api.SendAsync(body, path, authorization, retryCount, ct);
        IDecisionClient pipeline = new DecisionTransport(
            send, SystemOneProtocol.Instance, settings.Capabilities, settings.Provider.ToString(), settings.Model, _metadata, pool, disposed);
        DecisionRetry? rawRetry = null;
        if (settings.UseStandardPipeline)
        {
            pipeline = new RetryingDecisionClient(pipeline, _retryOptions.Clone(), logger, time);
            if (logger is not null)
            {
                pipeline = new LoggingDecisionClient(pipeline, logger);
            }

            pipeline = new OpenTelemetryDecisionClient(pipeline);

            // The raw System One calls keep their own span and log, and retry through the same loop with the same settings.
            rawRetry = new DecisionRetry(_retryOptions.Clone(), logger, time, disposed);
        }

        _pipeline = pipeline;

        // Always wired: with nothing listening, the generated proxy returns each operation's own task.
        _operations = new DecisionOperationsInstrumented(new DecisionOperations(api, settings, rawRetry, disposed));
    }

    /// <summary>
    /// Configures an <see cref="HttpClient"/> as <see cref="SystemOneDecisionClient"/> configures one it creates, except its handler, for a client you then
    /// pass to a constructor that takes an <see cref="HttpClient"/>, such as one from <c>IHttpClientFactory</c>.
    /// </summary>
    /// <param name="httpClient">The client to configure. It must not have sent a request yet.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <remarks>
    /// Sets <see cref="HttpClient.BaseAddress"/> from <see cref="SystemOneDecisionClientOptions.BaseAddress"/>, the
    /// <see cref="SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable"/> environment variable or the provider's
    /// default, only when <paramref name="httpClient"/> has none. Sets <see cref="HttpClient.Timeout"/> to <see cref="SystemOneDecisionClientOptions.Timeout"/>,
    /// the per-attempt time-out, and adds the <c>Minos.NET</c> User-Agent unless it is already there. It reads no
    /// API key, so it can run before one is configured.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The provider, the time-out or the base address option is invalid, or the base address
    /// the provider needs is missing.
    /// </exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid, or <paramref name="httpClient"/> has already sent a request.</exception>
    public static void ConfigureHttpClient(HttpClient httpClient, SystemOneDecisionClientOptions? options)
        => ConfigureHttpClient(httpClient, options, Environment.GetEnvironmentVariable);

    // environment reads an environment variable; tests pass a fake.
    internal static void ConfigureHttpClient(HttpClient httpClient, SystemOneDecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClientSetup.Apply(httpClient, SystemOneDecisionClientSettings.ResolveHttp(options, environment), UserAgent);
    }

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state and, optionally, the model.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state and, optionally, the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>
    /// Runs the client's pipeline: with <see cref="SystemOneDecisionClientOptions.UseStandardPipeline"/>, telemetry, logging when
    /// the client has a logger factory, and retries with the options' retry settings; without it, one attempt. The
    /// request is sent with <see cref="SystemOneDecisionClientOptions.Model"/> when <see cref="DecisionRequest.Model"/> is
    /// <see langword="null"/>, and every pooled buffer is returned before the call completes. A request that
    /// <see cref="SystemOneDecisionClientOptions.Capabilities"/> does not accept, such as one with images, returns <see cref="DecisionErrorKind.Unsupported"/>
    /// without a request, and is not retried.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pipeline.EvaluateAsync(request, cancellationToken);
    }

    // Explicit, so the public pair above follows the (x)/(x, CancellationToken) convention: implementing the interface's
    // optional parameter implicitly would either drop its default or give this class an overload with an optional one.
    ValueTask<Result<DecisionResponse, DecisionError>> IDecisionClient.EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken)
        => EvaluateAsync(request, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Returns this client for a type it is, a new <see cref="DecisionRetryOptions"/> from its retry settings, a new
    /// <see cref="DecisionCapabilities"/> with its resolved capabilities, every property set, its
    /// <see cref="DecisionClientMetadata"/>, then whatever its pipeline's stages return, such as
    /// <see cref="RetryingDecisionClient"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null)
        {
            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType == typeof(DecisionRetryOptions))
            {
                return _retryOptions.Clone();
            }

            if (serviceType == typeof(DecisionCapabilities))
            {
                return _capabilities.ToOptions();
            }

            if (serviceType.IsInstanceOfType(_metadata))
            {
                return _metadata;
            }
        }

        return _pipeline.GetService(serviceType, serviceKey);
    }

    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = DecisionLog.StartTiming(_logger);
        return WithLogging(
            _operations.EvaluateAsync(request, _providerName, _endpoint, cancellationToken),
            request.Model,
            _logger is null ? 0 : request.Questions is { } questions ? questions.Count : 0,
            started,
            cancellationToken);
    }

    /// <summary>
    /// Lists the models and aliases available to the account, with <c>GET v1/models</c>, on a provider whose
    /// <see cref="DecisionCapabilities.SupportsModelListing"/> is on: by default TypeSafe only.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The models, or the <see cref="DecisionError"/> that prevented them; <see cref="DecisionErrorKind.Unsupported"/>,
    /// without a request, where <see cref="DecisionCapabilities.SupportsModelListing"/> is off.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = DecisionLog.StartTiming(_logger);

        // Created before any call, so it bypasses the proxy: no request is sent, and no span or metric is recorded.
        if (!_capabilities.ModelListing)
        {
            return WithModelLogging(
                ValueTask.FromResult(Result<ModelList, DecisionError>.Failure(new DecisionError(
                    DecisionErrorKind.Unsupported, "Model listing is not available on " + _provider.ToString() + "."))),
                started,
                cancellationToken);
        }

        return WithModelLogging(_operations.ListModelsAsync(_providerName, _endpoint, cancellationToken), started, cancellationToken);
    }

    /// <summary>Disposes the client's pipeline and the <see cref="HttpClient"/> this client created; a borrowed one is left alone.</summary>
    /// <remarks>
    /// <para>
    /// A call started after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>. A call already in flight
    /// over an <see cref="HttpClient"/> this client created is torn down with it, or is never sent when the disposal
    /// lands just before the send; either way it returns a <see cref="DecisionErrorKind.Disposed"/> failure. A real time-out
    /// that was mapped before <see cref="Dispose"/> set the flag stays <see cref="DecisionErrorKind.Timeout"/> when no retry
    /// is left.
    /// </para>
    /// <para>
    /// A disposed client never retries. A retry that would start after <see cref="Dispose"/> is not sent, and the call
    /// returns <see cref="DecisionErrorKind.Disposed"/>, whether the client created its <see cref="HttpClient"/> or borrowed
    /// it. A borrowed <see cref="HttpClient"/> is not disposed, so the attempt already in flight over it is not torn down
    /// and keeps its own result. Calling <see cref="Dispose"/> more than once does nothing.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Set first: disposing the HttpClient cancels the requests in flight, or makes a send that has not started throw
        // ObjectDisposedException, and the mapper, the transport and the retry loops must see the flag when either happens.
        _disposed = true;
        _pipeline.Dispose();
        _ownedHttpClient?.Dispose();
    }

    // Checked per call: without a logger, or with every level the operation can emit disabled, this returns call
    // itself, so the client runs exactly the unlogged code and pays no extra state machine.
    private ValueTask<Result<SystemOneResponse, DecisionError>> WithLogging(
        ValueTask<Result<SystemOneResponse, DecisionError>> call, string model, int questionCount, long started, CancellationToken ct)
        => _logger is { } logger && DecisionLog.IsAnyEnabled(logger)
            ? LogEvaluationAsync(logger, call, model, questionCount, started, ct)
            : call;

    // Logs the whole outcome once, after any retries; the filter logs a thrown exception without catching it, so it
    // surfaces unchanged.
    private async ValueTask<Result<SystemOneResponse, DecisionError>> LogEvaluationAsync(
        ILogger logger,
        ValueTask<Result<SystemOneResponse, DecisionError>> call,
        string model,
        int questionCount,
        long started,
        CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            var durationMs = DecisionLog.ElapsedMilliseconds(started);
            if (result.IsSuccess)
            {
                DecisionLog.EvaluationSucceeded(logger, SystemOneLog.Evaluate, model, _providerName, questionCount, durationMs, cached: false);
            }
            else
            {
                var error = result.Error;
                var message = DecisionLog.SafeMessage(error);
                DecisionLog.EvaluationFailed(logger, SystemOneLog.Evaluate, model, error.Kind, error.StatusCode, error.RequestId, durationMs, message);
            }

            return result;
        }
        catch (Exception exception) when (DecisionLog.LogUnexpected(logger, SystemOneLog.Evaluate, exception, ct))
        {
            throw;
        }
    }

    private ValueTask<Result<ModelList, DecisionError>> WithModelLogging(
        ValueTask<Result<ModelList, DecisionError>> call, long started, CancellationToken ct)
        => _logger is { } logger && DecisionLog.IsAnyEnabled(logger) ? LogModelsAsync(logger, call, started, ct) : call;

    private async ValueTask<Result<ModelList, DecisionError>> LogModelsAsync(
        ILogger logger, ValueTask<Result<ModelList, DecisionError>> call, long started, CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            var durationMs = DecisionLog.ElapsedMilliseconds(started);
            if (result.IsSuccess)
            {
                var modelCount = result.Value.Models.Count;
                SystemOneLog.ModelsListed(logger, _providerName, modelCount, durationMs);
            }
            else
            {
                var error = result.Error;
                var message = DecisionLog.SafeMessage(error);
                SystemOneLog.ModelsListFailed(logger, _providerName, error.Kind, error.StatusCode, durationMs, message);
            }

            return result;
        }
        catch (Exception exception) when (DecisionLog.LogUnexpected(logger, SystemOneLog.ListModels, exception, ct))
        {
            throw;
        }
    }

    private static SystemOneDecisionClientSettings ResolveSettings(SystemOneDecisionClientOptions? options)
        => SystemOneDecisionClientSettings.Resolve(options, Environment.GetEnvironmentVariable);

    // Validates httpClient before resolving settings, so an invalid options value never masks a null httpClient.
    private static SystemOneDecisionClientSettings ResolveSettings(HttpClient httpClient, SystemOneDecisionClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return ResolveSettings(options);
    }
}
