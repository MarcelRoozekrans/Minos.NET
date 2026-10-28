namespace Minos.Recording;

/// <summary>
/// One answer of a recorded decision: the same members as a <see cref="QuestionAnswer"/>, kept as a value that does
/// not borrow the response's buffers. Only the library creates one, from a response or a record file; two answers are
/// equal when every member is, with the probabilities compared one by one. The <see langword="default"/> value has a
/// <see langword="null"/> key and no probabilities.
/// </summary>
public readonly struct RecordedAnswer : IEquatable<RecordedAnswer>
{
    private readonly IReadOnlyList<double>? _probabilities;

    internal RecordedAnswer(
        string key,
        QuestionKind kind,
        int chosenIndex,
        double probability,
        double expected,
        double confidence,
        IReadOnlyList<double> probabilities,
        ProbabilitySource probabilitySource = ProbabilitySource.Model)
    {
        Key = key;
        Kind = kind;
        ChosenIndex = chosenIndex;
        Probability = probability;
        Expected = expected;
        Confidence = confidence;
        _probabilities = probabilities;
        ProbabilitySource = probabilitySource;
    }

    /// <summary>Gets the key of the question that was answered.</summary>
    public string Key { get; }

    /// <summary>Gets the kind of the question.</summary>
    public QuestionKind Kind { get; }

    /// <summary>Gets the chosen option or level, from 0; -1 for a Noul answer.</summary>
    public int ChosenIndex { get; }

    /// <summary>Gets the probability of <see langword="true"/> for a Noul answer; 0 for the other kinds.</summary>
    public double Probability { get; }

    /// <summary>Gets the expected level of a Score answer; 0 for the other kinds.</summary>
    public double Expected { get; }

    /// <summary>Gets the confidence in the answer, from 0 to 1.</summary>
    public double Confidence { get; }

    /// <summary>Gets the probability of each option or level; empty for a Noul answer.</summary>
    public IReadOnlyList<double> Probabilities => _probabilities ?? [];

    /// <summary>Gets where the probabilities came from: the model's own, its logprobs, or a verbalized estimate.</summary>
    public ProbabilitySource ProbabilitySource { get; }

    /// <summary>Determines whether two answers are equal.</summary>
    /// <param name="left">The first answer.</param>
    /// <param name="right">The second answer.</param>
    /// <returns><see langword="true"/> when every member is equal.</returns>
    public static bool operator ==(RecordedAnswer left, RecordedAnswer right) => left.Equals(right);

    /// <summary>Determines whether two answers differ.</summary>
    /// <param name="left">The first answer.</param>
    /// <param name="right">The second answer.</param>
    /// <returns><see langword="true"/> when a member differs.</returns>
    public static bool operator !=(RecordedAnswer left, RecordedAnswer right) => !left.Equals(right);

    /// <summary>Gets a value that says whether another answer has the same members, with the probabilities compared one by one.</summary>
    /// <param name="other">The other answer.</param>
    /// <returns><see langword="true"/> when every member is equal.</returns>
    public bool Equals(RecordedAnswer other)
        => string.Equals(Key, other.Key, StringComparison.Ordinal)
            && Kind == other.Kind
            && ChosenIndex == other.ChosenIndex
            && Probability.Equals(other.Probability)
            && Expected.Equals(other.Expected)
            && Confidence.Equals(other.Confidence)
            && ProbabilitySource == other.ProbabilitySource
            && Probabilities.SequenceEqual(other.Probabilities);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RecordedAnswer other && Equals(other);

    /// <summary>Gets a hash code that agrees with <see cref="Equals(RecordedAnswer)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(Kind);
        hash.Add(ChosenIndex);
        hash.Add(Probability);
        hash.Add(Expected);
        hash.Add(Confidence);
        hash.Add(ProbabilitySource);
        foreach (var probability in Probabilities)
        {
            hash.Add(probability);
        }

        return hash.ToHashCode();
    }

    internal static RecordedAnswer From(QuestionAnswer answer)
        => new(answer.Key, answer.Kind, answer.ChosenIndex, answer.Probability, answer.Expected, answer.Confidence, answer.Probabilities.ToArray(), answer.ProbabilitySource);
}
