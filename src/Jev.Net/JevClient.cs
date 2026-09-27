using System.Net.Http.Headers;
using System.Reflection;
using Jev.Net.Serialization;
using Jev.Net.Transport;
using ZeroAlloc.Rest.SystemTextJson;
using ZeroAlloc.Results;

namespace Jev.Net;

/// <summary>Calls TypeSafe's Jev System One API, directly or through OpenRouter.</summary>
/// <remarks>Thread-safe. Create one per application and reuse it; dispose it when the application stops.</remarks>
public sealed class JevClient : IJevClient, IDisposable
{
    private static readonly ProductInfoHeaderValue UserAgent = new("Jev.Net", ClientVersion());

    private readonly JevApiClient _api;
    private readonly HttpClient? _ownedHttpClient;
    private readonly string _authorization;
    private readonly JevProvider _provider;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="JevClient"/> class that creates and owns its <see cref="HttpClient"/>.</summary>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(JevClientOptions? options = null)
        : this(JevClientSettings.Resolve(options, Environment.GetEnvironmentVariable), httpClient: null, ownedHandler: null, TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JevClient"/> class over a caller-owned <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">The client to send requests with. Its base address is kept when set; it is not disposed.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses defaults and environment variables.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public JevClient(HttpClient httpClient, JevClientOptions? options = null)
        : this(
            JevClientSettings.Resolve(options, Environment.GetEnvironmentVariable),
            httpClient ?? throw new ArgumentNullException(nameof(httpClient)),
            ownedHandler: null,
            TimeProvider.System)
    {
    }

    internal JevClient(JevClientSettings settings, HttpClient? httpClient, HttpMessageHandler? ownedHandler, TimeProvider time)
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
        else
        {
            httpClient.BaseAddress ??= settings.BaseAddress;
        }

        _provider = settings.Provider;
        _authorization = "Bearer " + settings.ApiKey;
        _api = new JevApiClient(httpClient, new SystemTextJsonSerializer(JevJson.Options), new JevErrorMapper(time));
    }

    /// <inheritdoc />
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return EvaluateCoreAsync(request, ct);
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

    private async ValueTask<Result<SystemOneResponse, JevError>> EvaluateCoreAsync(SystemOneRequest request, CancellationToken ct)
    {
        var result = await _api.EvaluateAsync(request, _authorization, ct).ConfigureAwait(false);
        if (result.IsSuccess && (result.Value is null || HasNullAnswer(result.Value)))
        {
            return Result<SystemOneResponse, JevError>.Failure(Unreadable("The response has no answers object or contains a null answer."));
        }

        return result;
    }

    private async ValueTask<Result<ModelList, JevError>> ListModelsCoreAsync(CancellationToken ct)
    {
        var result = await _api.ListModelsAsync(_authorization, ct).ConfigureAwait(false);
        return result.IsSuccess && result.Value is null
            ? Result<ModelList, JevError>.Failure(Unreadable("The response body is null."))
            : result;
    }

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

    private static JevError Unreadable(string message) => new(JevErrorKind.InvalidResponse, message, statusCode: 200);

    private static string ClientVersion()
    {
        var version = typeof(JevClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrEmpty(version) ? "0.0.0" : version;
    }
}
