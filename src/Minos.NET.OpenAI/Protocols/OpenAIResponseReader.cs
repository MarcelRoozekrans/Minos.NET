using System.Text.Json;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.OpenAI.Protocols;

/// <summary>Reads a <c>/v1/decisions</c> body into a <see cref="DecisionResponse"/> in one pass.</summary>
/// <remarks>
/// Fails as the System One reader does: status 200 and <see cref="DecisionErrorKind.InvalidResponse"/>, or
/// <see cref="DecisionErrorKind.Refused"/> when the model refused a question. <c>model</c>, <c>id</c> and <c>usage</c>
/// are optional and read leniently: a field of the wrong type is treated as absent, never as an error. The first
/// occurrence of a field decides it, even when that occurrence has the wrong type; a repeat is skipped, and so is a
/// second <c>usage</c> object. The guide documents no usage object, so its token counts and cost are read only when present.
/// </remarks>
internal static class OpenAIResponseReader
{
    private static readonly AnswerFactory<(AnswerSlot[] Slots, double[] Probabilities)> Capture
        = static slots => (slots.HeapSlots ?? slots.Slots.ToArray(), slots.Probabilities);

    // The last model read, shared by the whole process: a client meets few distinct values, so a value that matches the
    // last one is returned as that same string instead of a new one per response. A race only replaces the cached value.
    private static string? lastModel;

    public static Result<DecisionResponse, DecisionError> Read(OpenAIDecisionsProtocol protocol, ReadOnlySpan<byte> body, QuestionSetDefinition definition)
    {
        var reader = new Utf8JsonReader(ResponseReading.SkipUtf8Bom(body));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return ResponseReading.Invalid("The response body is not a JSON object.");
            }

            (AnswerSlot[] Slots, double[] Probabilities)? answers = null;
            string? model = null, id = null;
            bool modelSeen = false, idSeen = false, usageSeen = false;
            int? inputTokens = null, outputTokens = null;
            double? cost = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("answers"u8))
                {
                    reader.Read();
                    if (answers is not null)
                    {
                        return ResponseReading.Invalid("The response has more than one answers property.");
                    }

                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        return ResponseReading.Invalid("The response's answers are null.");
                    }

                    answers = protocol.ReadAnswers(ref reader, definition, Capture);
                }
                else if (reader.ValueTextEquals("model"u8))
                {
                    reader.Read();
                    if (modelSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        modelSeen = true;
                        model = ResponseReading.CachedStringOrSkip(ref reader, ref lastModel);
                    }
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    if (idSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        idSeen = true;
                        id = ResponseReading.StringOrSkip(ref reader);
                    }
                }
                else if (reader.ValueTextEquals("usage"u8))
                {
                    reader.Read();
                    if (usageSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        usageSeen = true;
                        ResponseReading.ReadUsage(ref reader, ref inputTokens, ref outputTokens, ref cost);
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            // Reads past the root object, so trailing content after it is reported.
            reader.Read();

            return answers is { } read
                ? Result<DecisionResponse, DecisionError>.Success(
                    new DecisionResponse(definition, read.Slots, read.Probabilities, model, inputTokens, outputTokens, cost, id, provider: null))
                : ResponseReading.Invalid("The response has no answers.");
        }
        catch (RefusalException refusal)
        {
            return Result<DecisionResponse, DecisionError>.Failure(Refused(refusal.Keys));
        }
        catch (JsonException exception)
        {
            return ResponseReading.Unreadable(exception);
        }
    }

    // One failure per refused question, rule "refusal", in answer order; the other answers are discarded.
    private static DecisionError Refused(IReadOnlyList<string> keys)
    {
        var failures = new QuestionFailure[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            failures[i] = new QuestionFailure("refusal", keys[i], "The model refused to answer '" + keys[i] + "'.");
        }

        return new DecisionError(DecisionErrorKind.Refused, "The model refused to answer: " + string.Join(", ", keys) + ".", failures)
        {
            StatusCode = 200,
        };
    }
}
