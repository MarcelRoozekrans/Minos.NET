using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Minos.Protocols;

namespace Minos.Transport;

/// <summary>
/// Checks a caller's state and hands it, with a writer for its JSON value, to a protocol's
/// <see cref="IDecisionProtocol.WriteRequest{TArg}"/>, which writes the request body around it. The state is written
/// straight into the protocol's pooled body; no <see cref="SystemOneRequest"/> is built.
/// </summary>
internal static class RequestStates
{
    // A guess for states whose size is unknown until they are written.
    private const int UnknownStateSize = 1024;

    // The four whitespace bytes JSON allows between tokens: space, tab, line feed and carriage return.
    private static ReadOnlySpan<byte> JsonWhitespace => [0x20, 0x09, 0x0A, 0x0D];

    /// <summary>Writes a request with a text state.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The text.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson WriteRequest(this IDecisionProtocol protocol, QuestionSetDefinition definition, string state, string model, ArrayPool<byte> pool)
        => protocol.WriteRequest<string>(definition, state, state.Length, static (writer, _, text) => writer.WriteStringValue(text), model, pool);

    /// <summary>Writes a request with a JSON state, which must be a string, object or array.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    public static RawJson WriteRequest(this IDecisionProtocol protocol, QuestionSetDefinition definition, JsonElement state, string model, ArrayPool<byte> pool)
    {
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        return protocol.WriteRequest<JsonElement>(definition, state, UnknownStateSize, static (writer, _, element) => element.WriteTo(writer), model, pool);
    }

    /// <summary>Writes a request with a <see cref="DecisionContent"/> state: text as a string, JSON as its value.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state, which must be initialized.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson WriteRequest(this IDecisionProtocol protocol, QuestionSetDefinition definition, DecisionContent state, string model, ArrayPool<byte> pool)
    {
        if (state.TryGetString(out var text))
        {
            return protocol.WriteRequest(definition, text, model, pool);
        }

        state.TryGetJson(out var json);
        return protocol.WriteRequest(definition, json, model, pool);
    }

    /// <summary>Writes a request with a UTF-8 JSON state, which must be one string, object or array.</summary>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="utf8JsonState">The state.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="utf8JsonState"/> is not exactly one well-formed JSON string, object or array.
    /// </exception>
    public static RawJson WriteUtf8Request(
        this IDecisionProtocol protocol, QuestionSetDefinition definition, ReadOnlySpan<byte> utf8JsonState, string model, ArrayPool<byte> pool)
    {
        TypedEvaluation.EnsureStateJson(utf8JsonState, nameof(utf8JsonState));
        return protocol.WriteRequest<ReadOnlySpan<byte>>(
            definition,
            utf8JsonState,
            utf8JsonState.Length,
            static (writer, _, utf8) => writer.WriteRawValue(utf8, skipInputValidation: false),
            model,
            pool);
    }

    /// <summary>Writes a request with a typed state, serialized through its source-generated metadata.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="protocol">The protocol that writes the body.</param>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state.</param>
    /// <param name="stateTypeInfo">The metadata <paramref name="state"/> is serialized with.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    public static RawJson WriteRequest<TState>(
        this IDecisionProtocol protocol, QuestionSetDefinition definition, TState state, JsonTypeInfo<TState> stateTypeInfo, string model, ArrayPool<byte> pool)
        => protocol.WriteRequest<(TState State, JsonTypeInfo<TState> TypeInfo)>(
            definition,
            (state, stateTypeInfo),
            UnknownStateSize,
            static (writer, body, typed) =>
            {
                writer.Flush();
                var stateStart = body.Length;
                JsonSerializer.Serialize(writer, typed.State, typed.TypeInfo);
                writer.Flush();
                EnsureWrittenStateKind(body.Span[stateStart..]);
            },
            model,
            pool);

    // Checks the kind of a state a JsonTypeInfo wrote. The writer is not indented, but a converter may write a raw value
    // with leading whitespace, as the default path accepts, so JSON whitespace is skipped before the first byte is read.
    private static void EnsureWrittenStateKind(ReadOnlySpan<byte> written)
    {
        var start = written.IndexOfAnyExcept(JsonWhitespace);
        var first = start < 0 ? (byte)0 : written[start];
        if (first is not ((byte)'"' or (byte)'{' or (byte)'['))
        {
            throw TypedEvaluation.InvalidStateKind("state");
        }
    }
}
