namespace Minos.Conformance;

/// <summary>What a probe did: the exchanges that went over the wire, in order, and what Minos returned.</summary>
public sealed class ProbeOutcome
{
    /// <summary>Initializes a new instance of the <see cref="ProbeOutcome"/> class.</summary>
    /// <param name="probe">The probe.</param>
    /// <param name="exchanges">The exchanges, in the order they were sent; more than one when the client retried.</param>
    /// <param name="response">Minos's response, or <see langword="null"/> when it returned an error.</param>
    /// <param name="error">Minos's error, or <see langword="null"/> when it returned a response.</param>
    public ProbeOutcome(ConformanceProbe probe, IReadOnlyList<ConformanceExchange> exchanges, DecisionResponse? response, DecisionError? error)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(exchanges);

        Probe = probe;
        Exchanges = exchanges;
        Response = response;
        Error = error;
    }

    /// <summary>Gets the probe.</summary>
    public ConformanceProbe Probe { get; }

    /// <summary>Gets the exchanges, in the order they were sent.</summary>
    public IReadOnlyList<ConformanceExchange> Exchanges { get; }

    /// <summary>Gets Minos's response, or <see langword="null"/>.</summary>
    public DecisionResponse? Response { get; }

    /// <summary>Gets Minos's error, or <see langword="null"/>.</summary>
    public DecisionError? Error { get; }

    /// <summary>Gets the last exchange, the one whose result Minos returned, or <see langword="null"/> when nothing was sent.</summary>
    public ConformanceExchange? Last => Exchanges.Count == 0 ? null : Exchanges[^1];
}
