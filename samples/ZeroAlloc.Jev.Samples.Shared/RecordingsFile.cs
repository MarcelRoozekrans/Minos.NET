using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Jev.Samples;

/// <summary>
/// A sample's recorded answers: who produced them and when, and each response body keyed by its request's hash. No
/// HTTP headers and no key are ever stored.
/// </summary>
public sealed class RecordingsFile
{
    /// <summary>The provider the responses came from, for example <c>OpenRouter</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The model the responses report.</summary>
    public required string Model { get; init; }

    /// <summary>The recording date, as <c>yyyy-MM-dd</c>.</summary>
    public required string Recorded { get; init; }

    /// <summary>The responses, ordered by request hash so re-recording gives stable diffs.</summary>
    public required IReadOnlyList<RecordedResponse> Entries { get; init; }

    public static RecordingsFile Load(string path)
        => JsonSerializer.Deserialize(File.ReadAllText(path), RecordingsJsonContext.Default.RecordingsFile)
            ?? throw new InvalidOperationException("The recordings file " + path + " is empty.");

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, RecordingsJsonContext.Default.RecordingsFile) + "\n");
}

/// <summary>One recorded response body and the hash of the request that produced it.</summary>
public sealed record RecordedResponse(string RequestHash, string ResponseBody);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, NewLine = "\n")]
[JsonSerializable(typeof(RecordingsFile))]
internal sealed partial class RecordingsJsonContext : JsonSerializerContext;
