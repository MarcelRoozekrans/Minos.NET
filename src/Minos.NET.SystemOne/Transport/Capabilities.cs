using System.Globalization;
using Minos.Generator;

namespace Minos.SystemOne.Transport;

/// <summary>
/// The resolved <see cref="SystemOneCapabilities"/>: a preset's defaults with the options' overrides applied, and the check
/// a question set must pass before it is sent.
/// </summary>
/// <param name="Kinds">The question kinds the provider accepts.</param>
/// <param name="MaxQuestions">The most questions per request, or <see langword="null"/> for no limit.</param>
/// <param name="MaxChoiceOptions">The most options per Choice question, or <see langword="null"/> for no limit.</param>
/// <param name="Images">Whether the provider accepts images; declared, not checked yet.</param>
/// <param name="ModelListing">Whether the provider serves <c>GET v1/models</c>.</param>
internal readonly record struct CapabilitySet(
    SystemOneQuestionKinds Kinds, int? MaxQuestions, int? MaxChoiceOptions, bool Images, bool ModelListing)
{
    /// <summary>Gets a preset's defaults.</summary>
    /// <param name="provider">The preset.</param>
    /// <returns>What the preset's vendor accepts.</returns>
    internal static CapabilitySet For(SystemOneProvider provider) => provider switch
    {
        SystemOneProvider.TypeSafe => new(SystemOneQuestionKinds.All, null, 255, Images: false, ModelListing: true),
        SystemOneProvider.OpenRouter => new(SystemOneQuestionKinds.All, null, 255, Images: false, ModelListing: false),
        SystemOneProvider.CloudflareWorkersAi => new(SystemOneQuestionKinds.All, 64, 255, Images: true, ModelListing: false),
        SystemOneProvider.VllmSemanticRouter => new(SystemOneQuestionKinds.All, null, null, Images: false, ModelListing: false),
        _ => new(SystemOneQuestionKinds.All, null, null, Images: false, ModelListing: true),
    };

    /// <summary>Applies the overrides: each one set wins, each one left <see langword="null"/> keeps this value.</summary>
    /// <param name="overrides">The options' capabilities, already checked by the settings resolver.</param>
    /// <returns>The merged capabilities.</returns>
    internal CapabilitySet With(SystemOneCapabilities overrides) => new(
        overrides.QuestionKinds ?? Kinds,
        overrides.MaxQuestions ?? MaxQuestions,
        overrides.MaxChoiceOptions ?? MaxChoiceOptions,
        overrides.Images ?? Images,
        overrides.ModelListing ?? ModelListing);

    /// <summary>Copies these values into a new, fully set <see cref="SystemOneCapabilities"/>, for <c>GetService</c>.</summary>
    /// <returns>A new instance; changing it changes nothing in the client.</returns>
    internal SystemOneCapabilities ToOptions() => new()
    {
        QuestionKinds = Kinds,
        MaxQuestions = MaxQuestions,
        MaxChoiceOptions = MaxChoiceOptions,
        Images = Images,
        ModelListing = ModelListing,
    };

    /// <summary>
    /// Checks a question set against these capabilities. Walks the questions once and allocates nothing when the set
    /// passes; the failures and their messages are built only once one is found.
    /// </summary>
    /// <param name="definition">The question set about to be sent.</param>
    /// <param name="provider">The preset, named in each message.</param>
    /// <returns>
    /// <see langword="null"/> when the set is supported; otherwise an <see cref="DecisionErrorKind.Unsupported"/> error
    /// listing every failure: the MIN202 set-level one first, then each question's in wire order, MIN201 before MIN203.
    /// Its message is the first failure's.
    /// </returns>
    internal DecisionError? Check(QuestionSetDefinition definition, SystemOneProvider provider)
    {
        var questions = definition.QuestionArray;
        List<QuestionFailure>? failures = null;

        if (MaxQuestions is { } maxQuestions && questions.Length > maxQuestions)
        {
            failures = [new QuestionFailure(DiagnosticIds.TooManyQuestions, null, TooManyQuestions(provider, maxQuestions, questions.Length))];
        }

        foreach (var question in questions)
        {
            var kind = question.Kind;
            if ((Kinds & FlagOf(kind)) == SystemOneQuestionKinds.None)
            {
                (failures ??= []).Add(new QuestionFailure(
                    DiagnosticIds.UnsupportedQuestionKind, question.Key, provider.ToString() + " does not support " + NameOf(kind) + " questions."));
            }

            if (kind == QuestionKind.Choice && MaxChoiceOptions is { } maxOptions && question.OptionArray.Length > maxOptions)
            {
                (failures ??= []).Add(new QuestionFailure(
                    DiagnosticIds.TooManyChoiceOptions, question.Key, TooManyOptions(provider, maxOptions, question.Key, question.OptionArray.Length)));
            }
        }

        return failures is null ? null : new DecisionError(DecisionErrorKind.Unsupported, failures[0].Message, failures);
    }

    private static SystemOneQuestionKinds FlagOf(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => SystemOneQuestionKinds.Noul,
        QuestionKind.Choice => SystemOneQuestionKinds.Choice,
        QuestionKind.Score => SystemOneQuestionKinds.Score,
        _ => SystemOneQuestionKinds.None,
    };

    private static string NameOf(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => nameof(QuestionKind.Noul),
        QuestionKind.Choice => nameof(QuestionKind.Choice),
        _ => nameof(QuestionKind.Score),
    };

    private static string TooManyQuestions(SystemOneProvider provider, int max, int count)
        => provider.ToString() + " accepts at most " + max.ToString(CultureInfo.InvariantCulture)
            + (max == 1 ? " question" : " questions") + " per request; the set has " + count.ToString(CultureInfo.InvariantCulture) + ".";

    private static string TooManyOptions(SystemOneProvider provider, int max, string key, int count)
        => provider.ToString() + " accepts at most " + max.ToString(CultureInfo.InvariantCulture) + " options per Choice question; '"
            + key + "' has " + count.ToString(CultureInfo.InvariantCulture) + ".";
}
