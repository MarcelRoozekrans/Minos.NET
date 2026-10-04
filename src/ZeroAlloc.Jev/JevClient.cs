using System.Buffers;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Telemetry;
using ZeroAlloc.Jev.Transport;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev;

/// <summary>Calls TypeSafe's Jev System One API, directly or through OpenRouter.</summary>
/// <remarks>
/// Thread-safe. Create one per application and reuse it; dispose it when the application stops. Pass an
/// <see cref="ILoggerFactory"/> to log each operation, each retried attempt and each unexpected exception. What the
/// library writes never contains the state, questions, answers, API key, a header value or an error response body; the
/// unexpected-exception event carries the exception as thrown, which can include one from your own handler.
/// Spans and metrics come from the ZeroAlloc.Jev ActivitySource and Meter; see the
/// <see href="https://jev.zeroalloc.net/observability">observability guide</see>.
/// </remarks>
public sealed class JevClient : IJevClient, IDisposable
{
    private static readonly ProductInfoHeaderValue UserAgent = CreateUserAgent();

    private readonly JevOperationsInstrumented _operations;
    private readonly HttpClient? _ownedHttpClient;
    private readonly string _model;
    private readonly string _providerName;
    private readonly Uri _endpoint;
    private readonly ArrayPool<byte> _pool;
    private readonly JevProvider _provider;
    private readonly ILogger? _logger;
    // Volatile: Dispose writes it before it disposes the owned HttpClient, and the error mapper and the disposal guard
    // read it on the threads that complete the calls in flight.
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="JevClient"/> class that creates and owns its
    /// <see cref="HttpClient"/>, using defaults and environment variables.
    /// </summary>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient()
        : this((JevClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JevClient"/> class that creates and owns its <see cref="HttpClient"/>.</summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(JevClientOptions? options)
        : this(ResolveSettings(options), httpClient: null, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JevClient"/> class over a caller-owned <see cref="HttpClient"/>,
    /// using defaults and environment variables.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="JevClientOptions.BaseAddress"/> when set, and must end in '/'; when it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="httpClient"/> already has an invalid base address.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(HttpClient httpClient)
        : this(httpClient, (JevClientOptions?)null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JevClient"/> class over a caller-owned <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="JevClientOptions.BaseAddress"/> when set, and must end in '/'; when it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(HttpClient httpClient, JevClientOptions? options)
        : this(ResolveSettings(httpClient, options), httpClient, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JevClient"/> class that creates and owns its <see cref="HttpClient"/>
    /// and logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>ZeroAlloc.Jev.JevClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(JevClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(options),
            httpClient: null,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(JevLog.Category))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JevClient"/> class over a caller-owned <see cref="HttpClient"/> that
    /// logs through <paramref name="loggerFactory"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with; it is not disposed. Its own <see cref="HttpClient.BaseAddress"/> wins over
    /// <see cref="JevClientOptions.BaseAddress"/> when set, and must end in '/'; when it is <see langword="null"/>,
    /// this constructor sets it, so <paramref name="httpClient"/> must not have sent a request yet.
    /// </param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <param name="loggerFactory">
    /// Creates the client's logger, in the <c>ZeroAlloc.Jev.JevClient</c> category; <see langword="null"/> logs nothing.
    /// The client does not dispose it, so it must outlive the client.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value, or <paramref name="httpClient"/> already has an invalid base address.
    /// </exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(HttpClient httpClient, JevClientOptions? options, ILoggerFactory? loggerFactory)
        : this(
            ResolveSettings(httpClient, options),
            httpClient,
            ownedHandler: null,
            TimeProvider.System,
            ArrayPool<byte>.Shared,
            loggerFactory?.CreateLogger(JevLog.Category))
    {
    }

    internal JevClient(JevClientSettings settings, HttpClient? httpClient, HttpMessageHandler? ownedHandler, TimeProvider time)
        : this(settings, httpClient, ownedHandler, time, ArrayPool<byte>.Shared)
    {
    }

    // pool supplies the typed path's request and response buffers; tests pass a counting pool to check every one is returned.
    internal JevClient(
        JevClientSettings settings,
        HttpClient? httpClient,
        HttpMessageHandler? ownedHandler,
        TimeProvider time,
        ArrayPool<byte> pool)
        : this(settings, httpClient, ownedHandler, time, pool, logger: null)
    {
    }

    // logger is null when no factory was given, and then nothing in the pipeline or the operations changes.
    internal JevClient(
        JevClientSettings settings,
        HttpClient? httpClient,
        HttpMessageHandler? ownedHandler,
        TimeProvider time,
        ArrayPool<byte> pool,
        ILogger? logger)
    {
        if (httpClient is null)
        {
            httpClient = new HttpClient(ownedHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
            ApplyHttpSettings(httpClient, new JevHttpSettings(settings.BaseAddress, settings.Timeout));
            _ownedHttpClient = httpClient;
        }
        else if (httpClient.BaseAddress is not null)
        {
            ValidateBorrowedBaseAddress(httpClient);
        }
        else
        {
            httpClient.BaseAddress = settings.BaseAddress;
        }

        _provider = settings.Provider;
        _providerName = JevTelemetry.ProviderOf(settings.Provider);
        _endpoint = httpClient.BaseAddress!;
        _model = settings.Model;
        _pool = pool;
        _logger = logger;
        // A disposed client never retries. Only an owned HttpClient is disposed with the client, so only an owned one can
        // tear a request down: the mapper reports such an attempt as Disposed, which is never retried. A borrowed
        // HttpClient's attempt in flight keeps its own result, so its mapper never reads the flag.
        Func<bool> disposed = () => _disposed;
        var transport = new JevApiClient(
            httpClient,
            new SystemTextJsonSerializer(JevJsonContext.Default),
            new JevRawSerializer(pool),
            new JevErrorMapper(time, _ownedHttpClient is null ? null : disposed));
        var retry = RetryPolicyFor(settings);

        // The retry proxy, then the disposal guard, then the logging decorator when there is a logger, then the transport.
        // The guard answers any attempt that would start after Dispose, owned or borrowed, with Disposed instead of
        // sending it. The logging decorator sees every attempt sent with its retry number and shares the proxy's policy.
        IJevApi attempts = logger is null ? transport : new LoggingJevApi(transport, logger, retry);
        var api = new IJevApiResilienceProxy(
            new DisposalGuardJevApi(attempts, disposed), new JevApiResiliencePolicies { Retry = retry });

        // Always wired: with nothing listening, the generated proxy returns each operation's own task.
        _operations = new JevOperationsInstrumented(new JevOperations(api, "Bearer " + settings.ApiKey));
    }

    /// <summary>
    /// Configures an <see cref="HttpClient"/> as <see cref="JevClient"/> configures one it creates, except its handler, for a client you then
    /// pass to a constructor that takes an <see cref="HttpClient"/>, such as one from <c>IHttpClientFactory</c>.
    /// </summary>
    /// <param name="httpClient">The client to configure. It must not have sent a request yet.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <remarks>
    /// Sets <see cref="HttpClient.BaseAddress"/> from <see cref="JevClientOptions.BaseAddress"/>, the
    /// <see cref="JevDefaults.BaseAddressEnvironmentVariable"/> environment variable or the provider's default, only when
    /// <paramref name="httpClient"/> has none. Sets <see cref="HttpClient.Timeout"/> to <see cref="JevClientOptions.Timeout"/>,
    /// the per-attempt time-out, and adds the <c>ZeroAlloc.Jev</c> User-Agent unless it is already there. It reads no
    /// API key, so it can run before one is configured.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The provider, the time-out or the base address option is invalid.</exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid, or <paramref name="httpClient"/> has already sent a request.</exception>
    public static void ConfigureHttpClient(HttpClient httpClient, JevClientOptions? options)
        => ConfigureHttpClient(httpClient, options, Environment.GetEnvironmentVariable);

    // environment reads an environment variable; tests pass a fake.
    internal static void ConfigureHttpClient(HttpClient httpClient, JevClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ApplyHttpSettings(httpClient, JevClientSettings.ResolveHttp(options, environment));
    }

    /// <inheritdoc />
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);
        return WithLogging(
            _operations.EvaluateAsync(request, _providerName, _endpoint, cancellationToken),
            JevLog.Evaluate,
            request.Model,
            _logger is null ? 0 : request.Questions is { } questions ? questions.Count : 0,
            started,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Calls <see cref="EvaluateAsync{T}(string, CancellationToken)"/> without cancellation.</remarks>
    public ValueTask<Result<T, JevError>> EvaluateAsync<T>(string state)
        where T : IJevQuestionSet<T>
        => EvaluateAsync<T>(state, CancellationToken.None);

    /// <inheritdoc />
    /// <remarks>
    /// Writes the request straight from <typeparamref name="T"/>'s questions, with <see cref="JevClientOptions.Model"/>,
    /// and reads the typed answers straight from the response body, in pooled buffers, without building a
    /// <see cref="SystemOneRequest"/> or <see cref="SystemOneResponse"/>. Retries and errors work as for
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/>.
    /// </remarks>
    public ValueTask<Result<T, JevError>> EvaluateAsync<T>(string state, CancellationToken cancellationToken)
        where T : IJevQuestionSet<T>
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);
        return EvaluateGeneratedAsync<T>(started, TypedRequestWriter.Write(T.QuestionsUtf8, state, _model, _pool), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Calls <see cref="EvaluateAsync{T}(JsonElement, CancellationToken)"/> without cancellation.</remarks>
    public ValueTask<Result<T, JevError>> EvaluateAsync<T>(JsonElement state)
        where T : IJevQuestionSet<T>
        => EvaluateAsync<T>(state, CancellationToken.None);

    /// <inheritdoc />
    /// <remarks>
    /// Writes the request straight from <typeparamref name="T"/>'s questions, with <see cref="JevClientOptions.Model"/>,
    /// and reads the typed answers straight from the response body, in pooled buffers, without building a
    /// <see cref="SystemOneRequest"/> or <see cref="SystemOneResponse"/>. Retries and errors work as for
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/>.
    /// </remarks>
    public ValueTask<Result<T, JevError>> EvaluateAsync<T>(JsonElement state, CancellationToken cancellationToken)
        where T : IJevQuestionSet<T>
    {
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);
        return EvaluateGeneratedAsync<T>(started, TypedRequestWriter.Write(T.QuestionsUtf8, state, _model, _pool), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes the request straight from <typeparamref name="T"/>'s questions, with <see cref="JevClientOptions.Model"/>,
    /// and reads the typed answers straight from the response body, in pooled buffers, without building a
    /// <see cref="SystemOneRequest"/> or <see cref="SystemOneResponse"/>. Retries and errors work as for
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/>.
    /// </remarks>
    public ValueTask<Result<T, JevError>> EvaluateUtf8Async<T>(ReadOnlyMemory<byte> utf8JsonState, CancellationToken cancellationToken = default)
        where T : IJevQuestionSet<T>
    {
        TypedEvaluation.EnsureStateJson(utf8JsonState.Span, nameof(utf8JsonState));
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);
        return EvaluateGeneratedAsync<T>(started, TypedRequestWriter.WriteUtf8(T.QuestionsUtf8, utf8JsonState.Span, _model, _pool), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Calls <see cref="EvaluateAsync{T, TState}(TState, JsonTypeInfo{TState}, CancellationToken)"/> without cancellation.
    /// </remarks>
    public ValueTask<Result<T, JevError>> EvaluateAsync<T, TState>(TState state, JsonTypeInfo<TState> stateTypeInfo)
        where T : IJevQuestionSet<T, TState>
        => EvaluateAsync<T, TState>(state, stateTypeInfo, CancellationToken.None);

    /// <inheritdoc />
    /// <remarks>
    /// Writes the request straight from <typeparamref name="T"/>'s questions, with <see cref="JevClientOptions.Model"/>,
    /// and reads the typed answers straight from the response body, in pooled buffers, without building a
    /// <see cref="SystemOneRequest"/> or <see cref="SystemOneResponse"/>. Retries and errors work as for
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/>.
    /// </remarks>
    public ValueTask<Result<T, JevError>> EvaluateAsync<T, TState>(TState state, JsonTypeInfo<TState> stateTypeInfo, CancellationToken cancellationToken)
        where T : IJevQuestionSet<T, TState>
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        ArgumentNullException.ThrowIfNull(stateTypeInfo);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);
        return EvaluateGeneratedAsync<T>(started, TypedRequestWriter.Write(T.QuestionsUtf8, state, stateTypeInfo, _model, _pool), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>Calls <see cref="EvaluateAsync(JevQuestionSet, JevContent, CancellationToken)"/> without cancellation.</remarks>
    public ValueTask<Result<JevAnswers, JevError>> EvaluateAsync(JevQuestionSet questionSet, JevContent state)
        => EvaluateAsync(questionSet, state, CancellationToken.None);

    /// <inheritdoc />
    /// <remarks>
    /// Writes the request straight from the set's <see cref="JevQuestionSet.QuestionsUtf8"/>, with
    /// <see cref="JevClientOptions.Model"/>, and reads the answers straight from the response body, in pooled buffers,
    /// without building a <see cref="SystemOneRequest"/> or <see cref="SystemOneResponse"/>. Retries and errors work as
    /// for <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/>.
    /// </remarks>
    public ValueTask<Result<JevAnswers, JevError>> EvaluateAsync(JevQuestionSet questionSet, JevContent state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questionSet);
        JevContent.EnsureInitialized(state, nameof(state));
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);
        var body = TypedRequestWriter.Write(questionSet.QuestionsUtf8, state, _model, _pool);
        return WithLogging(
            Evaluated.Unwrap(_operations.EvaluateBuiltSetAsync(body, questionSet, _model, _providerName, _endpoint, cancellationToken)),
            JevLog.EvaluateBuiltSet,
            _model,
            questionSet.Plan.Length,
            started,
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = JevLog.StartTiming(_logger);

        // Created before any call, so it bypasses the proxy: no request is sent, and no span or metric is recorded.
        if (_provider == JevProvider.OpenRouter)
        {
            return WithModelLogging(
                ValueTask.FromResult(Result<ModelList, JevError>.Failure(new JevError(
                    JevErrorKind.Unsupported,
                    "Model listing is only available on TypeSafe's API; OpenRouter has its own Models API."))),
                started,
                cancellationToken);
        }

        return WithModelLogging(_operations.ListModelsAsync(_providerName, _endpoint, cancellationToken), started, cancellationToken);
    }

    /// <summary>Disposes the <see cref="HttpClient"/> this client created; a borrowed one is left alone.</summary>
    /// <remarks>
    /// <para>
    /// A call started after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>. A call already in flight
    /// over an <see cref="HttpClient"/> this client created is torn down with it, and returns a
    /// <see cref="JevErrorKind.Disposed"/> failure. A real time-out that was mapped before <see cref="Dispose"/> set the
    /// flag stays <see cref="JevErrorKind.Timeout"/> when no retry is left.
    /// </para>
    /// <para>
    /// A disposed client never retries. A retry that would start after <see cref="Dispose"/> is not sent, and the call
    /// returns <see cref="JevErrorKind.Disposed"/>, whether the client created its <see cref="HttpClient"/> or borrowed
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

        // Set first: disposing the HttpClient cancels the requests in flight, and their failures must see the flag.
        _disposed = true;
        _ownedHttpClient?.Dispose();
    }

    // The four typed overloads: the request is already written, from the generated set's questions, into body. The
    // question count is a span tag on every call, so it is read every call, from a static field set once per set type,
    // and the log reuses the same value.
    private ValueTask<Result<T, JevError>> EvaluateGeneratedAsync<T>(long started, RawJson body, CancellationToken ct)
        where T : IJevQuestionSet<T>
    {
        var questionCount = GeneratedQuestionCount<T>.Value;
        return WithLogging(
            Evaluated.Unwrap(_operations.EvaluateTypedAsync<T>(body, _model, _providerName, _endpoint, questionCount, ct)),
            JevLog.EvaluateTyped,
            _model,
            questionCount,
            started,
            ct);
    }

    // Checked per call: without a logger, or with every level the operation can emit disabled, this returns call
    // itself, so the client runs exactly the unlogged code and pays no extra state machine.
    private ValueTask<Result<TResult, JevError>> WithLogging<TResult>(
        ValueTask<Result<TResult, JevError>> call, string operation, string model, int questionCount, long started, CancellationToken ct)
        => _logger is { } logger && JevLog.IsAnyEnabled(logger)
            ? LogEvaluationAsync(logger, call, operation, model, questionCount, started, ct)
            : call;

    // Logs the whole outcome once, after any retries and after typed parsing; the filter logs a thrown exception
    // without catching it, so it surfaces unchanged.
    private async ValueTask<Result<TResult, JevError>> LogEvaluationAsync<TResult>(
        ILogger logger,
        ValueTask<Result<TResult, JevError>> call,
        string operation,
        string model,
        int questionCount,
        long started,
        CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            var durationMs = JevLog.ElapsedMilliseconds(started);
            if (result.IsSuccess)
            {
                JevLog.EvaluationSucceeded(logger, operation, model, _provider, questionCount, durationMs);
            }
            else
            {
                var error = result.Error;
                var message = JevLog.SafeMessage(error);
                JevLog.EvaluationFailed(logger, operation, model, error.Kind, error.StatusCode, durationMs, message);
            }

            return result;
        }
        catch (Exception exception) when (JevLog.LogUnexpected(logger, operation, exception, ct))
        {
            throw;
        }
    }

    private ValueTask<Result<ModelList, JevError>> WithModelLogging(
        ValueTask<Result<ModelList, JevError>> call, long started, CancellationToken ct)
        => _logger is { } logger && JevLog.IsAnyEnabled(logger) ? LogModelsAsync(logger, call, started, ct) : call;

    private async ValueTask<Result<ModelList, JevError>> LogModelsAsync(
        ILogger logger, ValueTask<Result<ModelList, JevError>> call, long started, CancellationToken ct)
    {
        try
        {
            var result = await call.ConfigureAwait(false);
            var durationMs = JevLog.ElapsedMilliseconds(started);
            if (result.IsSuccess)
            {
                var modelCount = result.Value.Models.Count;
                JevLog.ModelsListed(logger, _provider, modelCount, durationMs);
            }
            else
            {
                var error = result.Error;
                var message = JevLog.SafeMessage(error);
                JevLog.ModelsListFailed(logger, _provider, error.Kind, error.StatusCode, durationMs, message);
            }

            return result;
        }
        catch (Exception exception) when (JevLog.LogUnexpected(logger, JevLog.ListModels, exception, ct))
        {
            throw;
        }
    }

    // An owned client's HttpClient.Timeout, from JevClientOptions.Timeout, bounds each attempt; a borrowed client's
    // own Timeout applies instead. Either way the policy adds no per-attempt timeout.
    private static RetryPolicy RetryPolicyFor(JevClientSettings settings)
        => new(
            maxAttempts: settings.MaxRetries + 1,
            backoffMs: (int)Math.Ceiling(settings.InitialBackoff.TotalMilliseconds),
            jitter: settings.Jitter,
            perAttemptTimeoutMs: 0,
            maxDelayMs: (int)Math.Ceiling(settings.MaxRetryDelay.TotalMilliseconds));

    // Validates a caller-supplied HttpClient.BaseAddress with the same rules as a configured address. The caller's
    // client is never modified, so unlike a configured address (which gets a trailing slash appended), the path here
    // must already end in '/'.
    private static void ValidateBorrowedBaseAddress(HttpClient httpClient)
    {
        var baseAddress = httpClient.BaseAddress!;

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("httpClient.BaseAddress must be an absolute URI.", nameof(httpClient));
        }

        if (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("httpClient.BaseAddress must use the http or https scheme.", nameof(httpClient));
        }

        if (baseAddress.Query.Length > 0 || baseAddress.Fragment.Length > 0)
        {
            throw new ArgumentException("httpClient.BaseAddress must not contain a query or fragment.", nameof(httpClient));
        }

        if (!baseAddress.AbsoluteUri.EndsWith('/'))
        {
            throw new ArgumentException("httpClient.BaseAddress must end with '/'.", nameof(httpClient));
        }
    }

    // What an owned HttpClient gets and ConfigureHttpClient applies: the base address when there is none, the per-attempt
    // time-out, and the User-Agent once.
    private static void ApplyHttpSettings(HttpClient httpClient, JevHttpSettings settings)
    {
        // Timeout first: its setter can throw, and nothing else has been changed by then.
        httpClient.Timeout = settings.Timeout;
        httpClient.BaseAddress ??= settings.BaseAddress;
        if (!httpClient.DefaultRequestHeaders.UserAgent.Contains(UserAgent))
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(UserAgent);
        }
    }

    private static JevClientSettings ResolveSettings(JevClientOptions? options)
        => JevClientSettings.Resolve(options, Environment.GetEnvironmentVariable);

    // Validates httpClient before resolving settings, so an invalid options value never masks a null httpClient.
    private static JevClientSettings ResolveSettings(HttpClient httpClient, JevClientOptions? options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return ResolveSettings(options);
    }

    // ProductInfoHeaderValue's constructor throws FormatException for a version that is not a valid product token;
    // a User-Agent header must never prevent client construction, so this falls back to "0.0.0" instead.
    private static ProductInfoHeaderValue CreateUserAgent()
    {
        try
        {
            return new ProductInfoHeaderValue("ZeroAlloc.Jev", ClientVersion());
        }
        catch (FormatException)
        {
            return new ProductInfoHeaderValue("ZeroAlloc.Jev", "0.0.0");
        }
    }

    private static string ClientVersion()
    {
        var version = typeof(JevClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
        {
            return "0.0.0";
        }

        // Strip build metadata (e.g. a source-control commit hash appended after '+' by the build), which is not
        // part of the version a caller would want to see or compare in a User-Agent header.
        var buildMetadata = version.IndexOf('+', StringComparison.Ordinal);
        return buildMetadata < 0 ? version : version[..buildMetadata];
    }
}
