using ZeroAlloc.Jev.Validation;

namespace ZeroAlloc.Jev;

/// <summary>A question as the builder collects it, before <see cref="JevQuestionSetBuilder.Build"/> checks and writes it.</summary>
/// <param name="key">The wire key.</param>
/// <param name="kind">The question's kind.</param>
/// <param name="instructions">The instructions.</param>
/// <param name="enumOptions">
/// For an enum question, creates the option set answers are read with from the checked spec: the enum's
/// <c>ForChoice</c> set, or a Score's levels in the order given; <see langword="null"/> for a Noul or a keyed question.
/// </param>
/// <param name="enumMembers">For an enum Score, the members every level must cover once; else <see langword="null"/>.</param>
internal sealed class QuestionDraft(
    string key, QuestionKind kind, JevContent instructions, Func<QuestionSpec, IJevOptionKeys>? enumOptions, string[]? enumMembers)
{
    public string Key { get; } = key;

    public QuestionKind Kind { get; } = kind;

    public JevContent Instructions { get; } = instructions;

    public JevContent? WhenTrue { get; set; }

    public JevContent? WhenFalse { get; set; }

    public List<OptionSpec> Options { get; } = [];

    public QuestionSpec ToSpec()
        => new()
        {
            Key = Key,
            Kind = Kind,
            Instructions = Instructions,
            WhenTrue = WhenTrue,
            WhenFalse = WhenFalse,
            Options = [.. Options],
            EnumMembers = enumMembers,
        };

    /// <summary>
    /// The option set answers are read with: the enum's, a keyed set over <paramref name="spec"/>'s keys, or none for a
    /// Noul. Called only for a spec that passed validation, so an enum Score's levels cover each member once.
    /// </summary>
    public IJevOptionKeys? PlanOptions(QuestionSpec spec)
    {
        if (Kind == QuestionKind.Noul)
        {
            return null;
        }

        if (enumOptions is not null)
        {
            return enumOptions(spec);
        }

        // A keyed Score's levels are keyed by index, "0" to n - 1, which is what its level builder writes.
        if (Kind == QuestionKind.Score)
        {
            return KeyedOptionSet.Levels(spec.Options.Length);
        }

        var keys = new string[spec.Options.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = spec.Options[i].Key;
        }

        return new KeyedOptionSet(keys);
    }
}
