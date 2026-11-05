using Microsoft.Extensions.Logging;
using Minos.Telemetry;
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
/// stage's deadline set as its <see cref="DecisionRequest.Deadline"/>. When it answers, its response is returned
/// untouched, with no path added. When it fails with an error <see cref="FallbackOptions.ShouldFallBack"/> rejects, that
/// error is returned as is. Without a deadline, a call step 0 answers synchronously allocates nothing in the stage; one it
/// completes asynchronously boxes the stage's own state machine, as any awaiting stage does. With a deadline, the stage
/// arms a timer to cut step 0's call, described below.
/// </para>
/// <para>
/// Each later step gets a new request for the same question set, with the request's state, images and deadline,
/// <see cref="DecisionRequest.RetryAttempt"/> 0 and no <see cref="DecisionRequest.Model"/>, so the step uses its own
/// model. The first step that answers ends the chain: the response is that step's, with its model, id, upstream provider
/// and cache flag, its <see cref="DecisionResponse.Usage"/> summed over the path's calls, and a path whose routes are
/// <see cref="RouteReason.FellBack"/> to that step and whose calls list each step tried, ending with the answering one.
/// A step whose response already has a path, from a routing stage in its own pipeline, keeps its routes, and its calls
/// follow the failed ones. A step that answers a different question set than it was asked is a failed call of kind
/// <see cref="DecisionErrorKind.InvalidResponse"/> that keeps the usage it reported, so that usage counts toward the
/// response's.
/// </para>
/// <para>
/// A step's failure that <see cref="FallbackOptions.ShouldFallBack"/> rejects is returned as is, and so is a
/// <see cref="DecisionErrorKind.Disposed"/> failure of any step, step 0 included, whatever
/// <see cref="FallbackOptions.ShouldFallBack"/> returns. When every step has failed or been skipped, the error of the last
/// step called is returned; step 0 is always called. An exception a step throws propagates, and the remaining steps are
/// not asked. Disposing the stage disposes the inner client, not the steps' clients.
/// </para>
/// <para>
/// The deadline is the request's <see cref="DecisionRequest.Deadline"/>, or the stage's entry plus
/// <see cref="FallbackOptions.Budget"/>, read from the stage's <see cref="System.TimeProvider"/>, or none. The stage sets it
/// as the <see cref="DecisionRequest.Deadline"/> of every request it sends. Without a deadline, the caller's token alone
/// governs each call. With one, a listed step is skipped, as a <see cref="CallStatus.NotCalledNoTimeLeft"/> call, when
/// less than its <see cref="FallbackStep.ExpectedLatency"/> is left, or the deadline has passed; and each step's call,
/// step 0's included, is cut at the deadline minus the smallest <see cref="FallbackStep.ExpectedLatency"/> of the steps
/// after it, so that one of them still fits. When no step is left after it, or that moment has already passed when the
/// step starts, the call is cut at the deadline. After a cut, the time left for the next step is measured from the
/// moment the cut was due, so the room it reserved is not lost to the time the cut takes. A cut call fails with a
/// <see cref="DecisionErrorKind.Timeout"/> error of the stage's own, which goes through
/// <see cref="FallbackOptions.ShouldFallBack"/> like any other failure. Step 0 is called even when the deadline has
/// already passed, and that call is then left to the caller's token. The caller's own cancellation stops the chain and throws an
/// <see cref="OperationCanceledException"/> whose <see cref="OperationCanceledException.CancellationToken"/> is the
/// caller's token.
/// </para>
/// <para>
/// A call that went past step 0 is counted once on the <c>minos.fallbacks</c> counter of the <c>Minos</c> meter, as
/// <c>answered</c> with the answering step's name or as <c>exhausted</c>, and, with a logger, logs each step that failed
/// while a later step was left to move on to, each step skipped for time, and the answering step or the exhausted chain.
/// A chain the caller's cancellation stops is neither counted nor logged as exhausted. A response a later step
/// answered carries the <c>minos.fallback.steps</c> and <c>minos.fallback.answered_by</c> values for a telemetry stage
/// outside this one. None of these carry the state, a question or a key: only step names, counts and error kinds.
/// </para>
/// </remarks>
public sealed class FallbackDecisionClient : DelegatingDecisionClient
{
    // The error for a step that answered another question set: a fixed message, with no step name and no state.
    private static readonly DecisionError MismatchError =
        new(DecisionErrorKind.InvalidResponse, "A fallback step answered a different question set than it was asked.");

    // The error for a step cut at its time limit: a fixed message, with no step name, no status code and no state.
    private static readonly DecisionError CutError = new(DecisionErrorKind.Timeout, "A fallback step ran past its time limit.");

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
        // stage below uses the same clock. The clock is read only when there is a deadline.
        var entry = request.Deadline is not null || _options.Budget is not null ? TimeProvider.GetUtcNow() : default;
        var deadline = request.Deadline ?? (_options.Budget is { } budget ? entry + budget : null);
        var firstRequest = deadline == request.Deadline ? request : request with { Deadline = deadline };

        // Without a deadline the inner client is called directly, with no timer: a call step 0 answers synchronously
        // allocates nothing here, which is what the allocation gate measures. One step 0 completes asynchronously boxes
        // this method's state machine. With a deadline, one already past leaves step 0 to the caller's token: it is never
        // handed a cancelled token.
        StepOutcome first;
        if (deadline is { } entryDue && entryDue > entry)
        {
            first = await CallAsync(InnerClient, firstRequest, entryDue, entry, _laterLatency[0], cancellationToken).ConfigureAwait(false);
        }
        else
        {
            first = new(await InnerClient.EvaluateAsync(firstRequest, cancellationToken).ConfigureAwait(false), null);
        }

        if (first.Result.IsSuccess || !MovesOn(first.Result.Error))
        {
            return first.Result;
        }

        // Past step 0 only: a call step 0 answers allocates nothing here.
        var steps = _options.StepList;
        var calls = new List<DecisionCall>(steps.Count + 1) { FailedCall(_options.PrimaryName, first.Result.Error.Kind) };
        var stepRequest = new DecisionRequest(request.Definition, request.State) { Images = request.Images, Deadline = deadline };
        var last = first.Result;
        var lastLogged = LoggedKind(first.Result.Error, first.CutAt is not null);
        LogStepFailed(_options.PrimaryName, lastLogged, cancellationToken);

        // When a call was cut, the time left is measured from the scheduled cut instant, not from the clock read after it:
        // the cut reserved exactly that room for a later step, and a real timer fires at or after its due time.
        var cutAt = first.CutAt;
        for (var i = 0; i < steps.Count; i++)
        {
            // The caller's cancellation stops the chain, even when the step before turned it into a failure result.
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[i];
            Result<DecisionResponse, DecisionError> result;
            if (deadline is { } due)
            {
                // One clock read serves the check and the step's timer. A deadline the clock has already reached leaves
                // no time, whatever the cut reserved.
                var now = TimeProvider.GetUtcNow();
                if (due - (cutAt ?? now) < step.ExpectedLatency || due <= now)
                {
                    calls.Add(new DecisionCall(step.Name, null, null, IsCached: false) { Status = CallStatus.NotCalledNoTimeLeft });
                    if (Logger is { } timeLogger)
                    {
                        DecisionLog.FallbackStepSkippedForTime(timeLogger, step.Name);
                    }

                    continue;
                }

                var outcome = await CallAsync(step.Client, stepRequest, due, now, _laterLatency[i + 1], cancellationToken).ConfigureAwait(false);
                result = outcome.Result;
                cutAt = outcome.CutAt;
            }
            else
            {
                result = await step.Client.EvaluateAsync(stepRequest, cancellationToken).ConfigureAwait(false);
            }

            DecisionError error;
            string logged;
            if (result.IsSuccess)
            {
                var answer = result.Value;
                if (ReferenceEquals(answer.Definition, request.Definition))
                {
                    if (Logger is { } logger)
                    {
                        DecisionLog.FellBack(logger, step.Name, i + 1);
                    }

                    FallbackTelemetry.Answered(step.Name);
                    var call = new DecisionCall(step.Name, answer.Model, answer.Usage, answer.IsCached);
                    return Result<DecisionResponse, DecisionError>.Success(FallbackPath.Build(answer, step.Name, calls, call, i + 1));
                }

                // Answers for another set cannot be routed by position. The call is a failure, but it keeps the step's
                // model, usage and cache flag: it was made, and a response the chain still builds sums its path's calls.
                error = MismatchError;
                logged = DecisionLog.DefinitionMismatch;
                result = Result<DecisionResponse, DecisionError>.Failure(error);
                calls.Add(new DecisionCall(step.Name, answer.Model, answer.Usage, answer.IsCached) { Status = CallStatus.Failed, ErrorKind = error.Kind });
            }
            else
            {
                error = result.Error;
                logged = LoggedKind(error, cutAt is not null);
                calls.Add(FailedCall(step.Name, error.Kind));
            }

            last = result;
            lastLogged = logged;
            if (!MovesOn(error))
            {
                return Exhausted(result, i + 1, logged, cancellationToken);
            }

            // The last step's failure ends the chain, so only 1019 says so.
            if (i + 1 < steps.Count)
            {
                LogStepFailed(step.Name, logged, cancellationToken);
            }
        }

        return Exhausted(last, steps.Count, lastLogged, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>For <see cref="FallbackOptions"/> with no key, returns a copy of the stage's options; changing it has no effect.</remarks>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType == typeof(FallbackOptions) ? _options.Clone() : base.GetService(serviceType, serviceKey);
    }

    // The error kind the events log: DeadlineExceeded for a call the stage cut, whatever failure the client made of the
    // cancellation, as escalation logs a cut target; the error's own kind otherwise.
    private static string LoggedKind(DecisionError error, bool cut)
        => cut ? DecisionLog.DeadlineExceeded : DecisionTelemetry.ErrorTypeOf(error.Kind);

    // A failure after the caller's cancellation does not move the chain on: the next step's check throws. It logs nothing,
    // so no event says the chain moved on when it did not.
    private void LogStepFailed(string step, string errorKind, CancellationToken cancellationToken)
    {
        if (Logger is { } logger && !cancellationToken.IsCancellationRequested)
        {
            DecisionLog.FallbackStepFailed(logger, step, errorKind);
        }
    }

    // Whether a failure moves the chain on. A Disposed failure never does, whatever ShouldFallBack says: the app is
    // shutting down, as the retry stage treats it.
    private bool MovesOn(DecisionError error)
        => error.Kind != DecisionErrorKind.Disposed && _options.ShouldFallBack(error);

    // The chain went past step 0 and got no answer: every step failed or was skipped, or a step's failure stopped it. A
    // failure a step made of the caller's cancellation is not an exhausted chain: the caller's cancellation throws.
    private Result<DecisionResponse, DecisionError> Exhausted(
        Result<DecisionResponse, DecisionError> result, int steps, string errorKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Logger is { } logger)
        {
            DecisionLog.FallbackExhausted(logger, steps, errorKind);
        }

        FallbackTelemetry.Exhausted();
        return result;
    }

    private static DecisionCall FailedCall(string name, DecisionErrorKind kind)
        => new(name, null, null, IsCached: false) { Status = CallStatus.Failed, ErrorKind = kind };

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
    // moment a later step could still start, or the deadline itself when no later step is left or that moment is not after
    // now. The deadline is after now. A call cut there fails with CutError, whether it throws or returns a failure of its
    // own, and the outcome carries the cut instant; an answer stands. The caller's own cancellation, and any other
    // exception, propagate.
    private async ValueTask<StepOutcome> CallAsync(
        IDecisionClient client, DecisionRequest stepRequest, DateTimeOffset deadline, DateTimeOffset now, TimeSpan? laterLatency, CancellationToken cancellationToken)
    {
        var cut = laterLatency is { } room && deadline - room > now ? deadline - room : deadline;
        var outcome = await TimeLimitedCall.EvaluateAsync(client, stepRequest, cut - now, TimeProvider, cancellationToken).ConfigureAwait(false);
        if (!outcome.TimedOut)
        {
            // Without the timer firing, the call ended with a result of its own.
            return new(outcome.Result.GetValueOrDefault(), null);
        }

        return outcome.Result is { IsSuccess: true } answer ? new(answer, null) : new(Result<DecisionResponse, DecisionError>.Failure(CutError), cut);
    }

    // A step's result, and the instant its call was cut at, when it was.
    private readonly record struct StepOutcome(Result<DecisionResponse, DecisionError> Result, DateTimeOffset? CutAt);
}
