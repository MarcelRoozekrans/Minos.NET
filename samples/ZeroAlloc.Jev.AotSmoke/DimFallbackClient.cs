using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// Implements only <see cref="IJevClient"/>'s two abstract members, so every typed <c>EvaluateAsync</c> call runs
/// through its default interface method: the compatible, allocating fallback that <see cref="JevClient"/> overrides.
/// Proves that fallback path compiles and runs under Native AOT, not just over a real <see cref="JevClient"/>.
/// </summary>
internal sealed class DimFallbackClient(SystemOneResponse response) : IJevClient
{
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct)
        => new(Result<SystemOneResponse, JevError>.Success(response));

    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Not exercised by the smoke app.");
}
