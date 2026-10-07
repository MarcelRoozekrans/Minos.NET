using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// Implements only <see cref="IJevClient"/>'s two abstract members, so every typed <c>EvaluateAsync</c> call runs
/// through its default interface method: the compatible, allocating fallback that <see cref="JevClient"/> overrides.
/// Proves that fallback path compiles and runs under Native AOT, not just over a real <see cref="JevClient"/>.
/// It records the request each default method builds, so a check can assert the state reached the wire request.
/// </summary>
internal sealed class DimFallbackClient(SystemOneResponse response) : IJevClient
{
    /// <summary>The request the last call passed to the abstract <c>EvaluateAsync</c>, or <see langword="null"/>.</summary>
    public SystemOneRequest? LastRequest { get; private set; }

    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return new(Result<SystemOneResponse, JevError>.Success(response));
    }

    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not exercised by the smoke app.");
}
