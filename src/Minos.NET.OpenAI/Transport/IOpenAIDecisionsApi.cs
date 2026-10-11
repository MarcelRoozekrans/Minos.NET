using Minos.Transport;
using Minos.Transport.Rest;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace Minos.OpenAI.Transport;

/// <summary>OpenAI's Decisions HTTP API. ZeroAlloc.Rest implements it as <c>OpenAIDecisionsApiClient</c>.</summary>
/// <remarks>
/// The API key travels as a per-call <c>Authorization</c> header so a caller's <see cref="HttpClient"/> is never
/// mutated. Error bodies are read up to 16 KiB. The method sends one attempt: retries are the pipeline's job, through
/// <see cref="RetryingDecisionClient"/>. OpenAI has no retry-count header, so a retry is sent exactly as the first attempt.
/// </remarks>
[ZeroAllocRestClient(MaxErrorBodyBytes = 16384)]
[ErrorMapper(typeof(DecisionErrorMapper))]
internal interface IOpenAIDecisionsApi
{
    /// <summary>
    /// The protocol-neutral attempt: POSTs pre-written UTF-8 JSON to <paramref name="path"/>, relative to the base address,
    /// and returns the raw response body. <see cref="DecisionTransport"/> sends one attempt through it, by the
    /// <see cref="RawSend"/> the client builds, passing <see cref="OpenAIClientSettings.Path"/>. The caller owns both
    /// <see cref="RawJson"/> values.
    /// </summary>
    /// <remarks>
    /// <c>{**path}</c> escapes each segment and keeps the <c>/</c> between them, so <c>decisions</c> is sent as is, under the base address's own path.
    /// </remarks>
    [Post("{**path}")]
    [Serializer(typeof(DecisionRawSerializer))]
    ValueTask<Result<RawJson, DecisionError>> SendAsync(
        [Body] RawJson body, string path, [Header("Authorization")] string? authorization, CancellationToken ct);
}
