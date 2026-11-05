namespace Minos;

/// <summary>One step of a fallback chain, a client registered in the container under a key.</summary>
/// <param name="Key">The client's service key, which also names the step in <see cref="DecisionResponse.Path"/>.</param>
/// <param name="ExpectedLatency">
/// How long the client is expected to take, as <see cref="FallbackStep.ExpectedLatency"/>. Must not be negative.
/// </param>
public readonly record struct KeyedFallbackStep(string Key, TimeSpan ExpectedLatency);
