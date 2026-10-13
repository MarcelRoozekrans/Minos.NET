using System.Text.Json.Serialization;
using Minos.SystemOne.Telemetry;

namespace Minos.SystemOne;

/// <summary>The answers to a <see cref="SystemOneRequest"/>, one per question.</summary>
public sealed class SystemOneResponse
{
    /// <summary>Gets the versioned id of the model that answered, e.g. <c>jev-1.13.0</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the answers, keyed by the question ids from the request.</summary>
    public required IReadOnlyDictionary<string, Answer> Answers { get; init; }

    /// <summary>Gets the token usage for the request.</summary>
    public required DecisionUsage Usage { get; init; }

    /// <summary>Gets the generation id, when the server sends one: OpenRouter does, TypeSafe's API does not.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets the upstream provider OpenRouter routed the request to; <see langword="null"/> when the server does not report
    /// one, as on TypeSafe's API.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? UpstreamProvider { get; init; }

    /// <summary>Gets each Choice and Score answer's confidence, for the telemetry proxy; reading it does not allocate.</summary>
    internal AnswerConfidences Confidences => new(Answers);
}
