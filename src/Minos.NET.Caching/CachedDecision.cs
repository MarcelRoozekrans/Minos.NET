using System.ComponentModel;

namespace Minos.Caching;

/// <summary>
/// One cached answer: the fields a <see cref="DecisionResponse"/> is rebuilt from, without its definition, which the
/// cache key already covers.
/// </summary>
/// <remarks>
/// HybridCache keeps a sealed type marked <see cref="ImmutableObjectAttribute"/> in its in-process cache as the instance
/// itself, so an L1 hit deserializes nothing. The response built for the first definition instance is kept and returned
/// again for that same instance; an equal definition in another instance gets a response of its own, since the typed
/// calls check the definition's identity. An entry the serializer could not read is <see cref="Unreadable"/> and builds
/// nothing.
/// </remarks>
[ImmutableObject(true)]
internal sealed class CachedDecision
{
    /// <summary>The layout version the serializer writes and reads.</summary>
    public const int Version = 1;

    /// <summary>The entry for a stored value the serializer could not read: an unknown version, or corrupt bytes.</summary>
    public static readonly CachedDecision Unreadable = new([], [], null, null, null, null, null, null);

    private DecisionResponse? _response;

    public CachedDecision(
        AnswerSlot[] slots, double[] probabilities, string? model, int? inputTokens, int? outputTokens, double? cost, string? id, string? provider)
    {
        Slots = slots;
        Probabilities = probabilities;
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Cost = cost;
        Id = id;
        Provider = provider;
    }

    public AnswerSlot[] Slots { get; }

    public double[] Probabilities { get; }

    public string? Model { get; }

    public int? InputTokens { get; }

    public int? OutputTokens { get; }

    public double? Cost { get; }

    public string? Id { get; }

    public string? Provider { get; }

    /// <summary>The entry for a response that was just stored: it keeps <paramref name="cached"/> for its definition.</summary>
    /// <param name="cached">A response made with <see cref="DecisionResponse.AsCached"/>.</param>
    /// <returns>The entry.</returns>
    public static CachedDecision From(DecisionResponse cached)
        => new(cached.Slots, cached.Probabilities, cached.Model, cached.InputTokens, cached.OutputTokens, cached.Cost, cached.Id, cached.Provider)
        {
            _response = cached,
        };

    /// <summary>The cached response for <paramref name="definition"/>, or <see langword="null"/> when this entry does not fit it.</summary>
    /// <param name="definition">The request's definition.</param>
    /// <param name="firstBuild"><see langword="true"/> when this call built the entry's first response, so a read from L2 can be promoted to L1.</param>
    /// <returns>The response, or <see langword="null"/>.</returns>
    public DecisionResponse? ResponseFor(QuestionSetDefinition definition, out bool firstBuild)
    {
        var kept = Volatile.Read(ref _response);
        if (kept is not null && ReferenceEquals(kept.Definition, definition))
        {
            firstBuild = false;
            return kept;
        }

        var built = Fits(definition)
            ? new DecisionResponse(definition, Slots, Probabilities, Model, InputTokens, OutputTokens, Cost, Id, Provider, isCached: true)
            : null;
        firstBuild = kept is null && built is not null && Interlocked.CompareExchange(ref _response, built, null) is null;
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
