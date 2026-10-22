namespace Minos.Recording;

/// <summary>A decision record with the outcomes that were written for it, in file order.</summary>
public sealed class RecordedDecision
{
    internal RecordedDecision(DecisionRecord record, IReadOnlyList<DecisionOutcome> outcomes)
    {
        Record = record;
        Outcomes = outcomes;
    }

    /// <summary>Gets the record. When a decision was retried, it is the record of the highest attempt.</summary>
    public DecisionRecord Record { get; }

    /// <summary>Gets the outcomes of the decision, in the order the file holds them.</summary>
    public IReadOnlyList<DecisionOutcome> Outcomes { get; }
}
