using System.Globalization;
using Microsoft.Extensions.AI;
using Minos.Generator;
using Minos.Transport;
using OpenAI.Chat;
using ZeroAlloc.Results;

namespace Minos.Extensions.AI;

/// <summary>
/// Answers a request's Noul, Choice and Score questions by asking a Microsoft.Extensions.AI <see cref="IChatClient"/>,
/// such as one for OpenAI, OpenRouter, Azure or Ollama.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe. The bridge does not own the chat client: <see cref="Dispose"/> leaves it usable, so it can be shared
/// with other code or the container.
/// </para>
/// <para>
/// A request is checked before anything is sent. One with no questions returns <see cref="DecisionErrorKind.Validation"/>,
/// and one that <see cref="ChatDecisionClientOptions.Capabilities"/> does not accept returns
/// <see cref="DecisionErrorKind.Unsupported"/>. The bridge does not retry; the pipeline's retry stage does.
/// </para>
/// </remarks>
public sealed class ChatDecisionClient : IDecisionClient
{
    // The name capability failures give the bridge, as in "The chat client bridge does not support images.".
    private const string ProviderLabel = "The chat client bridge";

    // The telemetry stages' own name for a provider they cannot name, used when the chat client has no metadata.
    private const string UnknownProvider = "unknown";

    // The most top logprobs OpenAI returns per token.
    private const int TopLogprobCount = 20;

    // Options and levels are labelled A to Z.
    private static readonly CapabilitySet Defaults = new(QuestionKinds.All, null, LabelDistribution.MaxLabels, Images: false, ModelListing: false);

    private readonly IChatClient _chatClient;
    private readonly CapabilitySet _capabilities;
    private readonly DecisionClientMetadata _metadata;
    private readonly string? _model;
    private readonly ProbabilityMode _probabilityMode;
    // Volatile: Dispose writes it, and the threads that start calls read it.
    private volatile bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ChatDecisionClient"/> class over <paramref name="chatClient"/>.</summary>
    /// <param name="chatClient">The chat client to ask. It is not disposed with this client.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    public ChatDecisionClient(IChatClient chatClient, ChatDecisionClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        options ??= new ChatDecisionClientOptions();

        var model = options.Model is { } explicitModel ? ClientSettingRules.CheckModel(explicitModel, nameof(options)) : null;
        if (options.Capabilities?.MaxChoiceOptions is > LabelDistribution.MaxLabels)
        {
            throw new ArgumentException("Capabilities.MaxChoiceOptions must be at most 26: options are labelled A to Z.", nameof(options));
        }

        // The bridge has no model listing, so ModelListing stays false whatever the options say, and GetService reports
        // the truth.
        _capabilities = ClientSettingRules.ResolveCapabilities(Defaults, options.Capabilities, nameof(options)) with { ModelListing = false };

        var chatMetadata = chatClient.GetService<ChatClientMetadata>();
        _metadata = new DecisionClientMetadata(
            chatMetadata?.ProviderName ?? UnknownProvider, chatMetadata?.ProviderUri, model ?? chatMetadata?.DefaultModelId);
        _chatClient = chatClient;
        _model = model;
        _probabilityMode = options.ProbabilityMode;
    }

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state, any images and, optionally, the model.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(DecisionRequest, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state, any images and, optionally, the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <remarks>
    /// <para>
    /// A request with no questions returns <see cref="DecisionErrorKind.Validation"/>. One that
    /// <see cref="ChatDecisionClientOptions.Capabilities"/> does not accept, or with a Score question of more than 26
    /// levels, returns <see cref="DecisionErrorKind.Unsupported"/>. Neither calls the chat client.
    /// </para>
    /// <para>
    /// Each question is asked in its own chat call. <see cref="ChatDecisionClientOptions.ProbabilityMode"/> sets how its
    /// probabilities are made:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ProbabilityMode.Logprobs"/> reads them from the logprobs of the first output token. A response without
    /// logprobs returns <see cref="DecisionErrorKind.Unsupported"/>, and one with no usable option label among the top
    /// logprobs returns <see cref="DecisionErrorKind.InvalidResponse"/>.
    /// </description></item>
    /// <item><description>
    /// <see cref="ProbabilityMode.Verbalized"/> asks the model to state each option's probability as JSON. A reply that
    /// is not that JSON, misses an option, or states a negative value or all zeros returns
    /// <see cref="DecisionErrorKind.InvalidResponse"/>.
    /// </description></item>
    /// <item><description>
    /// <see cref="ProbabilityMode.Auto"/> tries logprobs first. When that response has none, or no usable option label,
    /// the question is asked once more, verbalized, and that reply decides.
    /// </description></item>
    /// </list>
    /// <para>
    /// Each answer's <see cref="QuestionAnswer.ProbabilitySource"/> says which path made it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Definition.QuestionArray.Length == 0)
        {
            return Failure(new DecisionError(DecisionErrorKind.Validation, "The request has no questions."));
        }

        if (CheckCapabilities(request) is { } failures)
        {
            return Failure(CapabilitySet.Unsupported(failures));
        }

        return AnswerAllAsync(request, cancellationToken);
    }

    // Explicit, so the public pair above follows the (x)/(x, CancellationToken) convention: implementing the interface's
    // optional parameter implicitly would either drop its default or give this class an overload with an optional one.
    ValueTask<Result<DecisionResponse, DecisionError>> IDecisionClient.EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken)
        => EvaluateAsync(request, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Returns this client for a type it is, a new <see cref="DecisionCapabilities"/> with its resolved capabilities,
    /// every property set, and its <see cref="DecisionClientMetadata"/>: the chat client's provider name, endpoint and
    /// default model, with <see cref="ChatDecisionClientOptions.Model"/> in place of the model when set, and
    /// <c>unknown</c> as the provider name when the chat client gives none. Anything else is asked of the chat client.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null)
        {
            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType == typeof(DecisionCapabilities))
            {
                return _capabilities.ToOptions();
            }

            if (serviceType == typeof(DecisionClientMetadata))
            {
                return _metadata;
            }
        }

        return _chatClient.GetService(serviceType, serviceKey);
    }

    /// <summary>Marks the client disposed. The chat client is not disposed: the caller or the container owns it.</summary>
    /// <remarks>A call started after <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>. Calling it more than once does nothing.</remarks>
    public void Dispose() => _disposed = true;

    private static ValueTask<Result<DecisionResponse, DecisionError>> Failure(DecisionError error)
        => new(Result<DecisionResponse, DecisionError>.Failure(error));

    private static bool HasTooManyLevels(QuestionDefinition question)
        => question.Kind == QuestionKind.Score && question.OptionArray.Length > LabelDistribution.MaxLabels;

    private static string TooManyLevels(QuestionDefinition question)
        => ProviderLabel + " accepts at most " + LabelDistribution.MaxLabels.ToString(CultureInfo.InvariantCulture)
            + " levels per Score question; '" + question.Key + "' has " + question.OptionArray.Length.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>
    /// The capability failures, as <see cref="CapabilitySet.Collect"/> orders them: MIN202 first, then each question's in
    /// order, then MIN206. A Score with more levels than labels adds its MIN203 among its question's failures, since the
    /// capabilities cap Choice options only. Returns null when the request is supported.
    /// </summary>
    private List<QuestionFailure>? CheckCapabilities(DecisionRequest request)
    {
        var questions = request.Definition.QuestionArray;
        var collected = _capabilities.Collect(request.Definition, request.Images.Count, ProviderLabel);
        if (!Array.Exists(questions, HasTooManyLevels))
        {
            return collected;
        }

        collected ??= [];
        var failures = new List<QuestionFailure>(collected.Count + 1);
        var next = 0;
        if (next < collected.Count && string.Equals(collected[next].Rule, DiagnosticIds.TooManyQuestions, StringComparison.Ordinal))
        {
            failures.Add(collected[next++]);
        }

        // Collect lists each question's failures together and in question order, so one pass merges them.
        foreach (var question in questions)
        {
            while (next < collected.Count && string.Equals(collected[next].QuestionKey, question.Key, StringComparison.Ordinal))
            {
                failures.Add(collected[next++]);
            }

            if (HasTooManyLevels(question))
            {
                failures.Add(new QuestionFailure(DiagnosticIds.TooManyChoiceOptions, question.Key, TooManyLevels(question)));
            }
        }

        for (; next < collected.Count; next++)
        {
            failures.Add(collected[next]);
        }

        return failures;
    }

    // One call per question, in order. The first failure fails the request.
    private async ValueTask<Result<DecisionResponse, DecisionError>> AnswerAllAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        var questions = request.Definition.QuestionArray;
        var answers = new QuestionAnswer[questions.Length];
        string? servedModel = null;
        for (var i = 0; i < questions.Length; i++)
        {
            var result = await AnswerAsync(questions[i], request, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<DecisionResponse, DecisionError>.Failure(result.Error);
            }

            answers[i] = result.Value.Answer;
            servedModel ??= result.Value.Response.ModelId;
        }

        return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, answers, servedModel));
    }

    /// <summary>
    /// Answers one question by the configured <see cref="ProbabilityMode"/>. In <see cref="ProbabilityMode.Auto"/>, a
    /// logprob response with no logprobs, or with no usable label among them, is asked again verbalized, once. Returns
    /// the chat response that gave the answer too, for its model and usage. Exceptions from the chat client propagate.
    /// </summary>
    private async ValueTask<Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError>> AnswerAsync(
        QuestionDefinition question, DecisionRequest request, CancellationToken cancellationToken)
    {
        if (_probabilityMode == ProbabilityMode.Verbalized)
        {
            return await AskVerbalizedAsync(question, request, cancellationToken).ConfigureAwait(false);
        }

        var result = await AskLogprobsAsync(question, request, cancellationToken).ConfigureAwait(false);
        // Its only failures are no logprobs and no usable label among them; the chat client's exceptions propagate.
        if (result.IsFailure && _probabilityMode == ProbabilityMode.Auto)
        {
            return await AskVerbalizedAsync(question, request, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Asks one question for a single label and reads the answer from the first output token's logprobs.</summary>
    private async ValueTask<Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError>> AskLogprobsAsync(
        QuestionDefinition question, DecisionRequest request, CancellationToken cancellationToken)
    {
        var model = request.Model ?? _model;
        var chatOptions = new ChatOptions
        {
            ModelId = model,
            MaxOutputTokens = 1,
            Temperature = 0,
            // A new SDK options object per call: the OpenAI chat client fills in the rest of it.
            RawRepresentationFactory = static _ => new ChatCompletionOptions
            {
                IncludeLogProbabilities = true,
                TopLogProbabilityCount = TopLogprobCount,
            },
        };

        var messages = QuestionPrompt.Build(question, request, verbalized: false);
        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);

        // The messages below name the model and the question key, never the state or the model's output.
        if (LogprobReader.TopAtFirstToken(response) is not { } top)
        {
            return AnswerFailure(new DecisionError(
                DecisionErrorKind.Unsupported,
                "The model '" + ModelName(response, model) + "' returned no logprobs. Set ProbabilityMode to Verbalized for a model without them."));
        }

        if (LabelDistribution.FromLogprobs(LabelCount(question), top) is not { } p)
        {
            return AnswerFailure(new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "The model '" + ModelName(response, model) + "' gave no usable option label among its top logprobs for question '" + question.Key + "'."));
        }

        return Answered(question, p, ProbabilitySource.Logprobs, response);
    }

    /// <summary>Asks one question for each label's probability, as JSON that follows a schema, and reads the answer from it.</summary>
    private async ValueTask<Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError>> AskVerbalizedAsync(
        QuestionDefinition question, DecisionRequest request, CancellationToken cancellationToken)
    {
        var model = request.Model ?? _model;
        var labelCount = LabelCount(question);
        var chatOptions = new ChatOptions
        {
            ModelId = model,
            Temperature = 0,
            ResponseFormat = VerbalizedReader.Format(labelCount),
        };

        var messages = QuestionPrompt.Build(question, request, verbalized: true);
        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);

        if (VerbalizedReader.Read(response.Text, labelCount) is not { } stated || LabelDistribution.FromStated(labelCount, stated) is not { } p)
        {
            // Names the model and the question key, never the state or the model's output.
            return AnswerFailure(new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "The model '" + ModelName(response, model) + "' gave no usable probability for each option of question '" + question.Key + "'."));
        }

        return Answered(question, p, ProbabilitySource.Verbalized, response);
    }

    private string ModelName(ChatResponse response, string? model)
        => response.ModelId ?? model ?? _metadata.DefaultModel ?? "the chat client's default model";

    private static int LabelCount(QuestionDefinition question)
        => question.Kind == QuestionKind.Noul ? 2 : question.OptionArray.Length;

    private static Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError> AnswerFailure(DecisionError error)
        => Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError>.Failure(error);

    // p is the distribution over the question's labels; for a Noul, A is yes.
    private static Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError> Answered(
        QuestionDefinition question, double[] p, ProbabilitySource source, ChatResponse response)
    {
        QuestionAnswer answer;
        if (question.Kind == QuestionKind.Noul)
        {
            answer = QuestionAnswer.Noul(p[0], source);
        }
        else
        {
            var (picked, confidence) = LabelDistribution.Pick(p);
            answer = question.Kind == QuestionKind.Choice
                ? QuestionAnswer.Choice(picked, confidence, p, source)
                : QuestionAnswer.Score(picked, LabelDistribution.Expected(p), confidence, p, source);
        }

        return Result<(QuestionAnswer Answer, ChatResponse Response), DecisionError>.Success((answer, response));
    }
}
