namespace Jev.Net;

/// <summary>A request to evaluate a state against a set of named, typed questions.</summary>
public sealed class SystemOneRequest
{
    /// <summary>Gets the content to evaluate: text, or structured JSON such as records or a chat log.</summary>
    public required JevContent State { get; init; }

    /// <summary>Gets the model that handles the request. Defaults to <see cref="JevDefaults.Model"/>.</summary>
    public string Model { get; init; } = JevDefaults.Model;

    /// <summary>Gets the questions, keyed by ids you choose; answers come back under the same ids.</summary>
    public required IReadOnlyDictionary<string, JevQuestion> Questions { get; init; }
}
