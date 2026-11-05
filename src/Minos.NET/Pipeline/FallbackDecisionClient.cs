using Microsoft.Extensions.Logging;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>
/// A stage that asks the inner client first and, when it fails with an error <see cref="FallbackOptions.ShouldFallBack"/>
/// accepts, asks each of <see cref="FallbackOptions.Steps"/> in order until one answers. <see cref="DecisionResponse.Path"/>
/// says which clients were tried, how each call ended, and which one answered.
/// </summary>
/// <remarks>
/// <para>
/// The inner client is step 0, named <see cref="FallbackOptions.PrimaryName"/>, and gets the caller's request, with the
/// stage's deadline set as its <see cref="DecisionRequest.Deadline"/>. When it
/// answers, its response is returned untouched, with no path added. When it fails with an error
/// <see cref="FallbackOptions.ShouldFallBack"/> rejects, that error is returned as is.
/// </para>
/// <para>
/// Each later step gets a new request for the same question set, with the request's state, images and deadline,
/// <see cref="DecisionRequest.RetryAttempt"/> 0 and no <see cref="DecisionRequest.Model"/>, so the step uses its own
/// model. The first step that answers ends the chain: the response is that step's, with its model, id, upstream provider,
/// usage and cache flag, and a path whose routes are <see cref="RouteReason.FellBack"/> to that step and whose calls list
/// each step tried, ending with the answering one. A step whose response already has a path, from a routing stage in its
/// own pipeline, keeps its routes, and its calls follow the failed ones. A step that answers a different question set
/// than it was asked is a failed call of kind <see cref="DecisionErrorKind.InvalidResponse"/>.
/// </para>
/// <para>
/// A step's failure that <see cref="FallbackOptions.ShouldFallBack"/> rejects is returned as is. When every step has
/// failed or been skipped, the error of the last step called is returned; step 0 is always called. An exception a step
/// throws propagates, and the remaining steps are not asked. Disposing the stage disposes the inner client, not the
/// steps' clients.
/// </para>
/// <para>
/// The deadline is the request's <see cref="DecisionRequest.Deadline"/>, or the stage's entry plus
/// <see cref="FallbackOptions.Budget"/>, read from the stage's <see cref="System.TimeProvider"/>, or none. The stage sets it
/// as the <see cref="DecisionRequest.Deadline"/> of every request it sends. Without a deadline, the caller's token alone
/// governs each call. With one, a listed step is skipped, as a <see cref="CallOutcome.NotCalledNoTimeLeft"/> call, when
/// less than its <see cref="FallbackStep.ExpectedLatency"/> is left; and each step's call, step 0's included, is cut at
/// the deadline minus the smallest <see cref="FallbackStep.ExpectedLatency"/> of the steps after it, so that one of them
/// still fits. When no step is left after it, or that moment has already passed when the step starts, the call is cut at
/// the deadline. A cut call fails with a <see cref="DecisionErrorKind.Timeout"/> error of the stage's own, which goes
/// through <see cref="FallbackOptions.ShouldFallBack"/> like any other failure. A deadline already past when a step is
/// called leaves that call to the caller's token. The caller's own cancellation stops the chain and throws an
/// <see cref="OperationCanceledException"/> whose <see cref="OperationCanceledException.CancellationToken"/> is the
/// caller's token.
/// </para>
/// </remarks>
public sealed class FallbackDecisionClient : DelegatingDecisionClient
{
    // The error for a step that answered another question set: a fixed message, with no step name and no state.
    private static readonly DecisionError MismatchError =
        new(DecisionErrorKind.InvalidResponse, "A fallback step answered a different question set than it was asked.");

    // The error for a step cut at its time limit: a fixed message, with no step name, no status code and no state.
    private static readonly DecisionError CutError = new(DecisionErrorKind.Timeout, "A fallback step ran past its time limit.");

    // The longest delay a CancellationTokenSource timer takes: uint.MaxValue - 1 milliseconds, about 49.7 days.
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly FallbackOptions _options;
    private readonly TimeSpan?[] _laterLatency;

    /// <summary>Initializes a new instance of the <see cref="FallbackDecisionClient"/> class.</summary>
    /// <param name="innerClient">The primary client, asked first, as step 0.</param>
    /// <param name="options">The steps and their names; at least one step is required. The stage copies them.</param>
    /// <param name="logger">Logs the stage's events; <see langword="null"/> for none.</param>
    /// <param name="timeProvider">Reads the time for the deadline; <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is invalid.</exception>
    public FallbackDecisionClient(IDecisionClient innerClient, FallbackOptions options, ILogger? logger = null, TimeProvider? timeProvider = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        var copy = options.Clone();
        copy.Validate(nameof(options));
        _options = copy;
        _laterLatency = LaterLatencies(copy.StepList);
        Logger = logger;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    // Read by the time check and the stage's events.
    internal ILogger? Logger { get; }

    internal TimeProvider TimeProvider { get; }

    /// <inheritdoc />
    public override async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The effective deadline, resolved once at entry and stamped on every request the stage sends, so a deadline-aware
        // stage below uses the same clock. The clock is read only when the budget is needed.
        var deadline = request.Deadline ?? (_options.Budget is { } budget ? TimeProvider.GetUtcNow() + budget : null);
        var firstRequest = deadline == request.Deadline ? request : request with { Deadline = deadline };
        // Without a deadline the inner client is called directly, so a call step 0 answers adds no state machine of its own.
        var first = deadline is { } entryDue
            ? await CallAsync(InnerClient, firstRequest, entryDue, _laterLatency[0], cancellationToken).ConfigureAwait(false)
            : await InnerClient.EvaluateAsync(firstRequest, cancellationToken).ConfigureAwait(false);
        var shouldFallBack = _options.ShouldFallBack;
        if (first.IsSuccess || !shouldFallBack(first.Error))
        {
            return first;
        }

        // Past step 0 only: a call step 0 answers allocates nothing here.
        var steps = _options.StepList;
        var calls = new List<DecisionCall>(steps.Count + 1) { FailedCall(_options.PrimaryName, first.Error.Kind) };
        var stepRequest = new DecisionRequest(request.Definition, request.State) { Images = request.Images, Deadline = deadline };
        var last = first;
        for (var i = 0; i < steps.Count; i++)
        {
            // The caller's cancellation stops the chain, even when the step before turned it into a failure result.
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[i];
            if (deadline is { } due && due - TimeProvider.GetUtcNow() < step.ExpectedLatency)
            {
                calls.Add(new DecisionCall(step.Name, null, null, IsCached: false) { Outcome = CallOutcome.NotCalledNoTimeLeft });
                continue;
            }

            var result = deadline is { } stepDue
                ? await CallAsync(step.Client, stepRequest, stepDue, _laterLatency[i + 1], cancellationToken).ConfigureAwait(false)
                : await step.Client.EvaluateAsync(stepRequest, cancellationToken).ConfigureAwait(false);
            DecisionError error;
            if (result.IsSuccess)
            {
                var answer = result.Value;
                if (ReferenceEquals(answer.Definition, request.Definition))
                {
                    var call = new DecisionCall(step.Name, answer.Model, answer.Usage, answer.IsCached);
                    return Result<DecisionResponse, DecisionError>.Success(FallbackPath.Build(answer, step.Name, calls, call));
                }

                // Answers for another set cannot be routed by position. The call keeps the step's model but no usage and
                // no cache flag: the response's usage is the answering step's own, and it equals the sum of the path's calls.
                error = MismatchError;
                result = Result<DecisionResponse, DecisionError>.Failure(error);
                calls.Add(new DecisionCall(step.Name, answer.Model, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = error.Kind });
            }
            else
            {
                error = result.Error;
                calls.Add(FailedCall(step.Name, error.Kind));
            }

            last = result;
            if (!shouldFallBack(error))
            {
                return result;
            }
        }

        return last;
    }

    /// <inheritdoc />
    /// <remarks>For <see cref="FallbackOptions"/> with no key, returns a copy of the stage's options; changing it has no effect.</remarks>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType == typeof(FallbackOptions) ? _options.Clone() : base.GetService(serviceType, serviceKey);
    }

    private static DecisionCall FailedCall(string name, DecisionErrorKind kind)
        => new(name, null, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = kind };

    // The smallest ExpectedLatency of the listed steps from each index on, and null past the last: entry i is what step i's
    // cut leaves room for, with step 0 the inner client, so entry 0 covers every listed step.
    private static TimeSpan?[] LaterLatencies(List<FallbackStep> steps)
    {
        var later = new TimeSpan?[steps.Count + 1];
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var latency = steps[i].ExpectedLatency;
            later[i] = later[i + 1] is { } next && next < latency ? next : latency;
        }

        return later;
    }

    // Calls a step with a token linked to the caller's and cancelled at the step's cut: deadline - laterLatency, the latest
    // moment a later step could still start, or the deadline itself when no later step is left or that moment has passed.
    // A call cut there fails with CutError, whether it throws or returns a failure of its own. The caller's own
    // cancellation, and any other exception, propagate.
    private async ValueTask<Result<DecisionResponse, DecisionError>> CallAsync(
        IDecisionClient client, DecisionRequest stepRequest, DateTimeOffset deadline, TimeSpan? laterLatency, CancellationToken cancellationToken)
    {
        var delay = deadline - TimeProvider.GetUtcNow();
        if (laterLatency is { } room && delay - room > TimeSpan.Zero)
        {
            delay -= room;
        }

        // A deadline already past, or one further off than a timer can run: the caller's token alone governs the call, so
        // step 0 is never handed a token that is already cancelled.
        if (delay <= TimeSpan.Zero || delay > MaxTimerDelay)
        {
            return await client.EvaluateAsync(stepRequest, cancellationToken).ConfigureAwait(false);
        }

        using var atCut = new CancellationTokenSource(delay, TimeProvider);
        using var linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, atCut.Token)
            : null;
        Result<DecisionResponse, DecisionError> result;
        try
        {
            result = await client.EvaluateAsync(stepRequest, linked?.Token ?? atCut.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (atCut.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Which token fired decides, not the exception: the cut's is a step failure, the caller's propagates.
            return Result<DecisionResponse, DecisionError>.Failure(CutError);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested && exception.CancellationToken != cancellationToken)
        {
            // The caller's cancellation, seen through the linked token: name the caller's token, as a call without a deadline does.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }

        // A step that turned the cut into a failure of its own fails with the stage's error; an answer stands.
        return !result.IsSuccess && atCut.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            ? Result<DecisionResponse, DecisionError>.Failure(CutError)
            : result;
    }
}
