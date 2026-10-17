namespace Minos.Evaluation;

/// <summary>One labelled example: the state, the questions asked about it, and the right answers.</summary>
public sealed class DatasetItem
{
    internal DatasetItem(
        string id,
        DecisionContent state,
        QuestionSetDefinition definition,
        IReadOnlyDictionary<string, ExpectedAnswer> expected,
        string? source,
        int line)
    {
        Id = id;
        State = state;
        Definition = definition;
        Expected = expected;
        Source = source;
        Line = line;
    }

    /// <summary>Gets the item's id, unique within its file.</summary>
    public string Id { get; }

    /// <summary>Gets the state the questions are asked about.</summary>
    public DecisionContent State { get; }

    /// <summary>Gets the item's questions.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>
    /// Gets the right answer per question key. A question with no entry is still sent, but it is not scored.
    /// </summary>
    public IReadOnlyDictionary<string, ExpectedAnswer> Expected { get; }

    /// <summary>Gets free text naming where the item came from, or <see langword="null"/>.</summary>
    public string? Source { get; }

    /// <summary>Gets the 1-based line of the file the item was read from.</summary>
    public int Line { get; }
}
