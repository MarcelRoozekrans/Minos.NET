using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Minos.Samples.Tests;

public sealed partial class RecordingsSafetyTests
{
    private static readonly string[] Forbidden = ["Bearer", "api_key", "Authorization"];
    private static readonly string[] AllowedRootProperties = ["entries", "model", "provider", "recorded"];
    private static readonly string[] AllowedEntryProperties = ["requestHash", "responseBody"];

    // The shape of an API key, such as OpenRouter's sk-or-v1-..., not the bare prefix: a response body may say "risk-based".
    [GeneratedRegex("sk-[A-Za-z0-9_-]{20,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KeyShape();

    public static TheoryData<string> Recordings
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root, "samples"), "*.json", SearchOption.AllDirectories))
            {
                if (IsRecording(path) && !InBuildOutput(path))
                {
                    data.Add(Path.GetRelativePath(Repository.Root, path));
                }
            }

            return data;
        }
    }

    // The provider files in recordings/ arrive with the first recording runs. EveryProviderSample_HasARecordingPerProvider
    // is added with them and checks them: issue #166.
    [Fact]
    public void EverySingleProviderSample_HasRecordings()
    {
        var samples = SampleFolders().Where(dir => !IsProviderSample(dir)).ToArray();

        Assert.NotEmpty(samples);
        Assert.All(samples, dir => Assert.True(File.Exists(Path.Combine(dir, "recordings.json")), dir + " has no recordings.json"));
    }

    // CI replays every recordings.json without --provider, so a sample with several providers must not have one.
    [Fact]
    public void EveryProviderSample_HasNoSingleRecordingsFile()
    {
        var samples = SampleFolders().Where(IsProviderSample).ToArray();

        Assert.NotEmpty(samples);
        Assert.All(samples, dir => Assert.False(File.Exists(Path.Combine(dir, "recordings.json")), dir + " has a recordings.json"));
    }

    [Fact]
    public void KeyShape_AllowsOrdinaryWordsThatContainSk()
        => Assert.DoesNotMatch(KeyShape(), "a risk-based check of the task-list");

    [Fact]
    public void KeyShape_CatchesAnOpenRouterKey()
        => Assert.Matches(KeyShape(), "sk-or-v1-" + new string('a', 40));

    [Theory]
    [MemberData(nameof(Recordings))]
    public void Recordings_HoldNoKeyAndNoHeaders(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Repository.Root, relativePath));
        using var json = JsonDocument.Parse(text);

        // The response bodies are nested JSON, so the raw text of the whole file is scanned, bodies included.
        Assert.DoesNotMatch(KeyShape(), text);
        Assert.All(Forbidden, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AllowedRootProperties, json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.NotEmpty(json.RootElement.GetProperty("entries").EnumerateArray());
        Assert.All(
            json.RootElement.GetProperty("entries").EnumerateArray(),
            entry => Assert.Equal(AllowedEntryProperties, entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
    }

    private static string[] SampleFolders()
        => [.. Directory.EnumerateDirectories(Path.Combine(Repository.Root, "samples"), "Minos.NET.Samples.*")
            .Where(dir => !string.Equals(Path.GetFileName(dir), "Minos.NET.Samples.Shared", StringComparison.Ordinal))];

    // A sample with Minos:Providers in its appsettings.json runs against several providers and keeps one file per provider.
    private static bool IsProviderSample(string dir)
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir, "appsettings.json"), optional: true)
            .Build()
            .GetSection(SampleProviders.ProvidersKey)
            .Exists();

    // A sample's single recordings.json, or one provider's file in a recordings/ folder.
    private static bool IsRecording(string path)
        => string.Equals(Path.GetFileName(path), "recordings.json", StringComparison.Ordinal)
            || string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "recordings", StringComparison.Ordinal);

    private static bool InBuildOutput(string path)
        => path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
