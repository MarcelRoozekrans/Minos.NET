using System.Collections.ObjectModel;

namespace Minos.Recording;

/// <summary>
/// What became of a decision after it was made: the answers a human confirmed, or a signal such as a click or a
/// complaint. It is written to the file as its own line and attached to its record when the file is read.
/// </summary>
public abstract class DecisionOutcome
{
    private protected DecisionOutcome(string source, DateTimeOffset at)
    {
        Source = source;
        At = at;
    }

    /// <summary>Gets a name for where the outcome came from, for example <c>reviewer-ui</c>.</summary>
    public string Source { get; }

    /// <summary>Gets when the outcome happened.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Creates an outcome that confirms the answers to some of a decision's questions.</summary>
    /// <param name="source">Where the outcome came from.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="answers">The confirmed answer per question key; at least one.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is empty, or <paramref name="answers"/> is empty or has an empty key.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="answers"/> is <see langword="null"/>.</exception>
    public static DecisionOutcome Confirmed(string source, DateTimeOffset at, IReadOnlyDictionary<string, ConfirmedAnswer> answers)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Count == 0)
        {
            throw new ArgumentException("A confirmed outcome needs at least one answer.", nameof(answers));
        }

        var copy = new Dictionary<string, ConfirmedAnswer>(answers.Count, StringComparer.Ordinal);
        foreach (var (key, answer) in answers)
        {
            ArgumentException.ThrowIfNullOrEmpty(key, nameof(answers));
            copy[key] = answer;
        }

        return new ConfirmedOutcome(source, at, copy);
    }

    /// <summary>Creates an outcome that is a named signal, for example <c>clicked</c> or <c>complained</c>.</summary>
    /// <param name="source">Where the outcome came from.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="name">The name of the signal.</param>
    /// <param name="value">A number that goes with the signal, or <see langword="null"/>. It must be finite.</param>
    /// <param name="note">Free text that goes with the signal, or <see langword="null"/>.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> or <paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite.</exception>
    public static DecisionOutcome Signal(string source, DateTimeOffset at, string name, double? value = null, string? note = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number.");
        }

        return new SignalOutcome(source, at, name, value, note);
    }

    internal static IReadOnlyDictionary<string, ConfirmedAnswer> Freeze(Dictionary<string, ConfirmedAnswer> answers)
        => new ReadOnlyDictionary<string, ConfirmedAnswer>(answers);
}

/// <summary>An outcome that confirms the answers to some of a decision's questions.</summary>
public sealed class ConfirmedOutcome : DecisionOutcome
{
    internal ConfirmedOutcome(string source, DateTimeOffset at, Dictionary<string, ConfirmedAnswer> answers)
        : base(source, at)
        => Answers = Freeze(answers);

    /// <summary>Gets the confirmed answer per question key.</summary>
    public IReadOnlyDictionary<string, ConfirmedAnswer> Answers { get; }
}

/// <summary>An outcome that is a named signal, with an optional number and note.</summary>
public sealed class SignalOutcome : DecisionOutcome
{
    internal SignalOutcome(string source, DateTimeOffset at, string name, double? value, string? note)
        : base(source, at)
    {
        Name = name;
        Value = value;
        Note = note;
    }

    /// <summary>Gets the name of the signal.</summary>
    public string Name { get; }

    /// <summary>Gets the number that goes with the signal, or <see langword="null"/>.</summary>
    public double? Value { get; }

    /// <summary>Gets the free text that goes with the signal, or <see langword="null"/>.</summary>
    public string? Note { get; }
}
