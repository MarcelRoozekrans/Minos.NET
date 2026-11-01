using System.Collections.Concurrent;
using Minos.Recording;

namespace Minos.Samples.ShadowReplay;

/// <summary>
/// Forwards every call to an inner sink and lets a replay wait for the write that carries a decision's shadow part.
/// </summary>
/// <param name="inner">The sink that keeps the records.</param>
public sealed class ShadowSignalSink(IDecisionRecordSink inner) : IDecisionRecordSink, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _waiting = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers interest in a decision's shadow write. Call it before the decision is made: the registration is removed
    /// once the shadow write is signalled, so a call after that returns a new task that never completes.
    /// </summary>
    /// <param name="decisionId">The decision id.</param>
    /// <returns>A task that completes once the record with a shadow part is written to the inner sink. The same task for the same id.</returns>
    public Task Expect(string decisionId)
        => _waiting.GetOrAdd(decisionId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    /// <summary>Drops a registration whose wait ended without a shadow.</summary>
    /// <param name="decisionId">The decision id.</param>
    public void Forget(string decisionId) => _waiting.TryRemove(decisionId, out _);

    /// <inheritdoc/>
    public async ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await inner.WriteAsync(record, cancellationToken).ConfigureAwait(false);
        if (record.Shadow is not null && _waiting.TryRemove(record.DecisionId, out var signal))
        {
            signal.TrySetResult();
        }
    }

    /// <inheritdoc/>
    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
        => inner.AddOutcomeAsync(decisionId, outcome, cancellationToken);

    /// <inheritdoc/>
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
        => inner.EraseAsync(erasureKey, cancellationToken);

    /// <inheritdoc/>
    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
        => inner.EraseDecisionAsync(decisionId, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => inner is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;
}
