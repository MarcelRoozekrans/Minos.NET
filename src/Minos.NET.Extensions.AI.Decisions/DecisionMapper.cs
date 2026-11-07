using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Minos.Generator;
using ZeroAlloc.Results;
using MinosRequest = Minos.DecisionRequest;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;

namespace Minos.Extensions.AI.Decisions;

/// <summary>
/// Converts questions, state and requests between Minos and the upstream decision abstractions, as
/// docs/extensions-ai-decisions/mapping.md describes row by row.
/// </summary>
/// <remarks>
/// Minos to upstream returns <see cref="DecisionErrorKind.Unsupported"/> before anything is sent for each rule the page
/// refuses: MIN207 for an empty set, then each question's failures in wire order, one per rule in rule-id order, then
/// MIN206 for images. Every other loss converts as the page says. No message carries question text or state.
/// </remarks>
[Experimental("MEAI001")]
internal static class DecisionMapper
{
    /// <summary>Converts a Minos request to an upstream request and its options.</summary>
    /// <param name="request">The Minos request.</param>
    /// <param name="providerName">The provider, named at the start of each failure message.</param>
    /// <returns>The upstream call, or <see cref="DecisionErrorKind.Unsupported"/> with every failure.</returns>
    /// <remarks>The retry attempt and the deadline are dropped; upstream requests have neither.</remarks>
    internal static Result<UpstreamCall, DecisionError> ToUpstream(MinosRequest request, string providerName)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CollectUnsupported(request.Definition, request.Images.Count, providerName) is { } failures)
        {
            return Result<UpstreamCall, DecisionError>.Failure(Unsupported(failures));
        }

        var upstream = new UpstreamRequest(ToUpstreamState(request.State), Convert(request.Definition));
        return Result<UpstreamCall, DecisionError>.Success(new UpstreamCall(upstream, new DecisionOptions { ModelId = request.Model }));
    }

    /// <summary>Converts a Minos question set to upstream questions, in wire order.</summary>
    /// <param name="definition">The question set.</param>
    /// <param name="providerName">The provider, named at the start of each failure message.</param>
    /// <returns>The questions, or <see cref="DecisionErrorKind.Unsupported"/> with every failure.</returns>
    internal static Result<IReadOnlyList<DecisionQuestion>, DecisionError> ToUpstreamQuestions(QuestionSetDefinition definition, string providerName)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return CollectUnsupported(definition, imageCount: 0, providerName) is { } failures
            ? Result<IReadOnlyList<DecisionQuestion>, DecisionError>.Failure(Unsupported(failures))
            : Result<IReadOnlyList<DecisionQuestion>, DecisionError>.Success(Convert(definition));
    }

    /// <summary>Every rule of the mapping page that <paramref name="definition"/> and its images break.</summary>
    /// <param name="definition">The question set about to be sent.</param>
    /// <param name="imageCount">How many images the request carries.</param>
    /// <param name="providerName">The provider, named at the start of each message.</param>
    /// <returns><see langword="null"/> when the set can be sent; otherwise every failure, in the order the class describes.</returns>
    internal static List<QuestionFailure>? CollectUnsupported(QuestionSetDefinition definition, int imageCount, string providerName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(providerName);

        List<QuestionFailure>? failures = null;
        var questions = definition.Questions;
        if (questions.Count == 0)
        {
            failures = [new QuestionFailure(DiagnosticIds.EmptyQuestionSet, null, providerName + " needs at least one question; the set has none.")];
        }

        foreach (var question in questions)
        {
            CollectQuestion(question, providerName, ref failures);
        }

        if (imageCount > 0)
        {
            (failures ??= []).Add(new QuestionFailure(DiagnosticIds.ImagesUnsupported, null, providerName + " does not support images."));
        }

        return failures;
    }

    /// <summary>Converts Minos state to upstream state: text as a JSON string, JSON as it is.</summary>
    /// <param name="state">The Minos state.</param>
    /// <returns>The JSON value.</returns>
    internal static JsonElement ToUpstreamState(DecisionContent state)
    {
        if (state.TryGetJson(out var json))
        {
            return json;
        }

        if (!state.TryGetString(out var text))
        {
            // DecisionRequest checks its state is initialized, so this is only reached by a caller's own default.
            throw new ArgumentException("The state is uninitialized.", nameof(state));
        }

        var buffer = new ArrayBufferWriter<byte>(text.Length + 2);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStringValue(text);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    /// <summary>Converts an upstream request to a Minos request, with a new question set on each call.</summary>
    /// <param name="request">The upstream request.</param>
    /// <param name="options">The upstream options, for the model; may be <see langword="null"/>.</param>
    /// <returns>The Minos request, with retry attempt 0, no images and no deadline.</returns>
    /// <exception cref="DecisionClientException">The state is a number, <c>true</c>, <c>false</c> or <c>null</c>; not transient.</exception>
    /// <remarks>The options' additional properties and raw representation factory have no Minos slot and are ignored.</remarks>
    internal static MinosRequest ToMinos(UpstreamRequest request, DecisionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(request);

        var state = ToMinosState(request.State);
        return new MinosRequest(ToMinosDefinition(request.Questions), state) { Model = options?.ModelId };
    }

    /// <summary>Converts upstream state to Minos state: a JSON string to text, an object or array to JSON.</summary>
    /// <param name="state">The upstream state.</param>
    /// <returns>The Minos content.</returns>
    /// <exception cref="DecisionClientException">The state is a number, <c>true</c>, <c>false</c> or <c>null</c>; not transient.</exception>
    internal static DecisionContent ToMinosState(JsonElement state)
        => state.ValueKind is JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array
            ? DecisionContent.FromJson(state)
            : throw new DecisionClientException(
                "A Minos decision client takes state that is a JSON string, object or array only.", isTransient: false);

    /// <summary>
    /// Converts upstream questions to a Minos question set, in order. Instructions and descriptions become text. Score
    /// levels are keyed by position, as Minos keys them; their upstream ids stay on the upstream question.
    /// </summary>
    /// <param name="questions">The upstream questions, which upstream has already checked.</param>
    /// <returns>A new question set.</returns>
    internal static QuestionSetDefinition ToMinosDefinition(IReadOnlyList<DecisionQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var converted = new QuestionDefinition[questions.Count];
        for (var i = 0; i < converted.Length; i++)
        {
            converted[i] = ToMinos(questions[i]);
        }

        return new QuestionSetDefinition(converted);
    }

    private static QuestionDefinition ToMinos(DecisionQuestion question) => question switch
    {
        BinaryDecisionQuestion binary => QuestionDefinition.Noul(
            binary.Id, binary.Instructions, TextOrNull(binary.TrueDescription), TextOrNull(binary.FalseDescription)),
        ChoiceDecisionQuestion choice => QuestionDefinition.Choice(choice.Id, choice.Instructions, Options(choice.Candidates)),
        ScoreDecisionQuestion score => QuestionDefinition.Score(score.Id, score.Instructions, Levels(score.Levels)),

        // The base constructor is private protected, so upstream's three question types are the only ones.
        _ => throw new UnreachableException(),
    };

    private static OptionDefinition[] Options(IReadOnlyList<DecisionCandidate> candidates)
    {
        var options = new OptionDefinition[candidates.Count];
        for (var i = 0; i < options.Length; i++)
        {
            options[i] = new OptionDefinition(candidates[i].Id, Criterion.Text(candidates[i].Description));
        }

        return options;
    }

    private static Criterion[] Levels(IReadOnlyList<DecisionScoreLevel> levels)
    {
        var criteria = new Criterion[levels.Count];
        for (var i = 0; i < criteria.Length; i++)
        {
            criteria[i] = Criterion.Text(levels[i].Description);
        }

        return criteria;
    }

    private static DecisionContent? TextOrNull(string? text) => text is null ? (DecisionContent?)null : DecisionContent.FromString(text);

    // Called only once CollectUnsupported found nothing, so every upstream constructor check passes.
    private static DecisionQuestion[] Convert(QuestionSetDefinition definition)
    {
        var questions = definition.Questions;
        var converted = new DecisionQuestion[questions.Count];
        for (var i = 0; i < converted.Length; i++)
        {
            converted[i] = Convert(questions[i]);
        }

        return converted;
    }

    private static DecisionQuestion Convert(QuestionDefinition question)
    {
        // Text as it is, JSON as its JSON text: the provider no longer knows the instructions were JSON.
        var instructions = question.Instructions.ToString();
        switch (question.Kind)
        {
            case QuestionKind.Noul:
                return new BinaryDecisionQuestion(question.Key, instructions, StringOrNull(question.WhenTrue), StringOrNull(question.WhenFalse));
            case QuestionKind.Choice:
                var candidates = new DecisionCandidate[question.Options.Count];
                for (var i = 0; i < candidates.Length; i++)
                {
                    var option = question.Options[i];
                    candidates[i] = new DecisionCandidate(option.Key, Description(option));
                }

                return new ChoiceDecisionQuestion(question.Key, instructions, candidates);
            default:
                var levels = new DecisionScoreLevel[question.Options.Count];
                for (var i = 0; i < levels.Length; i++)
                {
                    // The level ids are Minos's position keys, "0", "1" and so on.
                    var level = question.Options[i];
                    levels[i] = new DecisionScoreLevel(level.Key, Description(level));
                }

                return new ScoreDecisionQuestion(question.Key, instructions, levels);
        }
    }

    // An undescribed option is sent with its key, since the key is all a Minos provider sees for it; a JSON criterion
    // as its JSON text.
    private static string Description(OptionDefinition option) => option.Criterion switch
    {
        null => option.Key,
        { Description: { } text } => text,
        { JsonContent: { } json } => json.ToString(),
        _ => throw new UnreachableException(),
    };

    private static string? StringOrNull(DecisionContent? content) => content?.ToString();

    private static void CollectQuestion(QuestionDefinition question, string providerName, ref List<QuestionFailure>? failures)
    {
        var key = question.Key;
        var options = question.Options;

        var detail = false;
        var blankId = string.IsNullOrWhiteSpace(key);
        var blankDescription = false;
        foreach (var option in options)
        {
            blankId |= string.IsNullOrWhiteSpace(option.Key);
            if (option.Criterion is { Description: { } text } criterion)
            {
                blankDescription |= string.IsNullOrWhiteSpace(text);
                detail |= criterion.Examples.Count > 0 || criterion.NotFor.Count > 0;
            }
        }

        var missingInstructions = false;
        if (question.Kind == QuestionKind.Noul)
        {
            blankDescription |= IsBlankText(question.WhenTrue) || IsBlankText(question.WhenFalse);
            missingInstructions = IsBlankText(question.Instructions)
                && (IsBlankOrMissing(question.WhenTrue) || IsBlankOrMissing(question.WhenFalse));
        }

        if (detail)
        {
            Add(ref failures, DiagnosticIds.CriterionDetailUnsupported, key,
                providerName + " does not support Examples or NotFor on a criterion; describe them in its text.");
        }

        if (blankId)
        {
            Add(ref failures, DiagnosticIds.BlankId, key,
                providerName + " does not accept a question or option key that is white space only.");
        }

        if (blankDescription)
        {
            Add(ref failures, DiagnosticIds.BlankDescription, key,
                providerName + " does not accept a description that is empty or white space only.");
        }

        if (missingInstructions)
        {
            Add(ref failures, DiagnosticIds.NoulWithoutInstructions, key,
                providerName + " needs a Noul's instructions, or both WhenTrue and WhenFalse, to be more than white space.");
        }

        if (question.Kind == QuestionKind.Score && options.Count < 2)
        {
            Add(ref failures, DiagnosticIds.SingleLevelScore, key,
                providerName + " needs at least two levels on a Score question.");
        }
    }

    private static void Add(ref List<QuestionFailure>? failures, string rule, string key, string message)
        => (failures ??= []).Add(new QuestionFailure(rule, key, message));

    // JSON content is an object or array, never blank.
    private static bool IsBlankText(DecisionContent? content)
        => content is { } value && value.TryGetString(out var text) && string.IsNullOrWhiteSpace(text);

    private static bool IsBlankOrMissing(DecisionContent? content) => content is null || IsBlankText(content);

    private static DecisionError Unsupported(List<QuestionFailure> failures)
        => new(DecisionErrorKind.Unsupported, failures[0].Message, failures);
}

/// <summary>An upstream request and the options it is sent with.</summary>
/// <param name="Request">The upstream request.</param>
/// <param name="Options">The options, with the model id.</param>
[Experimental("MEAI001")]
internal readonly record struct UpstreamCall(UpstreamRequest Request, DecisionOptions Options);
