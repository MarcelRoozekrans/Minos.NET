namespace Minos.Recording.Tests;

/// <summary>
/// A sink that writes to an in-memory sink and keeps a log of its calls, as <c>write:id</c>, <c>outcome:id</c>,
/// <c>erase:key</c> and <c>erase-decision:id</c>. It also notes when two calls overlapped, which one writer never does.
/// </summary>
internal sealed class RecordingSpySink : IDecisionRecordSink
{
    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];
    private int _active;

    public InMemoryDecisionRecordSink Inner { get; } = new();

    public bool Overlapped { get; private set; }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
        => RunAsync($"write:{record.DecisionId}", () => Inner.WriteAsync(record, cancellationToken));

    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
        => RunAsync($"outcome:{decisionId}", () => Inner.AddOutcomeAsync(decisionId, outcome, cancellationToken));

    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
        => RunAsync($"erase:{erasureKey}", () => Inner.EraseAsync(erasureKey, cancellationToken));

    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
        => RunAsync($"erase-decision:{decisionId}", () => Inner.EraseDecisionAsync(decisionId, cancellationToken));

    private async ValueTask RunAsync(string call, Func<ValueTask> inner)
    {
        if (Interlocked.Increment(ref _active) > 1)
        {
            Overlapped = true;
        }

        try
        {
            await Task.Yield();
            await inner();
            lock (_gate)
            {
                _calls.Add(call);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
