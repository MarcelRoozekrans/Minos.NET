using System.Buffers;
using System.Text.Json;
using Minos.Generator;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.OpenAI.Protocols;

/// <summary>OpenAI's <c>/v1/decisions</c> wire format.</summary>
internal sealed class OpenAIDecisionsProtocol : IDecisionProtocol
{
    private const int CacheSlot = 1;

    // Slots for this many questions live on the stack; larger sets allocate them, as System One does.
    private const int MaxStackSlots = 64;

    // Flags are one byte each, so they stay on the stack for more questions than slots do.
    private const int MaxStackFlags = 256;

    private const byte Found = 1;
    private const byte Refused = 2;

    public static OpenAIDecisionsProtocol Instance { get; } = new();

    private OpenAIDecisionsProtocol()
    {
    }

    /// <summary>Gets the <c>questions</c> array for <paramref name="definition"/>, written once and cached on it.</summary>
    public static ReadOnlySpan<byte> QuestionsJson(QuestionSetDefinition definition)
        => definition.GetOrAddProtocolData(CacheSlot, static d => OpenAIQuestionsWriter.Write(d));

    public RawJson WriteRequest<TArg>(
        QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
        IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct
        => OpenAIRequestWriter.Write(QuestionsJson(definition), state, stateSizeHint, writeState, images, model, pool);

    // A predicate takes only instructions and a description is one string, so Noul criteria (MIN204) and a criterion's
    // Examples or NotFor (MIN205) cannot be sent. Nothing is dropped silently; each question fails once per rule.
    public void CollectUnsupported(QuestionSetDefinition definition, string providerName, ref List<QuestionFailure>? failures)
    {
        foreach (var question in definition.QuestionArray)
        {
            if (question.Kind == QuestionKind.Noul)
            {
                if (question.WhenTrue is not null || question.WhenFalse is not null)
                {
                    (failures ??= []).Add(new QuestionFailure(
                        DiagnosticIds.NoulCriteriaUnsupported,
                        question.Key,
                        providerName + " does not support WhenTrue or WhenFalse on a Noul; put the meaning in the instructions."));
                }
            }
            else if (HasStructuredCriterion(question))
            {
                (failures ??= []).Add(new QuestionFailure(
                    DiagnosticIds.CriterionDetailUnsupported,
                    question.Key,
                    providerName + " does not support Examples or NotFor on a criterion; describe them in its text."));
            }
        }
    }

    public Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition)
        => OpenAIResponseReader.Read(this, body, definition);

    public DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter, string? requestId)
        => OpenAIErrors.Map(statusCode, body, bodyTruncated, contentType, retryAfter, requestId);

    /// <inheritdoc />
    /// <remarks>
    /// Each element of the <c>answers</c> array is matched to its question by <c>name</c>, which may come before or after
    /// <c>type</c>; an answer for a name the set does not have is skipped, and a second answer or refusal for a question
    /// is an error.
    /// </remarks>
    /// <exception cref="RefusalException">The model refused at least one question; checked before any missing answer.</exception>
    public TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create)
    {
        AnswerReading.EnsureStartArray(ref answers);
        var questions = definition.QuestionArray;
        var count = questions.Length;
        var probabilities = definition.ProbabilityCount == 0 ? [] : new double[definition.ProbabilityCount];
        var heapSlots = count <= MaxStackSlots ? null : new AnswerSlot[count];
        Span<AnswerSlot> slots = heapSlots ?? stackalloc AnswerSlot[count];

        // Whether each question has been answered or refused.
        Span<byte> flags = count <= MaxStackFlags ? stackalloc byte[count] : new byte[count];
        flags.Clear();
        List<string>? refused = null;

        while (AnswerReading.NextElement(ref answers))
        {
            var (index, type) = OpenAIAnswers.Probe(answers, definition.KeysUtf8);
            if (index < 0)
            {
                answers.Skip();
                continue;
            }

            // One answer or refusal per question: a second one would silently replace the first.
            if (flags[index] != 0)
            {
                throw new JsonException($"The response has more than one answer for '{questions[index].Key}'.");
            }

            if (type == OpenAIAnswers.AnswerType.Refusal)
            {
                flags[index] = Refused;
                (refused ??= []).Add(questions[index].Key);
                answers.Skip();
                continue;
            }

            var question = questions[index];
            OpenAIAnswers.EnsureType(type, question.Kind);
            var offset = definition.Offsets[index];
            switch (question.Kind)
            {
                case QuestionKind.Noul:
                    slots[index] = new AnswerSlot(0, OpenAIAnswers.ReadPredicate(ref answers), 0, 0);
                    break;
                case QuestionKind.Choice:
                    var (choice, confidence) = OpenAIAnswers.ReadChoice(ref answers, definition.OptionKeysUtf8[index], probabilities, offset);
                    slots[index] = new AnswerSlot(choice, 0, confidence, offset);
                    break;
                default:
                    var (level, expected, scoreConfidence) = OpenAIAnswers.ReadScore(ref answers, question.OptionArray.Length, probabilities, offset);
                    slots[index] = new AnswerSlot(level, expected, scoreConfidence, offset);
                    break;
            }

            flags[index] = Found;
        }

        // A refused question has no answer, so a refusal is reported before the missing-answer check.
        if (refused is not null)
        {
            throw new RefusalException(refused);
        }

        for (var i = 0; i < count; i++)
        {
            if ((flags[i] & Found) == 0)
            {
                throw AnswerReading.MissingAnswer(questions[i].Key);
            }
        }

        return create(new AnswerSlots(slots, probabilities, definition, heapSlots));
    }

    private static bool HasStructuredCriterion(QuestionDefinition question)
    {
        foreach (var option in question.OptionArray)
        {
            if (option.Criterion is { } criterion && (criterion.Examples.Count > 0 || criterion.NotFor.Count > 0))
            {
                return true;
            }
        }

        return false;
    }
}
