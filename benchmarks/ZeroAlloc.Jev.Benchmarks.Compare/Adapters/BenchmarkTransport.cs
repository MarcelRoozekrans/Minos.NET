using System.Net;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>
/// The one transport every client that accepts an <see cref="HttpClient"/> gets, so the comparison measures the
/// clients and not their handlers: a <see cref="SocketsHttpHandler"/> with a two-minute pooled-connection lifetime,
/// automatic decompression off and the default connections per server.
/// </summary>
public static class BenchmarkTransport
{
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

        return http;
    }
}
