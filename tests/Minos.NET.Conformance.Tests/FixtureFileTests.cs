using System.Text.Json;
using System.Text.RegularExpressions;

namespace Minos.Conformance.Tests;

public sealed partial class FixtureFileTests
{
    private static readonly string[] Forbidden = ["Bearer", "Authorization", "api_key", "x-api-key", "Cookie", .. ConformanceExchange.DroppedProperties];
    private static readonly string[] RootProperties = ["exchanges", "model", "note", "recorded", "source", "target"];
    private static readonly string[] ExchangeProperties = ["contentType", "headers", "probe", "recorded", "request", "response", "skipped", "status"];

    public static TheoryData<string> Files
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(Fixtures.SourceDirectory, "*.json").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(path));
            }

            return data;
        }
    }

    // The shape of an API key, such as sk-or-v1-..., sk-proj-... or sk-ant-..., not the bare prefix: a body may say "risk-based".
    // It covers sk- keys, which is the shape the three providers in scope use.
    [GeneratedRegex("sk-[A-Za-z0-9_-]{20,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyShape();

    [Fact]
    public void EveryTarget_HasAFixture()
        => Assert.Equal(
            ["chat-openrouter.json", "openai.json", "openrouter.json", "typesafe.json"],
            Directory.EnumerateFiles(Fixtures.SourceDirectory, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal));

    // The chat fixture is what a recording against the scripted network gives, so a prompt change shows here. Set
    // MINOS_WRITE_CHAT_FIXTURE=1 to write it to the repository instead, then review and commit it.
    [Fact]
    public async Task ChatFixture_IsWhatTheScriptedNetworkRecords()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-chat-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await new ConformanceRunner(static () => new ScriptedChatNetwork(), TimeProvider.System)
                .RunAsync(Fixtures.Replay(ChatTargets.OpenRouterName), ConformanceMode.Record, path);
            var recorded = ConformanceFixture.Load(path);

            // The questions of a probe are sent in parallel, so they are kept in a stable order; replay matches by body.
            var json = new ConformanceFixture
            {
                Target = recorded.Target,
                Source = ConformanceFixture.TestsSource,
                Model = recorded.Model,
                Note = "Built from the bridge's scripted responses; the live recording follows from live-smoke.",
                Exchanges = [.. recorded.Exchanges.GroupBy(exchange => exchange.Probe, StringComparer.Ordinal)
                    .SelectMany(probe => probe.OrderBy(exchange => exchange.Request?.GetRawText(), StringComparer.Ordinal))],
            }.ToJson();

            var committed = Path.Combine(Fixtures.SourceDirectory, ChatTargets.OpenRouterName + ".json");
            if (string.Equals(Environment.GetEnvironmentVariable("MINOS_WRITE_CHAT_FIXTURE"), "1", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(committed, json);
            }

            Assert.True(File.Exists(committed), "The chat fixture is missing; run this test with MINOS_WRITE_CHAT_FIXTURE=1 to write it.");
            Assert.Equal(await File.ReadAllTextAsync(committed), json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void TheHeader_IsValid_AndNamesItsTarget(string file)
    {
        var fixture = ConformanceFixture.Load(Path.Combine(Fixtures.SourceDirectory, file));

        Assert.Null(fixture.HeaderProblem());
        Assert.Equal(Path.GetFileNameWithoutExtension(file), fixture.Target);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void AFixture_HoldsNoKeyNoAuthHeaderAndOnlyAllowlistedHeaders(string file)
        => Assert.Empty(Problems(File.ReadAllText(Path.Combine(Fixtures.SourceDirectory, file))));

    [Fact]
    public void TheScan_CatchesAnAuthorizationHeader()
        => Assert.NotEmpty(Problems("""{"target":"t","exchanges":[{"probe":"p","headers":{"Authorization":"x"}}]}"""));

    [Fact]
    public void TheScan_CatchesBearerText()
        => Assert.NotEmpty(Problems("""{"target":"t","note":"Bearer abc123","exchanges":[]}"""));

    [Fact]
    public void TheScan_CatchesAnAccountIdentifier()
        => Assert.Contains("forbidden word user_id", Problems("""{"target":"t","exchanges":[{"probe":"p","response":{"user_id":"user_1"}}]}"""));

    [Fact]
    public void TheScan_CatchesAnUnlistedHeader()
        => Assert.Contains("header x-secret", Problems("""{"target":"t","exchanges":[{"probe":"p","headers":{"x-secret":"1"}}]}"""));

    [Fact]
    public void TheScan_CatchesAnUnknownRootProperty()
        => Assert.Contains("root property extra", Problems("""{"target":"t","extra":1,"exchanges":[]}"""));

    [Fact]
    public void TheScan_CatchesAnUnknownExchangeProperty()
        => Assert.Contains("exchange property extra", Problems("""{"target":"t","exchanges":[{"probe":"p","extra":1}]}"""));

    [Fact]
    public void TheScan_PassesACleanFixture()
        => Assert.Empty(Problems("""{"target":"t","exchanges":[{"probe":"p","status":200}]}"""));

    // The bodies are nested JSON, so the whole text is scanned, bodies and request bodies included.
    private static List<string> Problems(string text)
    {
        var problems = new List<string>();
        if (KeyShape().IsMatch(text))
        {
            problems.Add("key shape");
        }

        foreach (var word in Forbidden)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("forbidden word " + word);
            }
        }

        if (text.Contains(ExchangeCapture.InvalidKey, StringComparison.Ordinal))
        {
            problems.Add("the invalid key");
        }

        using var json = JsonDocument.Parse(text);
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (!RootProperties.Contains(property.Name))
            {
                problems.Add("root property " + property.Name);
            }
        }

        foreach (var exchange in json.RootElement.GetProperty("exchanges").EnumerateArray())
        {
            foreach (var property in exchange.EnumerateObject())
            {
                if (!ExchangeProperties.Contains(property.Name))
                {
                    problems.Add("exchange property " + property.Name);
                }
            }

            if (exchange.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateObject())
                {
                    if (!ExchangeCapture.RecordedHeaders.Contains(header.Name))
                    {
                        problems.Add("header " + header.Name);
                    }
                }
            }
        }

        return problems;
    }

    [Fact]
    public void KeyShape_AllowsOrdinaryWordsThatContainSk()
        => Assert.DoesNotMatch(KeyShape(), "a risk-based check of the task-list");

    [Fact]
    public void KeyShape_CatchesAKey()
        => Assert.Matches(KeyShape(), "sk-or-v1-" + new string('a', 40));
}
