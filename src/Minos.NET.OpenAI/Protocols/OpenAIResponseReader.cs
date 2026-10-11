using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.OpenAI.Protocols;

/// <summary>Reads a <c>/v1/decisions</c> body into a <see cref="DecisionResponse"/> in one pass.</summary>
/// <remarks>
/// Fails as the System One reader does: status 200 and <see cref="DecisionErrorKind.InvalidResponse"/>, or
/// <see cref="DecisionErrorKind.Refused"/> when the model refused a question. <c>model</c>, <c>id</c> and <c>usage</c>
/// are optional and read leniently: a field of the wrong type is treated as absent, never as an error. The first
/// occurrence of a field decides it, even when that occurrence has the wrong type; a repeat is skipped, and so is a
/// second <c>usage</c> object. The guide documents no usage object, so its token counts are read only when present.
/// </remarks>
internal static class OpenAIResponseReader
{
    private static readonly AnswerFactory<(AnswerSlot[] Slots, double[] Probabilities)> Capture
        = static slots => (slots.HeapSlots ?? slots.Slots.ToArray(), slots.Probabilities);

    // The last model read, shared by the whole process: a client meets few distinct values, so a value that matches the
    // last one is returned as that same string instead of a new one per response. A race only replaces the cached value.
    private static string? lastModel;

    // Utf8JsonReader treats a leading UTF-8 BOM as an invalid start of a value; a body read here tolerates one.
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public static Result<DecisionResponse, DecisionError> Read(OpenAIDecisionsProtocol protocol, ReadOnlySpan<byte> body, QuestionSetDefinition definition)
    {
        var reader = new Utf8JsonReader(body.StartsWith(Utf8Bom) ? body[Utf8Bom.Length..] : body);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Invalid("The response body is not a JSON object.");
            }

            (AnswerSlot[] Slots, double[] Probabilities)? answers = null;
            string? model = null, id = null;
            bool modelSeen = false, idSeen = false, usageSeen = false;
            int? inputTokens = null, outputTokens = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("answers"u8))
                {
                    reader.Read();
                    if (answers is not null)
                    {
                        return Invalid("The response has more than one answers property.");
                    }

                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        return Invalid("The response's answers are null.");
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
                        model = CachedStringOrSkip(ref reader, ref lastModel);
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
                        id = StringOrSkip(ref reader);
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
                        ReadUsage(ref reader, ref inputTokens, ref outputTokens);
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
                    new DecisionResponse(definition, read.Slots, read.Probabilities, model, inputTokens, outputTokens, cost: null, id, provider: null))
                : Invalid("The response has no answers.");
        }
        catch (RefusalException refusal)
        {
            return Result<DecisionResponse, DecisionError>.Failure(Refused(refusal.Keys));
        }
        catch (JsonException exception)
        {
            return Result<DecisionResponse, DecisionError>.Failure(new DecisionError(
                DecisionErrorKind.InvalidResponse,
                "The response could not be read as the question set's answers: " + exception.Message)
            {
                StatusCode = 200,
                Exception = exception,
            });
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

    private static string? CachedStringOrSkip(ref Utf8JsonReader reader, ref string? cache)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        var cached = Volatile.Read(ref cache);
        if (cached is not null && reader.ValueTextEquals(cached))
        {
            return cached;
        }

        var value = reader.GetString();
        Volatile.Write(ref cache, value);
        return value;
    }

    private static string? StringOrSkip(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();
        return null;
    }

    private static void ReadUsage(ref Utf8JsonReader reader, ref int? inputTokens, ref int? outputTokens)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return;
        }

        bool inputSeen = false, outputSeen = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var field = reader.ValueTextEquals("input_tokens"u8) ? 1 : reader.ValueTextEquals("output_tokens"u8) ? 2 : 0;
            reader.Read();
            switch (field)
            {
                case 1 when !inputSeen:
                    inputSeen = true;
                    inputTokens = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var input) ? input : null;
                    break;
                case 2 when !outputSeen:
                    outputSeen = true;
                    outputTokens = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var output) ? output : null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private static Result<DecisionResponse, DecisionError> Invalid(string message)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.InvalidResponse, message) { StatusCode = 200 });
}
