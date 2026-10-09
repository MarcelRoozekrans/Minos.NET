using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Minos.Benchmarks.Mock;

/// <summary>
/// Starts the benchmark mock: a minimal Kestrel endpoint that serves the recorded Jev answer and counts what it serves.
/// </summary>
/// <remarks>
/// <c>POST /v1/systemone</c> writes the cached response bytes as <c>application/json</c> and adds one to the count.
/// <c>GET /count</c> returns the count as a plain number, and <c>POST /count/reset</c> sets it to zero; neither is counted.
/// Every other path answers 404. There is no logging, no request-body buffering and no middleware beyond routing, so
/// the server's own per-request cost stays as small as it can be, and the same for every client.
/// </remarks>
public static class MockHost
{
    /// <summary>The path the clients call.</summary>
    public const string SystemOnePath = "/v1/systemone";

    /// <summary>The path that returns the served-request count as a plain number.</summary>
    public const string CountPath = "/count";

    /// <summary>The path that sets the served-request count back to zero.</summary>
    public const string CountResetPath = "/count/reset";

    /// <summary>Starts the server on <paramref name="port"/> of the loopback address; port 0 picks a free port.</summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="responsePath">The file whose bytes <c>POST /v1/systemone</c> returns.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The running server; dispose it to stop it and release the port.</returns>
    public static async Task<MockServer> StartAsync(int port, string responsePath, CancellationToken cancellationToken)
    {
        var body = await File.ReadAllBytesAsync(responsePath, cancellationToken).ConfigureAwait(false);
        var counter = new RequestCounter();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port));
        var app = builder.Build();

        app.MapPost(SystemOnePath, context =>
        {
            counter.Increment();
            return WriteAsync(context.Response, body, "application/json");
        });
        app.MapGet(CountPath, context => WriteAsync(context.Response, Utf8(counter.Value), "text/plain"));
        app.MapPost(CountResetPath, context =>
        {
            counter.Reset();
            return WriteAsync(context.Response, Utf8(0), "text/plain");
        });

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A failed start, such as a port already in use, must not leave the app's services behind.
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        return new MockServer(app, new Uri(address), counter);
    }

    private static Task WriteAsync(HttpResponse response, byte[] body, string contentType)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = contentType;
        response.ContentLength = body.Length;
        return response.Body.WriteAsync(body, 0, body.Length);
    }

    private static byte[] Utf8(long value) => System.Text.Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>A running benchmark mock.</summary>
public sealed class MockServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly RequestCounter _counter;

    internal MockServer(WebApplication app, Uri baseAddress, RequestCounter counter)
    {
        _app = app;
        BaseAddress = baseAddress;
        _counter = counter;
    }

    /// <summary>Gets the server's root address, such as <c>http://127.0.0.1:5005</c>.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets how many <c>POST /v1/systemone</c> requests the server has served.</summary>
    public long Count => _counter.Value;

    /// <summary>Stops the server and releases the port.</summary>
    /// <returns>A task that completes when the server has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>The served-request count, safe to update from every request at once.</summary>
internal sealed class RequestCounter
{
    private long _value;

    public long Value => Interlocked.Read(ref _value);

    public void Increment() => Interlocked.Increment(ref _value);

    public void Reset() => Interlocked.Exchange(ref _value, 0);
}
