using System.Text.Json.Serialization;

namespace Minos.SystemOne;

/// <summary>A request to evaluate a state against a set of named, typed questions.</summary>
public sealed class SystemOneRequest
{
    /// <summary>Gets the content to evaluate: text, or structured JSON such as records or a chat log.</summary>
    public required DecisionContent State { get; init; }

    /// <summary>
    /// Gets the model that handles the request. When <see langword="null"/>, the client's
    /// <see cref="SystemOneDecisionClientOptions.Model"/>, or the provider's default.
    /// </summary>
    /// <remarks>
    /// Must not be blank; the client throws <see cref="ArgumentException"/> before sending. The client resolves a
    /// <see langword="null"/> model before it serializes the request, so the body always names one.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Model { get; init; }

    /// <summary>Gets the questions, keyed by ids you choose; answers come back under the same ids.</summary>
    public required IReadOnlyDictionary<string, Question> Questions { get; init; }
}
