using ZeroAlloc.Results;

namespace Minos.Transport;

/// <summary>Sends one attempt of a pre-written request body and returns the raw response body; the caller owns both.</summary>
/// <param name="body">The request body. The caller disposes it once the returned task completes.</param>
/// <param name="retryCount">The attempt's retry number, or <see langword="null"/> on the first attempt.</param>
/// <param name="cancellationToken">Cancels the attempt.</param>
/// <returns>The response body, which the caller owns and must dispose, or the mapped error.</returns>
internal delegate ValueTask<Result<RawJson, DecisionError>> RawSend(RawJson body, int? retryCount, CancellationToken cancellationToken);
