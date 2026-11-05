using System.Runtime.InteropServices;

namespace Minos;

/// <summary>Which client answered one question, and why, in a <see cref="DecisionPath"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct QuestionRoute : IEquatable<QuestionRoute>
{
    /// <summary>Initializes a new instance of the <see cref="QuestionRoute"/> struct.</summary>
    /// <param name="key">The question's key.</param>
    /// <param name="answeredBy">The name of the client whose answer the response holds.</param>
    /// <param name="reason">Why that client's answer is the one the response holds.</param>
    /// <param name="primaryCertainty">The certainty of the primary client's answer, between 0 and 1.</param>
    /// <param name="targetCertainty">The certainty of the target client's answer, between 0 and 1, or <see langword="null"/> when the target did not answer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="answeredBy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A certainty is not between 0 and 1, or is NaN.</exception>
    public QuestionRoute(string key, string answeredBy, RouteReason reason, double primaryCertainty, double? targetCertainty)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(answeredBy);
        EnsureCertainty(primaryCertainty, nameof(primaryCertainty));
        if (targetCertainty is { } target)
        {
            EnsureCertainty(target, nameof(targetCertainty));
        }

        Key = key;
        AnsweredBy = answeredBy;
        Reason = reason;
        PrimaryCertainty = primaryCertainty;
        TargetCertainty = targetCertainty;
    }

    /// <summary>Gets the question's key.</summary>
    public string Key { get; }

    /// <summary>Gets the name of the client whose answer the response holds: the primary's name or the target's.</summary>
    public string AnsweredBy { get; }

    /// <summary>Gets why that client's answer is the one the response holds.</summary>
    public RouteReason Reason { get; }

    /// <summary>
    /// Gets the certainty of the primary client's answer: a Choice's or Score's confidence, or the larger of
    /// <c>p</c> and <c>1 - p</c> for a Noul with probability <c>p</c>.
    /// </summary>
    public double PrimaryCertainty { get; }

    /// <summary>Gets the certainty of the target client's answer, measured as <see cref="PrimaryCertainty"/> is; <see langword="null"/> when the target did not answer.</summary>
    public double? TargetCertainty { get; }

    /// <summary>Compares two routes for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(QuestionRoute left, QuestionRoute right) => left.Equals(right);

    /// <summary>Compares two routes for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(QuestionRoute left, QuestionRoute right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(QuestionRoute other)
        => string.Equals(Key, other.Key, StringComparison.Ordinal)
           && string.Equals(AnsweredBy, other.AnsweredBy, StringComparison.Ordinal)
           && Reason == other.Reason
           && PrimaryCertainty.Equals(other.PrimaryCertainty)
           && Nullable.Equals(TargetCertainty, other.TargetCertainty);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is QuestionRoute other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(
            Key is null ? 0 : StringComparer.Ordinal.GetHashCode(Key),
            AnsweredBy is null ? 0 : StringComparer.Ordinal.GetHashCode(AnsweredBy),
            Reason,
            PrimaryCertainty,
            TargetCertainty);

    // The negated range check also rejects NaN.
    private static void EnsureCertainty(double certainty, string paramName)
    {
        if (!(certainty is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(paramName, certainty, "A certainty must be between 0 and 1.");
        }
    }
}
