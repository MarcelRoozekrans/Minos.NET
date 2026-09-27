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

    /// <summary>Writes a request with a text state.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="state">The text.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    public static RawJson Write<T>(string state, string model, ArrayPool<byte> pool)
        where T : IJevQuestionSet<T>
    {
        var body = Start<T>(model, state.Length, pool, out var writer);
        try
        {
            using (writer)
            {
                writer.WriteStringValue(state);
                Finish<T>(writer, model);
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

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

        var body = Start<T>(model, UnknownStateSize, pool, out var writer);
        try
        {
            using (writer)
            {
                state.WriteTo(writer);
                Finish<T>(writer, model);
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
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

        var body = Start<T>(model, utf8JsonState.Length, pool, out var writer);
        try
        {
            using (writer)
            {
                writer.WriteRawValue(utf8JsonState, skipInputValidation: false);
                Finish<T>(writer, model);
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
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
    {
        var body = Start<T>(model, UnknownStateSize, pool, out var writer);
        try
        {
            using (writer)
            {
                writer.Flush();
                var stateStart = body.Length;
                JsonSerializer.Serialize(writer, state, stateTypeInfo);
                writer.Flush();

                // The writer is not indented, so the value's first byte tells its kind.
                var first = body.Length > stateStart ? body.Span[stateStart] : (byte)0;
                if (first is not ((byte)'"' or (byte)'{' or (byte)'['))
                {
                    throw TypedEvaluation.InvalidStateKind(nameof(state));
                }

                Finish<T>(writer, model);
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

    // Rents the body and writes {"state": — the caller writes the state value next.
    private static RawJson Start<T>(string model, int stateSizeHint, ArrayPool<byte> pool, out Utf8JsonWriter writer)
        where T : IJevQuestionSet<T>
    {
        var body = RawJson.Create(pool, T.QuestionsUtf8.Length + model.Length + stateSizeHint + Overhead);
        try
        {
            writer = new Utf8JsonWriter(body);
            writer.WriteStartObject();
            writer.WritePropertyName("state"u8);
            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

    // Writes ,"model":…,"questions":…} after the state and flushes it all into the body.
    private static void Finish<T>(Utf8JsonWriter writer, string model)
        where T : IJevQuestionSet<T>
    {
        writer.WriteString("model"u8, model);
        writer.WritePropertyName("questions"u8);
        writer.WriteRawValue(T.QuestionsUtf8, skipInputValidation: true);
        writer.WriteEndObject();
        writer.Flush();
    }
}
