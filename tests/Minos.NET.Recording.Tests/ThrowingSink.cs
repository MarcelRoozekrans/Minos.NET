namespace Minos.Recording.Tests;

/// <summary>
/// A sink that throws <see cref="InvalidOperationException"/> for the calls <paramref name="fails"/> picks, named as
/// <see cref="RecordingSpySink"/> names them, and passes the rest to a spy. The message quotes the call, so a test can
/// check that it is never repeated.
/// </summary>
internal sealed class ThrowingSink(Func<string, bool> fails) : IDecisionRecordSink
{
    public const string MessagePrefix = "SINK-FAILED ";

    public RecordingSpySink Spy { get; } = new();

    public List<Exception> Thrown { get; } = [];

    public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
        => Fails($"write:{record.DecisionId}", out var thrown) ? ValueTask.FromException(thrown) : Spy.WriteAsync(record, cancellationToken);

    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
        => Fails($"outcome:{decisionId}", out var thrown) ? ValueTask.FromException(thrown) : Spy.AddOutcomeAsync(decisionId, outcome, cancellationToken);

    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
        => Fails($"erase:{erasureKey}", out var thrown) ? ValueTask.FromException(thrown) : Spy.EraseAsync(erasureKey, cancellationToken);

    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
        => Fails($"erase-decision:{decisionId}", out var thrown) ? ValueTask.FromException(thrown) : Spy.EraseDecisionAsync(decisionId, cancellationToken);

    private bool Fails(string call, out Exception thrown)
    {
        thrown = new InvalidOperationException(MessagePrefix + call);
        if (!fails(call))
        {
            return false;
        }

        lock (Thrown)
        {
            Thrown.Add(thrown);
        }

        return true;
    }
}
