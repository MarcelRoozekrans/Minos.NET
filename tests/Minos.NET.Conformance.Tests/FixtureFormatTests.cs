using System.Text.Json;

namespace Minos.Conformance.Tests;

public sealed class FixtureFormatTests
{
    [Fact]
    public void ARecordedFixture_WithoutAModel_IsInvalid()
        => Assert.Equal("A recorded fixture names its model.", Header(ConformanceFixture.RecordedSource, model: null, recorded: "2026-10-11").HeaderProblem());

    [Fact]
    public void ARecordedFixture_WithoutADate_IsInvalid()
        => Assert.Equal("A recorded fixture names its date.", Header(ConformanceFixture.RecordedSource, model: "jev-1.13.0", recorded: null).HeaderProblem());

    [Fact]
    public void AnUnknownSource_OrABadDate_IsInvalid()
    {
        Assert.Equal("The source 'made-up' is not recorded, guide or tests.", Header("made-up", null, null).HeaderProblem());
        Assert.Equal("The date '11-10-2026' is not yyyy-MM-dd.", Header(ConformanceFixture.RecordedSource, "jev-1.13.0", "11-10-2026").HeaderProblem());
    }

    [Fact]
    public void AGuideOrTestsFixture_NeedsNoModelOrDate()
    {
        Assert.Null(Header(ConformanceFixture.GuideSource, null, null).HeaderProblem());
        Assert.Null(Header(ConformanceFixture.TestsSource, null, null).HeaderProblem());
    }

    [Fact]
    public void AnExchangeDate_MustBeADate()
    {
        var fixture = new ConformanceFixture
        {
            Target = "openai",
            Source = ConformanceFixture.GuideSource,
            Exchanges = [new ConformanceExchange { Probe = "bad-key", Recorded = "yesterday", Status = 401 }],
        };

        Assert.Equal("The bad-key exchange's date 'yesterday' is not yyyy-MM-dd.", fixture.HeaderProblem());
    }

    [Fact]
    public void ANonJsonBody_IsKeptAsText_AndSentBackAsItCame()
    {
        var exchange = ConformanceExchange.FromHttp("noul", null, 502, "text/html", null, "<html>Bad gateway</html>");

        Assert.Equal(JsonValueKind.String, exchange.Response!.Value.ValueKind);
        Assert.Equal("<html>Bad gateway</html>", exchange.ResponseText());
        Assert.False(exchange.TryGetJson(out _));
    }

    [Fact]
    public void AJsonBody_IsKeptAsJson()
    {
        var exchange = ConformanceExchange.FromHttp("noul", "{\"state\":\"s\"}", 200, "application/json; charset=utf-8", null, "{\"answers\":{}}");

        Assert.True(exchange.TryGetJson(out var body));
        Assert.Equal(JsonValueKind.Object, body.GetProperty("answers").ValueKind);
        Assert.Equal("s", exchange.Request!.Value.GetProperty("state").GetString());
    }

    [Fact]
    public void AFixture_RoundTrips_LeavingNullsOut()
    {
        var fixture = new ConformanceFixture
        {
            Target = "typesafe",
            Source = ConformanceFixture.RecordedSource,
            Model = "jev-1.13.0",
            Recorded = "2026-10-11",
            Exchanges =
            [
                ConformanceExchange.FromHttp("noul", "{}", 401, "application/json", new Dictionary<string, string>(StringComparer.Ordinal) { ["x-request-id"] = "req_1" }, "{\"error\":{}}"),
                new ConformanceExchange { Probe = "invalid", Skipped = "Not yet." },
            ],
        };

        var json = fixture.ToJson();
        var read = ConformanceFixture.Parse(json);

        Assert.Equal(json, read.ToJson());
        Assert.Equal("req_1", read.Find("noul")!.Header("X-Request-Id"));
        Assert.DoesNotContain("\"note\"", json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
    }

    private static ConformanceFixture Header(string source, string? model, string? recorded) => new()
    {
        Target = "typesafe",
        Source = source,
        Model = model,
        Recorded = recorded,
        Exchanges = [],
    };
}
