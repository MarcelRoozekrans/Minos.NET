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
    private readonly int? _maxParallelCalls;
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
        if (options.ProbabilityMode is < ProbabilityMode.Auto or > ProbabilityMode.Verbalized)
        {
            throw new ArgumentException("ProbabilityMode must be Auto, Logprobs or Verbalized.", nameof(options));
        }

        if (options.Capabilities?.MaxChoiceOptions is > LabelDistribution.MaxLabels)
        {
            throw new ArgumentException("Capabilities.MaxChoiceOptions must be at most 26: options are labelled A to Z.", nameof(options));
        }

        if (options.MaxParallelCalls is < 1)
        {
            throw new ArgumentException("MaxParallelCalls must be at least 1, or null for no limit.", nameof(options));
        }

        // The bridge has no model listing, so ModelListing stays false whatever the options say, and GetService reports
        // the truth.
        _capabilities = ClientSettingRules.ResolveCapabilities(Defaults, options.Capabilities, nameof(options)) with { ModelListing = false };

        var chatMetadata = chatClient.GetService<ChatClientMetadata>();
        _metadata = new DecisionClientMetadata(
            chatMetadata?.ProviderName ?? UnknownProvider, chatMetadata?.ProviderUri, model ?? chatMetadata?.DefaultModelId)
        {
            CacheScope = CacheScope(options.ProbabilityMode, chatClient, chatMetadata, model),
        };
        _chatClient = chatClient;
        _model = model;
        _probabilityMode = options.ProbabilityMode;
        _maxParallelCalls = options.MaxParallelCalls;
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
    /// Each question is asked in its own chat call, and the calls run at once, at most
    /// <see cref="ChatDecisionClientOptions.MaxParallelCalls"/> of them when it is set. The answers come back in definition
    /// order whatever order the calls finish in. <see cref="ChatDecisionClientOptions.ProbabilityMode"/> sets how each
    /// question's probabilities are made:
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
    /// <see cref="DecisionErrorKind.InvalidResponse"/>. This call sends no temperature, which reasoning models reject.
    /// </description></item>
    /// <item><description>
    /// <see cref="ProbabilityMode.Auto"/> tries logprobs first. When that response has none, or no usable option label,
    /// or the provider rejects the logprob request as invalid (HTTP 400 or 422, as reasoning models do), the question is
    /// asked once more, verbalized, and that call decides, its error included.
    /// </description></item>
    /// </list>
    /// <para>
    /// Each answer's <see cref="QuestionAnswer.ProbabilitySource"/> says which path made it.
    /// <see cref="DecisionResponse.Usage"/> sums the tokens of every call, the logprob call before a fallback included,
    /// and <see cref="DecisionResponse.Model"/> is the first model the responses name, in question order. When none names
    /// one, it is the request's <see cref="DecisionRequest.Model"/>, then <see cref="ChatDecisionClientOptions.Model"/>.
    /// </para>
    /// <para>
    /// The first failed question fails the request and cancels the other calls, and questions still waiting for a free
    /// call are not asked. An exception from the chat client is returned as an error when Minos knows it: an OpenAI SDK
    /// <c>ClientResultException</c> by its HTTP status, with <see cref="DecisionError.RetryAfter"/> and
    /// <see cref="DecisionError.RequestId"/> from its headers; an <see cref="HttpRequestException"/> as
    /// <see cref="DecisionErrorKind.Network"/>; and a <see cref="TimeoutException"/> as
    /// <see cref="DecisionErrorKind.Timeout"/>. Any other exception propagates, so a bug does not pass as a provider
    /// failure. The error's message is the bridge's own; the provider's exception is in
    /// <see cref="DecisionError.Exception"/>, and a JSON error body in <see cref="DecisionError.Detail"/>. Once the
    /// caller has cancelled, the call throws <see cref="OperationCanceledException"/> instead of returning an error.
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
    /// <c>unknown</c> as the provider name when the chat client gives none. Its <see cref="DecisionClientMetadata.CacheScope"/>
    /// is the <see cref="ChatDecisionClientOptions.ProbabilityMode"/> in lowercase: <c>auto</c>, <c>logprobs</c> or
    /// <c>verbalized</c>. When the chat client names no provider, endpoint or model and no
    /// <see cref="ChatDecisionClientOptions.Model"/> is set, a <c>/</c> and the chat client's full type name follow. Anything
    /// else is asked of the chat client.
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

    /// <summary>
    /// The probability mode, so clients that make their probabilities differently never share a cache entry. When the
    /// chat client names no provider, endpoint or model and the options set no model, its type is added, so two different
    /// chat clients without metadata do not share one either.
    /// </summary>
    private static string CacheScope(ProbabilityMode mode, IChatClient chatClient, ChatClientMetadata? chatMetadata, string? model)
    {
        // The constructor rejects a value outside the enum, so Auto is the only mode left.
        var scope = mode switch
        {
            ProbabilityMode.Logprobs => "logprobs",
            ProbabilityMode.Verbalized => "verbalized",
            _ => "auto",
        };

        if (model is null && chatMetadata?.ProviderName is null && chatMetadata?.ProviderUri is null && chatMetadata?.DefaultModelId is null)
        {
            var type = chatClient.GetType();
            scope += "/" + (type.FullName ?? type.Name);
        }

        return scope;
    }

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

    /// <summary>
    /// Asks every question at once, one call each, and puts each answer at its question's index, whatever order the calls
    /// finish in. With a MaxParallelCalls below the question count, each question first waits for one of that many slots.
    /// The first failure, returned or mapped from an exception, fails the request and cancels the other calls. An
    /// exception the mapper does not know propagates unchanged, and cancels the other calls too, unless a failure is
    /// already recorded: that failure cancelled the call, so it decides. The caller's cancellation throws,
    /// even when the chat client reports it as a failure the mapper knows.
    /// </summary>
    private async ValueTask<Result<DecisionResponse, DecisionError>> AnswerAllAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        var questions = request.Definition.QuestionArray;
        var calls = new QuestionCall[questions.Length];
        DecisionError? failure = null;
        using var rest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var slots = _maxParallelCalls is { } most && most < questions.Length ? new SemaphoreSlim(most, most) : null;

        var running = new Task[questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            running[i] = AnswerOneAsync(i);
        }

        await Task.WhenAll(running).ConfigureAwait(false);
        if (failure is not null)
        {
            return Result<DecisionResponse, DecisionError>.Failure(failure);
        }

        string? servedModel = null;
        long? inputTokens = null;
        long? outputTokens = null;
        var answers = new QuestionAnswer[questions.Length];
        for (var i = 0; i < calls.Length; i++)
        {
            answers[i] = calls[i].Answer;
            servedModel ??= calls[i].Model;
            inputTokens = Sum(inputTokens, calls[i].InputTokens);
            outputTokens = Sum(outputTokens, calls[i].OutputTokens);
        }

        var usage = new DecisionUsage { InputTokens = Clamp(inputTokens), OutputTokens = Clamp(outputTokens) };
        return Result<DecisionResponse, DecisionError>.Success(
            new DecisionResponse(request.Definition, answers, servedModel ?? request.Model ?? _model, usage));

        // Waits for a slot when there is a limit, and holds it until the question is answered, an Auto fallback call
        // included. The slot is released only after a failure is recorded and the other calls cancelled, so a question
        // still waiting sees the cancellation instead of calling the chat client.
        async Task AnswerOneAsync(int index)
        {
            if (slots is null)
            {
                await AskAsync(index).ConfigureAwait(false);
                return;
            }

            try
            {
                await slots.WaitAsync(rest.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Another call failed first and cancelled the wait; that failure decides.
                return;
            }

            try
            {
                await AskAsync(index).ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        }

        async Task AskAsync(int index)
        {
            Result<QuestionCall, DecisionError> result;
            try
            {
                result = await AnswerAsync(questions[index], request, rest.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (rest.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Another call failed first and cancelled this one; that failure decides.
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller asked for it, so it is never mapped, not even a time-out's cancellation that came first.
                throw;
            }
            catch (Exception exception) when (ChatErrorMapper.Map(exception) is { } error)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // The chat client reported the caller's cancellation as a failure of its own; the caller asked for it.
                    throw new OperationCanceledException("The call was cancelled.", exception, cancellationToken);
                }

                result = Result<QuestionCall, DecisionError>.Failure(error);
            }
            catch (Exception) when (Volatile.Read(ref failure) is not null && !cancellationToken.IsCancellationRequested)
            {
                // A sibling's failure is recorded and cancelled this call, which the chat client reported as an exception
                // of its own; the first failure decides.
                return;
            }
            catch
            {
                await rest.CancelAsync().ConfigureAwait(false);
                throw;
            }

            if (result.IsFailure)
            {
                // Only the first failure is kept.
                Interlocked.CompareExchange(ref failure, result.Error, null);
                await rest.CancelAsync().ConfigureAwait(false);
                return;
            }

            calls[index] = result.Value;
        }
    }

    /// <summary>
    /// Answers one question by the configured <see cref="ProbabilityMode"/>. In <see cref="ProbabilityMode.Auto"/>, a
    /// logprob response with no logprobs, or with no usable label among them, is asked again verbalized, once, and so is
    /// a logprob request the provider rejects as invalid. The answer carries the first response's model and the usage of
    /// every call made for it. Other exceptions from the chat client propagate.
    /// </summary>
    private async ValueTask<Result<QuestionCall, DecisionError>> AnswerAsync(
        QuestionDefinition question, DecisionRequest request, CancellationToken cancellationToken)
    {
        var model = request.Model ?? _model;
        ChatResponse? first = null;
        if (_probabilityMode != ProbabilityMode.Verbalized
            && await AskForLogprobsAsync(question, request, model, cancellationToken).ConfigureAwait(false) is { } response)
        {
            var read = ReadLogprobs(question, response, model);

            // Its only failures are no logprobs and no usable label among them.
            if (read.IsSuccess || _probabilityMode == ProbabilityMode.Logprobs)
            {
                return Called(read, response, previous: null);
            }

            first = response;
        }

        // No temperature: reasoning models reject any but their own, and this is the path they take.
        var labelCount = LabelCount(question);
        var verbalizedOptions = new ChatOptions { ModelId = model, ResponseFormat = VerbalizedReader.Format(labelCount) };
        var verbalized = await _chatClient.GetResponseAsync(
            QuestionPrompt.Build(question, request, verbalized: true), verbalizedOptions, cancellationToken).ConfigureAwait(false);
        return Called(ReadVerbalized(question, labelCount, verbalized, model), verbalized, first);
    }

    /// <summary>
    /// Sends the logprob call. In <see cref="ProbabilityMode.Auto"/>, a request the provider rejects as invalid, HTTP 400
    /// or 422 as reasoning models answer logprobs and temperature, returns <see langword="null"/>, so the question is
    /// asked verbalized instead. Any other exception propagates.
    /// </summary>
    private async Task<ChatResponse?> AskForLogprobsAsync(
        QuestionDefinition question, DecisionRequest request, string? model, CancellationToken cancellationToken)
    {
        try
        {
            return await _chatClient.GetResponseAsync(
                QuestionPrompt.Build(question, request, verbalized: false), LogprobOptions(model), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (_probabilityMode == ProbabilityMode.Auto
            && ChatErrorMapper.Map(exception) is { Kind: DecisionErrorKind.Validation })
        {
            return null;
        }
    }

    // One token at temperature 0, with the top logprobs. A new SDK options object per call: the OpenAI chat client
    // fills in the rest of it.
    private static ChatOptions LogprobOptions(string? model) => new()
    {
        ModelId = model,
        MaxOutputTokens = 1,
        Temperature = 0,
        RawRepresentationFactory = static _ => new ChatCompletionOptions
        {
            IncludeLogProbabilities = true,
            TopLogProbabilityCount = TopLogprobCount,
        },
    };

    /// <summary>Reads the answer from the first output token's logprobs.</summary>
    private Result<QuestionAnswer, DecisionError> ReadLogprobs(QuestionDefinition question, ChatResponse response, string? model)
    {
        // The messages below name the model and the question key, never the state or the model's output.
        if (LogprobReader.TopAtFirstToken(response) is not { } top)
        {
            return Result<QuestionAnswer, DecisionError>.Failure(new DecisionError(
                DecisionErrorKind.Unsupported,
                "The model '" + ModelName(response, model) + "' returned no logprobs. Set ProbabilityMode to Verbalized for a model without them."));
        }

        if (LabelDistribution.FromLogprobs(LabelCount(question), top) is not { } p)
        {
            return Result<QuestionAnswer, DecisionError>.Failure(new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "The model '" + ModelName(response, model) + "' gave no usable option label among its top logprobs for question '" + question.Key + "'."));
        }

        return Answer(question, p, ProbabilitySource.Logprobs);
    }

    /// <summary>Reads the answer from each label's stated probability, given as JSON that follows the schema.</summary>
    private Result<QuestionAnswer, DecisionError> ReadVerbalized(QuestionDefinition question, int labelCount, ChatResponse response, string? model)
    {
        if (VerbalizedReader.Read(response.Text, labelCount) is not { } stated || LabelDistribution.FromStated(labelCount, stated) is not { } p)
        {
            // Names the model and the question key, never the state or the model's output.
            return Result<QuestionAnswer, DecisionError>.Failure(new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "The model '" + ModelName(response, model) + "' gave no usable probability for each option of question '" + question.Key + "'."));
        }

        return Answer(question, p, ProbabilitySource.Verbalized);
    }

    private string ModelName(ChatResponse response, string? model)
        => response.ModelId ?? model ?? _metadata.DefaultModel ?? "the chat client's default model";

    private static int LabelCount(QuestionDefinition question)
        => question.Kind == QuestionKind.Noul ? 2 : question.OptionArray.Length;

    // A count is summed over the calls that report it, and stays null when none does.
    private static long? Sum(long? total, long? count) => count is { } value ? (total ?? 0) + value : total;

    private static int? Clamp(long? count) => count is { } value ? (int)Math.Clamp(value, 0, int.MaxValue) : null;

    // The answer from response, with the model and usage of previous, the logprob call an Auto fallback made first.
    private static Result<QuestionCall, DecisionError> Called(Result<QuestionAnswer, DecisionError> read, ChatResponse response, ChatResponse? previous)
    {
        if (read.IsFailure)
        {
            return Result<QuestionCall, DecisionError>.Failure(read.Error);
        }

        return Result<QuestionCall, DecisionError>.Success(new QuestionCall(
            read.Value,
            previous?.ModelId ?? response.ModelId,
            Sum(previous?.Usage?.InputTokenCount, response.Usage?.InputTokenCount),
            Sum(previous?.Usage?.OutputTokenCount, response.Usage?.OutputTokenCount)));
    }

    // p is the distribution over the question's labels; for a Noul, A is yes.
    private static Result<QuestionAnswer, DecisionError> Answer(QuestionDefinition question, double[] p, ProbabilitySource source)
    {
        if (question.Kind == QuestionKind.Noul)
        {
            return Result<QuestionAnswer, DecisionError>.Success(QuestionAnswer.Noul(p[0], source));
        }

        var (picked, confidence) = LabelDistribution.Pick(p);
        return Result<QuestionAnswer, DecisionError>.Success(question.Kind == QuestionKind.Choice
            ? QuestionAnswer.Choice(picked, confidence, p, source)
            : QuestionAnswer.Score(picked, LabelDistribution.Expected(p), confidence, p, source));
    }

    /// <summary>One question's answer, the model the first of its responses names, and the tokens all its calls used.</summary>
    private readonly record struct QuestionCall(QuestionAnswer Answer, string? Model, long? InputTokens, long? OutputTokens);
}
