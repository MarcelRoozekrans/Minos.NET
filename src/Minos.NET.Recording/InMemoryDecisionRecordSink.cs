namespace Minos.Recording;

/// <summary>
/// A sink that keeps records and outcomes in memory, for tests and for apps that read them in process. It applies
/// outcomes and erasures as <see cref="DecisionRecordFile.ReadAsync(string, CancellationToken)"/> does for a file: an
/// erasure removes what was kept before it, a record of the same decision replaces the earlier one when its attempt is
/// higher or equal, and an outcome with no record is ignored. It is safe to read <see cref="Records"/> while a recorder
/// writes to it.
/// </summary>
public sealed class InMemoryDecisionRecordSink : IDecisionRecordSink
{
    private readonly Lock _gate = new();
    private readonly List<Entry> _order = [];
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);

    /// <summary>Gets a snapshot of the decisions kept, in the order their records first arrived, each with its outcomes in order.</summary>
    public IReadOnlyList<RecordedDecision> Records
    {
        get
        {
            lock (_gate)
            {
                var decisions = new List<RecordedDecision>(_order.Count);
                foreach (var entry in _order)
                {
                    decisions.Add(new RecordedDecision(entry.Record, DecisionRecord.Freeze(entry.Outcomes)));
                }

                return decisions.AsReadOnly();
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            if (_byId.TryGetValue(record.DecisionId, out var existing))
            {
                if (record.Attempt >= existing.Record.Attempt)
                {
                    existing.Record = record;
                }
            }
            else
            {
                var entry = new Entry(record);
                _byId[record.DecisionId] = entry;
                _order.Add(entry);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decisionId);
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_gate)
        {
            if (_byId.TryGetValue(decisionId, out var entry))
            {
                entry.Outcomes.Add(outcome);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasureKey);
        lock (_gate)
        {
            _order.RemoveAll(entry =>
            {
                if (!string.Equals(entry.Record.ErasureKey, erasureKey, StringComparison.Ordinal))
                {
                    return false;
                }

                _byId.Remove(entry.Record.DecisionId);
                return true;
            });
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decisionId);
        lock (_gate)
        {
            if (_byId.Remove(decisionId, out var entry))
            {
                _order.Remove(entry);
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed class Entry(DecisionRecord record)
    {
        public DecisionRecord Record { get; set; } = record;

        public List<DecisionOutcome> Outcomes { get; } = [];
    }
}
