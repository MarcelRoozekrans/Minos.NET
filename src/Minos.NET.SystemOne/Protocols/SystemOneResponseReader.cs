using System.Text.Json;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.SystemOne.Protocols;

/// <summary>Reads a <c>/v1/systemone</c> body into a <see cref="DecisionResponse"/> in one pass.</summary>
/// <remarks>
/// Fails exactly as the typed path did: the same messages, status 200, and <see cref="DecisionErrorKind.InvalidResponse"/>.
/// <c>model</c>, <c>usage</c>, <c>id</c> and <c>provider</c> are optional and read leniently: a field of the wrong type is
/// treated as absent, never as an error, as telemetry has always read them. The first occurrence of a field decides it, even when that occurrence has the wrong type; a repeat is skipped, and so is a second <c>usage</c> object.
/// </remarks>
internal static class SystemOneResponseReader
{
    private static readonly AnswerFactory<(AnswerSlot[] Slots, double[] Probabilities)> Capture
        = static slots => (slots.HeapSlots ?? slots.Slots.ToArray(), slots.Probabilities);

    // The last model and provider read, shared by the whole process: an allocation cut that keeps the typed call within
    // its AOT gate. A client meets few distinct values, so a value that matches the last one is returned as that same
    // string instead of a new one per response. A race only replaces the cached value; any string returned equals the
    // bytes read.
    private static string? lastModel;
    private static string? lastProvider;

    public static Result<DecisionResponse, DecisionError> Read(SystemOneProtocol protocol, ReadOnlySpan<byte> body, QuestionSetDefinition definition)
    {
        var reader = new Utf8JsonReader(ResponseReading.SkipUtf8Bom(body));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return ResponseReading.Invalid("The response body is not a JSON object.");
            }

            (AnswerSlot[] Slots, double[] Probabilities)? answers = null;
            string? model = null, id = null, provider = null;
            bool modelSeen = false, idSeen = false, providerSeen = false, usageSeen = false;
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
                else if (reader.ValueTextEquals("provider"u8))
                {
                    reader.Read();
                    if (providerSeen)
                    {
                        reader.Skip();
                    }
                    else
                    {
                        providerSeen = true;
                        provider = ResponseReading.CachedStringOrSkip(ref reader, ref lastProvider);
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
                    new DecisionResponse(definition, read.Slots, read.Probabilities, model, inputTokens, outputTokens, cost, id, provider))
                : ResponseReading.Invalid("The response has no answers.");
        }
        catch (JsonException exception)
        {
            return ResponseReading.Unreadable(exception);
        }
    }
}
