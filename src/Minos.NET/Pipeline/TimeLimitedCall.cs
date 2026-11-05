using ZeroAlloc.Results;

namespace Minos;

/// <summary>A routing stage's call to a client, bounded by a timer of the stage's own as well as by the caller's token.</summary>
internal static class TimeLimitedCall
{
    // The longest delay a CancellationTokenSource timer takes: uint.MaxValue - 1 milliseconds, about 49.7 days.
    internal static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Calls <paramref name="client"/> with a token linked to <paramref name="cancellationToken"/> and cancelled after
    /// <paramref name="delay"/> on <paramref name="timeProvider"/>.
    /// </summary>
    /// <remarks>
    /// A delay longer than <see cref="MaxTimerDelay"/> arms no timer: the caller's token alone governs the call. When the
    /// timer's cancellation ends the call with an exception, the outcome has no result. The caller's own cancellation
    /// throws an <see cref="OperationCanceledException"/> that names the caller's token; any other exception propagates.
    /// </remarks>
    internal static async ValueTask<TimeLimitedOutcome> EvaluateAsync(
        IDecisionClient client, DecisionRequest request, TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (delay > MaxTimerDelay)
        {
            return new(await client.EvaluateAsync(request, cancellationToken).ConfigureAwait(false), TimedOut: false);
        }

        using var timer = new CancellationTokenSource(delay, timeProvider);
        using var linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token)
            : null;
        try
        {
            var result = await client.EvaluateAsync(request, linked?.Token ?? timer.Token).ConfigureAwait(false);
            return new(result, timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Which token fired decides, not the exception: the timer's ends the call, the caller's propagates.
            return new(null, TimedOut: true);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested && exception.CancellationToken != cancellationToken)
        {
            // The caller's cancellation, seen through the linked token: name the caller's token, as a call without a timer does.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
    }
}

/// <summary>How a <see cref="TimeLimitedCall"/> ended.</summary>
/// <param name="Result">The client's result; <see langword="null"/> when the timer's cancellation ended the call with an exception.</param>
/// <param name="TimedOut">Whether the timer fired, and the caller's token did not, before the call ended.</param>
internal readonly record struct TimeLimitedOutcome(Result<DecisionResponse, DecisionError>? Result, bool TimedOut);
