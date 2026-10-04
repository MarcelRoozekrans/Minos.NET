using System.Net;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>
/// The one transport every client that accepts an <see cref="HttpClient"/> gets, so the comparison measures the
/// clients and not their handlers: a <see cref="SocketsHttpHandler"/> with a two-minute pooled-connection lifetime,
/// automatic decompression off and the default connections per server.
/// </summary>
public static class BenchmarkTransport
{
    // Every client CreateHttpClient made and has not been collected, so a test can check an adapter's client came from here.
    private static readonly ConditionalWeakTable<HttpClient, object> Created = [];

    /// <summary>How long a pooled connection lives before the handler replaces it.</summary>
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Creates the handler.</summary>
    /// <returns>A new handler; the caller disposes it, or the <see cref="HttpClient"/> that owns it does.</returns>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        PooledConnectionLifetime = PooledConnectionLifetime,
        AutomaticDecompression = DecompressionMethods.None,
    };

    /// <summary>Creates a long-lived <see cref="HttpClient"/> that owns a new <see cref="CreateHandler"/> handler.</summary>
    /// <param name="baseAddress">The client's base address, or <see langword="null"/> for none.</param>
    /// <returns>A new client; the caller disposes it.</returns>
    public static HttpClient CreateHttpClient(Uri? baseAddress = null)
    {
        var http = new HttpClient(CreateHandler(), disposeHandler: true);
        if (baseAddress is not null)
        {
            http.BaseAddress = baseAddress;
        }

        Created.AddOrUpdate(http, Created);
        return http;
    }

    /// <summary>Gets a value indicating whether <paramref name="http"/> came from <see cref="CreateHttpClient"/>.</summary>
    /// <param name="http">A client.</param>
    /// <returns><see langword="true"/> when this class created it.</returns>
    internal static bool IsFromHere(HttpClient http) => Created.TryGetValue(http, out _);
}
