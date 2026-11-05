namespace Minos;

/// <summary>One client in a fallback chain, after the inner client, with its name in <see cref="DecisionResponse.Path"/>.</summary>
/// <remarks>The fallback stage does not dispose a step's client; its owner does.</remarks>
public sealed class FallbackStep
{
    /// <summary>Initializes a new instance of the <see cref="FallbackStep"/> class.</summary>
    /// <param name="name">The step's name in <see cref="DecisionResponse.Path"/>; must not be empty.</param>
    /// <param name="client">The client the step asks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    public FallbackStep(string name, IDecisionClient client)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(client);
        Name = name;
        Client = client;
    }

    /// <summary>Gets the step's name in <see cref="DecisionResponse.Path"/>.</summary>
    public string Name { get; }

    /// <summary>Gets the client the step asks.</summary>
    public IDecisionClient Client { get; }

    /// <summary>
    /// Gets how long the step's client is expected to take. When less time than this is left before the deadline, the
    /// step is skipped, and an earlier step's call is cut so that this one still fits. Must not be negative. Defaults to
    /// <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan ExpectedLatency { get; init; }
}
