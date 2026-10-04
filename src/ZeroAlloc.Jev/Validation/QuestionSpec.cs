using System.Globalization;
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
    private static readonly string DepthLimit = JevLimits.MaximumJsonDepth.ToString(CultureInfo.InvariantCulture);

    [NotEmpty(ErrorCode = DiagnosticIds.DuplicateKey, Message = "The question key is empty.")]
    public required string Key { get; init; }

    public required QuestionKind Kind { get; init; }

    [NotBlankContent(Severity = Severity.Warning, ErrorCode = DiagnosticIds.EmptyText,
        Message = "The instructions are empty or whitespace, or an empty JSON object or array.")]
    public required JevContent Instructions { get; init; }

    [NotBlankContent(Severity = Severity.Warning, ErrorCode = DiagnosticIds.EmptyText,
        Message = "What a yes answer means is empty or whitespace, or an empty JSON object or array.")]
    public JevContent? WhenTrue { get; init; }

    [NotBlankContent(Severity = Severity.Warning, ErrorCode = DiagnosticIds.EmptyText,
        Message = "What a no answer means is empty or whitespace, or an empty JSON object or array.")]
    public JevContent? WhenFalse { get; init; }

    public required OptionSpec[] Options { get; init; }

    /// <summary>
    /// Gets an enum Score's distinct member names, in <c>EnumOptionSet&lt;T&gt;.ForChoice</c> order, which
    /// <see cref="ValidateEnumLevels"/> checks the levels against; <see langword="null"/> for every other question.
    /// </summary>
    public string[]? EnumMembers { get; init; }

    [GreaterThanOrEqualTo(JevLimits.MinimumOptions, When = nameof(IsChoice), ErrorCode = DiagnosticIds.EmptyChoiceEnum,
        Message = "A Choice question needs at least one option.")]
    [GreaterThanOrEqualTo(JevLimits.MinimumOptions, When = nameof(NeedsLevels), ErrorCode = DiagnosticIds.EmptyScoreEnum,
        Message = "A Score question needs at least one level.")]
    [LessThanOrEqualTo(JevLimits.MaximumChoiceOptions, When = nameof(IsChoice), Severity = Severity.Warning,
        ErrorCode = DiagnosticIds.OptionCountOutsideGuidance,
        Message = "A Choice question has more than 255 options, beyond the API's guidance.")]
    [InclusiveBetween(JevLimits.MinimumScoreLevels, JevLimits.MaximumScoreLevels, When = nameof(IsScoreWithLevels),
        Severity = Severity.Warning, ErrorCode = DiagnosticIds.OptionCountOutsideGuidance,
        Message = "A Score question has fewer than 2 or more than 10 levels, outside the API's guidance.")]
    public int OptionCount => Options.Length;

    public bool IsChoice() => Kind == QuestionKind.Choice;

    // A keyed Score, or an enum Score over an enum with no members: JEV002, as the generator reports it. An enum Score
    // over members with no level given fails JEV104 for each member instead, as the analyzers do.
    public bool NeedsLevels() => Kind == QuestionKind.Score && EnumMembers is not { Length: > 0 };

    // A Score with no levels already fails JEV002 or JEV104; JEV005 does not pile onto it, as in the analyzers.
    public bool IsScoreWithLevels() => Kind == QuestionKind.Score && Options.Length > 0;

    /// <summary>
    /// The rules across a question's options: JEV106 for an empty or repeated option key, reported once per key;
    /// JEV003 for a blank description or a blank example or not-for entry. Both Score <c>Level</c> methods require a
    /// criterion, so a level always has one.
    /// </summary>
    [CustomValidation]
    public ValidationFailure[] ValidateOptions()
    {
        List<ValidationFailure>? failures = null;
        for (var i = 0; i < Options.Length; i++)
        {
            var option = Options[i];
            if (option.Key.Length == 0)
            {
                Add(ref failures, Severity.Error, DiagnosticIds.DuplicateKey, $"An option key of '{Key}' is empty.");
            }
            else if (CountBefore(i, option.Key) == 1)
            {
                Add(ref failures, Severity.Error, DiagnosticIds.DuplicateKey, $"The option key '{option.Key}' is used more than once in '{Key}'.");
            }

            if (option.Criterion is not { } criterion)
            {
                continue;
            }

            if (criterion.IsJson ? NotBlankContentAttribute.IsBlank(criterion.JsonContent) : string.IsNullOrWhiteSpace(criterion.Description))
            {
                Add(ref failures, Severity.Warning, DiagnosticIds.EmptyText,
                    $"The description of '{option.Name}' in '{Key}' is empty or whitespace, or an empty JSON object or array.");
            }

            if (HasBlankEntry(criterion.Examples) || HasBlankEntry(criterion.NotFor))
            {
                Add(ref failures, Severity.Warning, DiagnosticIds.EmptyText,
                    $"An example or not-for entry of '{option.Name}' in '{Key}' is null, empty or whitespace.");
            }
        }

        return failures?.ToArray() ?? [];
    }

    /// <summary>
    /// An enum Score's levels against its members: JEV104 for a member not given a level, JEV106 for one given more than
    /// once, each reported once per member. An alias counts as the member it shares a value with.
    /// </summary>
    [CustomValidation]
    public ValidationFailure[] ValidateEnumLevels()
    {
        if (EnumMembers is not { } members)
        {
            return [];
        }

        List<ValidationFailure>? failures = null;
        for (var member = 0; member < members.Length; member++)
        {
            var given = 0;
            foreach (ref readonly var option in Options.AsSpan())
            {
                if (option.Member == member)
                {
                    given++;
                }
            }

            if (given == 0)
            {
                Add(ref failures, Severity.Error, DiagnosticIds.MissingLevel, $"The member '{members[member]}' of '{Key}' is not given a level.");
            }
            else if (given > 1)
            {
                Add(ref failures, Severity.Error, DiagnosticIds.DuplicateKey, $"The member '{members[member]}' of '{Key}' is given more than one level.");
            }
        }

        return failures?.ToArray() ?? [];
    }

    /// <summary>
    /// JEV108: instructions, a description, or what a yes or no answer means, nested deeper than
    /// <see cref="JevLimits.MaximumJsonDepth"/> levels.
    /// </summary>
    [CustomValidation]
    public ValidationFailure[] ValidateJsonDepth()
    {
        List<ValidationFailure>? failures = null;
        if (JsonDepth.Exceeds(Instructions))
        {
            AddDepth(ref failures, "The instructions");
        }

        if (WhenTrue is { } yes && JsonDepth.Exceeds(yes))
        {
            AddDepth(ref failures, "What a yes answer means");
        }

        if (WhenFalse is { } no && JsonDepth.Exceeds(no))
        {
            AddDepth(ref failures, "What a no answer means");
        }

        foreach (ref readonly var option in Options.AsSpan())
        {
            if (option.Criterion is { IsJson: true } criterion && JsonDepth.Exceeds(criterion.JsonContent))
            {
                AddDepth(ref failures, $"The description of '{option.Name}'");
            }
        }

        return failures?.ToArray() ?? [];
    }

    private void AddDepth(ref List<ValidationFailure>? failures, string what)
        => Add(ref failures, Severity.Error, DiagnosticIds.InvalidJson, $"{what} of '{Key}' nests deeper than {DepthLimit} levels.");

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

    private static bool HasBlankEntry(ReadOnlySpan<string?> entries)
    {
        foreach (ref readonly var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return true;
            }
        }

        return false;
    }

    private static void Add(ref List<ValidationFailure>? failures, Severity severity, string rule, string message)
        => (failures ??= []).Add(new ValidationFailure
        {
            PropertyName = nameof(Options),
            ErrorMessage = message,
            ErrorCode = rule,
            Severity = severity,
        });
}
