using System.Threading.RateLimiting;
using ZeroAlloc.Results;

namespace Minos.RateLimiting;

/// <summary>A stage that takes one permit from a <see cref="RateLimiter"/> for each call, before the call is sent.</summary>
/// <remarks>
/// <para>
/// A call waits for its permit as the limiter is configured, and holds it until the call completes, so a
/// <see cref="ConcurrencyLimiter"/> counts calls in flight. A call the limiter rejects returns a
/// <see cref="DecisionErrorKind.RateLimited"/> error without reaching the provider: its <see cref="DecisionError.StatusCode"/>
/// is <see langword="null"/>, which tells it apart from a server's 429, and its <see cref="DecisionError.RetryAfter"/> is
/// the limiter's <see cref="MetadataName.RetryAfter"/>, when the limiter reports one.
/// </para>
/// <para>
/// Cancelling a call while it waits for its permit throws <see cref="OperationCanceledException"/>. The limiter belongs to
/// the caller, who may share it across clients; disposing the stage disposes the inner client, never the limiter.
/// </para>
/// </remarks>
public sealed class RateLimitingDecisionClient : DelegatingDecisionClient
{
    /// <summary>The message of the error a rejected call returns.</summary>
    internal const string RejectedMessage = "The client's rate limiter rejected the call before it was sent.";

    private readonly RateLimiter _limiter;

    /// <summary>Initializes a new instance of the <see cref="RateLimitingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client to limit.</param>
    /// <param name="limiter">The limiter each call takes a permit from. The caller owns it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="limiter"/> is <see langword="null"/>.</exception>
    public RateLimitingDecisionClient(IDecisionClient innerClient, RateLimiter limiter)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        _limiter = limiter;
    }

    /// <inheritdoc />
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var acquiring = _limiter.AcquireAsync(1, cancellationToken);
        return acquiring.IsCompletedSuccessfully
            ? Proceed(acquiring.Result, request, cancellationToken)
            : AcquireThenProceedAsync(acquiring, request, cancellationToken);
    }

    private async ValueTask<Result<DecisionResponse, DecisionError>> AcquireThenProceedAsync(
        ValueTask<RateLimitLease> acquiring, DecisionRequest request, CancellationToken cancellationToken)
    {
        var lease = await acquiring.ConfigureAwait(false);
        return await Proceed(lease, request, cancellationToken).ConfigureAwait(false);
    }

    // Sends the call under the lease, and disposes the lease when the call completes; a rejected lease sends nothing.
    private ValueTask<Result<DecisionResponse, DecisionError>> Proceed(RateLimitLease lease, DecisionRequest request, CancellationToken cancellationToken)
    {
        if (!lease.IsAcquired)
        {
            var error = new DecisionError(DecisionErrorKind.RateLimited, RejectedMessage)
            {
                RetryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter) ? retryAfter : null,
            };
            lease.Dispose();
            return new(Result<DecisionResponse, DecisionError>.Failure(error));
        }

        ValueTask<Result<DecisionResponse, DecisionError>> call;
        try
        {
            call = InnerClient.EvaluateAsync(request, cancellationToken);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        if (call.IsCompleted)
        {
            lease.Dispose();
            return call;
        }

        return ReleaseWhenDoneAsync(call, lease);
    }

    private static async ValueTask<Result<DecisionResponse, DecisionError>> ReleaseWhenDoneAsync(
        ValueTask<Result<DecisionResponse, DecisionError>> call, RateLimitLease lease)
    {
        using (lease)
        {
            return await call.ConfigureAwait(false);
        }
    }
}
