using System.Diagnostics;

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

    // The route of an answer from a response with a path, or null; one reference, so an answer without a path is no
    // larger than it was before record version 3 added the route.
    private readonly Route? _route;

    internal RecordedAnswer(
        string key,
        QuestionKind kind,
        int chosenIndex,
        double probability,
        double expected,
        double confidence,
        IReadOnlyList<double> probabilities,
        ProbabilitySource probabilitySource = ProbabilitySource.Model,
        string? answeredBy = null,
        RouteReason? reason = null,
        double? primaryCertainty = null,
        double? targetCertainty = null)
    {
        Key = key;
        Kind = kind;
        ChosenIndex = chosenIndex;
        Probability = probability;
        Expected = expected;
        Confidence = confidence;
        _probabilities = probabilities;
        ProbabilitySource = probabilitySource;
        // A route is whole or absent: the record reader and From(answer, route) never pass part of one.
        Debug.Assert(
            answeredBy is null ? reason is null && primaryCertainty is null && targetCertainty is null : reason is not null && primaryCertainty is not null,
            "A route is whole or absent.");
        _route = answeredBy is null ? null : new Route(answeredBy, reason.GetValueOrDefault(), primaryCertainty.GetValueOrDefault(), targetCertainty);
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

    /// <summary>Gets the name of the client that answered the question, or <see langword="null"/> when the response had no <see cref="DecisionResponse.Path"/>.</summary>
    public string? AnsweredBy => _route?.AnsweredBy;

    /// <summary>Gets why that client answered, or <see langword="null"/> when the response had no path.</summary>
    public RouteReason? Reason => _route?.Reason;

    /// <summary>Gets the primary client's certainty in its own answer, from 0 to 1, or <see langword="null"/> when the response had no path.</summary>
    public double? PrimaryCertainty => _route?.PrimaryCertainty;

    /// <summary>Gets the target client's certainty in its answer, or <see langword="null"/> when the target did not answer or the response had no path.</summary>
    public double? TargetCertainty => _route?.TargetCertainty;

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
            && Route.Equals(_route, other._route)
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
        // The four route values, each null without a route, so an answer without a path hashes as it did before.
        hash.Add(AnsweredBy, StringComparer.Ordinal);
        hash.Add(Reason);
        hash.Add(PrimaryCertainty);
        hash.Add(TargetCertainty);
        foreach (var probability in Probabilities)
        {
            hash.Add(probability);
        }

        return hash.ToHashCode();
    }

    internal static RecordedAnswer From(QuestionAnswer answer)
        => new(answer.Key, answer.Kind, answer.ChosenIndex, answer.Probability, answer.Expected, answer.Confidence, answer.Probabilities.ToArray(), answer.ProbabilitySource);

    internal static RecordedAnswer From(QuestionAnswer answer, QuestionRoute route)
        => new(
            answer.Key, answer.Kind, answer.ChosenIndex, answer.Probability, answer.Expected, answer.Confidence, answer.Probabilities.ToArray(),
            answer.ProbabilitySource, route.AnsweredBy, route.Reason, route.PrimaryCertainty, route.TargetCertainty);

    // The four route members of one answer, immutable and shared by every copy of the answer.
    private sealed class Route(string answeredBy, RouteReason reason, double primaryCertainty, double? targetCertainty)
    {
        public string AnsweredBy { get; } = answeredBy;

        public RouteReason Reason { get; } = reason;

        public double PrimaryCertainty { get; } = primaryCertainty;

        public double? TargetCertainty { get; } = targetCertainty;

        // Two absent routes are equal, as the four null members they stand for were.
        public static bool Equals(Route? left, Route? right)
            => ReferenceEquals(left, right)
                || (left is not null
                    && right is not null
                    && string.Equals(left.AnsweredBy, right.AnsweredBy, StringComparison.Ordinal)
                    && left.Reason == right.Reason
                    && left.PrimaryCertainty.Equals(right.PrimaryCertainty)
                    && Nullable.Equals(left.TargetCertainty, right.TargetCertainty));
    }
}
