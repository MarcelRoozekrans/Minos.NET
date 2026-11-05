using Microsoft.Extensions.Logging;
using Minos.Telemetry;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>
/// A stage that asks the inner client first, then re-asks only the questions whose answers fall below their threshold
/// on a target client, and merges the target's answers into the response. <see cref="DecisionResponse.Path"/> says,
/// per question, which client answered and why.
/// </summary>
/// <remarks>
/// <para>
/// The inner client is the primary. A primary failure is returned as is, and the target is not called. An answer is
/// unsure when its certainty is below the threshold for its key in <see cref="EscalationOptions.Thresholds"/>, or
/// <see cref="EscalationOptions.DefaultThreshold"/>: a Choice's or Score's confidence, or the larger of <c>p</c> and
/// <c>1 - p</c> for a Noul. When no answer is unsure, the target is not called and the path has one call.
/// </para>
/// <para>
/// The unsure questions are sent to the target as one request for a question set of only those questions, each question
/// unchanged, with the request's state, images and deadline, <see cref="DecisionRequest.RetryAttempt"/> 0 and no
/// <see cref="DecisionRequest.Model"/>, so the target uses its own model. The target's answer replaces the primary's
/// even when it is less confident; the route records both certainties. When the target fails, or answers a different
/// question set than it was asked, the primary's answers stand and the unsure questions are routed
/// <see cref="RouteReason.TargetFailed"/>. An exception the target throws propagates, except the cancellation at the
/// deadline described below.
/// </para>
/// <para>
/// The deadline is the request's <see cref="DecisionRequest.Deadline"/>, or the stage's entry plus
/// <see cref="EscalationOptions.Budget"/>, read from the stage's <see cref="System.TimeProvider"/>, or none. The stage sets it
/// as the <see cref="DecisionRequest.Deadline"/> of the requests it sends to the inner client and the target. The primary
/// call is not bounded by it: the caller's token governs that call. When the primary has answered, and less than
/// <see cref="EscalationOptions.ExpectedLatency"/> is left before the deadline, the target is not called and the unsure
/// questions are routed <see cref="RouteReason.NoTimeLeft"/>. A target call still running at the deadline is cancelled and
/// counts as a target failure; the caller's own cancellation throws an <see cref="OperationCanceledException"/> whose
/// <see cref="OperationCanceledException.CancellationToken"/> is the caller's token.
/// </para>
/// <para>
/// The merged response has the primary's <see cref="DecisionResponse.Definition"/>, <see cref="DecisionResponse.Model"/>,
/// <see cref="DecisionResponse.Id"/> and <see cref="DecisionResponse.UpstreamProvider"/>, and its
/// <see cref="DecisionResponse.Usage"/> sums each call's. It is <see cref="DecisionResponse.IsCached"/> only when every
/// call it made was answered from a cache, the primary's included when every question was escalated. Disposing the stage
/// disposes the inner client, not the target.
/// </para>
/// </remarks>
public sealed class EscalatingDecisionClient : DelegatingDecisionClient
{
    private readonly IDecisionClient _target;
    private readonly EscalationOptions _options;
    private readonly Dictionary<string, double> _thresholds;

    // The error kind logged for a target that ran past the deadline, and for one that answered another question set.
    private const string DeadlineErrorKind = "DeadlineExceeded";
    private const string MismatchErrorKind = "DefinitionMismatch";

    // The longest delay a CancellationTokenSource timer takes: uint.MaxValue - 1 milliseconds, about 49.7 days.
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>Initializes a new instance of the <see cref="EscalatingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The primary client, asked first.</param>
    /// <param name="target">The client the unsure questions are re-asked on. The stage does not dispose it.</param>
    /// <param name="options">The thresholds and client names; <see cref="EscalationOptions.TargetName"/> is required. The stage copies them.</param>
    /// <param name="logger">Logs the stage's events; <see langword="null"/> for none.</param>
    /// <param name="timeProvider">Reads the time for the deadline; <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/>, <paramref name="target"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A setting in <paramref name="options"/> is invalid.</exception>
    public EscalatingDecisionClient(IDecisionClient innerClient, IDecisionClient target, EscalationOptions options, ILogger? logger = null, TimeProvider? timeProvider = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        var copy = options.Clone();
        copy.Validate(nameof(options));
        _target = target;
        _options = copy;
        _thresholds = copy.ThresholdMap;
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

        // The effective deadline, resolved once at entry and stamped on the inner and subset requests, so a deadline-aware
        // stage below uses the same clock. The clock is read only when the budget is needed.
        var deadline = request.Deadline ?? (_options.Budget is { } budget ? TimeProvider.GetUtcNow() + budget : null);
        var innerRequest = deadline == request.Deadline ? request : request with { Deadline = deadline };
        var first = await InnerClient.EvaluateAsync(innerRequest, cancellationToken).ConfigureAwait(false);
        if (!first.IsSuccess)
        {
            return first;
        }

        var primary = first.Value;
        var routes = Classify(primary, out var unsureCount);
        var primaryCall = new DecisionCall(_options.PrimaryName, primary.Model, primary.Usage, primary.IsCached);
        if (unsureCount == 0)
        {
            return EscalationMerge.KeepPrimary(primary, routes, [primaryCall]);
        }

        // The time check. The primary call is not bounded: one that overruns finds no time left here.
        var timeLeft = deadline is { } due ? due - TimeProvider.GetUtcNow() : (TimeSpan?)null;
        var targetName = _options.TargetName!;
        if (timeLeft < _options.ExpectedLatency)
        {
            MarkNoTimeLeft(routes);
            if (Logger is { } timeLogger)
            {
                DecisionLog.EscalationSkippedForTime(timeLogger, unsureCount, targetName);
            }

            return EscalationMerge.KeepPrimary(primary, routes, [primaryCall], targetName, DecisionTelemetry.SkippedNoTimeLeft);
        }

        var unsure = Unsure(routes, unsureCount);
        var subset = EscalationMerge.Subset(primary.Definition, unsure);
        var subsetRequest = new DecisionRequest(subset, request.State) { Images = request.Images, Deadline = deadline };
        var second = await EvaluateTargetAsync(subsetRequest, timeLeft, cancellationToken).ConfigureAwait(false);
        if (second is not { IsSuccess: true } success)
        {
            LogTargetFailed(second is { } failed ? DecisionTelemetry.ErrorTypeOf(failed.Error.Kind) : DeadlineErrorKind);
            return KeepAfterFailure(primary, routes, [primaryCall, new DecisionCall(targetName, null, null, IsCached: false)]);
        }

        var target = success.Value;
        var targetCall = new DecisionCall(targetName, target.Model, target.Usage, target.IsCached);

        // Answers for another set cannot be placed by position; the call still counts toward the usage.
        if (!ReferenceEquals(target.Definition, subset))
        {
            LogTargetFailed(MismatchErrorKind);
            return KeepAfterFailure(primary, routes, [primaryCall, targetCall]);
        }

        if (Logger is { } logger)
        {
            DecisionLog.Escalated(logger, unsureCount, targetName);
        }

        return EscalationMerge.Merge(primary, target, unsure, routes, targetName, [primaryCall, targetCall]);

        void LogTargetFailed(string errorKind)
        {
            if (Logger is { } failureLogger)
            {
                DecisionLog.EscalationTargetFailed(failureLogger, targetName, errorKind);
            }
        }

        DecisionResponse KeepAfterFailure(DecisionResponse kept, QuestionRoute[] keptRoutes, DecisionCall[] calls)
            => EscalationMerge.KeepPrimary(kept, keptRoutes, calls, targetName, DecisionTelemetry.SkippedTargetFailed);
    }

    /// <inheritdoc />
    /// <remarks>For <see cref="EscalationOptions"/> with no key, returns a copy of the stage's options; changing it has no effect.</remarks>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType == typeof(EscalationOptions) ? _options.Clone() : base.GetService(serviceType, serviceKey);
    }

    // One route per question, answered by the primary. An unsure question's route starts as TargetFailed, the reason it
    // keeps unless the target answers it.
    private QuestionRoute[] Classify(DecisionResponse primary, out int unsureCount)
    {
        var questions = primary.Definition.QuestionArray;
        var slots = primary.Slots;
        var routes = questions.Length == 0 ? [] : new QuestionRoute[questions.Length];
        var primaryName = _options.PrimaryName;
        unsureCount = 0;
        for (var i = 0; i < questions.Length; i++)
        {
            var question = questions[i];
            var certainty = DecisionPath.Certainty(question.Kind, slots[i]);
            var threshold = _thresholds.Count != 0 && _thresholds.TryGetValue(question.Key, out var own) ? own : _options.DefaultThreshold;
            var isUnsure = certainty < threshold;
            unsureCount += isUnsure ? 1 : 0;
            routes[i] = new QuestionRoute(question.Key, primaryName, isUnsure ? RouteReason.TargetFailed : RouteReason.Confident, certainty, null);
        }

        return routes;
    }

    // Calls the target with a token linked to the caller's and cancelled at the deadline. Returns null when the deadline
    // cancelled the call. The caller's own cancellation, and any other exception, propagate.
    private async ValueTask<Result<DecisionResponse, DecisionError>?> EvaluateTargetAsync(
        DecisionRequest subsetRequest, TimeSpan? timeLeft, CancellationToken cancellationToken)
    {
        // No deadline, or one further off than a timer can run: the caller's token alone governs the call.
        if (timeLeft is not { } delay || delay > MaxTimerDelay)
        {
            return await _target.EvaluateAsync(subsetRequest, cancellationToken).ConfigureAwait(false);
        }

        using var atDeadline = new CancellationTokenSource(delay, TimeProvider);
        using var linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, atDeadline.Token)
            : null;
        try
        {
            return await _target.EvaluateAsync(subsetRequest, linked?.Token ?? atDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (atDeadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Which token fired decides, not the exception: the deadline's is a target failure, the caller's propagates.
            return null;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested && exception.CancellationToken != cancellationToken)
        {
            // The caller's cancellation, seen through the linked token: name the caller's token, as a call without a deadline does.
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
        }
    }

    private static void MarkNoTimeLeft(QuestionRoute[] routes)
    {
        for (var i = 0; i < routes.Length; i++)
        {
            var route = routes[i];
            if (route.Reason != RouteReason.Confident)
            {
                routes[i] = new QuestionRoute(route.Key, route.AnsweredBy, RouteReason.NoTimeLeft, route.PrimaryCertainty, null);
            }
        }
    }

    private static int[] Unsure(QuestionRoute[] routes, int count)
    {
        var unsure = new int[count];
        var next = 0;
        for (var i = 0; i < routes.Length; i++)
        {
            if (routes[i].Reason != RouteReason.Confident)
            {
                unsure[next++] = i;
            }
        }

        return unsure;
    }
}
