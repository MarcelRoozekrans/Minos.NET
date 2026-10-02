using System.Text.Json;

namespace ZeroAlloc.Jev.Samples.Tests;

public sealed class RecordingsSafetyTests
{
    private static readonly string[] Forbidden = ["sk-", "Bearer", "api_key", "Authorization"];
    private static readonly string[] AllowedRootProperties = ["entries", "model", "provider", "recorded"];

    public static TheoryData<string> Recordings
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(Path.Combine(Repository.Root, "samples"), "recordings.json", SearchOption.AllDirectories))
            {
                if (!path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    data.Add(Path.GetRelativePath(Repository.Root, path));
                }
            }

            return data;
        }
    }

    [Fact]
    public void EverySample_HasRecordings()
        => Assert.Equal(3, Recordings.Count);

    [Theory]
    [MemberData(nameof(Recordings))]
    public void Recordings_HoldNoKeyAndNoHeaders(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(Repository.Root, relativePath));
        using var json = JsonDocument.Parse(text);

        Assert.All(Forbidden, word => Assert.DoesNotContain(word, text, StringComparison.Ordinal));
        Assert.Equal(AllowedRootProperties, json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }
}
