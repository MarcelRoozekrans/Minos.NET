using System.Buffers;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Minos.OpenAI.Protocols;
using Minos.OpenAI.Transport;
using Minos.Transport;
using Minos.Transport.Rest;
using ZeroAlloc.Results;

namespace Minos.OpenAI;

/// <summary>
/// Calls OpenAI's Decisions API, <c>POST /v1/decisions</c>, to answer a request's Noul, Choice and Score questions about
/// its state, text or images.
/// </summary>
/// <remarks>
/// The Decisions API is in public beta, and this client was built from the examples in OpenAI's Decisions guide, not
/// from a recorded exchange with the live API; a change to the beta can change what it sends or reads.
/// <para>
/// Thread-safe. Create one per application and reuse it; dispose it when the application stops. Pass an
/// <see cref="ILoggerFactory"/> to log each call, each retried attempt and each unexpected exception; a call is logged
/// only with <see cref="OpenAIDecisionClientOptions.UseStandardPipeline"/>. What the library writes never contains the
/// state, questions, answers, API key, a header value or an error response body; the unexpected-exception event carries
/// the exception as thrown, which can include one from your own handler. Spans and metrics come from the Minos
/// ActivitySource and Meter, with the provider name <c>openai</c>; see the
/// <see href="https://marcelroozekrans.github.io/Minos.NET/observability">observability guide</see>.
/// </para>
/// <para>
/// Each <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> call runs the standard pipeline: one span and one
/// set of metrics over the whole call (<see cref="OpenTelemetryDecisionClient"/>), one outcome log when a logger factory
/// was given (<see cref="LoggingDecisionClient"/>), then retries (<see cref="RetryingDecisionClient"/>) around a transport
/// that sends one attempt. <see cref="OpenAIDecisionClientOptions.UseStandardPipeline"/> set to <see langword="false"/>
/// leaves the transport alone. <see cref="GetService"/> finds each stage.
/// </para>
/// </remarks>
public sealed class OpenAIDecisionClient : IDecisionClient
{
    private static readonly ProductInfoHeaderValue UserAgent = HttpClientSetup.CreateUserAgent(typeof(OpenAIDecisionClient).Assembly);

    private readonly HttpClient? _ownedHttpClient;
    private readonly CapabilitySet _capabilities;
    private readonly DecisionClientMetadata _metadata;
    private readonly DecisionRetryOptions _retryOptions;
    private readonly IDecisionClient _pipeline;
    // Volatile: Dispose writes it before it disposes the owned HttpClient, and the error mapper and the transport read it
    // on the threads that complete the calls in flight.
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIDecisionClient"/> class that creates and owns its
    /// <see cref="HttpClient"/>, using defaults and environment variables.
    /// </summary>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public OpenAIDecisionClient()
        : this((OpenAIDecisionClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OpenAIDecisionClient"/> class that creates and owns its <see cref="HttpClient"/>.</summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public OpenAIDecisionClient(OpenAIDecisionClientOptions? options)
        : this(ResolveSettings(options), httpClient: null, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIDecisionClient"/> class over a caller-owned <see cref="HttpClient"/>,
    /// using defaults and environment variables.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="OpenAIDecisionClientOptions.BaseAddress"/> when set, and must end in '/'. It must then be the full API
    /// root, because it replaces <see cref="OpenAIDecisionClientOptions.BaseAddress"/>. When it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="httpClient"/> already has an invalid base address.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public OpenAIDecisionClient(HttpClient httpClient)
        : this(httpClient, (OpenAIDecisionClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OpenAIDecisionClient"/> class over a caller-owned <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="OpenAIDecisionClientOptions.BaseAddress"/> when set, and must end in '/'. It must then be the full API
    /// root, because it replaces <see cref="OpenAIDecisionClientOptions.BaseAddress"/>. When it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public OpenAIDecisionClient(HttpClient httpClient, OpenAIDecisionClientOptions? options)
        : this(ResolveSettings(httpClient, options), httpClient, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIDecisionClient"/> class that creates and owns its
    /// <see cref="HttpClient"/> and logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>Minos.DecisionClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public OpenAIDecisionClient(OpenAIDecisionClientOptions? options, ILoggerFactory? loggerFactory)
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
    /// Initializes a new instance of the <see cref="OpenAIDecisionClient"/> class over a caller-owned
    /// <see cref="HttpClient"/> that logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="OpenAIDecisionClientOptions.BaseAddress"/> when set, and must end in '/'. It must then be the full API
    /// root, because it replaces <see cref="OpenAIDecisionClientOptions.BaseAddress"/>. When it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
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
    public OpenAIDecisionClient(HttpClient httpClient, OpenAIDecisionClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(httpClient, options),
            httpClient,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(DecisionLog.Category))
    {
    }

    internal OpenAIDecisionClient(OpenAIClientSettings settings, HttpClient? httpClient, HttpMessageHandler? ownedHandler, TimeProvider time)
        : this(settings, httpClient, ownedHandler, time, ArrayPool<byte>.Shared, logger: null)
    {
    }

    // pool supplies the request and response buffers; tests pass a counting pool to check every one is returned. logger
    // is null when no factory was given, and then the pipeline has no logging stage.
    internal OpenAIDecisionClient(
        OpenAIClientSettings settings,
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

        _capabilities = settings.Capabilities;
        _retryOptions = new DecisionRetryOptions
        {
            MaxRetries = settings.MaxRetries,
            InitialBackoff = settings.InitialBackoff,
            MaxRetryDelay = settings.MaxRetryDelay,
            Jitter = settings.Jitter,
        };
        _metadata = new DecisionClientMetadata(OpenAIClientSettings.ProviderName, new Uri(httpClient.BaseAddress!, OpenAIClientSettings.Path), settings.Model);

        // A disposed client never retries. Only an owned HttpClient is disposed with the client, so only an owned one can
        // tear a request down: the mapper reports such an attempt as Disposed, which is never retried. A borrowed
        // HttpClient's attempt in flight keeps its own result, so its mapper never reads the flag.
        Func<bool> disposed = () => _disposed;

        // The only method names its own serializer, so the generated client's default one is never used; the raw
        // serializer fills that slot rather than a JSON context with nothing in it.
        var rawSerializer = new DecisionRawSerializer(pool);
        var api = new OpenAIDecisionsApiClient(
            httpClient,
            rawSerializer,
            rawSerializer,
            new DecisionErrorMapper(time, _ownedHttpClient is null ? null : disposed, OpenAIDecisionsProtocol.Instance));

        // OpenAI has no retry-count header, so every attempt is sent the same way and the retry count is not used.
        var authorization = settings.Authorization;
        RawSend send = (body, _, ct) => api.SendAsync(body, OpenAIClientSettings.Path, authorization, ct);

        // The standard chain, outermost first: one span over every attempt, one outcome log inside it, then retries. Each
        // stage reads the metadata through GetService, which the chain answers from the transport at its bottom.
        IDecisionClient pipeline = new DecisionTransport(
            send, OpenAIDecisionsProtocol.Instance, settings.Capabilities, OpenAIClientSettings.ProviderLabel, settings.Model, _metadata, pool, disposed);
        if (settings.UseStandardPipeline)
        {
            pipeline = new RetryingDecisionClient(pipeline, _retryOptions.Clone(), logger, time);
            if (logger is not null)
            {
                pipeline = new LoggingDecisionClient(pipeline, logger);
            }

            pipeline = new OpenTelemetryDecisionClient(pipeline);
        }

        _pipeline = pipeline;
    }

    /// <summary>
    /// Configures an <see cref="HttpClient"/> as <see cref="OpenAIDecisionClient"/> configures one it creates, except its
    /// handler, for a client you then pass to a constructor that takes an <see cref="HttpClient"/>, such as one from
    /// <c>IHttpClientFactory</c>.
    /// </summary>
    /// <param name="httpClient">The client to configure. It must not have sent a request yet.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <remarks>
    /// Sets <see cref="HttpClient.BaseAddress"/> from <see cref="OpenAIDecisionClientOptions.BaseAddress"/>, the
    /// <see cref="OpenAIDefaults.BaseAddressEnvironmentVariable"/> environment variable or
    /// <see cref="OpenAIDefaults.BaseAddress"/>, only when <paramref name="httpClient"/> has none. Sets
    /// <see cref="HttpClient.Timeout"/> to <see cref="OpenAIDecisionClientOptions.Timeout"/>, the per-attempt time-out, and
    /// adds the <c>Minos.NET</c> User-Agent unless it is already there. It reads no API key, so it can run before one is
    /// configured.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The time-out or the base address option is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The base address environment variable is invalid, or <paramref name="httpClient"/> has already sent a request.
    /// </exception>
    public static void ConfigureHttpClient(HttpClient httpClient, OpenAIDecisionClientOptions? options)
        => ConfigureHttpClient(httpClient, options, Environment.GetEnvironmentVariable);

    // environment reads an environment variable; tests pass a fake.
    internal static void ConfigureHttpClient(HttpClient httpClient, OpenAIDecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClientSetup.Apply(httpClient, OpenAIClientSettings.ResolveHttp(options, environment), UserAgent);
    }

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state, any images and, optionally, the model.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state, any images and, optionally, the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>
    /// Runs the client's pipeline: with <see cref="OpenAIDecisionClientOptions.UseStandardPipeline"/>, telemetry, logging
    /// when the client has a logger factory, and retries with the options' retry settings; without it, one attempt. The
    /// request is sent with <see cref="OpenAIDecisionClientOptions.Model"/> when <see cref="DecisionRequest.Model"/> is
    /// <see langword="null"/>, and every pooled buffer is returned before the call completes. A request that
    /// <see cref="OpenAIDecisionClientOptions.Capabilities"/> or the API does not accept, such as a Noul with
    /// <c>WhenTrue</c>, returns <see cref="DecisionErrorKind.Unsupported"/> without a request, and is not retried. A
    /// refusal returns <see cref="DecisionErrorKind.Refused"/>, and is not retried.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
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
        // ObjectDisposedException, and the mapper, the transport and the retry stage must see the flag when either happens.
        _disposed = true;
        _pipeline.Dispose();
        _ownedHttpClient?.Dispose();
    }

    private static OpenAIClientSettings ResolveSettings(OpenAIDecisionClientOptions? options)
        => OpenAIClientSettings.Resolve(options, Environment.GetEnvironmentVariable);

    // Validates httpClient before resolving settings, so an invalid options value never masks a null httpClient.
    private static OpenAIClientSettings ResolveSettings(HttpClient httpClient, OpenAIDecisionClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return ResolveSettings(options);
    }
}
