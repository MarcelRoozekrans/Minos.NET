using System.Buffers;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Transport;
using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev;

/// <summary>Calls TypeSafe's Jev System One API, directly or through OpenRouter.</summary>
/// <remarks>Thread-safe. Create one per application and reuse it; dispose it when the application stops.</remarks>
public sealed class JevClient : IJevClient, IDisposable
{
    private static readonly ProductInfoHeaderValue UserAgent = CreateUserAgent();

    private readonly IJevApiResilienceProxy _api;
    private readonly HttpClient? _ownedHttpClient;
    private readonly string _authorization;
    private readonly string _model;
    private readonly ArrayPool<byte> _pool;
    private readonly JevProvider _provider;
    private bool _disposed;

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
    {
        if (httpClient is null)
        {
            httpClient = new HttpClient(ownedHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            {
                BaseAddress = settings.BaseAddress,
                Timeout = settings.Timeout,
            };
            httpClient.DefaultRequestHeaders.UserAgent.Add(UserAgent);
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
        _authorization = "Bearer " + settings.ApiKey;
        _model = settings.Model;
        _pool = pool;
        var transport = new JevApiClient(
            httpClient,
            new SystemTextJsonSerializer(JevJsonContext.Default),
            new JevRawSerializer(pool),
            new JevErrorMapper(time));
        _api = new IJevApiResilienceProxy(transport, new JevApiResiliencePolicies { Retry = RetryPolicyFor(settings) });
    }

    /// <inheritdoc />
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateCoreAsync(request, ct);
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
    public ValueTask<Result<T, JevError>> EvaluateAsync<T>(string state, CancellationToken ct)
        where T : IJevQuestionSet<T>
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateTypedAsync(
            TypedRequestWriter.Write(T.QuestionsUtf8, state, _model, _pool),
            GeneratedAnswerParser<T>.Instance,
            ct);
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
    public ValueTask<Result<T, JevError>> EvaluateAsync<T>(JsonElement state, CancellationToken ct)
        where T : IJevQuestionSet<T>
    {
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateTypedAsync(
            TypedRequestWriter.Write(T.QuestionsUtf8, state, _model, _pool),
            GeneratedAnswerParser<T>.Instance,
            ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes the request straight from <typeparamref name="T"/>'s questions, with <see cref="JevClientOptions.Model"/>,
    /// and reads the typed answers straight from the response body, in pooled buffers, without building a
    /// <see cref="SystemOneRequest"/> or <see cref="SystemOneResponse"/>. Retries and errors work as for
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/>.
    /// </remarks>
    public ValueTask<Result<T, JevError>> EvaluateUtf8Async<T>(ReadOnlyMemory<byte> utf8JsonState, CancellationToken ct = default)
        where T : IJevQuestionSet<T>
    {
        TypedEvaluation.EnsureStateJson(utf8JsonState.Span, nameof(utf8JsonState));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateTypedAsync(
            TypedRequestWriter.WriteUtf8(T.QuestionsUtf8, utf8JsonState.Span, _model, _pool),
            GeneratedAnswerParser<T>.Instance,
            ct);
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
    public ValueTask<Result<T, JevError>> EvaluateAsync<T, TState>(TState state, JsonTypeInfo<TState> stateTypeInfo, CancellationToken ct)
        where T : IJevQuestionSet<T, TState>
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        ArgumentNullException.ThrowIfNull(stateTypeInfo);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateTypedAsync(
            TypedRequestWriter.Write(T.QuestionsUtf8, state, stateTypeInfo, _model, _pool),
            GeneratedAnswerParser<T>.Instance,
            ct);
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
    public ValueTask<Result<JevAnswers, JevError>> EvaluateAsync(JevQuestionSet questionSet, JevContent state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(questionSet);
        JevContent.EnsureInitialized(state, nameof(state));
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateTypedAsync(TypedRequestWriter.Write(questionSet.QuestionsUtf8, state, _model, _pool), questionSet.Parser, ct);
    }

    /// <inheritdoc />
    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_provider == JevProvider.OpenRouter)
        {
            return ValueTask.FromResult(Result<ModelList, JevError>.Failure(new JevError(
                JevErrorKind.Unsupported,
                "Model listing is only available on TypeSafe's API; OpenRouter has its own Models API.")));
        }

        return ListModelsCoreAsync(ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownedHttpClient?.Dispose();
    }

    // ZeroAlloc.Rest 3.0 itself rejects an empty or null success body as a Deserialization error before this runs, since
    // SystemOneResponse is non-nullable in IJevApi; only a null value nested inside a non-null response, such as an
    // answer, still needs to be caught here.
    private async ValueTask<Result<SystemOneResponse, JevError>> EvaluateCoreAsync(SystemOneRequest request, CancellationToken ct)
    {
        var result = await _api.EvaluateAsync(request, _authorization, retryCount: null, ct).ConfigureAwait(false);

        if (result.IsSuccess && HasNullAnswer(result.Value))
        {
            return Result<SystemOneResponse, JevError>.Failure(Unreadable("The response contains a null answer."));
        }

        return result;
    }

    // Owns body: the retry proxy sends the same instance on every attempt, so it is disposed only once the whole call,
    // retries included, has completed. A successful response is the only RawJson the transport hands back; failed
    // attempts carry a JevError and no buffer.
    private async ValueTask<Result<TResult, JevError>> EvaluateTypedAsync<TResult>(
        RawJson body, AnswerParser<TResult> parse, CancellationToken ct)
    {
        RawJson? response = null;
        try
        {
            var result = await _api.EvaluateRawAsync(body, _authorization, retryCount: null, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<TResult, JevError>.Failure(result.Error);
            }

            response = result.Value;
            return TypedEvaluation.ParseResponse(response.Span, parse, statusCode: 200);
        }
        finally
        {
            response?.Dispose();
            body.Dispose();
        }
    }

    // ZeroAlloc.Rest 3.0 itself rejects an empty or null success body as a Deserialization error before this runs,
    // since ModelList is non-nullable in IJevApi, so no null check remains needed here.
    private ValueTask<Result<ModelList, JevError>> ListModelsCoreAsync(CancellationToken ct)
        => _api.ListModelsAsync(_authorization, retryCount: null, ct);

    // An owned client's HttpClient.Timeout, from JevClientOptions.Timeout, bounds each attempt; a borrowed client's
    // own Timeout applies instead. Either way the policy adds no per-attempt timeout.
    private static RetryPolicy RetryPolicyFor(JevClientSettings settings)
        => new(
            maxAttempts: settings.MaxRetries + 1,
            backoffMs: (int)Math.Ceiling(settings.InitialBackoff.TotalMilliseconds),
            jitter: settings.Jitter,
            perAttemptTimeoutMs: 0,
            maxDelayMs: (int)Math.Ceiling(settings.MaxRetryDelay.TotalMilliseconds));

    // System.Text.Json does not apply nullable annotations to dictionary values, so a null answer can arrive.
    private static bool HasNullAnswer(SystemOneResponse response)
    {
        foreach (var answer in response.Answers.Values)
        {
            if (answer is null)
            {
                return true;
            }
        }

        return false;
    }

    // statusCode 200 is a placeholder: ZeroAlloc.Rest's generated client does not expose the real status of a
    // successful response that this client itself then rejects as unreadable, so 200 is kept only because that is
    // the status that let the response through in the first place.
    private static JevError Unreadable(string message) => new(JevErrorKind.InvalidResponse, message, statusCode: 200);

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
