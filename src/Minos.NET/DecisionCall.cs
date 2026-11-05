namespace Minos;

/// <summary>One call a routing stage made to a client while answering a request, in a <see cref="DecisionPath"/>.</summary>
/// <param name="ClientName">The name of the client that was called, such as the primary's name or the target's key.</param>
/// <param name="Model">The model that answered, as the call's response reported it; <see langword="null"/> when it did not say.</param>
/// <param name="Usage">The token usage and cost the call's response reported; <see langword="null"/> when it reported none.</param>
/// <param name="IsCached">Whether the call's response came from a cache stage rather than from the provider.</param>
/// <exception cref="ArgumentNullException"><paramref name="ClientName"/> is <see langword="null"/>.</exception>
public sealed record DecisionCall(string ClientName, string? Model, DecisionUsage? Usage, bool IsCached)
{
    private readonly string _clientName = ClientName ?? throw new ArgumentNullException(nameof(ClientName));

    /// <summary>Gets the name of the client that was called, such as the primary's name or the target's key.</summary>
    /// <exception cref="ArgumentNullException">On set: the value is <see langword="null"/>.</exception>
    public string ClientName
    {
        get => _clientName;
        init => _clientName = value ?? throw new ArgumentNullException(nameof(value));
    }
}
