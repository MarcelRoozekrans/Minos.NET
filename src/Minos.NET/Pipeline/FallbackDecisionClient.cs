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
/// The inner client is step 0, named <see cref="FallbackOptions.PrimaryName"/>, and gets the caller's request. When it
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
/// failed, the last step's error is returned. An exception a step throws propagates, and the remaining steps are not
/// asked. Disposing the stage disposes the inner client, not the steps' clients.
/// </para>
/// </remarks>
public sealed class FallbackDecisionClient : DelegatingDecisionClient
{
    // The error for a step that answered another question set: a fixed message, with no step name and no state.
    private static readonly DecisionError MismatchError =
        new(DecisionErrorKind.InvalidResponse, "A fallback step answered a different question set than it was asked.");

    private readonly FallbackOptions _options;

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
        var shouldFallBack = _options.ShouldFallBack;
        if (first.IsSuccess || !shouldFallBack(first.Error))
        {
            return first;
        }

        // Past step 0 only: a call step 0 answers allocates nothing here.
        var steps = _options.StepList;
        var calls = new List<DecisionCall>(steps.Count + 1) { FailedCall(_options.PrimaryName, first.Error.Kind) };
        var stepRequest = new DecisionRequest(request.Definition, request.State) { Images = request.Images, Deadline = request.Deadline };
        var last = first;
        foreach (var step in steps)
        {
            var result = await step.Client.EvaluateAsync(stepRequest, cancellationToken).ConfigureAwait(false);
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
}
