using ZeroAlloc.Resilience;
using ZeroAlloc.Rest.Attributes;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Transport;

/// <summary>The Jev HTTP API. ZeroAlloc.Rest implements it as <c>JevApiClient</c>; ZeroAlloc.Resilience wraps it in <c>IJevApiResilienceProxy</c>.</summary>
/// <remarks>
/// The API key travels as a per-call <c>Authorization</c> header so a caller's <see cref="HttpClient"/> is never
/// mutated. Error bodies are read up to 16 KiB. The attribute values of <see cref="RetryAttribute"/> are compile-time
/// defaults only: <see cref="JevClient"/> always supplies a runtime <see cref="RetryPolicy"/> built from
/// <see cref="JevClientOptions"/>. Each retry sends the attempt number as <c>X-TypeSafe-Retry-Count</c>, meant to be
/// absent on the first attempt, as TypeSafe's official SDKs do. ZeroAlloc.Rest 2.2.0 cannot omit it yet, so the
/// first attempt currently sends an empty value instead (ZeroAlloc-Net/ZeroAlloc.Rest#354, closed upstream but not
/// yet in a released package); no change is needed here once a release with that fix ships.
/// </remarks>
[ZeroAllocRestClient(MaxErrorBodyBytes = 16384)]
[ErrorMapper(typeof(JevErrorMapper))]
[Retry(RetryWhen = nameof(IsTransient), DelayHint = nameof(RetryAfter), RetryOnException = nameof(NeverRetry))]
internal interface IJevApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(
        [Body] SystemOneRequest body,
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] [RetryAttempt] int? retryCount,
        CancellationToken ct);

    [Get("v1/models")]
    ValueTask<Result<ModelList, JevError>> ListModelsAsync(
        [Header("Authorization")] string authorization,
        [Header("X-TypeSafe-Retry-Count")] [RetryAttempt] int? retryCount,
        CancellationToken ct);

    /// <summary>Whether a failure is worth another attempt: rate limiting, overload, server errors, 408, network failures and time-outs.</summary>
    static bool IsTransient(JevError error)
        => error.Kind is JevErrorKind.RateLimited or JevErrorKind.Overloaded or JevErrorKind.Server
            or JevErrorKind.Network or JevErrorKind.Timeout
            || error.StatusCode == 408;

    /// <summary>The wait the server asked for, which replaces the backoff for the next attempt.</summary>
    static TimeSpan? RetryAfter(JevError error) => error.RetryAfter;

    /// <summary>
    /// Thrown exceptions are programming errors, never transient: ZeroAlloc.Rest returns every transport failure as a
    /// failed Result.
    /// </summary>
    static bool NeverRetry(Exception exception) => false;
}
