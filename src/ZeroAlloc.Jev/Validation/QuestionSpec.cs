using ZeroAlloc.Jev.Generator;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Jev.Validation;

/// <summary>
/// One question of a set built at run time, as ZeroAlloc.Validation checks it against the API's rules. Every rule
/// names its JEV id in <see cref="ValidationFailure.ErrorCode"/>, the same id the analyzers report for a
/// <c>[JevQuestions]</c> set; advice is <see cref="Severity.Warning"/>.
/// </summary>
[Validate]
internal sealed record QuestionSpec
{
    [NotEmpty(ErrorCode = DiagnosticIds.DuplicateKey, Message = "The question key is empty.")]
    public required string Key { get; init; }

    public required QuestionKind Kind { get; init; }

    public required OptionSpec[] Options { get; init; }

    [GreaterThanOrEqualTo(JevLimits.MinimumOptions, When = nameof(IsChoice), ErrorCode = DiagnosticIds.EmptyChoiceEnum,
        Message = "A Choice question needs at least one option.")]
    [GreaterThanOrEqualTo(JevLimits.MinimumOptions, When = nameof(IsScore), ErrorCode = DiagnosticIds.EmptyScoreEnum,
        Message = "A Score question needs at least one level.")]
    [LessThanOrEqualTo(JevLimits.MaximumChoiceOptions, When = nameof(IsChoice), Severity = Severity.Warning,
        ErrorCode = DiagnosticIds.OptionCountOutsideGuidance,
        Message = "A Choice question has more than 255 options, beyond the API's guidance.")]
    [InclusiveBetween(JevLimits.MinimumScoreLevels, JevLimits.MaximumScoreLevels, When = nameof(IsScoreWithLevels), Severity = Severity.Warning,
        ErrorCode = DiagnosticIds.OptionCountOutsideGuidance,
        Message = "A Score question has fewer than 2 or more than 10 levels, outside the API's guidance.")]
    public int OptionCount => Options.Length;

    public bool IsChoice() => Kind == QuestionKind.Choice;

    public bool IsScore() => Kind == QuestionKind.Score;

    // A Score with no levels already fails JEV002; JEV005 does not pile onto it, as in the analyzers.
    public bool IsScoreWithLevels() => Kind == QuestionKind.Score && Options.Length > 0;

    /// <summary>JEV106: an empty option key, or one used more than once, reported once per key.</summary>
    [CustomValidation]
    public ValidationFailure[] ValidateOptionKeys()
    {
        List<ValidationFailure>? failures = null;
        for (var i = 0; i < Options.Length; i++)
        {
            var key = Options[i].Key;
            if (key.Length == 0)
            {
                (failures ??= []).Add(Error($"An option key of '{Key}' is empty."));
            }
            else if (CountBefore(i, key) == 1)
            {
                (failures ??= []).Add(Error($"The option key '{key}' is used more than once in '{Key}'."));
            }
        }

        return failures?.ToArray() ?? [];
    }

    private int CountBefore(int index, string key)
    {
        var count = 0;
        foreach (ref readonly var option in Options.AsSpan(0, index))
        {
            if (string.Equals(option.Key, key, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static ValidationFailure Error(string message)
        => new() { PropertyName = nameof(Options), ErrorMessage = message, ErrorCode = DiagnosticIds.DuplicateKey };
}
