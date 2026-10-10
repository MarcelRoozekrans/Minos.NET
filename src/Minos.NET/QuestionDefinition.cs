using System.Collections.ObjectModel;
using System.Globalization;

namespace Minos;

/// <summary>One question of a <see cref="QuestionSetDefinition"/>, independent of any provider's wire format.</summary>
public sealed class QuestionDefinition
{
    private QuestionDefinition(
        string key, QuestionKind kind, DecisionContent instructions, DecisionContent? whenTrue, DecisionContent? whenFalse, OptionDefinition[] options)
    {
        Key = key;
        Kind = kind;
        Instructions = instructions;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
        OptionArray = options;
        Options = options.Length == 0 ? [] : new ReadOnlyCollection<OptionDefinition>(options);
    }

    /// <summary>Gets the question's wire key.</summary>
    public string Key { get; }

    /// <summary>Gets the question's kind.</summary>
    public QuestionKind Kind { get; }

    /// <summary>Gets what the model is asked.</summary>
    public DecisionContent Instructions { get; }

    /// <summary>Gets what a yes answer means, for a Noul; otherwise <see langword="null"/>.</summary>
    public DecisionContent? WhenTrue { get; }

    /// <summary>Gets what a no answer means, for a Noul; otherwise <see langword="null"/>.</summary>
    public DecisionContent? WhenFalse { get; }

    /// <summary>Gets the Choice options or Score levels, in wire order; empty for a Noul.</summary>
    public IReadOnlyList<OptionDefinition> Options { get; }

    internal OptionDefinition[] OptionArray { get; }

    /// <summary>Defines a yes-or-no question.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">What the model is asked.</param>
    /// <param name="whenTrue">What a yes answer means, or <see langword="null"/>.</param>
    /// <param name="whenFalse">What a no answer means, or <see langword="null"/>.</param>
    /// <returns>The question.</returns>
    public static QuestionDefinition Noul(string key, DecisionContent instructions, DecisionContent? whenTrue = null, DecisionContent? whenFalse = null)
        => new(CheckKey(key), QuestionKind.Noul, Checked(instructions), CheckedOrNull(whenTrue, nameof(whenTrue)), CheckedOrNull(whenFalse, nameof(whenFalse)), []);

    /// <summary>Defines a question with one answer from <paramref name="options"/>.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">What the model is asked.</param>
    /// <param name="options">The options, in wire order.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentException"><paramref name="options"/> is empty or contains <see langword="null"/>.</exception>
    public static QuestionDefinition Choice(string key, DecisionContent instructions, params ReadOnlySpan<OptionDefinition> options)
    {
        if (options.IsEmpty)
        {
            throw new ArgumentException("A Choice question needs at least one option.", nameof(options));
        }

        foreach (ref readonly var option in options)
        {
            ArgumentNullException.ThrowIfNull(option, nameof(options));
        }

        return new(CheckKey(key), QuestionKind.Choice, Checked(instructions), null, null, options.ToArray());
    }

    /// <summary>Defines a question rated on <paramref name="levels"/>, lowest first. Levels are keyed by position: "0", "1", and so on.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">What the model is asked.</param>
    /// <param name="levels">What each level means, lowest first.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentException"><paramref name="levels"/> is empty or contains <see langword="null"/>.</exception>
    public static QuestionDefinition Score(string key, DecisionContent instructions, params ReadOnlySpan<Criterion> levels)
    {
        if (levels.IsEmpty)
        {
            throw new ArgumentException("A Score question needs at least one level.", nameof(levels));
        }

        var options = new OptionDefinition[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(levels[i], nameof(levels));
            options[i] = new OptionDefinition(i.ToString(CultureInfo.InvariantCulture), levels[i]);
        }

        return new(CheckKey(key), QuestionKind.Score, Checked(instructions), null, null, options);
    }

    private static string CheckKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return key;
    }

    private static DecisionContent? CheckedOrNull(DecisionContent? content, string paramName)
    {
        if (content is { } value)
        {
            DecisionContent.EnsureInitialized(value, paramName);
        }

        return content;
    }

    private static DecisionContent Checked(DecisionContent instructions)
    {
        DecisionContent.EnsureInitialized(instructions, nameof(instructions));
        return instructions;
    }
}
