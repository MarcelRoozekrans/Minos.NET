using Minos.SystemOne;

namespace Minos.Conformance.Tests;

public sealed class RunnerTests : IDisposable
{
    private static readonly DateTimeOffset Today = new(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "minos-conformance-" + Guid.NewGuid().ToString("N"));

    public RunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Record_WritesARecordedFixture_WithTheModelAndTheDate()
    {
        var network = new Network(Fixture("typesafe"));
        var path = Path.Combine(_directory, "typesafe.json");

        var report = await Runner(network).RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Record, path);

        var recorded = ConformanceFixture.Load(path);
        Assert.Equal(ConformanceMode.Record, report.Mode);
        Assert.Equal(0, report.Failed);
        Assert.Equal("typesafe", recorded.Target);
        Assert.Equal(ConformanceFixture.RecordedSource, recorded.Source);
        Assert.Equal("jev-1.13.0", recorded.Model);
        Assert.Equal("2026-10-11", recorded.Recorded);
        Assert.Null(recorded.HeaderProblem());
        Assert.Equal(ConformanceProbe.All.Select(probe => probe.Id), recorded.Exchanges.Select(exchange => exchange.Probe));
    }

    [Fact]
    public async Task Record_KeepsTheRequestBody_AndOnlyTheAllowlistedHeaders()
    {
        var network = new Network(Fixture("typesafe"), ("x-typesafe-request-id", "req_rec"), ("set-cookie", "session=secret"), ("CF-RAY", "8c1f-AMS"));
        var path = Path.Combine(_directory, "typesafe.json");

        await Runner(network).RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Record, path);

        var noul = ConformanceFixture.Load(path).Find("noul")!;
        Assert.Equal(["is_urgent"], noul.Request!.Value.GetProperty("questions").EnumerateObject().Select(question => question.Name));
        Assert.Equal(["x-typesafe-request-id"], noul.Headers!.Keys);
        Assert.DoesNotContain("test-key", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBadKeyProbe_SendsTheInvalidKey_AndEveryOtherProbeTheTargetsKey()
    {
        var network = new Network(Fixture("typesafe"));

        await Runner(network).RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Live);

        Assert.Equal("Bearer " + ExchangeCapture.InvalidKey, network.Authorization["bad-key"]);
        Assert.All(
            network.Authorization.Where(sent => !string.Equals(sent.Key, "bad-key", StringComparison.Ordinal)),
            sent => Assert.Equal("Bearer test-key", sent.Value));
    }

    [Fact]
    public async Task Live_SendsEveryProbe_AndWritesNoFixture()
    {
        var network = new Network(Fixture("typesafe"));

        var report = await Runner(network).RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Live, Path.Combine(_directory, "unused.json"));

        Assert.Equal(ConformanceMode.Live, report.Mode);
        Assert.Null(report.FixtureSource);
        Assert.Equal(ConformanceProbe.All.Select(probe => probe.Id), network.Authorization.Keys);
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task Replay_AnUnknownProbeId_FailsTheRun()
    {
        var path = Write(Fixture("typesafe").Replace("\"probe\": \"keyed\"", "\"probe\": \"keyed-v2\"", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ConformanceRunner().RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Replay, path));

        Assert.Equal("The fixture names probes the suite does not have: keyed-v2.", error.Message);
    }

    [Fact]
    public async Task Replay_AMissingProbeId_FailsTheRun()
    {
        var fixture = ConformanceFixture.Load(Fixtures.Path("typesafe"));
        var path = Path.Combine(_directory, "typesafe.json");
        new ConformanceFixture
        {
            Target = fixture.Target,
            Source = fixture.Source,
            Exchanges = [.. fixture.Exchanges.Where(exchange => !string.Equals(exchange.Probe, "keyed", StringComparison.Ordinal))],
        }.Save(path);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ConformanceRunner().RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Replay, path));

        Assert.Equal("The fixture has no exchange for these probes: keyed.", error.Message);
    }

    [Fact]
    public async Task Replay_AnotherTargetsFixture_FailsTheRun()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ConformanceRunner().RunAsync(SystemOneTargets.OpenRouter("test-key"), ConformanceMode.Replay, Fixtures.Path("typesafe")));

        Assert.Equal("The fixture is for typesafe, not openrouter.", error.Message);
    }

    [Fact]
    public async Task AProbeTheCapabilitiesRule_Out_IsSkippedWithTheReason_AndNeedsNoExchange()
    {
        var path = Write(Fixture("typesafe"));
        var target = new ConformanceTarget("typesafe", ConformanceWire.SystemOne, "jev-latest", ModelRule.TypeSafe, http =>
        {
            var options = new SystemOneClientOptions { ApiKey = "test-key", Capabilities = new DecisionCapabilities { QuestionKinds = QuestionKinds.Noul } };
            SystemOneClient.ConfigureHttpClient(http, options);
            return new SystemOneClient(http, options);
        });

        var report = await new ConformanceRunner().RunAsync(target, ConformanceMode.Replay, path);

        Assert.Equal(
            ["The target does not accept Choice questions.", "The target does not accept Score questions.", "The target does not accept Choice questions.", "The target does not accept Choice questions."],
            report.Probes.Where(probe => probe.Probe is "choice" or "score" or "mixed" or "keyed").Select(probe => probe.Skipped));
        Assert.Equal(0, report.Failed);
    }

    [Fact]
    public async Task RecordAndReplay_NeedAFixturePath()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ConformanceRunner().RunAsync(SystemOneTargets.TypeSafe("k"), ConformanceMode.Replay));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ConformanceRunner().RunAsync(SystemOneTargets.TypeSafe("k"), ConformanceMode.Record));
    }

    [Fact]
    public async Task Replay_ARepeatedProbeId_FailsTheRunBeforeAnyProbeRuns()
    {
        var fixture = ConformanceFixture.Load(Fixtures.Path("typesafe"));
        var path = Path.Combine(_directory, "typesafe.json");
        new ConformanceFixture
        {
            Target = fixture.Target,
            Source = fixture.Source,
            Exchanges = [.. fixture.Exchanges, fixture.Find("noul")!],
        }.Save(path);
        var network = new Network(Fixture("typesafe"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => Runner(network).RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Replay, path));

        Assert.Equal("The fixture names these probes more than once: noul.", error.Message);
        Assert.Empty(network.Authorization);
    }

    [Fact]
    public async Task Replay_AnExchangeThatIsSkipped_SkipsItsChecksWithTheReason()
    {
        var fixture = ConformanceFixture.Load(Fixtures.Path("typesafe"));
        var path = Path.Combine(_directory, "typesafe.json");
        new ConformanceFixture
        {
            Target = fixture.Target,
            Source = fixture.Source,
            Exchanges = [.. fixture.Exchanges.Select(exchange => string.Equals(exchange.Probe, "invalid", StringComparison.Ordinal)
                ? new ConformanceExchange { Probe = "invalid", Skipped = "No invalid-request body recorded yet." }
                : exchange)],
        }.Save(path);

        var report = await new ConformanceRunner().RunAsync(SystemOneTargets.TypeSafe("test-key"), ConformanceMode.Replay, path);

        var invalid = Assert.Single(report.Probes, probe => string.Equals(probe.Probe, "invalid", StringComparison.Ordinal));
        Assert.Equal("The fixture skips it: No invalid-request body recorded yet.", invalid.Skipped);
        Assert.NotEmpty(invalid.Checks);
        Assert.All(invalid.Checks, check =>
        {
            Assert.Equal(CheckOutcome.Skipped, check.Outcome);
            Assert.Equal("The fixture skips it: No invalid-request body recorded yet.", check.Detail);
        });
        Assert.Equal(0, report.Failed);
    }

    [Fact]
    public async Task TheBadKeyProbe_NeverSendsAKeyThatDoesNotParseAsACredential()
    {
        var sent = new List<string>();
        using var recorder = new Recorder(sent);
        using var capture = new ExchangeCapture(recorder);
        using var http = new HttpClient(capture);
        var badKey = ConformanceProbe.All.First(probe => probe.Kind == ProbeKind.BadKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://example.test/"));
        Assert.True(request.Headers.TryAddWithoutValidation("Authorization", "Bearer real,key"));

        capture.Begin(badKey);
        await http.SendAsync(request);

        Assert.Equal(["Bearer " + ExchangeCapture.InvalidKey], sent);
    }

    [Fact]
    public async Task AFixtureHandler_RefusesASecondRequestForTheSameProbe()
    {
        using var capture = new ExchangeCapture(new FixtureHandler(ConformanceFixture.Load(Fixtures.Path("typesafe"))));
        using var http = new HttpClient(capture);
        capture.Begin(ConformanceProbe.All.First(probe => string.Equals(probe.Id, "noul", StringComparison.Ordinal)));

        using var first = await http.PostAsync(new Uri("https://example.test/"), new StringContent("{}"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => http.PostAsync(new Uri("https://example.test/"), new StringContent("{}")));

        Assert.Contains("noul", error.Message, StringComparison.Ordinal);
        Assert.Contains("second request", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestOutsideAProbe_IsRefused()
    {
        using var capture = new ExchangeCapture(new Network(Fixture("typesafe")));
        using var http = new HttpClient(capture);

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync(new Uri("https://example.test/")));
    }

    private static ConformanceRunner Runner(Network network) => new(() => network, new FixedTime(Today));

    private static string Fixture(string target) => File.ReadAllText(Fixtures.Path(target));

    private string Write(string json)
    {
        var path = Path.Combine(_directory, "fixture.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// Stands in for the network: answers from a fixture, adds the given headers to every response, and keeps the
    /// <c>Authorization</c> each probe sent.
    /// </summary>
    private sealed class Network(string fixtureJson, params (string Name, string Value)[] headers) : DelegatingHandler(new FixtureHandler(ConformanceFixture.Parse(fixtureJson)))
    {
        public Dictionary<string, string?> Authorization { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var probe = request.Options.TryGetValue(ExchangeCapture.ProbeOption, out var id) ? id : "none";
            Authorization[probe] = request.Headers.Authorization?.ToString();
            var response = await base.SendAsync(request, cancellationToken);
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }

    private sealed class Recorder(List<string> sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Add(string.Join(", ", request.Headers.GetValues("Authorization")));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
