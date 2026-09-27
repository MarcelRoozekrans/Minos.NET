using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Transport;

/// <summary>The Jev HTTP API. The ZeroAlloc.Rest source generator implements it as <c>JevApiClient</c>.</summary>
/// <remarks>
/// The API key travels as a per-call <c>Authorization</c> header so a caller's <see cref="HttpClient"/> is never
/// mutated. Error bodies are read up to 16 KiB; Jev's are small.
/// </remarks>
[ZeroAllocRestClient(MaxErrorBodyBytes = 16384)]
[ErrorMapper(typeof(JevErrorMapper))]
internal interface IJevApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(
        [Body] SystemOneRequest body,
        [Header("Authorization")] string authorization,
        CancellationToken ct);

    [Get("v1/models")]
    ValueTask<Result<ModelList, JevError>> ListModelsAsync(
        [Header("Authorization")] string authorization,
        CancellationToken ct);
}
