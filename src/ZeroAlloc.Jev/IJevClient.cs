using ZeroAlloc.Results;

namespace ZeroAlloc.Jev;

/// <summary>Calls TypeSafe's Jev System One API. Implemented by <see cref="JevClient"/>; mock it in tests.</summary>
public interface IJevClient
{
    /// <summary>Asks Jev the request's questions about its state.</summary>
    /// <param name="request">The state, the questions and the model.</param>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="JevError"/> that prevented them.</returns>
    ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct = default);

    /// <summary>Lists the models and aliases available to the account. TypeSafe's API only.</summary>
    /// <param name="ct">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The models, or the <see cref="JevError"/> that prevented them; <see cref="JevErrorKind.Unsupported"/> on OpenRouter.</returns>
    ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default);
}
