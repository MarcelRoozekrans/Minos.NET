using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Transport;

/// <summary>
/// Sits between the ZeroAlloc.Resilience proxy and the transport of every <see cref="JevClient"/>, and answers every
/// attempt that starts after the client was disposed with a <see cref="JevErrorKind.Disposed"/> failure instead of
/// sending it, so a disposed client never retries.
/// </summary>
/// <remarks>
/// <para>
/// For an owned <see cref="HttpClient"/>, <see cref="JevErrorMapper"/> already maps an attempt torn down by the
/// disposal to <see cref="JevErrorKind.Disposed"/>, which <see cref="IJevApi.IsTransient"/> never retries. This covers
/// the other way a disposal meets a call in flight: an attempt fails transiently on its own, and the disposal lands
/// before the proxy sends the retry. An owned <see cref="HttpClient"/> would throw <see cref="ObjectDisposedException"/>
/// for that retry; a borrowed one would send it from a disposed client. Neither happens: the retry is never sent.
/// </para>
/// <para>
/// A borrowed <see cref="HttpClient"/> is never disposed by the client, so its attempt already in flight is not torn
/// down and keeps its own result; only the retries after it are refused. Before disposal this returns the inner call's
/// <see cref="ValueTask{TResult}"/> itself, so it adds no allocation and no state machine.
/// </para>
/// </remarks>
/// <param name="inner">The next step toward the transport.</param>
/// <param name="disposed">Reads whether the client has been disposed.</param>
internal sealed class DisposalGuardJevApi(IJevApi inner, Func<bool> disposed) : IJevApi
{
    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(
        SystemOneRequest body, string authorization, int? retryCount, CancellationToken ct)
        => disposed()
            ? new ValueTask<Result<SystemOneResponse, JevError>>(Result<SystemOneResponse, JevError>.Failure(JevErrorMapper.Disposed(exception: null)))
            : inner.EvaluateAsync(body, authorization, retryCount, ct);

    public ValueTask<Result<RawJson, JevError>> EvaluateRawAsync(RawJson body, string authorization, int? retryCount, CancellationToken ct)
        => disposed()
            ? new ValueTask<Result<RawJson, JevError>>(Result<RawJson, JevError>.Failure(JevErrorMapper.Disposed(exception: null)))
            : inner.EvaluateRawAsync(body, authorization, retryCount, ct);

    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(string authorization, int? retryCount, CancellationToken ct)
        => disposed()
            ? new ValueTask<Result<ModelList, JevError>>(Result<ModelList, JevError>.Failure(JevErrorMapper.Disposed(exception: null)))
            : inner.ListModelsAsync(authorization, retryCount, ct);
}
