using System.Buffers;
using System.Text.Json;
using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>
/// A provider wire format: how a question set's request is written and how its answers are read. The client owns
/// transport, retries, telemetry and logging; a protocol owns only bytes in and answers out.
/// </summary>
internal interface IDecisionProtocol
{
    /// <summary>Rents and writes a request body for <paramref name="definition"/>, a state and its images.</summary>
    /// <param name="definition">The questions to ask.</param>
    /// <param name="state">The state, which <paramref name="writeState"/> writes.</param>
    /// <param name="stateSizeHint">A guess at the state's written size, for the body's first rent.</param>
    /// <param name="writeState">Writes the state at the writer's position.</param>
    /// <param name="images">The request's images, possibly empty.</param>
    /// <param name="model">The model.</param>
    /// <param name="pool">The pool the body's buffer is rented from.</param>
    /// <returns>The body, which the caller owns and must dispose.</returns>
    RawJson WriteRequest<TArg>(
        QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
        IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct;

    /// <summary>
    /// Adds the protocol's own pre-send failures, such as OpenAI's MIN204 and MIN205, in question order. Adds nothing and
    /// allocates nothing when the request passes.
    /// </summary>
    /// <param name="definition">The questions to check.</param>
    /// <param name="providerName">The provider's name, for the failure messages.</param>
    /// <param name="failures">The failures so far, created on the first one added.</param>
    void CollectUnsupported(QuestionSetDefinition definition, string providerName, ref List<QuestionFailure>? failures);

    /// <summary>Reads the answers to <paramref name="definition"/> and builds the result with <paramref name="create"/>.</summary>
    /// <exception cref="JsonException">An answer is missing, has the wrong type, names an unknown option or level, or lacks a required field.</exception>
    TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create);

    /// <summary>Reads a successful response body into a <see cref="DecisionResponse"/> for <paramref name="definition"/>.</summary>
    /// <returns>The response, or an <see cref="DecisionErrorKind.InvalidResponse"/> error with status 200.</returns>
    Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition);

    /// <summary>Maps an error status and its body to a <see cref="DecisionError"/>.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="body">The error body, up to the transport's limit.</param>
    /// <param name="bodyTruncated">Whether the transport cut the body at its limit.</param>
    /// <param name="contentType">The body's media type, if any.</param>
    /// <param name="retryAfter">The wait the response asked for, already parsed from its headers by the transport.</param>
    DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter);
}
