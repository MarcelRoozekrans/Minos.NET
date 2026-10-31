using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minos.Recording.RecordFormat;

// What the reader deserializes one line into. Every line kind shares the type: the reader checks "kind" and then the
// members that kind needs. The question set, the state and the confirmed answers stay JsonElements, because their shape
// is not fixed. Unknown members are skipped.
internal sealed class LineDto
{
    public string? Kind { get; set; }

    public int? Version { get; set; }

    public string? DecisionId { get; set; }

    public string? ErasureKey { get; set; }

    // Record members.
    public DateTimeOffset? Started { get; set; }

    public double LatencyMs { get; set; }

    public string? Provider { get; set; }

    public string? Endpoint { get; set; }

    public string? DefaultModel { get; set; }

    public string? RequestedModel { get; set; }

    public string? ServedModel { get; set; }

    public string? ResponseId { get; set; }

    public string? UpstreamProvider { get; set; }

    public UsageDto? Usage { get; set; }

    public bool Cached { get; set; }

    public JsonElement Questions { get; set; }

    public JsonElement State { get; set; }

    public int ImageCount { get; set; }

    public List<string>? ImageHashes { get; set; }

    public List<AnswerDto>? Answers { get; set; }

    public string? ErrorKind { get; set; }

    public List<string>? RefusedKeys { get; set; }

    public ShadowDto? Shadow { get; set; }

    // Outcome members.
    public string? Type { get; set; }

    public string? Source { get; set; }

    public DateTimeOffset? At { get; set; }

    public JsonElement Confirmed { get; set; }

    public string? Name { get; set; }

    public double? Value { get; set; }

    public string? Note { get; set; }
}

// The shadow candidate's part of a record line: the members of the record that describe a call.
internal sealed class ShadowDto
{
    public string? Provider { get; set; }

    public string? Endpoint { get; set; }

    public string? DefaultModel { get; set; }

    public string? RequestedModel { get; set; }

    public string? ServedModel { get; set; }

    public string? ResponseId { get; set; }

    public string? UpstreamProvider { get; set; }

    public double LatencyMs { get; set; }

    public UsageDto? Usage { get; set; }

    public List<AnswerDto>? Answers { get; set; }

    public string? ErrorKind { get; set; }

    public List<string>? RefusedKeys { get; set; }
}

internal sealed class UsageDto
{
    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public double? Cost { get; set; }
}

internal sealed class AnswerDto
{
    public string? Key { get; set; }

    public string? Kind { get; set; }

    public int? ChosenIndex { get; set; }

    public double Probability { get; set; }

    public double Expected { get; set; }

    public double Confidence { get; set; }

    public string? ProbabilitySource { get; set; }

    public List<double>? Probabilities { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(LineDto))]
internal sealed partial class RecordJsonContext : JsonSerializerContext
{
}
