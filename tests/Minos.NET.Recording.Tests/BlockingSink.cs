using System.Threading.Channels;

namespace Minos.Recording.Tests;

/// <summary>
/// A sink whose calls wait at a gate until <see cref="Release"/> opens it, then pass to a <see cref="RecordingSpySink"/>.
/// <see cref="WaitEnteredAsync"/> waits until a given number of calls have reached the gate. With
/// <c>honoursCancellation</c> false, a call ignores its token, as a badly behaved sink would.
/// </summary>
internal sealed class BlockingSink(bool honoursCancellation = true) : IDecisionRecordSink
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<bool> _entered = Channel.CreateUnbounded<bool>();

    public RecordingSpySink Spy { get; } = new();

    /// <summary>Opens the gate for every waiting call and every later one.</summary>
    public void Release() => _gate.TrySetResult();

    /// <summary>Waits until <paramref name="count"/> more calls have reached the gate, for at most 10 s.</summary>
    public async Task WaitEnteredAsync(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            await _entered.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    public async ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
    {
        await PassAsync(cancellationToken);
        await Spy.WriteAsync(record, cancellationToken);
    }

    public async ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        await PassAsync(cancellationToken);
        await Spy.AddOutcomeAsync(decisionId, outcome, cancellationToken);
    }

    public async ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
    {
        await PassAsync(cancellationToken);
        await Spy.EraseAsync(erasureKey, cancellationToken);
    }

    public async ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        await PassAsync(cancellationToken);
        await Spy.EraseDecisionAsync(decisionId, cancellationToken);
    }

    private Task PassAsync(CancellationToken cancellationToken)
    {
        _entered.Writer.TryWrite(true);
        return honoursCancellation ? _gate.Task.WaitAsync(cancellationToken) : _gate.Task;
    }
}
