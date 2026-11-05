using Microsoft.Extensions.Logging;
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
/// <see cref="RouteReason.TargetFailed"/>. An exception the target throws propagates.
/// </para>
/// <para>
/// The merged response has the primary's <see cref="DecisionResponse.Definition"/>, <see cref="DecisionResponse.Model"/>,
/// <see cref="DecisionResponse.Id"/> and <see cref="DecisionResponse.UpstreamProvider"/>, and its
/// <see cref="DecisionResponse.Usage"/> sums each call's. It is <see cref="DecisionResponse.IsCached"/> only when every
/// answer it holds came from a cached response. Disposing the stage disposes the inner client, not the target.
/// </para>
/// </remarks>
public sealed class EscalatingDecisionClient : DelegatingDecisionClient
{
    private readonly IDecisionClient _target;
    private readonly EscalationOptions _options;
    private readonly Dictionary<string, double> _thresholds;

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
        var first = await InnerClient.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
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

        var unsure = Unsure(routes, unsureCount);
        var subset = EscalationMerge.Subset(primary.Definition, unsure);
        var subsetRequest = new DecisionRequest(subset, request.State) { Images = request.Images, Deadline = request.Deadline };
        var targetName = _options.TargetName!;
        var second = await _target.EvaluateAsync(subsetRequest, cancellationToken).ConfigureAwait(false);
        if (!second.IsSuccess)
        {
            return EscalationMerge.KeepPrimary(primary, routes, [primaryCall, new DecisionCall(targetName, null, null, IsCached: false)]);
        }

        var target = second.Value;
        var targetCall = new DecisionCall(targetName, target.Model, target.Usage, target.IsCached);

        // Answers for another set cannot be placed by position; the call still counts toward the usage.
        return ReferenceEquals(target.Definition, subset)
            ? EscalationMerge.Merge(primary, target, unsure, routes, targetName, [primaryCall, targetCall])
            : EscalationMerge.KeepPrimary(primary, routes, [primaryCall, targetCall]);
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
