using System.Text.Json;
using System.Text.RegularExpressions;

namespace Minos.Conformance.Tests;

public sealed partial class FixtureFileTests
{
    private static readonly string[] Forbidden = ["Bearer", "Authorization", "api_key", "x-api-key", "Cookie"];
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
    [GeneratedRegex("sk-[A-Za-z0-9_-]{20,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyShape();

    [Fact]
    public void EveryTarget_HasAFixture()
        => Assert.Equal(
            ["openai.json", "openrouter.json", "typesafe.json"],
            Directory.EnumerateFiles(Fixtures.SourceDirectory, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal));

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
    {
        var text = File.ReadAllText(Path.Combine(Fixtures.SourceDirectory, file));
        using var json = JsonDocument.Parse(text);

        // The bodies are nested JSON, so the whole file's text is scanned, bodies and request bodies included.
        Assert.DoesNotMatch(KeyShape(), text);
        Assert.All(Forbidden, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ExchangeCapture.InvalidKey, text, StringComparison.Ordinal);
        foreach (var property in json.RootElement.EnumerateObject())
        {
            Assert.Contains(property.Name, RootProperties);
        }

        foreach (var exchange in json.RootElement.GetProperty("exchanges").EnumerateArray())
        {
            foreach (var property in exchange.EnumerateObject())
            {
                Assert.Contains(property.Name, ExchangeProperties);
            }

            if (exchange.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateObject())
                {
                    Assert.Contains(header.Name, ExchangeCapture.RecordedHeaders);
                }
            }
        }
    }

    [Fact]
    public void KeyShape_AllowsOrdinaryWordsThatContainSk()
        => Assert.DoesNotMatch(KeyShape(), "a risk-based check of the task-list");

    [Fact]
    public void KeyShape_CatchesAKey()
        => Assert.Matches(KeyShape(), "sk-or-v1-" + new string('a', 40));
}
