using System.Buffers;
using System.Text.Json;
using Minos.Serialization;
using Minos.Transport;

namespace Minos.Protocols;

/// <summary>The <c>/v1/systemone</c> wire format: TypeSafe, OpenRouter, Clef and compatible servers.</summary>
internal sealed class SystemOneProtocol : IDecisionProtocol
{
    private const int CacheSlot = 0;

    // The definition's cache slot this protocol owns; each protocol has one.
    private readonly int _cacheSlot;

    // Slots for this many questions live on the stack; larger sets rent nothing and allocate, as before.
    private const int MaxStackSlots = 64;

    public static SystemOneProtocol Instance { get; } = new();

    private SystemOneProtocol()
    {
        _cacheSlot = CacheSlot;
    }

    /// <summary>Gets the <c>questions</c> object for <paramref name="definition"/>, written once and cached on it.</summary>
    public ReadOnlySpan<byte> QuestionsUtf8(QuestionSetDefinition definition)
        => definition.GetOrAddProtocolData(_cacheSlot, static d => QuestionsWriter.Write(d));

    public RawJson WriteRequest<TArg>(QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct
        => TypedRequestWriter.Compose(QuestionsUtf8(definition), state, stateSizeHint, model, pool, writeState);

    public TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create)
    {
        SystemOneAnswers.EnsureStartObject(ref answers);
        var questions = definition.QuestionArray;
        var count = questions.Length;
        var probabilities = definition.ProbabilityCount == 0 ? [] : new double[definition.ProbabilityCount];
        Span<AnswerSlot> slots = count <= MaxStackSlots ? stackalloc AnswerSlot[count] : new AnswerSlot[count];
        Span<bool> found = count <= MaxStackSlots ? stackalloc bool[count] : new bool[count];

        while (SystemOneAnswers.NextProperty(ref answers))
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
                throw SystemOneAnswers.MissingAnswer(questions[i].Key);
            }
        }

        return create(new AnswerSlots(slots, probabilities, definition));
    }
}
