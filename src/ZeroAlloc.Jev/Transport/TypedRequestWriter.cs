using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ZeroAlloc.Jev.Transport;

/// <summary>
/// Writes a <c>/v1/systemone</c> request body for a <see cref="IJevQuestionSet{TSelf}"/> straight into a pooled
/// <see cref="RawJson"/>: the questions are copied from <see cref="IJevQuestionSet{TSelf}.QuestionsUtf8"/> and no
/// <see cref="SystemOneRequest"/> is built. The body is JSON-equal to the one <see cref="TypedEvaluation.CreateRequest{T}(JevContent, string)"/>
/// serializes to for the same state and model.
/// </summary>
internal static class TypedRequestWriter
{
    // Room for the property names, the model and the braces around them, so a typical body needs no regrowth.
    private const int Overhead = 64;

    // A guess for states whose size is unknown until they are written.
    private const int UnknownStateSize = 1024;

    // The four whitespace bytes JSON allows between tokens: space, tab, line feed and carriage return.
    private static ReadOnlySpan<byte> JsonWhitespace => [0x20, 0x09, 0x0A, 0x0D];

    // Writes the state value at the writer's current position; the helper below owns everything around it.
    private delegate void StateWriter<TArg>(Utf8JsonWriter writer, RawJson body, TArg arg)
        where TArg : allows ref struct;

    /// <summary>Writes a request with a text state.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="state">The text.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson Write<T>(string state, string model, ArrayPool<byte> pool)
        where T : IJevQuestionSet<T>
        => Compose<T, string>(state, state.Length, model, pool, static (writer, _, text) => writer.WriteStringValue(text));

    /// <summary>Writes a request with a JSON state, which must be a string, object or array.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    public static RawJson Write<T>(JsonElement state, string model, ArrayPool<byte> pool)
        where T : IJevQuestionSet<T>
    {
        TypedEvaluation.EnsureStateKind(state.ValueKind, nameof(state));
        return Compose<T, JsonElement>(state, UnknownStateSize, model, pool, static (writer, _, element) => element.WriteTo(writer));
    }

    /// <summary>Writes a request with a UTF-8 JSON state, which must be one string, object or array.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="utf8JsonState">The state.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="utf8JsonState"/> is not exactly one well-formed JSON string, object or array.
    /// </exception>
    public static RawJson WriteUtf8<T>(ReadOnlySpan<byte> utf8JsonState, string model, ArrayPool<byte> pool)
        where T : IJevQuestionSet<T>
    {
        TypedEvaluation.EnsureStateJson(utf8JsonState, nameof(utf8JsonState));
        return Compose<T, ReadOnlySpan<byte>>(
            utf8JsonState,
            utf8JsonState.Length,
            model,
            pool,
            static (writer, _, utf8) => writer.WriteRawValue(utf8, skipInputValidation: false));
    }

    /// <summary>Writes a request with a typed state, serialized through its source-generated metadata.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="stateTypeInfo">The metadata <paramref name="state"/> is serialized with.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    public static RawJson Write<T, TState>(TState state, JsonTypeInfo<TState> stateTypeInfo, string model, ArrayPool<byte> pool)
        where T : IJevQuestionSet<T>
        => Compose<T, (TState State, JsonTypeInfo<TState> TypeInfo)>(
            (state, stateTypeInfo),
            UnknownStateSize,
            model,
            pool,
            static (writer, body, typed) =>
            {
                writer.Flush();
                var stateStart = body.Length;
                JsonSerializer.Serialize(writer, typed.State, typed.TypeInfo);
                writer.Flush();
                EnsureWrittenStateKind(body.Span[stateStart..]);
            });

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

    // Rents the body and writes {"state":, then the state through writeState, then ,"model":…,"questions":…}. The body
    // is disposed if anything throws, so the caller owns it only once it is returned.
    private static RawJson Compose<T, TArg>(TArg arg, int stateSizeHint, string model, ArrayPool<byte> pool, StateWriter<TArg> writeState)
        where T : IJevQuestionSet<T>
        where TArg : allows ref struct
    {
        var body = RawJson.Create(pool, T.QuestionsUtf8.Length + model.Length + stateSizeHint + Overhead);
        try
        {
            using (var writer = new Utf8JsonWriter(body))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("state"u8);
                writeState(writer, body, arg);
                writer.WriteString("model"u8, model);
                writer.WritePropertyName("questions"u8);
                writer.WriteRawValue(T.QuestionsUtf8, skipInputValidation: true);
                writer.WriteEndObject();
                writer.Flush();
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }
}
