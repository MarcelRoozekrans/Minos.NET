using System.Buffers;
using System.Text.Json;

namespace Minos.Transport;

/// <summary>
/// Checks a caller's state and hands it, with a writer for its JSON value, to a protocol's
/// <see cref="IDecisionProtocol.WriteRequest{TArg}"/>, which writes the request body around it. The state is written
/// straight into the protocol's pooled body; no request object is built.
/// </summary>
internal static class RequestStates
{
    // A guess for states whose size is unknown until they are written.
    private const int UnknownStateSize = 1024;

    /// <summary>Writes a request with a text state.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The text.</param>
    /// <param name="images">The request's images, possibly empty.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson WriteRequest(
        this IDecisionProtocol protocol, QuestionSetDefinition definition, string state, IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
        => protocol.WriteRequest<string>(definition, state, state.Length, static (writer, _, text, _) => writer.WriteStringValue(text), images, model, pool);

    /// <summary>Writes a request with a JSON state, which must be a string, object or array.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state.</param>
    /// <param name="images">The request's images, possibly empty.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    public static RawJson WriteRequest(
        this IDecisionProtocol protocol, QuestionSetDefinition definition, JsonElement state, IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
    {
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        return protocol.WriteRequest<JsonElement>(
            definition,
            state,
            UnknownStateSize,
            static (writer, _, element, asText) => WriteJsonElement(writer, element, asText),
            images,
            model,
            pool);
    }

    /// <summary>
    /// Writes a request with a <see cref="DecisionContent"/> state: a caller's UTF-8 JSON state as its bytes, text as a
    /// string, JSON as its value.
    /// </summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state, which must be initialized.</param>
    /// <param name="images">The request's images, possibly empty.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson WriteRequest(
        this IDecisionProtocol protocol, QuestionSetDefinition definition, DecisionContent state, IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
    {
        if (state.TryGetUtf8State(out var utf8))
        {
            return protocol.WriteUtf8Request(definition, utf8, images, model, pool);
        }

        if (state.TryGetString(out var text))
        {
            return protocol.WriteRequest(definition, text, images, model, pool);
        }

        state.TryGetJson(out var json);
        return protocol.WriteRequest(definition, json, images, model, pool);
    }

    // As text, a string element is its value and anything else its raw JSON text.
    private static void WriteJsonElement(Utf8JsonWriter writer, JsonElement element, bool asText)
    {
        if (!asText)
        {
            element.WriteTo(writer);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            writer.WriteStringValue(element.GetString());
        }
        else
        {
            writer.WriteStringValue(element.GetRawText());
        }
    }

    // Copies the caller's UTF-8 JSON into the body byte for byte, so its whitespace and escapes are sent as written. The
    // content's creator checked it with TypedEvaluation.EnsureStateJson; the writer validates it again as it copies.
    private static RawJson WriteUtf8Request(
        this IDecisionProtocol protocol, QuestionSetDefinition definition, ReadOnlySpan<byte> utf8JsonState,
        IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
        => protocol.WriteRequest<ReadOnlySpan<byte>>(
            definition,
            utf8JsonState,
            utf8JsonState.Length,
            static (writer, _, utf8, asText) => WriteUtf8(writer, utf8, asText),
            images,
            model,
            pool);

    // As text, a JSON string is its decoded value and anything else the raw JSON as text.
    private static void WriteUtf8(Utf8JsonWriter writer, ReadOnlySpan<byte> utf8, bool asText)
    {
        if (!asText)
        {
            writer.WriteRawValue(utf8, skipInputValidation: false);
            return;
        }

        var reader = new Utf8JsonReader(utf8);
        if (reader.Read() && reader.TokenType == JsonTokenType.String)
        {
            if (!reader.ValueIsEscaped)
            {
                writer.WriteStringValue(reader.ValueSpan);
            }
            else
            {
                writer.WriteStringValue(reader.GetString());
            }

            return;
        }

        writer.WriteStringValue(utf8);
    }
}
