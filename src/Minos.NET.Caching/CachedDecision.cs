namespace Minos.Caching;

/// <summary>
/// One cached answer: the fields a <see cref="DecisionResponse"/> is rebuilt from, without its definition, which the
/// cache key already covers.
/// </summary>
/// <remarks>
/// The in-process cache keeps the entry itself, so a hit there reads nothing. The response built for the first definition
/// instance is kept and returned again for that same instance; an equal definition in another instance gets a response of
/// its own, since the typed calls check the definition's identity. An entry the format could not read is
/// <see cref="Unreadable"/> and builds nothing.
/// </remarks>
internal sealed class CachedDecision
{
    /// <summary>The layout version <see cref="CachedDecisionFormat"/> writes and reads.</summary>
    public const int Version = 1;

    /// <summary>The entry for a stored value the format could not read: an unknown version, or corrupt bytes.</summary>
    public static readonly CachedDecision Unreadable = new([], [], null, null, null, null, null, null);

    private DecisionResponse? _response;

    public CachedDecision(
        AnswerSlot[] slots, double[] probabilities, string? model, int? inputTokens, int? outputTokens, double? cost, string? id, string? upstreamProvider)
    {
        Slots = slots;
        Probabilities = probabilities;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Cost = cost;
        Id = id;
        UpstreamProvider = upstreamProvider;
    }

    public AnswerSlot[] Slots { get; }

    public double[] Probabilities { get; }

    public string? Model { get; }

    public int? InputTokens { get; }

    public int? OutputTokens { get; }

    public double? Cost { get; }

    public string? Id { get; }

    public string? UpstreamProvider { get; }

    /// <summary>The entry for a response that was just stored: it keeps <paramref name="cached"/> for its definition.</summary>
    /// <param name="cached">A response made with <see cref="DecisionResponse.AsCached"/>.</param>
    /// <returns>The entry.</returns>
    public static CachedDecision From(DecisionResponse cached)
        => new(cached.Slots, cached.Probabilities, cached.Model, cached.InputTokens, cached.OutputTokens, cached.Cost, cached.Id, cached.UpstreamProvider)
        {
            _response = cached,
        };

    /// <summary>The cached response for <paramref name="definition"/>, or <see langword="null"/> when this entry does not fit it.</summary>
    /// <param name="definition">The request's definition.</param>
    /// <returns>The response, or <see langword="null"/>.</returns>
    /// <remarks>The first response built is kept, so the next call for the same definition instance returns it.</remarks>
    public DecisionResponse? ResponseFor(QuestionSetDefinition definition)
    {
        var kept = Volatile.Read(ref _response);
        if (kept is not null && ReferenceEquals(kept.Definition, definition))
        {
            return kept;
        }

        var built = Fits(definition)
            ? new DecisionResponse(definition, Slots, Probabilities, Model, InputTokens, OutputTokens, Cost, Id, UpstreamProvider, isCached: true)
            : null;
        if (kept is null && built is not null)
        {
            // Two threads may race on the first build: one keeps its response, the other returns its own.
            Interlocked.CompareExchange(ref _response, built, null);
        }

        return built;
    }

    // Whether the slots and probabilities have the shape the definition's questions give them. The key covers the
    // definition, so a mismatch means a corrupt entry or a hash collision: either way, a miss.
    private bool Fits(QuestionSetDefinition definition)
    {
        var questions = definition.QuestionArray;
        if (ReferenceEquals(this, Unreadable) || Slots.Length != questions.Length || Probabilities.Length != definition.ProbabilityCount)
        {
            return false;
        }

        for (var i = 0; i < questions.Length; i++)
        {
            var slot = Slots[i];
            if (questions[i].Kind == QuestionKind.Noul)
            {
                continue;
            }

            if (slot.Offset != definition.Offsets[i] || (uint)slot.ValueIndex >= (uint)questions[i].OptionArray.Length)
            {
                return false;
            }
        }

        return true;
    }
}
