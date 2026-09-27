using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev;

/// <summary>Calls TypeSafe's Jev System One API. Implemented by <see cref="JevClient"/>; mock it in tests.</summary>
/// <remarks>
/// A mock implements <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> and
/// <see cref="ListModelsAsync(CancellationToken)"/>; the typed <c>EvaluateAsync&lt;T&gt;</c> overloads then work
/// through it.
/// </remarks>
public interface IJevClient
{
    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <returns>The answers, or the <see cref="JevError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> without cancellation.</remarks>
    ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request)
        => EvaluateAsync(request, CancellationToken.None);

    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="JevError"/> that prevented them.</returns>
    ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct);

    /// <summary>Lists the models and aliases available to the account. TypeSafe's API only.</summary>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The models, or the <see cref="JevError"/> that prevented them; <see cref="JevErrorKind.Unsupported"/> on OpenRouter.</returns>
    ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a text state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set.</typeparam>
    /// <param name="state">The text to evaluate.</param>
    /// <returns>The typed answers, or the <see cref="JevError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync{T}(string, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    ValueTask<Result<T, JevError>> EvaluateAsync<T>(string state)
        where T : IJevQuestionSet<T>
        => EvaluateAsync<T>(state, CancellationToken.None);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a text state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set.</typeparam>
    /// <param name="state">The text to evaluate.</param>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The typed answers, or the <see cref="JevError"/> that prevented them; answers the question set rejects give
    /// <see cref="JevErrorKind.InvalidResponse"/>.
    /// </returns>
    /// <remarks>
    /// This default implementation is the compatible, allocating path: it calls
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> with <see cref="JevDefaults.Model"/> and reads
    /// the typed answers from the untyped response. <see cref="JevClient"/> overrides it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    ValueTask<Result<T, JevError>> EvaluateAsync<T>(string state, CancellationToken ct)
        where T : IJevQuestionSet<T>
    {
        ArgumentNullException.ThrowIfNull(state);
        return TypedEvaluation.EvaluateAsync<T>(this, JevContent.FromString(state), ct);
    }

    /// <summary>Asks <typeparamref name="T"/>'s questions about a JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set.</typeparam>
    /// <param name="state">The state: a JSON string is sent as text; an object or array as structured content.</param>
    /// <returns>The typed answers, or the <see cref="JevError"/> that prevented them.</returns>
    /// <remarks>Calls <see cref="EvaluateAsync{T}(JsonElement, CancellationToken)"/> without cancellation.</remarks>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    ValueTask<Result<T, JevError>> EvaluateAsync<T>(JsonElement state)
        where T : IJevQuestionSet<T>
        => EvaluateAsync<T>(state, CancellationToken.None);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set.</typeparam>
    /// <param name="state">The state: a JSON string is sent as text; an object or array, such as records or a chat log, as structured content.</param>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The typed answers, or the <see cref="JevError"/> that prevented them; answers the question set rejects give
    /// <see cref="JevErrorKind.InvalidResponse"/>.
    /// </returns>
    /// <remarks>
    /// This default implementation is the compatible, allocating path: it calls
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> with <see cref="JevDefaults.Model"/> and reads
    /// the typed answers from the untyped response. <see cref="JevClient"/> overrides it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not a string, object or array.</exception>
    ValueTask<Result<T, JevError>> EvaluateAsync<T>(JsonElement state, CancellationToken ct)
        where T : IJevQuestionSet<T>
        => TypedEvaluation.EvaluateAsync<T>(this, TypedEvaluation.ToContent(state, nameof(state)), ct);

    /// <summary>Asks <typeparamref name="T"/>'s questions about a UTF-8 JSON state and returns its typed answers.</summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set.</typeparam>
    /// <param name="utf8JsonState">
    /// The state as one UTF-8 JSON value: a string is sent as text; an object or array as structured content. It is
    /// not referenced after the call returns.
    /// </param>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The typed answers, or the <see cref="JevError"/> that prevented them; answers the question set rejects give
    /// <see cref="JevErrorKind.InvalidResponse"/>.
    /// </returns>
    /// <remarks>
    /// This default implementation is the compatible, allocating path: it calls
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> with <see cref="JevDefaults.Model"/> and reads
    /// the typed answers from the untyped response. <see cref="JevClient"/> overrides it.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="utf8JsonState"/> is not exactly one well-formed JSON string, object or array.
    /// </exception>
    ValueTask<Result<T, JevError>> EvaluateUtf8Async<T>(ReadOnlyMemory<byte> utf8JsonState, CancellationToken ct = default)
        where T : IJevQuestionSet<T>
        => TypedEvaluation.EvaluateAsync<T>(this, TypedEvaluation.ToContent(utf8JsonState, nameof(utf8JsonState)), ct);

    /// <summary>
    /// Asks <typeparamref name="T"/>'s questions about a typed state, the one its
    /// <c>[JevQuestions(State = typeof(TState))]</c> names, and returns its typed answers.
    /// </summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set linked to <typeparamref name="TState"/>.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state; it must serialize to a JSON string, object or array.</param>
    /// <param name="stateTypeInfo">The source-generated metadata <paramref name="state"/> is serialized with.</param>
    /// <returns>The typed answers, or the <see cref="JevError"/> that prevented them.</returns>
    /// <remarks>
    /// Calls <see cref="EvaluateAsync{T, TState}(TState, JsonTypeInfo{TState}, CancellationToken)"/> without cancellation.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="stateTypeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    ValueTask<Result<T, JevError>> EvaluateAsync<T, TState>(TState state, JsonTypeInfo<TState> stateTypeInfo)
        where T : IJevQuestionSet<T, TState>
        => EvaluateAsync<T, TState>(state, stateTypeInfo, CancellationToken.None);

    /// <summary>
    /// Asks <typeparamref name="T"/>'s questions about a typed state, the one its
    /// <c>[JevQuestions(State = typeof(TState))]</c> names, and returns its typed answers.
    /// </summary>
    /// <typeparam name="T">A <c>[JevQuestions]</c> question set linked to <typeparamref name="TState"/>.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state; it must serialize to a JSON string, object or array.</param>
    /// <param name="stateTypeInfo">
    /// The source-generated metadata <paramref name="state"/> is serialized with, from your <c>JsonSerializerContext</c>.
    /// </param>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>
    /// The typed answers, or the <see cref="JevError"/> that prevented them; answers the question set rejects give
    /// <see cref="JevErrorKind.InvalidResponse"/>.
    /// </returns>
    /// <remarks>
    /// This default implementation is the compatible, allocating path: it calls
    /// <see cref="EvaluateAsync(SystemOneRequest, CancellationToken)"/> with <see cref="JevDefaults.Model"/> and reads
    /// the typed answers from the untyped response. <see cref="JevClient"/> overrides it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="stateTypeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> does not serialize to a string, object or array.</exception>
    ValueTask<Result<T, JevError>> EvaluateAsync<T, TState>(TState state, JsonTypeInfo<TState> stateTypeInfo, CancellationToken ct)
        where T : IJevQuestionSet<T, TState>
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        ArgumentNullException.ThrowIfNull(stateTypeInfo);
        return TypedEvaluation.EvaluateAsync<T>(this, TypedEvaluation.ToContent(state, stateTypeInfo, nameof(state)), ct);
    }
}
