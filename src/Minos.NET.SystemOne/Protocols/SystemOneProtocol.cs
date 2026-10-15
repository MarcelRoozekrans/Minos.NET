using System.Buffers;
using System.Text.Json;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.SystemOne.Protocols;

/// <summary>The <c>/v1/systemone</c> wire format: TypeSafe, OpenRouter, vLLM Semantic Router and compatible servers.</summary>
internal sealed class SystemOneProtocol : IDecisionProtocol
{
    private const int CacheSlot = 0;

    // Slots for this many questions live on the stack; larger sets rent nothing and allocate, as before.
    private const int MaxStackSlots = 64;

    // Found flags are one byte each, so they stay on the stack for more questions than slots do.
    private const int MaxStackFlags = 256;

    // The client's ,"provider":{…} member, appended to every request body; empty for Instance.
    private readonly ReadOnlyMemory<byte> _providerMember;

    /// <summary>Gets the protocol for a client without provider preferences, shared by every such client.</summary>
    public static SystemOneProtocol Instance { get; } = new(ReadOnlyMemory<byte>.Empty);

    /// <summary>
    /// Creates a protocol whose request bodies end with <paramref name="providerMember"/>. The cached <c>questions</c> bytes
    /// live on each definition, so they are shared with <see cref="Instance"/>.
    /// </summary>
    internal SystemOneProtocol(ReadOnlyMemory<byte> providerMember)
    {
        _providerMember = providerMember;
    }

    /// <summary>Gets the protocol for a client's resolved <paramref name="providerMember"/>: <see cref="Instance"/> when it is empty.</summary>
    public static SystemOneProtocol For(ReadOnlyMemory<byte> providerMember)
        => providerMember.IsEmpty ? Instance : new SystemOneProtocol(providerMember);

    /// <summary>Gets the <c>questions</c> object for <paramref name="definition"/>, written once and cached on it.</summary>
    public static ReadOnlySpan<byte> QuestionsJson(QuestionSetDefinition definition)
        => definition.GetOrAddProtocolData(CacheSlot, static d => SystemOneQuestionsWriter.Write(d));

    public RawJson WriteRequest<TArg>(
        QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
        IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct
        => SystemOneRequestWriter.Write(QuestionsJson(definition), state, stateSizeHint, writeState, images, model, _providerMember.Span, pool);

    // System One has no limits of its own beyond the capability check.
    public void CollectUnsupported(QuestionSetDefinition definition, string providerName, ref List<QuestionFailure>? failures)
    {
    }

    public Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition)
        => SystemOneResponseReader.Read(this, body, definition);

    public DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter, string? requestId)
        => SystemOneErrors.Map(statusCode, body, bodyTruncated, contentType, retryAfter, requestId);

    public TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create)
    {
        AnswerReading.EnsureStartObject(ref answers);
        var questions = definition.QuestionArray;
        var count = questions.Length;
        var probabilities = definition.ProbabilityCount == 0 ? [] : new double[definition.ProbabilityCount];
        var heapSlots = count <= MaxStackSlots ? null : new AnswerSlot[count];
        Span<AnswerSlot> slots = heapSlots ?? stackalloc AnswerSlot[count];
        Span<bool> found = count <= MaxStackFlags ? stackalloc bool[count] : new bool[count];

        while (AnswerReading.NextProperty(ref answers))
        {
            var index = Utf8Keys.IndexOf(ref answers, definition.KeysUtf8);
            if (index < 0)
            {
                answers.Skip();
                continue;
            }

            answers.Read();
            var offset = definition.Offsets[index];
            var keys = definition.OptionKeysUtf8[index];
            switch (questions[index].Kind)
            {
                case QuestionKind.Noul:
                    slots[index] = new AnswerSlot(0, SystemOneAnswers.ReadNoul(ref answers), 0, 0);
                    break;
                case QuestionKind.Choice:
                    var (choice, confidence) = SystemOneAnswers.ReadChoice(ref answers, keys, probabilities, offset);
                    slots[index] = new AnswerSlot(choice, 0, confidence, offset);
                    break;
                default:
                    var (level, expected, scoreConfidence) = SystemOneAnswers.ReadScore(ref answers, keys, probabilities, offset);
                    slots[index] = new AnswerSlot(level, expected, scoreConfidence, offset);
                    break;
            }

            found[index] = true;
        }

        for (var i = 0; i < count; i++)
        {
            if (!found[i])
            {
                throw AnswerReading.MissingAnswer(questions[i].Key);
            }
        }

        return create(new AnswerSlots(slots, probabilities, definition, heapSlots));
    }
}
