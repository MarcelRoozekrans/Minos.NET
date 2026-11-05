using Minos.Telemetry;

namespace Minos;

/// <summary>A provider-neutral evaluation result: the answers in definition order, and what the provider reported about the call.</summary>
public sealed class DecisionResponse
{
    // Set by the public constructor; a read response builds it on first access from the token counts it read.
    private DecisionUsage? _usage;

    // One boxed true for every cached response, so the telemetry tag allocates nothing per hit.
    private static readonly object BoxedTrue = true;

    private readonly DecisionPath? _path;

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
                slots[i] = new AnswerSlot(0, answer.Probability, 0, 0, answer.ProbabilitySource);
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
            slots[i] = new AnswerSlot(answer.ChosenIndex, answer.Expected, answer.Confidence, offset, answer.ProbabilitySource);
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

    /// <summary>Gets how the probabilities were made: the weakest source among the answers, or <see cref="Minos.ProbabilitySource.Model"/> when there are none.</summary>
    public ProbabilitySource ProbabilitySource
    {
        get
        {
            var weakest = ProbabilitySource.Model;
            foreach (var slot in Slots)
            {
                if (slot.Source > weakest)
                {
                    weakest = slot.Source;
                }
            }

            return weakest;
        }
    }

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

    /// <summary>
    /// Gets how a routing stage, such as escalation, answered the request: per question the client that answered and
    /// why, and each call it made. <see langword="null"/> when no routing stage ran. A custom routing stage sets it with
    /// an object initializer.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// On set: the path does not have one route per question, or a route's key is not its question's key, in definition order.
    /// </exception>
    public DecisionPath? Path
    {
        get => _path;
        init
        {
            if (value is not null)
            {
                EnsureRoutesMatch(value);
            }

            _path = value;
        }
    }

    internal AnswerSlot[] Slots { get; }

    internal double[] Probabilities { get; }

    // Read field by field, as the provider sent them, for telemetry: one count can be present without the other.
    internal int? InputTokens { get; }

    internal int? OutputTokens { get; }

    internal double? Cost { get; }

    internal SlotConfidences Confidences => new(this);

    // The minos.cache.hit tag's value: set on a cached response, absent otherwise, so an uncached span carries no tag.
    internal object? CacheHitTag => IsCached ? BoxedTrue : null;

    // The escalation stage's telemetry, read by the telemetry stage. A null tag is absent from the span and the counter.
    // The values live in one holder on the path, which the stage allocates only when a question was unsure, so a response
    // carries no field for them. They are computed getters, as CacheHitTag is.
    // minos.escalation.escalated: the number of questions the target answered.
    internal object? EscalatedTag => Escalation?.EscalatedTag;

    // minos.escalation.skipped: "no_time_left" or "target_failed".
    internal string? SkippedTag => Escalation?.SkippedTag;

    // minos.escalations: 1 for a call whose primary answer had an unsure question.
    internal int? EscalationEvents => Escalation is null ? null : 1;

    // minos.escalation.target and minos.escalation.reason: the counter's tags.
    internal string? EscalationTargetTag => Escalation?.TargetTag;

    internal string? EscalationReasonTag => Escalation?.ReasonTag;

    // The holder is reported for the response the stage built alone: a copy that shares the path, such as AsCached's or a
    // custom stage's, is not an escalation and reports none.
    private EscalationTelemetry? Escalation
        => _path?.Escalation is { } escalation && ReferenceEquals(escalation.Owner, this) ? escalation : null;

    /// <summary>A copy that a cache stores: the same answers, model, usage, id, upstream provider and path, marked as cached. A cache hit is not an escalation, so the copy reports no escalation telemetry: that is reported for the response the stage built alone.</summary>
    /// <returns>A new response sharing this one's arrays and path.</returns>
    internal DecisionResponse AsCached()
        => new(Definition, Slots, Probabilities, Model, InputTokens, OutputTokens, Cost, Id, UpstreamProvider, isCached: true) { Path = Path };

    /// <summary>Creates the typed answers from this response, as the typed <c>EvaluateAsync&lt;T&gt;</c> calls do.</summary>
    /// <typeparam name="T">The <c>[Questions]</c> type whose <see cref="IQuestionSet{TSelf}.Definition"/> this response answers.</typeparam>
    /// <returns>The typed answers.</returns>
    /// <remarks>
    /// Lets a typed caller read the envelope as well as the answers: send <c>new DecisionRequest(T.Definition, state)</c>
    /// to <see cref="IDecisionClient.EvaluateAsync"/>, then read <see cref="Path"/>, <see cref="Usage"/> or
    /// <see cref="Model"/> from the response and call this for the answers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Definition"/> is not the same instance as <typeparamref name="T"/>'s definition; definitions compare by reference.
    /// </exception>
    public T As<T>()
        where T : IQuestionSet<T>
    {
        EnsureDefinition(this, T.Definition);
        return T.Create(ToAnswerSlots());
    }

    // A pipeline returns the response for the request's own definition instance; a fake that answers another set is a
    // bug in the fake. QuestionSetDefinition has reference identity, so this is a reference check.
    internal static void EnsureDefinition(DecisionResponse response, QuestionSetDefinition expected)
    {
        if (!ReferenceEquals(response.Definition, expected))
        {
            throw new InvalidOperationException("The client returned answers for a different question set than the one it was asked.");
        }
    }

    // The message names positions only: question keys are state that an exception message does not carry.
    private void EnsureRoutesMatch(DecisionPath value)
    {
        var questions = Definition.QuestionArray;
        var routes = value.Routes;
        if (routes.Count != questions.Length)
        {
            throw new ArgumentException($"The definition has {questions.Length} questions, not {routes.Count} routes.", nameof(value));
        }

        for (var i = 0; i < questions.Length; i++)
        {
            if (!string.Equals(routes[i].Key, questions[i].Key, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Route {i} is for a different question than question {i}.", nameof(value));
            }
        }
    }

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
            ? new QuestionAnswer(question.Key, QuestionKind.Noul, -1, slot.Value, 0, null, 0, 0, slot.Source)
            : new QuestionAnswer(question.Key, question.Kind, slot.ValueIndex, question.Kind == QuestionKind.Choice ? 0 : slot.Value, slot.Confidence, Probabilities, slot.Offset, question.OptionArray.Length, slot.Source);
    }
}
