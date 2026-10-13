using Minos.Telemetry;

namespace Minos;

/// <summary>A provider-neutral evaluation result: the answers in definition order, and what the provider reported about the call.</summary>
public sealed class DecisionResponse
{
    // Set by the public constructor; a read response builds it on first access from the token counts it read.
    private DecisionUsage? _usage;

    // One boxed true for every cached response, so the telemetry tag allocates nothing per hit.
    private static readonly object BoxedTrue = true;

    /// <summary>Initializes a new instance of the <see cref="DecisionResponse"/> class from answers, such as for a test fake.</summary>
    /// <param name="definition">The questions the answers belong to.</param>
    /// <param name="answers">One answer per question, in definition order, made with the <see cref="QuestionAnswer"/> factories.</param>
    /// <param name="model">The model that answered, if known.</param>
    /// <param name="usage">The token usage, if known; one that reports no count and no cost is treated as <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> or <paramref name="answers"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Checked: one answer for every question, none of them a <see langword="default"/> value, and per answer its kind, its probability count against the question's options, and a chosen position below the option count. The factories check the ranges of the confidence and the probabilities, the range of a Noul's <see cref="QuestionAnswer.Probability"/> and that a Score's <see cref="QuestionAnswer.Expected"/> is finite. Not checked: a Score's <see cref="QuestionAnswer.Expected"/> against its levels, and that the probabilities sum to 1 or that the chosen position is the most probable.
    /// </remarks>
    /// <exception cref="ArgumentException">An answer is a default value, or the answers do not match the definition's questions in number, kind, option count or chosen position.</exception>
    public DecisionResponse(QuestionSetDefinition definition, IReadOnlyList<QuestionAnswer> answers, string? model = null, DecisionUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(answers);
        var questions = definition.QuestionArray;
        if (answers.Count != questions.Length)
        {
            throw new ArgumentException($"The definition has {questions.Length} questions, not {answers.Count} answers.", nameof(answers));
        }

        var probabilities = definition.ProbabilityCount == 0 ? [] : new double[definition.ProbabilityCount];
        var slots = new AnswerSlot[questions.Length];
        for (var i = 0; i < questions.Length; i++)
        {
            var answer = answers[i];
            if (answer.IsDefault)
            {
                throw new ArgumentException($"Answer {i} is a default value; make answers with the QuestionAnswer factories.", nameof(answers));
            }

            var question = questions[i];
            if (answer.Kind != question.Kind)
            {
                throw new ArgumentException($"Question {i} ({question.Key}) is a {question.Kind}, not a {answer.Kind}.", nameof(answers));
            }

            if (question.Kind == QuestionKind.Noul)
            {
                slots[i] = new AnswerSlot(0, answer.Probability, 0, 0);
                continue;
            }

            var options = question.OptionArray.Length;
            if (answer.Probabilities.Length != options)
            {
                throw new ArgumentException($"Question {i} ({question.Key}) has {options} options, not {answer.Probabilities.Length} probabilities.", nameof(answers));
            }

            if (answer.ChosenIndex >= options)
            {
                throw new ArgumentException($"Question {i} ({question.Key}) has {options} options; {answer.ChosenIndex} is not one of them.", nameof(answers));
            }

            var offset = definition.Offsets[i];
            answer.Probabilities.CopyTo(probabilities.AsSpan(offset));
            slots[i] = new AnswerSlot(answer.ChosenIndex, answer.Expected, answer.Confidence, offset);
        }

        Definition = definition;
        Slots = slots;
        Probabilities = probabilities;
        Model = model;
        // A usage that reports nothing is no usage, so Usage is null for it as it is after a cache round trip.
        _usage = usage is { InputTokens: null, OutputTokens: null, Cost: null } ? null : usage;
        InputTokens = _usage?.InputTokens;
        OutputTokens = _usage?.OutputTokens;
        Cost = _usage?.Cost;
    }

    internal DecisionResponse(
        QuestionSetDefinition definition,
        AnswerSlot[] slots,
        double[] probabilities,
        string? model,
        int? inputTokens,
        int? outputTokens,
        double? cost,
        string? id,
        string? upstreamProvider,
        bool isCached = false)
    {
        Definition = definition;
        Slots = slots;
        Probabilities = probabilities;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Cost = cost;
        Id = id;
        UpstreamProvider = upstreamProvider;
        IsCached = isCached;
    }

    /// <summary>Gets the questions the answers belong to.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>Gets the answers, one per question in definition order.</summary>
    public QuestionAnswerList Answers => new(this);

    /// <summary>Gets the model that answered, or <see langword="null"/> when the provider did not say.</summary>
    public string? Model { get; }

    /// <summary>
    /// Gets the token usage and cost the provider reported, or <see langword="null"/> when it reported none; a count the
    /// provider left out is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A read response creates it on first access, so a call that never reads it does not allocate it. Every access
    /// returns the same instance, even when threads race on the first one.
    /// </remarks>
    public DecisionUsage? Usage => _usage ?? CreateUsage();

    /// <summary>Gets the provider's response id, if any. A stage that rebuilds a response can set it with an object initializer.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets the upstream provider an aggregator routed the call to, such as the host OpenRouter picked; <see langword="null"/>
    /// when the provider is not an aggregator or did not say.
    /// </summary>
    public string? UpstreamProvider { get; init; }

    /// <summary>
    /// Gets a value indicating whether this response came from a cache stage, such as <c>UseCaching</c> in
    /// <c>Minos.NET.Caching</c>, rather than from the provider. A cached response reports the model, usage and cost of the
    /// call that first produced it. A custom cache stage sets it with an object initializer.
    /// </summary>
    public bool IsCached { get; init; }

    internal AnswerSlot[] Slots { get; }

    internal double[] Probabilities { get; }

    // Read field by field, as the provider sent them, for telemetry: one count can be present without the other.
    internal int? InputTokens { get; }

    internal int? OutputTokens { get; }

    internal double? Cost { get; }

    internal SlotConfidences Confidences => new(this);

    // The minos.cache.hit tag's value: set on a cached response, absent otherwise, so an uncached span carries no tag.
    internal object? CacheHitTag => IsCached ? BoxedTrue : null;

    /// <summary>A copy that a cache stores: the same answers, model, usage, id and upstream provider, marked as cached.</summary>
    /// <returns>A new response sharing this one's arrays.</returns>
    internal DecisionResponse AsCached()
        => new(Definition, Slots, Probabilities, Model, InputTokens, OutputTokens, Cost, Id, UpstreamProvider, isCached: true);

    // Publishes one instance: a thread that loses the race returns the winner's.
    private DecisionUsage? CreateUsage()
    {
        if (InputTokens is null && OutputTokens is null && Cost is null)
        {
            return null;
        }

        var usage = new DecisionUsage { InputTokens = InputTokens, OutputTokens = OutputTokens, Cost = Cost };
        return Interlocked.CompareExchange(ref _usage, usage, null) ?? usage;
    }

    internal AnswerSlots ToAnswerSlots() => new(Slots, Probabilities, Definition, Slots);

    internal QuestionAnswer AnswerAt(int index)
    {
        var question = Definition.QuestionArray[index];
        var slot = Slots[index];
        return question.Kind == QuestionKind.Noul
            ? new QuestionAnswer(question.Key, QuestionKind.Noul, -1, slot.Value, 0, null, 0, 0)
            : new QuestionAnswer(question.Key, question.Kind, slot.ValueIndex, question.Kind == QuestionKind.Choice ? 0 : slot.Value, slot.Confidence, Probabilities, slot.Offset, question.OptionArray.Length);
    }
}
