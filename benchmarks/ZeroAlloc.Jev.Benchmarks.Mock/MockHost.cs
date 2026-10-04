using System.Globalization;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;
using WireMock.Types;
using WireMock.Util;

namespace ZeroAlloc.Jev.Benchmarks.Mock;

/// <summary>
/// Starts the WireMock server that serves the recorded Jev answer to every benchmark client, and counts the answers it
/// serves.
/// </summary>
/// <remarks>
/// WireMock's request log is off: keeping an entry per request costs memory and time that would distort a long run.
/// The server counts served <c>POST /v1/systemone</c> requests itself instead. <c>GET /count</c> returns the count as
/// plain text, and <c>POST /count/reset</c> sets it back to zero.
/// </remarks>
public static class MockHost
{
    /// <summary>The path that returns the served-request count as a plain number.</summary>
    public const string CountPath = "/count";

    /// <summary>The path that sets the served-request count back to zero.</summary>
    public const string CountResetPath = "/count/reset";

    /// <summary>Starts the server on <paramref name="port"/>; port 0 picks a free port.</summary>
    /// <param name="port">The port to listen on, on the loopback address.</param>
    /// <param name="responsePath">The file whose bytes <c>POST /v1/systemone</c> returns.</param>
    /// <returns>The running server; stop it to release the port.</returns>
    public static WireMockServer Start(int port, string responsePath)
    {
        var body = File.ReadAllBytes(responsePath);
        var counter = new RequestCounter();
        var server = WireMockServer.Start(new WireMockServerSettings
        {
            Urls = [$"http://127.0.0.1:{port}"],
            StartAdminInterface = false,

            // Zero keeps no log entry: WireMock trims the log to this count after every request.
            MaxRequestLogCount = 0,
        });
        server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithCallback(_ =>
            {
                counter.Increment();
                return Message(body, "application/json");
            }));
        server
            .Given(Request.Create().WithPath(CountPath).UsingGet())
            .RespondWith(Response.Create().WithCallback(_ => Text(counter.Value)));
        server
            .Given(Request.Create().WithPath(CountResetPath).UsingPost())
            .RespondWith(Response.Create().WithCallback(_ =>
            {
                counter.Reset();
                return Text(0);
            }));
        return server;
    }

    private static ResponseMessage Text(long value)
        => Message(System.Text.Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture)), "text/plain");

    private static ResponseMessage Message(byte[] body, string contentType)
    {
        var message = new ResponseMessage
        {
            StatusCode = 200,
            BodyData = new BodyData { BodyAsBytes = body, DetectedBodyType = BodyType.Bytes },
        };
        message.AddHeader("Content-Type", contentType);
        return message;
    }

    private sealed class RequestCounter
    {
        private long _value;

        public long Value => Interlocked.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);

        public void Reset() => Interlocked.Exchange(ref _value, 0);
    }
}
