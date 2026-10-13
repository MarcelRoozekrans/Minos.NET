using System.Globalization;
using Minos.Generator;

namespace Minos.Transport;

/// <summary>
/// The resolved <see cref="DecisionCapabilities"/>: a provider's defaults with the options' overrides applied, and the check
/// a request must pass before it is sent.
/// </summary>
/// <param name="Kinds">The question kinds the provider accepts.</param>
/// <param name="MaxQuestions">The most questions per request, or <see langword="null"/> for no limit.</param>
/// <param name="MaxChoiceOptions">The most options per Choice question, or <see langword="null"/> for no limit.</param>
/// <param name="Images">Whether the provider accepts images.</param>
/// <param name="ModelListing">Whether the provider serves a model listing.</param>
internal readonly record struct CapabilitySet(
    QuestionKinds Kinds, int? MaxQuestions, int? MaxChoiceOptions, bool Images, bool ModelListing)
{
    /// <summary>Applies the overrides: each one set wins, each one left <see langword="null"/> keeps this value.</summary>
    /// <param name="overrides">The options' capabilities, already checked by the settings resolver.</param>
    /// <returns>The merged capabilities.</returns>
    internal CapabilitySet With(DecisionCapabilities overrides) => new(
        overrides.QuestionKinds ?? Kinds,
        overrides.MaxQuestions ?? MaxQuestions,
        overrides.MaxChoiceOptions ?? MaxChoiceOptions,
        overrides.SupportsImages ?? Images,
        overrides.SupportsModelListing ?? ModelListing);

    /// <summary>Copies these values into a new, fully set <see cref="DecisionCapabilities"/>, for <c>GetService</c>.</summary>
    /// <returns>A new instance; changing it changes nothing in the client.</returns>
    internal DecisionCapabilities ToOptions() => new()
    {
        QuestionKinds = Kinds,
        MaxQuestions = MaxQuestions,
        MaxChoiceOptions = MaxChoiceOptions,
        SupportsImages = Images,
        SupportsModelListing = ModelListing,
    };

    /// <summary>
    /// Checks a request against these capabilities. Walks the questions once and allocates nothing when the request
    /// passes; the failures and their messages are built only once one is found.
    /// </summary>
    /// <param name="definition">The question set about to be sent.</param>
    /// <param name="imageCount">How many images the request carries.</param>
    /// <param name="providerName">
    /// The provider, named in each message, as <c>TypeSafe</c> is in "TypeSafe does not support Score questions.".
    /// </param>
    /// <returns>
    /// <see langword="null"/> when the request is supported; otherwise every failure: the MIN202 set-level one first, then
    /// each question's in wire order, MIN201 before MIN203, then MIN206 for images.
    /// </returns>
    internal List<QuestionFailure>? Collect(QuestionSetDefinition definition, int imageCount, string providerName)
    {
        var questions = definition.QuestionArray;
        List<QuestionFailure>? failures = null;

        if (MaxQuestions is { } maxQuestions && questions.Length > maxQuestions)
        {
            failures = [new QuestionFailure(DiagnosticIds.TooManyQuestions, null, TooManyQuestions(providerName, maxQuestions, questions.Length))];
        }

        foreach (var question in questions)
        {
            var kind = question.Kind;
            if ((Kinds & FlagOf(kind)) == QuestionKinds.None)
            {
                (failures ??= []).Add(new QuestionFailure(
                    DiagnosticIds.UnsupportedQuestionKind, question.Key, providerName + " does not support " + NameOf(kind) + " questions."));
            }

            if (kind == QuestionKind.Choice && MaxChoiceOptions is { } maxOptions && question.OptionArray.Length > maxOptions)
            {
                (failures ??= []).Add(new QuestionFailure(
                    DiagnosticIds.TooManyChoiceOptions, question.Key, TooManyOptions(providerName, maxOptions, question.Key, question.OptionArray.Length)));
            }
        }

        if (imageCount > 0 && !Images)
        {
            (failures ??= []).Add(new QuestionFailure(DiagnosticIds.ImagesUnsupported, null, providerName + " does not support images."));
        }

        return failures;
    }

    /// <summary>Wraps capability failures in one <see cref="DecisionErrorKind.Unsupported"/> error.</summary>
    /// <param name="failures">The failures, at least one; the first one's message becomes the error's.</param>
    /// <returns>The error, listing every failure.</returns>
    internal static DecisionError Unsupported(List<QuestionFailure> failures)
        => new(DecisionErrorKind.Unsupported, failures[0].Message, failures);

    private static QuestionKinds FlagOf(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => QuestionKinds.Noul,
        QuestionKind.Choice => QuestionKinds.Choice,
        QuestionKind.Score => QuestionKinds.Score,
        _ => QuestionKinds.None,
    };

    private static string NameOf(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => nameof(QuestionKind.Noul),
        QuestionKind.Choice => nameof(QuestionKind.Choice),
        _ => nameof(QuestionKind.Score),
    };

    private static string TooManyQuestions(string providerName, int max, int count)
        => providerName + " accepts at most " + max.ToString(CultureInfo.InvariantCulture)
            + (max == 1 ? " question" : " questions") + " per request; the set has " + count.ToString(CultureInfo.InvariantCulture) + ".";

    private static string TooManyOptions(string providerName, int max, string key, int count)
        => providerName + " accepts at most " + max.ToString(CultureInfo.InvariantCulture) + " options per Choice question; '"
            + key + "' has " + count.ToString(CultureInfo.InvariantCulture) + ".";
}
