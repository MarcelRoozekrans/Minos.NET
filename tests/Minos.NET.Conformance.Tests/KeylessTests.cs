using System.Net;
using Minos.SystemOne;

namespace Minos.Conformance.Tests;

public sealed class KeylessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "minos-keyless-" + Guid.NewGuid().ToString("N"));

    public KeylessTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ConformanceTarget Target(string? apiKey)
        => new("custom", ConformanceWire.SystemOne, "m", modelRule: null, http =>
        {
            var options = new SystemOneDecisionClientOptions
            {
                Provider = SystemOneProvider.Custom,
                BaseAddress = new Uri("http://fake.local/"),
                ApiKey = apiKey,
                Model = "m",
            };
            SystemOneDecisionClient.ConfigureHttpClient(http, options);
            return new SystemOneDecisionClient(http, options);
        });

    private static CheckResult E2(ConformanceReport report)
        => report.Probes.Single(p => string.Equals(p.Probe, "bad-key", StringComparison.Ordinal)).Checks.Single(c => string.Equals(c.Id, "E2", StringComparison.Ordinal));

    private static ConformanceRunner Runner(HttpStatusCode status)
        => new(() => new Answering(status), TimeProvider.System);

    [Fact]
    public async Task Keyless_BadKeyProbe_SkipsE2()
    {
        var report = await Runner(HttpStatusCode.OK).RunAsync(Target(apiKey: null), ConformanceMode.Live);

        var e2 = E2(report);
        Assert.Equal(CheckOutcome.Skipped, e2.Outcome);
        Assert.Contains("no API key", e2.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keyed_BadKeyProbe_AnsweredWith200_FailsE2()
    {
        var report = await Runner(HttpStatusCode.OK).RunAsync(Target("k"), ConformanceMode.Live);

        Assert.Equal(CheckOutcome.Fail, E2(report).Outcome);
    }

    [Fact]
    public async Task Keyed_BadKeyProbe_AnsweredWith401_PassesE2()
    {
        var report = await Runner(HttpStatusCode.Unauthorized).RunAsync(Target("k"), ConformanceMode.Live);

        Assert.Equal(CheckOutcome.Pass, E2(report).Outcome);
    }

    [Fact]
    public async Task Keyless_Record_WritesTheBadKeyProbeAsSkipped_NotAnExchangeReplayWouldFail()
    {
        var path = Path.Combine(_directory, "custom.json");

        await Runner(HttpStatusCode.OK).RunAsync(Target(apiKey: null), ConformanceMode.Record, path);

        var exchange = ConformanceFixture.Load(path).Find("bad-key")!;
        Assert.NotNull(exchange.Skipped);
        Assert.Null(exchange.Status);
    }

    private sealed class Answering(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"detail\":\"x\"}", System.Text.Encoding.UTF8, "application/json") });
    }
}
