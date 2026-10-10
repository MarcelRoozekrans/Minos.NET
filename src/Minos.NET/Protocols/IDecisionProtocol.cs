using System.Buffers;
using System.Text.Json;
using Minos.Transport;

namespace Minos.Protocols;

/// <summary>
/// A provider wire format: how a question set's request is written and how its answers are read. The client owns
/// transport, retries, telemetry and logging; a protocol owns only bytes in and answers out.
/// </summary>
internal interface IDecisionProtocol
{
    /// <summary>Rents and writes a request body for <paramref name="definition"/> and a state.</summary>
    RawJson WriteRequest<TArg>(QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct;

    /// <summary>Reads the answers to <paramref name="definition"/> and builds the result with <paramref name="create"/>.</summary>
    /// <exception cref="JsonException">An answer is missing, has the wrong type, names an unknown option or level, or lacks a required field.</exception>
    TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create);
}
