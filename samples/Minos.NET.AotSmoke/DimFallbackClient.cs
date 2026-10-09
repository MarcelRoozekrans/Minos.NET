using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// Implements only <see cref="IDecisionClient"/>'s two abstract members, so every typed <c>EvaluateAsync</c> call runs
/// through its default interface method: the compatible, allocating fallback that <see cref="DecisionClient"/> overrides.
/// Proves that fallback path compiles and runs under Native AOT, not just over a real <see cref="DecisionClient"/>.
/// It records the request each default method builds, so a check can assert the state reached the wire request.
/// </summary>
internal sealed class DimFallbackClient(SystemOneResponse response) : IDecisionClient
{
    /// <summary>The request the last call passed to the abstract <c>EvaluateAsync</c>, or <see langword="null"/>.</summary>
    public SystemOneRequest? LastRequest { get; private set; }

    public ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(SystemOneRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return new(Result<SystemOneResponse, DecisionError>.Success(response));
    }

    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Not exercised by the smoke app.");
}
