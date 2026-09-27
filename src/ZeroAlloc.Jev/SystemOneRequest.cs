using System.Text.Json.Serialization;

namespace ZeroAlloc.Jev;

/// <summary>A request to evaluate a state against a set of named, typed questions.</summary>
public sealed class SystemOneRequest
{
    /// <summary>Gets the content to evaluate: text, or structured JSON such as records or a chat log.</summary>
    public required JevContent State { get; init; }

    /// <summary>Gets the model that handles the request. Defaults to <see cref="JevDefaults.Model"/>.</summary>
    /// <remarks>
    /// Never omitted from the request body even when <see langword="null"/>, so an
    /// accidental <see langword="null"/> assignment is rejected as a <see cref="System.Text.Json.JsonException"/>
    /// at serialization time instead of silently sending no model.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Model { get; init; } = JevDefaults.Model;

    /// <summary>Gets the questions, keyed by ids you choose; answers come back under the same ids.</summary>
    public required IReadOnlyDictionary<string, JevQuestion> Questions { get; init; }
}
