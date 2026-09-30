using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev;

/// <summary>
/// Shared plumbing for typed evaluation: turning a caller's state into <see cref="JevContent"/>, building the request
/// for a <see cref="IJevQuestionSet{TSelf}"/>, and reading typed answers, mapping a rejected response to
/// <see cref="JevErrorKind.InvalidResponse"/>.
/// </summary>
internal static class TypedEvaluation
{
    /// <summary>Converts a state to content, rejecting JSON values Jev does not accept.</summary>
    /// <param name="state">The state: a string, object or array.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <returns>The content, detached from <paramref name="state"/>'s document.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    public static JevContent ToContent(JsonElement state, string paramName)
    {
        EnsureStateKind(state.ValueKind, paramName);
        return JevContent.FromJson(state);
    }

    /// <summary>Converts UTF-8 JSON to content, rejecting input that is not a single string, object or array.</summary>
    /// <param name="utf8Json">The UTF-8 JSON state.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <returns>The content; it does not reference <paramref name="utf8Json"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="utf8Json"/> is not a single JSON string, object or array.</exception>
    public static JevContent ToContent(ReadOnlyMemory<byte> utf8Json, string paramName)
        => JevContent.FromUtf8Json(utf8Json.Span, paramName);

    /// <summary>Serializes a typed state to content through its source-generated metadata.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="stateTypeInfo">The metadata <paramref name="state"/> is serialized with.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <returns>The content.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    public static JevContent ToContent<TState>(TState state, JsonTypeInfo<TState> stateTypeInfo, string paramName)
        => JevContent.FromValue(state, stateTypeInfo, paramName);

    /// <summary>Checks that a state's JSON kind is one Jev accepts: a string, object or array.</summary>
    /// <param name="kind">The state's kind.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is not a string, object or array.</exception>
    public static void EnsureStateKind(JsonValueKind kind, string paramName)
    {
        if (kind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
        {
            throw InvalidStateKind(paramName);
        }
    }

    /// <summary>
    /// Checks that <paramref name="utf8Json"/> is exactly one complete JSON string, object or array. Does not allocate.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 JSON state.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    /// <exception cref="ArgumentException">
    /// The input is empty, malformed, truncated, holds more than one value, or is not a string, object or array.
    /// </exception>
    public static void EnsureStateJson(ReadOnlySpan<byte> utf8Json, string paramName)
    {
        JevContent.EnsureSingleJsonValue(utf8Json, paramName);

        var reader = new Utf8JsonReader(utf8Json);
        reader.Read();
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.StartObject or JsonTokenType.StartArray))
        {
            throw InvalidStateKind(paramName);
        }
    }

    /// <summary>The exception for a state that is not a JSON string, object or array.</summary>
    /// <param name="paramName">The caller's parameter name.</param>
    /// <returns>The exception.</returns>
    public static ArgumentException InvalidStateKind(string paramName)
        => new("The state must be a JSON string, object or array.", paramName);

    /// <summary>Builds the request that asks <typeparamref name="T"/>'s questions about <paramref name="state"/>.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="model">The model.</param>
    /// <returns>The request.</returns>
    public static SystemOneRequest CreateRequest<T>(JevContent state, string model)
        where T : IJevQuestionSet<T>
        => CreateRequest(T.QuestionsUtf8, state, model, typeof(T).Name);

    /// <summary>Builds the request that asks the given questions about <paramref name="state"/>.</summary>
    /// <param name="questionsUtf8">The <c>questions</c> object.</param>
    /// <param name="state">The state.</param>
    /// <param name="model">The model.</param>
    /// <param name="setName">The question set's name, for the exception.</param>
    /// <returns>The request.</returns>
    public static SystemOneRequest CreateRequest(ReadOnlySpan<byte> questionsUtf8, JevContent state, string model, string setName)
        => new()
        {
            State = state,
            Model = model,
            Questions = JsonSerializer.Deserialize(questionsUtf8, JevJsonContext.Default.IReadOnlyDictionaryStringJevQuestion)
                ?? throw new InvalidOperationException(setName + ".QuestionsUtf8 is JSON null."),
        };

    /// <summary>
    /// The default-interface-method path: sends <typeparamref name="T"/>'s questions through
    /// <see cref="IJevClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/> with <see cref="JevDefaults.Model"/>
    /// and reads the typed answers from the untyped response. Compatible with any <see cref="IJevClient"/>; allocates.
    /// </summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="state">The state.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The typed answers, or the <see cref="JevError"/> that prevented them.</returns>
    public static ValueTask<Result<T, JevError>> EvaluateAsync<T>(IJevClient client, JevContent state, CancellationToken ct)
        where T : IJevQuestionSet<T>
        => EvaluateCoreAsync(client, CreateRequest<T>(state, JevDefaults.Model), GeneratedAnswerParser<T>.Instance, ct);

    /// <summary>
    /// The default-interface-method path for a built set: sends its questions through
    /// <see cref="IJevClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/> with <see cref="JevDefaults.Model"/>
    /// and reads its answers from the untyped response. Compatible with any <see cref="IJevClient"/>; allocates.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="set">The question set.</param>
    /// <param name="state">The state.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The answers, or the <see cref="JevError"/> that prevented them.</returns>
    public static ValueTask<Result<JevAnswers, JevError>> EvaluateAsync(IJevClient client, JevQuestionSet set, JevContent state, CancellationToken ct)
        => EvaluateCoreAsync(client, CreateRequest(set.QuestionsUtf8, state, JevDefaults.Model, nameof(JevQuestionSet)), set.Parser, ct);

    /// <summary>Reads typed answers from an untyped response by re-serializing its answers.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="response">The response.</param>
    /// <returns>The typed answers, or an <see cref="JevErrorKind.InvalidResponse"/> error.</returns>
    public static Result<T, JevError> FromResponse<T>(SystemOneResponse response)
        where T : IJevQuestionSet<T>
        => FromResponse(response, GeneratedAnswerParser<T>.Instance);

    /// <summary>Reads typed answers from an untyped response by re-serializing its answers.</summary>
    /// <typeparam name="TResult">The typed answers.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="parse">The question set's parser.</param>
    /// <returns>The typed answers, or an <see cref="JevErrorKind.InvalidResponse"/> error.</returns>
    public static Result<TResult, JevError> FromResponse<TResult>(SystemOneResponse response, AnswerParser<TResult> parse)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(writer, response.Answers, JevJsonContext.Default.IReadOnlyDictionaryStringJevAnswer);
        }

        return ParseAnswersObject(buffer.WrittenSpan, parse);
    }

    /// <summary>Parses the typed answers from an <c>answers</c> JSON object.</summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="answersJson">The UTF-8 <c>answers</c> object.</param>
    /// <param name="statusCode">The HTTP status code to report on a failure, when one is known.</param>
    /// <returns>The typed answers, or an <see cref="JevErrorKind.InvalidResponse"/> error carrying the <see cref="JsonException"/>.</returns>
    public static Result<T, JevError> ParseAnswersObject<T>(ReadOnlySpan<byte> answersJson, int? statusCode = null)
        where T : IJevQuestionSet<T>
        => ParseAnswersObject(answersJson, GeneratedAnswerParser<T>.Instance, statusCode);

    /// <summary>Parses the typed answers from an <c>answers</c> JSON object.</summary>
    /// <typeparam name="TResult">The typed answers.</typeparam>
    /// <param name="answersJson">The UTF-8 <c>answers</c> object.</param>
    /// <param name="parse">The question set's parser.</param>
    /// <param name="statusCode">The HTTP status code to report on a failure, when one is known.</param>
    /// <returns>The typed answers, or an <see cref="JevErrorKind.InvalidResponse"/> error carrying the <see cref="JsonException"/>.</returns>
    public static Result<TResult, JevError> ParseAnswersObject<TResult>(
        ReadOnlySpan<byte> answersJson, AnswerParser<TResult> parse, int? statusCode = null)
    {
        var reader = new Utf8JsonReader(SkipUtf8Bom(answersJson));
        try
        {
            reader.Read();
            return Result<TResult, JevError>.Success(parse(ref reader));
        }
        catch (JsonException exception)
        {
            return Result<TResult, JevError>.Failure(Rejected(exception, statusCode));
        }
    }

    /// <summary>
    /// Parses the typed answers from a complete <c>/v1/systemone</c> response body: finds the top-level
    /// <c>answers</c> property and runs <typeparamref name="T"/>'s parser on it. The rest of the body is read to its
    /// end, so malformed or truncated JSON anywhere is reported. Does not allocate on success.
    /// </summary>
    /// <typeparam name="T">The question set.</typeparam>
    /// <param name="responseJson">The UTF-8 response body.</param>
    /// <param name="statusCode">The HTTP status code to report on a failure, when one is known.</param>
    /// <returns>
    /// The typed answers, or an <see cref="JevErrorKind.InvalidResponse"/> error when the body is not an object, has no
    /// or several <c>answers</c> properties, or the answers are rejected; a <see cref="JsonException"/> is kept as
    /// <see cref="JevError.Exception"/>.
    /// </returns>
    public static Result<T, JevError> ParseResponse<T>(ReadOnlySpan<byte> responseJson, int? statusCode = null)
        where T : IJevQuestionSet<T>
        => ParseResponse(responseJson, GeneratedAnswerParser<T>.Instance, statusCode);

    /// <summary>
    /// Parses the typed answers from a complete <c>/v1/systemone</c> response body: finds the top-level
    /// <c>answers</c> property and runs <paramref name="parse"/> on it. The rest of the body is read to its
    /// end, so malformed or truncated JSON anywhere is reported. Does not allocate on success.
    /// </summary>
    /// <typeparam name="TResult">The typed answers.</typeparam>
    /// <param name="responseJson">The UTF-8 response body.</param>
    /// <param name="parse">The question set's parser.</param>
    /// <param name="statusCode">The HTTP status code to report on a failure, when one is known.</param>
    /// <returns>
    /// The typed answers, or an <see cref="JevErrorKind.InvalidResponse"/> error when the body is not an object, has no
    /// or several <c>answers</c> properties, or the answers are rejected; a <see cref="JsonException"/> is kept as
    /// <see cref="JevError.Exception"/>.
    /// </returns>
    public static Result<TResult, JevError> ParseResponse<TResult>(
        ReadOnlySpan<byte> responseJson, AnswerParser<TResult> parse, int? statusCode = null)
    {
        var reader = new Utf8JsonReader(SkipUtf8Bom(responseJson));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Result<TResult, JevError>.Failure(Invalid("The response body is not a JSON object.", statusCode));
            }

            var found = false;
            TResult? answers = default;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isAnswers = reader.ValueTextEquals("answers"u8);
                reader.Read();
                if (!isAnswers)
                {
                    reader.Skip();
                    continue;
                }

                if (found)
                {
                    return Result<TResult, JevError>.Failure(Invalid("The response has more than one answers property.", statusCode));
                }

                if (reader.TokenType == JsonTokenType.Null)
                {
                    return Result<TResult, JevError>.Failure(Invalid("The response's answers are null.", statusCode));
                }

                answers = parse(ref reader);
                found = true;
            }

            // Reads past the root object, so trailing content after it is reported.
            reader.Read();

            return found
                ? Result<TResult, JevError>.Success(answers!)
                : Result<TResult, JevError>.Failure(Invalid("The response has no answers.", statusCode));
        }
        catch (JsonException exception)
        {
            return Result<TResult, JevError>.Failure(Rejected(exception, statusCode));
        }
    }

    private static async ValueTask<Result<TResult, JevError>> EvaluateCoreAsync<TResult>(
        IJevClient client, SystemOneRequest request, AnswerParser<TResult> parse, CancellationToken ct)
    {
        var result = await client.EvaluateAsync(request, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? FromResponse(result.Value, parse)
            : Result<TResult, JevError>.Failure(result.Error);
    }

    // Utf8JsonReader treats a leading UTF-8 BOM as an invalid start of a value, while the untyped path's
    // stream-based deserializer (System.Text.Json's Deserialize(Stream)/DeserializeAsync(Stream)) skips one. A
    // response body read straight into these reader-based paths must match that tolerance.
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> SkipUtf8Bom(ReadOnlySpan<byte> json)
        => json.StartsWith(Utf8Bom) ? json[Utf8Bom.Length..] : json;

    private static JevError Rejected(JsonException exception, int? statusCode)
        => new(JevErrorKind.InvalidResponse, "The response could not be read as the question set's answers: " + exception.Message, statusCode, exception: exception);

    private static JevError Invalid(string message, int? statusCode) => new(JevErrorKind.InvalidResponse, message, statusCode);
}
